using Bunit;
using Microsoft.JSInterop;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.Create31;
using NSubstitute;
using static Consultologist.Web.Tests.V31.Package31Fixtures;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 10 (#1509, D5): a 31 <c>markdown_block</c> input is a text area holding Markdown with a five-button toolbar and a
/// Preview toggle rendered by <c>MarkdownPreview</c>. The value is the Markdown text; no HTML is stored.
/// </summary>
public class MarkdownInputTests : ClientRenderTestContext
{
    [Fact]
    public void TypingText_ReportsItAsMarkdown()
    {
        string? reported = null;
        var cut = Render<MarkdownInput>(p => p
            .Add(c => c.Label, "Consult draft")
            .Add(c => c.ValueChanged, (string? v) => reported = v));

        cut.Find("fluent-text-area").Change("# Plan\n\n- **one**");

        Assert.Equal("# Plan\n\n- **one**", reported);
    }

    [Fact]
    public void TheToolbar_HasFiveLabelledButtons()
    {
        var cut = Render<MarkdownInput>(p => p.Add(c => c.Label, "Consult draft"));

        var buttons = cut.FindAll(".markdown-input__toolbar button[data-format]");

        Assert.Equal(
            ["Bold", "Italic", "Heading", "Bulleted list", "Numbered list"],
            buttons.Select(b => b.GetAttribute("aria-label")!).ToArray());
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        // The page may draw this inside a <label>: the toolbar's clicks must not reach it.
        var toolbar = cut.Find(".markdown-input__toolbar");
        Assert.True(toolbar.HasAttribute("blazor:onclick:preventdefault"));
        Assert.True(toolbar.HasAttribute("blazor:onclick:stoppropagation"));
    }

    [Fact]
    public void Bold_AsksTheBrowserToWrapTheSelection()
    {
        var cut = Render<MarkdownInput>(p => p.Add(c => c.Label, "Consult draft"));

        cut.Find("button[data-format=bold]").Click();

        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier.EndsWith("wrapSelection"));
        Assert.Equal("**", call.Arguments[1]);
        Assert.Equal("**", call.Arguments[2]);
        Assert.Equal(string.Empty, call.Arguments[3]);
        // The browser gave nothing back (loose mode): the component still stands.
        Assert.NotNull(cut.Find("fluent-text-area"));
    }

    [Fact]
    public void AWrapsResult_BecomesTheValue_AndIsReported()
    {
        JSInterop.Setup<MarkdownInput.WrapResult?>("consultologistMarkdown.wrapSelection", _ => true)
            .SetResult(new MarkdownInput.WrapResult("**x**", 2, 3));
        string? reported = null;
        var cut = Render<MarkdownInput>(p => p
            .Add(c => c.Label, "Consult draft")
            .Add(c => c.Value, "x")
            .Add(c => c.ValueChanged, (string? v) => reported = v));

        cut.Find("button[data-format=bold]").Click();

        Assert.Equal("**x**", reported);
        Assert.Equal("**x**", cut.Find("fluent-text-area").GetAttribute("value"));
    }

    [Fact]
    public void AWrapThatFailsInTheBrowser_LeavesTheValueAlone()
    {
        JSInterop.Setup<MarkdownInput.WrapResult?>("consultologistMarkdown.wrapSelection", _ => true)
            .SetException(new JSException("no selection on this element"));
        var reported = false;
        var cut = Render<MarkdownInput>(p => p
            .Add(c => c.Label, "Consult draft")
            .Add(c => c.Value, "x")
            .Add(c => c.ValueChanged, (string? _) => reported = true));

        cut.Find("button[data-format=bold]").Click();

        Assert.False(reported);
        Assert.Equal("x", cut.Find("fluent-text-area").GetAttribute("value"));
    }

    [Fact]
    public void Preview_RendersThroughMarkdownPreview_AndHidesTheTextArea()
    {
        var cut = Render<MarkdownInput>(p => p
            .Add(c => c.Label, "Consult draft")
            .Add(c => c.Value, "Some **bold** text <script>alert(1)</script>"));

        cut.Find("button[data-action=preview]").Click();

        Assert.Empty(cut.FindAll("fluent-text-area"));
        Assert.Equal("bold", cut.Find(".markdown-input__preview strong").TextContent);
        Assert.Empty(cut.FindAll(".markdown-input__preview script"));

        cut.Find("button[data-action=preview]").Click();
        Assert.NotNull(cut.Find("fluent-text-area"));
        Assert.Empty(cut.FindAll(".markdown-input__preview"));
    }

    [Fact]
    public void Preview_OfALeadingHash_ShowsAHeading()
    {
        var cut = Render<MarkdownInput>(p => p
            .Add(c => c.Label, "Consult draft")
            .Add(c => c.Value, "# Not a comment"));

        cut.Find("button[data-action=preview]").Click();

        Assert.Equal("Not a comment", cut.Find(".markdown-input__preview h1").TextContent);
    }

    [Fact]
    public void ADocument_CanStillBeAttached_ToAMarkdownInput()
    {
        var package = Package(Input("consult_draft", "Consult draft", true, MarkdownBlock));
        WorkflowService.GetCurrentPackageAsync().Returns(package);
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));

        var page = Render<Consults>();

        var slot = page.Find("[data-input-id='consult_draft']");
        Assert.NotNull(slot.QuerySelector(".markdown-input"));
        Assert.NotNull(slot.QuerySelector(".markdown-input__toolbar"));
        Assert.NotNull(slot.QuerySelector("input[type=file]"));
        Assert.NotNull(slot.QuerySelector(".run-picker"));
    }
}
