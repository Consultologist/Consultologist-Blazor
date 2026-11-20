namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): one operation of part 03 § 5: its <paramref name="Name"/>, the
/// fewest operands it takes (<paramref name="Min"/>) and the most (<paramref name="Max"/>, <c>null</c>
/// for "or more"), and whether it is one of the format's own (<paramref name="Custom"/>, versioned in
/// <c>constants.json</c>'s <c>operations</c>) rather than standard JSON Logic.
/// </summary>
public sealed record JsonLogicOperation(string Name, int Min, int? Max, bool Custom);

/// <summary>
/// #1501 (specVersion 31, area 04): the operations a rule may name (part 03 § 5), in
/// <c>constants.json</c>'s order (its <c>standardOperations</c>, then the keys of its
/// <c>operations</c>), with their operand counts and the versions the record pins. Tests hold every
/// name and version here to the published file.
/// </summary>
public static class JsonLogicOperations
{
    /// <summary>The evaluator's version (<c>constants.json</c> <c>evaluator</c>).</summary>
    public const string EvaluatorVersion = "1.0.0";

    /// <summary>The printer's version (<c>constants.json</c> <c>builtins.printer</c>).</summary>
    public const string PrinterVersion = "1.0.0";

    /// <summary>The version of every custom operation (<c>constants.json</c> <c>operations</c>).</summary>
    public const string OperationVersion = "1.0.0";

    /// <summary>The 21 standard operations, then the 18 custom ones.</summary>
    public static IReadOnlyList<JsonLogicOperation> All { get; } =
    [
        Standard("var", 1, 1),
        Standard("missing", 1, null),
        Standard("if", 3, null),
        Standard("===", 2, 2),
        Standard("!==", 2, 2),
        Standard("<", 2, 2),
        Standard("<=", 2, 2),
        Standard(">", 2, 2),
        Standard(">=", 2, 2),
        Standard("and", 2, null),
        Standard("or", 2, null),
        Standard("!", 1, 1),
        Standard("in", 2, 2),
        Standard("cat", 2, null),
        Standard("+", 2, null),
        Standard("-", 1, 2),
        Standard("*", 2, null),
        Standard("/", 2, 2),
        Standard("%", 2, 2),
        Standard("min", 1, null),
        Standard("max", 1, null),
        Custom("size", 1, 1),
        Custom("blank", 1, 1),
        Custom("words", 1, 1),
        Custom("lines", 1, 1),
        Custom("lower", 1, 1),
        Custom("trim", 1, 1),
        Custom("starts_with", 2, 2),
        Custom("ends_with", 2, 2),
        Custom("plain", 1, 1),
        Custom("object", 1, 1),
        Custom("matches", 2, 2),
        Custom("has_word", 2, 2),
        Custom("terms_subset", 3, 3),
        Custom("all_pass", 2, 2),
        Custom("count_pass", 3, 3),
        Custom("days_between", 2, 2),
        Custom("years_between", 2, 2),
        Custom("add_days", 2, 2),
    ];

    /// <summary>The run facts a rule reads as <c>run.&lt;fact&gt;</c> (part 03 § 4.3; <c>constants.json</c> <c>runFacts</c>).</summary>
    public static IReadOnlyList<string> RunFacts { get; } = ["date", "id", "package", "engine"];

    /// <summary>S03-006's <c>&lt;the allowed list&gt;</c>: every name, in order, joined by <c>, </c> (D8).</summary>
    public static string AllowedList { get; } = string.Join(", ", All.Select(o => o.Name));

    private static readonly Dictionary<string, JsonLogicOperation> ByName =
        All.ToDictionary(o => o.Name, StringComparer.Ordinal);

    /// <summary>The operation named <paramref name="name"/> (ordinal), or <c>null</c> when part 03 § 5 has none.</summary>
    public static JsonLogicOperation? Find(string name) =>
        name != null && ByName.TryGetValue(name, out var operation) ? operation : null;

    /// <summary>
    /// S03-010's <c>&lt;m&gt;</c> (D8): <c>1</c>, <c>2</c>, <c>3</c>, <c>1 or 2</c>, <c>1 or more</c>,
    /// <c>2 or more</c>, <c>3 or more</c>.
    /// </summary>
    public static string Takes(JsonLogicOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Max switch
        {
            null => FormattableString.Invariant($"{operation.Min} or more"),
            var max when max == operation.Min => FormattableString.Invariant($"{operation.Min}"),
            var max => FormattableString.Invariant($"{operation.Min} or {max}"),
        };
    }

    private static JsonLogicOperation Standard(string name, int min, int? max) => new(name, min, max, false);

    private static JsonLogicOperation Custom(string name, int min, int? max) => new(name, min, max, true);
}
