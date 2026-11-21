using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6; part 01 §§ 5.2, 5.3; D2, D12): snippets. A snippet is placed as
/// <c>snippet.&lt;id&gt;.content</c> (a folder item as <c>snippet.&lt;folder&gt;.&lt;id&gt;.content</c>; one the package does
/// not define is S05-035, said by <see cref="PathTyping"/>). Its body is checked in every place it is placed, at the
/// placer's frame and by the placer's row of § 3.1: a read that place does not give is S05-022. Placed in a part's text
/// (or a macro's instruction), directly or through another snippet, it places no <c>part.*</c> or <c>macro.*</c>
/// (A07-001). A snippet reaching itself is S05-023, said once per cycle, and a chain of more than 15 is S05-013 (D12).
/// Every snippet, placed or not, is parsed and held to the dialect's statements; a single snippet's <c>format</c> is a
/// text format (A07-010) and a folder item's header is of its folder's <c>format</c> (S05-036). A template places at
/// most <see cref="UnitLimit"/> units, counting what they place (A07-011), so a publish never expands without bound.
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>The most units one template may place, counting what its parts and snippets place in turn (A07-011).</summary>
    internal const int UnitLimit = 1_000;

    private static readonly CorvusFormatValueValidator HeaderValidator = new();

    /// <summary>
    /// A snippet's body checked at one place: the body (<c>null</c> when it can't be checked), what that place does not
    /// give it (reads and parts as written, in order) and the parts and macros it places that the place does not allow.
    /// </summary>
    private sealed record PlacedSnippet(CheckedBody? Body, IReadOnlyList<string> Unavailable, IReadOnlyList<string> Guarded)
    {
        public static readonly PlacedSnippet None = new(null, [], []);
    }

    /// <summary>A snippet a placement names: its file, its name in sentences, its site in <see cref="PlacedUnit"/> and its <c>if</c>.</summary>
    private sealed record SnippetRef(SourceText Text, string Name, string Site, JsonNode? If);

    private sealed partial class Context
    {
        private readonly Dictionary<SourceText, TemplateSyntax> syntaxes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SourceText, Dictionary<(ScopeFrame Frame, TemplateSite Reads, int Depth), PlacedSnippet>> placedBodies =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<SourceText> open = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The units <paramref name="body"/> places, in order, each followed by the units it places in turn (D2):
        /// <paramref name="self"/> is <paramref name="body"/>'s own site (<c>null</c> for a template's body),
        /// <paramref name="reads"/> the row its snippets read by, <paramref name="parts"/> a prompt's declared parts,
        /// <paramref name="depth"/> how many snippets deep it is. For a snippet's body <paramref name="unavailable"/>
        /// gathers the parts a prompt's, a <c>rendered</c> node's or a deliverable's snippet places (S05-022), and
        /// <paramref name="guarded"/> the other parts and macros it places, itself or through a snippet, that its place
        /// does not allow (said once, by the template that places the snippet: <see cref="Barred"/>). A macro is placed
        /// by a prompt, a part's text or a prompt's snippet (<see cref="PlaceMacro"/>). Each placement of a snippet says
        /// S05-022 for what the snippet's place does not give.
        /// </summary>
        private List<PlacedUnit> Expand(
            CheckedBody body, string? self, TemplateSite reads, IReadOnlyDictionary<string, PartUnit>? parts, int depth,
            List<string>? unavailable, List<string>? guarded)
        {
            var units = new List<PlacedUnit>();
            foreach (var placement in body.Placements)
            {
                switch (placement.Kind)
                {
                    case "snippet":
                        if (Lookup(placement.Unit, body.Host.Function) is not { } snippet)
                            break;
                        if (depth + 1 > DialectRules.NestingLimit)
                        {
                            Refusals.Add(DialectSentences.NestsTooDeep($"Snippet '{snippet.Name}'", depth + 1, placement.Line, body.Host.Path));
                            break;
                        }

                        var (unitBody, missing, inner) = Snippet(snippet, body.Host.Frame, reads, depth + 1);
                        foreach (var read in missing)
                            Refusals.Add(DialectSentences.SnippetReadsUnavailable(snippet.Name, read, body.Host.Subject, placement.Line, body.Host.Path));
                        if (unitBody is not null)
                        {
                            units.Add(new PlacedUnit(snippet.Site, self, unitBody, snippet.If));
                            units.AddRange(unitBody.Units);
                        }

                        if (guarded is not null)
                            guarded.AddRange(inner);
                        else
                            Barred(inner, snippet.Name, body, placement, self, reads);

                        break;
                    case "macro" when unavailable is null || reads == TemplateSite.Prompt:
                        // A prompt's, a part's text's, or a snippet's in a prompt (part 05 § 8.3).
                        PlaceMacro(placement, body, self, units);
                        break;
                    case "part" or "macro" when unavailable is not null:
                        // A snippet's own part or macro its place does not allow: said by the template that places the
                        // snippet (Barred: the guard, a macro's body, a node that is no agent), or a part a prompt's
                        // snippet places, which its place does not give (a part belongs to one node's body).
                        if (Guard(reads) is not null || placement.Kind == "macro" || reads == TemplateSite.MacroBody)
                            guarded!.Add(placement.Unit);
                        else
                            unavailable.Add(placement.Unit);
                        break;
                    case "part" when parts is not null:
                        if (parts.TryGetValue(placement.Unit["part.".Length..], out var part) && part.Body is { } text)
                        {
                            units.Add(new PlacedUnit(placement.Unit, self, text, part.If));
                            units.AddRange(text.Units);
                        }

                        break;
                }

                if (units.Count > UnitLimit)
                {
                    Refusals.Add(DialectSentences.PlacesTooManyUnits(body.Host.Subject, UnitLimit, body.Host.Path));
                    units.RemoveRange(UnitLimit, units.Count - UnitLimit);
                    break;
                }
            }

            return units;
        }

        /// <summary>Where the guard holds (§ 6.3), in its sentence's words; <c>null</c> elsewhere.</summary>
        private static string? Guard(TemplateSite reads) => reads switch
        {
            TemplateSite.PartText => "a part's text",
            TemplateSite.MacroInstruction => "a macro's instruction",
            _ => null,
        };

        /// <summary>
        /// <paramref name="snippet"/>'s body checked at its place (part 05 § 3.2): at <paramref name="frame"/>, by the row
        /// <paramref name="reads"/>, <paramref name="depth"/> snippets deep; with the units it places, the reads and units
        /// that place does not give (said per placement, as S05-022, by the caller) and the parts and macros it places
        /// under the guard. Checked once per place, whatever line places it, and its body shared by every placement there;
        /// never round a cycle (S05-023 is <see cref="Cycles"/>'s). A body with a syntax fault (said by
        /// <see cref="Definitions"/>) gives none.
        /// </summary>
        private PlacedSnippet Snippet(SnippetRef snippet, ScopeFrame frame, TemplateSite reads, int depth)
        {
            if (open.Contains(snippet.Text))
                return PlacedSnippet.None;
            if (!placedBodies.TryGetValue(snippet.Text, out var known))
                placedBodies[snippet.Text] = known = [];
            if (known.TryGetValue((frame, reads, depth), out var done))
                return done;

            var result = PlacedSnippet.None;
            var syntax = SyntaxOf(snippet.Text);
            var host = new TemplateHost(TemplateSite.SnippetBody, $"Snippet '{snippet.Name}'", snippet.Text.Path, snippet.Text, frame.Function, frame, null);
            var unavailable = new List<string>();
            if (syntax.Faults.Count == 0 && Body(host, syntax, reads, unavailable) is { } body)
            {
                open.Add(snippet.Text);
                var guarded = new List<string>();
                var units = Expand(body, snippet.Site, reads, null, depth, unavailable, guarded);
                open.Remove(snippet.Text);
                result = new PlacedSnippet(
                    body with { Units = units },
                    unavailable.Distinct(StringComparer.Ordinal).ToList(),
                    guarded.Distinct(StringComparer.Ordinal).ToList());
            }

            known[(frame, reads, depth)] = result;
            return result;
        }

        /// <summary>The snippet <paramref name="unit"/> (<c>snippet.&lt;id&gt;.content</c> or <c>snippet.&lt;folder&gt;.&lt;id&gt;.content</c>) names, the function's own before the package's.</summary>
        private SnippetRef? Lookup(string unit, SourceFunction? function)
        {
            var s = unit.Split('.');
            if (s.Length < 3)
                return null;
            var entry = function?.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == s[1])
                ?? package.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == s[1]);
            return (entry, s.Length) switch
            {
                (SourceSingleText single, 3) => new SnippetRef(single.Text, single.Id, $"snippet.{single.Id}", single.Text.Header["if"]),
                (SourceTextFolder folder, 4) when folder.Items.FirstOrDefault(i => i.Id == s[2]) is { } item =>
                    new SnippetRef(item, $"{folder.Id}/{item.Id}", $"snippet.{folder.Id}.{item.Id}", null),
                _ => null,
            };
        }

        private TemplateSyntax SyntaxOf(SourceText text)
        {
            if (!syntaxes.TryGetValue(text, out var syntax))
                syntaxes[text] = syntax = TemplateSyntax.Parse(text.Body);
            return syntax;
        }

        // ---- every snippet, placed or not ---------------------------------------------------------

        /// <summary>
        /// Every snippet of the package and of each function: its body held to the dialect's statements (Task 2's
        /// faults), a single snippet's <c>format</c> a text format (A07-010), a folder item's header of its folder's
        /// <c>format</c> (S05-036); then the cycles each scope's snippets make (S05-023).
        /// </summary>
        public void Definitions()
        {
            var tree = resolution.Tree;
            var scopes = new List<(SourceDefinitions Definitions, SourceFunction? Function, ScopeFrame Frame)> { (package.Definitions, null, tree.PackageRoot) };
            scopes.AddRange(package.Functions.Items.Select(f => (f.Definitions, (SourceFunction?)f, tree.FunctionRoot(f))));
            var said = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (definitions, function, frame) in scopes)
            {
                var formats = tree.FormatsOf(frame);
                foreach (var entry in definitions.Snippets.Items)
                {
                    switch (entry)
                    {
                        case SourceSingleText { Text: var text }:
                            if (!(FormatSet.NameOf(text.Header["format"]) is { } name && TextFormats.Contains(name)))
                                Refusals.Add(DialectSentences.SnippetFormatNotText(text.Id, TypeStrings.Print(text.Header["format"], formats), text.Path));
                            Statements(text, text.Id, frame);
                            break;
                        case SourceTextFolder folder:
                            foreach (var item in folder.Items)
                            {
                                Header(folder, item, formats);
                                Statements(item, $"{folder.Id}/{item.Id}", frame);
                            }

                            break;
                    }
                }

                Cycles(function, said);
            }
        }

        /// <summary>Task 2's faults of a snippet's body, said of the snippet in its own file.</summary>
        private void Statements(SourceText text, string name, ScopeFrame frame)
        {
            var syntax = SyntaxOf(text);
            if (syntax.Faults.Count == 0)
                return;
            var host = new TemplateHost(TemplateSite.SnippetBody, $"Snippet '{name}'", text.Path, text, frame.Function, frame, null);
            Refusals.AddRange(new BodyCheck(host, syntax, package, resolution, registry, producers).Faults());
        }

        /// <summary>
        /// S05-036: <paramref name="item"/>'s header against <paramref name="folder"/>'s <c>format</c>; the error is the first
        /// fault's keyword and its place in the header (a JSON pointer, or <c>the header</c> for the whole), never a value.
        /// </summary>
        private void Header(SourceTextFolder folder, SourceText item, FormatSet formats)
        {
            var format = folder.Index["format"];
            if (!FormatTypes.Expands(format, formats))
                return; // A folder format the type rules refuse (S02-017) is not judged again here.
            var result = HeaderValidator.Validate(item.Header, format, formats);
            if (result.Valid || result.Faults.Count == 0)
                return;
            var fault = result.Faults[0];
            var error = $"{fault.Keyword} at {(fault.Pointer.Length == 0 ? "the header" : fault.Pointer)}";
            Refusals.Add(DialectSentences.SnippetHeaderNotFormat(folder.Id, item.Id, TypeStrings.Print(format, formats), error, item.Path));
        }

        /// <summary>
        /// S05-023: the cycles the snippets seen from <paramref name="function"/> (its own, then the package's) make through
        /// their placements, each said once (<paramref name="said"/> holds every cycle said, from its first snippet in
        /// ordinal order). A depth-first walk over at most every snippet of the package.
        /// </summary>
        private void Cycles(SourceFunction? function, HashSet<string> said)
        {
            var texts = new List<(SourceText Text, string Name)>();
            foreach (var entry in package.Definitions.Snippets.Items.Concat(function?.Definitions.Snippets.Items ?? []))
            {
                if (entry is SourceSingleText single)
                    texts.Add((single.Text, single.Id));
                else if (entry is SourceTextFolder folder)
                    texts.AddRange(folder.Items.Select(i => (i, $"{folder.Id}/{i.Id}")));
            }

            var names = new Dictionary<SourceText, string>(ReferenceEqualityComparer.Instance);
            foreach (var (text, name) in texts)
                names.TryAdd(text, name);
            var state = new Dictionary<SourceText, bool>(ReferenceEqualityComparer.Instance); // false: on the walk's path; true: done
            var path = new List<SourceText>();

            void Visit(SourceText text)
            {
                state[text] = false;
                path.Add(text);
                foreach (var next in Placed(text, function))
                {
                    if (!state.TryGetValue(next, out var finished))
                    {
                        Visit(next);
                    }
                    else if (!finished)
                    {
                        var cycle = path.Skip(path.FindIndex(t => ReferenceEquals(t, next))).Select(t => names.GetValueOrDefault(t, t.Id)).ToList();
                        var first = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
                        if (said.Add(string.Join(" ", cycle.Skip(first).Concat(cycle.Take(first)))))
                            Refusals.Add(DialectSentences.ReachesItself("Snippet", cycle[0], [.. cycle, cycle[0]], next.Path));
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[text] = true;
            }

            foreach (var (text, _) in texts)
            {
                if (!state.ContainsKey(text))
                    Visit(text);
            }
        }

        /// <summary>The snippets <paramref name="text"/>'s body places, as seen from <paramref name="function"/>, in source order.</summary>
        private IEnumerable<SourceText> Placed(SourceText text, SourceFunction? function)
        {
            var syntax = SyntaxOf(text);
            if (syntax.Faults.Count > 0)
                yield break;
            foreach (var output in syntax.Outputs)
            {
                if (output is { Path: { } unit, Pipes.Count: 0, Paths.Count: 1 }
                    && unit.Split('.') is ["snippet", _, "content"] or ["snippet", _, _, "content"]
                    && Lookup(unit, function) is { } snippet)
                {
                    yield return snippet.Text;
                }
            }
        }
    }
}
