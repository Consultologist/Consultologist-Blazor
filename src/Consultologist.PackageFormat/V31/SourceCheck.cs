namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): what <see cref="SourceCheck.Run"/> made of an upload: the
/// reader's result whatever it was; the refusals, the reader's when it has any, else the type
/// rules'; and what the type rules learned, null exactly when there is a refusal.
/// </summary>
public sealed record SourceCheckResult(SourceParseResult Parse, IReadOnlyList<SourceRefusal> Refusals, PackageTypes? Types);

/// <summary>
/// #1500 (specVersion 31, area 03): a package source held to every rule areas 02 and 03 know:
/// <see cref="SourceParser.Parse"/> against the registry's names, then, when it parsed,
/// <see cref="SourceTypes.Check"/>. Never throws on an upload; a null argument is the caller's
/// error (<see cref="ArgumentNullException"/>).
/// </summary>
public static class SourceCheck
{
    /// <summary>Reads <paramref name="upload"/> (path to bytes) and, when the reader accepts it, holds it to the type rules.</summary>
    public static SourceCheckResult Run(IReadOnlyDictionary<string, byte[]> upload, TypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(registry);

        var parse = SourceParser.Parse(upload, registry.Names);
        if (parse.Package == null)
            return new SourceCheckResult(parse, parse.Refusals, null);

        var types = SourceTypes.Check(parse.Package, registry);
        return new SourceCheckResult(parse, types.Refusals, types.Types);
    }
}
