using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498 (part 10 § 2.4): the scopes an expression of <c>conditions/expressions.json</c> is judged
/// over. The values are generated from the expression's <c>reads</c> (each type's values, the
/// product over the reads, cut to <see cref="MaxCases"/>); the scope is built by the engine's own
/// functions, so absence is what the engine makes of it: the inputs pass through
/// <see cref="ConsultGenerationJobStarter.NormalizeInputs"/> and
/// <see cref="ConsultGenerationJobStarter.ResolveEffectiveInputs"/> (an unsupplied optional input
/// becomes the effective map's <c>""</c>, which the render types as <c>""</c>, null, an empty array
/// or an empty object), and the render scope is <see cref="ConsultRenderPayload.ScopeFor"/>'s over
/// a package declaring exactly what is read. An absent node is a node with no output, as the
/// engine's skipped list carries one (a flow that took no branch, a while's body before its first
/// pass): <c>.content</c> and <c>.value</c> read null.
/// </summary>
internal static class ConditionScopes
{
    /// <summary>A read's value in a case's scope when nothing is supplied.</summary>
    public const string Absent = "<absent>";

    public const int MaxCases = 200;

    /// <summary>The input every scope supplies, so the start never refuses a request with no inputs at all; no expression reads it.</summary>
    public const string ConsultDraft = "consult_draft";

    private const string ItemsInput = "items";

    private static readonly string[] Dates = ["2026-11-09", "2026-11-10", "2026-11-11"];

    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// A read's declared type: <c>text</c>, <c>enum</c> and <c>classification</c> (with
    /// <c>values</c>), <c>number</c>, <c>boolean</c>, <c>date</c>, <c>array</c> (with <c>items</c>),
    /// <c>object</c> (with <c>fields</c>, or a node's JSON Schema read into fields), and
    /// <c>item</c> (its <c>id</c> or its <c>value</c>).
    /// </summary>
    internal sealed record ReadType(
        string Type,
        IReadOnlyList<string>? Values = null,
        ReadType? Items = null,
        IReadOnlyList<(string Name, ReadType Type)>? Fields = null,
        JsonObject? Schema = null);

    /// <summary>One expression's reads, in declared order.</summary>
    public static IReadOnlyList<(string Read, ReadType Type)> ReadsOf(JsonObject reads) =>
        reads.Select(pair => (pair.Key, TypeOf(pair.Value!))).ToList();

    private static ReadType TypeOf(JsonNode node)
    {
        if (node is JsonValue)
        {
            return new ReadType(node.GetValue<string>());
        }

        var declared = node.AsObject();
        if (declared["schema"] is JsonObject schema)
        {
            return FromSchema(schema) with { Schema = schema };
        }

        if (!declared.ContainsKey("type"))
        {
            // item: { "id": "text" } or { "value": <type> }
            return new ReadType("item", Fields: declared.Select(pair => (pair.Key, TypeOf(pair.Value!))).ToList());
        }

        return new ReadType(
            declared["type"]!.GetValue<string>(),
            declared["values"]?.AsArray().Select(value => value!.GetValue<string>()).ToList(),
            declared["items"] is { } items ? TypeOf(items) : null,
            declared["fields"]?.AsObject().Select(pair => (pair.Key, TypeOf(pair.Value!))).ToList());
    }

    private static ReadType FromSchema(JsonObject schema) => schema["type"]?.GetValue<string>() switch
    {
        "object" => new ReadType("object", Fields: (schema["properties"]?.AsObject() ?? new JsonObject())
            .Select(pair => (pair.Key, FromSchema(pair.Value!.AsObject()))).ToList()),
        "array" => new ReadType("array", Items: FromSchema(schema["items"]!.AsObject())),
        "string" when schema["enum"] is JsonArray values => new ReadType("enum", values.Select(value => value!.GetValue<string>()).ToList()),
        "string" => new ReadType("text"),
        "number" or "integer" => new ReadType("number"),
        "boolean" => new ReadType("boolean"),
        var other => throw new InvalidOperationException($"A node schema of type '{other}' has no generated values."),
    };

