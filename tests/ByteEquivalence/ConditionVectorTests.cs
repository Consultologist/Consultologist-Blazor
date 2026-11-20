using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498 (part 10 § 2.4): what today's condition and <c>let</c> evaluator answers, frozen for the
/// converter. <c>conditions/expressions.json</c> is hand-written: every condition, <c>case</c> and
/// <c>let</c> of the vendored and fixture packages, one or more expressions per row of § 2.2's
/// table and per refusal of § 2.3, each with what it reads. <c>conditions/vectors.json</c> is
/// written here: per expression, 30's refusal or, over scopes generated from the reads
/// (<see cref="ConditionScopes"/>), 30's answer. Area 10 converts each expression to JSON Logic and
/// must give the same answers, the whitespace-only text of <c>!a</c> on text excepted.
/// </summary>
public class ConditionVectorTests
{
    private const string ExpressionsPath = "conditions/expressions.json";
    private const string VectorsPath = "conditions/vectors.json";

    /// <summary>The left cells of § 2.2's table, as written (a cell's <c>\|</c> is the table's escape of <c>|</c>).</summary>
    private static readonly string[] Rows2_2 =
    [
        "a read: `node.x.value.y`, `node.x.content`, `item.id`, `item.value.y`, `pass.number`, `run.x`",
        "a read of an input: `input.x`, `input.x.y`",
        "`a == b`, `a != b`",
        "`a < b`, `a <= b`, `a > b`, `a >= b`",
        "`a && b`, `a || b`",
        "`!a`, with `a` boolean",
        "`!a`, with `a` text",
        "`+ - * / %` on numbers",
        "`(x | array.size)`, `x.size` on a list",
        "`string.downcase x`, `string.strip x`",
        "`string.starts_with x \"p\"`, `string.ends_with x \"p\"`",
        "`string.contains x \"p\"`",
        "`date.parse \"YYYY-MM-DD\"`",
        "a number, `true`, `false`, a quoted string",
        "`{ a: x, b: y }` (a `let`'s object)",
        "`[x, y]`",
    ];

    /// <summary>The four bullets of § 2.3, as written, each without its closing <c>;</c> or <c>.</c>.</summary>
    private static readonly string[] Refusals2_3 =
    [
        "a builtin or pipe with no 31 operation",
        "a comparison the 31 checker refuses by type (for example `<` on text), using **the checker's own sentence** (part 03)",
        "a read of `profile.*` in a condition, which 31 refuses (#1408)",
        "an assignment, a template, or more than one statement",
    ];

    /// <summary>The § 2.2 expression whose whitespace-only case is the documented difference from <c>blank(x)</c>.</summary>
    private const string NotOnTextId = "row.not-text.node";

    private static readonly JsonSerializerOptions Style = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    private sealed record Expression(string Id, string Kind, string Text, string? LetType, string Origin, IReadOnlyList<(string Read, ConditionScopes.ReadType Type)> Reads);

