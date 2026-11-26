using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): one expression site of a package and what it is on. <paramref name="Declaration"/>
/// is the <see cref="SourceNode"/>, <see cref="SourceText"/> (a macro, a snippet, a macro folder's item),
/// <see cref="SourceTextFolder"/> or <see cref="SourceDeliverable"/> that holds it; <paramref name="Function"/> the
/// function it is in, if any; <paramref name="PartId"/> the part of an agent's <c>parts.&lt;id&gt;.if</c>.
/// </summary>
public sealed record SiteContext(ExpressionSite Site, object Declaration, SourceFunction? Function, string? PartId);

/// <summary>
/// #1501 (specVersion 31, area 04; D2): the scope each site is read in. Area 05 writes the one that knows
/// <c>node</c>, <c>check</c>, <c>item</c>, <c>pass</c> and <c>param</c>; without one, <see cref="PackageScope"/> is used.
/// </summary>
public interface IExpressionScopes
{
    /// <summary>Who can be read at <paramref name="context"/>'s site.</summary>
    IExpressionScope For(SiteContext context);
}

/// <summary>
/// #1501 (specVersion 31, area 04; D2): what this area knows of a site's scope. Outside a function, <c>input.&lt;id&gt;</c>
/// is a <see cref="ScopeHeadKind.Format"/> head of two segments with the input's <c>format</c>, nullable unless the
/// input's <c>required</c> is <c>true</c> (an id the manifest does not declare is <see cref="ScopeHeadKind.Unknown"/>), and
/// <c>run.&lt;fact&gt;</c> a <see cref="ScopeHeadKind.Value"/> head of two (<c>date</c> a <c>date</c>, the other facts of
/// <see cref="JsonLogicOperations.RunFacts"/> a <c>string</c>; another fact, or <c>run</c> alone, unknown). Every other
/// root, and every root inside a function, is <see cref="ScopeHeadKind.Deferred"/>: area 05 says S04-050 and S04-051.
/// </summary>
public sealed class PackageScope(SourcePackage package, bool inFunction) : IExpressionScope
{
    private static readonly ScopeHead Deferred = new(ScopeHeadKind.Deferred, 0, null, false, null);
    private static readonly ScopeHead Unknown = new(ScopeHeadKind.Unknown, 0, null, false, null);

    private readonly SourcePackage package = package ?? throw new ArgumentNullException(nameof(package));

    /// <inheritdoc/>
    public ScopeHead Head(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (inFunction || segments.Count == 0)
            return Deferred;

        switch (segments[0])
        {
            case "input":
                if (segments.Count < 2)
                    return Unknown;
                var input = package.Manifest.Inputs.FirstOrDefault(i => i.Id == segments[1]);
                return input == null
                    ? Unknown
                    : new ScopeHead(ScopeHeadKind.Format, 2, input.Json["format"], !IsTrue(input.Json["required"]), null);
            case "run":
                if (segments.Count < 2 || !JsonLogicOperations.RunFacts.Contains(segments[1]))
                    return Unknown;
                return new ScopeHead(ScopeHeadKind.Value, 2, JsonValue.Create(segments[1] == "date" ? "date" : "string"), false, null);
            default:
                return Deferred;
        }
    }

    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}

/// <summary>#1501 (specVersion 31, area 04): one site's rule (as written, read-only) and what the checker found of it.</summary>
public sealed record SiteExpression(SiteContext Context, JsonNode? Rule, ExpressionCheck Check);

/// <summary>
/// #1501 (specVersion 31, area 04): every checked site of a package, in the order <see cref="SourceExpressions.Check"/>
/// visits them, and the warnings of part 03 § 11.2 (S03-037, S03-038; D28), ordered as refusals are.
/// </summary>
public sealed record PackageExpressions(IReadOnlyList<SiteExpression> Sites, IReadOnlyList<SourceRefusal> Warnings);

/// <summary>
/// #1501 (specVersion 31, area 04): what <see cref="SourceExpressions.Check"/> made of a package. <see cref="Expressions"/>
/// is null exactly when <see cref="Refusals"/> is not empty; the refusals are said once each and ordered by path
/// (ordinal), then in the order found.
/// </summary>
public sealed record ExpressionCheckResult(IReadOnlyList<SourceRefusal> Refusals, PackageExpressions? Expressions);

/// <summary>
/// #1501 (specVersion 31, area 04): every JSON Logic site of a package source (part 03 § 3; D4, D5), each checked by
/// <see cref="ExpressionChecker.Check"/>: the third step of <see cref="SourceCheck.Run"/>. The sites are those
/// <see cref="SourceScopes"/> bounds, in its order: the nodes of <c>nodes/</c>, <c>checks/</c> and each function, a
/// flow node before its children; then the definitions of the package and of each function; then every deliverable.
/// A condition site whose member is <c>null</c> or absent is skipped; a computed node's <c>expression</c> is always
/// checked (<c>null</c> is the <c>null</c> literal). Never throws on package content.
/// </summary>
public static class SourceExpressions
{
    /// <summary>
    /// Checks every site of <paramref name="package"/> against <paramref name="types"/>' formats (a function's own
    /// <see cref="FormatSet"/> inside it), each read in the scope <paramref name="scopes"/> gives, or
    /// <see cref="PackageScope"/> when none is given. A null package or types is the caller's error.
    /// </summary>
    public static ExpressionCheckResult Check(SourcePackage package, PackageTypes types, IExpressionScopes? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);

