using System.Text.Json;

namespace Consultologist.PackageFormat;

/// <summary>
/// Resolves a specVersion-5 manifest's data table against the package files:
/// scalars become bindable text, collection directories parse their index.json into
/// declared fields and materialized items (the item file's text is the "content"
/// field). Shared by the validator (which collects the errors) and the store (which
/// runs after validation, when errors are impossible).
/// See external/consultologist-package-format/package-format-v5.md.
/// </summary>
public static class WorkflowDataResolver
{
    public const string IndexFileName = "index.json";

    /// <summary>The item fields v5.0 can source: id and name from the index, content from the item file.</summary>
    public static readonly IReadOnlySet<string> SupportedFields = new HashSet<string>(StringComparer.Ordinal) { "id", "name", "content" };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static WorkflowPackageData Resolve(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        List<string> errors) =>
        Resolve(manifest.Data, "Data", files, errors);

    /// <summary>
    /// v25 (B2 #1001): the package's text content whichever key carries it —
    /// <c>data</c> below 25, <c>snippets</c> at 25 (the same table shape, so
    /// the same resolution; the noun follows the key).
    /// </summary>
    public static WorkflowPackageData ResolveContent(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        List<string> errors) =>
        manifest.Data is not null || manifest.Snippets is null
            ? Resolve(manifest.Data, "Data", files, errors)
            : Resolve(manifest.Snippets, "Snippet", files, errors);

    /// <summary>
    /// v25 (#999): the same resolution over <c>manifest.snippets</c> — the
    /// table is the same shape (id → file, or folder → "dir/" with an
    /// index.json), the noun in every sentence is "Snippet", and a folder
    /// item's <c>when</c> is carried on the item.
    /// </summary>
    public static WorkflowPackageData Resolve(
        IReadOnlyDictionary<string, string>? table,
        string noun,
        IReadOnlyDictionary<string, string> files,
        List<string> errors)
    {
        var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
        var collections = new Dictionary<string, WorkflowDataCollection>(StringComparer.Ordinal);

        foreach (var (id, path) in table ?? new Dictionary<string, string>())
        {
            if (path.EndsWith('/'))
            {
                var collection = ResolveCollection(id, path, files, errors, noun);
                if (collection != null)
                {
                    collections[id] = collection;
                }
            }
            else if (files.TryGetValue(path, out var content))
            {
                scalars[id] = content;
            }
            else
            {
                errors.Add($"{noun} entry '{id}' file '{path}' is missing from the package.");
            }
        }

        return new WorkflowPackageData(scalars, collections);
    }

    private static WorkflowDataCollection? ResolveCollection(
        string id,
        string directory,
        IReadOnlyDictionary<string, string> files,
        List<string> errors,
        string noun = "Data")
    {
        var indexPath = directory + IndexFileName;

        if (!files.TryGetValue(indexPath, out var indexJson))
        {
            errors.Add($"{noun} collection '{id}' is missing '{indexPath}'.");
            return null;
        }

        WorkflowDataIndexFile? index;
        try
        {
            index = JsonSerializer.Deserialize<WorkflowDataIndexFile>(indexJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"{noun} collection '{id}' index.json is not valid JSON: {ex.Message}");
            return null;
        }

        if (index?.Fields is not { Count: > 0 })
        {
            errors.Add($"{noun} collection '{id}' index.json declares no fields.");
            return null;
        }

        if (!index.Fields.Contains("id") || !index.Fields.Contains("name"))
        {
            errors.Add($"{noun} collection '{id}' fields must include 'id' and 'name'.");
        }

        foreach (var field in index.Fields.Where(f => !SupportedFields.Contains(f)))
        {
            errors.Add($"{noun} collection '{id}' declares field '{field}' with no source in specVersion 5.0 (supported: id, name, content).");
        }

        if (index.Items is not { Count: > 0 })
        {
            errors.Add($"{noun} collection '{id}' declares no items.");
            return null;
        }

        var wantsContent = index.Fields.Contains("content");
        var items = new List<WorkflowDataItem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in index.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add($"{noun} collection '{id}' has an item without an id or name.");
                continue;
            }

            if (!seenIds.Add(item.Id))
            {
                errors.Add($"{noun} collection '{id}' has duplicate item id '{item.Id}'.");
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = item.Id,
                ["name"] = item.Name
            };

            if (wantsContent)
            {
                if (string.IsNullOrWhiteSpace(item.File))
                {
                    errors.Add($"{noun} collection '{id}' item '{item.Id}' declares no file (the collection declares a 'content' field).");
                    continue;
                }

                var filePath = directory + item.File;
                if (!files.TryGetValue(filePath, out var content))
                {
                    errors.Add($"{noun} collection '{id}' item '{item.Id}' file '{filePath}' is missing from the package.");
                    continue;
                }

                fields["content"] = content;
            }

            items.Add(new WorkflowDataItem(item.Id, fields, item.When, item.If));
        }

        return new WorkflowDataCollection(index.Fields, items);
    }
}
