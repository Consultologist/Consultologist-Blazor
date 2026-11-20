using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1501 (specVersion 31, area 04): what is wrong with the written form of a JSON Logic rule (part 03 §§ 2, 5, 11.1).</summary>
public enum ShapeFaultKind
{
    TooDeep,
    NotARule,
    LooseEquality,
    LooseInequality,
    Substr,
    UnknownOperation,
    BareOperand,
    Arity,
    IfWithoutElse,
    VarDefault,
    VarNotText,
    PathNotAPath,
    PathPosition,
    ObjectOperand,
    ComputedLiteral,
    LiteralNotText,
    CountPassMin,
    BlankWord,
    PatternNotIRegexp,
    PatternTooLong,
    PatternRepeats,
    PatternDoesNotCompile,
}

/// <summary>
/// #1501 (specVersion 31, area 04): one fault of the written form. Members a kind does not use are
/// <c>""</c> and 0. <c>Operation</c> is the operation at fault; <c>Text</c> is, by kind, the arity words
/// (<see cref="ShapeFaultKind.Arity"/>), what a value is (A02-001's words), a path, an operand's name,
/// a literal's JSON or a pattern; <c>Count</c> is a member, operand or code-point count; <c>Detail</c> is
/// what a literal is, or a pattern's reason.
/// </summary>
public sealed record ShapeFault(ShapeFaultKind Kind, string Operation, string Text, int Count, string Detail);

/// <summary>
/// #1501 (specVersion 31, area 04): the written form of a rule, judged with no types: its objects, its
/// operations and their operand counts, its paths and its literal operands (part 03 §§ 2, 5; D6 to D10).
/// Never throws: any <see cref="JsonNode"/>, or <c>null</c> (the JSON value <c>null</c>), is accepted.
/// </summary>
public static class JsonLogicShape
{
    /// <summary>A rule nested deeper than this is <see cref="ShapeFaultKind.TooDeep"/> (D17).</summary>
    public const int MaxDepth = 64;

    /// <summary>Every fault of <paramref name="rule"/>, in tree order: an operation's own, then its operands'.</summary>
    public static IReadOnlyList<ShapeFault> Check(JsonNode? rule)
    {
        var faults = new List<ShapeFault>();
        var tooDeep = false;
        Walk(rule, 1, faults, ref tooDeep);
        return faults;
    }

    /// <summary>A path is non-empty segments joined by dots (D6).</summary>
    public static bool IsPath(string path) =>
        path != null && path.Length > 0 && path.Split('.').All(s => s.Length > 0);

    /// <summary>A path with a segment of digits only reaches into a list by position (S03-013).</summary>
    public static bool IsPosition(string path) =>
        path != null && path.Split('.').Any(s => s.Length > 0 && s.All(c => c is >= '0' and <= '9'));

