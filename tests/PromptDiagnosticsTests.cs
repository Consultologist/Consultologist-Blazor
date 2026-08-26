using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>#245: the prompt-node console lines say how much, never what.</summary>
public class PromptDiagnosticsTests
{
    private const string Sentinel = "SENTINEL-CLINICAL-CONTENT-0f1e2d";
    private const string Text = "Past medical history:\n" + Sentinel + "\n\nPlan:\n- follow up";

    [Fact]
    public void Off_CarriesLengthAndDigestOnly_OnOneLine()
    {
        var line = PromptDiagnostics.Describe("AgentPrompt", "extract", Text, includeText: false);

        Assert.Equal($"[AgentPrompt] Stage=extract; Length={Text.Length}; Sha256={ConsultGenerationProvenance.Sha256Hex(Text)}", line);
        Assert.DoesNotContain(Sentinel, line, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void On_AppendsTheText()
    {
        var line = PromptDiagnostics.Describe("AgentResponse", "extract", Text, includeText: true);

        Assert.StartsWith($"[AgentResponse] Stage=extract; Length={Text.Length}; Sha256=", line);
        Assert.EndsWith("; Text=" + Text, line);
    }

    [Fact]
    public void TheDefault_IsOff()
    {
        // The environment of a test run never carries the setting; the
        // process-wide default is what production gets.
        Assert.Null(Environment.GetEnvironmentVariable(PromptDiagnostics.Setting));
        Assert.False(PromptDiagnostics.LogPromptText);
        Assert.DoesNotContain(Sentinel, PromptDiagnostics.Describe("AgentPrompt", "x", Text), StringComparison.Ordinal);
        Assert.Contains("OFF", PromptDiagnostics.StartupLine());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" true ", true)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheWordTrue_TurnsItOn(string? value, bool expected)
    {
        Assert.Equal(expected, PromptDiagnostics.IsOn(value));
    }

    [Fact]
    public void TheStartupLine_NamesTheStateAndTheSetting()
    {
        Assert.Contains("ON", PromptDiagnostics.StartupLine(true));
        Assert.Contains(PromptDiagnostics.Setting, PromptDiagnostics.StartupLine(true));
        Assert.Contains("never set this on production", PromptDiagnostics.StartupLine(true));
        Assert.Contains("OFF", PromptDiagnostics.StartupLine(false));
    }
}
