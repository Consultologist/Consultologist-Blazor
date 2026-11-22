using System.Text;
using Consultologist.Api.Tests.V31Source;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501 (specVersion 31, area 04): exports <see cref="LogicVectors.All"/> into the content repository named by
/// <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites the six JSON Logic families' folders under
/// <c>v31/conformance/</c>, one <c>&lt;name&gt;.json</c> per file as <see cref="LogicVectors.Render"/> writes it, then
/// sets each family's <c>cases</c> in <c>v31/conformance/index.json</c> to its file names, sorted, leaving every other
/// byte of that file as it was. A no-op without the variable, as <see cref="RefusalPackagesExport"/> is: it is a
/// generator, not a check.
/// </summary>
[Collection("V31 conformance export")] // #1505: the exporters each rewrite v31/conformance/index.json, so they run one at a time.
public class LogicVectorsExport
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    [Fact]
    public void Export()
    {
        var root = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT_V31");
        if (string.IsNullOrWhiteSpace(root))
            return;

        var conformance = Path.Combine(root, "v31", "conformance");
        var indexPath = Path.Combine(conformance, "index.json");
        var index = File.ReadAllText(indexPath, Utf8NoBom);
        var files = LogicVectors.All();
        foreach (var family in LogicVectors.Families)
        {
            var folder = Path.Combine(conformance, family);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);

            var names = new List<string>();
            foreach (var file in files.Where(f => f.Family == family))
            {
                File.WriteAllText(Path.Combine(folder, file.Name + ".json"), LogicVectors.Render(file), Utf8NoBom);
                names.Add(file.Name + ".json");
            }
            index = RefusalPackagesExport.WithCases(index, family, names.Order(StringComparer.Ordinal).ToList());
        }

        File.WriteAllText(indexPath, index, Utf8NoBom);
    }
}
