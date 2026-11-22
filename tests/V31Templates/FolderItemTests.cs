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

    // ---- routes out of the loop (fix round 1) --------------------------------------------------------

    /// <summary><paramref name="files"/> with the text <paramref name="old"/> of <paramref name="path"/> replaced (it must be there).</summary>
    private static Dictionary<string, byte[]> Edited(Dictionary<string, byte[]> files, string path, string old, string replacement)
    {
        var text = System.Text.Encoding.UTF8.GetString(files[path]);
        Assert.Contains(old, text, StringComparison.Ordinal);
        files[path] = System.Text.Encoding.UTF8.GetBytes(text.Replace(old, replacement, StringComparison.Ordinal));
        return files;
    }

    /// <summary>Part 04 § 7.7's package (<see cref="ResolveFixture.Functions"/>), the call <c>section</c> binding <paramref name="with"/>, hpi's body <paramref name="hpi"/>.</summary>
    private static Dictionary<string, byte[]> Called(string with, string hpi)
    {
        var files = Edited(ResolveFixture.Functions(), "nodes/standards/section.md", "\"standard\": \"item\", \"draft\": \"input.consult_draft.content\"", with);
        return Edited(files, "snippets/standards/hpi.md", "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n", hpi);
    }

    private static string Carried(string subject, string route, int line = 1) =>
        $"Snippet 'standards/hpi' holds a tag at line {line}, and {subject} {route}, where it isn't rendered; "
        + "keep a folder item's body plain text, or read it as item.content in its loop.";

    private const string Passes = "passes it to function 'draft_section' as param.";

    [Theory]
    [InlineData("\"standard\": \"item\", \"draft\": \"input.consult_draft.content\"", "standard")]
    [InlineData("\"standard\": \"snippet.standards.hpi\", \"draft\": \"input.consult_draft.content\"", "standard")]
    public void AFolderBodyPassedToAFunction_IsPlainText(string with, string param)
    {
        var refusal = Single(Check(Called(with, "Open with '{{ item.value.title }}'.\n")));

        Assert.Equal("A07-013", refusal.Id);
        Assert.Equal(Carried("Call 'section'", Passes + param), refusal.Sentence);
        Assert.Equal("nodes/standards/section.md", refusal.Path);

        Assert.Empty(Check(Called(with, "Open with a one-sentence orientation.\n")).Refusals);
    }

    [Fact]
    public void AFolderBodyPassedAsText_IsPlainText_OnEachLineItIsTagged()
    {
        // item.content passed as the markdown_block draft: its text, raw; a raw block is no plain text either.
        var check = Check(Called("\"standard\": \"item\", \"draft\": \"item.content\"", "Plain first.\nKeep {%{ this }%} as written.\n"));

        Assert.Equal([Carried("Call 'section'", Passes + "standard", line: 2), Carried("Call 'section'", Passes + "draft", line: 2)],
            check.Refusals.Select(r => r.Sentence));
        Assert.All(check.Refusals, V31Contract.AssertSentence);
    }

    /// <summary>The loop <c>standards</c> over the folder of the computed node <c>text</c> (<paramref name="header"/> its <c>if</c> and <c>expression</c>).</summary>
    private static Dictionary<string, byte[]> Computing(string hpi, string header) =>
        Folder(hpi)
            .Listed("nodes/index.json", "standards")
            .With("nodes/standards/index.json", Json(Node("for", "Standards", ArrayOf("\"markdown_block\""), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"text\"], \"ret\": \"node.standards.text\"")))
            .With("nodes/standards/text.md", Md(Node("computed", "Text", "\"markdown_block\"", true, true, header), ""));

    [Fact]
    public void AFolderBodyReadByAnExpression_IsPlainText()
    {
        const string reads = "\"if\": null, \"expression\": { \"var\": \"item.content\" }";
        var refusal = Single(Check(Computing("Open with '{{ item.value.title }}'.\n", reads)));

        Assert.Equal("A07-013", refusal.Id);
        Assert.Equal(Carried("Node 'text'", "reads it in its expression"), refusal.Sentence);
        Assert.Equal("nodes/standards/text.md", refusal.Path);
        Assert.Empty(Check(Computing("Open plainly.\n", reads)).Refusals);

        // A condition only decides: it carries no text, so the body may be a template there.
        Assert.Empty(Check(Computing("Open with '{{ item.value.title }}'.\n",
            "\"if\": { \"!\": [{ \"blank\": [{ \"var\": \"item.content\" }] }] }, \"expression\": \"Has a standard.\"")).Refusals);
    }

    /// <summary>The element of <c>node.standards.items</c> in part 04 § 7.7's package: its <c>item</c> carries the folder item (and, at run time, its body).</summary>
    private const string Entry = "{ \"type\": \"object\", \"required\": [\"id\", \"value\"], \"additionalProperties\": false, "
        + "\"properties\": { \"id\": { \"type\": \"string\" }, \"value\": { \"$ref\": \"standard_section\" } } }";

    private const string OrNullText = "{ \"anyOf\": [{ \"$ref\": \"plaintext\" }, { \"type\": \"null\" }] }";

    private const string States = "{ \"type\": \"string\", \"enum\": [\"completed\", \"skipped\", \"not_run\"] }";

    /// <summary>An element of <c>node.standards.items</c>: its <c>item</c> (<see cref="Entry"/>), its label, state, value and content, and its nodes'.</summary>
    private const string Element = "{ \"type\": \"object\", "
        + "\"required\": [\"item\", \"label\", \"state\", \"reason\", \"value\", \"content\", \"nodes\"], \"additionalProperties\": false, \"properties\": { "
        + "\"item\": " + Entry + ", \"label\": { \"type\": \"string\" }, \"state\": " + States + ", \"reason\": { \"type\": [\"string\", \"null\"] }, "
        + "\"value\": " + OrNullText + ", \"content\": " + OrNullText + ", "
        + "\"nodes\": { \"type\": \"object\", \"required\": [\"section\"], \"additionalProperties\": false, \"properties\": { \"section\": { "
        + "\"type\": \"object\", \"required\": [\"value\", \"content\", \"state\"], \"additionalProperties\": false, \"properties\": { "
        + "\"value\": " + OrNullText + ", \"content\": " + OrNullText + ", \"state\": " + States + " } } } } } }";

    private const string Items = "{ \"type\": \"array\", \"items\": " + Element + " }";

    /// <summary>
    /// Part 04 § 7.7's package (hpi's body <paramref name="hpi"/>) with the loop <c>again</c> over <c>node.standards.items</c>
    /// whose call <c>redo</c> passes <paramref name="argument"/> to the function <c>note_item</c>, of the one parameter
    /// <c>entry</c> (<paramref name="format"/>, inline), which reads <c>param.entry.content</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Redone(string argument, string format, string hpi) =>
        WithNoteItem(Edited(ResolveFixture.Functions(), "snippets/standards/hpi.md",
                "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n", hpi), format)
            .Listed("nodes/index.json", "again")
            .With("nodes/again/index.json", Json(Node("for", "Again", ArrayOf(Plain), false, true,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"redo\"], \"ret\": \"node.again.redo\"")))
            .With("nodes/again/redo.md", Redo(argument));

    /// <summary>The call <c>redo</c> passing <paramref name="argument"/> to <see cref="WithNoteItem"/>'s function as <c>param.entry</c>.</summary>
    private static string Redo(string argument) =>
        Md(Node("call", "Redoing the section", Plain, false, true,
            "\"if\": null, \"function\": \"note_item\", \"with\": { \"entry\": \"" + argument + "\" }"), "");

    /// <summary>Adds the function <c>note_item</c>, of the one parameter <c>entry</c> (<paramref name="format"/>, inline), which reads <c>param.entry.content</c>.</summary>
    private static Dictionary<string, byte[]> WithNoteItem(Dictionary<string, byte[]> files, string format)
    {
        files.Listed("functions/index.json", "note_item")
            .With("functions/note_item/index.json", Json(
                "{ \"label\": \"Note an item\", \"description\": null, \"inputs\": { "
                + "\"entry\": { \"label\": \"Entry\", \"description\": null, \"format\": " + format + " } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [\"standard_section\"], \"renderers\": [] }, "
                + "\"nodes\": [\"say\"], \"ret\": \"node.say\", \"format\": \"plaintext\" }"))
            .With("functions/note_item/say.md", Agent("Noting the entry", Plain, "Note:\n{{ param.entry.content }}\n"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/note_item/{folder}/index.json", Index([]));
        return files;
    }

    [Theory]
    [InlineData("item.item", Entry)]
    [InlineData("item", Element)]
    [InlineData("node.standards.items", Items)]
    public void AFolderBodyCarriedByALoopsItems_IsPlainText(string argument, string format)
    {
        // The runner's .items element holds item.content, the body as written; passed on, nothing renders it.
        var check = Check(Redone(argument, format, "Open with '{{ item.value.title }}'.\n"));

        var redo = Assert.Single(check.Refusals, r => r.Path == "nodes/again/redo.md");
        V31Contract.AssertSentence(redo);
        Assert.Equal(Carried("Call 'redo'", "passes it to function 'note_item' as param.entry"), redo.Sentence);
        Assert.Single(check.Refusals, r => r.Path == "nodes/standards/section.md");

        Assert.Empty(Check(Redone(argument, format, "Open with a one-sentence orientation.\n")).Refusals);
    }

    /// <summary>
    /// Part 04 § 7.7's package (hpi's body <paramref name="hpi"/>) with the loop <c>again</c> over <c>node.standards.items</c>
    /// of the computed node <c>copy</c> (<paramref name="format"/>), whose expression reads <paramref name="read"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Copied(string read, string format, string hpi) =>
        Edited(ResolveFixture.Functions(), "snippets/standards/hpi.md",
                "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n", hpi)
            .Listed("nodes/index.json", "again")
            .With("nodes/again/index.json", Json(Node("for", "Again", ArrayOf(format), false, true,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"copy\"], \"ret\": \"node.again.copy\"")))
            .With("nodes/again/copy.md", Md(Node("computed", "Copy", format, true, true,
                "\"if\": null, \"expression\": { \"var\": \"" + read + "\" }"), ""));

    [Theory]
    [InlineData("item.item", Entry)]
    [InlineData("item", Element)]
    [InlineData("node.standards.items", Items)]
    public void AFolderBodyCarriedByALoopsItems_InAnExpression_IsPlainText(string read, string format)
    {
        // A computed node's value is what its expression reads: the .items element's item, holding the body as written.
        var check = Check(Copied(read, format, "Open with '{{ item.value.title }}'.\n"));

        var copy = Assert.Single(check.Refusals, r => r.Path == "nodes/again/copy.md");
        V31Contract.AssertSentence(copy);
        Assert.Equal(Carried("Node 'copy'", "reads it in its expression"), copy.Sentence);

        Assert.Empty(Check(Copied(read, format, "Open with a one-sentence orientation.\n")).Refusals);
    }

    // ---- at every depth (#1505 ruling 5, R13) ---------------------------------------------------------

    /// <summary>
    /// Part 04 § 7.7's package (hpi's body <paramref name="hpi"/>) with loops chained below <c>standards</c>, each over the
    /// one before's <c>.items</c>: <c>again</c>, then <c>third</c>, then <c>fourth</c>, <paramref name="loops"/> loops in
    /// all with <c>standards</c>. Each but the last runs the computed node <c>&lt;loop&gt;_step</c> (a fixed text); the last
    /// runs <paramref name="last"/> (the file <paramref name="file"/>), of format <paramref name="format"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Chained(Dictionary<string, byte[]> files, int loops, string last, string file, string format)
    {
        string[] ids = ["again", "third", "fourth"];
        var over = "standards";
        for (var i = 0; i < loops - 1; i++)
        {
            var (id, final) = (ids[i], i == loops - 2);
            var node = final ? last : $"{id}_step";
            files.Listed("nodes/index.json", id)
                .With($"nodes/{id}/index.json", Json(Node("for", $"Loop {id}", ArrayOf(final ? format : Plain), false, true,
                    $"\"in\": \"node.{over}.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                    + $"\"itemLabel\": null, \"nodes\": [\"{node}\"], \"ret\": \"node.{id}.{node}\"")))
                .With($"nodes/{id}/{node}.md", final ? file
                    : Md(Node("computed", $"Step {id}", Plain, true, true, "\"if\": null, \"expression\": \"Next.\""), ""));
            over = id;
        }

        return files;
    }

    private static Dictionary<string, byte[]> WithHpi(string hpi) =>
        Edited(ResolveFixture.Functions(), "snippets/standards/hpi.md",
            "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n", hpi);

    /// <summary><see cref="Chained"/> whose last loop's call <c>redo</c> passes <paramref name="argument"/> to <c>note_item</c> as <c>param.entry</c> (<paramref name="format"/>).</summary>
    private static Dictionary<string, byte[]> RedoneDeep(int loops, string argument, string format, string hpi) =>
        Chained(WithNoteItem(WithHpi(hpi), format), loops, "redo", Redo(argument), Plain);

    /// <summary><see cref="Chained"/> whose last loop's computed node <c>copy</c> (<paramref name="format"/>) reads <paramref name="read"/>.</summary>
    private static Dictionary<string, byte[]> CopiedDeep(int loops, string read, string format, string hpi) =>
        Chained(WithHpi(hpi), loops, "copy",
            Md(Node("computed", "Copy", format, true, true, "\"if\": null, \"expression\": { \"var\": \"" + read + "\" }"), ""), format);

    /// <summary>
    /// The folder item as the last of <paramref name="loops"/> loops reads it: its <c>item</c> is the element of the loop
    /// before's <c>.items</c>, whose <c>item.value</c> is the element of the one before that, down to the production loop's,
    /// whose <c>item</c> is the folder item (<c>item.item</c> two loops deep, <c>item.item.value.item</c> three deep).
    /// </summary>
    private static string Through(int loops) => "item.item" + string.Concat(Enumerable.Repeat(".value.item", loops - 2));

    private const string Tagged = "Open with '{{ item.value.title }}'.\n";

    private const string Plainly = "Open with a one-sentence orientation.\n";

    /// <summary>A body with a tag carried out of the last of <paramref name="loops"/> loops, to a function and to an expression, is A07-013 there.</summary>
    private static void AssertCarriedDeep(int loops)
    {
        // The folder item itself, and the production loop's element, which holds it.
        var element = Through(loops)[..^".item".Length];
        foreach (var (path, format) in new[] { (Through(loops), Entry), (element, Element) })
        {
            var call = Check(RedoneDeep(loops, path, format, Tagged));
            var redo = Assert.Single(call.Refusals, r => r.Path.EndsWith("/redo.md", StringComparison.Ordinal));
            V31Contract.AssertSentence(redo);
            Assert.Equal(Carried("Call 'redo'", "passes it to function 'note_item' as param.entry"), redo.Sentence);
            Assert.Empty(Check(RedoneDeep(loops, path, format, Plainly)).Refusals);

            var expression = Check(CopiedDeep(loops, path, format, Tagged));
            var copy = Assert.Single(expression.Refusals, r => r.Path.EndsWith("/copy.md", StringComparison.Ordinal));
            V31Contract.AssertSentence(copy);
            Assert.Equal(Carried("Node 'copy'", "reads it in its expression"), copy.Sentence);
            Assert.Empty(Check(CopiedDeep(loops, path, format, Plainly)).Refusals);
        }
    }

    [Fact]
    public void AFolderItemCarried_ThreeLoopsDeep_IsRefused() => AssertCarriedDeep(3);

    [Fact]
    public void AFolderItemCarried_FourLoopsDeep_IsRefused() => AssertCarriedDeep(4);

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void WhatTheChainHoldsBesideTheBody_IsNotCarried(int loops)
    {
        // The folder item's header (its .value) and the production loop's element's content (its section, as rendered) are
        // not the body: only a read that holds the folder item is A07-013.
        var element = Through(loops)[..^".item".Length];
        foreach (var (path, format) in new[] { (Through(loops) + ".value", "\"standard_section\""), (element + ".content", OrNullText) })
        {
            Assert.Empty(Check(RedoneDeep(loops, path, format, Tagged)).Refusals.Where(r => r.Path.EndsWith("/redo.md", StringComparison.Ordinal)));
            Assert.Empty(Check(CopiedDeep(loops, path, format, Tagged)).Refusals.Where(r => r.Path.EndsWith("/copy.md", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("{{ item | renderer.card }}\n", "{{ value.title }}\n", "A07-008")]
    [InlineData("{{ item.value | renderer.card }}\n", "{{ value.content }}\n", "S05-011")]
    public void AWholeFolderItemPipedIntoARenderer_IsRefusedAlready(string reader, string renderer, string id)
    {
        // A bare item is no value to pipe (A07-008), and a renderer of the folder's format reads only its header (S05-011):
        // no renderer reaches a folder item's body but through item.content, which is rendered first (A07-012's route).
        var files = Standards("Open with '{{ item.value.title }}'.\n", reader: reader)
            .WithRenderer("card", "\"standard_section\"", renderer);

        Assert.Equal(id, Single(Check(files)).Id);
        Assert.Empty(Check(Standards("Open with '{{ item.value.title }}'.\n", reader: "{{ item.content | renderer.text }}\n")
            .WithRenderer("text", "\"markdown_block\"", "{{ value }}\n")).Refusals);
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