    /// <summary>Every <c>var</c> path and <c>missing</c> path that is text, distinct, in first-written order.</summary>
    public static IReadOnlyList<string> Paths(JsonNode? rule)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Collect(rule, 1, paths, seen);
        return paths;
    }

    private static void Walk(JsonNode? node, int depth, List<ShapeFault> faults, ref bool tooDeep)
    {
        if (node is not JsonObject and not JsonArray)
            return;
        if (depth > MaxDepth)
        {
            if (!tooDeep)
                faults.Add(Fault(ShapeFaultKind.TooDeep));
            tooDeep = true;
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var element in array)
                Walk(element, depth + 1, faults, ref tooDeep);
            return;
        }

        var obj = (JsonObject)node;
        if (obj.Count != 1)
        {
            faults.Add(Fault(ShapeFaultKind.NotARule, count: obj.Count));
            return;
        }

        var (name, operands) = obj.First();
        switch (name)
        {
            case "==":
                faults.Add(Fault(ShapeFaultKind.LooseEquality, name));
                Operands(operands, depth, faults, ref tooDeep);
                return;
            case "!=":
                faults.Add(Fault(ShapeFaultKind.LooseInequality, name));
                Operands(operands, depth, faults, ref tooDeep);
                return;
            case "substr":
                faults.Add(Fault(ShapeFaultKind.Substr, name));
                Operands(operands, depth, faults, ref tooDeep);
                return;
        }

        var operation = JsonLogicOperations.Find(name);
        if (operation == null)
        {
            faults.Add(Fault(ShapeFaultKind.UnknownOperation, name));
            Operands(operands, depth, faults, ref tooDeep);
            return;
        }

        switch (name)
        {
            case "var":
                CheckVar(operands, faults);
                return;
            case "object":
                if (operands is JsonObject fields)
                {
                    foreach (var (_, value) in fields)
                        Walk(value, depth + 1, faults, ref tooDeep);
                }
                else
                    faults.Add(Fault(ShapeFaultKind.ObjectOperand, name, Describe(operands)));
                return;
        }

        if (operands is not JsonArray list)
        {
            faults.Add(Fault(ShapeFaultKind.BareOperand, name));
            return;
        }

        var count = list.Count;
        if (count < operation.Min || (operation.Max is int max && count > max))
        {
            if (name == "if" && count >= 2 && count % 2 == 0)
                faults.Add(Fault(ShapeFaultKind.IfWithoutElse, name));
            else
            {
                faults.Add(Fault(ShapeFaultKind.Arity, name, JsonLogicOperations.Takes(operation), count));
                Operands(operands, depth, faults, ref tooDeep);
                return;
            }
        }
        else if (name == "if" && count % 2 == 0)
            faults.Add(Fault(ShapeFaultKind.IfWithoutElse, name));

        if (name == "missing")
        {
            foreach (var operand in list)
            {
                if (operand is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    CheckPath(value.GetValue<string>(), faults);
                else
                    faults.Add(Literal(name, "path", operand));
            }
            return;
        }

        var literals = LiteralPositions(name);
        for (var i = 0; i < list.Count; i++)
        {
            if (literals.TryGetValue(i, out var literal))
                CheckLiteral(name, literal, list[i], faults);
            else
                Walk(list[i], depth + 1, faults, ref tooDeep);
        }
    }

    private static void Operands(JsonNode? operands, int depth, List<ShapeFault> faults, ref bool tooDeep)
    {
        if (operands is JsonArray array)
            foreach (var operand in array)
                Walk(operand, depth + 1, faults, ref tooDeep);
    }

    private static void CheckVar(JsonNode? operand, List<ShapeFault> faults)
    {
        if (operand is JsonArray)
            faults.Add(Fault(ShapeFaultKind.VarDefault, "var"));
        else if (operand is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            CheckPath(value.GetValue<string>(), faults);
        else
            faults.Add(Fault(ShapeFaultKind.VarNotText, "var", Describe(operand)));
    }

    private static void CheckPath(string path, List<ShapeFault> faults)
    {
        if (!IsPath(path))
            faults.Add(Fault(ShapeFaultKind.PathNotAPath, text: path));
        else if (IsPosition(path))
            faults.Add(Fault(ShapeFaultKind.PathPosition, text: path));
    }

    private static Dictionary<int, string> LiteralPositions(string name) => name switch
    {
        "matches" => new() { [1] = "pattern" },
        "has_word" => new() { [1] = "word" },
        "terms_subset" => new() { [2] = "failWith" },
        "all_pass" => new() { [1] = "failWith" },
        "count_pass" => new() { [1] = "min", [2] = "failWith" },
        _ => [],
    };

    private static void CheckLiteral(string operation, string operand, JsonNode? node, List<ShapeFault> faults)
    {
        if (node is JsonObject or JsonArray)
        {
            faults.Add(Literal(operation, operand, node));
            return;
        }

        if (operand == "min")
        {
            if (!IsCountMin(node))
                faults.Add(Fault(ShapeFaultKind.CountPassMin, text: node?.ToJsonString() ?? "null"));
            return;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            faults.Add(Literal(operation, operand, node));
            return;
        }

        var text = value.GetValue<string>();
        switch (operand)
        {
            case "pattern":
                var check = IRegexp.Check(text);
                var kind = check.Fault switch
                {
                    IRegexpFault.TooLong => ShapeFaultKind.PatternTooLong,
                    IRegexpFault.NotIRegexp => ShapeFaultKind.PatternNotIRegexp,
                    IRegexpFault.Repeats => ShapeFaultKind.PatternRepeats,
                    IRegexpFault.DoesNotCompile => ShapeFaultKind.PatternDoesNotCompile,
                    _ => (ShapeFaultKind?)null,
                };
                if (kind != null)
                    faults.Add(Fault(kind.Value, text: text, count: check.Count, detail: check.Reason));
                break;
            case "word":
                if (!text.EnumerateRunes().Any(r => !Rune.IsWhiteSpace(r)))
                    faults.Add(Fault(ShapeFaultKind.BlankWord));
                break;
        }
    }

    private static bool IsCountMin(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
            return false;
        if (!value.TryGetValue<double>(out var d) || !double.IsFinite(d))
            return false;
        return d == Math.Floor(d) && d >= 0 && d <= int.MaxValue;
    }

    private static ShapeFault Literal(string operation, string operand, JsonNode? node) =>
        Fault(node is JsonObject or JsonArray ? ShapeFaultKind.ComputedLiteral : ShapeFaultKind.LiteralNotText,
            operation, operand, detail: Describe(node));

    /// <summary>A02-001's <c>&lt;what it is&gt;</c>.</summary>
    private static string Describe(JsonNode? value) => value switch
    {
        null => "null",
        JsonArray => "a list",
        JsonObject => "an object",
        _ => value.GetValueKind() switch
        {
            JsonValueKind.String => "text",
            JsonValueKind.True or JsonValueKind.False => "true or false",
            JsonValueKind.Number => "a number",
            _ => "null",
        },
    };

    private static void Collect(JsonNode? node, int depth, List<string> paths, HashSet<string> seen)
    {
        if (depth > MaxDepth || node is not JsonObject and not JsonArray)
            return;
        if (node is JsonArray array)
        {
            foreach (var element in array)
                Collect(element, depth + 1, paths, seen);
            return;
        }

        var obj = (JsonObject)node;
        if (obj.Count != 1)
            return;
        var (name, operands) = obj.First();
        switch (name)
        {
            case "var":
                Add(operands, paths, seen);
                break;
            case "missing":
                if (operands is JsonArray list)
                    foreach (var operand in list)
                        Add(operand, paths, seen);
                break;
            case "object":
                if (operands is JsonObject fields)
                    foreach (var (_, value) in fields)
                        Collect(value, depth + 1, paths, seen);
                break;
            default:
                if (operands is JsonArray all)
                    foreach (var operand in all)
                        Collect(operand, depth + 1, paths, seen);
                break;
        }
    }

    private static void Add(JsonNode? node, List<string> paths, HashSet<string> seen)
    {
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            var path = value.GetValue<string>();
            if (seen.Add(path))
                paths.Add(path);
        }
    }

    private static ShapeFault Fault(ShapeFaultKind kind, string operation = "", string text = "", int count = 0, string detail = "") =>
        new(kind, operation, text, count, detail);
}
