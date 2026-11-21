using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 3.1): one body at one place. <paramref name="Subject"/> is the template as
/// sentences name it (<c>Node 'draft'</c>, <c>Snippet 'tone'</c>, <c>Part 'no_standard' of node 'x'</c>, <c>Macro 'm'</c>,
/// <c>Renderer 'r'</c>, <c>Deliverable 'note'</c>) and <paramref name="Path"/> its file; <paramref name="Declaration"/>
/// is what holds it (a <see cref="SourceLeaf"/>, a <see cref="SourceDeliverable"/>, a <see cref="SourceText"/>),
/// <paramref name="Function"/> the function it is in, <paramref name="Frame"/> where its reads resolve and
/// <paramref name="PartId"/> the part, for a part's text.
/// </summary>
public sealed record TemplateHost(
    TemplateSite Site, string Subject, string Path, object Declaration, SourceFunction? Function, ScopeFrame Frame, string? PartId);

/// <summary>
/// #1504 (specVersion 31, area 07): one read of a body, typed. <paramref name="Written"/> is the path as written;
/// <paramref name="Head"/> the part of it that names the thing read (<c>node.draft_summary</c>, <c>input.x</c>,
/// <c>item</c>, a <c>for</c> variable), <paramref name="RootedHead"/> that thing's rooted key where it has one
/// (<c>node.refine.draft_summary</c>), and <paramref name="Canonical"/> the path with its head rooted
/// (<c>node.refine.draft_summary.content</c>; the written path when the two are one). <paramref name="Format"/> is the
/// format read, <c>null</c> when it cannot be typed yet (a deferred loop source, a snippet the package does not define).
/// </summary>
public sealed record TypedRead(string Written, string Canonical, string? Head, string? RootedHead, JsonNode? Format, bool Nullable, int Line);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.6): one placement of a body: the <paramref name="Unit"/> as written
/// (<c>snippet.tone.content</c>, <c>part.no_standard</c>, <c>macro.letrozole</c>), its <paramref name="Kind"/>
/// (<c>"snippet"</c>, <c>"part"</c>, <c>"macro"</c>), whether it is a block (<c>{{~ x }}</c> alone on its line) and its line.
/// </summary>
public sealed record Placement(string Unit, string Kind, bool Block, int Line);

/// <summary>
/// #1504 (specVersion 31, area 07): one body held to the dialect at its host's place: its syntax, its reads typed in
/// source order, its placements, its pipes into renderers (one per pipe step into a renderer, in source order; a later
/// step of a chain has the source <c>render:&lt;n&gt;</c> of the step before it), its run spots (part 05 § 4, D4) and
/// the units it places (<see cref="PlacedUnit"/>; part 05 §§ 3.2, 6.3, 7.2, D2): every part and snippet it places, in
/// order of appearance, each followed by the units its own text places (expanded in place).
/// </summary>
public sealed record CheckedBody(
    TemplateHost Host,
    TemplateSyntax Syntax,
    IReadOnlyList<TypedRead> Reads,
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<TemplateRender> Renders,
    IReadOnlyList<TemplateRunSpot> RunSpots,
    IReadOnlyList<PlacedUnit> Units);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.3, 7.2; D2): one unit a body places, as D2's <c>placements</c>
/// lists it. <paramref name="Site"/> is the unit (<c>snippet.tone</c>, <c>snippet.standards.hpi</c>,
/// <c>part.brief</c>), <paramref name="Within"/> the unit whose text places it (<c>null</c> for the body itself),
/// <paramref name="Body"/> its text checked where it is placed (a snippet's body at the placer's place, with its
/// reads; a part's text at its node's) and <paramref name="If"/> its header rule as written (a snippet's or a part's
/// <c>if</c>; <c>null</c> for always, and for a folder item, which has none). Macros are Task 6's: it adds what a
/// placed macro carries (its marker, its instruction and its body).
/// </summary>
public sealed record PlacedUnit(string Site, string? Within, CheckedBody Body, JsonNode? If);

