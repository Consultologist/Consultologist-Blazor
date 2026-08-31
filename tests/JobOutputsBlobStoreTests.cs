using System.Text.Json;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #557: the naming policy and the payload's wire shape — the parts of the
/// outputs store that are rules rather than plumbing.
/// </summary>
public class JobOutputsBlobStoreTests
{
    [Theory]
    [InlineData(SignInKinds.Organisation, "org-job-outputs")]
    [InlineData(SignInKinds.Personal, "personal-job-outputs")]
    // Null falls to personal — #517's own default; unreachable for stamped
    // accounts, but the rule exists rather than a throw.
    [InlineData(null, "personal-job-outputs")]
    [InlineData("", "personal-job-outputs")]
    public void TheContainer_IsTheAccountsKind(string? kind, string expected)
    {
        Assert.Equal(expected, JobOutputsBlobStore.ContainerFor(kind));
    }

    [Fact]
    public void TheName_IsAccountThenJob()
    {
        Assert.Equal("user-1/0123abcd.json", JobOutputsBlobStore.NameFor("user-1", "0123abcd"));
    }

    [Fact]
    public void ThePayload_RoundTrips_AndToleratesUnknownFields()
    {
        var payload = new JobOutputsPayload(
            JobOutputsPayload.CurrentVersion,
            null,
            new[] { new JobOutputsDocument("note", "Consultation note\n\nAppended.", "abc123") },
            new Dictionary<string, string> { ["note:hpi"] = "History." },
            new Dictionary<string, IReadOnlyList<ClinicalConcept>>
            {
                ["extract"] = new[] { new ClinicalConcept("Chest pain", "finding", "29857009", true, true, "draft") }
            });

        var json = JsonSerializer.Serialize(payload);
        var read = JsonSerializer.Deserialize<JobOutputsPayload>(json)!;
        Assert.Equal(1, read.Version);
        Assert.Equal("Consultation note\n\nAppended.", read.Documents!.Single().Text);
        Assert.Equal("History.", read.BlockTexts!["note:hpi"]);
        Assert.Equal("Chest pain", read.NodeConcepts!["extract"].Single().Term);

        // A future version's extra field reads back as today's shape.
        var withExtra = json[..^1] + ",\"FutureField\":true}";
        Assert.Equal(1, JsonSerializer.Deserialize<JobOutputsPayload>(withExtra)!.Version);
    }
}
