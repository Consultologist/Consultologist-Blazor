using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #407: builds the JSON Schema consultologist-package-format publishes for each
/// specVersion — the artifact an editor or a CI job can read, which neither the
/// prose nor the conformance suite gives them.
///
/// The <em>shape</em> is exported from WorkflowPackageManifest, so the property
/// set and its types cannot drift from the wire form. The <em>rules</em> are
/// written on top, because an exporter cannot know that ids are snake_case, that
/// values belongs to enum and nothing else, or that inputs arrive at
/// specVersion 7.
///
/// Every enrichment asserts it found what it was enriching. A renamed field
/// fails the build loudly rather than quietly publishing a schema missing a
/// rule — which is the whole reason to export the shape rather than hand-write
/// it twice.
/// </summary>
internal static class PackageFormatSchema
{
    /// <summary>Snake_case declared ids: input ids, result ids, enum values.</summary>
    private const string DeclaredId = "^[a-z][a-z0-9_]*$";

    /// <summary>A node reference, as `result`, `results[].node` and `aggregate[]` all use.</summary>
    private const string NodeRef = "^node:.+$";

    /// <summary>
    /// A concrete package ref. The month range is spelled out because C# checks
    /// it outside the regex (`month is &lt; 1 or &gt; 12`), so copying the C# pattern
    /// alone would accept v2026.13.1.
    /// </summary>
    private const string ConcreteRef = @"^[a-z0-9][a-z0-9-]*@v\d{4}\.(0[1-9]|1[0-2])\.[1-9]\d*$";

    private const string BindingSource = "^(input|item|data|node):.+$";

    /// <summary>A title: no line break anywhere (v9 § 4).</summary>
    private const string SingleLine = @"^[^\r\n]*$";

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        // The form the publisher writes and the store reads.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonSerializerOptions Rendering = new() { WriteIndented = true };

    internal static string Render(int specVersion) =>
        Build(specVersion).ToJsonString(Rendering) + "\n";

    internal static JsonObject Build(int specVersion)
    {
        var root = (JsonObject)WireOptions.GetJsonSchemaAsNode(typeof(WorkflowPackageManifest))!;

        Collapse(root);
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["title"] = $"Consultologist workflow package manifest, specVersion {specVersion}";

        var properties = Object(root, "properties");

        Object(properties, "specVersion")["const"] = specVersion;
        Object(properties, "derivedFrom")["pattern"] = ConcreteRef;
        Object(properties, "result")["pattern"] = NodeRef;

        EnrichTemplating(Object(properties, "templating"));
        EnrichPrompts(Object(properties, "prompts"));
        EnrichNodes(Object(properties, "nodes"), specVersion);

        // The MANIFEST's title lives under properties — root["title"] above is
        // this schema's own title, a different field that shares the name.
        // v9 § 4 (#432): both arrive at 9. The engine counts UTF-16 units where
        // JSON Schema's maxLength counts code points; the engine is the
        // authority, and the schema is looser only for astral characters.
        if (specVersion < 9)
        {
            Remove(properties, "title");
            Remove(properties, "description");
            Remove(properties, "tags");
        }
        else
        {
            var title = Object(properties, "title");
            title["minLength"] = 1;
            title["maxLength"] = WorkflowPackageMetadata.MaxTitleLength;
            title["pattern"] = SingleLine;

            var description = Object(properties, "description");
            description["minLength"] = 1;
            description["maxLength"] = WorkflowPackageMetadata.MaxDescriptionLength;

            // #453: required at 9 (added to the required list below), an array
            // of single-line labels. The exporter offers the nullable form;
            // null is not a spelling of "none" on a v9 manifest, so the type
            // is narrowed to the array alone. Trimming and case-insensitive
            // distinctness are the engine's rules; JSON Schema's uniqueItems
            // is case-sensitive, and is stated as the looser bound it is.
            var tags = Object(properties, "tags");
            tags["type"] = "array";
            tags["maxItems"] = WorkflowPackageMetadata.MaxTags;
            tags["uniqueItems"] = true;
            var tag = Object(tags, "items");
            tag["type"] = "string";
            tag["minLength"] = 1;
            tag["maxLength"] = WorkflowPackageMetadata.MaxTagLength;
            tag["pattern"] = SingleLine;
        }

        // Declared sections arrive at 7. A section the version does not have is
        // an error, never an ignored field — package-format-v8.md § 2.
        if (specVersion < 7)
        {
            Remove(properties, "inputs");
            Remove(properties, "results");
            root["required"] = Required("name", "version", "specVersion", "templating", "nodes", "result");
        }
        else
        {
            EnrichInputs(Object(properties, "inputs"), specVersion);
            EnrichResults(Object(properties, "results"), specVersion);
            root["required"] = specVersion < 9
                ? Required("name", "version", "specVersion", "templating", "nodes", "inputs")
                // #453: every v9 manifest states its tags.
                : Required("name", "version", "specVersion", "templating", "nodes", "inputs", "tags");

            // The list form owns the declaration; the string form stays valid as
            // one-entry sugar. One of them, never both.
            root["oneOf"] = new JsonArray(
                new JsonObject { ["required"] = Required("result"), ["not"] = new JsonObject { ["required"] = Required("results") } },
                new JsonObject { ["required"] = Required("results"), ["not"] = new JsonObject { ["required"] = Required("result") } });
        }

        Close(root);
        return root;
    }

