using System.Collections;
using System.Globalization;
using System.Text.Json.Nodes;
using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// v30 (#1339 A3, package-format-v30-design.md § 4): a <c>let</c> node — one
/// Scriban expression computed with no model call, its value typed by
/// <c>type</c>: <c>text</c>, <c>number</c>, <c>boolean</c>, or a declared object
/// format (no bare list — a list is a format whose field is an array). The
/// expression reads what a template reads at the node's place, through the same
/// walker; it calls pure builtins only (<see cref="TemplateObjects.PureBuiltins"/>);
/// a type the walk can know and that contradicts <c>type</c> is refused here, the
/// rest is the run's <c>onInvalid</c> (fail or flag — there is no model to retry).
/// </summary>
public static class WorkflowLetAt30
{
    public const string Text = WorkflowInputTypes.Text;
    public const string Number = WorkflowInputTypes.Number;
    public const string Boolean = WorkflowInputTypes.Boolean;

    /// <summary>The scalar types a let names directly; anything else names a format.</summary>
    public static readonly IReadOnlyList<string> ScalarTypes = new[] { Text, Number, Boolean };

    /// <summary>The internal producer of a scalar let's synthesized format — never a format file's.</summary>
    public const string Producer = "let";

    /// <summary>#1343: the computed operation a let runs as — beside <see cref="WorkflowCheckOps.TermsSubsetAt25"/>, with no catalogue entry.</summary>
    public const string Op = "let";

    /// <summary>
    /// A scalar type's shape as a format spec — its id is the type's name, its
    /// schema the JSON type, so <c>node.&lt;let&gt;.value</c> types as the walker types a
    /// format's value; null for a format id.
    /// </summary>
    public static WorkflowFormatSpec? ScalarSpecOf(string? type) => type switch
    {
        Text => new WorkflowFormatSpec(Text, Producer),
        Number => new WorkflowFormatSpec(Number, Producer, Schema: new JsonObject { ["type"] = "number" }),
        Boolean => new WorkflowFormatSpec(Boolean, Producer, Schema: new JsonObject { ["type"] = "boolean" }),
        _ => null,
    };

    /// <summary>
    /// Walks a let's expression: blank, a template, or more than one statement are
    /// refused; the walk records its reads (the let's edges) and refuses impure
    /// builtins; a knowable kind that contradicts <paramref name="type"/> is refused.
    /// </summary>
    public static WorkflowConditionAt25.ConditionWalk Walk(string subject, string? text, string? type, TemplateReferenceContext context)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var collector = new TemplateReferenceCollector();

        if (trimmed.Length == 0)
        {
            collector.Errors.Add($"{subject} is blank; a let computes one expression.");
            return new WorkflowConditionAt25.ConditionWalk(collector.ToReferences(), null);
        }

        if (trimmed.Contains("{{", StringComparison.Ordinal) || trimmed.Contains("{%", StringComparison.Ordinal))
        {
            collector.Errors.Add($"{subject} is a template, not an expression; write the expression bare (node.<id>.value | array.size).");
            return new WorkflowConditionAt25.ConditionWalk(collector.ToReferences(), null);
        }

        if (!WorkflowConditionAt25.TryParse(trimmed, out var expression, out var parseError))
        {
            collector.Errors.Add(parseError is not null && parseError.StartsWith("is not one expression", StringComparison.Ordinal)
                ? $"{subject} is not one expression; a let computes one expression."
                : $"{subject} {parseError}");
            return new WorkflowConditionAt25.ConditionWalk(collector.ToReferences(), null);
        }

        var (references, kind) = TemplateReferenceWalker.WalkValue(subject, expression!, context);

        if (references.IsValid && type is not null && Contradicts(kind, type))
        {
            references = references with { Errors = new[] { $"{subject} yields {Describe(kind)}; it declares type {type}." } };
        }

