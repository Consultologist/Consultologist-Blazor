using Consultologist.PackageFormat;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// v10 step (g) (#498): what the editor's authoring surfaces ask of a
/// condition — which input, field or value it reads, and a rename — answered
/// over the format's own parse rather than the text. Replaces the text
/// reader the editor kept while it authored one clause.
/// </summary>
public static class ConditionText
{
    public static WorkflowConditionExpression? Parse(string? when) =>
        !string.IsNullOrWhiteSpace(when) && WorkflowResultConditions.TryParseExpression(when, out var expression, out _) ? expression : null;

    /// <summary>Every operand in the expression, including those inside arithmetic terms.</summary>
    public static IEnumerable<WorkflowResultCondition> Operands(string? when) =>
        Parse(when)?.Leaves.SelectMany(leaf => leaf.IsArithmetic
            ? (leaf.Left?.Operands ?? Enumerable.Empty<WorkflowResultCondition>()).Concat(leaf.Right?.Operands ?? Enumerable.Empty<WorkflowResultCondition>())
            : new[] { leaf })
        ?? Enumerable.Empty<WorkflowResultCondition>();

    public static bool ReadsInput(string? when, string inputId) =>
        Operands(when).Any(operand => !operand.IsNodeValue && operand.InputId == inputId);

    /// <summary>Reads the input's top-level field, bare or by a longer path beneath it.</summary>
    public static bool ReadsField(string? when, string inputId, string fieldId) =>
        Operands(when).Any(operand => !operand.IsNodeValue && operand.InputId == inputId && operand.Segments.Count > 0 && operand.Segments[0] == fieldId);

    public static bool ReadsNode(string? when, string nodeId) =>
        Operands(when).Any(operand => operand.IsNodeValue && operand.NodeId == nodeId);

    /// <summary>Compares the input (or its top-level field) to the literal, in any clause.</summary>
    public static bool TestsValue(string? when, string inputId, string? fieldId, string value) =>
        Parse(when)?.Leaves.Any(leaf => !leaf.IsArithmetic && !leaf.IsNodeValue && leaf.InputId == inputId
            && (fieldId is null ? leaf.Segments.Count == 0 : leaf.Segments.Count > 0 && leaf.Segments[0] == fieldId)
            && leaf.Literal == value) ?? false;

    /// <summary>The condition with an input renamed wherever it is read, re-spelled by the format's writer.</summary>
    public static string RenameInput(string when, string oldId, string newId) =>
        Map(when, clause => clause.InputId == oldId && !clause.IsNodeValue ? clause with { InputId = newId } : clause);

    /// <summary>The condition with an input's top-level field renamed wherever it is read.</summary>
    public static string RenameField(string when, string inputId, string oldField, string newField) =>
        Map(when, clause => clause.InputId == inputId && !clause.IsNodeValue && clause.Segments.Count > 0 && clause.Segments[0] == oldField
            ? clause with
            {
                Field = newField,
                Path = clause.Path is null ? null : new[] { newField }.Concat(clause.Path.Skip(1)).ToList()
            }
            : clause);

    private static string Map(string when, Func<WorkflowResultCondition, WorkflowResultCondition> rename)
    {
        var expression = Parse(when);
        return expression is null ? when : MapExpression(expression, rename).Text;
    }

    private static WorkflowConditionExpression MapExpression(WorkflowConditionExpression expression, Func<WorkflowResultCondition, WorkflowResultCondition> rename) =>
        expression switch
        {
            WorkflowClauseExpression { Clause: var clause } => new WorkflowClauseExpression(MapClause(clause, rename)),
            WorkflowNotExpression { Inner: var inner } => new WorkflowNotExpression(MapExpression(inner, rename)),
            WorkflowAndExpression { Left: var l, Right: var r } => new WorkflowAndExpression(MapExpression(l, rename), MapExpression(r, rename)),
            WorkflowOrExpression { Left: var l, Right: var r } => new WorkflowOrExpression(MapExpression(l, rename), MapExpression(r, rename)),
            _ => expression
        };

    private static WorkflowResultCondition MapClause(WorkflowResultCondition clause, Func<WorkflowResultCondition, WorkflowResultCondition> rename) =>
        clause.IsArithmetic
            ? clause with { Left = MapTerm(clause.Left, rename), Right = MapTerm(clause.Right, rename) }
            : rename(clause);

    private static WorkflowConditionTerm? MapTerm(WorkflowConditionTerm? term, Func<WorkflowResultCondition, WorkflowResultCondition> rename) =>
        term switch
        {
            null => null,
            WorkflowOperandTerm { Operand: var operand } => new WorkflowOperandTerm(rename(operand)),
            WorkflowBinaryTerm { Op: var op, Left: var l, Right: var r } => new WorkflowBinaryTerm(op, MapTerm(l, rename)!, MapTerm(r, rename)!),
            WorkflowNegateTerm { Inner: var inner } => new WorkflowNegateTerm(MapTerm(inner, rename)!),
            _ => term
        };
}
