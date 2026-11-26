using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 18; part 09 §§ 4.2, 4.3; part 02 § 12; D12, D13): <c>WorkflowPackages/Current</c>
/// describes a 31 package from its <c>resolve.json</c> (<see cref="V31PackageDescription"/>): its top-level nodes, its inputs
/// in written order with each existing member filled and <c>Format</c> and <c>Channel</c> added, its deliverables, and
/// <c>Macros</c> empty. The endpoint tries the compiled load first; with the preview setting unset a 31 pin is S09-001 as a
/// 400, and a load refusal is its sentence as a 400 (Review Focus 4).
/// </summary>
public class V31PackageDescriptionTests
{
    private const string AppUserId = "0123456789abcdef0123456789abcdef";

    private static readonly ConcurrentDictionary<string, CompiledPackage> Published = new(StringComparer.Ordinal);

    private static readonly Lazy<EngineCapabilities> ThisEngine = new(() => V31Job.Factory().Current());

    private static CompiledGoldenTests.GoldenCase Golden(string name) => CompiledGoldenTests.Cases().Single(c => c.Name == name);

    /// <summary>The plan of the golden <paramref name="name"/>, its <c>resolve.json</c> changed by <paramref name="change"/>.</summary>
    private static RunPlan Plan(string name, Action<JsonObject>? change = null)
    {
        var pair = Published.GetOrAdd(name, n => CompiledGoldenTests.Published(Golden(n)));
        var document = JsonNode.Parse(pair.Resolve)!.AsObject();
        change?.Invoke(document);
        var (plan, refusal) = RunPlan.Build(CanonicalJson.Write(document), pair.ResolveHash, pair.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    }

    /// <summary>The built-in formats of part 02 § 2.4 an inline input below refers to, as the bundle holds them.</summary>
    private static readonly Dictionary<string, string> BuiltIns = new(StringComparer.Ordinal)
    {
        ["string"] = """{"$id":"urn:consultologist:spec:31:format:string","$schema":"urn:consultologist:spec:31:dialect","description":"Text.","type":"string"}""",
        ["number"] = """{"$id":"urn:consultologist:spec:31:format:number","$schema":"urn:consultologist:spec:31:dialect","description":"A number.","type":"number"}""",
        ["boolean"] = """{"$id":"urn:consultologist:spec:31:format:boolean","$schema":"urn:consultologist:spec:31:dialect","description":"true or false.","type":"boolean"}""",
        ["date"] = """{"$id":"urn:consultologist:spec:31:format:date","$schema":"urn:consultologist:spec:31:dialect","description":"A calendar date, written YYYY-MM-DD.","format":"date","type":"string"}""",
        ["plaintext"] = """{"$id":"urn:consultologist:spec:31:format:plaintext","$schema":"urn:consultologist:spec:31:dialect","contentMediaType":"text/plain","description":"Plain text, with no markup.","type":"string"}""",
        ["markdown_block"] = """{"$id":"urn:consultologist:spec:31:format:markdown_block","$schema":"urn:consultologist:spec:31:dialect","contentMediaType":"text/markdown","description":"Markdown text that stands on its own lines (a paragraph, a list or a table).","type":"string","x-markdown":"block"}""",
    };

    /// <summary>
    /// The conditional golden with one input <c>x</c> of <paramref name="format"/> (its channel <paramref name="channel"/>),
    /// the built-ins and <paramref name="defs"/> joining its bundle.
    /// </summary>
    private static RunPlan OneInput(string format, string? defs = null, string? channel = null) => Plan("conditional", document =>
    {
        document["inputs"] = new JsonObject
        {
            ["x"] = new JsonObject
            {
                ["channel"] = channel,
                ["description"] = null,
                ["format"] = JsonNode.Parse(format),
                ["label"] = "Encounter details",
                ["required"] = true,
            },
        };
        document["inputOrder"] = new JsonArray("x");
        var bundle = document["formats"]!["$defs"]!.AsObject();
        foreach (var (key, schema) in BuiltIns)
            bundle[key] = JsonNode.Parse(schema);
        if (defs != null)
        {
            foreach (var (key, schema) in JsonNode.Parse(defs)!.AsObject())
                bundle[key] = schema!.DeepClone();
        }
    });

    private static WorkflowPackageInputResponse Input(RunPlan plan) => Assert.Single(V31PackageDescription.Describe(plan).Inputs!);

    private static JsonNode? FormatOf(WorkflowPackageInputResponse input) =>
        input.Format is { } format ? JsonNode.Parse(format.GetRawText()) : null;

    /// <summary>
    /// Part 09 § 4.3, "its <c>$id</c>s kept and its <c>$ref</c>s resolved within the document": each <c>$id</c> appears once,
    /// and each <c>$ref</c> is one of them or a <c>#/$defs/</c> pointer to a definition of the resource it is written in.
    /// </summary>
    private static void AssertResolvesWithin(JsonNode format)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<(string Reference, JsonObject Resource)>();
        void Walk(JsonNode? node, JsonObject resource)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["$id"] is JsonValue id)
                    {
                        Assert.True(ids.Add((string)id!), $"$id {id} appears twice");
                        resource = obj;
                    }

