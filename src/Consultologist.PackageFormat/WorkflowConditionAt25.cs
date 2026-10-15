using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1037 (design § 16): a <c>when</c> at 25 is a Scriban boolean expression over
/// the objects a template reads — <c>node.scope.value == "in_scope"</c>,
/// <c>input.kind == "new"</c>, <c>item.id == "hpi"</c>, <c>(input.docs | array.size) > 0</c>,
/// joined by <c>&amp;&amp;</c>, <c>||</c>, <c>!</c> — walked by the template walker in
/// condition mode (its reads are the condition's edges, its comparisons typed)
/// and evaluated to a boolean. The ≤24 grammar (<c>node:scope == in_scope</c>) is
/// refused at 25 by name with the spelling in the sentence.
/// </summary>
public static class WorkflowConditionAt25
{
    /// <summary>A condition walked: what it reads and the sentences that refuse it, and the parsed expression when it parsed.</summary>
    public sealed record ConditionWalk(TemplateReferences References, ScriptExpression? Expression)
    {
        public bool IsValid => References.IsValid && Expression is not null;
    }

    private static readonly LexerOptions ScriptOnly = new() { Mode = ScriptMode.ScriptOnly };

    /// <summary>
    /// Parses and walks one condition. <paramref name="subject"/> opens every
    /// sentence ("Node 'assessment' condition", "Deliverable 'consult' macro 'closing'
    /// condition"); <paramref name="inputTypeOf"/> types the literals of the
    /// ≤24-grammar hint.
    /// </summary>
    public static ConditionWalk Walk(string subject, string? when, TemplateReferenceContext context, Func<string, string?>? inputTypeOf = null)
    {
        var text = when?.Trim() ?? string.Empty;
        var collector = new TemplateReferenceCollector();

        if (text.Length == 0)
        {
            collector.Errors.Add($"{subject} is blank.");
            return new ConditionWalk(collector.ToReferences(), null);
        }

        if (text.Contains("{{", StringComparison.Ordinal) || text.Contains("{%", StringComparison.Ordinal))
        {
            collector.Errors.Add($"{subject} is a template, not a condition; write the expression bare (node.<id>.value == \"<value>\").");
            return new ConditionWalk(collector.ToReferences(), null);
        }

        if (!TryParse(text, out var expression, out var parseError))
        {
            collector.Errors.Add(LegacyHint(subject, text, inputTypeOf) ?? $"{subject} {parseError}");
            return new ConditionWalk(collector.ToReferences(), null);
        }

        var references = TemplateReferenceWalker.WalkExpression(subject, expression!, context);

        if (!references.IsValid && LegacyHint(subject, text, inputTypeOf) is { } hint)
        {
            return new ConditionWalk(references with { Errors = new[] { hint } }, null);
        }

        return new ConditionWalk(references, references.IsValid ? expression : null);
    }

    /// <summary>Parses one bare expression; false with the sentence's tail when the text is not one.</summary>
    public static bool TryParse(string text, out ScriptExpression? expression, out string? error)
    {
        expression = null;
        error = null;

        var template = Template.Parse(text, lexerOptions: ScriptOnly);

        if (template.HasErrors)
        {
            var message = template.Messages[0];
            error = $"does not parse: {message.Message}";
            return false;
        }

        var statements = template.Page?.Body?.Statements;

        if (statements is not { Count: 1 } || statements[0] is not ScriptExpressionStatement statement || statement.Expression is null)
        {
            error = statements is { Count: 0 } ? "is blank." : "is not one expression; a condition is one boolean expression.";
            return false;
        }

        expression = statement.Expression;
        return true;
    }

    /// <summary>Evaluates a parsed condition in a context whose globals are pushed; the value when it is a boolean, null otherwise.</summary>
    public static bool? Evaluate(ScriptExpression expression, TemplateContext context) =>
        context.Evaluate(expression) is bool value ? value : null;

    /// <summary>
    /// #1038: whether a condition holds over a render scope — the engine's judge
    /// at every site (a node's own when and a deliverable's at the boundary, a
    /// placement's there too, a node instance's at completion, a folder item's at
    /// start). The scope is exactly what a template reads, so absence is what
    /// <c>{{ if }}</c> sees: an unsupplied optional text or enum input is "", a
    /// number, date or boolean null, an array empty (and falsy), an object empty
    /// (design § 16, 2026-10-15). The validator proved the text parses and yields a
    /// boolean over the probe; here either failure is loud, with the subject.
    /// </summary>
    public static bool Holds(string subject, string when, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        if (!TryParse(when.Trim(), out var expression, out var error) || expression is null)
        {
            throw new InvalidOperationException($"{subject} {error}");
        }

        var context = TemplateRenderer.ContextFor(subject, scope, pipes);
        var value = TemplateRenderer.Unwrap(subject, "evaluate", () => context.Evaluate(expression));

        return value is bool holds
            ? holds
            : throw new InvalidOperationException($"{subject} must be a boolean expression; it yields {DescribeValue(value)}.");
    }

    /// <summary>
    /// #1038 (provenance@v2026.10.2): the reason a 25 condition gives when it does
    /// not hold — one fixed form over the condition's own text, package-authored,
    /// never a value: a 25 when may read a node's text or a snippet, so nothing
    /// it compared is printable.
    /// </summary>
    public static string Reason(string when) => $"needs `{when.Trim()}`; it does not hold";

    /// <summary>"a text", "a number", "a list", "nothing" — what a non-boolean condition yielded.</summary>
    public static string DescribeValue(object? value) => value switch
    {
        null => "nothing",
        string => "a text",
        bool => "a boolean",
        int or long or double or decimal or float => "a number",
        DateTime => "a date",
        Scriban.Runtime.ScriptArray => "a list",
        Scriban.Runtime.ScriptObject => "an object",
        _ => "a value"
    };

    /// <summary>The sentence for a condition written in the ≤24 grammar, with the 25 spelling — or null when the text is not that grammar.</summary>
    private static string? LegacyHint(string subject, string text, Func<string, string?>? inputTypeOf)
    {
        // The object spelling is never the ≤24 grammar, whatever the ≤24 parser
        // makes of it (input.kind is a path to it); its sentences are the walk's.
        if (ConditionScriban.LooksLikeObjectSpelling(text)
            || !WorkflowResultConditions.TryParseLegacy(text, out var legacy, out _) || legacy is null)
        {
            return null;
        }

        return $"{subject} '{text}' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write {ConditionScriban.Spell(legacy, inputTypeOf)}.";
    }
}