    private static IReadOnlyList<Expression> Expressions()
    {
        var document = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "expressions.json")))!;

        return document["expressions"]!.AsArray().Select(entry => new Expression(
            entry!["id"]!.GetValue<string>(),
            entry["kind"]!.GetValue<string>(),
            entry["expression"]!.GetValue<string>(),
            entry["letType"]?.GetValue<string>(),
            entry["origin"]!.GetValue<string>(),
            ConditionScopes.ReadsOf(entry["reads"]!.AsObject()))).ToList();
    }

    private static IEnumerable<string> PackageNames() =>
        ByteEquivalenceFixtures.Vendored.Select(package => package.Name).Concat(ByteEquivalenceFixtures.FixturePackages);

    /// <summary>Every condition, case and let of one package, from its typed manifest and its text headers: (origin, kind, expression, let type).</summary>
    private static IEnumerable<(string Origin, string Kind, string Text, string? LetType)> SitesOf(string name)
    {
        var package = ByteEquivalenceFixtures.Load(name);
        var manifest = package.Manifest;
        var origin = $"package {name}, ";

        foreach (var node in manifest.Nodes ?? [])
        {
            foreach (var (key, text) in new[] { ("if", node.If), ("when", node.When), ("while", node.While), ("until", node.Until) })
            {
                if (text is not null)
                {
                    yield return ($"{origin}node {node.Id}, {key}", "condition", text, null);
                }
            }

            if (node.Case is { } subject)
            {
                yield return ($"{origin}node {node.Id}, case", "case", subject, null);
            }

            if (node.Let is { } let)
            {
                yield return ($"{origin}node {node.Id}, let", "let", let, node.Type);
            }

            foreach (var macro in node.Macros ?? [])
            {
                if ((macro.If ?? macro.When) is { } gate)
                {
                    yield return ($"{origin}node {node.Id} macro {macro.Id}, if", "condition", gate, null);
                }
            }
        }

        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            if ((deliverable.If ?? deliverable.When) is { } gate)
            {
                yield return ($"{origin}deliverable {deliverable.Id}, if", "condition", gate, null);
            }

            foreach (var macro in deliverable.Macros ?? [])
            {
                if ((macro.If ?? macro.When) is { } macroGate)
                {
                    yield return ($"{origin}deliverable {deliverable.Id} macro {macro.Id}, if", "condition", macroGate, null);
                }
            }
        }

        // Every snippet, folder item, macro and macro-folder item file's header, by path.
        foreach (var (path, header) in (package.Texts?.Headers ?? new Dictionary<string, WorkflowTextHeader>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (header.If is { } gate)
            {
                yield return ($"{origin}file {path}, if", "condition", gate, null);
            }
        }
    }

    [Fact]
    public void EveryConditionAndLetOfThePackages_IsInTheExpressions()
    {
        var expressions = Expressions();
        var sites = PackageNames().SelectMany(SitesOf).ToList();

        var missing = sites
            .Where(site => !expressions.Any(e => e.Origin == site.Origin && e.Kind == site.Kind && e.Text == site.Text && e.LetType == site.LetType))
            .Select(site => $"{site.Origin}: {site.Kind} `{site.Text}`")
            .ToList();
        var stale = expressions
            .Where(e => e.Origin.StartsWith("package ", StringComparison.Ordinal) && !sites.Any(site => site.Origin == e.Origin))
            .Select(e => $"{e.Id}: {e.Origin}")
            .ToList();

        Assert.True(missing.Count == 0, "The expressions lack:\n" + string.Join("\n", missing));
        Assert.True(stale.Count == 0, "The expressions name sites no package has:\n" + string.Join("\n", stale));
    }

    /// <summary>
    /// A package expression's reads are what its package declares: an input's type (and values,
    /// fields) as the declaration gives them, a node's as the engine types its format
    /// (<see cref="ConsultRenderPayload.FormatTypeOf"/>), an object node's schema its format's.
    /// The scope builder declares what the reads say, so this is what makes a package's scopes mean
    /// what the engine means.
    /// </summary>
    [Fact]
    public void APackageExpressionsReads_AreItsPackagesDeclarations()
    {
        var wrong = new List<string>();

        foreach (var expression in Expressions().Where(e => e.Origin.StartsWith("package ", StringComparison.Ordinal)))
        {
            var name = expression.Origin["package ".Length..expression.Origin.IndexOf(',')];
            var package = ByteEquivalenceFixtures.Load(name);

            foreach (var (read, type) in expression.Reads)
            {
                var parts = read.Split('.', 2);
                var problem = parts[0] switch
                {
                    "input" => InputMismatch(package, parts[1], type),
                    "node" => NodeMismatch(package, parts[1], type),
                    _ => null,
                };

                if (problem is not null)
                {
                    wrong.Add($"{expression.Id} reads {read}: {problem}");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    private static string? InputMismatch(WorkflowPackage package, string id, ConditionScopes.ReadType type)
    {
        var declared = package.Manifest.Inputs?.FirstOrDefault(input => input.Id == id);
        if (declared is null)
        {
            return "the package declares no such input";
        }

        var read = ConditionScopes.Declaration(id, type);
        return Describe(read, onlyFields: read.Fields?.Select(f => f.Id).ToHashSet()) == Describe(declared, onlyFields: read.Fields?.Select(f => f.Id).ToHashSet())
            ? null
            : $"declared {Describe(declared, null)}, read as {Describe(read, null)}";
    }

    /// <summary>An input's type, values and (the read's) fields with theirs, as one string.</summary>
    private static string Describe(WorkflowInputSpec input, ISet<string>? onlyFields) =>
        $"{WorkflowInputTypes.Of(input)} [{string.Join(",", input.Values ?? [])}] items:{input.Items?.Type} {{"
        + string.Join(", ", (input.Fields ?? []).Where(f => onlyFields?.Contains(f.Id) != false).Select(f => $"{f.Id}: {f.Type ?? "text"} [{string.Join(",", f.Values ?? [])}]"))
        + "}";

    private static string? NodeMismatch(WorkflowPackage package, string id, ConditionScopes.ReadType type)
    {
        var node = package.Manifest.Nodes?.FirstOrDefault(n => n.Id == id);
        if (node is null)
        {
            return "the package has no such node";
        }

        var engineType = ConsultRenderPayload.FormatTypeOf(package, node);
        var readType = type.Type switch { "classification" => WorkflowInputTypes.Enum, var other => other };
        if (engineType != readType)
        {
            return $"the engine types it {engineType}, read as {type.Type}";
        }

        if (type.Type == "classification" && node.With?.GetValueOrDefault("values") is { } values
            && !JsonNode.DeepEquals(values, new JsonArray(type.Values!.Select(v => (JsonNode?)v).ToArray())))
        {
            return $"the classifier answers {values.ToJsonString()}, read as {string.Join(",", type.Values!)}";
        }

        if (type.Schema is { } schema)
        {
            var file = package.Manifest.Formats!.Single(format => format.Id == node.Format).File;
            var formatSchema = JsonNode.Parse(package.SourceFiles![file])!["schema"];
            if (!JsonNode.DeepEquals(formatSchema, schema))
            {
                return $"the schema is not format {node.Format}'s";
            }
        }

        return null;
    }

    [Fact]
    public void EveryRowOf2_2_AndEveryRefusalOf2_3_HasAnExpression()
    {
        var origins = Expressions().Select(e => e.Origin).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            Rows2_2.Select(row => $"§ 2.2: {row}").Order(StringComparer.Ordinal),
            origins.Where(origin => origin.StartsWith("§ 2.2: ", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(
            Refusals2_3.Select(bullet => $"§ 2.3: {bullet}").Order(StringComparer.Ordinal),
            origins.Where(origin => origin.StartsWith("§ 2.3: ", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheEvaluatorAt30_GivesTheCapturedAnswers()
    {
        var vectors = new JsonArray(Expressions().Select(e => (JsonNode?)Vector(e)).ToArray());

        ByteEquivalenceFixtures.Matches(VectorsPath, new JsonObject { ["vectors"] = vectors }.ToJsonString(Style) + "\n");
    }

    [Fact]
    public void AWhitespaceOnlyText_IsTruthyAt30()
    {
        var notOnText = Expressions().Single(e => e.Id == NotOnTextId);
        var (read, _) = Assert.Single(notOnText.Reads);

        var whitespace = Vector(notOnText)["cases"]!.AsArray()
            .Single(c => c!["scope"]![read] is JsonValue value && value.TryGetValue<string>(out var text) && text == " ");

        // blank(" ") is true at 31; at 30 " " is a present text, so !" " is false.
        Assert.False(whitespace!["result"]!.GetValue<bool>());
        Assert.Null(whitespace["error"]);
    }

    /// <summary>
    /// The doc comment on <see cref="WorkflowConditionAt25.Holds"/>: an unsupplied optional text or
    /// enum input reads "", a number, date or boolean null, an array empty (and falsy), an object
    /// empty — through the scope builder, which is the engine's effective-input map.
    /// </summary>
    [Theory]
    [InlineData("input.note == \"\"", "text")]
    [InlineData("input.kind == \"\"", "enum")]
    [InlineData("input.age == null", "number")]
    [InlineData("input.seen_on == null", "date")]
    [InlineData("input.urgent == null", "boolean")]
    [InlineData("(input.docs | array.size) == 0 && !input.docs", "array")]
    [InlineData("!input.patient", "object")]
    public void AnAbsentInput_ReadsAsTheEngineMakesIt(string condition, string type)
    {
        var id = condition.Split('.', 2)[1].Split(' ', '|', ')')[0];
        var readType = type switch
        {
            "enum" => new ConditionScopes.ReadType("enum", ["new", "review"]),
            "array" => new ConditionScopes.ReadType("array", Items: new ConditionScopes.ReadType("text")),
            "object" => new ConditionScopes.ReadType("object", Fields: [("age", new ConditionScopes.ReadType("number"))]),
            _ => new ConditionScopes.ReadType(type),
        };

        var (scope, refusal) = ConditionScopes.Build([($"input.{id}", readType)], [null]);

        Assert.Null(refusal);
        Assert.True(WorkflowConditionAt25.Holds("Condition 'absent'", condition, scope!, TemplatePipes.Engine));
    }

    // ---- 30's answers ------------------------------------------------------------------

    private static string SubjectOf(Expression e) => e.Kind switch
    {
        "let" => $"Let '{e.Id}'",
        "case" => $"Node '{e.Id}' case",
        _ => $"Condition '{e.Id}'",
    };

    private static JsonObject Vector(Expression e)
    {
        var subject = SubjectOf(e);
        var refused = Refusal(e, subject);
        var cases = new JsonArray();

        if (refused is null)
        {
            foreach (var values in ConditionScopes.Cases(e.Reads, e.Text))
            {
                var (result, error) = Answer(e, subject, values);
                cases.Add(new JsonObject
                {
                    ["scope"] = ConditionScopes.ScopeJson(e.Reads, values),
                    ["result"] = result,
                    ["error"] = error,
                });
            }
        }

        return new JsonObject { ["id"] = e.Id, ["refused"] = refused, ["cases"] = cases };
    }

    /// <summary>30's refusal: a template as the walk refuses it, anything else as <see cref="WorkflowConditionAt25.TryParse"/> does (the sentence the judge throws).</summary>
    private static string? Refusal(Expression e, string subject)
    {
        var text = e.Text.Trim();

        if (text.Contains("{{", StringComparison.Ordinal) || text.Contains("{%", StringComparison.Ordinal))
        {
            var walk = e.Kind == "let"
                ? WorkflowLetAt30.Walk(subject, text, e.LetType, TemplateReferenceContext.Empty)
                : WorkflowConditionAt25.Walk(subject, text, TemplateReferenceContext.Empty);
            return walk.References.Errors[0];
        }

        return WorkflowConditionAt25.TryParse(text, out _, out var error) ? null : $"{subject} {error}";
    }

    /// <summary>
    /// One case: a condition judged by <see cref="WorkflowConditionAt25.Holds"/>; a case's subject
    /// evaluated, with the sentence <see cref="WorkflowFlowDecision.Decide(string, WorkflowFlowNodes.Kind, string, IReadOnlyList{WorkflowFlowNodes.Body}, TemplateRenderScope, IReadOnlyDictionary{string, Func{TemplateNodeOutput, string}}?)"/>
    /// throws when it is not a value; a let evaluated and stored by <see cref="WorkflowLetAt30.ValueOf"/>,
    /// its errors joined as the activity joins them.
    /// </summary>
    private static (JsonNode? Result, string? Error) Answer(Expression e, string subject, IReadOnlyList<JsonNode?> values)
    {
        var (scope, refusal) = ConditionScopes.Build(e.Reads, values);
        if (scope is null)
        {
            return (null, refusal);
        }

        try
        {
            switch (e.Kind)
            {
                case "condition":
                    return (WorkflowConditionAt25.Holds(subject, e.Text, scope, TemplatePipes.Engine), null);

                case "case":
                    var subjectValue = WorkflowLetAt30.ToJson(WorkflowLetAt30.Evaluate(subject, e.Text, scope, TemplatePipes.Engine));
                    try
                    {
                        WorkflowFlowDecision.Decide(e.Id, WorkflowFlowNodes.Kind.Case, e.Text, [], scope, TemplatePipes.Engine);
                        return (subjectValue, null);
                    }
                    catch (InvalidOperationException notAValue)
                    {
                        return (subjectValue, notAValue.Message);
                    }

                case "let":
                    var value = WorkflowLetAt30.Evaluate(subject, e.Text, scope, TemplatePipes.Engine);
                    var (stored, errors) = WorkflowLetAt30.ValueOf(subject, e.LetType!, value);
                    var result = value is string ? JsonValue.Create(stored) : JsonNode.Parse(stored);
                    return (result, errors.Count > 0 ? string.Join(" ", errors) : null);

                default:
                    throw new ArgumentException($"Expression '{e.Id}' has kind '{e.Kind}'; a kind is condition, case or let.");
            }
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }
    }
}
