// The Responses SDK marks its types evaluation-only; the Api project
// suppresses OPENAI001 project-wide for the same reason.
#pragma warning disable OPENAI001
using System.ClientModel.Primitives;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using OpenAI.Responses;

namespace Consultologist.Api.Tests;

/// <summary>
/// #551: the capture seam — the provider's usage object becomes the record's
/// counts, and absence stays absence, never zero.
/// </summary>
public class TokenUsageTests
{
    [Fact]
    public void TheProvidersCounts_MapInputToInput_OutputToOutput()
    {
        // The SDK type has no public constructor; its own wire reader builds
        // it from the Responses contract's required shape.
        var usage = ModelReaderWriter.Read<ResponseTokenUsage>(BinaryData.FromString("""
            {"input_tokens":1234,"output_tokens":567,"total_tokens":1801,
             "input_tokens_details":{"cached_tokens":0},
             "output_tokens_details":{"reasoning_tokens":0}}
            """))!;

        Assert.Equal(new ConsultTokenUsage(1234, 567), AgentSectionGenerator.UsageOf(usage));
    }

    [Fact]
    public void NoUsage_IsNull_NeverZero()
    {
        Assert.Null(AgentSectionGenerator.UsageOf(null));
    }
}
