using System.Text.Json.Nodes;

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
}
