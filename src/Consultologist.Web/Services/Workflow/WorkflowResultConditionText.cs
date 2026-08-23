namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// The editor's read-only view of a condition string. The engine owns the
/// grammar (Consultologist.Api.Workflow.WorkflowResultConditions); this only
/// needs to answer the questions the authoring surfaces ask, so it reads the
/// text rather than mirroring the parser — a second parser would be a second
/// thing to keep in step.
///
/// #427: it reads the whole v9 grammar — six operators, a path into a field,
/// count() — even though the editor composes only the v8 forms (#429). A
/// condition the editor cannot yet write must still name its input
/// correctly, or the pre-publish check refuses a package the engine accepts
/// and a rename leaves the condition reading an input that no longer exists.
/// </summary>
public static class WorkflowResultConditionText
{
    /// <summary>Two-character operators first, so ">=" is never ">" followed by a literal.</summary>
    private static readonly string[] Operators = { ">=", "<=", "==", "!=", ">", "<" };

    /// <summary>The input id a condition reads, or null when it is unparseable.</summary>
    public static string? InputOf(string? when)
    {
        var text = when?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var operand = OperandText(text);

        if (operand.StartsWith("count(", StringComparison.Ordinal) && operand.EndsWith(')'))
        {
            operand = operand[6..^1].Trim();
        }

        var dot = operand.IndexOf('.');
        if (dot >= 0)
        {
            operand = operand[..dot].Trim();
        }

        return operand.Length == 0 ? null : operand;
    }

    public static bool ReadsInput(string? when, string inputId) =>
        string.Equals(InputOf(when), inputId, StringComparison.Ordinal);

    /// <summary>Compose: null literal is the bare truthy form.</summary>
    public static string Compose(string inputId, bool negated, string? literal) =>
        literal is null
            ? inputId
            : $"{inputId} {(negated ? "!=" : "==")} {literal}";

    /// <summary>The operator, or null for the bare form.</summary>
    public static string? OperatorOf(string? when) =>
        FindOperator(when?.Trim() ?? string.Empty)?.Operator;

    public static bool IsNegated(string? when) =>
        OperatorOf(when) == "!=";

    /// <summary>The literal a condition compares against, or null for the bare form.</summary>
    public static string? LiteralOf(string? when)
    {
        var text = when?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return FindOperator(text) is { } found
            ? text[(found.Index + found.Operator.Length)..].Trim().Trim('"')
            : null;
    }

    /// <summary>
    /// The same condition reading <paramref name="newId"/> where it read
    /// <paramref name="oldId"/>: the field, the count and the comparison are
    /// carried as written.
    /// </summary>
    public static string Rename(string when, string newId)
    {
        var text = when.Trim();
        var found = FindOperator(text);
        var operand = OperandText(text);
        var rest = found is { } f ? text[f.Index..].Trim() : string.Empty;

        var renamed = operand.StartsWith("count(", StringComparison.Ordinal) && operand.EndsWith(')')
            ? $"count({newId})"
            : operand.IndexOf('.') is var dot && dot >= 0
                ? newId + operand[dot..]
                : newId;

        return rest.Length == 0 ? renamed : $"{renamed} {rest}";
    }

    /// <summary>The operand as written — "patient.age", "count(prior_notes)", "billable" — or null when blank.</summary>
    public static string? OperandOf(string? when)
    {
        var text = when?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var operand = OperandText(text);
        return operand.Length == 0 ? null : operand;
    }

    /// <summary>The field a path reads ("age" for "patient.age …"), or null.</summary>
    public static string? FieldOf(string? when)
    {
        var operand = OperandOf(when);
        if (operand is null || IsCount(when))
        {
            return null;
        }

        var dot = operand.IndexOf('.');
        return dot >= 0 ? operand[(dot + 1)..].Trim() : null;
    }

    public static bool IsCount(string? when) =>
        OperandOf(when) is { } operand && operand.StartsWith("count(", StringComparison.Ordinal) && operand.EndsWith(')');

    public static bool IsOrdered(string? when) =>
        OperatorOf(when) is ">" or "<" or ">=" or "<=";

    /// <summary>A path, a count or an ordering: the forms v9 added (#427).</summary>
    public static bool IsV9Form(string? when) =>
        FieldOf(when) != null || IsCount(when) || IsOrdered(when);

    /// <summary>The same condition with one field renamed, where it reads that field; otherwise unchanged.</summary>
    public static string RenameField(string when, string inputId, string oldField, string newField)
    {
        if (!ReadsInput(when, inputId) || FieldOf(when) != oldField)
        {
            return when;
        }

        var text = when.Trim();
        var rest = FindOperator(text) is { } found ? text[found.Index..].Trim() : string.Empty;
        var renamed = $"{inputId}.{newField}";

        return rest.Length == 0 ? renamed : $"{renamed} {rest}";
    }

    private static string OperandText(string text) =>
        (FindOperator(text) is { } found ? text[..found.Index] : text).Trim();

    private static (int Index, string Operator)? FindOperator(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            foreach (var candidate in Operators)
            {
                if (string.CompareOrdinal(text, index, candidate, 0, candidate.Length) == 0)
                {
                    return (index, candidate);
                }
            }
        }

        return null;
    }
}
