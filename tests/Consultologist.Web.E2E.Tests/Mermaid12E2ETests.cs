using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Consultologist.PackageFormat.V31.Diagram;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1510 (area 13, Task 1, D1): the vendored Mermaid is 12, the full build, and the app draws with ELK whether or not
/// a diagram's text declares it. The three diagrams of part 09 § 10.5 are copied verbatim: the 31 drawing rules
/// (the <c>doc</c> and <c>tag-rect</c> shapes, an edge to a frame under ELK) depend on 12 drawing them. Task 7 (D13, D15;
/// Review Focus 1, 4): every golden <c>dag.mmd</c> of <c>tests/V31Diagram/Golden/</c> (linked into this project) renders,
/// and a drawing whose labels hold Mermaid's own syntax shows them as written.
/// </summary>
[Collection(BrowserCollection.Name)]
public class Mermaid12E2ETests
{
    /// <summary>Part 09 § 10.5, the first example: the routing loop, a Checks frame, the gated deliverable.</summary>
    private const string RoutingLoopExample = """
        ---
        config:
          layout: elk
        ---
        flowchart TD
            input__sections(["input.sections"])
            input__consult_draft(["input.consult_draft"])
            run__date(["run.date"])

            subgraph for_node__sections_draft["sections_draft: per input.sections.value item"]
                subgraph case_node__sections_draft__route_branch_1["route: narrative"]
                    node__sections_draft__route__narrative_draft["narrative_draft<br/>Drafting a narrative section<br/>agent → markdown_block · generated"]
                end
                subgraph case_node__sections_draft__route_branch_2["route: table"]
                    node__sections_draft__route__table_draft["table_draft<br/>Drafting a table section<br/>agent → markdown_block · generated"]
                end
                node__sections_draft__route["route<br/>Routing a section by its kind<br/>case item.value.kind"]
            end
            node__sections_draft["sections_draft<br/>Sections — drafting each requested section by its kind<br/>item in input.sections.value"]

            subgraph checks["Checks"]
                check__coverage[["coverage<br/>Every requested section is present<br/>computed → check_result"]]
            end

            deliverable__consult_note{{"consult_note<br/>Consult note"}}

            input__consult_draft -->|"content"| node__sections_draft__route__narrative_draft
            input__consult_draft -->|"content"| node__sections_draft__route__table_draft
            node__sections_draft__route__narrative_draft -->|"ret (narrative)"| node__sections_draft__route
            node__sections_draft__route__table_draft -->|"ret (table)"| node__sections_draft__route
            input__sections -->|"for"| for_node__sections_draft
            node__sections_draft__route -->|"ret"| node__sections_draft
            node__sections_draft -->|"items"| deliverable__consult_note
            run__date --> deliverable__consult_note
            deliverable__consult_note -->|"content"| check__coverage
            input__sections -->|"value"| check__coverage
            check__coverage ==>|"gates"| deliverable__consult_note
            style checks stroke-width:3px
        """;

    /// <summary>Part 09 § 10.5, the second example: a renderer and a macro inside one node's frame.</summary>
    private const string RendererAndMacroExample = """
        flowchart TD
            node__extract_patient_concepts["extract_patient_concepts<br/>Extracting clinical concepts<br/>agent → concept_list · generated"]
            node__mentions_letrozole["mentions_letrozole<br/>Does the draft prescribe letrozole?<br/>agent → judgement · generated"]
            subgraph uses_node__identify_problem["identify_problem · Identifying the primary problem"]
                node__identify_problem__render_1[/"renderer.concept_bullets<br/>concept_list → markdown_block"/]
                node__identify_problem__macro_letrozole_monitoring@{ shape: doc, label: "macro.letrozole_monitoring<br/>markdown_block · placed verbatim" }
                node__identify_problem["identify_problem<br/>Identifying the primary problem<br/>agent → markdown_block · generated"]
                node__identify_problem__render_1 --> node__identify_problem
                node__identify_problem__macro_letrozole_monitoring -->|"placed"| node__identify_problem
            end
            node__extract_patient_concepts -->|"value"| node__identify_problem__render_1
            node__mentions_letrozole -.->|"if"| node__identify_problem__macro_letrozole_monitoring
        """;

