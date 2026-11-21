using System.Globalization;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; D1, D2, D4, D18; part 05 §§ 3.2, 4, 8.6; part 07 § 2.5): every body of a package as
/// the dialect checked it (<see cref="SourceTemplates.Check"/>), and what the resolver is told of each: the real
/// <see cref="ITemplateReads"/>. A template (an agent's prompt, a <c>rendered</c> node's or a deliverable's body) reads
/// what its own text reads and what the units it places read, a part's text, a snippet's body, a macro's instruction and
/// its body (part 05 § 3.2: "their reads count as the placing node's reads"); the units are <see cref="Units"/> (D2) and
/// the rooted key of each head written there <see cref="Names"/> (D1). A renderer, a snippet or a macro, as an entry of
/// its own, reads what its text reads as written, wherever it is placed. Never throws on what it is asked.
/// </summary>
public sealed class PackageTemplates : ITemplateReads
{
    /// <summary>The roots a body reads a value through (part 05 § 3.1); <c>part</c>, <c>macro</c>, <c>renderer</c> and <c>for</c> are placements, pipes and positions.</summary>
    private static readonly IReadOnlySet<string> ReadRoots = new HashSet<string>(StringComparer.Ordinal)
    {
        "node", "check", "input", "item", "pass", "param", "run", "profile", "snippet",
    };

    private readonly Dictionary<object, CheckedBody> tops = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, TemplateReads?> said = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceText, (TemplateSyntax Body, TemplateSyntax? Instruction)> definitions = new(ReferenceEqualityComparer.Instance);

    internal PackageTemplates(IReadOnlyList<CheckedBody> bodies, IReadOnlyList<SourceRefusal> warnings, SourcePackage package)
    {
        Bodies = bodies;
        Warnings = warnings;
        foreach (var body in bodies)
            tops.TryAdd(body.Host.Declaration, body);
        foreach (var set in package.Functions.Items.Select(f => f.Definitions).Prepend(package.Definitions))
        {
            foreach (var text in set.Snippets.Items.SelectMany(SourceResolve.Texts))
                definitions.TryAdd(text, (TemplateSyntax.Parse(text.Body), null));
            foreach (var text in set.Macros.Items.SelectMany(SourceResolve.Texts))
                definitions.TryAdd(text, (TemplateSyntax.Parse(text.Body), Instruction(text)));
        }
    }

    /// <summary>The checked bodies, in the order <see cref="SourceTemplates.Check"/> visits them.</summary>
    internal IReadOnlyList<CheckedBody> Bodies { get; }

    /// <summary>The dialect's warnings (S05-060 for each run spot, S05-061), each with its file, ordered by path.</summary>
    public IReadOnlyList<SourceRefusal> Warnings { get; }

