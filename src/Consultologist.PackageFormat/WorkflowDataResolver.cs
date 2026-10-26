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
    /// #1105 (v26 design § 5): the same resolution at 26 — the snippets through
    /// the headers <see cref="WorkflowTextHeaders.Resolve"/> read (the files
    /// given hold bodies), a folder's index the ordered list, an item's label
    /// and gate from its header.
    /// </summary>
    public static WorkflowPackageData ResolveContent(
        WorkflowPackageManifest manifest,
        WorkflowResolvedTexts texts,
        List<string> errors) =>
        manifest.Data is not null || manifest.Snippets is null
            ? Resolve(manifest.Data, "Data", texts.Files, errors)
            : Resolve(manifest.Snippets, "Snippet", texts.Files, errors, texts.Headers);

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
        List<string> errors,
        // #1105: non-null at 26 — the headers by path; a folder's index is then
        // the ordered [{ id, file }] list and an item's label and if are its file's.
        IReadOnlyDictionary<string, WorkflowTextHeader>? headers = null)
    {
        var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
        var collections = new Dictionary<string, WorkflowDataCollection>(StringComparer.Ordinal);

        foreach (var (id, path) in table ?? new Dictionary<string, string>())
        {
            if (path.EndsWith('/'))
            {
                var collection = headers is null
                    ? ResolveCollection(id, path, files, errors, noun)
                    : ResolveCollectionAt26(id, path, files, headers, errors, noun);
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

    /// <summary>#1105: the folder at 26 — the index an ordered list, the item's label and gate from its header (its text already the body).</summary>
    private static WorkflowDataCollection? ResolveCollectionAt26(
        string id,
        string directory,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, WorkflowTextHeader> headers,
        List<string> errors,
        string noun)
    {
        var indexPath = directory + IndexFileName;

        if (!files.TryGetValue(indexPath, out var indexJson))
        {
            errors.Add($"{noun} collection '{id}' is missing '{indexPath}'.");
            return null;
        }

        var index = WorkflowTextHeaders.ReadIndexAt26(indexJson, id, noun, errors);
        if (index is null)
        {
            return null;
        }

        if (index.Count == 0)
        {
            errors.Add($"{noun} collection '{id}' declares no items.");
            return null;
        }

        var items = new List<WorkflowDataItem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (itemId, file) in index)
        {
            if (!seenIds.Add(itemId))
            {
                errors.Add($"{noun} collection '{id}' has duplicate item id '{itemId}'.");
                continue;
            }

            var filePath = directory + file;
            if (!files.TryGetValue(filePath, out var content))
            {
                errors.Add($"{noun} collection '{id}' item '{itemId}' file '{filePath}' is missing from the package.");
                continue;
            }

            // A file with no header was refused by name at the header pass; it has no label here.
            var header = headers.GetValueOrDefault(filePath);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = itemId,
                ["label"] = header?.Label ?? string.Empty,
                ["content"] = content,
            };

            items.Add(new WorkflowDataItem(itemId, fields, null, header?.If, header));
        }

        return new WorkflowDataCollection(new[] { "id", "label", "content" }, items);
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