    private static void EnrichTemplating(JsonObject templating)
    {
        var properties = Object(templating, "properties");
        Object(properties, "engine")["const"] = "scriban";
        Object(properties, "engineVersion")["pattern"] = @"^\d+(\.\d+){1,3}$";
        Close(templating);
    }

    private static void EnrichPrompts(JsonObject prompts)
    {
        var item = Object(prompts, "items");
        var properties = Object(item, "properties");
        Object(properties, "id")["minLength"] = 1;
        Object(properties, "file")["minLength"] = 1;
        Object(properties, "prelude")["minLength"] = 1;
        Close(item);
    }

    private static void EnrichNodes(JsonObject nodes, int specVersion)
    {
        nodes["minItems"] = 1;

        var item = Object(nodes, "items");
        var properties = Object(item, "properties");

        Object(properties, "id")["minLength"] = 1;
        Object(properties, "label")["minLength"] = 1;
        // v9 (#426): a fan may also walk a caller-supplied array. Below 9
        // only a data: collection can be fanned, and the schema says so.
        Object(properties, "forEach")["pattern"] = specVersion >= 9 ? "^(data|input):.+$" : "^data:.+$";

        // The exporter cannot see through WorkflowBindingValueConverter: it
        // reports the record, so `bindings` arrives as a bare object. On the
        // wire a binding is a source string, or { from, as } — and the converter
        // itself rejects any other property, which is why this closes.
        var bindings = Object(properties, "bindings");
        Require(bindings.ContainsKey("type"), "bindings lost its type; the converter's shape is hand-written and needs revisiting");
        bindings["additionalProperties"] = new JsonObject
        {
            ["oneOf"] = new JsonArray(
                new JsonObject { ["type"] = "string", ["pattern"] = BindingSource },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["from"] = new JsonObject { ["type"] = "string", ["pattern"] = BindingSource },
                        ["as"] = new JsonObject { ["enum"] = new JsonArray("concept-bullets", "concept-context") }
                    },
                    ["required"] = Required("from"),
                    ["additionalProperties"] = false
                })
        };

        var output = Object(properties, "output");
        Object(Object(output, "properties"), "failIfEmpty")["minLength"] = 1;
        Close(output);

        if (specVersion < 6)
        {
            // Aggregators arrive at 6.
            Remove(properties, "aggregate");
        }
        else
        {
            var aggregate = Object(properties, "aggregate");
            aggregate["minItems"] = 1;
            Object(aggregate, "items")["pattern"] = NodeRef;
        }

        Close(item);
    }

    private static void EnrichInputs(JsonObject inputs, int specVersion)
    {
        inputs["minItems"] = 1;

        var item = Object(inputs, "items");
        var properties = Object(item, "properties");

        Object(properties, "id")["pattern"] = DeclaredId;
        Object(properties, "label")["minLength"] = 1;

        if (specVersion < 8)
        {
            Remove(properties, "type");
            Remove(properties, "values");
            Remove(properties, "items");
            Remove(properties, "fields");
        }
        else if (specVersion < 9)
        {
            // The v8 type set, keyed by version so this schema's bytes do not
            // move when a later version widens the vocabulary.
            Object(properties, "type")["enum"] = TypeNames(WorkflowInputTypes.ForSpecVersion(8));
            EnrichValues(Object(properties, "values"));
            Remove(properties, "items");
            Remove(properties, "fields");

            // values belongs to enum and to nothing else. An absent type is
            // text, so the "otherwise" branch has to cover absence too.
            item["if"] = TypeIs(WorkflowInputTypes.Enum);
            item["then"] = new JsonObject { ["required"] = Required("values") };
            item["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = Required("values") } };
        }
        else
        {
            EnrichStructuredInput(item, properties);
        }

        // Required defaults to true when absent, so it is not required here.
        item["required"] = Required("id", "label");
        Close(item);
    }

    /// <summary>
    /// v9 (package-format-v9-design.md § 4): number, object and array; items for
    /// an array, fields for an object or an array of objects; values for an
    /// enum or an array of enums. Structure is one level deep — a field's type
    /// is a scalar, and items may not be array.
    /// </summary>
    private static void EnrichStructuredInput(JsonObject item, JsonObject properties)
    {
        Object(properties, "type")["enum"] = TypeNames(WorkflowInputTypes.ForSpecVersion(9));
        Object(properties, "items")["enum"] = TypeNames(WorkflowInputTypes.ElementTypes);
        EnrichValues(Object(properties, "values"));

        var fields = Object(properties, "fields");
        fields["minItems"] = 1;
        var field = Object(fields, "items");
        var fieldProperties = Object(field, "properties");
        Object(fieldProperties, "id")["pattern"] = DeclaredId;
        Object(fieldProperties, "label")["minLength"] = 1;
        Object(fieldProperties, "type")["enum"] = TypeNames(WorkflowInputTypes.Scalars);
        EnrichValues(Object(fieldProperties, "values"));
        field["if"] = TypeIs(WorkflowInputTypes.Enum);
        field["then"] = new JsonObject { ["required"] = Required("values") };
        field["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = Required("values") } };
        field["required"] = Required("id", "label");
        Close(field);

        var isArray = TypeIs(WorkflowInputTypes.Array);
        var isObject = TypeIs(WorkflowInputTypes.Object);
        var isArrayOfObjects = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["const"] = WorkflowInputTypes.Array },
                ["items"] = new JsonObject { ["const"] = WorkflowInputTypes.Object }
            },
            ["required"] = Required("type", "items")
        };
        var isArrayOfEnums = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["const"] = WorkflowInputTypes.Array },
                ["items"] = new JsonObject { ["const"] = WorkflowInputTypes.Enum }
            },
            ["required"] = Required("type", "items")
        };

        item["allOf"] = new JsonArray(
            // items: required for an array, forbidden otherwise.
            new JsonObject
            {
                ["if"] = isArray,
                ["then"] = new JsonObject { ["required"] = Required("items") },
                ["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = Required("items") } }
            },
            // fields: required for an object or an array of objects, forbidden otherwise.
            new JsonObject
            {
                ["if"] = new JsonObject { ["anyOf"] = new JsonArray(isObject, isArrayOfObjects) },
                ["then"] = new JsonObject { ["required"] = Required("fields") },
                ["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = Required("fields") } }
            },
            // values: required for an enum or an array of enums, forbidden otherwise.
            new JsonObject
            {
                ["if"] = new JsonObject { ["anyOf"] = new JsonArray(TypeIs(WorkflowInputTypes.Enum), isArrayOfEnums) },
                ["then"] = new JsonObject { ["required"] = Required("values") },
                ["else"] = new JsonObject { ["not"] = new JsonObject { ["required"] = Required("values") } }
            });
    }

    private static void EnrichValues(JsonObject values)
    {
        // An enum with one value is a constant, not a choice.
        values["minItems"] = 2;
        values["uniqueItems"] = true;
        Object(values, "items")["pattern"] = DeclaredId;
    }

    private static JsonObject TypeIs(string type) => new()
    {
        ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = type } },
        ["required"] = Required("type")
    };

    private static JsonArray TypeNames(IEnumerable<string> names) =>
        new(names.Select(name => (JsonNode?)name).ToArray());

    private static void EnrichResults(JsonObject results, int specVersion)
    {
        results["minItems"] = 1;

        var item = Object(results, "items");
        var properties = Object(item, "properties");

        Object(properties, "id")["pattern"] = DeclaredId;
        Object(properties, "label")["minLength"] = 1;
        Object(properties, "node")["pattern"] = NodeRef;

        if (specVersion < 8)
        {
            // Conditions arrive at 8.
            Remove(properties, "when");
        }
        else
        {
            Object(properties, "when")["minLength"] = 1;
        }

        Close(item);
    }

    /// <summary>
    /// Optional means absent, not null: the publisher omits nulls, so a schema
    /// that kept the exporter's ["object","null"] unions would accept
    /// `"nodes": null` as a well-formed manifest.
    /// </summary>
    private static void Collapse(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                Collapse(element);
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        if (obj["type"] is JsonArray types && types.Count == 2)
        {
            var kept = types.FirstOrDefault(entry => entry?.GetValue<string>() != "null")?.GetValue<string>();
            if (kept != null)
            {
                obj["type"] = kept;
            }
        }

        // A `default: null` beside an omitted-when-null property is noise that
        // reads as "null is a value here".
        // A JSON null reads back as a null reference, not a JsonValue holding
        // null — so this asks whether the key is present and its value absent.
        if (obj.ContainsKey("default") && obj["default"] is null)
        {
            obj.Remove("default");
        }

        foreach (var pair in obj.ToList())
        {
            Collapse(pair.Value);
        }
    }

    /// <summary>
    /// package-format-v8.md § 2: "a section the version does not have is never a
    /// silently ignored field." Since #416 the engine enforces it too, through
    /// WorkflowPackageManifestJson.ReadOptions, so the schema and the reference
    /// implementation now say the same thing rather than disagreeing about the
    /// specification they share.
    /// </summary>
    private static void Close(JsonObject schema)
    {
        if (!schema.ContainsKey("additionalProperties"))
        {
            schema["additionalProperties"] = false;
        }
    }

    private static JsonObject Object(JsonObject parent, string key)
    {
        Require(parent[key] is JsonObject, $"the exported schema has no '{key}' to enrich — did the model rename it?");
        return (JsonObject)parent[key]!;
    }

    private static void Remove(JsonObject parent, string key)
    {
        Require(parent.ContainsKey(key), $"the exported schema has no '{key}' to remove — did the model rename it?");
        parent.Remove(key);
    }

    private static JsonArray Required(params string[] names) =>
        new(names.Select(name => (JsonNode?)name).ToArray());

    private static void Require(bool condition, string because)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Package format schema generation: {because}");
        }
    }
}
