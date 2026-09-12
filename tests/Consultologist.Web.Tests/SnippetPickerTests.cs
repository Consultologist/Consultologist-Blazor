using System;
using System.Collections.Generic;
using Bunit;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #561/#691: the snippet inserter — a disclosure button that reveals the
/// account's snippets and hands the chosen one to the host. Purely data-driven,
/// no services.
/// </summary>
public class SnippetPickerTests : ClientRenderTestContext
{
    private static IReadOnlyList<Snippets.Snippet> TwoSnippets() => new[]
    {
        new Snippets.Snippet("s1", "Normal exam", "The exam was unremarkable.", DateTimeOffset.UtcNow),
        new Snippets.Snippet("s2", "Follow-up", "Return in two weeks.", DateTimeOffset.UtcNow),
    };

    [Fact]
    public void ClosedByDefault_OpensToAGroupOfItems()
    {
        var page = Render<SnippetPicker>(p => p.Add(x => x.Items, TwoSnippets()));

        Assert.Equal("false", page.Find(".snippet-picker__trigger").GetAttribute("aria-expanded"));

        page.Find(".snippet-picker__trigger").Click();

        var panel = page.Find(".snippet-picker__panel");
        Assert.Equal("group", panel.GetAttribute("role"));
        Assert.Equal("Snippets", panel.GetAttribute("aria-label"));
        Assert.Equal(2, page.FindAll(".snippet-picker__item").Count);
        Assert.Equal("true", page.Find(".snippet-picker__trigger").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ChoosingAnItem_HandsItToTheHost()
    {
        Snippets.Snippet? chosen = null;
        var page = Render<SnippetPicker>(p => p
            .Add(x => x.Items, TwoSnippets())
            .Add(x => x.OnChosen, EventCallback.Factory.Create<Snippets.Snippet>(this, s => chosen = s)));

        page.Find(".snippet-picker__trigger").Click();
        page.FindAll(".snippet-picker__item")[1].Click();

        Assert.NotNull(chosen);
        Assert.Equal("Follow-up", chosen!.Name);
    }
}
