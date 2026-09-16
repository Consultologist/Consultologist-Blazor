using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v15 (#731): a prompt may be `raw` — sent verbatim, no Scriban parse/render,
/// no strict variable check. A raw prompt declares no variables. The raw
/// fixture is a template node (its render is its output, no model call) whose
/// prompt carries literal braces that would fail Scriban if it were not raw.
/// </summary>
public static class V15Fixtures
{
    public static WorkflowPackageManifest Minimal() => V14Fixtures.Minimal() with { SpecVersion = 15 };

    /// <summary>
    /// The text a raw prompt carries — verbatim JSON and a literal {{ }} token
    /// that references nothing declared, so a non-raw prompt fails strict
    /// rendering while a raw one emits it unchanged.
    /// </summary>
    public const string RawText = "Return exactly: {\"ok\": true}. Escalate per {{ policy.rules }} — the braces are literal.";

    /// <summary>
    /// A raw template node added to the minimal manifest and aggregated into
    /// its deliverable. `raw` toggles the prompt; `variables` lets a test
    /// declare a (forbidden) variable on a raw prompt.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) WithRawTemplateNode(
        bool raw = true, List<string>? variables = null)
    {
        var manifest = Minimal();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("disclaimer", "prompts/disclaimer.md", variables ?? new List<string>(), Raw: raw ? true : null)
        };
        var node = new WorkflowNodeSpec("disclaimer-block", "Disclaimer",
            Prompt: "disclaimer",
            Bindings: new Dictionary<string, WorkflowBindingValue>(),
            Kind: WorkflowNodeKinds.Template);
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!) { node };
        nodes = nodes.Select(n => n.Aggregate != null
            ? n with { Aggregate = new List<string>(n.Aggregate) { "node:disclaimer-block" } }
            : n).ToList();
        manifest = manifest with { Prompts = prompts, Nodes = nodes };

        var files = new Dictionary<string, string>(V6Fixtures.Files(manifest), StringComparer.Ordinal)
        {
            ["prompts/disclaimer.md"] = RawText
        };
        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV15RawPromptTests
{
    [Fact]
    public void ARawTemplateNode_WithLiteralBraces_IsValid()
    {
        var result = V15Fixtures.Validate(V15Fixtures.WithRawTemplateNode());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheSameText_NotRaw_FailsToParse()
    {
        // The control: the identical literal-braces text on a non-raw prompt is
        // refused at publish — which is the whole reason raw exists.
        var result = V15Fixtures.Validate(V15Fixtures.WithRawTemplateNode(raw: false));
        Assert.Contains(result.Errors, e => e.Contains("does not parse") || e.Contains("failed strict rendering"));
    }

    [Fact]
    public void ARawPrompt_BelowFifteen_IsRefusedByVersion()
    {
        var (manifest, files) = V15Fixtures.WithRawTemplateNode();
        var result = WorkflowPackageValidator.Validate(
            manifest with { SpecVersion = 14 }, files, TestOutputContracts.CatalogSchemas);

        Assert.Contains(
            "Prompt 'disclaimer' declares raw, which requires specVersion 15.",
            result.Errors);
    }

    [Fact]
    public void ARawPrompt_WithDeclaredVariables_IsRefused()
    {
        var result = V15Fixtures.Validate(
            V15Fixtures.WithRawTemplateNode(variables: new List<string> { "seen_on" }));

        Assert.Contains(result.Errors, e =>
            e.Contains("Prompt 'disclaimer' is raw and must declare no variables"));
    }
}

public class PromptTemplateRendererRawTests
{
    private static WorkflowPromptTemplate Raw(string text, string? prelude = null) =>
        new("p", text, Array.Empty<string>(), prelude, Raw: true);

    [Fact]
    public void ARawPrompt_RendersVerbatim_SkippingScriban()
    {
        var rendered = PromptTemplateRenderer.Render(Raw(V15Fixtures.RawText), new Dictionary<string, string>());
        Assert.Equal(V15Fixtures.RawText, rendered);
    }

    [Fact]
    public void ARawPrompt_StillPrependsItsPrelude()
    {
        var rendered = PromptTemplateRenderer.Render(Raw("Body {{ x }}", "Prelude line"), new Dictionary<string, string>());
        Assert.Equal("Prelude line\n\nBody {{ x }}", rendered);
    }

    [Fact]
    public void TheSameText_NotRaw_Throws()
    {
        var notRaw = Raw("Body {{ x }}") with { Raw = false };
        Assert.Throws<InvalidOperationException>(() =>
            PromptTemplateRenderer.Render(notRaw, new Dictionary<string, string>()));
    }
}
