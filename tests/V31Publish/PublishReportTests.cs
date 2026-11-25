using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 14; part 07 § 9; D10): the publish report's JSON fits the pinned
/// <c>publish-report.schema.json</c>, in a published and a refused shape, and carries nothing the caller logs only.
/// </summary>
public class PublishReportTests
{
    private static readonly string Hash = "sha256:" + new string('a', 64);
    private static readonly string Other = "sha256:" + new string('b', 64);

    private static TestResult Pass(string id) => new(id, Hash, true, Other, null, 12);

    private static PublishReport Published() => new(
        "example-nested-note", "v2026.12.1", true, new DateTimeOffset(2026, 12, 1, 15, 4, 5, 987, TimeSpan.FromHours(2)), "api",
        Hash, Other, Hash, ["notes/a.md"],
        [], [new SourceRefusal("S04-073", "Up to 9 steps could run.", ""), new SourceRefusal("S07-014", "A stub is never used.", "tests/one.json")],
        [Pass("one")]);

    private static PublishReport Refused() => new(
        "example-nested-note", "v2026.12.1", false, null, "editor",
        Hash, null, null, [],
        [new SourceRefusal("A08-006", "The template couldn't be read.", "prompts/a.md") { EngineFault = "InvalidOperationException" }],
        [], [new TestResult("one", Hash, false, Other, new TestMismatch("values", "node.x", "1", "2"), 3)]);

    [Fact]
    public void APublishedReport_FitsTheSchema_AndSaysWhatItIs()
    {
        var json = Published().ToJson();

        Assert.True(ResolveFixture.PinnedSchema("publish-report").Validate(json.ToJsonString(), null));
        Assert.Equal(1, (int)json["documentVersion"]!);
        Assert.Equal("published", (string?)json["outcome"]);
        Assert.Equal("2026-12-01T13:04:05Z", (string?)json["publishedAtUtc"]);
        Assert.Equal("example-nested-note", (string?)json["package"]!["name"]);
        Assert.Equal("v2026.12.1", (string?)json["package"]!["version"]);
    }

    [Fact]
    public void ARefusedReport_FitsTheSchema_WithNoTimeAndNoHashesOfEvidence()
    {
        var json = Refused().ToJson();

        Assert.True(ResolveFixture.PinnedSchema("publish-report").Validate(json.ToJsonString(), null));
        Assert.Equal("refused", (string?)json["outcome"]);
        Assert.Null(json["publishedAtUtc"]);
        Assert.Null(json["resolveHash"]);
        Assert.Null(json["lockHash"]);
        Assert.True(json.ContainsKey("publishedAtUtc") && json.ContainsKey("resolveHash") && json.ContainsKey("lockHash"));
    }

    [Fact]
    public void WarningsAndRefusals_AreSentenceAndAt_WithAtNullForThePackageAsAWhole()
    {
        var warnings = Published().ToJson()["warnings"]!.AsArray();
        Assert.Equal(2, warnings.Count);
        Assert.Null(warnings[0]!["at"]);
        Assert.True(warnings[0]!.AsObject().ContainsKey("at"));
        Assert.Equal("Up to 9 steps could run.", (string?)warnings[0]!["sentence"]);
        Assert.Equal("tests/one.json", (string?)warnings[1]!["at"]);
        Assert.Equal(["at", "sentence"], warnings[1]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));

        var refusal = Refused().ToJson()["refusals"]!.AsArray().Single()!;
        Assert.Equal("prompts/a.md", (string?)refusal["at"]);
    }

    [Fact]
    public void ATestResult_IsWrittenAsTheTestResultWritesIt()
    {
        var report = Refused();
        Assert.Equal(report.Tests[0].ToJson().ToJsonString(), report.ToJson()["tests"]!.AsArray().Single()!.ToJsonString());
    }

    [Fact]
    public void TheCanonicalText_IsStable_AndIsTheCanonicalFormOfTheJson()
    {
        var report = Published();
        var text = report.ToCanonical();

        Assert.Equal(text, Published().ToCanonical());
        Assert.Equal(CanonicalJson.Write(report.ToJson()), text);
        Assert.DoesNotContain("\n", text);
        Assert.Equal(PublishReport.FileName, "publish-report.json");
        Assert.Equal("application/vnd.consultologist.publish-report+json", PublishReport.MediaType);
    }

    [Fact]
    public void AnEngineFaultsExceptionType_IsNotInTheReport()
    {
        var text = Refused().ToCanonical();

        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("EngineFault", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePublishedTime_IsWholeSecondsInUtc_WhateverTheOffsetOrTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var at = new DateTimeOffset(2026, 12, 1, 23, 59, 59, 999, TimeSpan.FromHours(-5));
            var json = (Published() with { PublishedAtUtc = at }).ToJson();
            Assert.Equal("2026-12-02T04:59:59Z", (string?)json["publishedAtUtc"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void TheSchemaCheck_IsReal_ARefusedReportWithATimeDoesNotFit()
    {
        var json = Refused().ToJson();
        json["publishedAtUtc"] = "2026-12-01T15:04:05Z";
        Assert.False(ResolveFixture.PinnedSchema("publish-report").Validate(json.ToJsonString(), null));
    }

    [Fact]
    public void ANullArgument_Throws_AndNothingElseDoes()
    {
        Assert.Throws<ArgumentNullException>(() => new PublishReport("n", "v", true, null, "api", Hash, null, null, null!, [], [], []).ToJson());
    }
}
