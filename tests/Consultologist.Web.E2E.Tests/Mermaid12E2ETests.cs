using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1510 (area 13, Task 1, D1): the vendored Mermaid is 12, the full build, and the app draws with ELK whether or not
/// a diagram's text declares it. The three diagrams of part 09 § 10.5 are copied verbatim: the 31 drawing rules
/// (the <c>doc</c> and <c>tag-rect</c> shapes, an edge to a frame under ELK) depend on 12 drawing them.
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
}
