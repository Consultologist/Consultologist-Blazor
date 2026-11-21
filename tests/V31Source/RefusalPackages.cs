using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): one conformance case for a refusal sentence: a valid package
/// (the nearest one that keeps the rule, the limit itself where the rule is a limit) and an invalid
/// one that breaks the rule alone, each as the upload a reader receives (path to bytes).
/// </summary>
internal sealed record RefusalCase(string Id, string About, Dictionary<string, byte[]> Valid, Dictionary<string, byte[]> Invalid);

/// <summary>
/// #1499 (specVersion 31, area 02): the <c>refusal-packages</c> cases of the file model, one for
/// every sentence <see cref="SourceParser"/> can say (chapter 12 § 12.2), except S01-037 to S01-039,
/// whose packages (513 files, a 257 KiB file, a package over 2 MiB) the library's tests prove and
/// the suite does not ship (D17). Each side is <see cref="SourceFixture.Minimal"/> plus only the
/// construct its rule needs (a loop, a while, an if, a case, a function with its call, a snippet
/// folder, a delivery folder, a computed node, a format or a renderer: the <c>With…</c> helpers
/// below), so a later change to <see cref="SourceFixture.Full"/> rewrites no case. Each invalid side
/// is its valid side with one change (and, where the change empties a body, its children's files
/// removed so they are not strays), so it gives exactly one refusal; every edit is a text replacement
/// that must match exactly once, so a fixture change cannot quietly empty a case.
/// The suite's <c>expect.json</c> files are written from what <see cref="SourceCheck.Run"/> says
/// (<see cref="RefusalPackagesExport"/>), never by hand. #1500 (specVersion 31, area 03): every case
/// is held to the reader and the type rules together, and chapter 2's cases are
/// <c>RefusalPackages.Types.cs</c>'s. #1501 (specVersion 31, area 04): and to the expression rules, the third
/// step; chapter 3's cases are <c>RefusalPackages.Logic.cs</c>'s. #1502 (specVersion 31, area 05): and to the resolve
/// step before them; the resolver's cases are <c>RefusalPackages.Resolve.cs</c>'s. #1503 (specVersion 31, area 06): the
/// rules area 06 adds at publish are <c>RefusalPackages.Engine.cs</c>'s.
/// </summary>
internal static partial class RefusalPackages
{
    private static readonly JsonSerializerOptions ExpectOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>
    /// A case's <c>expect.json</c>, from what <see cref="SourceCheck.Run"/> said: <c>about</c>,
    /// <c>valid</c> and the sentences in its order; 2-space indented, <c>\n</c> line ends and a final newline.
    /// </summary>
    public static string Expect(string about, IReadOnlyList<SourceRefusal> refusals)
    {
        var sentences = new JsonArray();
        foreach (var refusal in refusals)
            sentences.Add(refusal.Sentence);
        var expect = new JsonObject
        {
            ["about"] = about,
            ["valid"] = refusals.Count == 0,
            ["sentences"] = sentences,
        };
        return expect.ToJsonString(ExpectOptions) + "\n";
    }

    /// <summary>Every case, in id order.</summary>
    public static IReadOnlyList<RefusalCase> All() =>
        Intake().Concat(Layout()).Concat(Headers()).Concat(Members()).Concat(Ids()).Concat(Kinds())
            .Concat(Loops()).Concat(Branches()).Concat(Functions()).Concat(Scopes()).Concat(Registry())
            .Concat(Types()).Concat(Logic()).Concat(Resolve()).Concat(Engine())
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

    // ---- Chapter 1 § 1.2.4 and § 1.7: intake ------------------------------------------------------