        var outside = new PackageScope(package, inFunction: false);
        var inside = new PackageScope(package, inFunction: true);
        var sites = new List<SiteExpression>();
        var refusals = new List<SourceRefusal>();
        var warnings = new List<SourceRefusal>();

        foreach (var (context, rule) in Sites(package))
        {
            var formats = context.Function != null && types.FunctionFormats.TryGetValue(context.Function.Id, out var own)
                ? own
                : types.Formats;
            var scope = scopes?.For(context) ?? (context.Function != null ? inside : outside);
            var check = ExpressionChecker.Check(rule, context.Site, scope, formats);
            sites.Add(new SiteExpression(context, rule, check));
            refusals.AddRange(check.Refusals);
            if (check.Refusals.Count == 0)
                warnings.AddRange(Warnings(context.Site, check.Reads));
        }

        var ordered = Ordered(refusals);
        return new ExpressionCheckResult(ordered, ordered.Count == 0 ? new PackageExpressions(sites, Ordered(warnings)) : null);
    }

    private static List<SourceRefusal> Ordered(List<SourceRefusal> said) =>
        said.Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ToList();

    /// <summary>D28: S03-037 or S03-038 once for each run fact the site's rule reads itself.</summary>
    private static IEnumerable<SourceRefusal> Warnings(ExpressionSite site, IReadOnlyList<string> reads)
    {
        var facts = reads
            .Select(read => read.Split('.'))
            .Where(segments => segments.Length >= 2 && segments[0] == "run" && JsonLogicOperations.RunFacts.Contains(segments[1]))
            .Select(segments => segments[1])
            .Distinct(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            yield return site.Kind == ExpressionSiteKind.Expression
                ? ExpressionSentences.RunDependentValue(site.ComputedId ?? "", $"run.{fact}", site.Path, site.InChecks)
                : ExpressionSentences.RunDependentDecision(site, fact);
        }
    }

    // ---- The sites, in SourceScopes' order -------------------------------------------------------

    private static IEnumerable<(SiteContext Context, JsonNode? Rule)> Sites(SourcePackage package)
    {
        foreach (var node in Walk(package.Nodes.Nodes))
            foreach (var site in NodeSites(node, null))
                yield return site;
        foreach (var node in Walk(package.Checks.Nodes))
            foreach (var site in NodeSites(node, null))
                yield return site;
        foreach (var function in package.Functions.Items)
            foreach (var node in Walk(function.Body.Nodes))
                foreach (var site in NodeSites(node, function))
                    yield return site;

        foreach (var site in DefinitionSites(package.Definitions, null))
            yield return site;
        foreach (var function in package.Functions.Items)
            foreach (var site in DefinitionSites(function.Definitions, function))
                yield return site;

        foreach (var site in DeliverableSites(package.Deliverables))
            yield return site;
    }

    /// <summary>Every node under <paramref name="nodes"/> in tree order, a flow node before its children.</summary>
    private static IEnumerable<SourceNode> Walk(IReadOnlyList<SourceNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var block in Blocks(node))
                foreach (var inner in Walk(block.Nodes))
                    yield return inner;
        }
    }

    /// <summary>The bodies of a flow node, in written order; none for a leaf.</summary>
    private static IEnumerable<SourceBlock> Blocks(SourceNode node)
    {
        switch (node)
        {
            case SourceFor loop:
                yield return loop.Body;
                break;
            case SourceWhile loop:
                yield return loop.Body;
                break;
            case SourceIf branch:
                yield return branch.Then;
                if (branch.Else != null)
                    yield return branch.Else;
                break;
            case SourceCase choice:
                foreach (var branch in choice.Branches)
                    yield return branch.Body;
                if (choice.Else != null)
                    yield return choice.Else;
                break;
        }
    }

    /// <summary>
    /// A node's sites: a leaf's <c>if</c>, a computed leaf's <c>expression</c> and each agent part's <c>if</c> in written
    /// order; an <c>if</c>'s <c>if</c>; a <c>while</c>'s <c>while</c> and <c>until</c>; a <c>for</c>'s <c>until</c>.
    /// </summary>
    private static IEnumerable<(SiteContext, JsonNode?)> NodeSites(SourceNode node, SourceFunction? function)
    {
        var path = node is SourceLeaf ? node.Path : SourceLayout.Join(node.Path, "index.json");
        var suffix = function == null ? "" : $" in function '{function.Id}'";
        // #1505 (area 08; D1): a rule of a node under checks/ (a check, a check loop, a branch there) may read deliverable.*.
        var inChecks = function == null && node.Key.StartsWith("check.", StringComparison.Ordinal);

        switch (node)
        {
            case SourceLeaf leaf:
                var isCheck = leaf.Key.StartsWith("check.", StringComparison.Ordinal);
                if (leaf.Header["if"] is { } condition)
                {
                    yield return Site(isCheck ? ExpressionSiteKind.CheckIf : ExpressionSiteKind.NodeIf,
                        $"{(isCheck ? "Check" : "Node")} '{leaf.Id}'{suffix}", "if", path, leaf, function, condition, inChecks);
                }

                if (leaf.Kind == "computed")
                {
                    var site = new ExpressionSite(ExpressionSiteKind.Expression, $"{(isCheck ? "Check" : "Computed node")} '{leaf.Id}'{suffix}", "expression",
                        path, leaf.Id, leaf.Header["format"], inChecks);
                    yield return (new SiteContext(site, leaf, function, null), leaf.Header["expression"]);
                }

                if (leaf.Kind == "agent" && leaf.Header["parts"] is JsonObject parts)
                {
                    foreach (var (id, part) in parts)
                    {
                        if ((part as JsonObject)?["if"] is { } partIf)
                        {
                            var site = new ExpressionSite(ExpressionSiteKind.PartIf, $"Part '{id}' of node '{leaf.Id}'{suffix}", "if", path, null, null);
                            yield return (new SiteContext(site, leaf, function, id), partIf);
                        }
                    }
                }

                break;
            case SourceIf branch:
                if (branch.Header["if"] is { } test)
                    yield return Site(ExpressionSiteKind.FlowIf, $"If '{node.Id}'{suffix}", "if", path, node, function, test, inChecks);
                break;
            case SourceWhile loop:
                if (loop.Header["while"] is { } whileRule)
                    yield return Site(ExpressionSiteKind.While, $"While '{node.Id}'{suffix}", "while", path, node, function, whileRule);
                if (loop.Header["until"] is { } untilRule)
                    yield return Site(ExpressionSiteKind.Until, $"While '{node.Id}'{suffix}", "until", path, node, function, untilRule);
                break;
            case SourceFor loop:
                if (loop.Header["until"] is { } until)
                    yield return Site(ExpressionSiteKind.Until, $"Loop '{node.Id}'{suffix}", "until", path, node, function, until, inChecks);
                break;
        }
    }

    /// <summary>A single snippet's <c>if</c> (D5); a single macro's, a macro folder's and each of its items' (D4).</summary>
    private static IEnumerable<(SiteContext, JsonNode?)> DefinitionSites(SourceDefinitions definitions, SourceFunction? function)
    {
        var suffix = function == null ? "" : $" in function '{function.Id}'";

        foreach (var entry in definitions.Snippets.Items)
        {
            if (entry is SourceSingleText { Text: var snippet } && snippet.Header["if"] is { } condition)
                yield return Site(ExpressionSiteKind.SnippetIf, $"Snippet '{snippet.Id}'{suffix}", "if", snippet.Path, snippet, function, condition);
        }

        foreach (var entry in definitions.Macros.Items)
        {
            switch (entry)
            {
                case SourceSingleText { Text: var macro }:
                    if (macro.Header["if"] is { } condition)
                        yield return Site(ExpressionSiteKind.MacroIf, $"Macro '{macro.Id}'{suffix}", "if", macro.Path, macro, function, condition);
                    break;
                case SourceTextFolder folder:
                    if (folder.Index["if"] is { } shared)
                    {
                        yield return Site(ExpressionSiteKind.MacroIf, $"Macro '{folder.Id}'{suffix}", "if",
                            SourceLayout.Join(folder.Path, "index.json"), folder, function, shared);
                    }

                    foreach (var item in folder.Items)
                    {
                        if (item.Header["if"] is { } own)
                            yield return Site(ExpressionSiteKind.MacroIf, $"Macro '{folder.Id}.{item.Id}'{suffix}", "if", item.Path, item, function, own);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<(SiteContext, JsonNode?)> DeliverableSites(SourceDeliveryFolder folder)
    {
        foreach (var deliverable in folder.Deliverables)
        {
            if (deliverable.Header["if"] is { } condition)
                yield return Site(ExpressionSiteKind.DeliverableIf, $"Deliverable '{deliverable.Id}'", "if", deliverable.Path, deliverable, null, condition);
        }

        foreach (var inner in folder.Folders)
            foreach (var site in DeliverableSites(inner))
                yield return site;
    }

    private static (SiteContext, JsonNode?) Site(
        ExpressionSiteKind kind, string subject, string member, string path, object declaration, SourceFunction? function, JsonNode rule,
        bool inChecks = false) =>
        (new SiteContext(new ExpressionSite(kind, subject, member, path, null, null, inChecks), declaration, function, null), rule);
}