                    foreach (var (name, value) in obj)
                    {
                        if (name == "$ref")
                            references.Add(((string)value!, resource));
                        else
                            Walk(value, resource);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                        Walk(item, resource);
                    break;
            }
        }

        if (format is JsonObject root)
            Walk(root, root);
        foreach (var (reference, resource) in references)
        {
            Assert.True(
                ids.Contains(reference)
                || (reference.StartsWith("#/$defs/", StringComparison.Ordinal) && resource["$defs"]?[reference["#/$defs/".Length..]] is JsonObject),
                $"{reference} does not resolve within the document");
        }
    }

    /// <summary>Whether <paramref name="value"/> fits <paramref name="format"/>, through the library's validator.</summary>
    private static bool Validates(JsonNode format, string value) =>
        new CorvusFormatValueValidator().ValidateExpanded(JsonNode.Parse(value), format).Valid;

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), actual?.ToJsonString());

    // ---- the goldens ---------------------------------------------------------------------------------------------------

    [Fact]
    public void TheNestedNote_IsDescribedFromItsResolveJson()
    {
        var response = V31PackageDescription.Describe(Plan("nested-note"));

        Assert.Equal("example-nested-note", response.Name);
        Assert.Equal("v2026.12.1", response.Version);
        Assert.Equal(31, response.SpecVersion);
        Assert.Equal("Nested note demo", response.Title);
        // D12: the top-level nodes in graph.order, by key; no node inside a loop or a while.
        Assert.Equal(
            [
                new WorkflowPackageBlockResponse("node.plan", "Planning the sections"),
                new WorkflowPackageBlockResponse("node.section_count", "Counting the sections"),
                new WorkflowPackageBlockResponse("node.sections", "Sections"),
                new WorkflowPackageBlockResponse("node.refine", "Refining the summary"),
            ],
            response.Blocks!);
        Assert.Equal([new WorkflowPackageResultResponse("deliverable.note", "Nested consultation note")], response.Results!);
        Assert.NotNull(response.Macros);
        Assert.Empty(response.Macros!);

        var draft = Assert.Single(response.Inputs!);
        Assert.Equal(("consult_draft", "Consult draft", true, "text"), (draft.Id, draft.Label, draft.Required, draft.Type));
        Assert.Null(draft.Values);
        Assert.Null(draft.Items);
        Assert.Null(draft.Fields);
        Assert.Null(draft.ExpectedContent);
        Assert.Null(draft.Channel);
        Assert.Null(draft.Types);
        AssertJson(
            """{"$id":"urn:consultologist:spec:31:format:markdown_block","contentMediaType":"text/markdown","description":"Markdown text that stands on its own lines (a paragraph, a list or a table).","type":"string","x-markdown":"block"}""",
            FormatOf(draft));
    }

    [Fact]
    public void TheConditionalPackage_IsDescribedFromItsResolveJson()
    {
        var response = V31PackageDescription.Describe(Plan("conditional"));

        Assert.Equal(31, response.SpecVersion);
        Assert.Equal("Conditional sections demo", response.Title);
        Assert.Equal(
            [
                new WorkflowPackageBlockResponse("node.plan", "Planning the sections"),
                new WorkflowPackageBlockResponse("node.sections_draft", "Sections: drafting each by its kind"),
                new WorkflowPackageBlockResponse("node.patient_summaries", "Patient summaries"),
                new WorkflowPackageBlockResponse("node.standing_sections", "Standing sections"),
            ],
            response.Blocks!);
        Assert.Equal([new WorkflowPackageResultResponse("deliverable.note", "Consultation note")], response.Results!);
        Assert.Empty(response.Macros!);
        Assert.Equal(["consult_draft", "sections", "include_billing"], response.Inputs!.Select(i => i.Id));

        var sections = response.Inputs![1];
        Assert.Equal(("Sections", true, "array"), (sections.Label, sections.Required, sections.Type));
        Assert.Null(sections.Fields);
        Assert.Equal("object", sections.Items!.Type);
        Assert.Null(sections.Items.Items);
        Assert.Null(sections.Items.Values);
        // Fields in written order (#1508, R10: the format's propertyOrder, though canonical JSON sorts the properties), each
        // required (not nullable, D13).
        Assert.Equal(
            [
                new WorkflowPackageFieldResponse("title", "Title", true, "text"),
                new WorkflowPackageFieldResponse("kind", "Kind", true, "enum", ["narrative", "table", "omit"]),
            ],
            sections.Items.Fields!,
            FieldComparer.Instance);
        // One compound document: the input's own schema at the root, the format it reaches once under $defs with its
        // $id, the reference written by that $id.
        AssertJson(
            """
            {"items":{"$ref":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:section_request"},
             "type":"array",
             "$defs":{"section_request":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:section_request",
               "additionalProperties":false,
               "properties":{"kind":{"enum":["narrative","table","omit"],"title":"Kind","type":"string"},"title":{"title":"Title","type":"string"}},
               "required":["title","kind"],"type":"object"}}}
            """,
            FormatOf(sections));

        var billing = response.Inputs[2];
        Assert.Equal(("Include the billing section", false, "boolean"), (billing.Label, billing.Required, billing.Type));
        // A named format at the root is that format, with its $id; it reaches nothing, so it has no $defs.
        AssertJson("""{"$id":"urn:consultologist:spec:31:format:boolean","description":"true or false.","type":"boolean"}""", FormatOf(billing));
        Assert.All(response.Inputs, i => AssertResolvesWithin(FormatOf(i)!));
        Assert.True(Validates(FormatOf(sections)!, """[{"kind":"table","title":"Medications"}]"""));
        Assert.False(Validates(FormatOf(sections)!, """[{"kind":"chart","title":"Medications"}]"""));
    }

    // ---- part 02 § 12's Type table, row by row --------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"$ref":"#/$defs/plaintext"}""", "text")]
    [InlineData("""{"$ref":"#/$defs/markdown_block"}""", "text")]
    [InlineData("""{"$ref":"#/$defs/string"}""", "text")]
    [InlineData("""{"type":"string"}""", "text")]
    [InlineData("""{"$ref":"#/$defs/date"}""", "date")]
    [InlineData("""{"$ref":"#/$defs/number"}""", "number")]
    [InlineData("""{"type":"integer"}""", "number")]
    [InlineData("""{"$ref":"#/$defs/boolean"}""", "boolean")]
    [InlineData("""{"additionalProperties":false,"properties":{"seen":{"$ref":"#/$defs/date"}},"required":["seen"],"type":"object"}""", "object")]
    [InlineData("""{"items":{"$ref":"#/$defs/plaintext"},"type":"array"}""", "array")]
    public void AFormat_GivesTheTypeItsRowSays(string format, string type)
    {
        var input = Input(OneInput(format));

        Assert.Equal(type, input.Type);
        Assert.Null(input.Values);
        Assert.Null(input.Types);
    }

    [Fact]
    public void AStringWithEnum_IsAnEnum_WithItsValues()
    {
        var input = Input(OneInput("""{"enum":["new_patient","follow_up"],"type":"string"}"""));

        Assert.Equal("enum", input.Type);
        Assert.Equal(["new_patient", "follow_up"], input.Values!);
    }

    [Fact]
    public void AStringWithALabelledOneOf_IsAnEnum_WithEachConst()
    {
        var input = Input(OneInput("""{"oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"}],"type":"string"}"""));

        Assert.Equal("enum", input.Type);
        Assert.Equal(["new_patient", "follow_up"], input.Values!);
        // The titles are in Format.
        Assert.Contains("\"title\":\"Follow-up\"", input.Format!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ANullableEnum_ListsItsTextValuesAlone()
    {
        var input = Input(OneInput("""{"enum":["left","right",null],"type":["string","null"]}"""));

        Assert.Equal("enum", input.Type);
        Assert.Equal(["left", "right"], input.Values!);
    }

    [Fact]
    public void ChannelAndExpectedContent_AreBothTheChannel()
    {
        var input = Input(OneInput("""{"$ref":"#/$defs/plaintext"}""", channel: "transcript"));

        Assert.Equal("transcript", input.Channel);
        Assert.Equal("transcript", input.ExpectedContent);
    }

    // ---- Items and Fields at depth (D13) --------------------------------------------------------------------------------

    [Fact]
    public void ItemsAndFields_AreMappedAtEveryDepth()
    {
        var input = Input(OneInput(
            """{"$ref":"#/$defs/medication_list"}""",
            """
            {"medication_list":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:medication_list",
              "items":{"additionalProperties":false,
                "properties":{
                  "name":{"$ref":"#/$defs/plaintext","title":"Drug"},
                  "dose_mg":{"type":["number","null"]},
                  "route":{"oneOf":[{"const":"oral","title":"By mouth"},{"const":"iv","title":"Intravenous"}],"title":"Route","type":"string"},
                  "times":{"items":{"$ref":"#/$defs/date"},"title":"Times","type":"array"},
                  "prescriber":{"additionalProperties":false,"properties":{"name":{"type":"string"}},"required":["name"],"title":"Prescriber","type":["object","null"]}},
                "required":["name","dose_mg","route","times","prescriber"],"type":"object"},
              "type":"array"}}
            """));

        Assert.Equal("array", input.Type);
        Assert.Null(input.Fields);
        var element = input.Items!;
        Assert.Equal("object", element.Type);
        Assert.Equal(
            [
                // With no propertyOrder (#1508, R10), in resolve.json's order (canonical JSON: ordinal). D13: a field is required when its type is not
                // nullable; its label is the property name when it has no title.
                new WorkflowPackageFieldResponse("dose_mg", "dose_mg", false, "number"),
                new WorkflowPackageFieldResponse("name", "Drug", true, "text"),
                new WorkflowPackageFieldResponse("prescriber", "Prescriber", false, "object",
                    Fields: [new WorkflowPackageFieldResponse("name", "name", true, "text")]),
                new WorkflowPackageFieldResponse("route", "Route", true, "enum", ["oral", "iv"]),
                new WorkflowPackageFieldResponse("times", "Times", true, "array", Items: new WorkflowPackageElementResponse("date")),
            ],
            element.Fields!,
            FieldComparer.Instance);
    }

    [Fact]
    public void AnObjectInput_CarriesItsFields_AndNoItems()
    {
        var input = Input(OneInput("""{"additionalProperties":false,"properties":{"seen":{"$ref":"#/$defs/date","title":"Date seen"}},"required":["seen"],"type":"object"}"""));

        Assert.Null(input.Items);
        Assert.Equal([new WorkflowPackageFieldResponse("seen", "Date seen", true, "date")], input.Fields!, FieldComparer.Instance);
    }

    [Fact]
    public void Fields_ComeInWrittenOrder()
    {
        // #1508 (R10): resolve.json sorts the properties (canonical JSON); each object's propertyOrder gives them back as
        // written, zeta, alpha, mid, at every depth: an inline format, its nested object, a bundled format, its list's items.
        var plan = RunFixture.Plan(PropertyOrderTests.Files(), limits: CompiledGoldenTests.EngineLimits);
        var inputs = V31PackageDescription.Describe(plan).Inputs!.ToDictionary(i => i.Id, StringComparer.Ordinal);
        string[] written = ["zeta", "alpha", "mid"];

        var details = inputs["details"];
        Assert.Equal(written, details.Fields!.Select(f => f.Id));
        Assert.Equal(written, details.Fields!.Single(f => f.Id == "mid").Fields!.Select(f => f.Id));

        var arranged = inputs["arranged"];
        Assert.Equal(written, arranged.Fields!.Select(f => f.Id));
        Assert.Equal(written, arranged.Fields!.Single(f => f.Id == "mid").Fields!.Select(f => f.Id));
        Assert.Equal(written, arranged.Fields!.Single(f => f.Id == "alpha").Items!.Fields!.Select(f => f.Id));
    }

    [Fact]
    public void Format_CarriesNoPropertyOrder_AndAPropertyOfThatNameIsKept()
    {
        // #1508 (R10, the controller's ruling): Format is a schema in the dialect, so resolve.json's propertyOrder is left
        // out at every depth (the root, a nested object, a list's items, a format under $defs); Fields carry the order.
        var plan = RunFixture.Plan(PropertyOrderTests.Files(), limits: CompiledGoldenTests.EngineLimits);
        Assert.NotNull(((JsonObject)plan.Document["formats"]!["$defs"]!["ordered"]!)["propertyOrder"]);
        var inputs = V31PackageDescription.Describe(plan).Inputs!.ToDictionary(i => i.Id, StringComparer.Ordinal);

        foreach (var id in new[] { "details", "arranged" })
        {
            var format = FormatOf(inputs[id])!;
            Assert.Empty(PropertyOrders(format));
            AssertResolvesWithin(format);
        }

        // arranged names the bundled format ordered: its root, its nested mid and its alpha's items had propertyOrder.
        var arranged = FormatOf(inputs["arranged"])!;
        Assert.True(arranged["properties"]!["mid"]!["properties"] is JsonObject);
        Assert.True(arranged["properties"]!["alpha"]!["items"]!["properties"] is JsonObject);

        // details' nested mid; and Fields still in written order.
        Assert.True(FormatOf(inputs["details"])!["properties"]!["mid"]!["properties"] is JsonObject);
        Assert.Equal(["zeta", "alpha", "mid"], inputs["arranged"].Fields!.Select(f => f.Id));
        Assert.Equal(["zeta", "alpha", "mid"], inputs["details"].Fields!.Select(f => f.Id));

        // A format under $defs; and a property named propertyOrder (no source can write one: a property name is an id,
        // lowercase) is a schema in a properties map, kept, and a field in written order.
        var listing = Input(OneInput(
            """{"items":{"$ref":"#/$defs/listing"},"type":"array"}""",
            """
            {"listing":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:listing","additionalProperties":false,
              "properties":{"name":{"title":"Name","type":"string"},"propertyOrder":{"title":"Order of service","type":"string"}},
              "propertyOrder":["propertyOrder","name"],"required":["propertyOrder","name"],"type":"object"}}
            """));
        var kept = FormatOf(listing)!;
        Assert.Equal("Order of service", (string?)kept["$defs"]!["listing"]!["properties"]!["propertyOrder"]!["title"]);
        Assert.Empty(PropertyOrders(kept));
        AssertResolvesWithin(kept);
        Assert.Equal(["propertyOrder", "name"], listing.Items!.Fields!.Select(f => f.Id));
        Assert.True(Validates(kept, """[{"propertyOrder":"First","name":"Dr A. Sample"}]"""));
    }

    /// <summary>Every <c>propertyOrder</c> list beside a <c>properties</c> map in <paramref name="node"/>, at any depth.</summary>
    private static List<JsonNode> PropertyOrders(JsonNode? node)
    {
        var found = new List<JsonNode>();
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject && obj["propertyOrder"] is JsonArray order)
                    found.Add(order);
                foreach (var (_, value) in obj)
                    found.AddRange(PropertyOrders(value));
                break;
            case JsonArray array:
                foreach (var item in array)
                    found.AddRange(PropertyOrders(item));
                break;
        }

        return found;
    }

    [Fact]
    public void AListOfLists_CarriesEachElement()
    {
        var input = Input(OneInput("""{"items":{"items":{"$ref":"#/$defs/number"},"type":"array"},"type":"array"}"""));

        Assert.Equal("array", input.Items!.Type);
        Assert.Equal("number", input.Items.Items!.Type);
    }

    // ---- Format: one compound document ----------------------------------------------------------------------------------------

    [Fact]
    public void Format_IsOneCompoundDocument_ItsIdsKept_AndItsRefsResolvedWithinIt()
    {
        // R2: an entry of the bundle refers to another by its $id; the input refers to the first by #/$defs/, with an
        // annotation beside the reference, so the root is the input's own schema.
        var input = Input(OneInput(
            """{"$ref":"#/$defs/visit","description":"The visit to write up."}""",
            """
            {"visit":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:visit",
              "$schema":"urn:consultologist:spec:31:dialect","additionalProperties":false,
              "properties":{"seen":{"$ref":"urn:consultologist:spec:31:format:date","title":"Date seen"}},
              "required":["seen"],"title":"Visit","type":"object"}}
            """));

        var format = FormatOf(input)!;
        AssertJson(
            """
            {"$ref":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:visit",
             "description":"The visit to write up.",
             "$defs":{
               "visit":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:visit",
                 "additionalProperties":false,
                 "properties":{"seen":{"$ref":"urn:consultologist:spec:31:format:date","title":"Date seen"}},
                 "required":["seen"],"title":"Visit","type":"object"},
               "date":{"$id":"urn:consultologist:spec:31:format:date","description":"A calendar date, written YYYY-MM-DD.","format":"date","type":"string"}}}
            """,
            format);
        AssertResolvesWithin(format);
        Assert.True(Validates(format, """{"seen":"2026-12-01"}"""));
        Assert.False(Validates(format, """{"seen":"first of December"}"""));
        Assert.Equal("object", input.Type);
        Assert.Equal([new WorkflowPackageFieldResponse("seen", "Date seen", true, "date")], input.Fields!, FieldComparer.Instance);
    }

    [Fact]
    public void AFormatUsedTwice_SitsOnceUnderDefs()
    {
        var input = Input(OneInput(
            """{"$ref":"#/$defs/admission"}""",
            """
            {"admission":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:admission",
              "$schema":"urn:consultologist:spec:31:dialect","additionalProperties":false,
              "properties":{"admitted":{"$ref":"urn:consultologist:spec:31:format:date","title":"Admitted"},
                            "discharged":{"$ref":"urn:consultologist:spec:31:format:date","title":"Discharged"}},
              "required":["admitted","discharged"],"type":"object"}}
            """));

        var format = FormatOf(input)!.AsObject();
        Assert.Equal("urn:consultologist:package:example-conditional-sections@v2026.12.1:format:admission", (string)format["$id"]!);
        Assert.Equal(["date"], format["$defs"]!.AsObject().Select(p => p.Key));
        AssertResolvesWithin(format);
        Assert.True(Validates(format, """{"admitted":"2026-11-30","discharged":"2026-12-02"}"""));
        Assert.False(Validates(format, """{"admitted":"2026-11-30","discharged":"soon"}"""));
        Assert.Equal(["admitted", "discharged"], input.Fields!.Select(f => f.Type == "date" ? f.Id : null));
    }

    [Fact]
    public void AFormatFilesOwnDefinitions_StayInIt_AndResolveWithinIt()
    {
        // Part 02 § 3.1: a format file's own #/$defs reference resolves against the file, kept in the bundle as written.
        var input = Input(OneInput(
            """{"items":{"$ref":"#/$defs/dose_list"},"type":"array"}""",
            """
            {"dose_list":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:dose_list",
              "$defs":{"dose":{"additionalProperties":false,"properties":{"mg":{"$ref":"urn:consultologist:spec:31:format:number"}},"required":["mg"],"type":"object"}},
              "$schema":"urn:consultologist:spec:31:dialect",
              "items":{"$ref":"#/$defs/dose"},"type":"array"}}
            """));

        var format = FormatOf(input)!;
        AssertResolvesWithin(format);
        Assert.Equal(["dose_list", "number"], format["$defs"]!.AsObject().Select(p => p.Key));
        Assert.Equal("#/$defs/dose", (string)format["$defs"]!["dose_list"]!["items"]!["$ref"]!);
        Assert.True(Validates(format, """[[{"mg":5}]]"""));
        Assert.False(Validates(format, """[[{"mg":"five"}]]"""));
        Assert.Equal("number", input.Items!.Items!.Fields!.Single().Type);
    }

    [Fact]
    public void AFormatThatRefersToItself_IsFalse_AsTheRunHoldsIt()
    {
        // Publish refuses a recursive format (S02-032); a document that holds one anyway is described as the run reads it:
        // the format nothing fits, never a loop.
        var input = Input(OneInput(
            """{"$ref":"#/$defs/outline"}""",
            """
            {"outline":{"$id":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:outline",
              "additionalProperties":false,
              "properties":{"children":{"items":{"$ref":"urn:consultologist:package:example-conditional-sections@v2026.12.1:format:outline"},"type":"array"}},
              "required":["children"],"type":"object"}}
            """));

        Assert.Equal(JsonValueKind.False, input.Format!.Value.ValueKind);
        Assert.Null(input.Type);
        Assert.Null(input.Fields);
        Assert.Null(input.Items);
    }

    // ---- the endpoint ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Current_DescribesAStamped31Version_FromItsResolveJson()
    {
        var rig = new Rig();
        var (packageRef, _) = rig.Registry.Put(Golden("nested-note"));
        rig.Pin(packageRef);

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(FakeHttp.TextOf(response))!;
        Assert.Equal(31, (int)body["SpecVersion"]!);
        Assert.Equal("[]", body["Macros"]!.ToJsonString());
        Assert.Equal("urn:consultologist:spec:31:format:markdown_block", (string)body["Inputs"]![0]!["Format"]!["$id"]!);
        Assert.Null(body["Inputs"]![0]!["Channel"]);
        await rig.Store.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Current_WithThePreviewUnset_AnswersA31PinWithS09001_As400()
    {
        // Review Focus 4: the setting removed on a deployment whose registry holds 31 versions. The compiled load reads
        // nothing; today's path reads the manifest and its reader refuses 31, which is S09-001, never a 500.
        var rig = new Rig(preview: null);
        var (packageRef, _) = rig.Registry.Put(Golden("nested-note"));
        rig.Pin(packageRef);
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>()).Returns<WorkflowPackage>(_ =>
            throw Assert.Throws<WorkflowPackageSpecVersionException>(() => WorkflowPackageManifestJson.Read(
                """{"specVersion":31,"name":"example-nested-note","version":"v2026.12.1"}""", packageRef.ToString(),
                WorkflowPackageStore.SupportedSpecVersions)));

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!;
        Assert.Equal(SpecVersionAdmissionTests.With(null).NotSupported(31), error);
        V31Contract.AssertSentence(new SourceRefusal("S09-001", error, ""));
        Assert.Empty(rig.Registry.Reads);
    }

    [Fact]
    public async Task Current_WithThePreviewSet_AnswersAnUnstamped31VersionAsNotPublished()
    {
        // A folder publish left without its stamp does not exist for any reader: the compiled load passes it to today's
        // path, whose reader refuses 31; with 31 admitted that is A06-004, not S09-001.
        var rig = new Rig();
        var packageRef = new WorkflowPackageRef("example-nested-note", "v2026.12.1");
        rig.Pin(packageRef);
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowPackageSpecVersionException(packageRef.ToString(), 31, WorkflowPackageStore.SupportedSpecVersions));

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CompiledPackageStore.NotPublished(packageRef.Name, packageRef.Version),
            (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!);
    }

    public static TheoryData<string, string> Refusals() => new()
    {
        { "S09-009", "resolve.json one byte changed" },
        { "S09-014", "lock.json naming another hash" },
        { "S07-004", "requires a kind this engine lacks" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Current_AnswersALoadRefusal_WithItsSentence_As400(string id, string how)
    {
        var rig = new Rig();
        var golden = Golden("nested-note");
        WorkflowPackageRef packageRef;
        switch (how)
        {
            case "resolve.json one byte changed":
            {
                (packageRef, _) = rig.Registry.Put(golden);
                var path = FakeRegistry.PathOf(packageRef, CompiledPackage.ResolveFile);
                rig.Registry.Blobs[path] = Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(rig.Registry.Blobs[path]).Replace("example-nested-note", "example-nested-notf", StringComparison.Ordinal));
                break;
            }

            case "lock.json naming another hash":
            {
                CompiledPackage pair;
                (packageRef, pair) = rig.Registry.Put(golden);
                var document = JsonNode.Parse(pair.Lock)!.AsObject();
                document["resolve"]!["hash"] = CanonicalJson.HashText("{}");
                var lockText = CanonicalJson.Write(document);
                rig.Registry.Blobs[FakeRegistry.PathOf(packageRef, ResolveCompiler.LockFile)] = Encoding.UTF8.GetBytes(lockText);
                rig.Registry.Blobs[FakeRegistry.PathOf(packageRef, PublishedStamp.FileName)] =
                    Encoding.UTF8.GetBytes(new PublishedStamp(pair.ResolveHash, CanonicalJson.HashText(lockText)).ToJson());
                break;
            }

            default:
                packageRef = rig.Registry.PutChanged(golden, resolve: r => r["requires"]!["kinds"]!.AsArray().Add("teleport"));
                break;
        }

        rig.Pin(packageRef);
        var load = await rig.Compiled.TryLoadAsync(packageRef, CancellationToken.None);
        Assert.Equal(id, load!.RefusalId);

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!;
        Assert.Equal(load.Refusal, error);
        V31Contract.AssertSentence(new SourceRefusal(id, error, ""));
        await rig.Store.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Current_TakesTodaysPath_ForAVersionWithNo31Stamp()
    {
        var rig = new Rig();
        var packageRef = new WorkflowPackageRef("general", "v2026.08.1");
        rig.Pin(packageRef);
        var manifest = V7Fixtures.Minimal();
        var data = WorkflowDataResolver.Resolve(manifest, V6Fixtures.Files(manifest), new List<string>());
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>()).Returns(new WorkflowPackage(
            manifest, Nodes: manifest.Nodes, Data: data, ResultNodeId: "assemble-note", Results: [new("consult", "assemble-note", "Assemble note")]));

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(FakeHttp.TextOf(response))!;
        Assert.Equal(manifest.SpecVersion, (int)body["SpecVersion"]!);
        Assert.Null(body["Macros"]);
        Assert.Null(body["Inputs"]![0]!["Format"]);
        Assert.Equal([FakeRegistry.PathOf(packageRef, PublishedStamp.FileName)], rig.Registry.Reads);
    }

    [Fact]
    public async Task Current_TakesTodaysPath_WhenTheRegistryCannotBeRead()
    {
        var rig = new Rig();
        var packageRef = new WorkflowPackageRef("example-nested-note", "v2026.12.1");
        rig.Pin(packageRef);
        rig.Registry.ReadFailure = new Azure.RequestFailedException(503, "unavailable");
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("unavailable"));

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Workflow package registry is unavailable.", (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!);
    }

    [Fact]
    public async Task Current_WithThePreviewSet_AnswersA31PinWhoseStampCouldNotBeRead_503()
    {
        // A storage blip on publish.json: nothing is known of the version, which may well be stamped. Today's path then
        // reads the manifest, which is 31; the answer is the registry's, retriable, never "isn't published".
        var rig = new Rig();
        var (packageRef, _) = rig.Registry.Put(Golden("nested-note"));
        rig.Pin(packageRef);
        rig.Registry.ReadFailure = new Azure.RequestFailedException(503, "unavailable");
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowPackageSpecVersionException(packageRef.ToString(), 31, WorkflowPackageStore.SupportedSpecVersions));

        var response = await rig.CurrentAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Workflow package registry is unavailable.", (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!);
    }

    /// <summary><c>WorkflowPackages/Current</c> over the in-memory registry, the real compiled store and a substituted 5–30 store.</summary>
    private sealed class Rig
    {
        public Rig(string? preview = "31")
        {
            var admission = SpecVersionAdmissionTests.With(preview);
            Compiled = new CompiledPackageStore(Registry, Registry, ThisEngine, admission, new EngineLimitSettings(new ConfigurationBuilder().Build()));
            var authorizer = Substitute.For<IAccountAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(AdminDrainTests.Admin.Account(AppUserId));
            Functions = new WorkflowPackages(
                Store, Pins, publisher: null!, ingestor: null!, lineage: null!, containerFactory: null!, authorizer,
                Substitute.For<IWorkflowPackageOwnership>(), NullLogger<WorkflowPackages>.Instance, Compiled, admission);
        }

        public FakeRegistry Registry { get; } = new();

        public IWorkflowPackageStore Store { get; } = Substitute.For<IWorkflowPackageStore>();

        public IWorkflowPackagePinResolver Pins { get; } = Substitute.For<IWorkflowPackagePinResolver>();

        public CompiledPackageStore Compiled { get; }

        public WorkflowPackages Functions { get; }

        public void Pin(WorkflowPackageRef packageRef)
        {
            Pins.ResolvePinAsync(AppUserId, Arg.Any<CancellationToken>()).Returns(packageRef);
            Registry.ClearReads();
        }

        public async Task<HttpResponseData> CurrentAsync()
        {
            Registry.ClearReads();
            return await Functions.GetCurrentAsync(FakeHttp.Request("GET"));
        }
    }

    /// <summary>Fields compared member by member, their lists by their items.</summary>
    private sealed class FieldComparer : IEqualityComparer<WorkflowPackageFieldResponse>
    {
        public static readonly FieldComparer Instance = new();

        public bool Equals(WorkflowPackageFieldResponse? x, WorkflowPackageFieldResponse? y) =>
            JsonSerializer.Serialize(x) == JsonSerializer.Serialize(y);

        public int GetHashCode(WorkflowPackageFieldResponse obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
