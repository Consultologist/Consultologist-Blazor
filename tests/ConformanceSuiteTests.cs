using System.Text.Json;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #407: replays the conformance suite consultologist-package-format publishes,
/// and asserts this engine still produces exactly the outcomes it advertises.
///
/// This is the half that makes publishing the suite worth anything. The other
/// direction — an outside implementation checking itself — needs no code here.
/// This one is the first mechanism that would notice the specifications and the
/// validator enforcing them drifting apart; before this, nothing compared them.
///
/// Read off the submodule rather than the registry, for the same reason
/// SpecVersionSetTests does: a network call in the suite that gates every merge
/// fails when the network does, to check a fact that is committed in this tree.
/// Bumping the submodule pin is the deliberate act of adopting a newer suite.
///
/// Each case carries its own catalog schemas, so a case stays replayable
/// forever even as the output-contract catalog moves. A catalog change that
/// strands a package is a different failure, and #374 owns it.
/// </summary>
public class ConformanceSuiteTests
{
    private sealed record ExpectedOutcome(bool Valid, List<string> Errors, List<string> Warnings);

    private sealed record ConformanceCase(
        string Id,
        int SpecVersion,
        string Description,
        ExpectedOutcome Expect,
        WorkflowPackageManifest Manifest,
        Dictionary<string, string> Files);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string SuiteDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "external", "consultologist-package-format", "conformance");
    }

    public static TheoryData<string> PublishedCases()
    {
        var data = new TheoryData<string>();
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(SuiteDirectory(), "index.json")));

        foreach (var entry in index.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(entry.GetProperty("path").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PublishedCases))]
    public void ThePublishedOutcome_IsStillWhatThisEngineSays(string relativePath)
    {
        var suite = SuiteDirectory();
        var testCase = JsonSerializer.Deserialize<ConformanceCase>(
            File.ReadAllText(Path.Combine(suite, relativePath)), Json)!;

        var catalogSchemas = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(suite, "catalog-schemas.json")), Json)!;

        var result = WorkflowPackageValidator.Validate(testCase.Manifest, testCase.Files, catalogSchemas);

        // The description is in the message on purpose: a failure here is read
        // by whoever changed a rule, and "invalid-values-without-enum" alone
        // does not tell them which promise they broke.
        Assert.True(
            testCase.Expect.Valid == result.IsValid,
            $"{testCase.Id}: published validity {testCase.Expect.Valid}, engine says {result.IsValid}. "
            + $"{testCase.Description} Engine errors: {string.Join(" | ", result.Errors)}");

        // Order included. The suite was generated from this same code path, so a
        // reordering is a real change in how a package's problems are reported,
        // not noise to sort away.
        Assert.Equal(testCase.Expect.Errors, result.Errors);
        Assert.Equal(testCase.Expect.Warnings, result.Warnings);
    }

    [Fact]
    public void TheSuiteCovers_EverySpecVersionTheEngineRuns()
    {
        // A published format with no conformance case is one an author is told
        // to conform to with nothing to check against.
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(SuiteDirectory(), "index.json")));
        var covered = index.RootElement.GetProperty("cases").EnumerateArray()
            .Select(entry => entry.GetProperty("specVersion").GetInt32())
            .ToHashSet();

        Assert.Empty(WorkflowPackageStore.SupportedSpecVersions.Except(covered));
    }

    [Fact]
    public void TheSuiteIsMostly_CasesThatMustBeRejected()
    {
        // The ratio is the point, and it is easy to lose: valid fixtures are
        // pleasant to write and prove far less than a rejection naming its
        // reason. If this ever flips, the suite has stopped testing the rules.
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(SuiteDirectory(), "index.json")));
        var cases = index.RootElement.GetProperty("cases").EnumerateArray().ToList();
        var invalid = cases.Count(entry => !entry.GetProperty("valid").GetBoolean());

        Assert.True(invalid > cases.Count / 2, $"only {invalid} of {cases.Count} cases are rejections");
    }
}