        return new WorkflowConditionAt25.ConditionWalk(references, references.IsValid ? expression : null);
    }

    /// <summary>Whether a kind the walk typed cannot be a value of the declared type; an unknown kind never contradicts.</summary>
    private static bool Contradicts(string kind, string type)
    {
        if (kind == TemplateValueShape.Unknown)
        {
            return false;
        }

        return type switch
        {
            Text => kind is not (WorkflowInputTypes.Text or WorkflowInputTypes.Enum or "item_id"),
            Number => kind != WorkflowInputTypes.Number,
            Boolean => kind != "truth" && kind != WorkflowInputTypes.Boolean,
            // A format: its value is an object.
            _ => kind != WorkflowInputTypes.Object,
        };
    }

    private static string Describe(string kind) => kind switch
    {
        "truth" or WorkflowInputTypes.Boolean => "a boolean",
        "item_id" => "an item id",
        WorkflowInputTypes.Enum => "an enum value",
        WorkflowInputTypes.Array => "an array",
        WorkflowInputTypes.Object => "an object",
        _ => $"a {kind}",
    };

    // ---- #1343 B4 (design § 4.2): the run's half --------------------------------------------

    /// <summary>
    /// The value a let's expression yields over a render scope — the raw Scriban
    /// value (a decimal or int, a bool, a string, a ScriptObject or ScriptArray, or
    /// null). The validator proved it parses and calls only pure builtins; a
    /// failure here is loud, with the subject.
    /// </summary>
    public static object? Evaluate(string subject, string text, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        if (!WorkflowConditionAt25.TryParse(text.Trim(), out var expression, out var error) || expression is null)
        {
            throw new InvalidOperationException($"{subject} {error}");
        }

        var context = TemplateRenderer.ContextFor(subject, scope, pipes);
        return TemplateRenderer.Unwrap(subject, "evaluate", () => context.Evaluate(expression));
    }

    /// <summary>
    /// A let's value as its node stores it, and whether it is of its type: a text
    /// let's string itself (decided 2026-11-11 — a deliverable composes it bare);
    /// a number, a boolean or a format's object as canonical JSON (a number with no
    /// trailing zeros, an object's keys sorted). A value not of the type is still
    /// stored when it can be written (<c>onInvalid: flag</c> keeps it) with the
    /// sentence naming the mismatch; a format's object is checked against the
    /// format's schema too.
    /// </summary>
    public static (string Stored, IReadOnlyList<string> Errors) ValueOf(string subject, string type, object? value, JsonNode? formatSchema = null)
    {
        var json = ToJson(value);
        var stored = value is string text ? text : Canonical(json);

        var holds = type switch
        {
            Text => value is string,
            Number => IsNumber(value),
            Boolean => value is bool,
            _ => value is ScriptObject,
        };

        if (!holds)
        {
            return (stored, new[] { $"{subject} yielded {WorkflowConditionAt25.DescribeValue(value)}; it declares type {type}." });
        }

        if (type is Text or Number or Boolean || formatSchema is null)
        {
            return (stored, Array.Empty<string>());
        }

        var errors = JsonSchemaSubsetValidator.Validate(json, formatSchema);
        return (stored, errors.Select(error => $"{subject}'s value does not match format {type}: {error}").ToList());
    }

    private static bool IsNumber(object? value) => value is int or long or decimal or double or float or short or byte or uint or ulong;

    /// <summary>The inverse of <see cref="TemplateRenderer.FromJson"/>: Scriban's values as JSON, every number a normalized decimal.</summary>
    internal static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        _ when IsNumber(value) => JsonValue.Create(Normalized(value!)),
        ScriptObject obj => new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, ToJson(pair.Value)))),
        IDictionary map => new JsonObject(map.Keys.Cast<object>().Select(key => KeyValuePair.Create(Convert.ToString(key, CultureInfo.InvariantCulture)!, ToJson(map[key])))),
        IEnumerable list => new JsonArray(list.Cast<object?>().Select(ToJson).ToArray()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    /// <summary>A number with no trailing zeros, so 2.50 and 2.5 store alike.</summary>
    private static decimal Normalized(object number)
    {
        decimal value;
        try
        {
            value = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"A let's number is out of range: {Convert.ToString(number, CultureInfo.InvariantCulture)}.");
        }

        return value / 1.000000000000000000000000000000000m;
    }

    /// <summary>Sorted keys at every level, compact — the bytes the output hash covers.</summary>
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{System.Text.Json.JsonSerializer.Serialize(pair.Key)}:{Canonical(pair.Value)}")) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };
}
