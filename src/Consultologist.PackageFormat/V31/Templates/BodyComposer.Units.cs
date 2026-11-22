using System.Globalization;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.6, 3.2, 6.3, 7.2; D2, D3): the units a body places. Before anything is
/// rendered, the body and every unit its tags place are planned in order of appearance, each unit's text where it is
/// placed (a snippet's body, the function's own snippet before the package's; a part's lines; a macro's instruction),
/// which numbers the renderer steps as the entry's <c>renders</c> does. At run time a placement tag (<c>snippet.….content</c>,
/// <c>part.&lt;id&gt;</c>, <c>macro.…</c>, its whole output) splices its unit's rendered text when its site is placed and
/// nothing when not. A macro is offered when its site, its folder's and the units that place it are placed: its
/// instruction is spliced, its own marker written as the marker to copy (D17), and its body is rendered in the same data
/// for the reply (part 05 §§ 3.2, 8.3, 8.6; D5, D14).
/// </summary>
public static partial class BodyComposer
{
    internal sealed partial class Composing
    {
        private Dictionary<int, string>? renderSources;

        /// <summary>
        /// <paramref name="text"/> (the text of <paramref name="site"/>, <c>null</c> for the body) planned at its place,
        /// <paramref name="depth"/> units deep: each placement with its unit planned in turn, each renderer step numbered for
        /// the entry; <c>null</c> when it or a unit it places is not what publish wrote (absent, not a dialect template, or
        /// nested or numerous past any publish bound).
        /// </summary>
        public TextPlan? Plan(string text, string? site, int depth)
        {
            if (depth > UnitDepth || ++planned > SourceTemplates.UnitLimit + 1 || Parse(text) is not { } read)
                return null;

            var plan = new TextPlan(site, read.Template, read.Syntax);
            foreach (var statement in Outputs(read.Template.Page!))
            {
                if (statement.Expression is not { } written)
                    continue;
                var expression = DialectRules.Unwrap(written);
                if (site != null && site.StartsWith("macro.", StringComparison.Ordinal) && Chain(expression) == site + ".content")
                {
                    plan.Markers[Offset(statement)] = MarkerLiteral(read.Syntax, statement, site);
                    continue;
                }

                if (PlacementOf(expression) is { } unit)
                {
                    if (UnitText(unit.Kind, unit.Site) is not { } unitText || Plan(unitText, unit.Site, depth + 1) is not { } child)
                        return null;
                    plan.Placements[Offset(expression)] = new Placement(unit.Site, unit.Kind, child);
                    continue;
                }

                var spot = SpotOf(plan, statement);
                if (spot != null)
                    plan.Spots[Offset(statement)] = spot;
                string? before = null;
                foreach (var (offset, renderer, head) in RendererSteps(expression))
                {
                    var n = ++Numbered;
                    var source = RenderSource(n) ?? before ?? head;
                    plan.Renders[offset] = new RenderStep(n, renderer, source, spot != null);
                    before = string.Create(CultureInfo.InvariantCulture, $"render:{n}");
                }
            }

            return plan;
        }

        /// <summary>
        /// D17: what the macro <paramref name="site"/>'s own marker writes in its instruction: the marker as § 8.6 reads it
        /// (<c>{{~ macro.x.content }}</c> for a block, <c>{{ macro.x.content }}</c> inline), and for a block marker alone on
        /// its line the indentation before it, which the tag's <c>~</c> would take.
        /// </summary>
        private static string MarkerLiteral(TemplateSyntax syntax, ScriptExpressionStatement statement, string site)
        {
            var offset = Offset(statement);
            var tag = syntax.Tags.FirstOrDefault(t => t.Start <= offset && offset < t.End);
            var block = tag is { TildeLeft: true, AloneOnLine: true };
            var indentation = string.Empty;
            if (block)
            {
                var start = Math.Clamp(tag!.Start, 0, syntax.Text.Length);
                var lineStart = start == 0 ? 0 : syntax.Text.LastIndexOf('\n', start - 1) + 1;
                indentation = syntax.Text[lineStart..start];
            }

            return indentation + MacroMarkers.Open(new MacroMarker(site["macro.".Length..], site + ".content", block));
        }

