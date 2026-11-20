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
/// compares its answers with these; where 30's <c>!a</c> on a text and a <c>blank(a)</c> differ is
/// recorded here, not decided (the README's <c>!x</c> section).
/// </summary>
public class ConditionVectorTests
{
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

    private sealed record Expression(string Id, string Kind, string Text, string? LetType, string Origin, string? Note, IReadOnlyList<(string Read, ConditionScopes.ReadType Type)> Reads);

    private static IReadOnlyList<Expression> Expressions()
    {
        var document = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "expressions.json")))!;

        return document["expressions"]!.AsArray().Select(entry => new Expression(
            entry!["id"]!.GetValue<string>(),
            entry["kind"]!.GetValue<string>(),
            entry["expression"]!.GetValue<string>(),
            entry["letType"]?.GetValue<string>(),
            entry["origin"]!.GetValue<string>(),
            entry["note"]?.GetValue<string>(),
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
    /// A package expression's reads are what its package declares: an input's type, values,
    /// required and fields (each field's type, values and required) as the declaration gives them,
    /// an item's value fields as its loop's input declares them, a node's type as the engine types
    /// its format (<see cref="ConsultRenderPayload.FormatTypeOf"/>), an object node's schema its
    /// format's; a read marked settled says why in the entry's note. The scope builder declares
    /// what the reads say, so this is what makes a package's scopes mean what the engine means.
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
                    "node" => NodeMismatch(package, parts[1], type) ?? (type.Settled && expression.Note is null ? "settled, with no note saying why" : null),
                    "item" when type.Fields is [("value", var value)] => ItemMismatch(package, expression.Origin, value),
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

    /// <summary>An item value's fields against the input its loop runs over (the loop whose body holds the site's node).</summary>
    private static string? ItemMismatch(WorkflowPackage package, string origin, ConditionScopes.ReadType value)
    {
        var nodeId = origin.Split(", ")[1].Split(' ')[1];
        var loop = package.Manifest.Nodes?.FirstOrDefault(n => n.For is { } f && f.StartsWith("item in input.", StringComparison.Ordinal) && n.Nodes?.Contains(nodeId) == true);
        if (loop is null)
        {
            return $"node {nodeId} is in no loop over an input";
        }

        var collection = loop.For!["item in input.".Length..];
        var declared = package.Manifest.Inputs!.Single(input => input.Id == collection);
        var read = ConditionScopes.Declaration(collection, new ConditionScopes.ReadType("array", Items: value));
        var fields = value.Fields?.Select(f => f.Name).ToHashSet();
        return Describe(read, fields, withRequired: false) == Describe(declared, fields, withRequired: false)
            ? null
            : $"input {collection} declares {Describe(declared, fields, withRequired: false)}, read as {Describe(read, fields, withRequired: false)}";
    }

    /// <summary>An input's type, values, required and (the read's) fields with theirs, as one string.</summary>
    private static string Describe(WorkflowInputSpec input, ISet<string>? onlyFields, bool withRequired = true) =>
        $"{WorkflowInputTypes.Of(input)} [{string.Join(",", input.Values ?? [])}]{(withRequired ? $" required:{input.Required}" : "")} items:{input.Items?.Type} {{"
        + string.Join(", ", (input.Fields ?? []).Where(f => onlyFields?.Contains(f.Id) != false)
            .Select(f => $"{f.Id}: {f.Type ?? "text"} [{string.Join(",", f.Values ?? [])}] required:{f.Required}"))
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

        ByteEquivalenceFixtures.Matches(VectorsPath, ByteEquivalenceFixtures.WriteJson(new JsonObject { ["vectors"] = vectors }));
    }

    // ---- !a on a text, at 30 ------------------------------------------------------------
    // 30's truthiness: only null, false, an empty array or object and a skipped node are falsy
    // (PromptTemplateRenderer.RenderingContext.ToBool). So !x on a text holds only for null —
    // a text "" and a whitespace-only text are both present. The README says where a blank(x)
    // would differ; the fixture records 30 and takes no side.

    /// <summary>The case of the !a-on-text expression whose read has this value (the string, or <see cref="ConditionScopes.Absent"/>).</summary>
    private static JsonNode NotOnTextCase(string value)
    {
        var notOnText = Expressions().Single(e => e.Id == NotOnTextId);
        var (read, _) = Assert.Single(notOnText.Reads);

        return Vector(notOnText)["cases"]!.AsArray()
            .Single(c => c!["scope"]![read] is JsonValue v && v.TryGetValue<string>(out var text) && text == value)!;
    }

    [Fact]
    public void AWhitespaceOnlyText_IsTruthyAt30()
    {
        var whitespace = NotOnTextCase(" ");

        Assert.False(whitespace["result"]!.GetValue<bool>());
        Assert.Null(whitespace["error"]);
    }

    [Fact]
    public void AnEmptyText_IsTruthyAt30_Too()
    {
        var empty = NotOnTextCase("");

        Assert.False(empty["result"]!.GetValue<bool>());
        Assert.Null(empty["error"]);
    }

    [Fact]
    public void OnlyANullText_IsFalsyAt30()
    {
        // An absent node reads its content as null — the one text value !x holds for.
        Assert.True(NotOnTextCase(ConditionScopes.Absent)["result"]!.GetValue<bool>());
        Assert.False(NotOnTextCase("lorem")["result"]!.GetValue<bool>());
    }

    [Fact]
    public void TheCases_AreTheProductInDeclaredOrder_CutAt200()
    {
        // Four text reads, four values each (absent, "", " ", "lorem"): 256 in the product.
        var text = new ConditionScopes.ReadType("text");
        var reads = new[] { "a", "b", "c", "d" }.Select(id => ($"input.{id}", text)).ToList();

        var cases = ConditionScopes.Cases(reads, "input.a == input.b");

        Assert.Equal(ConditionScopes.MaxCases, cases.Count);
        Assert.All(cases[0], value => Assert.Null(value));
        // Case 199 is 3·64 + 0·16 + 1·4 + 3 in the product, the first read slowest: lorem, absent, "", lorem.
        Assert.Equal(["\"lorem\"", "null", "\"\"", "\"lorem\""], cases[199].Select(value => value?.ToJsonString() ?? "null"));
    }

    /// <summary>
    /// A let has no bare list at 30, so <c>[x, y]</c> appears in the fixture only inside an object:
    /// the walk refuses a list whose kind it knows against every type, and a list it cannot type
    /// fails <see cref="WorkflowLetAt30.ValueOf"/> whatever the type.
    /// </summary>
    [Theory]
    [InlineData("text")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("pair_format")]
    public void ABareListLet_IsRefusedAt30(string type)
    {
        var docs = ConditionScopes.Declaration("docs", new ConditionScopes.ReadType("array", Items: new ConditionScopes.ReadType("text")));
        var context = TemplateReferenceContext.Empty with
        {
            Inputs = new Dictionary<string, WorkflowDeclarationNode> { ["docs"] = WorkflowDeclarationNode.Of(docs) },
        };

        var walk = WorkflowLetAt30.Walk("Let 'pair'", "input.docs", type, context);
        var (_, errors) = WorkflowLetAt30.ValueOf("Let 'pair'", type, new Scriban.Runtime.ScriptArray { "a", "b" });

        Assert.Equal([$"Let 'pair' yields an array; it declares type {type}."], walk.References.Errors);
        Assert.Equal([$"Let 'pair' yielded a list; it declares type {type}."], errors);
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
