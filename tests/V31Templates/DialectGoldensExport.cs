using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Logic;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 10 § 5): the three families of the dialect, generated from the code, and their
/// export into the content repository named by <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites
/// <c>v31/conformance/dialect-goldens/</c>, <c>renderer-goldens/</c> and <c>markdown-escape-vectors/</c> with
/// <see cref="Files"/>, then sets each family's <c>cases</c> in <c>v31/conformance/index.json</c> to <see cref="Cases"/>,
/// leaving every other byte of that file as it was. A no-op without the variable, as <see cref="RefusalPackagesExport"/>
/// is: it is a generator, not a check. <see cref="DialectGoldenTests"/> holds the in-repo goldens and the pin to it.
/// </summary>
[Collection("V31 conformance export")] // #1505: the exporters each rewrite v31/conformance/index.json, so they run one at a time.
public class DialectGoldensExport
{
    public const string DialectGoldens = "dialect-goldens";
    public const string RendererGoldens = "renderer-goldens";
    public const string EscapeVectors = "markdown-escape-vectors";

    public static readonly string[] Families = [DialectGoldens, RendererGoldens, EscapeVectors];

    /// <summary>The entry a <c>template.md</c> case renders: the agent <c>golden</c> at the top of <c>nodes/</c>.</summary>
    private const string Golden = "node.golden";

    private const int Steps = 100_000;

    private const string Draft = "Margaret Okafor, 58, referred by Dr A. Sample for two weeks of fatigue (fictional).";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly JsonSerializerOptions Writing = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>
    /// One dialect golden: its folder <paramref name="Name"/>, what it pins (<paramref name="About"/>), the package
    /// published (<paramref name="Package"/>), the body of the agent <c>golden</c> when the case is that one body
    /// (<paramref name="Template"/>, written as <c>template.md</c>; else the case writes <c>package/</c> and
    /// <c>entry.txt</c>), the entry composed, the data and the decided placements, and for a macro the model's reply.
    /// </summary>
    internal sealed record DialectCase(
        string Name, string About, Dictionary<string, byte[]> Package, string? Template, string Entry,
        JsonObject Scope, IReadOnlyList<(string Site, bool Placed)> Placed, string? Reply = null);

    [Fact]
    public void Export()
    {
        var root = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT_V31");
        if (string.IsNullOrWhiteSpace(root))
            return;

        var conformance = Path.Combine(root, "v31", "conformance");
        var indexPath = Path.Combine(conformance, "index.json");
        var index = File.ReadAllText(indexPath, Utf8NoBom);
        foreach (var family in Families)
        {
            Write(Path.Combine(conformance, family), Files(family));
            index = RefusalPackagesExport.WithCases(index, family, Cases(family));
        }

        File.WriteAllText(indexPath, index, Utf8NoBom);
    }

