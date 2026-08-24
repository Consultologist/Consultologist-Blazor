using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consultologist.PackageFormat;

/// <summary>
/// How a manifest is read, in one place because four sites read one — the store
/// at load, the publish request body, the diagram-for-a-manifest endpoint, and
/// the offline validator script. A rule applied at three of those and forgotten
/// at the fourth is not a rule.
///
/// #416: unmapped members are refused, because the format says so —
/// "a section the version does not have is never a silently ignored field"
/// (package-format-v8.md § 2) — and because the schemas published beside those
/// documents already carry additionalProperties: false (#407). Before this the
/// engine accepted what its own specification called an error, and the publisher
/// then dropped the field on the next editor publish, so an author could watch
/// something survive in git and vanish through the product (#398).
///
/// Deliberately NOT the store's shared options instance, which also reads
/// data-collection index files and the latest-pointer. The pointer is a mutable
/// {"version": …} that a later field could reasonably join; an unknown key there
/// is forward compatibility, not a malformed package.
/// </summary>
public static class WorkflowPackageManifestJson
{
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Read a manifest the way the engine must: the version first, then the
    /// shape. The order is the whole point (#416) and it lives here rather than
    /// at the call site because nothing constructs WorkflowPackageStore outside
    /// dependency injection — a rule inlined there could not be tested, and a
    /// mutation that reversed it passed the entire suite.
    /// </summary>
    public static WorkflowPackageManifest Read(
        string manifestJson,
        string packageRef,
        IReadOnlyList<int> supportedSpecVersions)
    {
        if (!TryReadSpecVersion(manifestJson, out var declaredSpecVersion))
        {
            throw new InvalidOperationException($"Workflow package manifest for {packageRef} is empty or malformed.");
        }

        // Before the shape: a pre-v5 package is made of retired vocabulary, and
        // refusing it as a parse error would replace the sentence that explains
        // it is archived with one about a field nobody asked about.
        if (!supportedSpecVersions.Contains(declaredSpecVersion))
        {
            throw new WorkflowPackageSpecVersionException(packageRef, declaredSpecVersion, supportedSpecVersions);
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowPackageManifest>(manifestJson, ReadOptions)
                ?? throw new InvalidOperationException($"Workflow package manifest for {packageRef} is empty or malformed.");
        }
        catch (JsonException ex)
        {
            // Content, not registry: the package is there and readable, and this
            // engine will not accept what it says.
            throw new WorkflowPackageContentException($"Workflow package {packageRef}: {Describe(ex)}");
        }
    }

    /// <summary>
    /// The declared specVersion, read without imposing the current shape.
    ///
    /// Read first, and separately, so a pre-v5 package still gets the sentence
    /// that explains it: those manifests use retired vocabulary — `sectionSteps`
    /// on general@v2026.07.4 is the one such package in the registry — and a
    /// strict read would refuse them as a parse error naming a field, instead of
    /// saying they are archived and not executable.
    /// </summary>
    public static bool TryReadSpecVersion(string manifestJson, out int specVersion)
    {
        specVersion = 0;

        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Case-insensitively, as every reader of these manifests is.
                if (!property.NameEquals("specVersion")
                    && !string.Equals(property.Name, "specVersion", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out specVersion);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// A JsonException as a sentence an author can act on. The path is the whole
    /// value — "malformed JSON" tells somebody nothing about which of two hundred
    /// lines to look at — and it is used rather than the exception's own message
    /// because that one names .NET types.
    /// </summary>
    public static string Describe(JsonException exception) =>
        string.IsNullOrWhiteSpace(exception.Path)
            ? "The manifest is not valid JSON."
            : $"Manifest property '{exception.Path}' is not part of the package format.";
}
