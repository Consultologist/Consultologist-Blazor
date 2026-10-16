using System.Net.ServerSentEvents;
using System.Threading.Channels;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1051: the catch-up events a stream materialises — their payloads omit
/// nulls and never carry the held INPUT text (an empty map keeps the client's
/// flag), and when the store refuses them the stream still sends them, without
/// ids, <c>done</c> included, each once per connection.
/// </summary>
public class ConsultGenerationTransportTests
{
    private static ConsultGenerationJobs Transport(IConsultGenerationJobEventStore store) => new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultGenerationJobs>.Instance,
        Substitute.For<Consultologist.Api.Auth.IAccountAuthorizer>(),
        store,
        Substitute.For<IConsultGenerationJobStarter>(),
        Substitute.For<Consultologist.Api.Email.IGraphMailClient>(),
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
        Substitute.For<Consultologist.Api.Auth.IAccountSettingsStore>(),
        Substitute.For<IJobOutputsBlobStore>(),
        Substitute.For<IJobInputsBlobStore>(),
        Substitute.For<IConsultGenerationLinkStore>());

    private static ConsultGenerationJobResponse Response(string status = ConsultGenerationJobStatuses.Completed, IReadOnlyDictionary<string, string>? heldInputs = null, string blockText = "The plan.")
    {
        var nodes = new[] { new ConsultNodeDescriptor("extract", "Extracting clinical concepts") };
        var outputs = new Dictionary<string, ConsultGenerationNodeStatusResponse>
        {
            ["extract"] = new("extract", "Extracting clinical concepts", ConsultGenerationNodeStatuses.Completed, "in", "out"),
        };

        return new ConsultGenerationJobResponse(
            "job-1", "user-1", status, 1, 1, 0,
            new Dictionary<string, string> { ["hpi"] = blockText }, new Dictionary<string, string>(), false,
            SchemaVersion: 3,
            Nodes: nodes,
            NodeOutputs: outputs,
            HeldInputs: heldInputs);
    }

    private static ConsultGenerationJobEventCandidate Candidate(ConsultGenerationJobResponse response, string key) =>
        ConsultGenerationJobs.CreateSemanticEventCandidates(response).Single(c => c.EventKey == key);

    [Fact]
    public void TheSnapshotAndDone_CarryAnEmptyHeldInputsMap_NeverTheInputText()
    {
        var held = new Dictionary<string, string> { ["notes"] = "Referral for Emily Lee, 62." };

        var snapshot = Candidate(Response(heldInputs: held), "snapshot");
        var done = Candidate(Response(heldInputs: held), "done");

        Assert.Contains("\"HeldInputs\":{}", snapshot.PayloadJson);
        Assert.Contains("\"HeldInputs\":{}", done.PayloadJson);
        Assert.DoesNotContain("Emily Lee", snapshot.PayloadJson);
        Assert.DoesNotContain("Emily Lee", done.PayloadJson);
    }

    [Fact]
    public void WithoutHeldInputs_ThePayloadHasNoHeldInputsMember_AndNoNulls()
    {
        var snapshot = Candidate(Response(), "snapshot");

        Assert.DoesNotContain("HeldInputs", snapshot.PayloadJson);
        Assert.DoesNotContain(":null", snapshot.PayloadJson);
        Assert.All(ConsultGenerationJobs.CreateSemanticEventCandidates(Response()), c => Assert.DoesNotContain(":null", c.PayloadJson));
    }

    [Fact]
    public void ALargeBlock_IsStillOneCandidate_TheStoreChunksIt()
    {
        var candidate = Candidate(Response(blockText: new string('x', 60_000)), "block-completed:hpi");

        Assert.True(candidate.PayloadJson.Length > 60_000);
        Assert.Equal(3, ConsultGenerationJobEventPayload.Split(candidate.PayloadJson).Count);
    }

    [Fact]
    public async Task WhenTheStoreRefuses_TheStreamSendsTheEventsWithoutIds_DoneLast_EachOnce()
    {
        var store = Substitute.For<IConsultGenerationJobEventStore>();
        store.AppendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<ConsultGenerationJobEventCandidate>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("1:The property value exceeds the maximum allowed size (64KB)."));
        var transport = Transport(store);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var connection = new ConsultGenerationJobs.SseConnectionState();

        var first = await transport.WriteMaterializedEventsAsync(channel.Writer, Response(), 0, connection, CancellationToken.None);
        var second = await transport.WriteMaterializedEventsAsync(channel.Writer, Response(), 0, connection, CancellationToken.None);
        channel.Writer.Complete();

        var items = new List<SseItem<string>>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        Assert.Equal(new[] { "snapshot", "node-completed", "block-completed", "done" }, items.Select(i => i.EventType));
        Assert.All(items, item => Assert.Null(item.EventId));
        Assert.Equal(4, first.EventCount);
        Assert.Null(first.LatestEventId);
        Assert.Equal(0, second.EventCount);
        Assert.True(connection.PersistenceFailureLogged);
    }

    [Fact]
    public async Task WhenTheStorePersists_TheStreamSendsStoredEventsWithIds_AsBefore()
    {
        var store = Substitute.For<IConsultGenerationJobEventStore>();
        store.AppendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<ConsultGenerationJobEventCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyList<ConsultGenerationJobEventCandidate>>(2);
                return candidates.Select((c, i) => new ConsultGenerationJobStoredEvent("job-1", "user-1", i + 1, c.EventType, c.EventKey, c.PayloadJson, DateTimeOffset.UtcNow)).ToList();
            });
        var transport = Transport(store);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var connection = new ConsultGenerationJobs.SseConnectionState();

        var result = await transport.WriteMaterializedEventsAsync(channel.Writer, Response(), 0, connection, CancellationToken.None);
        channel.Writer.Complete();

        var items = new List<SseItem<string>>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        Assert.Equal(4, result.EventCount);
        Assert.Equal("job-1:000000000004", result.LatestEventId);
        Assert.All(items, item => Assert.NotNull(item.EventId));
        Assert.Equal(4, result.HighestEmittedSequence);
        Assert.False(connection.PersistenceFailureLogged);
    }

    [Fact]
    public async Task AnEventSentUnpersisted_IsNotSentAgainWhenTheStoreRecovers()
    {
        var store = Substitute.For<IConsultGenerationJobEventStore>();
        var attempts = 0;
        store.AppendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<ConsultGenerationJobEventCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++attempts == 1)
                {
                    throw new InvalidOperationException("storage down");
                }

                var candidates = call.ArgAt<IReadOnlyList<ConsultGenerationJobEventCandidate>>(2);
                return candidates.Select((c, i) => new ConsultGenerationJobStoredEvent("job-1", "user-1", i + 1, c.EventType, c.EventKey, c.PayloadJson, DateTimeOffset.UtcNow)).ToList();
            });
        var transport = Transport(store);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var connection = new ConsultGenerationJobs.SseConnectionState();

        var running = Response(status: ConsultGenerationJobStatuses.Running);
        var first = await transport.WriteMaterializedEventsAsync(channel.Writer, running, 0, connection, CancellationToken.None);
        var second = await transport.WriteMaterializedEventsAsync(channel.Writer, Response(), first.HighestEmittedSequence, connection, CancellationToken.None);
        channel.Writer.Complete();

        var items = new List<SseItem<string>>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        // The first (refused) response's events went out without ids; when the
        // store recovers only what this connection has not sent goes out, stored.
        Assert.Equal(new[] { "snapshot", "node-completed", "block-completed", "done" }, items.Select(i => i.EventType));
        Assert.Equal(new[] { false, false, false, true }, items.Select(i => i.EventId is not null));
        Assert.Equal(1, second.EventCount);
        Assert.Equal(4, second.HighestEmittedSequence);
    }
}