    /// <summary>The quoted string literals and the numbers of an expression.</summary>
    internal sealed record Literals(IReadOnlyList<string> Strings, IReadOnlyList<decimal> Numbers)
    {
        private static readonly Regex Quoted = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
        private static readonly Regex Number = new(@"(?<![\w.])\d+(?:\.\d+)?(?![\w.])", RegexOptions.Compiled);

        public static Literals Of(string expression) => new(
            Quoted.Matches(expression).Select(match => JsonNode.Parse(match.Value)!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToList(),
            Number.Matches(Quoted.Replace(expression, "\"\"")).Select(match => decimal.Parse(match.Value, CultureInfo.InvariantCulture)).Distinct().ToList());
    }

    /// <summary>The cases: for each read its values (null is absent), the product in declared order, the first <see cref="MaxCases"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<JsonNode?>> Cases(IReadOnlyList<(string Read, ReadType Type)> reads, string expression)
    {
        var literals = Literals.Of(expression);
        IEnumerable<IReadOnlyList<JsonNode?>> product = [[]];

        foreach (var (read, type) in reads)
        {
            var values = ValuesOfRead(read, type, literals);
            product = product.SelectMany(prefix => values.Select(value => (IReadOnlyList<JsonNode?>)[.. prefix, value?.DeepClone()]));
        }

        return product.Take(MaxCases).ToList();
    }

    /// <summary>A case's scope as written: each read and its value, or <see cref="Absent"/>.</summary>
    public static JsonObject ScopeJson(IReadOnlyList<(string Read, ReadType Type)> reads, IReadOnlyList<JsonNode?> values)
    {
        var scope = new JsonObject();
        for (var i = 0; i < reads.Count; i++)
        {
            scope[reads[i].Read] = values[i]?.DeepClone() ?? JsonValue.Create(Absent);
        }

        return scope;
    }

    private static IReadOnlyList<JsonNode?> ValuesOfRead(string read, ReadType type, Literals literals) => read switch
    {
        // The run's and the profile's facts, the pass and the item are never absent.
        "run.date" => Dates.Select(date => (JsonNode?)date).ToList(),
        "profile.name" => ValuesOf(type, literals).Where(value => value is not null).ToList(),
        "pass.number" => new JsonNode?[] { 1, 2, 3 },
        "item" when type.Fields is [("id", _)] => literals.Strings.Prepend("1").Distinct(StringComparer.Ordinal).Select(id => (JsonNode?)new JsonObject { ["id"] = id }).ToList(),
        "item" when type.Fields is [("value", var value)] => ValuesOf(value, literals).Where(v => v is not null).Select(v => (JsonNode?)new JsonObject { ["value"] = v }).ToList(),
        "item" => throw new InvalidOperationException("An item read declares its id or its value."),
        _ => ValuesOf(type, literals),
    };

    /// <summary>A type's values, absent (null) first.</summary>
    private static IReadOnlyList<JsonNode?> ValuesOf(ReadType type, Literals literals)
    {
        IEnumerable<JsonNode?> values = type.Type switch
        {
            "text" => new[] { "", " ", "lorem" }.Concat(literals.Strings).Distinct(StringComparer.Ordinal).Select(text => (JsonNode?)text),
            "enum" or "classification" => type.Values!.Select(value => (JsonNode?)value),
            "number" => literals.Numbers.SelectMany(n => new[] { n - 1, n, n + 1 }).Prepend(0m).Distinct().Select(n => (JsonNode?)n),
            "boolean" => new JsonNode?[] { true, false },
            "date" => Dates.Select(date => (JsonNode?)date),
            "array" => ArraysOf(type.Items!, literals),
            "object" => ObjectsOf(type.Fields!, literals),
            var other => throw new InvalidOperationException($"A read of type '{other}' has no generated values."),
        };

        return values.Prepend(null).ToList();
    }