    /// <summary>
    /// What the body of <paramref name="declaration"/> reads (D1). For a template: its reads as written, then those of
    /// each unit it places in order (a macro's instruction, then its body and the snippets placed in it, which are
    /// <see cref="TemplateReads.Substituted"/> when nothing sent reads them too); <c>Places</c> the rooted ids of the
    /// macros placed, in order; <c>Renders</c> and <c>RunSpots</c> in order of appearance with the placed snippets, parts
    /// and macro instructions expanded in place, numbered for the entry (D4; a macro's body is substituted into the
    /// reply and holds no run spot, D5); <c>PlacesProfileMacro</c> whether a macro placed reads <c>profile.*</c> in its
    /// body or a snippet placed there. For a renderer, a snippet or a macro: what its own text reads. <c>null</c> for
    /// anything else.
    /// </summary>
    public TemplateReads? Of(object declaration, SourceFunction? function)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!said.TryGetValue(declaration, out var reads))
        {
            reads = tops.TryGetValue(declaration, out var body) ? Template(body)
                : declaration is SourceText text && definitions.TryGetValue(text, out var syntax) ? Definition(syntax.Body, syntax.Instruction)
                : null;
            said[declaration] = reads;
        }

        return reads;
    }

    /// <summary>The units the template of <paramref name="declaration"/> places (D2), in order of appearance, each followed by what it places; <c>[]</c> for anything else.</summary>
    public IReadOnlyList<PlacedUnit> Units(object declaration, SourceFunction? function)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return tops.TryGetValue(declaration, out var body) ? body.Units : [];
    }

    /// <summary>
    /// D1: each head the template of <paramref name="declaration"/> writes (<c>node.&lt;id&gt;</c> or
    /// <c>check.&lt;id&gt;</c>, in its body, its parts' text and the snippet and macro bodies and instructions it places)
    /// to the rooted key it names there, only where the two differ; empty for anything else.
    /// </summary>
    public IReadOnlyDictionary<string, string> Names(object declaration, SourceFunction? function)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!tops.TryGetValue(declaration, out var body))
            return names;
        foreach (var read in Placed(body).SelectMany(b => b.Body.Reads))
        {
            if (read is { Head: { } head, RootedHead: { } rooted } && head != rooted && head.Split('.') is ["node" or "check", _])
                names.TryAdd(head, rooted);
        }

        return names;
    }

    // ---- a template ------------------------------------------------------------------------------

    private static TemplateReads Template(CheckedBody body)
    {
        var sent = new List<string>();
        var substituted = new List<string>();
        foreach (var (checkedBody, isSent) in Placed(body))
            (isSent ? sent : substituted).AddRange(ReadsOf(checkedBody));

        var renders = new List<TemplateRender>();
        var spots = new List<TemplateRunSpot>();
        Walk(body, null, renders, spots, 0);

        var reads = sent.Concat(substituted).Distinct(StringComparer.Ordinal).ToList();
        var unsent = substituted.Except(sent, StringComparer.Ordinal).ToList();
        var places = body.Units.Where(u => u.Instruction is not null).Select(u => u.Site).ToList();
        var profile = body.Units.Any(u => u.MacroBody is { } macro
            && new[] { macro }.Concat(macro.Units.Select(m => m.Body)).SelectMany(b => b.FolderItems.Prepend(b))
                .Any(b => b.Reads.Any(r => r.Written.StartsWith("profile.", StringComparison.Ordinal))));
        return new TemplateReads(reads, places, renders, spots, profile, unsent);
    }

    /// <summary>
    /// <paramref name="body"/> and every body it places, each with whether it is sent to the model: its own text, each
    /// unit's (a part's text, a snippet's body, a macro's instruction), then for a macro its body and the snippets placed
    /// in it, which are substituted into the reply; each followed by the folder items' bodies it reads as
    /// <c>item.content</c> (part 05 § 6.2: rendered where they are read, so their reads are the reader's).
    /// </summary>
    private static IEnumerable<(CheckedBody Body, bool Sent)> Placed(CheckedBody body)
    {
        foreach (var each in WithFolderItems(body, true))
            yield return each;
        foreach (var unit in body.Units)
        {
            foreach (var each in WithFolderItems(unit.Body, true))
                yield return each;
            if (unit.MacroBody is { } macro)
            {
                foreach (var each in WithFolderItems(macro, false))
                    yield return each;
                foreach (var inner in macro.Units)
                {
                    foreach (var each in WithFolderItems(inner.Body, false))
                        yield return each;
                }
            }
        }
    }

    /// <summary><paramref name="body"/>, then the folder items' bodies it reads as <c>item.content</c> (part 05 § 6.2), each <paramref name="sent"/> as it is.</summary>
    private static IEnumerable<(CheckedBody Body, bool Sent)> WithFolderItems(CheckedBody body, bool sent) =>
        body.FolderItems.Select(item => (item, sent)).Prepend((body, sent));

    /// <summary>The reads of <paramref name="body"/> as written, in source order, that read a value: not a <c>for</c> variable's, a position's or a renderer's <c>value</c>.</summary>
    private static IEnumerable<string> ReadsOf(CheckedBody body)
    {
        var loops = body.Syntax.Fors.Select(f => f.Variable).ToHashSet(StringComparer.Ordinal);
        return body.Reads.Select(r => r.Written).Where(w => IsRead(w, loops));
    }

    private static bool IsRead(string written, IReadOnlySet<string> loops)
    {
        var root = written.Split('.')[0];
        return ReadRoots.Contains(root) && !loops.Contains(root);
    }

    /// <summary>
    /// D4: the renders and run spots of <paramref name="body"/> (whose site is <paramref name="self"/>, <c>null</c> for
    /// the template's own) in order of appearance, each unit it places walked where it is placed; renders renumbered for
    /// the entry (a later step's <c>render:&lt;n&gt;</c> with them), spots numbered for the entry with their unit.
    /// Bounded by the units' own bound (<see cref="SourceTemplates.UnitLimit"/>) and the snippet chain's.
    /// </summary>
    private static void Walk(CheckedBody body, string? self, List<TemplateRender> renders, List<TemplateRunSpot> spots, int depth)
    {
        if (depth > 4 * DialectRules.NestingLimit)
            return;
        var direct = body.Units.Where(u => u.Within == self).ToList();
        var numbers = new Dictionary<int, int>();
        int placement = 0, unit = 0, render = 0, spot = 0;
        foreach (var output in body.Syntax.Outputs)
        {
            if (placement < body.Placements.Count && Places(output, body.Placements[placement]))
            {
                var site = SiteOf(body.Placements[placement++]);
                if (unit < direct.Count && direct[unit].Site == site)
                {
                    var placed = direct[unit++];
                    Walk(placed.Body, placed.Site, renders, spots, depth + 1);
                }

                continue;
            }

            foreach (var _ in output.Pipes.Where(p => p.Name.StartsWith(DialectRules.RendererRoot + ".", StringComparison.Ordinal)))
            {
                if (render < body.Renders.Count)
                    Renumbered(body.Renders[render++], renders, numbers);
            }

            var at = string.Create(CultureInfo.InvariantCulture, $"{output.Tag.Line}:{output.Tag.Column}");
            if (spot < body.RunSpots.Count && body.RunSpots[spot].At == at)
                spots.Add(body.RunSpots[spot++] with { Site = spots.Count + 1, Unit = self });
        }

        while (render < body.Renders.Count)
            Renumbered(body.Renders[render++], renders, numbers);
        while (spot < body.RunSpots.Count)
            spots.Add(body.RunSpots[spot++] with { Site = spots.Count + 1, Unit = self });
    }

    private static void Renumbered(TemplateRender render, List<TemplateRender> renders, Dictionary<int, int> numbers)
    {
        var n = renders.Count + 1;
        numbers[render.N] = n;
        var source = render.Source.StartsWith("render:", StringComparison.Ordinal)
            && int.TryParse(render.Source["render:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var before)
            && numbers.TryGetValue(before, out var renumbered)
                ? string.Create(CultureInfo.InvariantCulture, $"render:{renumbered}")
                : render.Source;
        renders.Add(render with { N = n, Source = source });
    }

    /// <summary>Whether <paramref name="output"/> is the tag of <paramref name="placement"/>: its whole expression, on its line.</summary>
    private static bool Places(TemplateOutput output, Placement placement) =>
        output is { Pipes.Count: 0, Paths.Count: 1 } && output.Path == placement.Unit && output.Tag.Line == placement.Line;

    /// <summary>The site a placement places (<c>snippet.tone</c> for <c>snippet.tone.content</c>; a part or a macro as written).</summary>
    private static string SiteOf(Placement placement) =>
        placement.Kind == "snippet" && placement.Unit.EndsWith(".content", StringComparison.Ordinal)
            ? placement.Unit[..^".content".Length]
            : placement.Unit;

    // ---- a definition ----------------------------------------------------------------------------

    /// <summary>A macro's instruction, its lines joined with <c>\n</c> (part 05 § 8.1); <c>null</c> when it is not a list of text.</summary>
    private static TemplateSyntax? Instruction(SourceText text)
    {
        if (text.Header["instruction"] is not System.Text.Json.Nodes.JsonArray lines)
            return null;
        var strings = lines.Select(l => l is null ? null : SourceLayout.Text(l)).ToList();
        return strings.All(l => l is not null) ? TemplateSyntax.Parse(string.Join("\n", strings)) : null;
    }

    /// <summary>
    /// A snippet's or a macro's own text as written, wherever it is placed: the paths it reads (a macro's instruction's
    /// first, its own marker no read, D17), each pipe step into a renderer, and the run facts a snippet writes (a macro's
    /// body has no run spot, D5).
    /// </summary>
    private static TemplateReads Definition(TemplateSyntax body, TemplateSyntax? instruction)
    {
        var reads = (instruction is null ? [] : Written(instruction)).Concat(Written(body)).Distinct(StringComparer.Ordinal).ToList();
        var renders = new List<TemplateRender>();
        foreach (var syntax in instruction is null ? [body] : new[] { instruction, body })
        {
            foreach (var output in syntax.Outputs)
            {
                int? before = null;
                foreach (var pipe in output.Pipes)
                {
                    if (!pipe.Name.StartsWith(DialectRules.RendererRoot + ".", StringComparison.Ordinal))
                        continue;
                    var n = renders.Count + 1;
                    var source = before is { } last ? string.Create(CultureInfo.InvariantCulture, $"render:{last}") : output.Path ?? output.Head;
                    renders.Add(new TemplateRender(n, source, pipe.Name[(DialectRules.RendererRoot.Length + 1)..]));
                    before = n;
                }
            }
        }

        var spots = new List<TemplateRunSpot>();
        if (instruction is null)
        {
            foreach (var output in body.Outputs)
            {
                if (output.Paths.FirstOrDefault(p => p.Split('.') is ["run", var fact, ..] && JsonLogicOperations.RunFacts.Contains(fact)) is { } fact)
                    spots.Add(new TemplateRunSpot(spots.Count + 1, string.Create(CultureInfo.InvariantCulture, $"{output.Tag.Line}:{output.Tag.Column}"), fact));
            }
        }

        return new TemplateReads(reads, [], renders, spots, false);
    }

    /// <summary>The paths <paramref name="syntax"/> reads as written, in source order: each <c>for</c>'s source, each output's and each test's.</summary>
    private static IEnumerable<string> Written(TemplateSyntax syntax)
    {
        var loops = syntax.Fors.Select(f => f.Variable).ToHashSet(StringComparer.Ordinal);
        return syntax.Fors.Select(f => (f.Tag.Start, Paths: (IEnumerable<string>)[f.Source.Trim()]))
            .Concat(syntax.Outputs.Select(o => (o.Tag.Start, Paths: (IEnumerable<string>)o.Paths)))
            .Concat(syntax.Ifs.SelectMany(i => i.Tests).Where(t => t.Path is not null).Select(t => (t.Start, Paths: (IEnumerable<string>)[t.Path!])))
            .OrderBy(p => p.Start)
            .SelectMany(p => p.Paths)
            .Where(p => IsRead(p, loops));
    }
}