    private static IEnumerable<RefusalCase> Intake()
    {
        yield return new RefusalCase("S01-014",
            "A file starts with a byte-order mark (chapter 1 § 1.2.4). Invalid: manifest.json begins with the bytes EF BB BF. "
            + "Valid: the same file without them.",
            Minimal(),
            Minimal().WithBytes("manifest.json", [0xEF, 0xBB, 0xBF, .. Minimal()["manifest.json"]]));

        var crlf = Text(Minimal(), "nodes/plan.md").Replace("\n", "\r\n", StringComparison.Ordinal);
        yield return new RefusalCase("S01-015",
            "A carriage return that does not end a line (chapter 1 § 1.2.4). Invalid: nodes/plan.md has a lone \\r inside its prompt. "
            + "Valid: every line of nodes/plan.md ends with \\r\\n, which the reader reads as \\n.",
            Minimal().With("nodes/plan.md", crlf),
            Minimal().Edit("nodes/plan.md", "Plan the sections of", "Plan the sections\rof"));

        const string place = "Plan the sections of a consultation note for the draft below.";
        const string placed = "Plan the sections of a consultation note for the draft below, written at the clinic in Montréal.";
        yield return new RefusalCase("A02-002",
            "Bytes that are not UTF-8 (chapter 1 § 1.2.4; this implementation's sentence). Invalid: nodes/plan.md writes "
            + "the é of Montréal as the single Latin-1 byte E9. Valid: the same prompt in UTF-8.",
            Minimal().Edit("nodes/plan.md", place, placed),
            Minimal().Edit("nodes/plan.md", place, placed).Latin1("nodes/plan.md"));

        yield return new RefusalCase("S01-040",
            "A path longer than SourceBounds.pathLength, 200 characters from the package root (chapter 1 § 1.7). Two loops "
            + "with 64-character ids nest an agent node. Invalid: the node's path is 201 characters. Valid: it is 200.",
            LongPath(61),
            LongPath(62));

        yield return new RefusalCase("S01-041",
            "JSON nested deeper than SourceBounds.jsonDepth, 32 levels, the outermost object being level 1 (chapter 1 § 1.7). "
            + "A format nests arrays of arrays. Invalid: formats/nested_points.json opens an object at level 33. Valid: its deepest object is at level 32.",
            Nested(32),
            Nested(33));
    }

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> plus two nested loops whose ids are 64 characters, holding an
    /// agent node whose id is <paramref name="leaf"/> characters: its path is 139 + <paramref name="leaf"/>. The outer
    /// loop iterates the input <c>sections</c>, the inner each section's points (#1502: typed sources).
    /// </summary>
    private static Dictionary<string, byte[]> LongPath(int leaf)
    {
        var outer = Fit("sections_of_the_consultation_note_for_margaret_okafor_each_in_turn", 64);
        var inner = Fit("paragraphs_of_one_section_of_the_consultation_note_drafted_in_order", 64);
        var node = Fit("draft_one_paragraph_of_the_section_from_its_point_for_dr_a_sample", leaf);
        return WithSectionsInput(Minimal())
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", $"\"items\": [\"plan\", \"{outer}\"]")
            .With($"nodes/{outer}/index.json", Json(Node("for", "Sections", ArrayOf(ArrayOf(Plain)), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + $"\"max\": null, \"itemLabel\": null, \"nodes\": [\"{inner}\"], \"ret\": \"node.{outer}.{inner}\"")))
            .With($"nodes/{outer}/{inner}/index.json", Json(Node("for", "Paragraphs", ArrayOf(Plain), false, true,
                "\"in\": \"item.value.points\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + $"\"max\": null, \"itemLabel\": null, \"nodes\": [\"{node}\"], \"ret\": \"node.{outer}.{inner}.{node}\"")))
            .With($"nodes/{outer}/{inner}/{node}.md", Agent("Drafting a paragraph", Plain,
                "Write one paragraph of the note about this point: {{ item.value }}\n"));
    }

    /// <summary><see cref="SourceFixture.Minimal"/> plus a format of nested lists whose JSON is <paramref name="levels"/> objects deep.</summary>
    private static Dictionary<string, byte[]> Nested(int levels)
    {
        var json = "{ \"$ref\": \"plaintext\" }";
        for (var level = levels - 1; level > 1; level--)
            json = $"{{ \"type\": \"array\", \"items\": {json} }}";
        json = $"{{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"array\", \"items\": {json} }}";
        return Minimal()
            .Edit("formats/index.json", "\"items\": []", "\"items\": [\"nested_points\"]")
            .With("formats/nested_points.json", Json(json));
    }

    // ---- Chapter 1 § 1.2: the folder layout -------------------------------------------------------

    private static IEnumerable<RefusalCase> Layout()
    {
        yield return new RefusalCase("S01-006",
            "A file no index lists (chapter 1 § 1.2.2). Invalid: nodes/notes.txt sits in nodes/ and nodes/index.json does not list it. "
            + "Valid: the package without it.",
            Minimal(),
            Minimal().With("nodes/notes.txt", "Notes for Dr A. Sample: ask Margaret Okafor about her sleep.\n"));

        yield return new RefusalCase("S01-007",
            "A folder without its index.json (chapter 1 § 1.2.2). Invalid: the snippet folder snippets/standards holds its "
            + "item and no index.json. Valid: the folder with its index.json.",
            WithSnippetFolder(),
            WithSnippetFolder().Without("snippets/standards/index.json"));

        yield return new RefusalCase("S01-008",
            "A top-level folder that is not there (chapter 1 § 1.2). Invalid: the package has no macros/ folder. "
            + "Valid: macros/ holds an index.json listing nothing.",
            Minimal(),
            Minimal().Without("macros/index.json"));

        yield return new RefusalCase("S01-009",
            "An index lists an id with nothing on disk (chapter 1 § 1.2.2). Invalid: nodes/index.json lists 'summary', and "
            + "nodes/ has no summary.md or summary/. Valid: the list names only 'plan'.",
            Minimal(),
            Minimal().Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"summary\"]"));

        yield return new RefusalCase("S01-010",
            "An index lists an id twice (chapter 1 § 1.2.2). Invalid: nodes/index.json lists 'plan' twice. Valid: once.",
            Minimal(),
            Minimal().Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"plan\"]"));

        yield return new RefusalCase("S01-011",
            "An index lists a folder with a trailing / (chapter 1 § 1.2.2). Invalid: nodes/index.json lists 'sections/'. "
            + "Valid: it lists the folder by its bare id, 'sections'.",
            WithLoop(),
            WithLoop().Edit("nodes/index.json", "\"sections\"]", "\"sections/\"]"));

        yield return new RefusalCase("S01-012",
            "One id names both a file and a folder (chapter 1 § 1.2.2). Invalid: nodes/ holds refine.md beside the folder "
            + "refine/. Valid: only the folder.",
            WithWhile(),
            WithWhile().With("nodes/refine.md", Agent("Refining the summary", Plain, "Refine the summary once.\n")));

        yield return new RefusalCase("S01-013",
            "A delivery folder inside a delivery folder (chapter 1 § 1.2). Invalid: deliverables/emr/archive/ is a folder with "
            + "its own index.json and deliverable. Valid: the delivery folder deliverables/emr/ alone.",
            WithDeliveryFolder(),
            WithDeliveryFolder()
                .With("deliverables/emr/archive/index.json", SourceFixture.Index(["old_consult"], extra: ", \"delivery\": [\"emr\"], \"bundle\": null"))
                .With("deliverables/emr/archive/old_consult.md", Md(
                    SourceFixture.Deliverable("Earlier consult letter", Plain, "null", "release"),
                    "Re: Margaret Okafor\n\n{{ node.plan.content }}\n")));
    }

    // ---- Chapter 1 § 1.4: front matter, and plain JSON files ----------------------------------------

    private static IEnumerable<RefusalCase> Headers()
    {
        yield return new RefusalCase("S01-016",
            "A .md file that is not a line of ---, one JSON object, a line of --- (chapter 1 § 1.4). Invalid: nodes/plan.md "
            + "holds its prompt and no header. Valid: the header between its fences.",
            Minimal(),
            Minimal().With("nodes/plan.md", Text(Minimal(), "nodes/plan.md").Split("\n---\n", 2)[1]));

        yield return new RefusalCase("S01-017",
            "A header that is not JSON (chapter 1 § 1.4). Invalid: nodes/plan.md's header ends with a comma before its closing "
            + "brace. Valid: without the comma. The sentence's parser message is this implementation's: the line and column "
            + "of the file where the JSON stops being valid.",
            Minimal(),
            Minimal().Edit("nodes/plan.md", "\"ordered\": false }", "\"ordered\": false, }"));

        yield return new RefusalCase("S01-018",
            "A member written twice in one header (chapter 1 § 1.4). Invalid: nodes/plan.md's header writes 'label' twice. "
            + "Valid: once.",
            Minimal(),
            Minimal().Edit("nodes/plan.md", "\"label\": \"Planning the sections\", ",
                "\"label\": \"Planning the sections\", \"label\": \"Planning\", "));

        yield return new RefusalCase("S01-019",
            "A body that is required and empty: an agent node's prompt (chapter 1 § 1.4). Invalid: nodes/plan.md ends at its "
            + "closing fence. Valid: the prompt follows the fence.",
            Minimal(),
            Minimal().Edit("nodes/plan.md",
                "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n", ""));

        yield return new RefusalCase("S01-020",
            "A body where none may be: a computed node keeps everything in its header (chapter 1 § 1.4). Invalid: "
            + "nodes/section_count.md has a line after its closing fence. Valid: nothing after the fence.",
            WithComputed(),
            WithComputed().With("nodes/section_count.md", Text(WithComputed(), "nodes/section_count.md") + "Measure the plan.\n"));

        yield return new RefusalCase("A02-005",
            "A plain JSON file whose root is not an object (chapter 1 § 1.4; this implementation's sentence). Invalid: "
            + "formats/verdict.json holds a JSON list. Valid: the format's object.",
            WithFormat("verdict"),
            WithFormat("verdict").With("formats/verdict.json", "[\"good\", \"revise\"]\n"));
    }

    // ---- Chapter 1 § 1.5: every member written ------------------------------------------------------

    private static IEnumerable<RefusalCase> Members()
    {
        const string plan = "nodes/plan.md";

        yield return new RefusalCase("S01-021",
            "A member left out (chapter 1 § 1.5). Invalid: nodes/plan.md's header does not write 'description'. "
            + "Valid: it writes \"description\": null.",
            Minimal(),
            Minimal().Edit(plan, "\"description\": null, ", ""));

        yield return new RefusalCase("S01-022",
            "null in a member that can never be null (chapter 1 § 1.5). Invalid: the agent node plan declares \"label\": null. "
            + "Valid: it names its label.",
            Minimal(),
            Minimal().Edit(plan, "\"label\": \"Planning the sections\"", "\"label\": null"));

        yield return new RefusalCase("S01-023",
            "A member the declaration does not have (chapter 1 § 1.5). Invalid: nodes/plan.md's header also writes 'lable', "
            + "a misspelt label. Valid: without it.",
            Minimal(),
            Minimal().Edit(plan, "\"label\": \"Planning the sections\", ",
                "\"label\": \"Planning the sections\", \"lable\": \"Planning the sections\", "));

        yield return new RefusalCase("S01-024",
            "An empty description (chapter 1 § 1.5.1). Invalid: the agent node plan declares \"description\": []. "
            + "Valid: \"description\": null, which is how none is written.",
            Minimal(),
            Minimal().Edit(plan, "\"description\": null", "\"description\": []"));

        yield return new RefusalCase("S01-025",
            "A description of more than 500 code points, its lines joined with \\n (chapter 1 § 1.5.1). Invalid: the agent node "
            + "plan's description is 501. Valid: it is 500, the limit.",
            Minimal().Edit(plan, "\"description\": null", $"\"description\": [\"{Description(500)}\"]"),
            Minimal().Edit(plan, "\"description\": null", $"\"description\": [\"{Description(501)}\"]"));

        yield return new RefusalCase("S01-026",
            "An empty label (chapter 1 § 1.5.1). Invalid: the agent node plan's label is three spaces. Valid: it names the node's work.",
            Minimal(),
            Minimal().Edit(plan, "\"label\": \"Planning the sections\"", "\"label\": \"   \""));

        yield return new RefusalCase("A02-001",
            "A member of the wrong JSON type (chapter 1 § 1.5; this implementation's sentence). Invalid: the agent node plan "
            + "declares \"ordered\": \"no\". Valid: \"ordered\": false.",
            Minimal(),
            Minimal().Edit(plan, "\"ordered\": false", "\"ordered\": \"no\""));
    }

    /// <summary>A description line of exactly <paramref name="length"/> code points, fictional.</summary>
    private static string Description(int length)
    {
        var text = new StringBuilder();
        while (text.Length < length)
            text.Append("Plans the sections of Margaret Okafor's consultation note for Dr A. Sample, one for each heading. ");
        return text.ToString(0, length);
    }

    // ---- Chapter 1 § 1.6 and § 1.3: ids and the manifest --------------------------------------------

    private static IEnumerable<RefusalCase> Ids()
    {
        yield return new RefusalCase("S01-030",
            "An id that is not a lowercase letter, then lowercase letters, digits or underscores (chapter 1 § 1.6). Invalid: "
            + "the manifest declares the input 'Referral_letter'. Valid: 'referral_letter'.",
            WithInput("referral_letter"),
            WithInput("Referral_letter"));

        yield return new RefusalCase("S01-031",
            "An id longer than SourceBounds.idLength, 64 characters (chapter 1 § 1.6). Invalid: the manifest declares an input "
            + "whose id is 65 characters. Valid: 64, the limit.",
            WithInput(Fit("referral_letter_from_the_family_doctor_with_the_recent_blood_results", 64)),
            WithInput(Fit("referral_letter_from_the_family_doctor_with_the_recent_blood_results", 65)));

        yield return new RefusalCase("S01-032",
            "An id that Windows reserves for a device and that cannot name a file (chapter 1 § 1.6). Invalid: the manifest "
            + "declares the input 'con'. Valid: 'cons', which Windows does not reserve.",
            WithInput("cons"),
            WithInput("con"));

        yield return new RefusalCase("S01-033",
            "An id that is a word references read (chapter 1 § 1.6). Invalid: the manifest declares the input 'content', a "
            + "member read in references. Valid: 'content_note'.",
            WithInput("content_note"),
            WithInput("content"));

        yield return new RefusalCase("A02-003",
            "A package without manifest.json (chapter 1 § 1.3; this implementation's sentence). Invalid: the package has every "
            + "folder and no manifest.json. Valid: the manifest at the root.",
            Minimal(),
            Minimal().Without("manifest.json"));

        yield return new RefusalCase("A02-004",
            "A manifest whose specVersion is not 31 (chapter 1 § 1.3; this implementation's sentence). Invalid: the manifest "
            + "declares \"specVersion\": 30. Valid: 31.",
            Minimal(),
            Minimal().Edit("manifest.json", "\"specVersion\": 31", "\"specVersion\": 30"));
    }

    /// <summary><see cref="SourceFixture.Minimal"/> with a second, optional input whose id is <paramref name="id"/>.</summary>
    private static Dictionary<string, byte[]> WithInput(string id) =>
        Minimal().Edit("manifest.json", "\"channel\": null }\n  }",
            "\"channel\": null },\n"
            + $"    \"{id}\": {{ \"label\": \"Referral letter\", \"description\": null, \"format\": \"markdown_block\", "
            + "\"required\": false, \"channel\": null }\n  }");

    // ---- Kinds and producers (chapter 1 § 1.5.2, chapter 2's kinds, chapter 4 § 4.1) ----------------

    private static IEnumerable<RefusalCase> Kinds()
    {
        const string plan = "nodes/plan.md";

        yield return new RefusalCase("S01-028",
            "A producer the node's kind does not have (chapter 1 § 1.5.2). Invalid: the agent node plan declares "
            + "\"producer\": \"computed\". Valid: \"generated\", an agent node's producer.",
            Minimal(),
            Minimal().Edit(plan, "\"producer\": \"generated\"", "\"producer\": \"computed\""));

        yield return new RefusalCase("S02-007",
            "A kind that is none of the eight (chapter 2). Invalid: nodes/plan.md declares \"kind\": \"chat\". Valid: \"agent\".",
            Minimal(),
            Minimal().Edit(plan, "\"kind\": \"agent\"", "\"kind\": \"chat\""));

        yield return new RefusalCase("A02-006",
            "A flow kind declared by a .md file, where a flow node is a folder with an index.json (chapter 4 § 4.1; this "
            + "implementation's sentence). Invalid: nodes/plan.md declares \"kind\": \"for\". Valid: \"agent\".",
            Minimal(),
            Minimal().Edit(plan, "\"kind\": \"agent\"", "\"kind\": \"for\""));

        yield return new RefusalCase("A02-007",
            "A leaf kind declared by a folder's index.json, where a leaf node is a file (chapter 4 § 4.1; this implementation's "
            + "sentence). Invalid: nodes/refine/index.json declares \"kind\": \"agent\". Valid: \"while\".",
            WithWhile(),
            WithWhile().Edit("nodes/refine/index.json", "\"kind\": \"while\"", "\"kind\": \"agent\""));
    }

    // ---- Chapter 4 §§ 4.2–4.3: for and while ------------------------------------------------------------

    private static IEnumerable<RefusalCase> Loops()
    {
        const string refine = "nodes/refine/index.json";
        const string sections = "nodes/sections/index.json";
        const string parallelFour = "\"parallel\": true, \"maxParallel\": 4";

        yield return new RefusalCase("S04-009",
            "A flow node's label is null (chapter 4 § 4.10.1, in place of chapter 1's sentence for null). Invalid: the while "
            + "refine declares \"label\": null. Valid: it names its label.",
            WithWhile(),
            WithWhile().Edit(refine, "\"label\": \"Refining the summary\"", "\"label\": null"));

        yield return new RefusalCase("S04-010",
            "A member that belongs to another flow kind (chapter 4 § 4.10.1). Invalid: the while refine declares "
            + "\"parallel\": false, a for's member. Valid: without it.",
            WithWhile(),
            WithWhile().Edit(refine, "\"checkFirst\": false, ", "\"checkFirst\": false, \"parallel\": false, "));

        yield return new RefusalCase("S04-011",
            "A body whose ret is null (chapter 4 § 4.7.6). Invalid: the while refine declares \"ret\": null. "
            + "Valid: it returns its child node.refine.draft_summary.",
            WithWhile(),
            WithWhile().Edit(refine, "\"ret\": \"node.refine.draft_summary\"", "\"ret\": null"));

        yield return new RefusalCase("S04-012",
            "A ret that is not one of the body's own children by its full name (chapter 4 § 4.7.6). Invalid: the while refine "
            + "returns node.draft_summary. Valid: node.refine.draft_summary.",
            WithWhile(),
            WithWhile().Edit(refine, "\"ret\": \"node.refine.draft_summary\"", "\"ret\": \"node.draft_summary\""));

        yield return new RefusalCase("S04-013",
            "A body that lists one node twice (chapter 4 § 4.10.1, in place of chapter 1's sentence for a list). Invalid: "
            + "the while refine's nodes lists draft_summary twice. Valid: once.",
            WithWhile(),
            WithWhile().Edit(refine, "\"nodes\": [\"draft_summary\", \"judge\"]", "\"nodes\": [\"draft_summary\", \"judge\", \"draft_summary\"]"));

        yield return new RefusalCase("S04-018",
            "A for with no body (chapter 4 § 4.2.1). Invalid: the for sections declares \"nodes\": [] and its folder holds no "
            + "children. Valid: its child outline, listed.",
            WithLoop(),
            WithLoop().Edit(sections, "\"nodes\": [\"outline\"]", "\"nodes\": []").Without("nodes/sections/outline.md"));

        yield return new RefusalCase("S04-019",
            "A parallel for capped at one item at a time (chapter 4 § 4.2.3). Invalid: the for sections declares "
            + "\"parallel\": true with \"maxParallel\": 1. Valid: what the sentence asks, \"parallel\": false and \"maxParallel\": null.",
            WithLoop().Edit(sections, parallelFour, "\"parallel\": false, \"maxParallel\": null"),
            WithLoop().Edit(sections, parallelFour, "\"parallel\": true, \"maxParallel\": 1"));

        yield return new RefusalCase("S01-005",
            "A maxParallel above FormatBounds.parallel, 15 (chapter 1 § 1.8, chapter 4 § 4.2.3). Invalid: the for sections "
            + "declares \"maxParallel\": 16. Valid: 15, the limit.",
            WithLoop().Edit(sections, parallelFour, "\"parallel\": true, \"maxParallel\": 15"),
            WithLoop().Edit(sections, parallelFour, "\"parallel\": true, \"maxParallel\": 16"));

        yield return new RefusalCase("S04-021",
            "A maxParallel below 2 that is not one with parallel true (chapter 4 § 4.2.3). Invalid: the for sections declares "
            + "\"maxParallel\": 0. Valid: 2, the least.",
            WithLoop().Edit(sections, parallelFour, "\"parallel\": true, \"maxParallel\": 2"),
            WithLoop().Edit(sections, parallelFour, "\"parallel\": true, \"maxParallel\": 0"));

        yield return new RefusalCase("S04-022",
            "A maxParallel on a sequential for (chapter 4 § 4.2.3). Invalid: the for sections declares \"parallel\": false "
            + "with \"maxParallel\": 4. Valid: \"parallel\": false with \"maxParallel\": null.",
            WithLoop().Edit(sections, parallelFour, "\"parallel\": false, \"maxParallel\": null"),
            WithLoop().Edit(sections, parallelFour, "\"parallel\": false, \"maxParallel\": 4"));

        // #1502: the until reads the section's title, a string the input types (it read a kind the source has no longer).
        const string until = "\"until\": { \"===\": [{ \"var\": \"item.value.title\" }, \"Plan\"] }";
        yield return new RefusalCase("S04-023",
            "An until on a parallel for (chapter 4 § 4.2.4). Invalid: the for sections declares an until with \"parallel\": true. "
            + "Valid: the same until with \"parallel\": false and \"maxParallel\": null.",
            WithLoop().Edit(sections, parallelFour + ", \"until\": null", $"\"parallel\": false, \"maxParallel\": null, {until}"),
            WithLoop().Edit(sections, parallelFour + ", \"until\": null", $"\"parallel\": true, \"maxParallel\": null, {until}"));

        yield return new RefusalCase("S04-024",
            "A for's max below 1 (chapter 4 § 4.2.4). Invalid: the for sections declares \"max\": 0. Valid: 1, the least.",
            WithLoop().Edit(sections, "\"max\": null", "\"max\": 1"),
            WithLoop().Edit(sections, "\"max\": null", "\"max\": 0"));

        yield return new RefusalCase("S04-031",
            "A while's max is null (chapter 4 § 4.3.1, in place of chapter 1's sentence for null). Invalid: the while refine "
            + "declares \"max\": null. Valid: \"max\": 3.",
            WithWhile(),
            WithWhile().Edit(refine, "\"max\": 3", "\"max\": null"));

        yield return new RefusalCase("S01-003",
            "A while's max above FormatBounds.passes, 15 (chapter 1 § 1.8, chapter 4 § 4.3.1). Invalid: the while refine "
            + "declares \"max\": 16. Valid: 15, the limit.",
            WithWhile().Edit(refine, "\"max\": 3", "\"max\": 15"),
            WithWhile().Edit(refine, "\"max\": 3", "\"max\": 16"));

        yield return new RefusalCase("S04-033",
            "A while's max below 1 (chapter 4 § 4.3.1). Invalid: the while refine declares \"max\": 0. Valid: 1, the least.",
            WithWhile().Edit(refine, "\"max\": 3", "\"max\": 1"),
            WithWhile().Edit(refine, "\"max\": 3", "\"max\": 0"));

        yield return new RefusalCase("S04-034",
            "A while with no body (chapter 4 § 4.3.1). Invalid: the while refine declares \"nodes\": [] and its folder holds "
            + "no children. Valid: its two children, listed.",
            WithWhile(),
            WithWhile().Edit(refine, "\"nodes\": [\"draft_summary\", \"judge\"]", "\"nodes\": []")
                .Without("nodes/refine/draft_summary.md")
                .Without("nodes/refine/judge.md"));
    }

    // ---- Chapter 4 § 4.5: if and case -------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Branches()
    {
        const string gate = "nodes/billing_gate/index.json";
        const string route = "nodes/route/index.json";
        const string omit = "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }";

        yield return new RefusalCase("S04-041",
            "An if whose then is null (chapter 4 § 4.5.1). Invalid: the if billing_gate declares \"then\": null. "
            + "Valid: its then body runs billing_text.",
            WithIf(),
            WithIf().Edit(gate, "\"then\": { \"nodes\": [\"billing_text\"], \"ret\": \"node.billing_gate.billing_text\" }", "\"then\": null"));

        yield return new RefusalCase("A02-008",
            "An if's then with no body (chapter 4 § 4.5.1; this implementation's sentence, since chapter 4's names loops and "
            + "whiles only). Invalid: the if billing_gate's then declares \"nodes\": [] and its folder holds no children. "
            + "Valid: then runs billing_text.",
            WithIf(),
            WithIf().Edit(gate, "\"nodes\": [\"billing_text\"]", "\"nodes\": []").Without("nodes/billing_gate/billing_text.md"));

        yield return new RefusalCase("S04-042",
            "A case with no branches (chapter 4 § 4.5.2). Invalid: the case route declares \"branches\": [] and its folder "
            + "holds no children. Valid: its two branches.",
            WithCase(),
            WithCase().Edit(route,
                    "\"branches\": [{ \"when\": [\"narrative\"], \"nodes\": [\"narrative_draft\"], \"ret\": \"node.route.narrative_draft\" }, "
                    + omit + "]",
                    "\"branches\": []")
                .Without("nodes/route/narrative_draft.md"));

        yield return new RefusalCase("S04-043",
            "A case branch whose when names no value (chapter 4 § 4.5.2). Invalid: the case route's second branch declares "
            + "\"when\": []. Valid: \"when\": [\"omit\"].",
            WithCase(),
            WithCase().Edit(route, omit, "{ \"when\": [], \"nodes\": [], \"ret\": null }"));

        yield return new RefusalCase("S04-048",
            "A case branch half empty: nodes [] with a ret, or nodes with a null ret (chapter 4 § 4.5.2). Invalid: the case "
            + "route's second branch declares \"nodes\": [] with a ret. Valid: the empty branch, nodes [] and ret null together.",
            WithCase(),
            WithCase().Edit(route, omit, "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": \"node.route.narrative_draft\" }"));

        yield return new RefusalCase("S04-076",
            "A case branch whose when holds null (chapter 4 § 4.5.2). Invalid: the case route's second branch declares "
            + "\"when\": [\"omit\", null]. Valid: \"when\": [\"omit\"].",
            WithCase(),
            WithCase().Edit(route, omit, "{ \"when\": [\"omit\", null], \"nodes\": [], \"ret\": null }"));
    }

    // ---- Chapter 4 § 4.7: functions ---------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Functions()
    {
        yield return new RefusalCase("S04-062",
            "A function folder that holds a checks/ folder (chapter 4 § 4.7.5). Invalid: functions/draft_section/checks/ holds "
            + "an index.json. Valid: the function without it.",
            WithFunction(),
            WithFunction().With("functions/draft_section/checks/index.json", SourceFixture.Index([])));

        yield return new RefusalCase("S04-067",
            "A function parameter that declares a package input's member (chapter 4 § 4.7.2.1). Invalid: the parameter draft "
            + "of draft_section declares \"required\": true. Valid: label, description and format only.",
            WithFunction(),
            WithFunction().Edit("functions/draft_section/index.json",
                "\"format\": \"markdown_block\" }", "\"format\": \"markdown_block\", \"required\": true }"));
    }

    // ---- Chapter 1 §§ 1.7 and 1.9: scopes and the expression bound --------------------------------------

    private static IEnumerable<RefusalCase> Scopes()
    {
        yield return new RefusalCase("S01-002",
            "A node whose id is the id of a node it can see (chapter 1 § 1.9.2). Invalid: the for sections holds a node plan, "
            + "and the top level has one. Valid: the inner node is outline.",
            WithLoop(),
            WithLoop()
                .Edit("nodes/sections/index.json", "\"nodes\": [\"outline\"], \"ret\": \"node.sections.outline\"",
                    "\"nodes\": [\"plan\"], \"ret\": \"node.sections.plan\"")
                .Rename("nodes/sections/outline.md", "nodes/sections/plan.md"));

        yield return new RefusalCase("S01-036",
            "Two nodes of one id inside one while, where neither sees the other (chapter 1 § 1.9.2): pass.….node.<id> must name "
            + "one node. Invalid: the while refine holds two ifs, brief and detailed, each with a node wording. Valid: the "
            + "second is detailed_wording.",
            TwoWordings("detailed_wording"),
            TwoWordings("wording"));

        var limit = Expression(997);
        var over = Expression(998);
        yield return new RefusalCase("S01-042",
            "An expression of more than SourceBounds.expressionNodes, 1,000 nodes (chapter 1 § 1.7): one for each operation and "
            + "each literal, a var with its argument counting one. Invalid: the deliverable note's if has 1,001. Valid: 1,000, the limit.",
            Minimal().Edit("deliverables/note.md", "\"if\": null", $"\"if\": {limit}"),
            Minimal().Edit("deliverables/note.md", "\"if\": null", $"\"if\": {over}"));
    }

    /// <summary><c>{ "&gt;": [{ "+": [1, …] }, 0] }</c> with <paramref name="ones"/> ones: <paramref name="ones"/> + 3 nodes.</summary>
    private static string Expression(int ones) =>
        "{ \">\": [{ \"+\": [" + string.Join(", ", Enumerable.Repeat("1", ones)) + "] }, 0] }";

    /// <summary>
    /// <see cref="WithWhile"/> whose while <c>refine</c> also holds two ifs, <c>brief</c> and <c>detailed</c>,
    /// each running one agent node: <c>wording</c>, and <paramref name="second"/>.
    /// </summary>
    private static Dictionary<string, byte[]> TwoWordings(string second)
    {
        const string blank = "{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }";
        static string If(string label, bool brief, string child) => Json(Node("if", label, Plain, false, true,
            $"\"if\": {(brief ? blank : $"{{ \"!\": [{blank}] }}")}, "
            + $"\"then\": {{ \"nodes\": [\"{child}\"], \"ret\": \"node.refine.{(brief ? "brief" : "detailed")}.{child}\" }}, \"else\": null"));

        return WithWhile()
            .Edit("nodes/refine/index.json", "\"nodes\": [\"draft_summary\", \"judge\"]",
                "\"nodes\": [\"draft_summary\", \"judge\", \"brief\", \"detailed\"]")
            .With("nodes/refine/brief/index.json", If("Wording the summary briefly", true, "wording"))
            .With("nodes/refine/brief/wording.md", Agent("Wording the summary briefly", Plain,
                "Shorten the summary to one sentence for Dr A. Sample.\n"))
            .With("nodes/refine/detailed/index.json", If("Wording the summary in full", false, second))
            .With($"nodes/refine/detailed/{second}.md", Agent("Wording the summary in full", Plain,
                "Keep the summary's two sentences and add the plan's headings.\n"));
    }

    // ---- The registry's names (chapter 1 §§ 1.2 and 1.9.1, chapter 2) -------------------------------------

    private static IEnumerable<RefusalCase> Registry()
    {
        yield return new RefusalCase("S01-034",
            "A package format named like a built-in format (chapter 2). Invalid: the package's own format of verdicts is named "
            + "judgement, a built-in's name. Valid: it is named verdict.",
            WithFormat("verdict"),
            WithFormat("judgement"));

        yield return new RefusalCase("S01-035",
            "A package renderer named like a built-in renderer (chapter 1 § 1.2). Invalid: the package's renderer of terms as "
            + "bullets is named concept_bullets, a built-in's name. Valid: it is named term_bullets.",
            WithRenderer("term_bullets"),
            WithRenderer("concept_bullets"));
    }

    // ---- The constructs a case adds to Minimal() ------------------------------------------------------------

    /// <summary><see cref="SourceFixture.Minimal"/> with <paramref name="id"/> listed after plan in <c>nodes/index.json</c>.</summary>
    private static Dictionary<string, byte[]> MinimalListing(string id) =>
        Minimal().Edit("nodes/index.json", "\"items\": [\"plan\"]", $"\"items\": [\"plan\", \"{id}\"]");

    /// <summary>
    /// A parallel <c>for</c>, <c>sections</c>, capped at 4, running one agent node, <c>outline</c>, per section of the input
    /// <c>sections</c> (#1502: a typed list, now that a loop's source is typed; the plan of <see cref="SourceFixture.Minimal"/>
    /// is plaintext, with no sections to iterate).
    /// </summary>
    private static Dictionary<string, byte[]> WithLoop() =>
        WithSectionsInput(MinimalListing("sections"))
            .With("nodes/sections/index.json", Json(Node("for", "Sections", ArrayOf(Plain), false, true,
                "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"outline\"], \"ret\": \"node.sections.outline\"")))
            .With("nodes/sections/outline.md", Agent("Outlining the section", Plain,
                "Outline the section '{{ item.value.title }}' of the note as a short list of points.\n"));

    /// <summary>
    /// <paramref name="files"/> with the required input <c>sections</c>: a list of sections, each a <c>title</c> and its
    /// <c>points</c> (a list of plaintext).
    /// </summary>
    private static Dictionary<string, byte[]> WithSectionsInput(Dictionary<string, byte[]> files) =>
        WithRequiredInput(files, "sections", "Sections",
            "{ \"type\": \"array\", \"items\": { \"type\": \"object\", \"required\": [\"title\", \"points\"], "
            + "\"additionalProperties\": false, \"properties\": { \"title\": { \"type\": \"string\", \"title\": \"Title\" }, "
            + "\"points\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" }, \"title\": \"Points\" } } } }");

    /// <summary>
    /// A <c>while</c>, <c>refine</c>, of at most 3 passes, drafting a summary and judging it: it runs again while the
    /// judge still has something to say (#1502: a text read is tested by blank, not compared, now that node reads are typed).
    /// </summary>
    private static Dictionary<string, byte[]> WithWhile() =>
        MinimalListing("refine")
            .With("nodes/refine/index.json", Json(Node("while", "Refining the summary", Plain, false, true,
                "\"while\": { \"!\": [{ \"blank\": [{ \"var\": \"node.judge.content\" }] }] }, \"max\": 3, "
                + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"draft_summary\", \"judge\"], "
                + "\"ret\": \"node.refine.draft_summary\"")))
            .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample.\n"))
            .With("nodes/refine/judge.md", Agent("Judging the summary", Plain,
                "Say what the summary below still needs; write nothing when it is good.\n\n{{ node.draft_summary.content }}\n"));

    /// <summary>An <c>if</c>, <c>billing_gate</c>, whose then body renders one line of billing text.</summary>
    private static Dictionary<string, byte[]> WithIf() =>
        MinimalListing("billing_gate")
            .With("nodes/billing_gate/index.json", Json(Node("if", "Adding the billing text to a written draft", Plain, true, false,
                "\"if\": { \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }, "
                + "\"then\": { \"nodes\": [\"billing_text\"], \"ret\": \"node.billing_gate.billing_text\" }, "
                + "\"else\": null")))
            .With("nodes/billing_gate/billing_text.md", Md(
                Node("rendered", "Billing text", Plain, true, false, "\"if\": null"),
                "Consultation with Margaret Okafor, 45 minutes.\n"));

    /// <summary>
    /// A <c>case</c>, <c>route</c>, on the required input <c>note_kind</c> (narrative or omit), with a branch that drafts
    /// and an empty branch (#1502: a case's subject is typed, an enumerated string; the plan of
    /// <see cref="SourceFixture.Minimal"/> is plaintext, with no kind to switch on).
    /// </summary>
    private static Dictionary<string, byte[]> WithCase() =>
        WithRequiredInput(MinimalListing("route"), "note_kind", "Kind of note", NoteKinds)
            .With("nodes/route/index.json", Json(Node("case", "Routing the note by its kind", Plain, false, true,
                "\"case\": \"input.note_kind.value\", \"branches\": ["
                + "{ \"when\": [\"narrative\"], \"nodes\": [\"narrative_draft\"], \"ret\": \"node.route.narrative_draft\" }, "
                + "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }], \"else\": null")))
            .With("nodes/route/narrative_draft.md", Agent("Drafting the note as prose", Plain,
                "Write the note for Margaret Okafor as prose.\n"));

    /// <summary>The format of <see cref="WithCase"/>'s input <c>note_kind</c>.</summary>
    private const string NoteKinds = "{ \"type\": \"string\", \"enum\": [\"narrative\", \"omit\"] }";

    /// <summary>A function, <c>draft_section</c>, of one parameter and one agent node, and a <c>call</c> node, <c>section</c>, that calls it.</summary>
    private static Dictionary<string, byte[]> WithFunction()
    {
        var files = MinimalListing("section")
            .With("nodes/section.md", Md(
                Node("call", "Drafting the section", Plain, false, true,
                    "\"if\": null, \"function\": \"draft_section\", \"with\": { \"draft\": \"input.consult_draft.content\" }"),
                ""))
            .Edit("functions/index.json", "\"items\": []", "\"items\": [\"draft_section\"]")
            .With("functions/draft_section/index.json", Json(
                "{ \"label\": \"Draft a section\", \"description\": null, "
                + "\"inputs\": { \"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"patient_draft\"], \"ret\": \"node.patient_draft\", \"format\": \"plaintext\" }"))
            .With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
                "Using the consult draft below, write what it says about Margaret Okafor.\n\n{{ param.draft.content }}\n"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/draft_section/{folder}/index.json", SourceFixture.Index([]));
        return files;
    }

    /// <summary>A snippet folder, <c>snippets/standards</c>, of one item.</summary>
    private static Dictionary<string, byte[]> WithSnippetFolder() =>
        Minimal()
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"standards\"]")
            .With("snippets/standards/index.json", SourceFixture.Index(["hpi"],
                description: "The standard sections of a consult note, in order.", extra: ", \"format\": \"plaintext\""))
            .With("snippets/standards/hpi.md", Md(
                "{ \"title\": \"History of Present Illness\" }",
                "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n"));

    /// <summary>A delivery folder, <c>deliverables/emr</c>, of one deliverable.</summary>
    private static Dictionary<string, byte[]> WithDeliveryFolder() =>
        Minimal()
            .Edit("deliverables/index.json", "\"items\": [\"note\"]", "\"items\": [\"note\", \"emr\"]")
            .With("deliverables/emr/index.json", SourceFixture.Index(["consult"], extra: ", \"delivery\": [\"emr\"], \"bundle\": null"))
            .With("deliverables/emr/consult.md", Md(
                SourceFixture.Deliverable("Consult letter", Plain, "null", "release"),
                "Re: Margaret Okafor\n\n{{ node.plan.content }}\n\nDr A. Sample\n"));

    /// <summary>
    /// A computed node, <c>section_count</c>, measuring the plan, a <c>plaintext</c> (#1502: its size, now that node reads
    /// are typed; the plan of <see cref="SourceFixture.Minimal"/> has no sections to count).
    /// </summary>
    private static Dictionary<string, byte[]> WithComputed() =>
        MinimalListing("section_count")
            .With("nodes/section_count.md", Md(
                Node("computed", "Measuring the plan", "\"number\"", true, true,
                    "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.plan.content\" }] }"),
                ""));

    /// <summary>A package format of verdicts named <paramref name="id"/>.</summary>
    private static Dictionary<string, byte[]> WithFormat(string id) =>
        Minimal()
            .Edit("formats/index.json", "\"items\": []", $"\"items\": [\"{id}\"]")
            .With($"formats/{id}.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"verdict\"], "
                + "\"additionalProperties\": false, \"properties\": { "
                + "\"verdict\": { \"type\": \"string\", \"enum\": [\"good\", \"revise\"] } } }"));

    /// <summary>A package renderer of terms as bullets named <paramref name="id"/>.</summary>
    private static Dictionary<string, byte[]> WithRenderer(string id) =>
        Minimal()
            .Edit("renderers/index.json", "\"items\": []", $"\"items\": [\"{id}\"]")
            .With($"renderers/{id}.md", Md(
                "{ \"label\": \"Terms as bullets\", \"kind\": \"rendered\", "
                + "\"description\": [\"One bullet for each term.\"], \"input\": \"concept_list\", \"format\": \"markdown_block\" }",
                "{{~ for c in value.concepts ~}}\n- {{ c.term }}\n{{~ end ~}}\n"));

    // ---- Edits ------------------------------------------------------------------------------------------

    private const string Plain = "\"plaintext\"";

    /// <summary>Replaces <paramref name="old"/>, which must occur exactly once in the file, with <paramref name="replacement"/>.</summary>
    private static Dictionary<string, byte[]> Edit(this Dictionary<string, byte[]> files, string path, string old, string replacement)
    {
        var text = Text(files, path);
        var at = text.IndexOf(old, StringComparison.Ordinal);
        if (at < 0 || text.IndexOf(old, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"{path}: '{old}' does not occur exactly once.");
        return files.With(path, text[..at] + replacement + text[(at + old.Length)..]);
    }

    private static Dictionary<string, byte[]> WithBytes(this Dictionary<string, byte[]> files, string path, byte[] bytes)
    {
        files[path] = bytes;
        return files;
    }

    /// <summary>The file at <paramref name="path"/> written as Latin-1, so a letter outside ASCII is one byte that is not UTF-8.</summary>
    private static Dictionary<string, byte[]> Latin1(this Dictionary<string, byte[]> files, string path) =>
        files.WithBytes(path, Encoding.Latin1.GetBytes(Text(files, path)));

    private static Dictionary<string, byte[]> Rename(this Dictionary<string, byte[]> files, string from, string to)
    {
        var bytes = files[from];
        files.Remove(from);
        return files.WithBytes(to, bytes);
    }

    /// <summary><paramref name="text"/> cut to <paramref name="length"/> characters; it must be at least that long.</summary>
    private static string Fit(string text, int length) =>
        text.Length >= length ? text[..length] : throw new InvalidOperationException($"'{text}' is shorter than {length}.");
}
