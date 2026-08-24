using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

public class CalVerVersionTests
{
    [Theory]
    [InlineData("v2026.07.1", 2026, 7, 1)]
    [InlineData("v2026.07.10", 2026, 7, 10)]
    [InlineData("v2026.12.3", 2026, 12, 3)]
    public void TryParse_AcceptsWellFormedVersions(string input, int year, int month, int counter)
    {
        Assert.True(CalVerVersion.TryParse(input, out var version));
        Assert.Equal(new CalVerVersion(year, month, counter), version);
        Assert.Equal(input, version.ToString());
    }

    [Theory]
    [InlineData("2026.07.1")]      // missing v prefix
    [InlineData("v2026.7.1")]      // month not zero-padded
    [InlineData("v2026.13.1")]     // month out of range
    [InlineData("v2026.07.0")]     // counter starts at 1
    [InlineData("v2026.07.01")]    // counter must not be zero-padded
    [InlineData("v26.07.1")]       // two-digit year
    [InlineData("v2026.07")]       // missing counter
    [InlineData("latest")]
    [InlineData("")]
    public void TryParse_RejectsMalformedVersions(string input)
    {
        Assert.False(CalVerVersion.TryParse(input, out _));
    }

    [Fact]
    public void CompareTo_OrdersNumericallyNotLexicographically()
    {
        Assert.True(CalVerVersion.TryParse("v2026.07.2", out var v2));
        Assert.True(CalVerVersion.TryParse("v2026.07.10", out var v10));
        Assert.True(CalVerVersion.TryParse("v2026.08.1", out var nextMonth));
        Assert.True(CalVerVersion.TryParse("v2027.01.1", out var nextYear));

        // "v2026.07.10" < "v2026.07.2" as strings; numerically it must be greater.
        Assert.True(v10.CompareTo(v2) > 0);
        Assert.True(nextMonth.CompareTo(v10) > 0);
        Assert.True(nextYear.CompareTo(nextMonth) > 0);
    }
}

public class WorkflowPackageRefTests
{
    [Theory]
    [InlineData("general@v2026.07.1", "general", "v2026.07.1")]
    [InlineData("breast-oncology@latest", "breast-oncology", "latest")]
    public void TryParse_AcceptsWellFormedRefs(string input, string name, string version)
    {
        Assert.True(WorkflowPackageRef.TryParse(input, out var packageRef));
        Assert.Equal(name, packageRef!.Name);
        Assert.Equal(version, packageRef.Version);
        Assert.Equal(version == "latest", packageRef.IsLatest);
    }

    [Theory]
    [InlineData("general")]                 // no version
    [InlineData("general@")]                // empty version
    [InlineData("@v2026.07.1")]             // empty name
    [InlineData("General@v2026.07.1")]      // uppercase name
    [InlineData("general@v2026.7.1")]       // malformed version
    [InlineData("general@newest")]          // unknown symbolic version
    [InlineData("a@b@c")]
    [InlineData(null)]
    [InlineData("")]
    public void TryParse_RejectsMalformedRefs(string? input)
    {
        Assert.False(WorkflowPackageRef.TryParse(input, out _));
    }
}

