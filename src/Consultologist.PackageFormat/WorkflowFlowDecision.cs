using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1154 (package-format-v26-design.md § 1.1, § 1.5): an <c>if</c> or <c>case</c>
/// node decided — the body it takes and the branch the record names. The
/// judge is the 25 condition's (<see cref="WorkflowConditionAt25.Holds"/>) for
/// an <c>if</c>; a <c>case</c> evaluates its subject over the same render scope
/// and takes the first branch whose <c>when</c> lists the value, else the
/// <c>else</c> body, else none. The engine decides where a 25 <c>when</c> is
/// decided: at start when the expression reads inputs and run facts only, at
/// the boundary when it reads a node's value or the profile. A loop is never
/// decided; asking is an engine fault.
/// </summary>
internal static class WorkflowFlowDecision
{
    /// <summary>The record's word for a flow node that took no branch.</summary>
    public const string None = "none";

    /// <summary>The record's word for the else body taken.</summary>
    public const string Else = "else";

    /// <summary>The body taken (null when none) and the branch the record names: "then" | "else" | "&lt;v1, v2&gt;" | "none".</summary>
    public sealed record Outcome(WorkflowFlowNodes.Body? Taken, string Branch)
    {
        public bool Skipped => Taken is null;
    }

    public static Outcome Decide(WorkflowFlowNodes.Flow flow, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        var subject = $"Node '{flow.Node.Id}' {(flow.Kind == WorkflowFlowNodes.Kind.If ? "if" : "case")}";

        switch (flow.Kind)
        {
            case WorkflowFlowNodes.Kind.For:
                throw new InvalidOperationException($"Loop '{flow.Node.Id}' is not decided; a loop runs over its collection.");

            case WorkflowFlowNodes.Kind.If:
            {
                var holds = WorkflowConditionAt25.Holds(subject, flow.Expression ?? string.Empty, scope, pipes);
                var taken = holds
                    ? flow.Bodies.FirstOrDefault(b => b.Label == "then")
                    : flow.Bodies.FirstOrDefault(b => b.Label == Else);
                return new Outcome(taken, taken is null ? None : BranchLabel(taken));
            }

            default:
            {
                var value = Subject(subject, flow.Expression ?? string.Empty, scope, pipes);
                var taken = flow.Bodies.FirstOrDefault(b => b.When is { } when && when.Contains(value, StringComparer.Ordinal))
                    ?? flow.Bodies.FirstOrDefault(b => b.When is null);
                return new Outcome(taken, taken is null ? None : BranchLabel(taken));
            }
        }
    }

    /// <summary>"then" / "else" for an if's bodies; the values listed, comma-joined, for a case branch; "else" for its else.</summary>
    public static string BranchLabel(WorkflowFlowNodes.Body body) =>
        body.When is { Count: > 0 } when ? string.Join(", ", when) : body.Label;

    /// <summary>The case subject evaluated over the scope — a declared value, as text; loud with the subject on anything else.</summary>
    private static string Subject(string subject, string expression, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes)
    {
        if (!WorkflowConditionAt25.TryParse(expression.Trim(), out var parsed, out var error) || parsed is null)
        {
            throw new InvalidOperationException($"{subject} {error}");
        }

        var context = TemplateRenderer.ContextFor(subject, scope, pipes);
        var value = TemplateRenderer.Unwrap(subject, "evaluate", () => context.Evaluate(parsed));

        return value is string text
            ? text
            : throw new InvalidOperationException($"{subject} must yield a declared value; it yields {WorkflowConditionAt25.DescribeValue(value)}.");
    }
}