    private static IEnumerable<JsonNode?> ArraysOf(ReadType items, Literals literals)
    {
        var present = ValuesOf(items, literals).Where(value => value is not null).ToList();
        JsonNode? At(int i) => present[i % present.Count]!.DeepClone();

        return [new JsonArray(), new JsonArray(At(0)), new JsonArray(At(0), At(1), At(2))];
    }

    /// <summary>Every field at its first present value, then each field through its own values with the others there.</summary>
    private static IEnumerable<JsonNode?> ObjectsOf(IReadOnlyList<(string Name, ReadType Type)> fields, Literals literals)
    {
        var perField = fields.Select(field => ValuesOf(field.Type, literals)).ToList();

        JsonObject With(int varied, JsonNode? value)
        {
            var obj = new JsonObject();
            for (var i = 0; i < fields.Count; i++)
            {
                var chosen = i == varied ? value : perField[i].First(v => v is not null);
                if (chosen is not null)
                {
                    obj[fields[i].Name] = chosen.DeepClone();
                }
            }

            return obj;
        }

        var objects = new List<JsonNode?> { With(-1, null) };
        for (var i = 0; i < fields.Count; i++)
        {
            objects.AddRange(perField[i].Select(value => With(i, value)));
        }

        return objects.DistinctBy(obj => obj!.ToJsonString(Compact), StringComparer.Ordinal);
    }

    // ---- the scope, built by the engine ---------------------------------------------------

    /// <summary>
    /// The render scope one case is judged over, or the start's sentence when the engine refuses
    /// the inputs (then nothing is judged).
    /// </summary>
    public static (TemplateRenderScope? Scope, string? Refusal) Build(IReadOnlyList<(string Read, ReadType Type)> reads, IReadOnlyList<JsonNode?> values)
    {
        var inputs = new List<WorkflowInputSpec> { new(ConsultDraft, "Consult draft") };
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            [ConsultDraft] = ConsultInputValue.OfText("Margaret Okafor, 71, seen in clinic for review of her breathlessness."),
        };
        var nodes = new List<WorkflowNodeSpec>();
        var formats = new List<WorkflowFormatReference>();
        var sourceFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<string>();
        IReadOnlyDictionary<string, string>? item = null;
        string? forEach = null;
        WorkflowInputSpec? itemsInput = null;
        ConsultPassPayload? pass = null;
        var runDate = "2026-11-10";
        var profileName = "Dr A. Sample";

        for (var i = 0; i < reads.Count; i++)
        {
            var (read, type) = reads[i];
            var value = values[i];

            switch (read.Split('.', 2))
            {
                case ["input", var id]:
                    inputs.Add(Declaration(id, type));
                    if (value is not null)
                    {
                        supplied[id] = ConsultInputValue.FromJson(value.ToJsonString(Compact));
                    }

                    break;

                case ["node", var id]:
                    nodes.Add(new WorkflowNodeSpec(id, id, Format: FormatOf(id, type, formats, sourceFiles)));
                    if (value is null)
                    {
                        skipped.Add(id);
                    }
                    else
                    {
                        nodeTexts[id] = value is JsonValue text && text.TryGetValue<string>(out var s) ? s : value.ToJsonString(Compact);
                    }

                    break;

                case ["item"] when value!["id"] is { } itemId:
                    // A snippet folder's item, as the starter's settled folder carries it.
                    forEach = WorkflowFanSources.SnippetPrefix + "sections";
                    item = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["id"] = itemId.GetValue<string>(),
                        ["label"] = itemId.GetValue<string>(),
                        ["content"] = "lorem",
                    };
                    break;

                case ["item"]:
                    // An input fan's element: the array supplied with it alone, the item as the fan makes it.
                    itemsInput = Declaration(ItemsInput, new ReadType("array", Items: type.Fields![0].Type));
                    inputs.Add(itemsInput);
                    supplied[ItemsInput] = ConsultInputValue.FromJson(new JsonArray(value!["value"]!.DeepClone()).ToJsonString(Compact));
                    forEach = $"{TemplateObjects.Input}.{ItemsInput}";
                    break;

                case ["pass", "number"]:
                    var number = value!.GetValue<int>();
                    pass = new ConsultPassPayload(number, Enumerable.Range(1, number - 1)
                        .Select(earlier => new ConsultPassTexts(earlier, new Dictionary<string, string>())).ToList());
                    break;

                case ["run", "date"]:
                    runDate = value!.GetValue<string>();
                    break;

                case ["profile", "name"]:
                    profileName = value!.GetValue<string>();
                    break;

                default:
                    throw new InvalidOperationException($"The read '{read}' has no scope rule.");
            }
        }

