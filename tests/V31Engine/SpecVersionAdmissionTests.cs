using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.3, 2.1): which spec versions the engine admits. The compiled-in lists stay
/// 5–30; <c>WorkflowPackages__PreviewSpecVersions</c> admits a version this build implements but has not released beside
/// them, and anything else in it stops the host at startup, naming the setting and the value.
/// </summary>
public class SpecVersionAdmissionTests
{
    internal static SpecVersionAdmission With(string? preview) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WorkflowPackages:PreviewSpecVersions"] = preview })
            .Build());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Unset_AdmitsOnlyTheCompiledInList(string? preview)
    {
        var admission = With(preview);

        Assert.True(admission.Admits(30));
        Assert.True(admission.Admits(5));
        Assert.False(admission.Admits(31));
        Assert.False(admission.Runs31);
        Assert.Empty(admission.Preview);
    }

    [Fact]
    public void Unset_WithNoSettingAtAll_AdmitsOnlyTheCompiledInList()
    {
        var admission = new SpecVersionAdmission(new ConfigurationBuilder().Build());

        Assert.False(admission.Admits(31));
        Assert.Empty(admission.Preview);
    }

    [Theory]
    [InlineData("31")]
    [InlineData(" 31 ")]
    [InlineData("31,31")]
    public void Listed31_IsAdmittedBesideTheList(string preview)
    {
        var admission = With(preview);

        Assert.True(admission.Admits(31));
        Assert.True(admission.Runs31);
        Assert.True(admission.Admits(30));
        Assert.True(admission.Admits(5));
        Assert.False(admission.Admits(4));
        Assert.False(admission.Admits(32));
        Assert.Equal(new[] { 31 }, admission.Preview);
    }

    [Theory]
    [InlineData("32", "32")]
    [InlineData("31,32", "32")]
    [InlineData("x", "x")]
    [InlineData("31;", "31;")]
    [InlineData("30", "30")]
    [InlineData("-1", "-1")]
    public void AnUnimplementedOrMalformedValue_StopsTheHost(string preview, string offending)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => With(preview));

        Assert.Contains("WorkflowPackages__PreviewSpecVersions", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"'{offending}'", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotSupported_IsS09001()
    {
        var without = With(null).NotSupported(31);
        var with = With("31").NotSupported(31);

        V31Contract.AssertSentence(new SourceRefusal("S09-001", without, ""));
        V31Contract.AssertSentence(new SourceRefusal("S09-001", with, ""));
        // <list> is the validator's own wording, the preview versions added after it in order.
        Assert.Equal(
            $"specVersion 31 is not supported: this engine accepts specVersion {WorkflowPackageValidator.DescribeAcceptedSpecVersions()} (pre-v5 packages are archived; see registry-operations.md).",
            without);
        Assert.Contains("specVersion 5, 6, ", with, StringComparison.Ordinal);
        Assert.Contains(", 29, 30 or 31 (pre-v5", with, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompiledInSets_AreUnchanged()
    {
        Assert.DoesNotContain(31, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(31, WorkflowPackageStore.SupportedSpecVersions);
        Assert.Equal(new[] { 31 }, SpecVersionAdmission.Implemented);
    }
}
