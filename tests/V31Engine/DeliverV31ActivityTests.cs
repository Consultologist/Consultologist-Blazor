using System.Text.Json;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; D10): <c>deliver-v31</c> makes one attempt of one send by its kind's channel;
/// a kind this build has no channel for is not set up, and a channel's fault is a failed send, never a throw. Its telemetry
/// names the send, never a document's text. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliverV31ActivityTests
{
    private const string Letter = "Dear Margaret Okafor, this is what we talked about. Dr A. Sample";

    [Theory]
    [InlineData("emr")]
    [InlineData("zip")]
    public async Task AKindWithNoChannel_IsNotSetUp(string kind)
    {
        var email = new Channel("email", _ => new DeliverySendOutcome(DeliverySendResult.Sent, null));
        var log = new CapturingLogger<DeliverV31Activity>();
        var activity = new DeliverV31Activity(new DeliveryChannels([email]), log);

        var outcome = await activity.RunAsync(Input(kind), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), outcome);
        Assert.Empty(email.Sends);
        Assert.Contains($"Kind={kind}", log.Everything, StringComparison.Ordinal);
        Assert.Contains("Result=NotSetUp", log.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChannelsOutcome_IsTheActivitys_AndAFault_IsAFailedSend()
    {
        var email = new Channel("email", _ => new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"));
        var pdf = new Channel("pdf", _ => throw new InvalidOperationException($"PDF render failed on {Letter}"));
        var log = new CapturingLogger<DeliverV31Activity>();
        var activity = new DeliverV31Activity(new DeliveryChannels([email, pdf]), log);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"), await activity.RunAsync(Input("email"), CancellationToken.None));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "error"), await activity.RunAsync(Input("pdf"), CancellationToken.None));
        Assert.Equal(Letter, Assert.Single(email.Sends).Send.Documents[0].Text);

        // the send is logged, never its text nor a fault's message
        Assert.Contains("ExceptionType=System.InvalidOperationException", log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", log.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInputAndTheOutcome_RoundTrip_AsDurableStoresThem()
    {
        var input = Input("pdf");
        var json = JsonSerializer.Serialize(input);
        var back = JsonSerializer.Deserialize<DeliverV31Input>(json)!;
        Assert.Equal((input.JobId, input.AppUserId, input.Send.Kind, input.Send.Bundle, input.Send.Folder, input.Send.Attempt),
            (back.JobId, back.AppUserId, back.Send.Kind, back.Send.Bundle, back.Send.Folder, back.Send.Attempt));
        Assert.Equal(input.Send.Documents, back.Send.Documents);

        foreach (var outcome in new[]
        {
            new DeliverySendOutcome(DeliverySendResult.Sent, null),
            new DeliverySendOutcome(DeliverySendResult.NotSetUp, null),
            new DeliverySendOutcome(DeliverySendResult.Failed, "too-large"),
            new DeliverySendOutcome(DeliverySendResult.Sent, null, DeliveryPlan.NoDocumentPassword),
        })
            Assert.Equal(outcome, FakeOrchestrationContext.RoundTrip<DeliverySendOutcome>(outcome));

        // #1507 (D18, D19): the start's delivery settings and a blocked job's send cross as they were made; an outcome or a
        // send written before they existed reads them as their defaults
        var blocked = input with { Send = input.Send with { EveryDeliverableBlocked = true }, Delivery = new V31DeliverySettings("dr.sample@example.com", false) };
        var again = FakeOrchestrationContext.RoundTrip<DeliverV31Input>(blocked);
        Assert.Equal((true, blocked.Delivery), (again.Send.EveryDeliverableBlocked, again.Delivery));
        Assert.Null(JsonSerializer.Deserialize<DeliverySendOutcome>("""{"Result":0,"ErrorKind":null}""")!.LinkOnly);
        Assert.False(JsonSerializer.Deserialize<DeliverySend>("""{"Kind":"pdf","Bundle":null,"Folder":"","Documents":[],"Attempt":1}""")!.EveryDeliverableBlocked);
    }

    private static DeliverV31Input Input(string kind) => new("job-31-0001", "user-1", new DeliverySend(kind, null, "",
        [new DeliveryDocument("deliverable.note", "Consultation note", "plaintext", Letter)], 2));

    /// <summary>A channel of <paramref name="kind"/> answering by <paramref name="answer"/>, keeping what it was asked.</summary>
    private sealed class Channel(string kind, Func<DeliverV31Input, DeliverySendOutcome> answer) : IDeliveryChannel
    {
        public List<DeliverV31Input> Sends { get; } = [];

        public string Kind => kind;

        public Task<DeliverySendOutcome> SendAsync(DeliverV31Input input, CancellationToken ct)
        {
            Sends.Add(input);
            return Task.FromResult(answer(input));
        }
    }
}