        var manifest = new WorkflowPackageManifest("conditions", "1.0.0", 30, Nodes: nodes, Inputs: inputs, Formats: formats);
        var package = new WorkflowPackage(manifest, Nodes: nodes, SourceFiles: sourceFiles, Formats: TestFormats.Catalog);

        var request = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(null, Inputs: supplied));
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(request, manifest);
        if (resolution.Error is { } refusal)
        {
            return (null, refusal);
        }

        if (itemsInput is not null)
        {
            item = WorkflowInputFans.Items(itemsInput, request.Inputs![ItemsInput])[0];
        }

        var scope = ConsultRenderPayload.ScopeFor(
            resolution.Effective!,
            ConsultConditionScope.TypesOf(package),
            nodeTexts,
            null,
            item,
            forEach,
            ConsultRenderPayload.RunFactsFor(DateTime.ParseExact(runDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), "conditions", "conditions@1.0.0", null),
            ConsultRenderPayload.ProfileFactsFor(profileName, null),
            package,
            skipped: skipped,
            pass: pass);

        return (scope, null);
    }

    /// <summary>A read input as the manifest declares it — optional, so its absence reaches the condition (a required input is never absent there: the start refuses the run).</summary>
    internal static WorkflowInputSpec Declaration(string id, ReadType type) =>
        JsonSerializer.Deserialize<WorkflowInputSpec>(DeclarationJson(id, type).ToJsonString(), WorkflowPackageManifestJson.ReadOptions)!;

    private static JsonObject DeclarationJson(string id, ReadType type)
    {
        var declaration = new JsonObject { ["id"] = id, ["label"] = id, ["required"] = false, ["type"] = type.Type };
        if (type.Values is { } values)
        {
            declaration["values"] = new JsonArray(values.Select(value => (JsonNode?)value).ToArray());
        }

        var fields = type.Fields;
        if (type.Items is { } items)
        {
            declaration["items"] = items.Type;
            fields = items.Fields;
        }

        if (fields is not null)
        {
            declaration["fields"] = new JsonArray(fields.Select(field => (JsonNode?)DeclarationJson(field.Name, field.Type)).ToArray());
        }

        return declaration;
    }

    /// <summary>The format a read node has: the catalogue's prose and classification, or a package-local object format with the read's schema.</summary>
    private static string FormatOf(string nodeId, ReadType type, List<WorkflowFormatReference> formats, Dictionary<string, string> sourceFiles)
    {
        switch (type.Type)
        {
            case "text":
                return "prose";
            case "classification":
                return "classification";
            case "object" when type.Schema is { } schema:
                var id = $"{nodeId}_format";
                var file = $"formats/{id}.json";
                formats.Add(new WorkflowFormatReference(id, file));
                sourceFiles[file] = new JsonObject
                {
                    ["type"] = "object",
                    ["producer"] = "custom",
                    ["outputMacros"] = false,
                    ["description"] = "The read node's format.",
                    ["schema"] = schema.DeepClone(),
                }.ToJsonString();
                return id;
            default:
                throw new InvalidOperationException($"A node read of type '{type.Type}' has no format; a node is text, classification or object with a schema.");
        }
    }
}
