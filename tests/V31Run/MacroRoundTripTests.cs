using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Run.BodyComposerTests;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504's first comment (specVersion 31, area 07; part 05 §§ 8.4, 8.6): the 31 version of
/// <c>MacroFolderCompletionTests</c>. A folder item's marker is followed all the way: the package is published and its plan
/// built, the prompt prepared (<see cref="AgentTurn.Prepare"/>), the marker taken from the prepared text itself and written
/// back as the reply, in both forms, and the reply finished (<see cref="AgentTurn.Finish"/>) to the item's content. The
/// marker is qualified, <c>macro.section_notes.hpi.content</c>; the item's bare id names no macro. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MacroRoundTripTests
{
    private const string Content = "History noted: Dr A. Sample.";

    private static readonly Regex AMarker = new(@"\{\{~? macro\.[a-z0-9_.]+\.content \}\}", RegexOptions.CultureInvariant);

    /// <summary>The macro folder <c>section_notes</c> holding <c>hpi</c>, whose instruction is <paramref name="instruction"/>, and the agent <c>draft</c> placing it.</summary>
    private static RunPlan Plan(params string[] instruction) =>
        RunFixture.Plan(Package()
            .Listed("macros/index.json", "section_notes")
            .With("macros/section_notes/index.json", Index(["hpi"], extra: ", \"if\": null"))
            .With("macros/section_notes/hpi.md", Md(
                "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + $"\"instruction\": {new JsonArray(instruction.Select(l => (JsonNode?)l).ToArray()).ToJsonString()} }}",
                Content + "\n"))
            .WithAgent("draft", "Draft the history of present illness for Margaret Okafor.\n{{~ macro.section_notes.hpi }}\n"));

    private static PreparedPrompt Prepare(RunPlan plan)
    {
        var call = new AgentCall("node.draft", "node.draft", null, 1, false, new JsonObject(),
            Placed("macro-folder.section_notes", "macro.section_notes.hpi"));
        var prepared = AgentTurn.Prepare(plan, call, 100_000, ["format"]);
        Assert.True(prepared.ErrorKind == null, prepared.Composition.Failure);
        return prepared;
    }

    /// <summary>The one marker the prepared text holds, as the model reads it there.</summary>
    private static string MarkerIn(PreparedPrompt prepared)
    {
        var marker = Assert.Single(AMarker.Matches(prepared.Text!)).Value;
        Assert.Contains("macro.section_notes.hpi.content", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("{{ macro.hpi.content }}", prepared.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{~ macro.hpi.content }}", prepared.Text, StringComparison.Ordinal);
        return marker;
    }

    private static string Finished(PreparedPrompt prepared, string reply)
    {
        var outcome = AgentTurn.Finish(prepared, reply);
        Assert.Equal(AgentOutcomeKind.Completed, outcome.Kind);
        Assert.True(outcome.Invalid == null, string.Join(" | ", outcome.Invalid?.Select(f => f.Sentence) ?? []));
        Assert.NotNull(outcome.Reply);
        Assert.True((bool)outcome.Detail!["macros"]![0]!["placed"]!);
        return outcome.Reply!;
    }

    [Fact]
    public void AFolderItemsBlockMarker_RoundTrips()
    {
        var prepared = Prepare(Plan("Place the history's closing line alone, after the narrative:", "{{~ macro.section_notes.hpi.content }}"));
        var marker = MarkerIn(prepared);
        Assert.Equal("{{~ macro.section_notes.hpi.content }}", marker);

        var text = Finished(prepared, $"Margaret Okafor reports two weeks of fatigue.\n{marker}\nNo other concerns.\n");

        Assert.Equal($"Margaret Okafor reports two weeks of fatigue.\n{Content}\nNo other concerns.\n", text);
        Assert.DoesNotContain('{', text);
        Assert.DoesNotContain('}', text);
    }

    [Fact]
    public void AFolderItemsInlineMarker_RoundTrips()
    {
        var prepared = Prepare(Plan("End the history with {{ macro.section_notes.hpi.content }} as its last sentence."));
        var marker = MarkerIn(prepared);
        Assert.Equal("{{ macro.section_notes.hpi.content }}", marker);

        var text = Finished(prepared, $"Margaret Okafor reports two weeks of fatigue. {marker}");

        Assert.Equal($"Margaret Okafor reports two weeks of fatigue. {Content}", text);
        Assert.DoesNotContain('{', text);
        Assert.DoesNotContain('}', text);
    }

    [Fact]
    public void AMarkerInsideASentence_AndABlockBeforeALine_LeaveNoStrayLineEnding()
    {
        // Fix round 1 (I1): the body file's last line ending is not substituted, in either form.
        var inline = Prepare(Plan("End the history with {{ macro.section_notes.hpi.content }} as its last sentence."));
        var inlineText = Finished(inline, $"Margaret Okafor reports fatigue; {MarkerIn(inline)} Seen with her daughter.\nPlan to follow.\n");
        Assert.Equal($"Margaret Okafor reports fatigue; {Content} Seen with her daughter.\nPlan to follow.\n", inlineText);

        var block = Prepare(Plan("Place the history's closing line alone, after the narrative:", "{{~ macro.section_notes.hpi.content }}"));
        var blockText = Finished(block, $"Margaret Okafor reports fatigue.\n{MarkerIn(block)}\nPlan to follow.\n\nSigned.\n");
        Assert.Equal($"Margaret Okafor reports fatigue.\n{Content}\nPlan to follow.\n\nSigned.\n", blockText);
    }

    [Fact]
    public void ABodyEndingInTwoLineEndings_KeepsOne()
    {
        var plan = RunFixture.Plan(Package()
            .Listed("macros/index.json", "section_notes")
            .With("macros/section_notes/index.json", Index(["hpi"], extra: ", \"if\": null"))
            .With("macros/section_notes/hpi.md", Md(
                "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the history's closing line alone:\", \"{{~ macro.section_notes.hpi.content }}\"] }",
                Content + "\n\n"))
            .WithAgent("draft", "Draft the history of present illness for Margaret Okafor.\n{{~ macro.section_notes.hpi }}\n"));
        var prepared = Prepare(plan);

        var offered = Assert.Single(prepared.Composition.Macros);
        Assert.Equal(Content + "\n", offered.Body);
        Assert.Equal(Consultologist.PackageFormat.V31.CanonicalJson.HashText(Content + "\n"), offered.ContentHash);
        Assert.Equal($"Fatigue.\n{Content}\n\nPlan.\n", Finished(prepared, "Fatigue.\n{{~ macro.section_notes.hpi.content }}\nPlan.\n"));
    }

    [Fact]
    public void ABareIdReply_IsRefused()
    {
        var prepared = Prepare(Plan("Place the history's closing line alone, after the narrative:", "{{~ macro.section_notes.hpi.content }}"));

        var outcome = AgentTurn.Finish(prepared, "Margaret Okafor reports two weeks of fatigue.\n{{~ macro.hpi.content }}\n");

        Assert.Equal(["S05-056", "S05-053"], outcome.Invalid!.Select(f => f.Id));
        Assert.Equal("The reply has a marker for 'hpi', which isn't a macro.", outcome.Invalid![0].Sentence);
        Assert.Equal("The reply has no marker for macro 'section_notes.hpi'.", outcome.Invalid![1].Sentence);
        Assert.False((bool)outcome.Detail!["macros"]![0]!["placed"]!);
    }
}