        /// <summary>
        /// The macro <paramref name="placement"/> places, offered when its site and its folder's (<c>macro-folder.&lt;id&gt;</c>,
        /// for a folder's macro) are placed (the units around it are, or this text would not be rendering): its instruction
        /// rendered where it stands, and its body (less its file's last line ending) rendered in the same data (<c>profile.*</c> readable, a <c>run.*</c> read
        /// filled directly, D5) and escaped as plain text when a <c>plaintext</c> macro stands in a Markdown node (D14),
        /// recorded as an <see cref="OfferedMacro"/>; <c>null</c>, which writes nothing, when it is not offered. A macro the
        /// plan lacks, with no body, or with no <c>form</c> in the entry's <c>placements</c> is not what publish wrote.
        /// </summary>
        private PlacedText? PlaceMacro(Placement placement)
        {
            var site = placement.Site;
            var segments = site.Split('.');
            if (!Request.Placed.TryGetValue(site, out var placed) || !placed
                || (segments.Length == 3 && (!Request.Placed.TryGetValue($"macro-folder.{segments[1]}", out var folder) || !folder)))
            {
                return null;
            }

            var macro = Macro(site);
            if (SourceLayout.Text(macro?["body"]) is not { } body || Form(site) is not { } form)
                throw Stopped(new ComposeStop(NotPublished));

            var instruction = Render(placement.Text, Globals(), out var spots);

            // The file's last line ending is not the body's (it would end a line mid-sentence): dropped from the source,
            // so a line ending a value writes at the end stays.
            if (Plan(body.EndsWith('\n') ? body[..^1] : body, site + ".content", 1) is not { } bodyPlan)
                throw Stopped(new ComposeStop(NotPublished));
            var text = Render(bodyPlan, Globals());
            if (RunPlan.Formats.TextFormat(macro!["format"]) == "plaintext" && RunPlan.Formats.TextFormat(Entry.Format) is "markdown_block" or "markdown_inline")
                text = MarkdownEscape.Escape(text);

            Offered.Add(new OfferedMacro(site, site + ".content", form == "block", text, CanonicalJson.HashText(text), ProfileReads(bodyPlan)));
            return new PlacedText(instruction, spots);
        }

