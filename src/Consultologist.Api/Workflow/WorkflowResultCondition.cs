using System.Globalization;
using Consultologist.Api.Models;

namespace Consultologist.Api.Workflow;

/// <summary>
/// A parsed deliverable condition (package-format-v8-design.md § 5, v9 § 6):
/// <c>when: billable</c>, <c>when: kind == follow_up</c>,
/// <c>when: kind != procedure</c> — and from v9 <c>when: length_of_stay > 7</c>,
/// <c>when: patient.age >= 65</c>, <c>when: count(prior_notes) > 1</c>,
/// <c>when: prior_notes</c>.
///
/// <c>Literal</c> null is the bare truthy form. <c>Field</c> names one field of
/// an object input; <c>IsCount</c> asks how many elements an array holds;
/// <c>Ordering</c> is one of the four ordering operators, null for equality.
/// Trailing optionals, so the v8 shape — id, literal, negated — reads and
/// constructs exactly as it did. Parsed once — at publish by the validator,
/// at load by the store — so the engine evaluates a structure and the grammar
/// lives in one place.
/// </summary>
public sealed record WorkflowResultCondition(
    string InputId,
    string? Literal,
    bool Negated,
    string? Field = null,
    bool IsCount = false,
    string? Ordering = null)
{
    /// <summary>The operand as an author wrote it: <c>patient.age</c>, <c>count(prior_notes)</c>.</summary>
    public string Operand => IsCount
        ? $"count({InputId})"
        : Field is null ? InputId : $"{InputId}.{Field}";

    /// <summary>The comparison, or null for the bare form.</summary>
    public string? Operator => Literal is null
        ? null
        : Ordering ?? (Negated ? WorkflowResultConditions.NotEqualsOperator : WorkflowResultConditions.EqualsOperator);

    public bool IsBare => Literal is null;

    public bool IsOrdered => Ordering is not null;
}

/// <summary>
/// The condition grammar, deliberately closed and grown exactly once (v9 § 6):
/// one operand, one operator, one literal. No and/or, no arithmetic.
///
/// A manifest is content authors fork freely and an operator cannot review line
/// by line; an evaluator is a thing with an order of operations, and this format
/// does not need one.
///
/// **Syntax only.** Whether the id is declared, whether its type may be tested
/// with this operator, and whether the literal is admissible are vocabulary
/// closures, and those belong to the validator — the same split the binding
/// parser follows. The parser accepts the full grammar whatever the version; a
/// v8 manifest using a v9 form is refused by the validator by name.
/// </summary>
public static class WorkflowResultConditions
{
    public const string EqualsOperator = "==";
    public const string NotEqualsOperator = "!=";

    // Two-character operators first, so ">=" is never read as ">" followed
    // by a literal beginning with "=".
    private static readonly string[] Operators = { ">=", "<=", EqualsOperator, NotEqualsOperator, ">", "<" };

    public static bool TryParse(string? when, out WorkflowResultCondition? condition, out string? error)
    {
        condition = null;
        error = null;

        var text = when?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            error = "is blank.";
            return false;
        }

        var (operatorIndex, op) = FindOperator(text);

        // The bare form: an operand and nothing else.
        if (operatorIndex < 0)
        {
            if (!TryParseOperand(text, out var bare))
            {
                error = $"'{text}' is not an input id. Write 'when: <input>' or 'when: <input> == <value>'.";
                return false;
            }

            condition = bare;
            return true;
        }

        var operandText = text[..operatorIndex].Trim();
        var literal = text[(operatorIndex + op!.Length)..].Trim();

        if (!TryParseOperand(operandText, out var operand))
        {
            error = $"'{operandText}' is not an input id.";
            return false;
        }

        if (literal.Length == 0)
        {
            error = "compares against nothing; write a value after the operator.";
            return false;
        }

        // A quoted literal is text. The parser accepts the full grammar even
        // where the validator will refuse the operand type, so widening later
        // is a validator change and never a parser one.
        if (literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"')
        {
            literal = literal[1..^1];
        }
        else if (literal.Contains('"'))
        {
            error = $"literal '{literal}' has a stray quote.";
            return false;
        }

        condition = operand! with
        {
            Literal = literal,
            Negated = op == NotEqualsOperator,
            Ordering = op is EqualsOperator or NotEqualsOperator ? null : op
        };
        return true;
    }

    private static (int Index, string? Operator) FindOperator(string text)
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

