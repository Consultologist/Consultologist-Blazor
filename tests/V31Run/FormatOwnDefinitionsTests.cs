using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 02 § 3.1): a format file's own <c>$defs</c> at run. The dialect resolves
/// <c>#/$defs/&lt;name&gt;</c> against the file it is written in, and the bundle keeps a file's definitions and those
/// references as written, beside the names it rewrote to <c>#/$defs/&lt;bundle key&gt;</c>. So while an entry of the bundle
/// is inlined, a reference is the entry's own definition first, then the bundle's entry. Each package here goes through
/// publish. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class FormatOwnDefinitionsTests
{
    /// <summary>A plan of visits: its steps are an in-file definition, which refers to another one and to a format by name.</summary>
    private const string VisitPlan =
        "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"steps\"], "
        + "\"additionalProperties\": false, \"properties\": { \"steps\": { \"type\": \"array\", \"items\": { \"$ref\": \"#/$defs/step\" } } }, "
        + "\"$defs\": { "
        + "\"step\": { \"type\": \"object\", \"required\": [\"title\", \"when\"], \"additionalProperties\": false, "
        + "\"properties\": { \"title\": { \"$ref\": \"plaintext\" }, \"when\": { \"$ref\": \"#/$defs/timing\" } } }, "
        + "\"timing\": { \"type\": \"string\", \"enum\": [\"today\", \"next_visit\"] } } }";

    private const string Fitting = """{"steps":[{"title":"Review Margaret Okafor's results","when":"today"},{"title":"Repeat the bloods","when":"next_visit"}]}""";

    private const string WrongTiming = """{"steps":[{"title":"Review Margaret Okafor's results","when":"someday"}]}""";

    private static JsonNode Ref(string key) => new JsonObject { ["$ref"] = $"#/$defs/{key}" };

    /// <summary><see cref="SourceFixture.Minimal"/> with the package formats <paramref name="formats"/> and its agent <c>plan</c> of format <c>visit_plan</c>.</summary>
    private static Dictionary<string, byte[]> Package(params (string Id, string Schema)[] formats)
    {
        var files = Minimal().With("formats/index.json", Index(formats.Select(f => f.Id).ToArray()));
        foreach (var (id, schema) in formats)
            files.With($"formats/{id}.json", Json(schema));
        return files
            .With("nodes/plan.md", Md(
                SourceFixture.Node("agent", "Planning the visits", "\"visit_plan\"", false, true,
                    "\"if\": null, \"onInvalid\": \"fail\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
                "Plan the follow-up visits for Margaret Okafor.\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", PlanRunnerLeavesTests.Plain, "null", "hold"), "Dr A. Sample\n"));
    }

    [Fact]
    public void AFormatWithItsOwnDefinitions_Expands_AndHoldsValuesToThem()
    {
        var formats = RunFixture.Plan(Package(("visit_plan", VisitPlan)), StubTemplates.Empty()).Formats;

        var expanded = Assert.IsType<JsonObject>(formats.Expand(Ref("visit_plan")));
        var text = expanded.ToJsonString();
        Assert.DoesNotContain("$ref", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$defs", text, StringComparison.Ordinal);

        // step, an in-file definition; in it timing, another, and plaintext, the bundle's
        var step = expanded["properties"]!["steps"]!["items"]!;
        Assert.Equal("object", (string?)step["type"]);
        Assert.Equal("""["today","next_visit"]""", step["properties"]!["when"]!["enum"]!.ToJsonString());
        Assert.Equal("string", (string?)step["properties"]!["title"]!["type"]);

        Assert.True(formats.Validate(JsonNode.Parse(Fitting), Ref("visit_plan")).Valid);
        Assert.False(formats.Validate(JsonNode.Parse(WrongTiming), Ref("visit_plan")).Valid);
        Assert.False(formats.Validate(JsonNode.Parse("""{"steps":[{"title":3,"when":"today"}]}"""), Ref("visit_plan")).Valid);

        // around the format, in a site's own schema
        var list = new JsonObject { ["type"] = "array", ["items"] = Ref("visit_plan") };
        Assert.True(formats.Validate(new JsonArray(JsonNode.Parse(Fitting)), list).Valid);
        Assert.False(formats.Validate(new JsonArray(JsonNode.Parse(WrongTiming)), list).Valid);
    }

    [Fact]
    public async Task AnAgentOfSuchAFormat_SettlesItsReply()
    {
        var plan = RunFixture.Plan(Package(("visit_plan", VisitPlan)), StubTemplates.Empty());

        var settled = await Run(plan, Fitting);
        Assert.Equal(RunState.Completed, settled.State);
        Assert.Equal("next_visit", (string?)settled.Values["node.plan"]!["value"]!["steps"]![1]!["when"]);

        var refused = await Run(plan, WrongTiming);
        Assert.Equal(RunState.Failed, refused.State);
        Assert.Equal("Node 'plan' gave a reply that isn't visit_plan.", refused.Sentence);
    }

    [Fact]
    public void AnEntrysOwnDefinitions_AreItsAlone()
    {
        // Two files each define "timing" for themselves, and one refers to the other by name.
        const string review =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"plan\", \"when\"], "
            + "\"additionalProperties\": false, \"properties\": { \"plan\": { \"$ref\": \"visit_plan\" }, \"when\": { \"$ref\": \"#/$defs/timing\" } }, "
            + "\"$defs\": { \"timing\": { \"type\": \"string\", \"enum\": [\"morning\", \"afternoon\"] } } }";
        var files = Package(("visit_plan", VisitPlan), ("review", review));
        files.With("nodes/plan.md", Md(
            SourceFixture.Node("agent", "Planning the visits", "\"review\"", false, true,
                "\"if\": null, \"onInvalid\": \"fail\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
            "Plan the follow-up visits for Margaret Okafor.\n"));
        var formats = RunFixture.Plan(files, StubTemplates.Empty()).Formats;

        Assert.True(formats.Validate(JsonNode.Parse($$"""{"plan":{{Fitting}},"when":"morning"}"""), Ref("review")).Valid);
        Assert.False(formats.Validate(JsonNode.Parse($$"""{"plan":{{Fitting}},"when":"today"}"""), Ref("review")).Valid);
        Assert.False(formats.Validate(JsonNode.Parse("""{"plan":{"steps":[{"title":"Repeat the bloods","when":"morning"}]},"when":"morning"}"""), Ref("review")).Valid);
    }

    /// <summary>
    /// #1508 (area 11; R2): a file that names a package format <c>timing</c> and also defines <c>timing</c> for itself
    /// (which publish refuses, R4, A11-002) once compiled both references to <c>#/$defs/timing</c>, and the run read both
    /// as the file's own definition. The bundle now refers to the package format by its <c>$id</c>, so the run reads each
    /// as publish read it: the file's own definition, and the package format.
    /// </summary>
    [Fact]
    public void ANameBesideAnOwnDefinitionOfTheSameName_ReadsAsThePackageFormat()
    {
        const string timing = "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"slot\"], \"additionalProperties\": false, "
            + "\"properties\": { \"slot\": { \"type\": \"string\", \"enum\": [\"morning\", \"afternoon\"] } } }";
        const string both =
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"mine\", \"theirs\"], "
            + "\"additionalProperties\": false, \"properties\": { \"mine\": { \"$ref\": \"#/$defs/timing\" }, \"theirs\": { \"$ref\": \"timing\" } }, "
            + "\"$defs\": { \"timing\": { \"type\": \"string\", \"enum\": [\"today\", \"next_visit\"] } } }";
        var plan = RunFixture.Plan(Package(("timing", timing), ("visit_plan", both)), StubTemplates.Empty());

        var entry = plan.Document["formats"]!["$defs"]!["visit_plan"]!["properties"]!;
        Assert.Equal("{\"$ref\":\"#/$defs/timing\"}", entry["mine"]!.ToJsonString());
        Assert.Equal(plan.Document["formats"]!["$defs"]!["timing"]!["$id"]!.ToJsonString(), entry["theirs"]!["$ref"]!.ToJsonString());

        // "theirs" is the package format (an object with a slot), as at publish; "mine" the file's own definition.
        Assert.True(plan.Formats.Validate(JsonNode.Parse("""{"mine":"today","theirs":{"slot":"morning"}}"""), Ref("visit_plan")).Valid);
        Assert.False(plan.Formats.Validate(JsonNode.Parse("""{"mine":"today","theirs":"next_visit"}"""), Ref("visit_plan")).Valid);

        // The package format itself, referred to from anywhere else, is unaffected.
        Assert.True(plan.Formats.Validate(JsonNode.Parse("""{"slot":"morning"}"""), Ref("timing")).Valid);
    }

    [Fact]
    public void ACycleThroughAnEntrysOwnDefinitions_ExpandsToFalse()
    {
        // Publish refuses such a file; a compiled document that holds one all the same fits nothing, and is read at once.
        var formats = new PlanFormats(new JsonObject
        {
            ["$defs"] = new JsonObject
            {
                ["chain"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["first"] = Ref("link") },
                    ["$defs"] = new JsonObject
                    {
                        ["link"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["next"] = Ref("back") } },
                        ["back"] = new JsonObject { ["type"] = "array", ["items"] = Ref("link") },
                    },
                },
                ["self"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = Ref("self"),
                    ["$defs"] = new JsonObject { ["self"] = new JsonObject { ["type"] = "array", ["items"] = Ref("self") } },
                },
            },
        });

        Assert.Equal("false", formats.Expand(Ref("chain")).ToJsonString());
        Assert.False(formats.Validate(new JsonObject(), Ref("chain")).Valid);
        Assert.Equal("false", formats.Expand(Ref("self")).ToJsonString());
    }

    private static Task<RunResult> Run(RunPlan plan, string reply) =>
        new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.Completed, reply, 0, null)), new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
}
