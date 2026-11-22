using System.Globalization;
using System.Text.Json.Nodes;
using Scriban.Parsing;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.4; #1504's first comment): the marker of macro <paramref name="Macro"/>
/// (<c>letrozole</c>, a folder's <c>section_notes.hpi</c>) as its instruction holds it: <paramref name="Text"/> the
/// marker without braces (<c>macro.letrozole.content</c>; a folder macro's is qualified,
/// <c>macro.section_notes.hpi.content</c>) and <paramref name="Block"/> whether it is the block form
/// (<c>{{~ … }}</c> alone on its line) rather than inline.
/// </summary>
public sealed record MacroMarker(string Macro, string Text, bool Block);

/// <summary>#1504 (specVersion 31, area 07; part 05 §§ 8.4, 8.6): the exact text a model writes for a marker.</summary>
public static class MacroMarkers
{
    /// <summary><c>{{~ macro.x.content }}</c> for a block marker, <c>{{ macro.x.content }}</c> for an inline one.</summary>
    public static string Open(MacroMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return (marker.Block ? "{{~ " : "{{ ") + marker.Text + " }}";
    }
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.1 to 8.5, 8.7; part 03 § 4.6; D17): macros. A macro is placed as
/// <c>macro.&lt;id&gt;</c> or <c>macro.&lt;folder&gt;.&lt;id&gt;</c> (one the package does not define is S05-011) only by an
/// agent's prompt, a part's text or a snippet placed in the prompt; anywhere else it is S05-047 (a <c>rendered</c> node,
/// a deliverable, a snippet they place), S05-042 (a macro's body), A07-003 (a macro's instruction) or A07-001 (a snippet
/// under the guard). The placing node returns a text format (S05-049) that takes the macro's (S05-046) and places each
/// macro once, counting its parts' text and its snippets (S05-048). Every macro, placed or not, is held once: its
/// instruction (lines joined with <c>\n</c>) holds its own marker exactly once (S05-044), in a placement's allowed form
/// (S05-015 to S05-018, A07-009), and no other macro's marker (S05-045, or S05-011 for no macro) and no placement
/// (A07-003); its body places nothing (S05-042, S05-043), holds no marker (S05-051) and, for a <c>plaintext</c> macro,
/// writes no Markdown (S05-050). Where it is placed, its instruction and its body are checked once per place (a frame),
/// whatever placer and line place it, with their snippets (D17: the instruction's own marker is no read). A prompt, a
/// part's text or an instruction reading an agent declared profile-carrying that places a profile-reading macro is
/// S05-064.
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>A macro a placement names: its file, its name in sentences (<c>letrozole</c>, <c>section_notes.hpi</c>), its site and its own <c>if</c>.</summary>
    private sealed record MacroRef(SourceText Text, string Name, string Site, JsonNode? If);

    /// <summary>A macro checked at one place: its instruction and its body (each <c>null</c> when it can't be checked) and its marker.</summary>
    private sealed record PlacedMacro(CheckedBody? Instruction, CheckedBody? Body, MacroMarker? Marker)
    {
        public static readonly PlacedMacro None = new(null, null, null);
    }

    private sealed partial class Context
    {
        private readonly Dictionary<SourceText, string?> instructions = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SourceText, TemplateSyntax> instructionSyntaxes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SourceText, Dictionary<ScopeFrame, PlacedMacro>> placedMacros = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Adds the macro <paramref name="placement"/> of <paramref name="body"/> names to <paramref name="units"/>: its
        /// instruction placed (within <paramref name="self"/>), then the units the instruction places. S05-011 when no
        /// macro has that name.
        /// </summary>
        private void PlaceMacro(Placement placement, CheckedBody body, string? self, List<PlacedUnit> units)
        {
            if (LookupMacro(placement.Unit, body.Host.Function) is not { } macro)
            {
                Refusals.Add(DialectSentences.ReadsWhatIsNotThere(body.Host.Subject, placement.Unit, placement.Line, SiteReads.AsWritten("macro"), body.Host.Path));
                return;
            }

            MacroRules(macro, body);
            var placed = Macro(macro, body.Host.Frame);
            if (placed.Instruction is not { } instruction)
                return;
            units.Add(new PlacedUnit(macro.Site, self, instruction, macro.If, placed.Marker, instruction, placed.Body));
            units.AddRange(instruction.Units);
        }

        /// <summary>
        /// What a snippet placed in <paramref name="body"/> (as <paramref name="name"/>, at <paramref name="placement"/>)
        /// places that the row <paramref name="reads"/> does not allow, itself or through a snippet it places, said once
        /// per unit: A07-001 under the guard, S05-042 or S05-043 in a macro's body (<paramref name="self"/>), S05-047 in
        /// a <c>rendered</c> node or a deliverable.
        /// </summary>
        private void Barred(IEnumerable<string> inner, string name, CheckedBody body, Placement placement, string? self, TemplateSite reads)
        {
            foreach (var unit in inner.Distinct(StringComparer.Ordinal))
            {
                var isMacro = unit.StartsWith("macro.", StringComparison.Ordinal);
                if (Guard(reads) is { } where)
                    Refusals.Add(DialectSentences.SnippetPlacesPartOrMacro(name, unit, body.Host.Subject, where, body.Host.Path));
                else if (reads == TemplateSite.MacroBody)
                {
                    var macro = (self ?? string.Empty).StartsWith("macro.", StringComparison.Ordinal) ? self!["macro.".Length..] : body.Host.Subject;
                    Refusals.Add(isMacro
                        ? DialectSentences.MacroBodyPlacesMacro(macro, unit["macro.".Length..], body.Host.Path)
                        : DialectSentences.MacroBodyPlacesPart(macro, unit["part.".Length..], body.Host.Path));
                }
                else if (isMacro)
                    Refusals.Add(DialectSentences.OnlyAgentsPlaceMacros(body.Host.Subject, unit["macro.".Length..], body.Host.Path));
                else
                    Refusals.Add(DialectSentences.SnippetReadsUnavailable(name, unit, body.Host.Subject, placement.Line, body.Host.Path));
            }
        }

        /// <summary>
        /// <paramref name="macro"/> checked at <paramref name="frame"/> (part 05 § 3.2): its instruction as a macro
        /// instruction and its body as a macro body, each with the snippets it places. Once per frame, whatever placer and
        /// line place it, every placement there sharing both bodies. A text with a syntax fault (said by
        /// <see cref="Macros"/>) gives none.
        /// </summary>
        private PlacedMacro Macro(MacroRef macro, ScopeFrame frame)
        {
            if (!placedMacros.TryGetValue(macro.Text, out var known))
                placedMacros[macro.Text] = known = new Dictionary<ScopeFrame, PlacedMacro>(ReferenceEqualityComparer.Instance);
            if (known.TryGetValue(frame, out var done))
                return done;

            var subject = $"Macro '{macro.Name}'";
            CheckedBody? instruction = null;
            MacroMarker? marker = null;
            if (InstructionSyntax(macro.Text) is { Faults.Count: 0 } syntax
                && Body(new TemplateHost(TemplateSite.MacroInstruction, subject, macro.Text.Path, macro.Text, frame.Function, frame, null), syntax) is { } checkedInstruction)
            {
                instruction = checkedInstruction with { Units = Expand(checkedInstruction, macro.Site, TemplateSite.MacroInstruction, null, 0, null, null) };
                var own = OwnMarkers(syntax, macro.Name);
                if (own.Count == 1)
                    marker = new MacroMarker(macro.Name, MarkerText(macro.Name), own[0].Tag.TildeLeft && own[0].Tag.AloneOnLine);
            }

            CheckedBody? body = null;
            if (SyntaxOf(macro.Text) is { Faults.Count: 0 } bodySyntax
                && Body(new TemplateHost(TemplateSite.MacroBody, subject, macro.Text.Path, macro.Text, frame.Function, frame, null), bodySyntax) is { } checkedBody)
            {
                body = checkedBody with { Units = Expand(checkedBody, macro.Site, TemplateSite.MacroBody, null, 0, null, null) };
            }

            var result = new PlacedMacro(instruction, body, marker);
            known[frame] = result;
            return result;
        }

        /// <summary>The macro <paramref name="unit"/> (<c>macro.&lt;id&gt;</c> or <c>macro.&lt;folder&gt;.&lt;id&gt;</c>) names, the function's own before the package's.</summary>
        private MacroRef? LookupMacro(string unit, SourceFunction? function)
        {
            var s = unit.Split('.');
            if (s.Length < 2 || s[0] != "macro")
                return null;
            var entry = function?.Definitions.Macros.Items.FirstOrDefault(e => e.Id == s[1])
                ?? package.Definitions.Macros.Items.FirstOrDefault(e => e.Id == s[1]);
            return (entry, s.Length) switch
            {
                (SourceSingleText single, 2) => new MacroRef(single.Text, single.Id, $"macro.{single.Id}", single.Text.Header["if"]),
                (SourceTextFolder folder, 3) when folder.Items.FirstOrDefault(i => i.Id == s[2]) is { } item =>
                    new MacroRef(item, $"{folder.Id}.{item.Id}", $"macro.{folder.Id}.{item.Id}", item.Header["if"]),
                _ => null,
            };
        }

        /// <summary>The instruction's lines joined with <c>\n</c> (part 05 § 8.1); <c>null</c> when it is not a list of text (the reader's refusal).</summary>
        private string? InstructionText(SourceText text)
        {
            if (instructions.TryGetValue(text, out var known))
                return known;
            string? joined = null;
            if (text.Header["instruction"] is JsonArray lines && lines.Select(l => l is null ? null : SourceLayout.Text(l)).ToList() is var strings
                && strings.All(l => l is not null))
            {
                joined = string.Join("\n", strings);
            }

            instructions[text] = joined;
            return joined;
        }

        private TemplateSyntax? InstructionSyntax(SourceText text)
        {
            if (InstructionText(text) is not { } joined)
                return null;
            if (!instructionSyntaxes.TryGetValue(text, out var syntax))
                instructionSyntaxes[text] = syntax = TemplateSyntax.Parse(joined);
            return syntax;
        }

        // ---- the placing node -------------------------------------------------------------------------

        /// <summary>
        /// Part 05 § 8.3, for the prompt of <paramref name="leaf"/> placing <paramref name="units"/>: each macro placed
        /// once (S05-048), in a node returning a text format (S05-049) that takes the macro's format (S05-046), each said
        /// once per macro.
        /// </summary>
        private void PlacingNode(IReadOnlyList<PlacedUnit> units, SourceLeaf leaf, TemplateHost host)
        {
            var macros = units.Where(u => u.Instruction is not null).ToList();
            if (macros.Count == 0)
                return;
            var formats = resolution.Tree.FormatsOf(host.Frame);
            var nodeFormat = FormatSet.NameOf(leaf.Header["format"]);
            foreach (var group in macros.GroupBy(u => u.Site, StringComparer.Ordinal))
            {
                var id = group.Key["macro.".Length..];
                if (group.Count() > 1)
                    Refusals.Add(DialectSentences.MacroPlacedTwice(host.Subject, id, host.Path));
                if (nodeFormat is null || !TextFormats.Contains(nodeFormat))
                {
                    Refusals.Add(DialectSentences.MacroInNonTextNode(leaf.Id, id, TypeStrings.Print(leaf.Header["format"], formats), host.Path));
                    continue;
                }

                if (group.First().Instruction!.Host.Declaration is SourceText { Header: var header }
                    && FormatSet.NameOf(header["format"]) is { } format && TextFormats.Contains(format) && !Takes(nodeFormat, format))
                {
                    Refusals.Add(DialectSentences.MacroFormatNotTaken(leaf.Id, id, format, nodeFormat, nodeFormat == "plaintext" ? "plaintext" : "plaintext and markdown_inline", host.Path));
                }
            }
        }

        /// <summary>Part 05 § 8.3's format match: Markdown into <c>plaintext</c> is refused, <c>markdown_block</c> into <c>markdown_inline</c> too; the rest is taken.</summary>
        private static bool Takes(string node, string macro) => node switch
        {
            "plaintext" => macro == "plaintext",
            "markdown_inline" => macro != "markdown_block",
            _ => true,
        };

        // ---- profile-carrying (part 05 § 8.6, part 03 § 4.6) --------------------------------------------

        /// <summary>
        /// S05-064: each read, in a prompt or in what it places (a part's text, a macro's instruction, a snippet), of an
        /// agent whose header declares <c>profileCarrying</c> true and whose prompt places a macro whose body, or a snippet
        /// it places, reads <c>profile.*</c>; that macro (the first such) is named. Any other profile-carrying read is the
        /// derived step's (S03-044).
        /// </summary>
        public void ProfileCarrying(IReadOnlyList<CheckedBody> bodies)
        {
            var carriers = new Dictionary<SourceLeaf, string>(ReferenceEqualityComparer.Instance);
            foreach (var body in bodies)
            {
                if (body.Host is { Site: TemplateSite.Prompt, Declaration: SourceLeaf leaf }
                    && body.Units.FirstOrDefault(u => u.MacroBody is { } macroBody && ReadsProfile(macroBody)) is { } unit)
                {
                    carriers.TryAdd(leaf, unit.Site["macro.".Length..]);
                }
            }

            if (carriers.Count == 0)
                return;
            foreach (var body in bodies.Where(b => b.Host.Site == TemplateSite.Prompt))
            {
                foreach (var reader in body.Units.Select(u => u.Body).Prepend(body).SelectMany(b => b.FolderItems.Prepend(b)))
                {
                    foreach (var read in reader.Reads)
                    {
                        if (read.Head?.Split('.') is ["node", var id]
                            && reader.Host.Frame.Visible("node", id) is SourceLeaf { Kind: "agent" } agent
                            && agent.Header["profileCarrying"] is JsonValue declared && declared.TryGetValue<bool>(out var flag) && flag
                            && carriers.TryGetValue(agent, out var macro))
                        {
                            Refusals.Add(DialectSentences.ReadsProfileCarrying(reader.Host.Subject, read.Written, read.Line, macro, reader.Host.Path));
                        }
                    }
                }
            }
        }

        /// <summary>Whether <paramref name="body"/>, a snippet it places, or a folder item it reads as <c>item.content</c>, reads <c>profile.*</c>.</summary>
        private static bool ReadsProfile(CheckedBody body) =>
            body.Units.Select(u => u.Body).Prepend(body).SelectMany(b => b.FolderItems.Prepend(b)).SelectMany(b => b.Reads)
                .Any(r => r.Head?.StartsWith("profile.", StringComparison.Ordinal) == true);

        // ---- every macro, placed or not ------------------------------------------------------------

        /// <summary>
        /// Every macro of the package and of each function, once (part 05 § 8.7): its instruction's and its body's syntax
        /// faults; then the instruction's markers and placements, and the body's placements, markers and Markdown.
        /// </summary>
        public void Macros()
        {
            var tree = resolution.Tree;
            var scopes = new List<(SourceDefinitions Definitions, SourceFunction? Function, ScopeFrame Frame)> { (package.Definitions, null, tree.PackageRoot) };
            scopes.AddRange(package.Functions.Items.Select(f => (f.Definitions, (SourceFunction?)f, tree.FunctionRoot(f))));
            foreach (var (definitions, function, frame) in scopes)
            {
                foreach (var entry in definitions.Macros.Items)
                {
                    IEnumerable<(SourceText Text, string Name)> macros = entry switch
                    {
                        SourceSingleText single => [(single.Text, single.Id)],
                        SourceTextFolder folder => folder.Items.Select(i => (i, $"{folder.Id}.{i.Id}")),
                        _ => [],
                    };
                    foreach (var (text, name) in macros)
                    {
                        Instruction(text, name, function, frame);
                        MacroBodyRules(text, name, frame);
                    }
                }
            }
        }

        /// <summary>
        /// The instruction of macro <paramref name="name"/>: its syntax faults (a <c>-</c> on a marker is S05-016), or
        /// its own marker exactly once (S05-044) in an allowed form, no other macro's marker (S05-045; S05-011 when it
        /// names no macro) and no <c>macro.*</c> or <c>part.*</c> placement (A07-003).
        /// </summary>
        private void Instruction(SourceText text, string name, SourceFunction? function, ScopeFrame frame)
        {
            if (InstructionSyntax(text) is not { } syntax)
                return;
            var subject = $"Macro '{name}'";
            if (syntax.Faults.Count > 0)
            {
                var host = new TemplateHost(TemplateSite.MacroInstruction, subject, text.Path, text, function, frame, null);
                Refusals.AddRange(new BodyCheck(host, syntax, package, resolution, registry, producers).Faults());
                return;
            }

            var own = MarkerText(name);
            var count = 0;
            var others = new HashSet<string>(StringComparer.Ordinal);
            var loops = syntax.Fors.Select(f => f.Variable).ToHashSet(StringComparer.Ordinal);
            foreach (var output in syntax.Outputs)
            {
                var pure = output is { Path: { }, Pipes.Count: 0, Paths.Count: 1 };
                if (pure && output.Path == own)
                {
                    count++;
                    Refusals.AddRange(BodyCheck.Forms(subject, own, output, text.Path));
                    continue;
                }

                foreach (var path in output.Paths)
                {
                    var s = path.Split('.');
                    if (loops.Contains(s[0]) || s[0] is not ("macro" or "part"))
                        continue;
                    if (pure && s is ["macro", _, _, ..] && s[^1] == "content")
                    {
                        var other = string.Join('.', s[1..^1]);
                        if (LookupMacro($"macro.{other}", function) is null)
                            Refusals.Add(DialectSentences.ReadsWhatIsNotThere(subject, path, output.Tag.Line, SiteReads.AsWritten("macro"), text.Path));
                        else if (others.Add(other))
                            Refusals.Add(DialectSentences.MarkerOfAnother(name, other, text.Path));
                    }
                    else
                    {
                        Refusals.Add(DialectSentences.InstructionPlaces(name, path, text.Path));
                    }
                }
            }

            if (count != 1)
                Refusals.Add(DialectSentences.MarkerCount(name, count, text.Path));
        }

        /// <summary>
        /// The body of macro <paramref name="name"/>: its syntax faults, or no <c>macro.*</c> (S05-042) or <c>part.*</c>
        /// (S05-043) placement, no marker (S05-051) and, for a <c>plaintext</c> macro, no Markdown in its literal text (S05-050).
        /// </summary>
        private void MacroBodyRules(SourceText text, string name, ScopeFrame frame)
        {
            var syntax = SyntaxOf(text);
            if (syntax.Faults.Count > 0)
            {
                var host = new TemplateHost(TemplateSite.MacroBody, $"Macro '{name}'", text.Path, text, frame.Function, frame, null);
                Refusals.AddRange(new BodyCheck(host, syntax, package, resolution, registry, producers).Faults());
                return;
            }

            var loops = syntax.Fors.Select(f => f.Variable).ToHashSet(StringComparer.Ordinal);
            var marker = false;
            foreach (var path in syntax.Outputs.SelectMany(o => o.Paths))
            {
                var s = path.Split('.');
                if (loops.Contains(s[0]))
                    continue;
                var rest = s.Length > 1 ? string.Join('.', s[1..]) : "*";
                if (s[0] == "macro" && s.Length >= 3 && s[^1] == "content")
                    marker = true;
                else if (s[0] == "macro")
                    Refusals.Add(DialectSentences.MacroBodyPlacesMacro(name, rest, text.Path));
                else if (s[0] == "part")
                    Refusals.Add(DialectSentences.MacroBodyPlacesPart(name, rest, text.Path));
            }

            if (marker)
                Refusals.Add(DialectSentences.MarkerInBody(name, text.Path));
            if (FormatSet.NameOf(text.Header["format"]) == "plaintext" && MarkdownLine(syntax) is { } line)
            {
                Refusals.Add(DialectSentences.MacroBodyNotFormat(
                    name, "plaintext", $"writes Markdown at line {line.ToString(CultureInfo.InvariantCulture)}", text.Path));
            }
        }
    }

    /// <summary>The marker of macro <paramref name="name"/> without braces: <c>macro.&lt;name&gt;.content</c> (qualified for a folder macro).</summary>
    private static string MarkerText(string name) => $"macro.{name}.content";

    /// <summary>The outputs of <paramref name="syntax"/> that are macro <paramref name="name"/>'s own marker, alone in their tag (a comment or a raw block holds none).</summary>
    private static List<TemplateOutput> OwnMarkers(TemplateSyntax syntax, string name) =>
        syntax.Outputs.Where(o => o is { Pipes.Count: 0, Paths.Count: 1 } && o.Path == MarkerText(name)).ToList();

    /// <summary>
    /// Part 06 § 6.4, for a <c>plaintext</c> template's own literal text (raw text and raw blocks; never what an output
    /// writes): the first 1-based line that starts, after spaces and tabs, with <c>#</c>, <c>-</c>, <c>*</c> or
    /// <c>&gt;</c>, or holds a <c>**</c> pair; <c>null</c> when none does. A tag that writes nothing (a statement, a
    /// comment, a raw block's braces) stands for nothing on its line.
    /// </summary>
    internal static int? MarkdownLine(TemplateSyntax syntax)
    {
        const char Nothing = '\u0001';
        const char Written = '\0';
        var text = syntax.Text;
        var literal = text.Select(c => c == '\n' ? '\n' : Nothing).ToArray();
        foreach (var output in syntax.Outputs)
        {
            for (var i = Math.Clamp(output.Tag.Start, 0, text.Length); i < Math.Clamp(output.Tag.End, 0, text.Length); i++)
            {
                if (literal[i] != '\n')
                    literal[i] = Written;
            }
        }

        foreach (var token in new Lexer(text))
        {
            if (token.Type is not (TokenType.Raw or TokenType.Escape))
                continue;
            var start = Math.Clamp(token.Start.Offset, 0, text.Length);
            var end = Math.Clamp(token.End.Offset + 1, start, text.Length);
            text.CopyTo(start, literal, start, end - start);
        }

        var lines = new string(literal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Replace(Nothing.ToString(), string.Empty, StringComparison.Ordinal).TrimStart(' ', '\t');
            if (line.Length > 0 && line[0] is '#' or '-' or '*' or '>')
                return i + 1;
            var first = line.IndexOf("**", StringComparison.Ordinal);
            if (first >= 0 && line.IndexOf("**", first + 2, StringComparison.Ordinal) >= 0)
                return i + 1;
        }

        return null;
    }

    private sealed partial class BodyCheck
    {
        /// <summary>
        /// Part 05 § 2.6's table for a tag placing <paramref name="unit"/> (or holding a macro's marker): <c>~}}</c>
        /// (S05-015), <c>{{~</c> inside a line (S05-017) or with text after it (S05-018), inside an <c>if</c> or a
        /// <c>for</c> (A07-009). The <c>-</c> is a fault (S05-016).
        /// </summary>
        internal static IEnumerable<SourceRefusal> Forms(string subject, string unit, TemplateOutput output, string path)
        {
            var tag = output.Tag;
            if (tag.TildeRight)
                yield return DialectSentences.PlacesWithTildeRight(subject, unit, tag.Line, path);
            if (tag.TildeLeft && tag.TextBefore)
                yield return DialectSentences.PlacesBlockInsideALine(subject, unit, tag.Line, path);
            else if (tag.TildeLeft && tag.TextAfter)
                yield return DialectSentences.TextAfterBlock(subject, unit, tag.Line, path);
            if (output.Enclosing.Count > 0)
                yield return DialectSentences.PlacesInsideAnIfOrFor(subject, unit, tag.Line, output.Enclosing[^1] == "for" ? "a for" : "an if", path);
        }

        /// <summary>
        /// Whether <paramref name="path"/> of <paramref name="output"/> is a macro's to judge rather than a read to type:
        /// in a macro's instruction or body any <c>macro.*</c> or <c>part.*</c> (its own marker is no read, D17; the rest
        /// is said once per macro by <see cref="Context.Macros"/>); in a <c>rendered</c> node or a deliverable a
        /// <c>macro.*</c> placement, said here (S05-047).
        /// </summary>
        private bool MacroPath(TemplateOutput output, string path, List<ForVariable> scope)
        {
            var s = path.Split('.');
            if (scope.Any(v => v.Name == s[0]))
                return false;
            switch (host.Site)
            {
                case TemplateSite.MacroInstruction or TemplateSite.MacroBody:
                    return s[0] is "macro" or "part";
                case TemplateSite.Rendered or TemplateSite.Deliverable
                    when output is { Pipes.Count: 0, Paths.Count: 1 } && output.Path == path && s is ["macro", _] or ["macro", _, not "content"]:
                    Refusals.Add(DialectSentences.OnlyAgentsPlaceMacros(host.Subject, path["macro.".Length..], host.Path));
                    return true;
                default:
                    return false;
            }
        }
    }
}
