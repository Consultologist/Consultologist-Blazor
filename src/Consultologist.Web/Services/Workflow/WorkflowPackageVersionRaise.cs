using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// v27 (#1236, package-format-v27-design.md § 7): the upgrade from 26 to 27 is a
/// version raise — 27 only permits what 26 refused, so nothing is rewritten: the
/// manifest's specVersion moves and every file stays. The editor's upgrade runs
/// it as the newest rung from 26 on; the library's migrations carry a package
/// up to 26 before it. The result is the 26 migration's shape so the editor
/// treats every rung alike.
/// </summary>
public static class WorkflowPackageVersionRaise
{
    public static Migration26Result To(string manifestJson, IReadOnlyDictionary<string, string> files, int to)
    {
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(manifestJson) as JsonObject
                ?? throw new JsonException("The manifest is not a JSON object.");
        }
        catch (JsonException ex)
        {
            return new Migration26Result(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), Array.Empty<string>(), new[] { $"The manifest does not parse: {ex.Message}" });
        }

        var from = manifest["specVersion"]?.GetValue<int>() ?? 0;
        if (from >= to)
        {
            return new Migration26Result(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), Array.Empty<string>(), new[] { $"The package is already at specVersion {from}." });
        }

        manifest["specVersion"] = to;
        return new Migration26Result(
            manifest.ToJsonString(WorkflowPackageManifestJson.WriteOptions),
            new Dictionary<string, string>(files, StringComparer.Ordinal),
            new[] { $"Raised to specVersion {to}; nothing rewritten — {to} permits what {from} refused." },
            Array.Empty<string>());
    }
}
