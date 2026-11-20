using System.Text;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): exports <see cref="RefusalPackages.All"/> into the content
/// repository named by <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites
/// <c>v31/conformance/refusal-packages/</c> — each case's <c>valid/</c> and <c>invalid/</c>, a
/// <c>package/</c> of the case's files byte for byte and the <c>expect.json</c> the parser's own
/// result gives — then sets the <c>refusal-packages</c> family's <c>cases</c> in
/// <c>v31/conformance/index.json</c>, leaving every other byte of that file as it was. A no-op
/// without the variable, as <see cref="ConformanceFixtureExport"/> is: it is a generator, not a check.
/// </summary>
public class RefusalPackagesExport
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    [Fact]
    public void Export()
    {
        var root = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT_V31");
        if (string.IsNullOrWhiteSpace(root))
            return;

        var family = Path.Combine(root, "v31", "conformance", "refusal-packages");
        if (Directory.Exists(family))
            Directory.Delete(family, recursive: true);

        var registry = V31Contract.PublishedRegistry();
        var cases = new List<string>();
        foreach (var refusalCase in RefusalPackages.All())
        {
            foreach (var (side, files) in new[] { ("valid", refusalCase.Valid), ("invalid", refusalCase.Invalid) })
            {
                var folder = Path.Combine(family, refusalCase.Id, side);
                foreach (var (path, bytes) in files)
                {
                    var file = Path.Combine([folder, "package", .. path.Split('/')]);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllBytes(file, bytes);
                }

                var result = SourceParser.Parse(files, registry);
                File.WriteAllText(Path.Combine(folder, "expect.json"), RefusalPackages.Expect(refusalCase.About, result), Utf8NoBom);
                cases.Add($"{refusalCase.Id}/{side}");
            }
        }

        var index = Path.Combine(root, "v31", "conformance", "index.json");
        File.WriteAllText(index, WithCases(File.ReadAllText(index, Utf8NoBom), cases.Order(StringComparer.Ordinal).ToList()), Utf8NoBom);
    }

    /// <summary>
    /// <paramref name="index"/> with the <c>refusal-packages</c> family's <c>cases</c> array replaced by
    /// <paramref name="cases"/>, one per line, indented as the <c>schemas</c> family's are; nothing
    /// else changes.
    /// </summary>
    internal static string WithCases(string index, IReadOnlyList<string> cases)
    {
        var family = index.IndexOf("\"id\": \"refusal-packages\"", StringComparison.Ordinal);
        Assert.True(family >= 0, "index.json has no refusal-packages family.");
        var member = index.IndexOf("\"cases\": [", family, StringComparison.Ordinal);
        Assert.True(member >= 0, "the refusal-packages family has no cases array.");

        var open = member + "\"cases\": ".Length;
        var close = index.IndexOf(']', open);
        var indent = index[(index.LastIndexOf('\n', member) + 1)..member];
        var array = cases.Count == 0
            ? "[]"
            : "[\n" + string.Join(",\n", cases.Select(c => $"{indent}  \"{c}\"")) + $"\n{indent}]";
        return index[..open] + array + index[(close + 1)..];
    }
}