        return (-1, null);
    }

    /// <summary>
    /// <c>id</c>, <c>id.field</c> or <c>count(id)</c>, each part a declared id.
    /// </summary>
    private static bool TryParseOperand(string raw, out WorkflowResultCondition? operand)
    {
        operand = null;

        if (raw.StartsWith("count(", StringComparison.Ordinal) && raw.EndsWith(')'))
        {
            var inner = raw["count(".Length..^1].Trim();

            if (!WorkflowDeclaredIds.IsValid(inner))
            {
                return false;
            }

            operand = new WorkflowResultCondition(inner, null, false, IsCount: true);
            return true;
        }

        var dot = raw.IndexOf('.');

        if (dot >= 0)
        {
            var id = raw[..dot];
            var field = raw[(dot + 1)..];

            if (!WorkflowDeclaredIds.IsValid(id) || !WorkflowDeclaredIds.IsValid(field))
            {
                return false;
            }

            operand = new WorkflowResultCondition(id, null, false, Field: field);
            return true;
        }

        if (!WorkflowDeclaredIds.IsValid(raw))
        {
            return false;
        }

        operand = new WorkflowResultCondition(raw, null, false);
        return true;
    }

    /// <summary>
    /// Whether a deliverable fires for these supplied inputs. Pure, and
    /// evaluated once at job start — the fire set is knowable before anything
    /// runs, which is what keeps TotalBlockCount a stored scalar (#176).
    ///
    /// A null condition always fires: a deliverable without a `when` is
    /// unconditional. Absence is not falsity: an optional input nobody supplied
    /// has not answered the question, so the condition does not hold —
    /// including the negated form (v8 § 4). The one exception is count(): an
    /// absent array counts zero, because "no entries supplied" and "an empty
    /// list supplied" answer the same clinical question (v9 § 6). Never throws,
    /// whatever map it is handed.
    /// </summary>
    public static bool Holds(
        WorkflowResultCondition? condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs)
    {
        if (condition is null)
        {
            return true;
        }

        if (condition.IsCount)
        {
            if (condition.IsBare || !TryCount(condition, suppliedInputs, out var count))
            {
                return false;
            }

            return int.TryParse(condition.Literal, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted)
                && Compare(count.CompareTo(wanted), condition);
        }

        if (!TryOperandValue(condition, suppliedInputs, out var value))
        {
            return false;
        }

        if (condition.IsBare)
        {
            return value.Kind switch
            {
                ConsultInputKind.Boolean => value.Flag!.Value,
                ConsultInputKind.Array => value.Elements!.Count > 0,
                _ => false
            };
        }

        switch (value.Kind)
        {
            case ConsultInputKind.Number:
                return ConsultInputValue.TryParseNumber(condition.Literal!, out var literalNumber)
                    && Compare(value.NumberValue!.Value.CompareTo(literalNumber.NumberValue!.Value), condition);

            case ConsultInputKind.Text when condition.IsOrdered:
                return DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && DateOnly.TryParseExact(condition.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var literalDate)
                    && Compare(date.CompareTo(literalDate), condition);

            case ConsultInputKind.Text:
            case ConsultInputKind.Boolean:
                var matches = string.Equals(value.Canonical, condition.Literal, StringComparison.Ordinal);
                return condition.Negated ? !matches : matches;

            default:
                // Structure compared to a literal answers nothing.
                return false;
        }
    }

    private static bool Compare(int comparison, WorkflowResultCondition condition) => condition.Ordering switch
    {
        ">" => comparison > 0,
        "<" => comparison < 0,
        ">=" => comparison >= 0,
        "<=" => comparison <= 0,
        _ => condition.Negated ? comparison != 0 : comparison == 0
    };

    /// <summary>The operand's value: the input, or one field of an object input. False on absence, a null field, or the wrong shape.</summary>
    private static bool TryOperandValue(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        out ConsultInputValue value)
    {
        value = null!;

        if (suppliedInputs is null
            || !suppliedInputs.TryGetValue(condition.InputId, out var input)
            || input.IsBlank)
        {
            return false;
        }

        if (condition.Field is null)
        {
            value = input;
            return true;
        }

        if (!input.IsObject)
        {
            return false;
        }

        var field = input.Fields!.FirstOrDefault(entry => string.Equals(entry.Id, condition.Field, StringComparison.Ordinal));

        if (field is null || field.Value.IsNull)
        {
            return false;
        }

        value = field.Value;
        return true;
    }

    /// <summary>count(): an absent array counts zero; a present array its elements; anything else answers nothing.</summary>
    private static bool TryCount(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs,
        out int count)
    {
        count = 0;

        if (suppliedInputs is null || !suppliedInputs.TryGetValue(condition.InputId, out var input) || input.IsBlank)
        {
            return true;
        }

        if (!input.IsArray)
        {
            return false;
        }

        count = input.Elements!.Count;
        return true;
    }

    /// <summary>
    /// Why a deliverable did not fire, in the words a reader needs: the operand,
    /// what the condition wanted, and what it found. This sentence reaches
    /// History and the email door's reply, so it prints only what is safe on
    /// every surface: authored content — an enum value, the literal — a boolean,
    /// and a count of entries. A number, a date or a field's value is the
    /// patient's, and is never printed: for those the sentence says what was
    /// needed and that it was not met.
    /// </summary>
    public static string Explain(
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, ConsultInputValue>? suppliedInputs)
    {
        if (condition.IsCount)
        {
            var has = TryCount(condition, suppliedInputs, out var count) ? count.ToString(CultureInfo.InvariantCulture) : "not a list";
            return $"needs {condition.Operand} to be {Wanted(condition, null)}; it is {has}";
        }

        var supplied = TryOperandValue(condition, suppliedInputs, out var value)
            ? Found(condition, value)
            : "not supplied";

        return $"needs {condition.Operand} to be {Wanted(condition, value)}; it is {supplied}";
    }

    private static string Wanted(WorkflowResultCondition condition, ConsultInputValue? value)
    {
        if (condition.IsBare)
        {
            return value?.IsArray == true ? "non-empty" : "true";
        }

        return condition.IsOrdered
            ? $"{condition.Ordering} {condition.Literal}"
            : $"{(condition.Negated ? "not " : string.Empty)}'{condition.Literal}'";
    }

    private static string Found(WorkflowResultCondition condition, ConsultInputValue value)
    {
        if (value.IsArray)
        {
            var entries = value.Elements!.Count;
            return entries == 0 ? "empty" : entries == 1 ? "1 entry" : $"{entries.ToString(CultureInfo.InvariantCulture)} entries";
        }

        if (value.IsBoolean)
        {
            return $"'{value.Canonical}'";
        }

        // A field's value, a number, or a date is the patient's. An enum value
        // is authored — but a date arrives as text too, and the only thing
        // that tells them apart here is its spelling.
        var isPatientData = condition.Field is not null
            || condition.IsOrdered
            || value.IsNumber
            || value.IsStructured
            || DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        return isPatientData
            ? "not"
            : value.HasCanonical ? $"'{value.Canonical}'" : value.Described;
    }
}
