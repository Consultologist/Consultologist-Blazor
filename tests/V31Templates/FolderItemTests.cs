using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.FolderItemFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>#1504 (specVersion 31, area 07; part 05 § 6.2): the packages of <see cref="FolderItemTests"/> and of the run-time tests of a folder item's body.</summary>
internal static class FolderItemFixture
{
    /// <summary>The agent <c>section</c> of the loop <c>standards</c>: reads its item's title and, on line 3, its body.</summary>
    internal const string Reader = "Draft the section '{{ item.value.title }}' for Margaret Okafor.\n\n{{ item.content }}\n";

    internal const string Pmh = "List each past condition on its own line.\n";

    /// <summary>
    /// The snippet folder <c>standards</c> (format <c>standard_section</c>, a <c>title</c>) holding <c>hpi</c> and
    /// <c>pmh</c>, the agent <c>plan</c> returning a <c>section_plan</c>, and the loop <c>standards</c> over the folder of
    /// the rendered node <c>context</c> and the agent <c>section</c> (<paramref name="reader"/>).
    /// </summary>
    internal static Dictionary<string, byte[]> Standards(string hpi, string pmh = Pmh, string reader = Reader) =>
        Folder(hpi, pmh).Loop("standards", "section", reader);

    /// <summary>The package of <see cref="Standards"/> without its loop, the agent <c>plan</c> returning a <c>section_plan</c>.</summary>
    internal static Dictionary<string, byte[]> Folder(string hpi, string pmh = Pmh) =>
        ResolveFixture.WithFormats(Minimal(), "section_plan", "standard_section")
            .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
                "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"))
            .Listed("snippets/index.json", "standards")
            .With("snippets/standards/index.json", Index(["hpi", "pmh"], extra: ", \"format\": \"standard_section\""))
            .With("snippets/standards/hpi.md", Md("{ \"title\": \"History of Present Illness\" }", hpi))
            .With("snippets/standards/pmh.md", Md("{ \"title\": \"Past Medical History\" }", pmh));

    /// <summary>Adds the loop <paramref name="id"/> over <c>snippet.standards</c> of the rendered node <c>context</c> and the agent <paramref name="agent"/>.</summary>
    internal static Dictionary<string, byte[]> Loop(this Dictionary<string, byte[]> files, string id, string agent, string reader) =>
        files.Listed("nodes/index.json", id)
            .With($"nodes/{id}/index.json", Json(Node("for", $"Loop {id}", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + $"\"itemLabel\": \"title\", \"nodes\": [\"context\", \"{agent}\"], \"ret\": \"node.{id}.{agent}\"")))
            .With($"nodes/{id}/context.md", Md(Node("rendered", "Context", Plain, true, false, "\"if\": null"), "Context for the section.\n"))
            .With($"nodes/{id}/{agent}.md", Agent($"Drafting in {id}", Plain, reader));
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.1, 3.2, 6.2; part 04 § 2.6): a loop over a snippet folder reads each
/// item's body as <c>item.content</c>, "rendered in the loop's scope". So a body that reads <c>item.content</c> places,
/// in effect, every item of that folder there: each item's body is checked as a snippet body at that reader's place, with
/// that reader's row of § 3.1 (S05-022 where a read is not there), its reads count as the reader's, its heads join the
/// reader's <c>names</c>, and it keeps the varying text simple: no part, macro or snippet, no renderer, no run spot
/// (A07-012). Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class FolderItemTests
{
    /// <summary>A07-012 as said for <paramref name="does"/> of hpi, read by <c>section</c> as <c>item.content</c> at line 3.</summary>
    private static string Barred(string does) =>
        $"Snippet 'standards/hpi' {does}, and Node 'section' reads it as item.content at line 3; a folder item's body read in a loop "
        + "places no part, macro or snippet, pipes into no renderer and reads no run.* or run-dependent value.";

    [Fact]
    public void AFolderItemsBody_IsCheckedWhereItIsRead()
    {
        var valid = Check(Standards("Open with {{ item.value.title }}.\n\n{{ input.consult_draft.content }}\n"));

        var body = Body(valid, "nodes/standards/section.md");
        Assert.Equal(["snippets/standards/hpi.md", "snippets/standards/pmh.md"], body.FolderItems.Select(b => b.Host.Path));
        Assert.All(body.FolderItems, b => Assert.Equal(TemplateSite.SnippetBody, b.Host.Site));
        Assert.Equal("Snippet 'standards/hpi'", body.FolderItems[0].Host.Subject);
        Assert.Equal(["item.value.title", "input.consult_draft.content"], body.FolderItems[0].Reads.Select(r => r.Written));
        Assert.Empty(body.Units);

        var profile = Single(Check(Standards("Sign as {{ profile.signature }}.\n")));
        Assert.Equal("S05-022", profile.Id);
        Assert.Equal("Snippet 'standards/hpi' reads profile.signature, which isn't available where Node 'section' places it at line 3.", profile.Sentence);
        Assert.Equal("nodes/standards/section.md", profile.Path);

        var nowhere = Single(Check(Standards("Read {{ node.nowhere.content }}.\n")));
        Assert.Equal("S05-022", nowhere.Id);
        Assert.Equal("Snippet 'standards/hpi' reads node.nowhere.content, which isn't available where Node 'section' places it at line 3.", nowhere.Sentence);
    }

    [Fact]
    public void AFolderItemsBody_IsSaidOnEveryLineThatReadsIt()
    {
        var check = Check(Standards("Sign as {{ profile.signature }}.\n", reader: "{{ item.content }}\nAgain:\n{{ item.content }}\n"));

        Assert.Equal([1, 3], check.Refusals.Select(r => int.Parse(r.Sentence.Split("at line ")[1].TrimEnd('.'), System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(check.Refusals, V31Contract.AssertSentence);
    }

    [Fact]
    public void AFolderItemsReads_AreTheReadingNodesReads()
    {
        var files = Standards("Open with {{ node.context.content }}.\n\n{{ input.consult_draft.content }}\n", "For {{ node.plan.content }}.\n");
        var check = Check(files);
        var leaf = (SourceLeaf)Body(check, "nodes/standards/section.md").Host.Declaration;

        var reads = check.Templates!.Of(leaf, null)!.Reads;
        Assert.Equal(["item.value.title", "item.content", "node.context.content", "input.consult_draft.content", "node.plan.content"], reads);
        Assert.Equal(new Dictionary<string, string> { ["node.context"] = "node.standards.context" }, check.Templates.Names(leaf, null));

        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, Said(refusals));
        var entry = JsonNode.Parse(compiled!.Resolve)!["nodes"]!["node.standards.section"]!;
        Assert.Contains("node.standards.context.content", entry["reads"]!.AsArray().Select(r => (string)r!));
        Assert.Contains("input.consult_draft.content", entry["reads"]!.AsArray().Select(r => (string)r!));
        Assert.Contains("node.plan.content", entry["reads"]!.AsArray().Select(r => (string)r!));
        Assert.Equal("node.standards.context", (string)entry["names"]!["node.context"]!);
    }

    public static TheoryData<string, string> Restrictions() => new()
    {
        { "Close.\n{{~ snippet.tone.content }}\n", "places snippet.tone.content" },
        { "Close.\n{{~ part.brief }}\n", "places part.brief" },
        { "Close.\n{{~ macro.m }}\n", "places macro.m" },
        { "Title: {{ item.value.title | renderer.line }}\n", "pipes into renderer.line" },
        { "Written on {{ run.date }}.\n", "reads run.date" },
        { "Written on {{ node.today.value }}.\n", "reads node.today.value" },
        { "Again: {{ item.content }}\n", "places item.content" },
    };

    [Theory]
    [MemberData(nameof(Restrictions))]
    public void AFolderItemsBody_KeepsItsTextSimple(string hpi, string does)
    {
        var files = Standards(hpi)
            .WithSnippet("tone", "Keep a calm tone.\n")
            .WithMacro("m", "Follow up with Dr A. Sample.\n")
            .WithRenderer("line", "\"plaintext\"", "- {{ value }}\n")
            .WithComputed("today", "\"date\"", "{ \"var\": \"run.date\" }", runDependent: true);

        var refusal = Single(Check(files));

        Assert.Equal("A07-012", refusal.Id);
        Assert.Equal(Barred(does), refusal.Sentence);
        Assert.Equal("nodes/standards/section.md", refusal.Path);
    }

    [Fact]
    public void AFolderItemReadInTwoLoops_IsCheckedInEach()
    {
        // hpi reads the loop's own rendered node context: there in the loop standards, not in the loop others, which has its own.
        var files = Folder("Open with {{ node.context.content }}.\n")
            .Loop("standards", "section", Reader)
            .Listed("nodes/index.json", "others")
            .With("nodes/others/index.json", Json(Node("for", "Loop others", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"other_section\"], \"ret\": \"node.others.other_section\"")))
            .With("nodes/others/other_section.md", Agent("Drafting again", Plain, "Again.\n{{ item.content }}\n"));

        var refusal = Single(Check(files));

        Assert.Equal("S05-022", refusal.Id);
        Assert.Equal("Snippet 'standards/hpi' reads node.context.content, which isn't available where Node 'other_section' places it at line 2.", refusal.Sentence);
        Assert.Equal("nodes/others/other_section.md", refusal.Path);

        // Read in each loop, each reader holds hpi checked at its own place, with its own rooted key.
        var both = Check(Folder("Open with {{ node.context.content }}.\n").Loop("standards", "section", Reader).Loop("again", "section", Reader));
        var first = Body(both, "nodes/standards/section.md").FolderItems[0];
        var second = Body(both, "nodes/again/section.md").FolderItems[0];
        Assert.Equal("node.standards.context", first.Reads.Single(r => r.Written == "node.context.content").RootedHead);
        Assert.Equal("node.again.context", second.Reads.Single(r => r.Written == "node.context.content").RootedHead);
    }

    [Fact]
    public void AFolderItemReadDirectly_IsAPlacementAsBefore()
    {
        var files = Folder("Open with {{ input.consult_draft.content }}.\n")
            .WithAgent("draft", "Draft.\n{{~ snippet.standards.hpi.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal("snippet.standards.hpi", Assert.Single(body.Units).Site);
        Assert.Empty(body.FolderItems);
    }

    [Fact]
    public void AnEmptyFolderItemsBody_IsValid()
    {
        var body = Body(Check(Standards("", "")), "nodes/standards/section.md");

        Assert.Equal(2, body.FolderItems.Count);
        Assert.All(body.FolderItems, b => Assert.Empty(b.Reads));
    }

    [Theory]
    [InlineData("{{ snippet.nope.content }} {{ part }} {{ macro.a.b.c.d }} {{ part.x | string.upcase }}")]
    [InlineData("{{ for s in item.value }}{{ s.x }}{{ end }} {{ if s.state == \"done\" }}{{ end }}")]
    [InlineData("{{ x = 1 }}")]
    [InlineData("{{ item.parent.content }} {{ item.content | string.upcase }} {{ item }} {{ item.value }}")]
    [InlineData("{{~ snippet.standards.pmh.content }}\n{{ profile.nope }} {{ run.date | renderer.nope }} {{ pass.number }}")]
    [InlineData("{{ if item.value.title }}{{ item.content }}{{ end }} {%{ {{ item.content }} }%}")]
    [InlineData("Plain prose, no tags.")]
    public void AnyFolderItemBody_IsRefusedOrRead_NeverThrows(string hpi)
    {
        var check = Check(Standards(hpi + "\n"));

        Assert.All(check.Refusals, V31Contract.AssertSentence);
        if (check.Refusals.Count == 0)
            Assert.Equal(2, Body(check, "nodes/standards/section.md").FolderItems.Count);
    }
}
