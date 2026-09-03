using Microsoft.AspNetCore.Http;

namespace Consultologist.Api.Tests;

public class FunctionCorsTests
{
    private static DefaultHttpContext CreateContext(string? origin)
    {
        var context = new DefaultHttpContext();
        if (origin != null)
        {
            context.Request.Headers.Origin = origin;
        }
        return context;
    }

    [Theory]
    [InlineData("https://app.consultologist.ai")]
    [InlineData("http://localhost:5000")]
    public void Apply_EchoesAllowedOrigin(string origin)
    {
        var context = CreateContext(origin);

        FunctionCors.Apply(context.Request, context.Response);

        Assert.Equal(origin, context.Response.Headers.AccessControlAllowOrigin);
        Assert.Equal("GET, POST, PUT, DELETE, OPTIONS", context.Response.Headers.AccessControlAllowMethods);
        Assert.Equal("Content-Type, Authorization, Last-Event-ID", context.Response.Headers.AccessControlAllowHeaders);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://localhost:9999")]
    [InlineData("")]
    public void Apply_IgnoresDisallowedOrigin(string origin)
    {
        var context = CreateContext(origin);

        FunctionCors.Apply(context.Request, context.Response);

        Assert.Empty(context.Response.Headers);
    }

    [Fact]
    public void Apply_IgnoresRequestWithoutOriginHeader()
    {
        var context = CreateContext(origin: null);

        FunctionCors.Apply(context.Request, context.Response);

        Assert.Empty(context.Response.Headers);
    }

    [Theory]
    [InlineData("https://app.consultologist.ai", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("https://app.consultologist.ai.evil.example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedOrigin_MatchesExactAllowListEntries(string? origin, bool expected)
    {
        Assert.Equal(expected, FunctionCors.IsAllowedOrigin(origin));
    }

    // #612: origins become configuration. The seam is asserted directly —
    // the AllowedOrigins property is process-cached, so these drive
    // WithConfigured, the function the cache is built from.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingSetting_ChangesNothing(string? setting)
    {
        Assert.Equal(FunctionCors.BaselineOrigins, FunctionCors.WithConfigured(setting));
    }

    [Fact]
    public void AConfiguredOrigin_Extends_AndEveryBaselineEntrySurvives()
    {
        var origins = FunctionCors.WithConfigured("https://satellite.example");

        Assert.Contains("https://satellite.example", origins);
        foreach (var baseline in FunctionCors.BaselineOrigins)
        {
            Assert.Contains(baseline, origins);
        }
    }

    [Fact]
    public void Separators_Trim_AndDuplicates()
    {
        // Semicolon is the documented form; comma and whitespace are
        // tolerated; a configured duplicate of a baseline entry appears
        // once — extends can never shrink or double the list.
        var origins = FunctionCors.WithConfigured(
            " https://a.example ;https://b.example,\nhttps://app.consultologist.ai");

        Assert.Contains("https://a.example", origins);
        Assert.Contains("https://b.example", origins);
        Assert.Equal(origins.Length, origins.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(FunctionCors.BaselineOrigins.Length + 2, origins.Length);
    }

    [Fact]
    public void AnOriginInNeitherList_IsStillRefused()
    {
        Assert.DoesNotContain("https://evil.example.com",
            FunctionCors.WithConfigured("https://satellite.example"));
    }
}