    /// <summary>Deletes <paramref name="folder"/> and writes <paramref name="files"/> under it.</summary>
    internal static void Write(string folder, IReadOnlyDictionary<string, byte[]> files)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        foreach (var (path, bytes) in files)
        {
            var file = Path.Combine([folder, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
    }

    /// <summary>The index's cases of <paramref name="family"/>: a case folder's path (<c>output-text</c>, <c>concept_bullets/empty</c>), or the one file <c>vectors.json</c>; sorted.</summary>
    internal static IReadOnlyList<string> Cases(string family) => family switch
    {
        DialectGoldens => DialectCases().Select(c => c.Name).Order(StringComparer.Ordinal).ToList(),
        RendererGoldens => RendererIds.SelectMany(r => V31BuiltInRenderersTests.Cases.Keys.Select(n => $"{r}/{Slug(n)}")).Order(StringComparer.Ordinal).ToList(),
        EscapeVectors => ["vectors.json"],
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>Every file of <paramref name="family"/>, by its path within the family's folder.</summary>
    internal static Dictionary<string, byte[]> Files(string family)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        switch (family)
        {
            case DialectGoldens:
                foreach (var golden in DialectCases())
                {
                    foreach (var (path, bytes) in DialectFiles(golden))
                        files[$"{golden.Name}/{path}"] = bytes;
                }

                break;
            case RendererGoldens:
                foreach (var id in RendererIds)
                {
                    foreach (var (name, concepts) in V31BuiltInRenderersTests.Cases)
                    {
                        foreach (var (path, bytes) in RendererFiles(id, name, concepts))
                            files[$"{id}/{Slug(name)}/{path}"] = bytes;
                    }
                }

                break;
            case EscapeVectors:
                files["vectors.json"] = Text(JsonText(EscapeVectorsDocument()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }

        return files;
    }

    /// <summary>A case name as a folder name: lower case, each space a <c>-</c>.</summary>
    internal static string Slug(string name) => name.Replace(' ', '-').ToLowerInvariant();

    private static byte[] Text(string text) => Utf8NoBom.GetBytes(text);

    /// <summary>JSON as the suite writes it: 2-space indentation, <c>\n</c> line ends, a final newline, every character outside ASCII as itself.</summary>
    private static string JsonText(JsonNode node) => LogicVectors.AsItself(node.ToJsonString(Writing)) + "\n";

    // ---- dialect-goldens ----------------------------------------------------------------------

    /// <summary>
    /// The files of one dialect golden: <c>about.txt</c>; <c>template.md</c>, or <c>package/</c> (the upload byte for
    /// byte) and <c>entry.txt</c> (the entry composed); <c>scope.json</c> (the data, rooted keys as the run holds them);
    /// <c>placed.json</c> (each decided site of the entry's <c>placements</c> to its decision); <c>expected.txt</c> (the
    /// composed text, as sent); <c>expected.neutral.txt</c> when a run spot was filled (the run-neutral text); and for a
    /// macro <c>reply.txt</c> and <c>expected.reply.txt</c> (the reply with each offered marker replaced).
    /// </summary>
    internal static Dictionary<string, byte[]> DialectFiles(DialectCase golden)
    {
        var plan = RunFixture.Plan(golden.Package);
        var placed = golden.Placed.ToDictionary(p => p.Site, p => p.Placed, StringComparer.Ordinal);
        var composition = BodyComposer.Compose(plan, new ComposeRequest(golden.Entry, null, golden.Scope.DeepClone().AsObject(), placed, Steps));
        Assert.True(composition.Failure == null && !composition.TooManySteps, $"{golden.Name}: {composition.Failure}");

        var placedJson = new JsonObject();
        foreach (var (site, decision) in golden.Placed)
            placedJson[site] = decision;

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (golden.Template != null)
        {
            files["template.md"] = Text(golden.Template);
        }
        else
        {
            foreach (var (path, bytes) in golden.Package)
                files[$"package/{path}"] = bytes;
            files["entry.txt"] = Text(golden.Entry + "\n");
        }

        files["scope.json"] = Text(JsonText(golden.Scope));
        files["placed.json"] = Text(JsonText(placedJson));
        files["expected.txt"] = Text(composition.Text!);
        if (composition.NeutralText != null)
            files["expected.neutral.txt"] = Text(composition.NeutralText);
        if (golden.Reply != null)
        {
            var call = new AgentCall(golden.Entry, golden.Entry, null, 1, false, golden.Scope.DeepClone().AsObject(), placed);
            var prepared = AgentTurn.Prepare(plan, call, Steps, []);
            Assert.Equal(composition.Text, prepared.Text);
            var outcome = AgentTurn.Finish(prepared, golden.Reply);
            Assert.True(outcome.Invalid == null, $"{golden.Name}: " + string.Join(" | ", outcome.Invalid?.Select(f => f.Sentence) ?? []));
            files["reply.txt"] = Text(golden.Reply);
            files["expected.reply.txt"] = Text(outcome.Reply!);
        }

        files["about.txt"] = Text(About(golden));
        return files;
    }

    private static string About(DialectCase golden)
    {
        var what = golden.Template != null
            ? "template.md is the prompt of the agent node golden at the top of nodes/ (format plaintext), in a package whose "
              + "reads are typed as scope.json's values are: the input consult_draft (markdown_block) and visit_date (date), "
              + "the agent summary (plaintext), the agent plan (a section_plan: sections, each a title and a kind), the loop "
              + "drafts over the plan's sections (a list of plaintext), the computed score (number) and flagged (boolean), and "
              + "the check coverage (a check_result)."
            : $"package/ is the upload, byte for byte; entry.txt names the entry composed ({golden.Entry}).";
        var reply = golden.Reply != null
            ? " reply.txt is the model's reply, and expected.reply.txt that reply with each offered macro's marker replaced by "
              + "the macro's rendered body (chapter 5 § 5.8.6); the reply is never rendered."
            : "";
        return golden.About + "\n\n" + what + " Composing it over scope.json (the data, keys rooted as the run holds them), "
            + "with each placement decided as placed.json says (a site it does not name is not placed), gives expected.txt "
            + "byte for byte: Scriban 7.2.5, the invariant culture, strict variables."
            + (golden.Name.StartsWith("run-spot", StringComparison.Ordinal)
                ? " expected.neutral.txt is the run-neutral text of the same render: each run spot is the marker {{run:<n>}}, "
                  + "numbered in order of appearance (chapter 5 § 5.4)."
                : "")
            + reply + "\n";
    }

    /// <summary>
    /// The package a <c>template.md</c> case is the agent <c>golden</c> of: <see cref="TemplateFixture.Package"/> with the
    /// optional date input <c>visit_date</c>, the agent <c>summary</c>, the loop <c>drafts</c> over the plan's sections, the
    /// computed <c>score</c> and <c>flagged</c>, the check <c>coverage</c>, and <paramref name="template"/> (declared
    /// run-dependent when it reads a run fact).
    /// </summary>
    private static Dictionary<string, byte[]> TemplatePackage(string template)
    {
        var files = Package()
            .WithJson("manifest.json", m => m["inputs"]!["visit_date"] = JsonNode.Parse(
                "{ \"label\": \"Visit date\", \"description\": null, \"format\": \"date\", \"required\": false, \"channel\": null }"))
            .WithAgent("summary", "Summarise the draft below for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n")
            .WithLoop("drafts", "node.plan.value.sections", "draft", "Draft the section '{{ item.value.title }}'.\n")
            .WithComputed("score", "\"number\"", "{ \"/\": [{ \"size\": [{ \"var\": \"input.consult_draft.content\" }] }, 10] }")
            .WithComputed("flagged", "\"boolean\"", "{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }")
            .WithCheck()
            .WithAgent("golden", template);
        return template.Contains("run.", StringComparison.Ordinal) ? files.RunDependent("nodes/golden.md") : files;
    }

    private static DialectCase OfTemplate(string name, string about, string template, JsonObject scope) =>
        new(name, about, TemplatePackage(template), template, Golden, scope, []);

    /// <summary>The data a body is given: each path's value at the path itself.</summary>
    private static JsonObject Scope(params (string Path, JsonNode? Value)[] paths) => BodyComposerTests.Scope(paths);

    private static JsonObject Sections() => new()
    {
        ["sections"] = new JsonArray(
            new JsonObject { ["title"] = "History of present illness", ["kind"] = "narrative" },
            new JsonObject { ["title"] = "Examination", ["kind"] = "table" },
            new JsonObject { ["title"] = "Assessment and plan", ["kind"] = "narrative" }),
    };

    private static JsonArray Drafts() => new(
        "Two weeks of fatigue, worse in the afternoons.",
        "Pale; heart rate 88; no lymphadenopathy.",
        "Check a full blood count and ferritin.");

    /// <summary>A <c>.items</c> element of the loop <c>drafts</c> (part 04 § 2.6): completed with its draft, or skipped or not run with its reason.</summary>
    private static JsonObject ItemOf(int index, string title, string kind, string state, string? draft, string? reason) => new()
    {
        ["item"] = new JsonObject { ["id"] = $"{index}", ["value"] = new JsonObject { ["title"] = title, ["kind"] = kind } },
        ["label"] = title,
        ["state"] = state,
        ["reason"] = reason,
        ["value"] = draft,
        ["content"] = draft,
        ["nodes"] = new JsonObject { ["draft"] = new JsonObject { ["value"] = draft, ["content"] = draft, ["state"] = state } },
    };

    /// <summary>The single snippet <paramref name="id"/> and the agent <c>golden</c> placing it, for a § 2.6 form.</summary>
    private static Dictionary<string, byte[]> Placing(string id, string text, string body) =>
        Package().WithSnippet(id, text).WithAgent("golden", body);

    private static DialectCase Placement(string name, string about, string text, string body, bool placed) =>
        new(name, about, Placing("aside", text, body), null, Golden, new JsonObject(), [("snippet.aside", placed)]);

    private static string MacroHeader(string label, string instruction) =>
        $"{{ \"label\": \"{label}\", \"description\": null, \"if\": null, \"format\": \"plaintext\", \"instruction\": {instruction} }}";

    /// <summary>Every dialect golden, one per allowed feature of <c>scriban-dialect.md</c> § 10.</summary>
    internal static IReadOnlyList<DialectCase> DialectCases()
    {
        var cases = new List<DialectCase>
        {
            // ---- output, member access, literals (chapter 5 § 5.2.1; D13) ----
            OfTemplate("output-text",
                "An output writes a text read as it is: the input's .content, and a member of an item read through a for "
                + "(chapter 5 § 5.2.1).",
                "Draft the note for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n\nFirst planned: {{ for s in node.plan.value.sections }}{{ if for.first }}{{ s.title }}{{ end }}{{ end }}.\n",
                Scope(("input.consult_draft.content", Draft), ("node.plan.value", Sections()))),
            OfTemplate("output-values",
                "An output writes a number in its invariant form (7.5, never 7,5), a boolean as true or false, a date as "
                + "%Y-%m-%d, and null as nothing (chapter 5 §§ 5.2.2, 5.2.3, 5.2.4).",
                "Score: {{ node.score.value }}\nFlagged: {{ node.flagged.value }}\nVisit: {{ input.visit_date.value }}\nSummary: [{{ node.summary.content }}]\n",
                Scope(("node.score.value", JsonNode.Parse("7.50")), ("node.flagged.value", false), ("input.visit_date.value", "2026-11-20"),
                    ("node.summary.content", null))),

            // ---- the built-ins, each with and without null (chapter 5 § 5.2.2) ----
            BuiltIn("string-upcase", "string.upcase gives the invariant upper case", "{{ node.summary.content | string.upcase }}",
                "Margaret Okafor is seen today by Dr A. Sample; fatigue, ﬁnding iß."),
            BuiltIn("string-downcase", "string.downcase gives the invariant lower case", "{{ node.summary.content | string.downcase }}",
                "MARGARET OKAFOR, ÉTÉ İ Σ."),
            BuiltIn("string-capitalize", "string.capitalize upper-cases the first character, invariant", "{{ node.summary.content | string.capitalize }}",
                "margaret okafor is seen today."),
            BuiltIn("string-strip", "string.strip removes leading and trailing white space", "[{{ node.summary.content | string.strip }}]",
                " \t Seen by Dr A. Sample.  \n"),
            BuiltIn("array-join", "array.join joins the elements by its literal separator", "{{ node.drafts.value | array.join \"; \" }}",
                Drafts(), "node.drafts.value"),
            BuiltIn("array-size", "array.size gives the element count", "{{ node.drafts.value | array.size }} drafts",
                Drafts(), "node.drafts.value"),
            BuiltIn("date-to-string", "date.to_string formats a date by its literal pattern, with English names",
                "{{ input.visit_date.value | date.to_string \"%A %d %B %Y\" }}", "2026-11-20", "input.visit_date.value"),

            // ---- for and its position variables (chapter 5 § 5.2.1) ----
            ForCase("first", "for.first holds on the first element only",
                "{{ for s in node.plan.value.sections }}{{ if for.first }}First: {{ else }}, then {{ end }}{{ s.title }}{{ end }}.\n"),
            ForCase("last", "for.last holds on the last element only",
                "{{ for s in node.plan.value.sections }}{{ s.title }}{{ if !for.last }}; {{ end }}{{ end }}.\n"),
            ForCase("index", "for.index is the element's 0-based position",
                "{{ for s in node.plan.value.sections }}{{ for.index }}: {{ s.title }}\n{{ end }}"),
            ForCase("even", "for.even holds on the elements at an even 0-based position",
                "{{ for s in node.plan.value.sections }}{{ if for.even }}* {{ else }}- {{ end }}{{ s.title }}\n{{ end }}"),
            ForCase("odd", "for.odd holds on the elements at an odd 0-based position",
                "{{ for s in node.plan.value.sections }}{{ if for.odd }}(odd) {{ end }}{{ s.title }}\n{{ end }}"),

            // ---- if over recorded facts (chapter 5 § 5.2.5) ----
            OfTemplate("if-state",
                "A body if tests an item's state against the closed set, with an else (chapter 5 § 5.2.5's example): a "
                + "completed draft is written, a skipped one leaves nothing, and a not-run one its reason.",
                "{{~ for s in node.drafts.items ~}}\n{{~ if s.state == \"completed\" ~}}\n{{ s.value }}\n{{~ else if s.state == \"skipped\" ~}}\n{{~ else ~}}\n_{{ s.reason }}_\n{{~ end ~}}\n{{~ end ~}}\n",
                Scope(("node.drafts.items", new JsonArray(
                    ItemOf(1, "History of present illness", "narrative", "completed", "Two weeks of fatigue, worse in the afternoons.", null),
                    ItemOf(2, "Examination", "table", "skipped", null, "skipped: item.value.kind = \"table\" is false"),
                    ItemOf(3, "Assessment and plan", "narrative", "not_run", null, "not run: beyond max 2"))))),
            OfTemplate("if-check-pass",
                "A body if tests whether a check passed, and combines tests with ! (chapter 5 § 5.2.5).",
                "{{ if check.coverage.value.pass }}Every term is covered.{{ end }}{{ if !check.coverage.value.pass }}Some terms are not covered.{{ end }}\n",
                Scope(("check.coverage.value", new JsonObject
                {
                    ["pass"] = false, ["message"] = "The plan does not cover every term of the draft.",
                    ["uncovered"] = new JsonArray("Fatigue"), ["untested"] = null, ["passed"] = null, ["total"] = null,
                }))),

            // ---- whitespace (chapter 5 § 5.2.6): ~ on statements and outputs; each placement form, placed and absent ----
            OfTemplate("tilde-statements",
                "~ on a statement or an ordinary output removes the spaces and tabs on its side, and on the right the one "
                + "newline ending the line (chapter 5 § 5.2.6).",
                "Sections:\n{{~ for s in node.plan.value.sections ~}}\n- {{ s.title }}\n{{~ end ~}}\n\nDraft: {{~ input.consult_draft.content ~}}  \nEnd.\n",
                Scope(("node.plan.value", Sections()), ("input.consult_draft.content", Draft))),
            Placement("placement-block-placed",
                "{{~ x }} alone on its line, nothing after it, is a block: the unit's text stands on the line (chapter 5 § 5.2.6).",
                "Keep a calm tone for Margaret Okafor.", "Before\n{{~ snippet.aside.content }}\nAfter\n", true),
            Placement("placement-block-absent",
                "{{~ x }} alone on its line with its unit absent: an empty line remains (chapter 5 § 5.2.6).",
                "Keep a calm tone for Margaret Okafor.", "Before\n{{~ snippet.aside.content }}\nAfter\n", false),
            Placement("placement-inline-placed",
                "{{ x }} inside a line, against a word: the unit's text is spliced where the tag stands, nothing removed "
                + "(chapter 5 § 5.2.6).",
                ", in brief", "Write about diabetes{{ snippet.aside.content }}.\n", true),
            Placement("placement-inline-absent",
                "{{ x }} inside a line with its unit absent: nothing is removed around it (chapter 5 § 5.2.6).",
                ", in brief", "Write about diabetes{{ snippet.aside.content }}.\n", false),
            Placement("placement-alone-placed",
                "{{ x }} alone on its line, indented: the indentation before it stays, and the unit's later lines are its own, "
                + "not indented again (chapter 5 § 5.2.6).",
                "Sign as Dr A. Sample,\nconsultant.", "Close:\n  {{ snippet.aside.content }}\nEnd\n", true),
            Placement("placement-alone-absent",
                "{{ x }} alone on its line, indented, with its unit absent: the indentation stays (chapter 5 § 5.2.6).",
                "Sign as Dr A. Sample,\nconsultant.", "Close:\n  {{ snippet.aside.content }}\nEnd\n", false),

            // ---- a part, a snippet, a folder item (chapter 5 §§ 5.6, 5.7) ----
            new("part",
                "A part placed: its text lines joined with \\n, no newline added after the last, rendered in the prompt's "
                + "scope (chapter 5 § 5.7); its header if was decided true, as placed.json says.",
                Package()
                    .WithAgent("summary", "Summarise the draft below for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n")
                    .WithAgent("golden", "Draft the plan for Margaret Okafor.\n{{~ part.caution }}\nThen sign it.\n")
                    .WithJson("nodes/golden.md", h => h["parts"] = JsonNode.Parse(
                        "{ \"caution\": { \"if\": { \"!\": [{ \"blank\": [{ \"var\": \"node.summary.content\" }] }] }, "
                        + "\"text\": [\"Keep to the summary:\", \"\", \"{{ node.summary.content | string.strip }}\"] } }")),
                null, Golden, Scope(("node.summary.content", "  Fatigue for two weeks; no weight loss.\n")), [("part.caution", true)]),
            new("snippet",
                "A snippet is a template rendered where it is placed, in the placing template's scope, and may place another "
                + "snippet; its text is the author's to the byte, its file's final newline included (chapter 5 §§ 5.3.2, "
                + "5.6.3). The inner snippet is decided after the outer one, which places it.",
                Package()
                    .WithSnippet("clinician", "Dr A. Sample")
                    .WithSnippet("follow_up", "Refer Margaret Okafor back to {{ snippet.clinician.content }} in six weeks: {{ input.consult_draft.content | string.downcase }}\n")
                    .WithAgent("golden", "Plan:\n{{~ snippet.follow_up.content }}\nEnd of plan.\n"),
                null, Golden, Scope(("input.consult_draft.content", "Fatigue, Not Weight Loss.")),
                [("snippet.follow_up", true), ("snippet.clinician", true)]),
            new("folder-item",
                "In a loop over a snippet folder, item.value is the item's header and item.content its body, rendered in the "
                + "loop's scope where it is read (chapter 5 § 5.6.2): here the item hpi's body reads its own title and the "
                + "input. scope.json's item.content is the item's body as written.",
                FolderItemFixture.Standards("Open with '{{ item.value.title }}'. The draft:\n{{ input.consult_draft.content }}\n"),
                null, "node.standards.section",
                Scope(("item.id", "hpi"), ("item.value", new JsonObject { ["title"] = "History of Present Illness" }),
                    ("item.content", "Open with '{{ item.value.title }}'. The draft:\n{{ input.consult_draft.content }}\n"),
                    ("input.consult_draft.content", Draft)),
                []),

            // ---- a macro's instruction with each marker form (chapter 5 §§ 5.8.3, 5.8.4, 5.8.6; D17) ----
            new("macro-block",
                "A macro offered: its instruction is rendered where the prompt places it, its own block marker written as its "
                + "source text, the line's indentation kept. Its body is rendered in the prompt's scope, a snippet it places "
                + "decided after the macro, and loses its file's one final line ending; in the reply, the block marker's "
                + "indentation and the spaces after it go, and the line's newline stays (chapter 5 §§ 5.8.4, 5.8.6).",
                Package()
                    .WithSnippet("clinician", "Dr A. Sample")
                    .Listed("macros/index.json", "follow_up")
                    .With("macros/follow_up.md", Md(MacroHeader("Follow-up",
                        "[\"Place the follow-up line alone, after the plan:\", \"  {{~ macro.follow_up.content }}\"]"),
                        "Follow up with {{ snippet.clinician.content }} on {{ input.visit_date.value | date.to_string \"%d %B %Y\" }}.\n"))
                    .WithJson("manifest.json", m => m["inputs"]!["visit_date"] = JsonNode.Parse(
                        "{ \"label\": \"Visit date\", \"description\": null, \"format\": \"date\", \"required\": false, \"channel\": null }"))
                    .WithAgent("golden", "Draft the plan for Margaret Okafor.\n{{~ macro.follow_up }}\nEnd with the plan.\n"),
                null, Golden, Scope(("input.visit_date.value", "2026-11-20")),
                [("macro.follow_up", true), ("snippet.clinician", true)],
                "Fatigue for two weeks; check a full blood count.\n   {{~ macro.follow_up.content }}   \nNo other concerns.\n"),
            new("macro-inline",
                "A macro of the folder letters offered: its instruction is rendered with its own inline marker, qualified "
                + "(macro.letters.closing.content), written as its source text; in the reply the inline marker is replaced "
                + "exactly where it stands, by the body without its file's final line ending (chapter 5 §§ 5.8.1, 5.8.4, 5.8.6).",
                Package()
                    .Listed("macros/index.json", "letters")
                    .With("macros/letters/index.json", Index(["closing"], extra: ", \"if\": null"))
                    .With("macros/letters/closing.md", Md(MacroHeader("Closing",
                        "[\"End the letter's last sentence with {{ macro.letters.closing.content }} before the signature.\"]"),
                        "Kind regards, Dr A. Sample\n"))
                    .WithAgent("golden", "Write the letter to Margaret Okafor.\n{{~ macro.letters.closing }}\n"),
                null, Golden, new JsonObject(), [("macro-folder.letters", true), ("macro.letters.closing", true)],
                "Dear Ms Okafor, your results are normal. {{ macro.letters.closing.content }}\n"),

            // ---- raw block, comment, renderers, run spot ----
            OfTemplate("raw-block",
                "A raw block's text is written literally, its braces not interpreted (chapter 5 § 5.2.1).",
                "Copy {%{{{ node.summary.content }}}%} as written.\n", new JsonObject()),
            OfTemplate("comment",
                "A comment renders as nothing (chapter 5 § 5.2.1).",
                "Draft the note.{{# for the author only, never sent }}\nThen stop.\n", new JsonObject()),
            new("pipe-renderer",
                "A pipe into a package renderer and into a built-in one, and a chain: each renderer's body renders with the "
                + "piped value as value, and a chain pipes each step's text into the next (chapter 5 §§ 5.5.2, 5.5.3): the package "
                + "renderer quote writes > once, before the whole of its value.",
                WithTerms(Package())
                    .WithRenderer("quote", "\"markdown_block\"", "> {{ value }}")
                    .WithAgent("golden", "Terms:\n{{ node.terms.value | renderer.concept_bullets }}\nQuoted:\n{{ node.terms.value | renderer.concept_bullets | renderer.quote }}\n"),
                null, Golden, Scope(("node.terms.value", Concepts())), []),
            new("renderer-if-value",
                "A renderer's body may test its own value in an if, here through a pipe to a built-in (ruling R2: a pipe to an "
                + "allowed built-in inside an if); a blank draft renders (none) (chapter 5 §§ 5.2.5, 5.5.1).",
                Package()
                    .WithLoop("drafts", "node.plan.value.sections", "draft", "Draft the section '{{ item.value.title }}'.\n")
                    .WithRenderer("bracket", "\"plaintext\"", "{{ if (value | string.strip) != \"\" }}[{{ value | string.strip }}]{{ else }}(none){{ end }}", "plaintext")
                    .WithAgent("golden", "{{ for d in node.drafts.value }}{{ d | renderer.bracket }}\n{{ end }}"),
                null, Golden, Scope(("node.drafts.value", new JsonArray("  Two weeks of fatigue. ", " \t ", "Pale."))), []),
            OfTemplate("run-spot",
                "Two run spots, each an output that reads a run fact and nothing else, the first piped to a built-in: the sent "
                + "text holds their outputs, the run-neutral text the markers {{run:1}} and {{run:2}} (chapter 5 § 5.4).",
                "Seen on {{ run.date | date.to_string \"%d %B %Y\" }} by Dr A. Sample.\nJob {{ run.id }}.\n",
                Scope(("run.date", "2026-11-21"), ("run.id", "job-0001"))),
        };
        cases.AddRange(NullCases());
        return cases;
    }

    private static DialectCase BuiltIn(string name, string about, string template, JsonNode value, string path = "node.summary.content") =>
        OfTemplate(name, $"{about} (chapter 5 § 5.2.2).", template + "\n", Scope((path, value)));

    /// <summary>Each built-in given <c>null</c> gives <c>null</c>, written as nothing; array.join over a null list too (D13).</summary>
    private static IEnumerable<DialectCase> NullCases()
    {
        (string Name, string Template, string Path)[] nulls =
        [
            ("string-upcase", "[{{ node.summary.content | string.upcase }}]", "node.summary.content"),
            ("string-downcase", "[{{ node.summary.content | string.downcase }}]", "node.summary.content"),
            ("string-capitalize", "[{{ node.summary.content | string.capitalize }}]", "node.summary.content"),
            ("string-strip", "[{{ node.summary.content | string.strip }}]", "node.summary.content"),
            ("array-join", "[{{ node.drafts.value | array.join \"; \" }}]", "node.drafts.value"),
            ("array-size", "[{{ node.drafts.value | array.size }}]", "node.drafts.value"),
            ("date-to-string", "[{{ input.visit_date.value | date.to_string \"%d %B %Y\" }}]", "input.visit_date.value"),
        ];
        foreach (var (name, template, path) in nulls)
        {
            var builtIn = template.Split('|')[1].Trim().Split(' ')[0];
            yield return OfTemplate(name + "-null",
                $"{builtIn} given null{(builtIn == "array.join" ? " (a null list, not a null element)" : "")} gives null, "
                + "which writes nothing (chapter 5 § 5.2.2).",
                template + "\n", Scope((path, null)));
        }
    }

    private static DialectCase ForCase(string position, string about, string template) =>
        OfTemplate("for-" + position, $"A for runs over a whole list in order; {about} (chapter 5 § 5.2.1).", template,
            Scope(("node.plan.value", Sections())));

    private static Dictionary<string, byte[]> WithTerms(Dictionary<string, byte[]> files) =>
        files.WithAgent("terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"");

    /// <summary><see cref="Consultologist.PackageFormat.ConceptSamples.ConceptListJson"/> as the run holds it: each concept with its <c>source</c>.</summary>
    private static JsonNode Concepts()
    {
        var value = JsonNode.Parse(Consultologist.PackageFormat.ConceptSamples.ConceptListJson)!;
        foreach (var concept in value["concepts"]!.AsArray())
            concept!["source"] = Consultologist.PackageFormat.ConceptSamples.Source;
        return value;
    }

    // ---- renderer-goldens ---------------------------------------------------------------------

    private static readonly string[] RendererIds = ["concept_bullets", "concept_context"];

    /// <summary>
    /// One renderer golden: <c>value.json</c> (the <c>concept_list</c> value piped, as the run holds it: a missing type
    /// written <c>""</c>, as the format requires a string) and <c>expected.txt</c>, what the published built-in renderer
    /// gives when a prompt pipes the value into it, and <c>about.txt</c>.
    /// </summary>
    private static Dictionary<string, byte[]> RendererFiles(string id, string name, V31BuiltInRenderersTests.Concept[] concepts)
    {
        var value = new JsonObject
        {
            ["concepts"] = new JsonArray(concepts.Select(c => (JsonNode?)new JsonObject
            {
                ["term"] = c.Term,
                ["type"] = c.Type ?? "",
                ["id"] = c.Id,
                ["isSnomedConcept"] = c.IsSnomedConcept,
                ["isActive"] = c.IsActive,
                ["source"] = c.Source,
                ["support"] = c.Support,
            }).ToArray()),
        };
        var plan = RunFixture.Plan(WithTerms(Package()).WithAgent("golden", $"{{{{ node.terms.value | renderer.{id} }}}}"));
        var composition = BodyComposer.Compose(plan, new ComposeRequest(Golden, null, Scope(("node.terms.value", value.DeepClone())),
            new Dictionary<string, bool>(StringComparer.Ordinal), Steps));
        Assert.True(composition.Failure == null && !composition.TooManySteps, $"{id}/{name}: {composition.Failure}");

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["about.txt"] = Text(
                $"The built-in renderer {id} (v31/renderers/{id}.md) over the concept list of the case '{name}'. Piping value.json "
                + $"into renderer.{id} gives expected.txt byte for byte (chapter 5 § 5.5.3): Scriban 7.2.5, the invariant "
                + "culture, the JSON member names. The C# renderer of version 30 gives the same bytes.\n"),
            ["value.json"] = Text(JsonText(value)),
            ["expected.txt"] = Text(composition.Text!),
        };
    }

    // ---- markdown-escape-vectors --------------------------------------------------------------

    /// <summary>The ten sentences of fictional clinical text the escape vectors hold beside the 32 characters.</summary>
    private static readonly string[] Sentences =
    [
        "Margaret Okafor, 58, seen by Dr A. Sample on 20/11/2026.",
        "BP 128/82 mmHg; HR 88 bpm (regular); SpO2 98% on air.",
        "Plan: FBC + ferritin -> review in 2 weeks [if Hb < 110 g/L].",
        "* Not a bullet: she said \"I'm tired all the time\" & wants answers!",
        "# Not a heading; _not emphasis_ and `not code` either.",
        "Metformin 500 mg BD ~ 1 g/day; aim HbA1c <= 53 mmol/mol.",
        "1. Not a list item: Dr A. Sample's {follow-up} at 09:30 = done?",
        "Allergies: penicillin (rash) | none known @ home; see note #4.",
        "    Seen with her daughter; the four spaces before this line stay as written.",
        "Fatigue – two weeks, worse ≥ 3 pm; café visit with her daughter, née Adeyemi.\nSecond line: \\ back-slashed.",
    ];

    private static JsonArray EscapeVectorsDocument()
    {
        // The 32 ASCII punctuation characters in code-point order (! to ~ without the letters and digits), then the sentences.
        var plains = Enumerable.Range('!', '~' - '!' + 1).Select(c => (char)c).Where(c => !char.IsAsciiLetterOrDigit(c))
            .Select(c => c.ToString()).Concat(Sentences);
        return new JsonArray(plains.Select(p => (JsonNode?)new JsonObject { ["plain"] = p, ["escaped"] = MarkdownEscape.Escape(p) }).ToArray());
    }
}
