using System.ClientModel;
using System.ClientModel.Primitives;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.4; D7): how one attempt of an agent call ends, from what the SDK throws: a 429
/// is throttled, with the response's <c>retry-after-ms</c> or <c>retry-after</c>; a 5xx, a time-out and no connection are
/// service failures of their kind; anything else, and the host's own cancellation, propagate as today. And the back-off when
/// the service names no wait.
/// </summary>
public class AgentAttemptTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A429_IsThrottled_WithItsRetryAfterMs()
    {
        var attempt = AgentAttempt.Of(Failed(429, ("retry-after-ms", "1500"), ("retry-after", "9")), CancellationToken.None, Now);

        Assert.Equal(new AgentAttempt(AgentOutcomeKind.Throttled, null, null, 1500, null), attempt);
    }

    [Fact]
    public void A429_IsThrottled_WithItsRetryAfter_InSecondsOrAsADate()
    {
        Assert.Equal(2000, AgentAttempt.Of(Failed(429, ("retry-after", "2")), CancellationToken.None, Now)!.RetryAfterMilliseconds);
        Assert.Equal(30_000, AgentAttempt.Of(Failed(429, ("retry-after", Now.AddSeconds(30).ToString("R"))), CancellationToken.None, Now)!.RetryAfterMilliseconds);
    }

    [Fact]
    public void A429_WithNoWait_IsThrottled_WithNone()
    {
        Assert.Equal(new AgentAttempt(AgentOutcomeKind.Throttled, null, null, null, null), AgentAttempt.Of(Failed(429), CancellationToken.None, Now));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void A5xx_IsAServiceFailure_Error(int status)
    {
        Assert.Equal(new AgentAttempt(AgentOutcomeKind.ServiceFailure, null, null, null, "error"), AgentAttempt.Of(Failed(status), CancellationToken.None, Now));
    }

    [Fact]
    public void ATimeOut_IsAServiceFailure_Timeout()
    {
        var timedOut = new TaskCanceledException("The operation was cancelled because it exceeded the configured timeout.", new TimeoutException());

        Assert.Equal("timeout", AgentAttempt.Of(timedOut, CancellationToken.None, Now)!.ErrorKind);
        Assert.Equal(AgentOutcomeKind.ServiceFailure, AgentAttempt.Of(timedOut, CancellationToken.None, Now)!.Kind);
    }

    [Fact]
    public void NoConnection_IsAServiceFailure_Unreachable()
    {
        var refused = new HttpRequestException("Connection refused");

        Assert.Equal("unreachable", AgentAttempt.Of(refused, CancellationToken.None, Now)!.ErrorKind);
        // as System.ClientModel's transport wraps it
        Assert.Equal("unreachable", AgentAttempt.Of(new ClientResultException(refused.Message, null, refused), CancellationToken.None, Now)!.ErrorKind);
    }

    [Fact]
    public void AnythingElse_AndTheHostsOwnCancellation_Propagate()
    {
        Assert.Null(AgentAttempt.Of(Failed(400), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(Failed(404), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(new InvalidOperationException("Azure AI configuration missing"), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(new HttpRequestException("Forbidden", null, System.Net.HttpStatusCode.Forbidden), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(new TaskCanceledException(), new CancellationToken(canceled: true), Now));
    }

    [Fact]
    public void AnEmptyReply_IsACompletedAttempt_WithEmptyText()
    {
        // model output, never an engine fault: the runner's validation, failIfEmpty and onInvalid decide it
        Assert.Equal(new AgentAttempt(AgentOutcomeKind.Completed, "", null, null, null),
            AgentAttempt.Of(new InvalidOperationException("No assistant response found"), CancellationToken.None, Now));
        // only that exception: not another type with the message, not one wrapping another failure, not the host's cancellation
        Assert.Null(AgentAttempt.Of(new ObjectDisposedException("No assistant response found"), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(new InvalidOperationException("No assistant response found", new IOException()), CancellationToken.None, Now));
        Assert.Null(AgentAttempt.Of(new InvalidOperationException("No assistant response found"), new CancellationToken(canceled: true), Now));
    }

    [Fact]
    public void TheEmptyReplyMessage_IsTheOneSendPromptAsyncThrows()
    {
        // SendPromptAsync is unchanged for 5-30 and marks the case only by this message: pinned to its source
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Consultologist.Api", "Agents", "AgentSectionGenerator.cs"));

        Assert.Contains($"throw new InvalidOperationException(\"{AgentAttempt.EmptyReplyMessage}\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBackoff_StartsAt5Seconds_Doubles_IsCappedAt60_WithUpTo20PercentJitter()
    {
        Assert.Equal(5000, AgentBackoff.Wait(1, 0));
        Assert.Equal(5999, AgentBackoff.Wait(1, 0.9999));
        Assert.Equal(10_000, AgentBackoff.Wait(2, 0));
        Assert.Equal(20_000, AgentBackoff.Wait(3, 0));
        Assert.Equal(40_000, AgentBackoff.Wait(4, 0));
        Assert.Equal(60_000, AgentBackoff.Wait(5, 0));
        Assert.Equal(60_000, AgentBackoff.Wait(500, 0));
        Assert.Equal(72_000, AgentBackoff.Wait(500, 1));
    }

    /// <summary>What the SDK throws for a response of <paramref name="status"/> with <paramref name="headers"/>.</summary>
    internal static ClientResultException Failed(int status, params (string Name, string Value)[] headers) =>
        new(new FakeResponse(status, headers));

    private sealed class FakeResponse(int status, (string Name, string Value)[] headers) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => "Fictional";

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.FromString("");

        protected override PipelineResponseHeaders HeadersCore => new FakeHeaders(headers);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);

        public override void Dispose()
        {
        }
    }

    private sealed class FakeHeaders((string Name, string Value)[] headers) : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, out string? value)
        {
            value = headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();
            return value != null;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            var found = headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();
            values = found.Count > 0 ? found : null;
            return found.Count > 0;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            headers.Select(h => KeyValuePair.Create(h.Name, h.Value)).GetEnumerator();
    }
}
