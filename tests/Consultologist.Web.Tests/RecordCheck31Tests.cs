using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests;

/// <summary>#1509 (Task 6): History's Verify at record storage 12, as the Api's ProvenanceRecordCheck does it, over the canonical document.</summary>
public class RecordCheck31Tests
{
    private const string Key = "deliverable.patient.patient_letter";
    private const string Text = "Dear Margaret Okafor, your results are normal.";

    private static string Document(int substitutionStart = 5)
    {
        var name = "Margaret Okafor";
        var document = new JsonObject
        {
            ["jobId"] = "job-31-0001",
            ["status"] = "Completed",
            ["deliverables"] = new JsonArray(new JsonObject
            {
                ["key"] = Key,
                ["composedHash"] = CanonicalJson.HashText(Text),
                ["substitutions"] = new JsonArray(new JsonObject
                {
                    ["start"] = substitutionStart,
                    ["size"] = name.Length,
                    ["hash"] = CanonicalJson.HashText(name),
                }),
            }),
        };
        return CanonicalJson.Write(document);
    }

    private static Dictionary<string, string?> Texts(string? text = Text) => new() { [Key] = text };

    private static RecordCheckResult Check(string document, string? hash, Dictionary<string, string?> texts) =>
        RecordCheck31.Check(hash, Encoding.UTF8.GetBytes(document), texts);

    [Fact]
    public void ASealedRecord_ChecksOut()
    {
        var document = Document();

        var result = Check(document, CanonicalJson.HashText(document), Texts());

        Assert.True(result.Ok);
        Assert.Equal(["recordHash", $"deliverables[{Key}].composedHash", $"deliverables[{Key}].replay"], result.Lines.Select(l => l.What));
        Assert.All(result.Lines, l => Assert.True(l.Ok));
    }

    [Fact]
    public void AByteChanged_FailsRecordHash()
    {
        var document = Document();
        var hash = CanonicalJson.HashText(document);

        var result = Check(document.Replace("Completed", "Completes", StringComparison.Ordinal), hash, Texts());

        Assert.False(result.Ok);
        Assert.False(Assert.Single(result.Lines, l => l.What == "recordHash").Ok);
    }

    [Fact]
    public void ADeliverablesTextChanged_FailsComposedHash_AndNamesTheDeliverableKey()
    {
        var document = Document();

        var result = Check(document, CanonicalJson.HashText(document), Texts(Text + " Edited."));

        Assert.False(result.Ok);
        var failed = Assert.Single(result.Lines, l => l.Ok == false);
        Assert.Contains(Key, failed.What, StringComparison.Ordinal);
        Assert.Contains("composedHash", failed.What, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubstitutionMoved_FailsTheReplay_WithRecordReplaysFault()
    {
        var document = Document(substitutionStart: 6);

        var result = Check(document, CanonicalJson.HashText(document), Texts());

        Assert.False(result.Ok);
        var failed = Assert.Single(result.Lines, l => l.Ok == false);
        Assert.Contains("replay", failed.What, StringComparison.Ordinal);
        Assert.Contains("its text does not hash to its hash", failed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DroppedText_IsNotAFailure()
    {
        var document = Document();

        var result = Check(document, CanonicalJson.HashText(document), Texts(null));

        Assert.True(result.Ok);
        var line = Assert.Single(result.Lines, l => l.What.Contains(Key, StringComparison.Ordinal));
        Assert.Null(line.Ok);
        Assert.Equal("text deleted; hash kept", line.Detail);
    }
}