    /// <summary>Part 09 § 10.5, the third example: a call and its function.</summary>
    private const string CallAndFunctionExample = """
        flowchart TD
            input__consult_draft(["input.consult_draft"])
            node__hpi_section@{ shape: tag-rect, label: "hpi_section<br/>call standard_section" }
            subgraph fn_standard_section["function standard_section (standard, draft) → titled_section"]
                param__standard(["param.standard"])
                param__draft(["param.draft"])
                fn_standard_section__node__standard_draft["standard_draft<br/>Drafting the section<br/>agent → markdown_block · generated"]
                fn_standard_section__node__section[["section<br/>The titled section<br/>computed → titled_section"]]
                param__standard --> fn_standard_section__node__standard_draft
                param__draft --> fn_standard_section__node__standard_draft
                fn_standard_section__node__standard_draft -->|"content"| fn_standard_section__node__section
            end
            input__consult_draft -->|"content"| node__hpi_section
            node__hpi_section -.-o|"calls"| fn_standard_section
        """;

    /// <summary>A flowchart with no frontmatter: only the app's own settings can ask for ELK.</summary>
    private const string NoFrontmatter = """
        flowchart TD
            input__consult_draft(["input.consult_draft"])
            node__draft["draft<br/>Drafting the note<br/>agent → markdown_block · generated"]
            input__consult_draft -->|"content"| node__draft
        """;

    /// <summary>
    /// A small <c>resolve.json</c> whose labels an author wrote with Mermaid's own syntax in them (Review Focus 1): a quote,
    /// markup, <c>]</c>, <c>|</c>, a backtick, the word <c>end</c>, an entity's text and a line break. Fictional text.
    /// </summary>
    private const string SyntaxDocument = """
        {
          "inputs": { "consult_draft": { "label": "consult_draft" }, "points": { "label": "points" } },
          "nodes": {
            "node.quoted": { "id": "quoted", "kind": "agent", "label": "Say \"hello\" to <b>Margaret Okafor</b>", "producer": "generated",
              "format": { "$ref": "#/$defs/plaintext" }, "modelDerived": true, "reads": ["input.consult_draft.content"] },
            "node.piped": { "id": "piped", "kind": "agent", "label": "Plan [a] | b | `code` and end", "producer": "generated",
              "format": { "$ref": "#/$defs/plaintext" }, "modelDerived": true, "reads": ["node.quoted.content"] },
            "node.loop": { "id": "loop", "kind": "for", "label": "end", "producer": "computed", "in": "input.points.value",
              "nodes": ["node.loop.broken"], "ret": "node.loop.broken", "until": null, "reads": ["input.points.value"] },
            "node.loop.broken": { "id": "broken", "kind": "agent", "label": "Line one\nline two #35; done", "producer": "generated",
              "format": { "$ref": "#/$defs/plaintext" }, "modelDerived": true, "reads": ["node.piped.content", "item.value"], "parent": "node.loop" }
          },
          "checks": {},
          "deliverables": {
            "deliverable.letter": { "id": "letter", "kind": "rendered", "label": "Letter for Dr A. Sample: end]",
              "format": { "$ref": "#/$defs/markdown_block" }, "reads": ["node.loop.items"] }
          },
          "graph": { "edges": [], "order": ["node.quoted", "node.piped", "node.loop", "deliverable.letter"] }
        }
        """;

    /// <summary>The labels of <see cref="SyntaxDocument"/> as a reader sees them: as written, a line break one space (D8).</summary>
    public static TheoryData<string> AsWritten() => new()
    {
        "Say \"hello\" to <b>Margaret Okafor</b>",
        "Plan [a] | b | `code` and end",
        "Line one line two #35; done",
        "Letter for Dr A. Sample: end]",
    };

