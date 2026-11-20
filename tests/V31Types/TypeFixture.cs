using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): what the type tests share: the published registry (read off the
/// submodules by <see cref="V31Contract.PublishedTypes"/>, failing loudly when they are not checked
/// out), and the small builders for a schema and a format file.
/// </summary>
internal static class TypeFixture
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    /// <summary>The repository's own file at <paramref name="parts"/>; fails when it is missing.</summary>
    public static string RepoFile(params string[] parts)
    {
        var path = Path.Combine([RepoRoot(), .. parts]);
        Assert.True(File.Exists(path), $"{path} is missing; check out the submodules (git submodule update --init).");
        return path;
    }

    /// <summary>
    /// The registry as the submodules publish it: the nine built-ins, the catalogue, the renderers, the
    /// bindings; read by <see cref="V31Contract.PublishedTypes"/>, which the refusal packages share.
    /// </summary>
    public static TypeRegistry Published() => V31Contract.PublishedTypes();

    private static readonly Lazy<TypeRegistry> Shared = new(Published);

    /// <summary>
    /// Parses <paramref name="files"/> against the published registry, asserts the reader accepts them,
    /// and runs <see cref="SourceTypes.Check"/> on the package.
    /// </summary>
    public static TypeCheckResult CheckTypes(Dictionary<string, byte[]> files)
    {
        var parsed = SourceParser.Parse(files, Shared.Value.Names);
        Assert.True(parsed.Package != null, "The reader refused: " + string.Join(" | ", parsed.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        return SourceTypes.Check(parsed.Package!, Shared.Value);
    }

    /// <summary>A schema, as the object a package's JSON gives.</summary>
    public static JsonObject Schema(string json) => JsonNode.Parse(json)!.AsObject();

    /// <summary>A format file's JSON text: the dialect's <c>$schema</c> line, then the members of <paramref name="body"/> (the text between the braces).</summary>
    public static string FormatFile(string body) =>
        "{\n  \"$schema\": \"urn:consultologist:spec:31:dialect\",\n  " + body.Trim() + "\n}\n";
}
