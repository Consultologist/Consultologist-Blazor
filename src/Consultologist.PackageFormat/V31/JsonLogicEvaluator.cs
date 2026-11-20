using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvus.Text.Json.JsonLogic;
using CorvusElement = Corvus.Text.Json.JsonElement;
using CorvusKind = Corvus.Text.Json.JsonValueKind;
using CorvusWorkspace = Corvus.Text.Json.JsonWorkspace;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1501 (specVersion 31, area 04): what a rule gave. <paramref name="Failure"/> null means it ran; then <paramref name="Value"/> is its value (C# null for JSON <c>null</c>).</summary>
public sealed record JsonLogicValue(JsonNode? Value, JsonLogicFailure? Failure);

/// <summary>#1501 (specVersion 31, area 04): what a condition decided (D22). With a failure, <paramref name="Result"/> is <c>false</c> and the node that owns the condition fails.</summary>
public sealed record JsonLogicDecision(bool Result, JsonLogicFailure? Failure);

/// <summary>
/// #1501 (specVersion 31, area 04): one compiled rule. A rule outside the format is <see cref="Declined"/> and never runs:
/// <see cref="Evaluate"/> and <see cref="Decide"/> give that failure. Neither throws on any data; <c>data</c> is one object
/// whose members are the roots (<c>input</c>, <c>run</c>, <c>node</c>, …), and C# null is the JSON value <c>null</c>.
/// </summary>
public interface IJsonLogicRule
{
    /// <summary>Why the rule will not run (A04-011's detail), or <c>null</c>.</summary>
    JsonLogicFailure? Declined { get; }

    /// <summary>The rule's value on <paramref name="data"/>.</summary>
    JsonLogicValue Evaluate(JsonNode? data);

    /// <summary>The rule as a condition (part 03 § 6.4, § 9; D22): <c>true</c>, <c>false</c>, <c>null</c> and a list decide; any other value is <see cref="JsonLogicFailureKind.NotABoolean"/>.</summary>
    JsonLogicDecision Decide(JsonNode? data);
}

/// <summary>
/// #1501 (specVersion 31, area 04): the evaluator of part 03 § 9. The engine, the validator's probe, the Editor and
/// the in-package tests evaluate through it; Corvus is behind it.
/// </summary>
public interface IJsonLogicEvaluator
{
    /// <summary>Our evaluator's version (<c>requires.evaluator</c>), not Corvus's.</summary>
    string Version { get; }

    /// <summary>
    /// <paramref name="rule"/> compiled, or declined. <paramref name="plainConverts"/> says, for each <c>plain</c> in the rule in
    /// depth-first order, whether its operand is Markdown (D14); without it, or past its end, a <c>plain</c> converts.
    /// </summary>
    IJsonLogicRule Compile(JsonNode? rule, IReadOnlyList<bool>? plainConverts = null);
}

/// <summary>
/// #1501 (specVersion 31, area 04; D1, D14 to D17, D22): <see cref="IJsonLogicEvaluator"/> on <c>Corvus.Text.Json.JsonLogic</c>.
/// One Corvus evaluator per instance, with our compiler registered for every operation of part 03 § 5 but <c>if</c>,
/// <c>!</c>, <c>===</c> and <c>!==</c> (Corvus's own, which agree with § 5), so no other Corvus default is reachable.
/// A rule with any fault of its written form (<see cref="JsonLogicShape.Check"/>) is declined before Corvus sees it (D17);
/// an accepted rule is rewritten for Corvus (D16's <c>object</c>; each <c>plain</c> carries its D14 choice as a second,
/// literal operand) and cached by its JSON text as written plus the <c>plain</c> list. Safe to share between threads:
/// calls into Corvus are serialised, since its evaluator is not.
/// </summary>
public sealed class CorvusJsonLogicEvaluator : IJsonLogicEvaluator
{
    private static readonly JsonLogicFailure NotARule = Declined("it is not a rule of this format");
    private static readonly JsonLogicFailure Failed = new(JsonLogicFailureKind.Fault, "the evaluator failed");

    /// <summary>
    /// The deepest JSON written or read on the way through Corvus: a rule (about two levels per operation, so
    /// <see cref="JsonLogicShape.MaxDepth"/> operations is some 130), its data, and every value. The libraries' default is 64;
    /// data deeper than this is <see cref="JsonLogicFailureKind.Fault"/>.
    /// </summary>
    private const int MaxJsonDepth = 512;

    private static readonly JsonSerializerOptions Writing = new() { MaxDepth = MaxJsonDepth };
    private static readonly JsonDocumentOptions Reading = new() { MaxDepth = MaxJsonDepth };
    private static readonly Corvus.Text.Json.JsonDocumentOptions CorvusReading = new() { MaxDepth = MaxJsonDepth };

    private readonly JsonLogicEvaluator corvus = new(Compilers());

    // Corvus 5.8.12 remembers the last rule it ran as two fields written one after the other
    // (_lastRuleIdentity, _lastCompiled), so two threads evaluating different rules at once can run
    // each other's delegate tree. Every call into it is serialised; the data is parsed and the
    // result read outside the lock.
    private readonly Lock gate = new();
    private readonly ConcurrentDictionary<string, IJsonLogicRule> compiled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Version => JsonLogicOperations.EvaluatorVersion;

    /// <inheritdoc />
    public IJsonLogicRule Compile(JsonNode? rule, IReadOnlyList<bool>? plainConverts = null)
    {
        string text;
        try
        {
            // As written: ToJsonString keeps an object's field order, which object's result keeps.
            text = rule?.ToJsonString(Writing) ?? "null";
        }
        catch (Exception)
        {
            return new Rule(this, null, NotARule); // a node built in code that cannot be written (NaN)
        }

        var key = text + "\n" + (plainConverts is null ? "*" : string.Concat(plainConverts.Select(c => c ? '1' : '0')));
        return compiled.GetOrAdd(key, _ => Build(rule, plainConverts));
    }

    private Rule Build(JsonNode? rule, IReadOnlyList<bool>? plainConverts)
    {
        var faults = JsonLogicShape.Check(rule);
        if (faults.Count > 0)
        {
            var outside = faults.FirstOrDefault(f => f.Kind is ShapeFaultKind.UnknownOperation or ShapeFaultKind.LooseEquality
                or ShapeFaultKind.LooseInequality or ShapeFaultKind.Substr);
            return new Rule(this, null, outside is null
                ? NotARule
                : Declined($"it uses {outside.Operation}, which is not an operation of this format"));
        }

        try
        {
            var plains = 0;
            var element = Element(Rewrite(rule, plainConverts, ref plains)?.ToJsonString(Writing) ?? "null");
            return new Rule(this, new JsonLogicRule(in element), null);
        }
        catch (Exception)
        {
            return new Rule(this, null, Failed); // the rule is the format's; the failure is ours
        }
    }

    private static JsonLogicFailure Declined(string detail) => new(JsonLogicFailureKind.Declined, detail);

    /// <summary>
    /// The rule as Corvus is given it: <c>{"object":{"a":r1,"b":r2}}</c> becomes <c>{"object":["a",r1,"b",r2]}</c> (D16), and
    /// the n-th <c>plain</c> (numbered before the operations inside it) gains its choice from <paramref name="plainConverts"/>.
    /// The written form is already checked, so the depth is at most <see cref="JsonLogicShape.MaxDepth"/>.
    /// </summary>
    private static JsonNode? Rewrite(JsonNode? node, IReadOnlyList<bool>? plainConverts, ref int plains)
    {
        switch (node)
        {
            case JsonArray array:
            {
                var list = new JsonArray();
                foreach (var element in array)
                    list.Add(Rewrite(element, plainConverts, ref plains));
                return list;
            }
            case JsonObject obj when obj.Count == 1:
            {
                var (name, operands) = obj.First();
                if (name == "object" && operands is JsonObject fields)
                {
                    var pairs = new JsonArray();
                    foreach (var (field, value) in fields)
                    {
                        pairs.Add(JsonValue.Create(field));
                        pairs.Add(Rewrite(value, plainConverts, ref plains));
                    }
                    return new JsonObject { [name] = pairs };
                }
                if (name == "plain" && operands is JsonArray plain)
                {
                    var converts = plainConverts is null || plains >= plainConverts.Count || plainConverts[plains];
                    plains++;
                    var withChoice = (JsonArray)Rewrite(plain, plainConverts, ref plains)!;
                    withChoice.Add(JsonValue.Create(converts));
                    return new JsonObject { [name] = withChoice };
                }
                return new JsonObject { [name] = name == "var" ? operands?.DeepClone() : Rewrite(operands, plainConverts, ref plains) };
            }
            default:
                return node?.DeepClone();
        }
    }

    private sealed class Rule(CorvusJsonLogicEvaluator owner, JsonLogicRule? rule, JsonLogicFailure? declined) : IJsonLogicRule
    {
        public JsonLogicFailure? Declined { get; } = declined;

        public JsonLogicValue Evaluate(JsonNode? data)
        {
            if (Declined is not null || rule is not { } compiledRule)
                return new JsonLogicValue(null, Declined ?? NotARule);
            try
            {
                var text = data?.ToJsonString(Writing) ?? "null";
                using var workspace = CorvusWorkspace.Create();
                using var document = Corvus.Text.Json.ParsedJsonDocument<CorvusElement>.Parse(text, CorvusReading);
                var root = document.RootElement;
                CorvusElement result;
                lock (owner.gate)
                    result = owner.corvus.Evaluate(in compiledRule, in root, workspace);
                return new JsonLogicValue(Node(result), null);
            }
            catch (JsonLogicFailureException e)
            {
                return new JsonLogicValue(null, e.Failure);
            }
            catch (Exception)
            {
                return new JsonLogicValue(null, Failed);
            }
        }

        public JsonLogicDecision Decide(JsonNode? data)
        {
            var value = Evaluate(data);
            if (value.Failure is not null)
                return new JsonLogicDecision(false, value.Failure);
            return value.Value switch
            {
                null => new JsonLogicDecision(false, null),
                JsonArray list => new JsonLogicDecision(list.Count > 0, null),
                JsonObject => NotABoolean("an object"),
                JsonValue v => v.GetValueKind() switch
                {
                    JsonValueKind.True => new JsonLogicDecision(true, null),
                    JsonValueKind.False or JsonValueKind.Null => new JsonLogicDecision(false, null),
                    JsonValueKind.Number => NotABoolean("a number"),
                    _ => NotABoolean("text"),
                },
                _ => NotABoolean("an object"),
            };
        }

        private static JsonLogicDecision NotABoolean(string kind) =>
            new(false, new JsonLogicFailure(JsonLogicFailureKind.NotABoolean, kind));
    }

    // ---- our compilers ----

    private static Dictionary<string, IOperatorCompiler> Compilers()
    {
        var compilers = new Dictionary<string, IOperatorCompiler>(StringComparer.Ordinal)
        {
            ["var"] = new Compiler(Var),
            ["missing"] = new Compiler(Missing),
            ["and"] = new Compiler(operands => Logical(operands, and: true)),
            ["or"] = new Compiler(operands => Logical(operands, and: false)),
            ["object"] = new Compiler(Object),
            ["plain"] = new Compiler(Plain),
        };
        foreach (var operation in JsonLogicOperations.All)
        {
            if (operation.Name is not ("if" or "!" or "===" or "!==") && !compilers.ContainsKey(operation.Name))
                compilers[operation.Name] = new Compiler(operands => Generic(operation.Name, operands));
        }
        return compilers;
    }

    private sealed class Compiler(Func<RuleEvaluator[], RuleEvaluator> compile) : IOperatorCompiler
    {
        public RuleEvaluator Compile(RuleEvaluator[] operands) => compile(operands);
    }

    /// <summary><c>var</c>: the operand is the path literal; the data's members are walked, objects only, and an absent path is <c>null</c>.</summary>
    private static RuleEvaluator Var(RuleEvaluator[] operands)
    {
        string[]? segments = null;
        return (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            segments ??= Literal(operands[0], workspace).Split('.');
            var found = Walk(data, segments);
            return EvalResult.FromElement(in found);
        };
    }

    /// <summary><c>missing</c>: the operands are path literals; <see cref="JsonLogicFunctions.Missing"/> judges each read.</summary>
    private static RuleEvaluator Missing(RuleEvaluator[] operands)
    {
        string[]? paths = null;
        return (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            if (paths is null)
            {
                var literals = new string[operands.Length];
                for (var i = 0; i < operands.Length; i++)
                    literals[i] = Literal(operands[i], workspace);
                paths = literals;
            }
            var root = data;
            return Result(JsonLogicFunctions.Missing(paths, path => Node(Walk(root, path.Split('.')))), workspace);
        };
    }

    /// <summary><c>and</c> and <c>or</c>: lazy, by truthiness, giving <c>true</c> or <c>false</c> (D15).</summary>
    private static RuleEvaluator Logical(RuleEvaluator[] operands, bool and) =>
        (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            foreach (var operand in operands)
            {
                if (operand(in data, workspace).IsTruthy() != and)
                    return Boolean(!and);
            }
            return Boolean(and);
        };

    /// <summary><c>object</c>, rewritten (D16): the operands alternate a field's name and its rule.</summary>
    private static RuleEvaluator Object(RuleEvaluator[] operands) =>
        (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            var result = new JsonObject();
            for (var i = 0; i + 1 < operands.Length; i += 2)
                result[Literal(operands[i], workspace)] = Node(operands[i + 1](in data, workspace).AsElement(workspace));
            return Result(result, workspace);
        };

    /// <summary><c>plain</c>, rewritten (D14): the operand, then the literal choice whether to convert.</summary>
    private static RuleEvaluator Plain(RuleEvaluator[] operands) =>
        (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            var markdown = Node(operands[0](in data, workspace).AsElement(workspace));
            var converts = operands.Length < 2 || operands[1](in data, workspace).AsElement(workspace).ValueKind != CorvusKind.False;
            return Result(JsonLogicFunctions.Apply("plain", [markdown], converts), workspace);
        };

    /// <summary>Every other operation of § 5: its operands evaluated, then <see cref="JsonLogicFunctions.Apply"/>.</summary>
    private static RuleEvaluator Generic(string operation, RuleEvaluator[] operands) =>
        (in CorvusElement data, CorvusWorkspace workspace) =>
        {
            var values = new JsonNode?[operands.Length];
            for (var i = 0; i < operands.Length; i++)
                values[i] = Node(operands[i](in data, workspace).AsElement(workspace));
            return Result(JsonLogicFunctions.Apply(operation, values), workspace);
        };

    // ---- crossing between Corvus's elements and JsonNode ----

    private static string Literal(RuleEvaluator operand, CorvusWorkspace workspace)
    {
        var element = operand(default, workspace).AsElement(workspace);
        return element.ValueKind == CorvusKind.String ? element.GetString() ?? "" : "";
    }

    private static CorvusElement Walk(CorvusElement current, string[] segments)
    {
        foreach (var segment in segments)
        {
            if (current.ValueKind != CorvusKind.Object || !current.TryGetProperty(segment, out var child))
                return JsonLogicHelpers.NullElement();
            current = child;
        }
        return current;
    }

    private static JsonNode? Node(CorvusElement element) =>
        element.ValueKind is CorvusKind.Undefined or CorvusKind.Null ? null : JsonNode.Parse(element.GetRawText(), documentOptions: Reading);

    private static EvalResult Boolean(bool value)
    {
        var element = JsonLogicHelpers.BooleanElement(value);
        return EvalResult.FromElement(in element);
    }

    private static EvalResult Result(JsonNode? value, CorvusWorkspace workspace)
    {
        CorvusElement element;
        if (value is null)
            element = JsonLogicHelpers.NullElement();
        else if (value is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
            element = JsonLogicHelpers.NumberFromSpan(Encoding.UTF8.GetBytes(v.ToJsonString(Writing)), workspace);
        else
            element = Element(value.ToJsonString(Writing));
        return EvalResult.FromElement(in element);
    }

    private static CorvusElement Element(string json)
    {
        using var document = Corvus.Text.Json.ParsedJsonDocument<CorvusElement>.Parse(json, CorvusReading);
        return document.RootElement.Clone();
    }
}
