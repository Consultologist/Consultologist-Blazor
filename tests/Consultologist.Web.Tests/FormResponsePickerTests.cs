using System;
using System.Collections.Generic;
using Bunit;
using Consultologist.Web.Services.Forms;
using Consultologist.Web.Shared;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #540/#691: the "load from a form response" disclosure. Opening it lists the
/// account's held responses (fetched on demand); a response whose values were
/// deleted is listed but not choosable.
/// </summary>
public class FormResponsePickerTests : ClientRenderTestContext
{
    private static readonly DateTimeOffset When = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opening_ListsResponses_AndADeletedOneIsNotChoosable()
    {
        FormsService.ListResponsesAsync().Returns(new List<FormResponseListRow>
        {
            new("triage-intake", "17", When, new[] { "consult_draft" }, null),
            new("triage-intake", "9", When.AddDays(-3), new[] { "consult_draft" }, DeletedAtUtc: When),
        });

        var page = Render<FormResponsePicker>();
        Assert.Equal("false", page.Find(".form-picker__trigger").GetAttribute("aria-expanded"));

        page.Find(".form-picker__trigger").Click();

        page.WaitForAssertion(() =>
        {
            var panel = page.Find(".form-picker__panel");
            Assert.Equal("group", panel.GetAttribute("role"));
            Assert.Equal("Held form responses", panel.GetAttribute("aria-label"));
            Assert.Equal(2, page.FindAll(".form-picker__response").Count);
        });

        var chooseButtons = page.FindAll(".form-picker__choose");
        // The live response is choosable; the deleted one is disabled.
        Assert.False(chooseButtons[0].HasAttribute("disabled"));
        Assert.True(chooseButtons[1].HasAttribute("disabled"));
    }
}