    /// <summary>The golden drawings linked into this project (<c>Golden/&lt;name&gt;.dag.mmd</c>), by name.</summary>
    private static string[] GoldenNames() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "*.dag.mmd")
            .Select(path => Path.GetFileName(path)[..^".dag.mmd".Length])
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<string> Goldens() => new(GoldenNames());

    private readonly BrowserFixture _fixture;

    public Mermaid12E2ETests(BrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A page of the served app with Mermaid loaded, and the console's messages from then on. Mermaid logs nothing
    /// below <c>fatal</c> by default, and the interop initializes it before each render, so the page's
    /// <c>initialize</c> is wrapped to log warnings: a layout Mermaid lacks shows as its fallback warning.
    /// </summary>
    private async Task<(IPage Page, ConcurrentQueue<string> Console)> OpenAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await ApiMock.InstallAsync(page);
        var console = new ConcurrentQueue<string>();
        page.Console += (_, message) => console.Enqueue(message.Type + ": " + message.Text);

        await page.GotoAsync(_fixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !!window.consultologistMermaid", null, new() { Timeout = 30_000 });
        await page.EvaluateAsync(
            """
            async () => {
                await window.consultologistMermaid.ensure();
                const initialize = window.mermaid.initialize;
                window.mermaid.initialize = config => initialize.call(window.mermaid, { ...config, logLevel: "warn" });
            }
            """);
        return (page, console);
    }

    private static string[] LayoutFallbacks(ConcurrentQueue<string> console) =>
        console.Where(m => m.Contains("Layout algorithm") || m.Contains("is not registered")).ToArray();

    /// <summary>
    /// 12 no longer sets <c>window.mermaid.version</c> (11 did); its version is what its <c>info</c> diagram draws
    /// (<c>v12.3.0</c>).
    /// </summary>
    [Fact]
    public async Task TheVendoredMermaid_Is12()
    {
        var (page, _) = await OpenAsync();

        var svg = await MermaidProbe.RenderAsync(page, "info");

        Assert.Contains(">v12.", svg);
    }

    [Theory]
    [InlineData(nameof(RoutingLoopExample), "Every requested section is present")]
    [InlineData(nameof(RendererAndMacroExample), "macro.letrozole_monitoring")]
    [InlineData(nameof(CallAndFunctionExample), "call standard_section")]
    public async Task TheSpecsThreeExamples_Render(string example, string label)
    {
        var diagram = example switch
        {
            nameof(RoutingLoopExample) => RoutingLoopExample,
            nameof(RendererAndMacroExample) => RendererAndMacroExample,
            _ => CallAndFunctionExample,
        };
        var (page, console) = await OpenAsync();

        var svg = await MermaidProbe.RenderAsync(page, diagram);

        Assert.StartsWith("<svg", svg);
        Assert.Contains(label, svg);
        Assert.Empty(LayoutFallbacks(console));
    }

    [Fact]
    public async Task ElkLaysOutADiagramWithoutFrontmatter()
    {
        var (page, console) = await OpenAsync();

        var svg = await MermaidProbe.RenderAsync(page, NoFrontmatter);

        Assert.Contains("Drafting the note", svg);
        Assert.Equal("elk", await page.EvaluateAsync<string>("() => window.mermaid.mermaidAPI.getConfig().layout"));
        Assert.Empty(LayoutFallbacks(console));
    }

    /// <summary>
    /// The text a reader sees in <paramref name="svg"/>: its nodes' text, with the SVG's own <c>&lt;style&gt;</c> left out.
    /// </summary>
    private static Task<string> TextOfAsync(IPage page, string svg) =>
        page.EvaluateAsync<string>(
            """
            svg => {
                const holder = document.createElement("div");
                holder.innerHTML = svg;
                holder.querySelectorAll("style").forEach(s => s.remove());
                return holder.textContent;
            }
            """,
            svg);

    /// <summary>
    /// Every golden <c>dag.mmd</c> renders with ELK: an SVG, no error and no layout fallback. The largest
    /// (<c>example-v12-constructs</c>) is Review Focus 4's size check; <c>functions</c> draws a <c>-.-o</c> edge into a
    /// function's frame, frame-to-frame <c>aligned</c> edges and edges whose ends sit in nested frames.
    /// </summary>
    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task EveryGoldenDiagram_RendersInTheBrowser(string name)
    {
        var diagram = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name + ".dag.mmd"));
        var (page, console) = await OpenAsync();

        var svg = await MermaidProbe.RenderAsync(page, diagram);

        Assert.StartsWith("<svg", svg);
        Assert.Empty(LayoutFallbacks(console));
    }

    /// <summary>The link holds every golden: the seven drawings of <c>DiagramFixture.Drawings</c> (Task 6).</summary>
    [Fact]
    public void TheGoldens_AreTheSevenDrawings()
    {
        Assert.Equal(
            ["checked-note", "composer-sources", "conditional", "example-v12-constructs", "functions", "macros", "nested-note"],
            GoldenNames());
    }

    /// <summary>
    /// Review Focus 1: a drawing generated from labels holding Mermaid's own syntax renders, and each label shows as the
    /// author wrote it (a line break as one space): asserted on the drawing's text, not only on its rendering.
    /// </summary>
    [Theory]
    [MemberData(nameof(AsWritten))]
    public async Task ALabelHoldingMermaidSyntax_RendersAsWritten(string label)
    {
        var (page, console) = await OpenAsync();

        var svg = await MermaidProbe.RenderAsync(page, PackageDiagram.Generate(SyntaxDocument));

        Assert.StartsWith("<svg", svg);
        Assert.Contains(label, await TextOfAsync(page, svg));
        Assert.Empty(LayoutFallbacks(console));
    }
}