/// <summary>
/// #1504 (specVersion 31, area 07): what <see cref="SourceTemplates.Check"/> made of a package: its refusals and its
/// warnings (S05-060), each ordered by path (ordinal), then in the order found, and the package's checked templates,
/// <c>null</c> exactly when there is a refusal.
/// </summary>
public sealed record TemplateCheck(IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings, PackageTemplates? Templates);

/// <summary>
/// #1504 (specVersion 31, area 07; D18): every body of a package as the dialect checked it. What the resolver is told of
/// each (<see cref="ITemplateReads"/>) is built on it later; for now it holds the checked bodies.
/// </summary>
public sealed class PackageTemplates
{
    internal PackageTemplates(IReadOnlyList<CheckedBody> bodies) => Bodies = bodies;

    /// <summary>The checked bodies, in the order <see cref="SourceTemplates.Check"/> visits them.</summary>
    internal IReadOnlyList<CheckedBody> Bodies { get; }
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.5, 2.6, 3.1, 4; part 02 § 2.1; part 06 §§ 6.2, 6.3, 10.1): every body
/// of a package held to the dialect in the scope the resolver gives: each agent's prompt and each <c>rendered</c> node's
/// body at the node's place (in <c>nodes/</c>, <c>checks/</c> and each function), each deliverable's at the top of
/// <c>nodes/</c>, and each renderer's (reading only its <c>value</c>, typed by its <c>input</c>). A body Scriban's reading
/// already faults (<see cref="TemplateSyntax.Parse"/>) is reported and not typed. Every pipe into a renderer is typed
/// against the renderer's <c>input</c> and recorded (<see cref="CheckedBody.Renders"/>). Each snippet a body places is
/// checked there, and each part a prompt places is its node's text (<see cref="CheckedBody.Units"/>; part 05 §§ 3.2, 6, 7);
/// every snippet is held to the dialect whether placed or not. Never throws on package content; every walk over
/// snippets is bounded (a chain of 15, each cycle cut and said once, <see cref="UnitLimit"/> units a template).
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>
    /// Checks every body of <paramref name="package"/> against <paramref name="types"/>' formats, each read where
    /// <paramref name="resolution"/> places it, a pipe into a built-in renderer typed by <paramref name="registry"/>'s.
    /// A null argument is the caller's error.
    /// </summary>
    public static TemplateCheck Check(SourcePackage package, PackageTypes types, Resolution resolution, TypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(registry);

        var context = new Context(package, resolution, registry);
        var bodies = new List<CheckedBody>();
        foreach (var (host, text) in Hosts(package, resolution.Tree))
        {
            if (context.Top(host, text) is { } body)
                bodies.Add(body);
        }

        context.Definitions();
        context.PartsOffAgents();

        var said = context.Refusals.Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
        return new TemplateCheck(
            said,
            context.Warnings.Distinct().OrderBy(w => w.Path, StringComparer.Ordinal).ToList(),
            said.Count == 0 ? new PackageTemplates(bodies) : null);
    }

    /// <summary>
    /// The bodies of this task's sites, in order: the template leaves of <c>nodes/</c>, <c>checks/</c> and each function
    /// (a flow node before its children), every deliverable, then the renderers of the package and of each function.
    /// </summary>
    private static IEnumerable<(TemplateHost Host, string Text)> Hosts(SourcePackage package, ScopeTree tree)
    {
        var leaves = ScopeTree.Walk(package.Nodes.Nodes).Concat(ScopeTree.Walk(package.Checks.Nodes))
            .Concat(package.Functions.Items.SelectMany(f => ScopeTree.Walk(f.Body.Nodes)));
        foreach (var leaf in leaves.OfType<SourceLeaf>())
        {
            var site = leaf.Kind switch
            {
                "agent" => TemplateSite.Prompt,
                "rendered" => TemplateSite.Rendered,
                _ => (TemplateSite?)null,
            };
            if (site is { } kind)
            {
                var frame = tree.FrameOf(leaf);
                yield return (new TemplateHost(kind, $"Node '{leaf.Id}'", leaf.Path, leaf, frame.Function, frame, null), leaf.Body);
            }
        }

        foreach (var deliverable in Deliverables(package.Deliverables))
        {
            yield return (new TemplateHost(TemplateSite.Deliverable, $"Deliverable '{deliverable.Id}'", deliverable.Path, deliverable, null, tree.PackageRoot, null),
                deliverable.Body);
        }

        foreach (var renderer in package.Definitions.Renderers.Items)
            yield return (new TemplateHost(TemplateSite.Renderer, $"Renderer '{renderer.Id}'", renderer.Path, renderer, null, tree.PackageRoot, null), renderer.Body);
        foreach (var function in package.Functions.Items)
        {
            var root = tree.FunctionRoot(function);
            foreach (var renderer in function.Definitions.Renderers.Items)
                yield return (new TemplateHost(TemplateSite.Renderer, $"Renderer '{renderer.Id}'", renderer.Path, renderer, function, root, null), renderer.Body);
        }
    }

    /// <summary>Every deliverable of the folder and of its delivery folders (one level deep, part 01 § 9.3).</summary>
    private static IEnumerable<SourceDeliverable> Deliverables(SourceDeliveryFolder folder) =>
        folder.Deliverables.Concat(folder.Folders.SelectMany(f => f.Deliverables));

    /// <summary>
    /// The check of one package: what it has said so far, and the bodies of the units placed (each snippet checked once
    /// per place it is placed in, <see cref="Snippet"/>: a frame, a row of § 3.1 and a depth).
    /// </summary>
    private sealed partial class Context
    {
        private readonly SourcePackage package;
        private readonly Resolution resolution;
        private readonly TypeRegistry registry;
        private readonly Producers producers;

        public Context(SourcePackage package, Resolution resolution, TypeRegistry registry)
        {
            this.package = package;
            this.resolution = resolution;
            this.registry = registry;
            producers = new Producers(resolution);
        }

        public List<SourceRefusal> Refusals { get; } = [];

        public List<SourceRefusal> Warnings { get; } = [];

        /// <summary>
        /// A body of a template leaf, a deliverable or a renderer, with the units it places (a prompt's parts held to
        /// part 05 § 7 first); <c>null</c> when its syntax is faulted.
        /// </summary>
        public CheckedBody? Top(TemplateHost host, string text)
        {
            var leaf = host is { Site: TemplateSite.Prompt, Declaration: SourceLeaf agent } ? agent : null;
            var parts = leaf is null ? null : Parts(leaf, host);
            if (Body(host, TemplateSyntax.Parse(text), null) is not { } body)
                return null;
            if (leaf is not null)
                CountParts(body, leaf, parts!);
            return body with { Units = Expand(body, null, host.Site, parts, 0, null, null) };
        }

        /// <summary>
        /// One body: its syntax faults (and nothing more), or its reads, <c>if</c>s, placements and run spots;
        /// for a snippet's body <paramref name="reads"/> is the row of § 3.1 its placer reads by, and
        /// <paramref name="unavailable"/> gathers, in order, the reads that place does not give (S05-022, said per
        /// placement by <see cref="Expand"/>, since only the placer and the line differ).
        /// </summary>
        private CheckedBody? Body(TemplateHost host, TemplateSyntax syntax, TemplateSite? reads = null, List<string>? unavailable = null)
        {
            var body = new BodyCheck(host, syntax, package, resolution, registry, producers, reads, unavailable);
            Refusals.AddRange(body.Header());
            if (syntax.Faults.Count > 0)
            {
                Refusals.AddRange(body.Faults());
                return null;
            }

            var checkedBody = body.Run();
            Refusals.AddRange(body.Refusals);
            Warnings.AddRange(body.Warnings);
            return checkedBody;
        }
    }

    /// <summary>The check of one body: its scope, its reads in source order, and what it says.</summary>
    private sealed partial class BodyCheck
    {
        /// <summary>What a snippet body's read is refused with whatever its place: said of the snippet, not as S05-022.</summary>
        private static readonly IReadOnlySet<string> OfTheText = new HashSet<string>(StringComparer.Ordinal)
        {
            "S05-030", "S05-035", "S05-037", "S06-028", "A07-008", "S02-019",
        };

        private readonly TemplateHost host;
        private readonly TemplateSyntax syntax;
        private readonly SourcePackage package;
        private readonly TypeRegistry registry;
        private readonly Producers producers;
        private readonly List<string>? unavailable;
        private readonly FormatSet formats;
        private readonly PathTyping typing;
        private readonly List<(int Start, int Depth, TemplateFor? For)> constructs;
        private readonly Dictionary<TemplateFor, JsonNode?> elements = new(ReferenceEqualityComparer.Instance);
        private readonly List<(int Start, int Order, TypedRead Read)> reads = [];
        private readonly List<Placement> placements = [];
        private readonly List<TemplateRender> renders = [];
        private readonly List<TemplateRunSpot> spots = [];

        public BodyCheck(
            TemplateHost host, TemplateSyntax syntax, SourcePackage package, Resolution resolution, TypeRegistry registry, Producers producers,
            TemplateSite? reads = null, List<string>? unavailable = null)
        {
            this.host = host;
            this.syntax = syntax;
            this.package = package;
            this.registry = registry;
            this.producers = producers;
            this.unavailable = unavailable;
            formats = resolution.Tree.FormatsOf(host.Frame);
            typing = new PathTyping(host, resolution.Tree, formats, package, reads ?? host.Site);
            constructs = syntax.Fors.Select(f => (f.Tag.Start, f.Depth, (TemplateFor?)f))
                .Concat(syntax.Ifs.Select(i => (i.Tag.Start, i.Depth, (TemplateFor?)null)))
                .OrderBy(c => c.Item1)
                .ToList();
        }

        public List<SourceRefusal> Refusals { get; } = [];

        public List<SourceRefusal> Warnings { get; } = [];

        /// <summary>Task 2's faults as sentences; a <c>-</c> on a placement's tag is S05-016 for that unit rather than S05-019.</summary>
        public IEnumerable<SourceRefusal> Faults()
        {
            // The dash faults were found first, one per dash tag in source order, and the sort by line keeps their order.
            var dashed = new Queue<TemplateTag>(syntax.Tags.Where(t => t.DashLeft || t.DashRight));
            foreach (var fault in syntax.Faults)
            {
                if (fault.Id == "S05-019" && dashed.TryDequeue(out var tag) && PlacementAt(tag) is { } unit)
                    yield return DialectSentences.PlacesWithDash(host.Subject, unit.Unit, fault.Line, host.Path);
                else
                    yield return DialectSentences.Of(fault, host.Subject, host.Path);
            }
        }

        public CheckedBody Run()
        {
            var order = 0;
            foreach (var loop in syntax.Fors)
            {
                if (Path(loop.Source) is { } source)
                    Record(loop.Tag.Start, order++, Type(source, loop.Tag.Line, loop.Tag.Start, loop.Depth));
            }

            foreach (var output in syntax.Outputs)
            {
                var scope = Scope(output.Tag.Start, output.Depth);
                TypedRead? head = null;
                // A part or a macro a snippet places is judged where the snippet is placed (Context.Expand), not read here.
                var unplaced = host.Site == TemplateSite.SnippetBody && Unit(output) is { Kind: "part" or "macro" };
                foreach (var path in output.Paths)
                {
                    if (unplaced)
                        continue;
                    if (host.Site == TemplateSite.PartText && path.Split('.')[0] == "part" && scope.All(v => v.Name != "part"))
                    {
                        // Part 05 § 7.1 (A07-002): a part's text places no part.
                        Refusals.Add(DialectSentences.PartOnlyPresents(host.PartId ?? string.Empty, NodeId, path, host.Path));
                        continue;
                    }

                    var read = Record(output.Tag.Start, order++, Typed(path, output.Tag.Line, scope));
                    if (path == output.Path)
                        head ??= read;
                    if (path == output.Path && output.Pipes.Count == 0 && read?.Format is { } format)
                        Written(path, output.Tag.Line, format);
                }

                Pipes(output, head);
                Literal(output);
                Place(output);
            }

            if (host.Site == TemplateSite.PartText && syntax.Ifs.Count > 0)
            {
                // Part 05 § 7.1 (A07-002): a part has no if of its own; its own header if decides it.
                Refusals.Add(DialectSentences.PartOnlyPresents(host.PartId ?? string.Empty, NodeId, "if", host.Path));
            }
            else
            {
                foreach (var chain in syntax.Ifs)
                    If(chain, ref order);
            }

            RendererPipesInTests();
            RunSpots();

            return new CheckedBody(
                host, syntax, reads.OrderBy(r => r.Start).ThenBy(r => r.Order).Select(r => r.Read).ToList(), placements, renders, spots, []);
        }

        // ---- scope -----------------------------------------------------------------------------------

        /// <summary>
        /// The <c>for</c> variables in scope at offset <paramref name="start"/>, <paramref name="depth"/> <c>if</c>s and
        /// <c>for</c>s deep, outermost first: at each depth below, the construct last opened before it there encloses it.
        /// </summary>
        private List<ForVariable> Scope(int start, int depth)
        {
            var scope = new List<ForVariable>();
            for (var level = 0; level < depth; level++)
            {
                var enclosing = constructs.LastOrDefault(c => c.Depth == level && c.Start < start);
                if (enclosing.For is { } loop)
                    scope.Add(new ForVariable(loop.Variable, Element(loop)));
            }

            return scope;
        }

        /// <summary>The element of what <paramref name="loop"/> iterates, once (a list's <c>items</c>); null when not typed. Bounded by the nesting limit.</summary>
        private JsonNode? Element(TemplateFor loop)
        {
            if (elements.TryGetValue(loop, out var known))
                return known;
            elements[loop] = null;
            JsonNode? element = null;
            if (Path(loop.Source) is { } source
                && typing.Type(source, loop.Tag.Line, Scope(loop.Tag.Start, loop.Depth)).Read is { Format: { } list }
                && FormatTypes.Shape(list, formats) == FormatShape.List)
            {
                element = ExpressionChecker.ItemsOf(list, formats);
            }

            elements[loop] = element;
            return element;
        }

        /// <summary>A <c>for</c>'s source when it is one access path as written (<c>node.sections.items</c>); its other forms are Task 2's faults.</summary>
        private static string? Path(string source)
        {
            var text = source.Trim();
            if (text.Length == 0 || text.Split('.').Any(s => s.Length == 0 || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')))
                return null;
            return text;
        }

        // ---- reads -----------------------------------------------------------------------------------

        /// <summary>A <c>for</c>'s source typed at the <c>for</c>'s place: a list (S05-006 otherwise, with the source as written).</summary>
        private (TypedRead?, SourceRefusal?) Type(string source, int line, int start, int depth)
        {
            var typed = Typed(source, line, Scope(start, depth));
            if (typed.Item1 is { Format: { } format } && FormatTypes.Shape(format, formats) != FormatShape.List)
                return (null, DialectSentences.ForOverNotAList(host.Subject, source, line, host.Path));
            return typed;
        }

        /// <summary>
        /// <paramref name="path"/> typed at this body's place; in a placed snippet's body a read its placer's place does
        /// not give is noted as unavailable (said as S05-022 per placement) and gives <see cref="Unavailable"/>; what is
        /// wrong whatever the place (a bare read, an unknown snippet) is said of the snippet.
        /// </summary>
        private (TypedRead?, SourceRefusal?) Typed(string path, int line, List<ForVariable> scope)
        {
            var typed = typing.Type(path, line, scope);
            if (unavailable is not null && typed.Refusal is { } refusal && !OfTheText.Contains(refusal.Id))
            {
                if (!unavailable.Contains(path, StringComparer.Ordinal))
                    unavailable.Add(path);
                return (null, Unavailable);
            }

            return typed;
        }

        /// <summary>The refusal a read noted as unavailable stands for: never said itself (<see cref="Record"/> drops it).</summary>
        private static readonly SourceRefusal Unavailable = new("S05-022", string.Empty, string.Empty);

        /// <summary>The id of the node whose prompt, part or <c>rendered</c> body this is.</summary>
        private string NodeId => host.Declaration is SourceNode node ? node.Id : host.Subject;

        private TypedRead? Record(int start, int order, (TypedRead? Read, SourceRefusal? Refusal) typed)
        {
            if (typed.Refusal is { } refusal && !ReferenceEquals(refusal, Unavailable))
                Refusals.Add(refusal);
            if (typed.Read is { } read)
                reads.Add((start, order, read));
            return typed.Read;
        }

        /// <summary>A07-007: an output whose whole expression is an object or a list.</summary>
        private void Written(string path, int line, JsonNode format)
        {
            var shape = FormatTypes.Shape(format, formats);
            if (shape is FormatShape.Object or FormatShape.List)
                Refusals.Add(DialectSentences.WritesAnObjectOrList(host.Subject, path, line, shape == FormatShape.Object ? "an object" : "a list", host.Path));
        }

        /// <summary>A07-007 for a literal list or object written as an output (<c>{{ [1, 2] }}</c>).</summary>
        private void Literal(TemplateOutput output)
        {
            if (output.Path != null || output.Paths.Count > 0 || output.Pipes.Count > 0)
                return;
            var inner = Inner(output.Tag);
            if (inner.StartsWith('[') || inner.StartsWith('{'))
                Refusals.Add(DialectSentences.WritesAnObjectOrList(host.Subject, inner, output.Tag.Line, inner.StartsWith('[') ? "a list" : "an object", host.Path));
        }

        /// <summary>The tag's expression: its text between the braces and the marks, trimmed.</summary>
        private string Inner(TemplateTag tag)
        {
            var text = syntax.Text[Math.Clamp(tag.Start, 0, syntax.Text.Length)..Math.Clamp(tag.End, tag.Start, syntax.Text.Length)];
            if (text.StartsWith("{{", StringComparison.Ordinal))
                text = text[2..];
            if (text.EndsWith("}}", StringComparison.Ordinal))
                text = text[..^2];
            return text.Trim().Trim('~', '-').Trim();
        }

        // ---- body if ---------------------------------------------------------------------------------

        /// <summary>
        /// Part 05 § 2.5: each test of <paramref name="chain"/> is a loop position, a state compared with a literal of the
        /// closed set (S05-010 otherwise), or whether a check passed (S05-007 for anything else); a chain branching on a
        /// state with no <c>else</c> handles every state (S05-009). A renderer's body may test its own <c>value</c> in any way (R2).
        /// </summary>
        private void If(TemplateIf chain, ref int order)
        {
            var scope = Scope(chain.Tag.Start, chain.Depth);
            var handled = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var test in chain.Tests)
            {
                var read = test.Path is { } path ? Typed(path, test.Line, scope) : (null, null);
                Record(test.Start, order++, read);
                if (read.Item2 != null || host.Site == TemplateSite.Renderer)
                    continue;

                switch (DialectRules.TestForm(test))
                {
                    case BodyTest.Position or BodyTest.CheckPassed:
                        break;
                    case BodyTest.State when read.Item1 is null || IsState(read.Item1.Format):
                        var literal = test.Literal![1..^1];
                        if (!DialectRules.States.Contains(literal, StringComparer.Ordinal))
                        {
                            Refusals.Add(DialectSentences.ComparesStateWith(host.Subject, test.Path!, literal, test.Line, host.Path));
                            break;
                        }

                        if (!handled.TryGetValue(test.Path!, out var states))
                            handled[test.Path!] = states = new HashSet<string>(StringComparer.Ordinal);
                        if (test.Operator == "==")
                            states.Add(literal);
                        else
                            states.UnionWith(DialectRules.States.Where(s => s != literal));
                        break;
                    default:
                        Refusals.Add(DialectSentences.TestsInBodyIf(host.Subject, test.Text, test.Line, host.Path));
                        break;
                }
            }

            if (chain.HasElse)
                return;
            foreach (var (path, states) in handled)
            {
                var missing = DialectRules.States.Where(s => !states.Contains(s)).ToList();
                if (missing.Count > 0)
                {
                    Refusals.Add(DialectSentences.BranchesWithoutEveryState(
                        host.Subject, path, DialectRules.States.Where(states.Contains).ToList(), missing, host.Path));
                }
            }
        }

        /// <summary>Whether <paramref name="format"/> is the closed set of states (an <c>enum</c> of exactly those three).</summary>
        private static bool IsState(JsonNode? format) =>
            format is JsonObject { } schema && schema["enum"] is JsonArray values
            && values.Select(v => FormatTypes.Text(v)).OrderBy(v => v, StringComparer.Ordinal)
                .SequenceEqual(DialectRules.States.OrderBy(v => v, StringComparer.Ordinal), StringComparer.Ordinal);

        // ---- placements ------------------------------------------------------------------------------

        /// <summary>
        /// Part 05 § 2.6: an output whose whole expression is a unit the site may place is a placement, its form held to
        /// § 2.6's table (S05-015, S05-017, S05-018; S05-016 is said of its fault), and placed at the top level (A07-009).
        /// </summary>
        private void Place(TemplateOutput output)
        {
            if (Unit(output) is not { } unit)
                return;
            var tag = output.Tag;
            var line = tag.Line;
            if (tag.TildeRight)
                Refusals.Add(DialectSentences.PlacesWithTildeRight(host.Subject, unit.Unit, line, host.Path));
            if (tag.TildeLeft && tag.TextBefore)
                Refusals.Add(DialectSentences.PlacesBlockInsideALine(host.Subject, unit.Unit, line, host.Path));
            else if (tag.TildeLeft && tag.TextAfter)
                Refusals.Add(DialectSentences.TextAfterBlock(host.Subject, unit.Unit, line, host.Path));
            if (output.Enclosing.Count > 0)
            {
                Refusals.Add(DialectSentences.PlacesInsideAnIfOrFor(
                    host.Subject, unit.Unit, line, output.Enclosing[^1] == "for" ? "a for" : "an if", host.Path));
            }

            placements.Add(unit with { Block = tag.TildeLeft && tag.AloneOnLine });
        }

        /// <summary>The placement on <paramref name="tag"/>, if an output there is one.</summary>
        private Placement? PlacementAt(TemplateTag tag) =>
            syntax.Outputs.Where(o => ReferenceEquals(o.Tag, tag) || o.Tag.Start == tag.Start).Select(Unit).FirstOrDefault(u => u != null);

        /// <summary>
        /// The unit <paramref name="output"/> places, when its whole expression is one and the site reads its root:
        /// <c>snippet.&lt;id&gt;.content</c> or <c>snippet.&lt;folder&gt;.&lt;id&gt;.content</c>, <c>part.&lt;id&gt;</c>,
        /// <c>macro.&lt;id&gt;</c> or <c>macro.&lt;folder&gt;.&lt;id&gt;</c> (a macro's marker, <c>….content</c>, is no placement).
        /// </summary>
        private Placement? Unit(TemplateOutput output)
        {
            if (host.Site == TemplateSite.Renderer || output.Path is not { } path || output.Pipes.Count > 0 || output.Paths.Count != 1)
                return null;
            var s = path.Split('.');
            var kind = s switch
            {
                ["snippet", _, "content"] or ["snippet", _, _, "content"] => "snippet",
                ["part", _] => "part",
                ["macro", _] or ["macro", _, not "content"] => "macro",
                _ => null,
            };
            if (kind == null || !SiteReads.Of(host.Site).Roots.Contains(s[0]))
                return null;
            return new Placement(path, kind, false, output.Tag.Line);
        }
    }
}