        /// <summary>The <c>form</c> the entry's <c>placements</c> gives the macro <paramref name="site"/> (<c>block</c> or <c>inline</c>), or <c>null</c>.</summary>
        private string? Form(string site) =>
            (Entry.Json["placements"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(p => SourceLayout.Text(p["site"]) == site)
                .Select(p => SourceLayout.Text(p["form"]))
                .FirstOrDefault(f => f is "block" or "inline");

        /// <summary>
        /// Each <c>profile.&lt;field&gt;</c> <paramref name="body"/> (or a snippet it places, when placed) writes, once, in order, with the hash
        /// of the value the data holds for it (part 08 § 9): of its text, or of its canonical JSON when it is not text.
        /// </summary>
        private IReadOnlyList<ProfileRead> ProfileReads(TextPlan body)
        {
            var fields = new List<string>();
            var pending = new Stack<TextPlan>();
            pending.Push(body);
            while (pending.TryPop(out var text))
            {
                foreach (var path in text.Syntax.Outputs.SelectMany(o => o.Paths))
                {
                    if (path.Split('.') is ["profile", var field, ..] && !fields.Contains(field, StringComparer.Ordinal))
                        fields.Add(field);
                }

                foreach (var child in text.Placements.Values.Reverse())
                {
                    if (Request.Placed.TryGetValue(child.Site, out var placed) && placed)
                        pending.Push(child.Text); // a snippet not placed wrote nothing, so read nothing
                }
            }

            return [.. fields.Select(field => new ProfileRead(field, Json($"profile.{field}") is JsonValue value && value.TryGetValue<string>(out var written)
                ? CanonicalJson.HashText(written)
                : CanonicalJson.Hash(Json($"profile.{field}"))))];
        }

        /// <summary>A renderer's body planned for a pipe into it (nothing placed, nothing numbered), or <c>null</c> when it is not what publish wrote.</summary>
        public TextPlan? RendererPlan(string body, string site)
        {
            if (Parse(body) is not { } read)
                return null;
            return new TextPlan(site, read.Template, read.Syntax);
        }

        /// <summary>The <c>source</c> the entry's <c>renders</c> gives step <paramref name="n"/>, or <c>null</c>.</summary>
        private string? RenderSource(int n)
        {
            if (renderSources == null)
            {
                renderSources = [];
                foreach (var render in (Entry.Json["renders"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (render["n"] is JsonValue number && number.TryGetValue<int>(out var at) && SourceLayout.Text(render["source"]) is { } source)
                        renderSources.TryAdd(at, source);
                }
            }

            return renderSources.GetValueOrDefault(n);
        }

        /// <summary>The text of the unit <paramref name="site"/> of <paramref name="kind"/>: a snippet's body, a part's lines or a macro's instruction joined with <c>\n</c>; <c>null</c> when absent.</summary>
        private string? UnitText(string kind, string site) => kind switch
        {
            "snippet" => SourceLayout.Text(Snippet(site)?["body"]),
            "part" => Lines((Entry.Json["parts"] as JsonObject)?[site["part.".Length..]]?["text"]),
            _ => Lines(Macro(site)?["instruction"]),
        };

        private static string? Lines(JsonNode? lines)
        {
            if (lines is not JsonArray list)
                return null;
            var texts = list.Select(SourceLayout.Text).ToList();
            return texts.All(t => t != null) ? string.Join("\n", texts) : null;
        }

        /// <summary>The snippet or snippet folder <paramref name="key"/> (<c>snippet.…</c>): the function's own, else the package's.</summary>
        public JsonObject? Snippet(string key) =>
            (function?.Json["snippets"] as JsonObject)?[key] as JsonObject ?? RunPlan.Snippets.GetValueOrDefault(key);

        /// <summary>The macro or macro folder <paramref name="key"/> (<c>macro.…</c>): the function's own, else the package's.</summary>
        public JsonObject? Macro(string key) =>
            (function?.Json["macros"] as JsonObject)?[key] as JsonObject ?? (RunPlan.Document["macros"] as JsonObject)?[key] as JsonObject;

        /// <summary>Whether a macro or macro folder is keyed under <paramref name="prefix"/> (<c>macro.f</c> for <c>macro.f.m</c>).</summary>
        public bool HasMacrosUnder(string prefix) =>
            new[] { function?.Json["macros"] as JsonObject, RunPlan.Document["macros"] as JsonObject }
                .Any(set => set != null && set.Any(m => m.Key.StartsWith(prefix + ".", StringComparison.Ordinal)));

        /// <summary>
        /// The header value of the snippet folder item <paramref name="key"/> (<c>snippet.&lt;folder&gt;.&lt;id&gt;</c>): the
        /// members its folder's format declares, as a loop over the folder gives <c>item.value</c>; <c>null</c> when it is no
        /// folder item.
        /// </summary>
        public object? FolderItemValue(string key)
        {
            var segments = key.Split('.');
            if (segments.Length != 3 || Snippet(key) is not { } item || Snippet($"snippet.{segments[1]}") is not { } folder || folder["items"] is not JsonArray)
                return null;
            var value = new JsonObject();
            var fields = (RunPlan.Formats.Expand(folder["format"]) as JsonObject)?["properties"] as JsonObject;
            foreach (var (field, _) in fields ?? [])
            {
                if (item.TryGetPropertyValue(field, out var data))
                    value[field] = data?.DeepClone();
            }

            return TemplateValues.FromJson(value);
        }
    }

    /// <summary>The source offset Scriban reports for <paramref name="node"/>: of a placement's path, of a pipe step, of a statement.</summary>
    internal static int Offset(ScriptNode node) => Math.Max(node.Span.Start.Offset, 0);

    /// <summary>Every output statement of <paramref name="page"/>, in source order (walked without recursion).</summary>
    private static IEnumerable<ScriptExpressionStatement> Outputs(ScriptNode page)
    {
        var pending = new Stack<ScriptNode>();
        pending.Push(page);
        while (pending.TryPop(out var node))
        {
            if (node is ScriptExpressionStatement statement)
            {
                yield return statement;
                continue;
            }

            foreach (var child in node.Children.Reverse())
            {
                if (child != null)
                    pending.Push(child);
            }
        }
    }

    /// <summary>
    /// The unit an output's whole expression places (part 05 § 2.6): <c>snippet.&lt;id&gt;.content</c> or
    /// <c>snippet.&lt;folder&gt;.&lt;id&gt;.content</c> (site <c>snippet.…</c> without <c>.content</c>), <c>part.&lt;id&gt;</c>,
    /// <c>macro.&lt;id&gt;</c> or <c>macro.&lt;folder&gt;.&lt;id&gt;</c>. A macro's marker (<c>macro.….content</c>) is no placement.
    /// </summary>
    private static (string Site, string Kind)? PlacementOf(ScriptExpression expression)
    {
        if (Chain(expression) is not { } chain)
            return null;
        var s = chain.Split('.');
        return s switch
        {
            ["snippet", _, "content"] or ["snippet", _, _, "content"] => (chain[..^".content".Length], "snippet"),
            ["part", _] => (chain, "part"),
            ["macro", _] or ["macro", _, not "content"] => (chain, "macro"),
            _ => null,
        };
    }

    /// <summary>Each pipe step of <paramref name="expression"/> into a renderer, in pipe order: its offset, the renderer's id, and the head piped in.</summary>
    private static IEnumerable<(int Offset, string Renderer, string Head)> RendererSteps(ScriptExpression expression)
    {
        if (expression is not ScriptPipeCall)
            yield break;
        var (head, steps) = Decompose(expression);
        var written = head == null ? string.Empty : Chain(head) ?? head.ToString() ?? string.Empty;
        foreach (var step in steps)
        {
            var target = step is ScriptFunctionCall { Target: { } called } ? called : step;
            if (Chain(target) is { } chain && chain.StartsWith(DialectRules.RendererRoot + ".", StringComparison.Ordinal))
                yield return (Offset(step), chain[(DialectRules.RendererRoot.Length + 1)..], written);
        }
    }

    /// <summary>A pipeline's head and its steps in order, parentheses taken off (Scriban reads <c>x | f | g</c> as a pipe from <c>x</c> to a pipe from <c>f</c> to <c>g</c>).</summary>
    private static (ScriptExpression? Head, List<ScriptExpression> Steps) Decompose(ScriptExpression? expression)
    {
        if (expression != null)
            expression = DialectRules.Unwrap(expression);
        if (expression is not ScriptPipeCall pipe)
            return (expression, []);
        var (head, steps) = Decompose(pipe.From);
        Steps(pipe.To, steps);
        return (head, steps);
    }

    private static void Steps(ScriptExpression? to, List<ScriptExpression> steps)
    {
        if (to != null)
            to = DialectRules.Unwrap(to);
        if (to is ScriptPipeCall next)
        {
            Steps(next.From, steps);
            Steps(next.To, steps);
        }
        else if (to != null)
        {
            steps.Add(to);
        }
    }

    /// <summary>The access path an expression is, dotted as written (<c>node.e.value</c>), or <c>null</c>.</summary>
    private static string? Chain(ScriptExpression? expression)
    {
        var members = new List<string>();
        while (expression is ScriptMemberExpression { Member: { } member } access)
        {
            members.Add(member.Name);
            expression = access.Target;
        }

        if (expression is not ScriptVariable variable)
            return null;
        members.Add(variable.Name);
        members.Reverse();
        return string.Join('.', members);
    }

    /// <summary>
    /// <c>snippet</c> and what is under it: a snippet, a folder or a folder item by its key, a placed one's text at its
    /// placement (<c>.content</c>), a folder item's header (<c>.value</c>). A key no snippet has fails the render. A
    /// snippet's <c>.content</c> read anywhere but at a placement of it reads <c>null</c>: only a placement is decided.
    /// </summary>
    internal sealed class SnippetRoot(Composing composing, string? key) : ScriptObject
    {
        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (context is not ComposeContext composer)
                throw composing.Stopped(new ComposeStop(null));
            if (key != null && member == "content" && SourceLayout.Text(composing.Snippet(key)?["body"]) != null)
            {
                value = composer.CurrentNode is { } node && composer.Text.Placements.TryGetValue(Offset(node), out var placement) && placement.Site == key
                    ? composing.Place(placement)
                    : null;
                return true;
            }

            if (key != null && member == "value" && composing.FolderItemValue(key) is { } header)
            {
                value = header;
                return true;
            }

            var under = key == null ? $"snippet.{member}" : $"{key}.{member}";
            if (composing.Snippet(under) == null)
                throw composer.Stopped(null);
            value = new SnippetRoot(composing, under);
            return true;
        }
    }

    /// <summary><c>part</c>: <c>part.&lt;id&gt;</c> of the entry's own parts is its text where placed and the part is; nothing otherwise. A part the entry lacks fails the render.</summary>
    internal sealed class PartRoot(Composing composing) : ScriptObject
    {
        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (context is not ComposeContext composer || (composing.Entry.Json["parts"] as JsonObject)?.ContainsKey(member) != true)
                throw composing.Stopped(new ComposeStop(null));
            var site = $"part.{member}";
            value = composer.CurrentNode is { } node && composer.Text.Placements.TryGetValue(Offset(node), out var placement) && placement.Site == site
                ? composing.Place(placement)
                : null;
            return true;
        }
    }

    /// <summary>
    /// <c>macro</c>: a macro (or a folder's, under its folder) at a placement of it is its instruction when it is offered
    /// (<see cref="Composing.Place"/>), nothing otherwise; read anywhere else it reads <c>null</c>. A key no macro has fails
    /// the render.
    /// </summary>
    internal sealed class MacroRoot(Composing composing, string? key) : ScriptObject
    {
        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (context is not ComposeContext composer)
                throw composing.Stopped(new ComposeStop(null));
            var under = key == null ? $"macro.{member}" : $"{key}.{member}";
            if (composing.HasMacrosUnder(under))
            {
                value = new MacroRoot(composing, under);
                return true;
            }

            if (composing.Macro(under) == null)
                throw composing.Stopped(new ComposeStop(null));
            value = composer.CurrentNode is { } node && composer.Text.Placements.TryGetValue(Offset(node), out var placement) && placement.Site == under
                ? composing.Place(placement)
                : null;
            return true;
        }
    }
}
