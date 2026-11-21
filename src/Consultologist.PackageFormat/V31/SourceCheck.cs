namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): what <see cref="SourceCheck.Run"/> made of an upload: the
/// reader's result whatever it was; the refusals, the reader's when it has any, else the type
/// rules', else the expressions'; and what the type rules learned, null exactly when the reader or
/// the type rules refused something.
/// #1501 (specVersion 31, area 04): and every expression site checked, null when an earlier step or an expression
/// is refused (<see cref="Types"/> stays set when only an expression is refused; #1502: it stays set when only the
/// derived step refuses).
/// #1502 (specVersion 31, area 05): and what the resolve step learned, null when the reader, the type rules or the
/// resolve step refused something (it stays set when only an expression is refused); and what the derived step worked out
/// (<see cref="SourceDerived"/>), null exactly when there is a refusal.
/// </summary>
public sealed record SourceCheckResult(
    SourceParseResult Parse,
    IReadOnlyList<SourceRefusal> Refusals,
    PackageTypes? Types,
    PackageExpressions? Expressions,
    Resolution? Resolution = null,
    PackageDerived? Derived = null);

/// <summary>
/// #1500 (specVersion 31, area 03): a package source held to every rule areas 02 to 04 know:
/// <see cref="SourceParser.Parse"/> against the registry's names, then, when it parsed,
/// <see cref="SourceTypes.Check"/>, then, when the type rules gave no refusal (#1502, area 05),
/// <see cref="SourceResolve.Check"/>, then, when it gave none (#1501, area 04), <see cref="SourceExpressions.Check"/>,
/// each site read in the scope the resolution gives, then, when they gave none (#1502, area 05), <see cref="SourceDerived.Check"/>
/// with what the seam says each body reads. Never throws on an upload; a null argument other than the seam is the caller's
/// error (<see cref="ArgumentNullException"/>).
/// </summary>
public static class SourceCheck
{
    /// <summary>
    /// Reads <paramref name="upload"/> (path to bytes) and, when the reader accepts it, holds it to the type rules, then
    /// the resolver's, then its expressions to theirs, then works out its derived values, each body read as
    /// <paramref name="templates"/> says (none: unknown, D1).
    /// </summary>
    public static SourceCheckResult Run(IReadOnlyDictionary<string, byte[]> upload, TypeRegistry registry, ITemplateReads? templates = null)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(registry);

        var parse = SourceParser.Parse(upload, registry.Names);
        if (parse.Package == null)
            return new SourceCheckResult(parse, parse.Refusals, null, null);

        var types = SourceTypes.Check(parse.Package, registry);
        if (types.Types == null)
            return new SourceCheckResult(parse, types.Refusals, null, null);

        var (refusals, resolution) = SourceResolve.Check(parse.Package, types.Types);
        if (resolution == null)
            return new SourceCheckResult(parse, refusals, types.Types, null);

        var expressions = SourceExpressions.Check(parse.Package, types.Types, resolution.Scopes);
        if (expressions.Expressions == null)
            return new SourceCheckResult(parse, expressions.Refusals, types.Types, null, resolution);

        var (derivedRefusals, derived) = SourceDerived.Check(parse.Package, types.Types, resolution, expressions.Expressions, templates);
        return new SourceCheckResult(parse, derivedRefusals, types.Types, expressions.Expressions, resolution, derived);
    }
}
