using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Run.BodyComposerTests;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.3 to 8.6, 8.8; part 08 §§ 4, 4.3, 9; part 02 § 3.5; D5, D7, D8, D10, D11,
/// D14, D17): <see cref="AgentTurn"/>, one attempt of an agent call prepared and finished. The prompt shows each offered
/// macro's instruction with the marker to write (D17), a plaintext macro in a Markdown node is escaped (D14), a profile
/// read is recorded by hash and never sent, a macro body's run fact is filled directly (D5), a custom format ends the
/// prompt and a list is enveloped (D8), a retry names what was wrong and never quotes the reply (D11), and the detail
/// carries part 08's members. Every package is published and loaded as the engine loads it. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class AgentTurnTests
{
    private const string Letrozole = "letrozole_prescription_text";

    private const string LetrozoleBody =
        "Discussed side effects including: hot flashes, joint or muscle pain, fatigue, headache, nausea,\nand bone thinning. Advised to monitor side effects.\n";

    private const string ThePlan = "History, then assessment and plan.";

    private static readonly string[] Rejects = ["format"];

    /// <summary>Part 05 § 8.4's files: the classifier <c>prescribes_letrozole</c>, the macro, and <c>assessment</c> (a <c>markdown_block</c> node) placing it.</summary>
    private static Dictionary<string, byte[]> LetrozoleFiles() =>
        Package()
            .WithAgent("prescribes_letrozole", "Does the plan for Margaret Okafor prescribe letrozole?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .WithMacroOf(Letrozole, LetrozoleBody,
                ["Place the following marker, alone on its own line, in the assessment and plan,", "after the medication discussion:", $"{{{{~ macro.{Letrozole}.content }}}}"],
                condition: "{ \"===\": [ { \"var\": \"node.prescribes_letrozole.value.answer\" }, true ] }")
            .WithAgent("assessment", "Write the assessment and plan for Dr A. Sample.\n\n{{ node.plan.content }}\n{{~ macro." + Letrozole + " }}\n", "\"markdown_block\"");

    private static JsonObject PlanScope() => Scope(("node.plan.content", ThePlan));

    internal static AgentCall Call(
        string entryKey, JsonObject scope, IReadOnlyDictionary<string, bool>? placed = null, IReadOnlyList<string>? faults = null,
        string? key = null, string? function = null, bool repair = false) =>
        new(key ?? entryKey, entryKey, function, 1, repair, scope, placed, faults);

    private static PreparedPrompt Prepared(PreparedPrompt prepared)
    {
        Assert.True(prepared.ErrorKind == null, prepared.ErrorKind + " " + prepared.Composition.Failure);
        Assert.NotNull(prepared.Text);
        return prepared;
    }

    private static JsonObject Row(JsonObject detail, string member) => Assert.Single(detail[member]!.AsArray())!.AsObject();

    // ---- the instruction ----------------------------------------------------------------------

    [Fact]
    public void ThePrompt_ShowsTheMarkerToWrite()
    {
        var plan = RunFixture.Plan(LetrozoleFiles());

        var triggered = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(), Placed($"macro.{Letrozole}")), 100_000, Rejects));
        var untriggered = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(),
            new Dictionary<string, bool> { [$"macro.{Letrozole}"] = false }), 100_000, Rejects));

        Assert.Equal(
            "Write the assessment and plan for Dr A. Sample.\n\n" + ThePlan + "\n"
            + "Place the following marker, alone on its own line, in the assessment and plan,\nafter the medication discussion:\n"
            + $"{{{{~ macro.{Letrozole}.content }}}}\n",
            triggered.Text);
        Assert.Contains($"\n{{{{~ macro.{Letrozole}.content }}}}\n", triggered.Text, StringComparison.Ordinal);
        var offered = Assert.Single(triggered.Composition.Macros);
        Assert.Equal(($"macro.{Letrozole}", $"macro.{Letrozole}.content", true), (offered.Site, offered.Marker, offered.Block));
        Assert.Empty(offered.ProfileReads);

        Assert.Equal($"Write the assessment and plan for Dr A. Sample.\n\n{ThePlan}\n\n", untriggered.Text);
        Assert.DoesNotContain("Place the following", untriggered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("macro.", untriggered.Text, StringComparison.Ordinal);
        Assert.Empty(untriggered.Composition.Macros);
    }

    [Fact]
    public void AnIndentedBlockMarker_KeepsItsIndentation_InTheInstruction()
    {
        // D17: the marker's tag renders as its source; for a block marker the indentation before it on its line is kept.
        var plan = RunFixture.Plan(Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["Close the plan with:", "    {{~ macro.m.content }}"])
            .WithAgent("draft", "Draft the note.\n{{~ macro.m }}\n"));

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.draft", new JsonObject(), Placed("macro.m")), 100_000, Rejects));

        Assert.Equal("Draft the note.\nClose the plan with:\n    {{~ macro.m.content }}\n", prepared.Text);
    }

    [Fact]
    public void AFunctionsOwnMacro_IsFoundBeforeThePackages()
    {
        // Task 6's parked minor: LookupMacro is function-first; the composer reads the function's own macro too.
        var files = ResolveFixture.Functions()
            .WithMacroOf("follow_up", "Follow up with Margaret Okafor's GP.\n", ["Close with {{ macro.follow_up.content }}"])
            .With("functions/draft_section/macros/index.json", Index(["follow_up"]))
            .With("functions/draft_section/macros/follow_up.md", Md(
                "{ \"label\": \"Follow-up\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"End with {{ macro.follow_up.content }} as the last sentence.\"] }",
                "Follow up with Dr A. Sample in six weeks.\n"))
            .With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
                "Using the consult draft below, write for this section.\n\n{{ param.draft.content }}\n{{~ macro.follow_up }}\n"));
        var plan = RunFixture.Plan(files);

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.patient_draft", Scope(("param.draft.content", "Margaret Okafor, 58.")),
            Placed("macro.follow_up"), key: "node.standards.section.patient_draft:hpi", function: "draft_section"), 100_000, Rejects));

        Assert.EndsWith("End with {{ macro.follow_up.content }} as the last sentence.\n", prepared.Text, StringComparison.Ordinal);
        Assert.Equal("Follow up with Dr A. Sample in six weeks.", Assert.Single(prepared.Composition.Macros).Body);
        var finished = AgentTurn.Finish(prepared, "The draft. {{ macro.follow_up.content }}");
        Assert.Equal("The draft. Follow up with Dr A. Sample in six weeks.", finished.Reply);
        Assert.Equal("node.standards.section.patient_draft:hpi/macro:follow_up", (string)Row(finished.Detail!, "macros")["id"]!);
    }

    // ---- the retry ----------------------------------------------------------------------------

    [Fact]
    public void ARetry_NamesWhatWasWrong_AndNeverQuotesTheReply()
    {
        var plan = RunFixture.Plan(LetrozoleFiles());
        var placed = Placed($"macro.{Letrozole}");
        const string bad = "Margaret Okafor will start letrozole 2.5 mg daily; reviewed with her daughter.";
        var first = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(), placed), 100_000, Rejects));

        var invalid = AgentTurn.Finish(first, bad);
        Assert.Equal(AgentOutcomeKind.Completed, invalid.Kind);
        var fault = Assert.Single(invalid.Invalid!);
        Assert.Equal("S05-053", fault.Id);

        var retry = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(), placed, [.. invalid.Invalid!.Select(f => f.Sentence)], repair: true),
            100_000, Rejects));

        Assert.Equal(
            first.Text!.TrimEnd() + "\n\nYour last reply could not be used:\n"
            + $"- The reply has no marker for macro '{Letrozole}'.\n"
            + "Write the reply again, with each marker exactly as given above.",
            retry.Text);
        foreach (var piece in new[] { bad, "Margaret Okafor will", "2.5 mg", "her daughter" })
            Assert.DoesNotContain(piece, retry.Text, StringComparison.Ordinal);
        Assert.Equal(CanonicalJson.HashText(retry.Text!), (string)retry.Detail["inputHash"]!);

        // A repair for a reply that did not fit its format (no marker faults) says so, and nothing else.
        var repair = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(), placed, repair: true), 100_000, Rejects));
        Assert.Equal(first.Text!.TrimEnd() + "\n\nYour last reply could not be used: it did not fit the required format. Write the reply again.", repair.Text);
    }

    // ---- the body -----------------------------------------------------------------------------

    [Fact]
    public void APlaintextMacroInAMarkdownNode_IsEscaped()
    {
        var plan = RunFixture.Plan(LetrozoleFiles()
            .WithAgent("summary", "Summarise the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n{{~ macro." + Letrozole + " }}\n"));
        var placed = Placed($"macro.{Letrozole}");
        var body = LetrozoleBody[..^1]; // the file's last line ending is not substituted
        var escaped = MarkdownEscape.Escape(body);
        Assert.NotEqual(body, escaped);

        var markdown = Prepared(AgentTurn.Prepare(plan, Call("node.assessment", PlanScope(), placed), 100_000, Rejects));
        var plain = Prepared(AgentTurn.Prepare(plan, Call("node.summary", PlanScope(), placed), 100_000, Rejects));

        var offered = Assert.Single(markdown.Composition.Macros);
        Assert.Equal(escaped, offered.Body);
        Assert.Equal(CanonicalJson.HashText(escaped), offered.ContentHash);
        Assert.Equal(CanonicalJson.HashText(escaped), (string)Row(markdown.Detail, "macros")["contentHash"]!);
        Assert.Equal(body, Assert.Single(plain.Composition.Macros).Body);
        Assert.Equal(CanonicalJson.HashText(body), Assert.Single(plain.Composition.Macros).ContentHash);

        var finished = AgentTurn.Finish(markdown, $"Assessment.\n{{{{~ macro.{Letrozole}.content }}}}\nPlan.\n");
        Assert.Equal($"Assessment.\n{escaped}\nPlan.\n", finished.Reply);
    }

    [Fact]
    public void AMacroBodyReadingTheProfile_GivesProfileReads_ByHash()
    {
        const string signature = "Dr A. Sample, consultant physician";
        var plan = RunFixture.Plan(Package()
            .WithMacro("signoff", "With thanks,\n{{ profile.signature }}\n")
            .WithAgent("draft", "Draft the note for Margaret Okafor.\n{{~ macro.signoff }}\n")
            .WithJson("nodes/draft.md", h => h["profileCarrying"] = true));

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.draft", Scope(("profile.signature", signature)), Placed("macro.signoff")), 100_000, Rejects));

        Assert.DoesNotContain(signature, prepared.Text, StringComparison.Ordinal);
        var offered = Assert.Single(prepared.Composition.Macros);
        Assert.Equal($"With thanks,\n{signature}", offered.Body);
        Assert.Equal([new ProfileRead("signature", CanonicalJson.HashText(signature))], offered.ProfileReads);
        var read = Row(prepared.Detail, "profileReads");
        Assert.Equal("signature", (string)read["field"]!);
        Assert.Equal(CanonicalJson.HashText(signature), (string)read["hash"]!);
        Assert.DoesNotContain(signature, prepared.Detail.ToJsonString(), StringComparison.Ordinal);

        var finished = AgentTurn.Finish(prepared, "The note. Close {{ macro.signoff.content }}");
        Assert.Equal($"The note. Close With thanks,\n{signature}", finished.Reply);
    }

    [Fact]
    public void AMacroBodysRunFact_IsFilledDirectly()
    {
        // D5: a macro body is substituted into the reply, which has no run-neutral form: its run.* read is no run spot.
        var plan = RunFixture.Plan(Package()
            .WithMacro("reviewed", "Reviewed on {{ run.date | date.to_string \"%d %B %Y\" }}.")
            .WithAgent("draft", "Draft the note for Margaret Okafor.\n{{~ macro.reviewed }}\n")
            .RunDependent("nodes/draft.md"));

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.draft", Scope(("run.date", "2026-11-21")), Placed("macro.reviewed")), 100_000, Rejects));

        Assert.Equal("Reviewed on 21 November 2026.", Assert.Single(prepared.Composition.Macros).Body);
        Assert.Empty(prepared.Composition.Spots);
        Assert.Null(prepared.Composition.NeutralText);
        Assert.Null(prepared.Detail["runNeutralHash"]);
        Assert.Empty(prepared.Detail["runSpots"]!.AsArray());
        Assert.DoesNotContain("{{run:", prepared.Text, StringComparison.Ordinal);
        Assert.Equal("Seen. Reviewed on 21 November 2026.", AgentTurn.Finish(prepared, "Seen. {{ macro.reviewed.content }}").Reply);
    }

    // ---- the format ---------------------------------------------------------------------------

    /// <summary>A package with its own list format <c>points</c> and the agents <c>list_points</c> (of it) and <c>outline_note</c> (of the object format <c>outline</c>), both bound through the custom-format agent.</summary>
    private static Dictionary<string, byte[]> CustomFormats() =>
        Package()
            .Listed("formats/index.json", "points")
            .With("formats/points.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"description\": \"The points of the plan, in order.\", "
                + "\"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }"))
            .WithAgent("list_points", "List the points of the plan for Margaret Okafor.\n\n{{ node.plan.content }}\n", "\"points\"")
            .WithAgent("outline_note", "Outline the note for Dr A. Sample.\n\n{{ node.plan.content }}\n", "\"outline\"");

    [Fact]
    public void ACustomFormat_EndsThePrompt_AndAListIsEnveloped()
    {
        var plan = RunFixture.Plan(CustomFormats());

        var list = Prepared(AgentTurn.Prepare(plan, Call("node.list_points", PlanScope()), 100_000, Rejects));
        var obj = Prepared(AgentTurn.Prepare(plan, Call("node.outline_note", PlanScope()), 100_000, Rejects));

        var format = list.Format!;
        Assert.True(format.Wrapped);
        Assert.Equal("object", (string)format.Sent["type"]!);
        Assert.Equal(["value"], format.Sent["required"]!.AsArray().Select(v => (string)v!));
        Assert.False((bool)format.Sent["additionalProperties"]!);
        Assert.Equal("array", (string)format.Sent["properties"]!["value"]!["type"]!);
        Assert.Equal(
            WorkflowCustomPrompt.Compose(list.Composition.Text!, AgentTurn.CustomTrailer, CanonicalJson.Write(format.Sent)),
            list.Text);
        var text = list.Text!;
        var sent = JsonNode.Parse(text[(text.LastIndexOf('\n') + 1)..]);
        Assert.Equal(CanonicalJson.Hash(sent), format.Hash);
        Assert.Equal(format.Hash, (string)list.Detail["sentFormatHash"]!);

        Assert.Equal("[\"Assessment\",\"Plan\"]", format.Unwrap("{ \"value\": [\"Assessment\", \"Plan\"] }")!.Replace(" ", "", StringComparison.Ordinal));
        Assert.Null(format.Unwrap("[\"Assessment\"]"));
        Assert.Null(format.Unwrap("{ \"other\": 1 }"));
        Assert.Null(format.Unwrap("{ \"value\": 1, \"other\": 2 }"));
        Assert.Null(format.Unwrap("not JSON"));
        Assert.Null(format.Unwrap("{ \"value\": 1, \"value\": 2 }"));
        var finished = AgentTurn.Finish(list, "{\"value\":[\"Assessment\",\"Plan\"]}");
        Assert.Equal(AgentOutcomeKind.Completed, finished.Kind);
        Assert.Equal("[\"Assessment\",\"Plan\"]", finished.Reply);
        Assert.True(plan.Formats.Validate(JsonNode.Parse(finished.Reply!), plan.Entries["node.list_points"].Format).Valid);

        var objectFormat = obj.Format!;
        Assert.False(objectFormat.Wrapped);
        Assert.Equal("object", (string)objectFormat.Sent["type"]!);
        Assert.True(objectFormat.Sent["properties"]!.AsObject().ContainsKey("points"));
        Assert.Equal("{\"points\":[\"a\"]}", objectFormat.Unwrap("{\"points\":[\"a\"]}"));
        Assert.EndsWith("\n" + CanonicalJson.Write(objectFormat.Sent), obj.Text, StringComparison.Ordinal);
        Assert.Equal(objectFormat.Hash, (string)obj.Detail["sentFormatHash"]!);
    }

    [Fact]
    public void AFormatBinding_IsNotSentAFormat_ButItsHashIsRecorded()
    {
        var plan = RunFixture.Plan(CustomFormats().WithAgent("checker", "Is the plan for Margaret Okafor complete?\n\n{{ node.plan.content }}\n", "\"judgement\""));

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.checker", PlanScope()), 100_000, Rejects));

        Assert.Null(prepared.Format);
        Assert.Equal(prepared.Composition.Text, prepared.Text);
        var sent = EnvelopedFormat.Of("judgement", FormatSet.ForPackage(ResolveFixture.Published(), []), Rejects);
        Assert.False(sent.Wrapped);
        Assert.Equal(sent.Hash, (string)prepared.Detail["sentFormatHash"]!);
        Assert.Equal("{\"answer\":true,\"reason\":\"Complete.\"}", AgentTurn.Finish(prepared, "{\"answer\":true,\"reason\":\"Complete.\"}").Reply);
    }

    // ---- the detail ---------------------------------------------------------------------------

    [Fact]
    public void TheDetail_HasPart08sMembers()
    {
        var plan = RunFixture.Plan(LetrozoleFiles()
            .WithMacroOf("follow_up", "Follow up with Dr A. Sample in six weeks.\n", ["Close with {{ macro.follow_up.content }}."])
            .WithSnippet("tone", "Keep a calm tone.")
            .WithAgent("note",
                "Write the note.\n{{~ snippet.tone.content }}\n\n{{ node.plan.content }}\n{{~ macro." + Letrozole + " }}\n{{~ macro.follow_up }}\n",
                "\"markdown_block\""));
        var placed = new Dictionary<string, bool>
        {
            ["snippet.tone"] = true, [$"macro.{Letrozole}"] = false, ["macro.follow_up"] = true,
        };

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.note", PlanScope(), placed), 100_000, Rejects));
        var detail = prepared.Detail;

        Assert.Equal(
            ["inputHash", "hashVersion", "runNeutralHash", "runSpots", "renders", "macros", "profileReads", "sentFormatHash", "notSent", "units"],
            detail.Select(m => m.Key));
        Assert.Equal(CanonicalJson.HashText(prepared.Text!), (string)detail["inputHash"]!);
        Assert.Equal(7, (int)detail["hashVersion"]!);
        Assert.Null(detail["runNeutralHash"]);
        Assert.Empty(detail["runSpots"]!.AsArray());
        Assert.Empty(detail["renders"]!.AsArray());
        Assert.Empty(detail["profileReads"]!.AsArray());
        Assert.Empty(detail["notSent"]!.AsArray());
        var unit = Row(detail, "units");
        Assert.Equal("snippet.tone", (string)unit["site"]!);
        Assert.Equal(CanonicalJson.HashText("Keep a calm tone."), (string)unit["hash"]!);

        var macros = detail["macros"]!.AsArray().Select(m => m!.AsObject()).ToList();
        Assert.Equal(2, macros.Count);
        Assert.Equal(
            $$"""{"id":"node.note/macro:{{Letrozole}}","macro":"macro.{{Letrozole}}","offered":false,"decisions":["node.note/if:macro.{{Letrozole}}"],"classifier":"node.prescribes_letrozole","placed":false,"form":null,"contentHash":null}""",
            macros[0].ToJsonString());
        var followUp = MarkdownEscape.Escape("Follow up with Dr A. Sample in six weeks.");
        Assert.Equal(
            $$"""{"id":"node.note/macro:follow_up","macro":"macro.follow_up","offered":true,"decisions":[],"classifier":null,"placed":false,"form":"inline","contentHash":"{{CanonicalJson.HashText(followUp)}}"}""",
            macros[1].ToJsonString());

        // Finished, the offered macro is placed; the detail is the prepared one with that said.
        var finished = AgentTurn.Finish(prepared, "The note. {{ macro.follow_up.content }}");
        Assert.Null(finished.Invalid);
        Assert.Equal($"The note. {followUp}", finished.Reply);
        var rows = finished.Detail!["macros"]!.AsArray();
        Assert.False((bool)rows[0]!["placed"]!);
        Assert.True((bool)rows[1]!["placed"]!);
        Assert.False((bool)prepared.Detail["macros"]![1]!["placed"]!);

        var invalid = AgentTurn.Finish(prepared, "The note, no marker.");
        Assert.Equal(["S05-053"], invalid.Invalid!.Select(f => f.Id));
        Assert.Equal("The note, no marker.", invalid.Reply);
        Assert.All(invalid.Detail!["macros"]!.AsArray(), m => Assert.False((bool)m!["placed"]!));
    }

    [Fact]
    public void TheDetail_NamesTheSubRows_ByTheRowsKey()
    {
        // A renderer step and a run spot, in an instance of a loop; the classifier is the loop's sibling instance (D10).
        var files = Package()
            .WithRenderer("tag", "\"plaintext\"", "[{{ value }}]", "plaintext")
            .Listed("nodes/index.json", "sections")
            .With("nodes/sections/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(Plain), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.sections.draft\"")))
            .With("nodes/sections/classify.md", Agent("Classifying the section", "\"judgement\"", "Does '{{ item.value.title }}' need a follow-up?\n"))
            .With("nodes/sections/draft.md", Agent("Drafting the section", Plain,
                "Draft '{{ item.value.title | renderer.tag }}' on {{ run.date }}.\n{{~ macro.follow_up }}\n"))
            .RunDependent("nodes/sections/draft.md")
            .RunDependent("nodes/sections/index.json")
            .WithMacroOf("follow_up", "Follow up with Dr A. Sample in six weeks.\n", ["Close with {{ macro.follow_up.content }}."],
                condition: "{ \"===\": [ { \"var\": \"node.classify.value.answer\" }, true ] }");
        var plan = RunFixture.Plan(files);
        var scope = Scope(("item.value.title", "Assessment"), ("run.date", "2026-11-21"));

        var prepared = Prepared(AgentTurn.Prepare(plan, Call("node.sections.draft", scope, Placed("macro.follow_up"), key: "node.sections.draft:2"), 100_000, Rejects));
        var detail = prepared.Detail;

        Assert.Equal("Draft '[Assessment]' on 2026-11-21.\nClose with {{ macro.follow_up.content }}.\n", prepared.Text);
        var render = Row(detail, "renders");
        Assert.Equal("node.sections.draft:2/render:1", (string)render["id"]!);
        Assert.Equal("renderer.tag", (string)render["renderer"]!);
        Assert.Equal("item.value.title", (string)render["source"]!);
        Assert.Equal(
            ["id", "renderer", "templateHash", "source", "inputHash", "inputSize", "outputHash", "outputLength", "runDependent"],
            render.Select(m => m.Key));
        var spot = Row(detail, "runSpots");
        Assert.Equal(
            $$"""{"id":"node.sections.draft:2/spot:1","site":1,"reads":"run.date","valueHash":"{{CanonicalJson.Hash(JsonValue.Create("2026-11-21"))}}","output":"2026-11-21","outputHash":"{{CanonicalJson.HashText("2026-11-21")}}"}""",
            spot.ToJsonString());
        Assert.Equal(CanonicalJson.HashText(prepared.Composition.NeutralText!), (string)detail["runNeutralHash"]!);
        var macro = Row(detail, "macros");
        Assert.Equal("node.sections.draft:2/macro:follow_up", (string)macro["id"]!);
        Assert.Equal("node.sections.classify:2", (string)macro["classifier"]!);
        Assert.Equal(["node.sections.draft:2/if:macro.follow_up"], macro["decisions"]!.AsArray().Select(d => (string)d!));
    }

    [Fact]
    public void APromptThatCannotBePrepared_SaysWhy()
    {
        var plan = RunFixture.Plan(LetrozoleFiles());

        var missing = AgentTurn.Prepare(plan, Call("node.nowhere", PlanScope()), 100_000, Rejects);
        var notAnAgent = AgentTurn.Prepare(plan, Call("node.plan", PlanScope(), function: "no_such_function"), 100_000, Rejects);
        var compose = AgentTurn.Prepare(plan, Call("node.assessment", new JsonObject()), 100_000, Rejects);

        Assert.Equal(("plan", (string?)null), (missing.ErrorKind, missing.Text));
        Assert.Equal("plan", notAnAgent.ErrorKind);
        Assert.Equal("compose", compose.ErrorKind);
        Assert.Null(compose.Text);
        Assert.Equal(RunSentences.CouldNotRender("Node 'assessment'"), compose.Composition.Failure);
        Assert.Equal(AgentOutcomeKind.Failed, AgentTurn.Finish(compose, "anything").Kind);
        Assert.Equal("compose", AgentTurn.Finish(compose, "anything").ErrorKind);

        var loop = RunFixture.Plan(Package().WithAgent("draft", "{{ for s in node.plan.value.sections }}{{ s.title }}{{ end }}\n"));
        var sections = new JsonArray(Enumerable.Range(0, 20).Select(_ => (JsonNode?)new JsonObject { ["title"] = "x", ["kind"] = "narrative" }).ToArray());
        var steps = AgentTurn.Prepare(loop, Call("node.draft", Scope(("node.plan.value.sections", sections))), 10, Rejects);
        Assert.Equal("steps", steps.ErrorKind);
        Assert.True(steps.Composition.TooManySteps);
    }
}
