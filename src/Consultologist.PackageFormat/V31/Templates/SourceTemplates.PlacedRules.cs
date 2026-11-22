using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.3 R-R3, 8.2; part 04 § 7.10): a placed unit's header <c>if</c> is
/// judged where it is placed. A snippet's or a macro's <c>if</c> (a macro folder's shared one too) is checked as a
/// condition at each placer's frame, through nested placements, by the expression checker
/// (<see cref="ExpressionChecker.Check"/>) in the scope of that place (<see cref="ResolvedScope"/>): a snippet's read the
/// place does not give is S05-022, naming the snippet and the place; a macro's is S04-063 when a function has no node it
/// reads, and the checker's sentence otherwise (S03-014). So the rule <c>placements</c> roots for the entry reads only
/// what its place has. What it reads is a dependency of the placing node at run time (<c>RunPlan</c>), so the derived
/// step counts it among that node's edges for circles (<c>SourceDerived.Circles</c>): a unit's <c>if</c> reading its
/// placer, or a node that reads its placer, is A05-003 there, worded as every circle is. Together, a package publish
/// accepts builds a <c>RunPlan</c> as far as its placement rules go.
/// </summary>
public static partial class SourceTemplates
{
    private sealed partial class Context
    {
        /// <summary>The <c>if</c> of the snippet <paramref name="snippet"/>, placed in <paramref name="body"/> at <paramref name="placement"/>, at that place.</summary>
        private void SnippetRule(SnippetRef snippet, CheckedBody body, Placement placement)
        {
            if (Ruled(snippet.If) is not { } rule)
                return;
            var function = body.Host.Function;
            var own = function != null && OwnEntry(function.Definitions.Snippets.Items, snippet.Text) != null;
            var subject = $"Snippet '{snippet.Name}'{(own ? $" in function '{function!.Id}'" : "")}";
            var check = AtPlace(rule, ExpressionSiteKind.SnippetIf, subject, snippet.Text.Path, body);
            if (check.Count == 0)
                return;

            var unavailable = JsonLogicShape.Paths(rule).Where(p => !Available(p, body)).Distinct(StringComparer.Ordinal).ToList();
            if (unavailable.Count == 0)
            {
                Refusals.AddRange(check);
                return;
            }

            foreach (var path in unavailable)
                Refusals.Add(DialectSentences.SnippetReadsUnavailable(snippet.Name, path, body.Host.Subject, placement.Line, body.Host.Path));
        }

        /// <summary>The <c>if</c> of the macro <paramref name="macro"/>, and of its folder for a folder's macro, placed in <paramref name="body"/>, at that place.</summary>
        private void MacroRules(MacroRef macro, CheckedBody body)
        {
            var function = body.Host.Function;
            var id = macro.Site.Split('.')[1];
            var entry = function?.Definitions.Macros.Items.FirstOrDefault(e => e.Id == id) ?? package.Definitions.Macros.Items.FirstOrDefault(e => e.Id == id);
            var own = function != null && function.Definitions.Macros.Items.Any(e => ReferenceEquals(e, entry));
            var suffix = own ? $" in function '{function!.Id}'" : "";
            var rules = new List<(string Name, JsonNode? Rule, string Path)>();
            if (entry is SourceTextFolder folder)
                rules.Add((folder.Id, folder.Index["if"], SourceLayout.Join(folder.Path, "index.json")));
            rules.Add((macro.Name, macro.If, macro.Text.Path));

            foreach (var (name, written, path) in rules)
            {
                if (Ruled(written) is not { } rule)
                    continue;
                var check = AtPlace(rule, ExpressionSiteKind.MacroIf, $"Macro '{name}'{suffix}", path, body);
                if (check.Count == 0)
                    continue;

                // Part 04 § 7.10: inside a function, a node the function has none of is said in the function's words.
                var missing = function == null ? [] : JsonLogicShape.Paths(rule)
                    .Select(p => p.Split('.'))
                    .Where(s => s is ["node", _, ..] && body.Host.Frame.Visible("node", s[1]) == null)
                    .Select(s => s[1])
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (missing.Count == 0)
                {
                    Refusals.AddRange(check);
                    continue;
                }

                foreach (var node in missing)
                    Refusals.Add(ResolveSentences.PlacedMacroReadsNothing(macro.Name, function!.Id, node, body.Host.Path));
            }
        }

        /// <summary>The refusals of <paramref name="rule"/>, a unit's <c>if</c>, checked as a condition where <paramref name="body"/> stands.</summary>
        private List<SourceRefusal> AtPlace(JsonNode rule, ExpressionSiteKind kind, string subject, string path, CheckedBody body)
        {
            var frame = body.Host.Frame;
            var site = new ExpressionSite(kind, subject, "if", path, null, null);
            var scope = new ResolvedScope(resolution.Tree, frame, subject, path, body.Host.Declaration as SourceNode);
            return [.. ExpressionChecker.Check(rule, site, scope, resolution.Tree.FormatsOf(frame)).Refusals];
        }

        /// <summary>Whether <paramref name="path"/>, read by a condition, names something where <paramref name="body"/> stands.</summary>
        private bool Available(string path, CheckedBody body)
        {
            if (ReferenceString.Parse(path) is not { } reference)
                return false;
            var read = resolution.Tree.Resolve(reference, body.Host.Frame, body.Host.Subject, body.Host.Path, condition: true, body.Host.Declaration as SourceNode);
            return read.Refusal == null && read.Kind != ScopeHeadKind.Unknown;
        }

        /// <summary>The entry of <paramref name="entries"/> that holds <paramref name="text"/> (the single text, or a folder holding it).</summary>
        private static SourceTextEntry? OwnEntry(IReadOnlyList<SourceTextEntry> entries, SourceText text) =>
            entries.FirstOrDefault(e => SourceResolve.Texts(e).Any(t => ReferenceEquals(t, text)));

        /// <summary><paramref name="rule"/>, or <c>null</c> for none (absent or JSON <c>null</c>).</summary>
        private static JsonNode? Ruled(JsonNode? rule) =>
            rule is null || rule.GetValueKind() == System.Text.Json.JsonValueKind.Null ? null : rule;
    }
}
