using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 03 § 4.3; part 09 § 7.2; D6): <c>run.date</c> is the date the job's orchestration
/// started in the account's time zone, <c>run.package</c> the version run; a fact the plan does not read is not given.
/// </summary>
public class V31RunFactsTests
{
    private static readonly DateTimeOffset LateEvening = new(2026, 11, 17, 3, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RunDate_IsTheStartsDate_InTheAccountsZone()
    {
        var plan = Plan("run.date", "run.package");

        var facts = V31RunFacts.Of(plan, LateEvening, "America/Toronto");

        Assert.Equal(new JsonObject { ["date"] = "2026-11-16", ["package"] = $"{plan.Package}@{plan.Version}" }.ToJsonString(), facts.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Zone")]
    public void WithNoZone_OrOneUnknown_RunDate_IsUtcs(string? zone)
    {
        Assert.Equal("2026-11-17", (string?)V31RunFacts.Of(Plan("run.date"), LateEvening, zone)["date"]);
    }

    [Fact]
    public void AFactThePlanDoesNotRead_IsNotGiven()
    {
        Assert.Empty(V31RunFacts.Of(Plan(), LateEvening, "America/Toronto"));
        Assert.Equal(["package"], V31RunFacts.Of(Plan("run.package", "run.id"), LateEvening, "America/Toronto").Select(f => f.Key));
    }

    /// <summary>The nested-note golden's plan, reading <paramref name="facts"/>.</summary>
    private static RunPlan Plan(params string[] facts)
    {
        var pair = CompiledGoldenTests.Published(CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note"));
        var document = JsonNode.Parse(pair.Resolve)!.AsObject();
        document["run"] = new JsonArray(facts.Select(f => (JsonNode?)f).ToArray());
        var text = CanonicalJson.Write(document);
        var (plan, refusal) = RunPlan.Build(text, CanonicalJson.HashText(text), pair.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    }
}
