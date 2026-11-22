using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 3, 11; part 09 §§ 3.7, 6.2; D13, D16; Review Focus 4): a 31 job's record
/// served. The job response carries the header's 31 members (none on a 5–30 job, whose bytes are as they were); a row is
/// fetched by its key with the text it is still owed, each member only when it hashes to the row's hash for it, and says so
/// once the text is dropped; the record downloads as the bytes sealed (sealing a terminal job that is not); and each stored
/// row is announced once, by key, as <c>row-finished</c>.
/// </summary>
public class RecordRoutesTests
{
    private const string UserId = "user-1";

    // ----- the job response's header -----------------------------------------------------------------------------------

    [Fact]
    public async Task A31JobsResponse_ShowsItsHeader()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var state = job.State;

        var json = JsonNode.Parse(JsonSerializer.Serialize(state.ToResponse()))!.AsObject();

        Assert.Equal(state.ResolveHash, (string?)json["ResolveHash"]);
        Assert.Equal(state.LockHash, (string?)json["LockHash"]);
        Assert.StartsWith("sha256:", (string?)json["ResolveHash"], StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(state.VersionsRan, json["VersionsRan"]));
        Assert.True(JsonNode.DeepEquals(state.RunFacts, json["RunFacts"]));
        Assert.Equal(state.Limits!.Count, json["Limits"]!.AsObject().Count);
        Assert.Equal(10, json["Limits"]!.AsObject().Count);
        Assert.Equal(state.RecordHash, (string?)json["RecordHash"]);
        Assert.NotNull(state.RecordHash);
        Assert.Equal(state.V31Progress!.Rows, (int?)json["Rows"]);
        Assert.Equal(state.V31Progress.Decisions, (int?)json["Decisions"]);
        Assert.Equal(state.V31Progress.CurrentKey, (string?)json["CurrentKey"]);
        Assert.Equal(state.V31Progress.States.Count, json["RowStates"]!.AsObject().Count);
        foreach (var (name, count) in state.V31Progress.States)
            Assert.Equal(count, (int?)json["RowStates"]![name]);

        // a package that reads no signature: the two members are left out, as the other nulls of 31 are
        Assert.False(json.ContainsKey("SignatureBlockId"));
        Assert.False(json.ContainsKey("SignatureChosen"));
    }

    private static readonly string[] HeaderNames =
    [
        "ResolveHash", "LockHash", "VersionsRan", "RunFacts", "Limits", "SignatureBlockId", "SignatureChosen", "RecordHash",
        "Rows", "RowStates", "Decisions", "CurrentKey",
    ];

    [Fact]
    public void A30JobsResponse_IsUnchanged()
    {
        var state = ConsultGenerationJobState.Create("job-30-0001", UserId, [new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "History" }]);
        state.CreatedAtUtc = new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero);
        state.PackageSpecVersion = 30;
        state.SchemaVersion = 11;
        state.Status = ConsultGenerationJobStatuses.Completed;
        state.Blocks["hpi"].Status = ConsultGenerationBlockStatuses.Completed;
        state.Blocks["hpi"].GeneratedText = "Dr A. Sample saw Margaret Okafor.";

        var json = JsonSerializer.Serialize(state.ToResponse());

        foreach (var name in HeaderNames)
            Assert.DoesNotContain($"\"{name}\"", json, StringComparison.Ordinal);

        // the bytes a storage-11 response wrote before the 31 members were added
        Assert.Equal(
            """{"JobId":"job-30-0001","AppUserId":"user-1","Status":"Completed","TotalBlockCount":1,"CompletedBlockCount":1,"FailedBlockCount":0,"GeneratedBlocks":{"hpi":"Dr A. Sample saw Margaret Okafor."},"FailedBlocks":{},"Success":true,"SchemaVersion":11,"AnalysisStatus":null,"AnalysisError":null,"CompletedStageCount":0,"TotalStageCount":0,"ItemProgress":{},"RuntimeFailureStage":null,"RuntimeFailureError":null,"CreatedAtUtc":"2026-11-16T14:00:00+00:00","StartedAtUtc":null,"CompletedAtUtc":null,"History":null,"WorkflowPackage":null,"EffectiveInputHash":null,"ItemSteps":null,"Nodes":null,"NodeOutputs":null,"AgentVersions":null,"EffectiveInputHashVersion":null,"CatalogRef":null,"WorkflowOutputHash":"da5971bb7adf9243a89c8510c432995b8e729f2d321516f3c68998334f863bd1","WorkflowOutputHashVersion":1,"AssembledDocument":null,"Source":null,"ScheduledAtUtc":null,"AssembledDocuments":null,"InputOrigins":null,"ResolvedInputTypes":null,"SkippedDocuments":null,"Collections":null,"PackageSpecVersion":30,"PackageTitle":null,"StartFailure":null,"PackageTags":null,"PackageFormatRef":null,"ProvenanceRef":null,"Terminology":null,"TerminologyServerRef":null,"TextDroppedAtUtc":null,"DeliveryOutcome":null,"DeliveredAtUtc":null,"DeliveryDocumentAttached":null,"Deciding":null,"DecidedAtUtc":null,"Classifications":null,"DecisionFailureKind":null,"ApiHost":null,"EngineCommit":null,"EngineRelease":null,"TerminologyServerRelease":null,"OutputsBlob":null,"InputsBlob":null,"InputsDroppedAtUtc":null,"HeldInputs":null,"RerunOf":null,"RerunVerdict":null,"RerunDivergence":null,"Tokens":null,"MacroChoices":null,"FailedDocuments":null,"ExcludedMacros":null,"FailureStack":null,"FailedBlockStacks":null,"AcknowledgedShortInputs":null,"FormatsRef":null,"SkippedBlocks":null,"UnresolvedLoops":null,"HeldDocuments":null}""",
            json);
    }

    // ----- a row by its key --------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARow_IsFetchedByItsKey()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var routes = new Routes(job);

        foreach (var key in new[] { "node.refine.judge#2", "node.sections.paragraphs.draft_paragraph:1/2", "deliverable.note" })
        {
            var stored = (await job.Rows.ReadAsync(V31Job.JobId, key, CancellationToken.None))!;
            var response = await routes.RowsAsync(key);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonNode.Parse(FakeHttp.TextOf(response))!.AsObject();
            Assert.Equal(["key", "section", "row", "text", "textDeleted"], body.Select(p => p.Key));
            Assert.Equal(key, (string?)body["key"]);
            Assert.Equal(stored.Section, (string?)body["section"]);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stored.Json), body["row"]), "the row is the stored row's JSON object");
            Assert.False((bool)body["textDeleted"]!);

            var text = body["text"]!.AsObject();
            var hashOf = stored.Section == "deliverable" ? "composedHash" : "outputHash";
            Assert.Equal((string?)body["row"]![hashOf], CanonicalJson.HashText((string)text["content"]!));
        }

        // a check row offers its other members once its content passes
        var checkedJob = V31Job.Of("checked-note");
        await V31Job.RunAsync(checkedJob.Context());
        var check = JsonNode.Parse(FakeHttp.TextOf(await new Routes(checkedJob).RowsAsync("check.part_coverage.coverage:1")))!;
        Assert.Equal("check", (string?)check["section"]);
        Assert.Equal(["content", "itemName", "uncovered", "untested", "value"], check["text"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));

        // a decision and a delivery row have no text
        var decision = JsonNode.Parse(FakeHttp.TextOf(await routes.RowsAsync("node.refine/if:pass.1")))!;
        Assert.Equal("decision", (string?)decision["section"]);
        Assert.Null(decision["text"]);
        var delivery = JsonNode.Parse(FakeHttp.TextOf(await new Routes(checkedJob).RowsAsync("delivery:deliverable.patient.patient_letter/email")))!;
        Assert.Equal("delivery", (string?)delivery["section"]);
        Assert.Null(delivery["text"]);
    }

    [Fact]
    public async Task TheRows_AreListedInKeyOrder_WithoutText()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var stored = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);

        var response = await new Routes(job).RowsAsync(null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(FakeHttp.TextOf(response))!.AsObject();
        Assert.Equal(["rows"], body.Select(p => p.Key));
        var rows = body["rows"]!.AsArray();
        Assert.Equal(stored.Select(r => r.Key).Order(StringComparer.Ordinal), rows.Select(r => (string)r!["key"]!));
        Assert.Contains(rows, r => (string?)r!["section"] == "decision");
        Assert.All(rows, r => Assert.Equal(["key", "section", "row"], r!.AsObject().Select(p => p.Key)));
        Assert.DoesNotContain("Stub reply", FakeHttp.TextOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARowOfAnotherAccountsJob_IsNotFound()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());

        var routes = new Routes(job) { SignedIn = AdminDrainTests.Admin.Account("user-2") };

        Assert.Equal(HttpStatusCode.NotFound, (await routes.RowsAsync("node.plan")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await routes.RowsAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await routes.RecordAsync()).StatusCode);
        Assert.Equal(0, job.Rows.Table.Calls - routes.TableCallsAtStart);

        // and a job that does not exist, and a 5–30 job
        Assert.Equal(HttpStatusCode.NotFound, (await new Routes(job) { Missing = true }.RowsAsync("node.plan")).StatusCode);
        job.State.SchemaVersion = 11;
        Assert.Equal(HttpStatusCode.NotFound, (await new Routes(job).RowsAsync("node.plan")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await new Routes(job).RecordAsync()).StatusCode);
    }

    [Fact]
    public async Task AnUnknownKey_Is404()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());

        var response = await new Routes(job).RowsAsync("node.nothing#9:1/x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("The job has no row with that key.", ErrorOf(response));
    }

    [Fact]
    public async Task ARowsText_IsOfferedOnlyWhenItHashes()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        const string key = "node.refine.judge#2";
        var routes = new Routes(job);
        Assert.NotNull(JsonNode.Parse(FakeHttp.TextOf(await routes.RowsAsync(key)))!["text"]!["value"]);

        // a late write after a cancel: settled text that is not what the row's hash says
        await job.Blobs.WriteTextAsync(V31Job.AccountKind, UserId, V31Job.JobId, key, "settled",
            new JsonObject { ["content"] = "A late reply about Margaret Okafor.", ["value"] = "{\"verdict\":\"late\"}" }, CancellationToken.None);

        var body = JsonNode.Parse(FakeHttp.TextOf(await routes.RowsAsync(key)))!;

        Assert.NotNull(body["row"]!["outputHash"]);
        Assert.Null(body["text"]);
        Assert.DoesNotContain("Okafor", body.ToJsonString(), StringComparison.Ordinal);

        // a prompt as sent is offered against inputHash: one that does not hash is left out, one that does is offered
        var rowJson = JsonNode.Parse((await job.Rows.ReadAsync(V31Job.JobId, key, CancellationToken.None))!.Json)!.AsObject();
        var content = "{\"verdict\":\"good\"}";
        rowJson["outputHash"] = CanonicalJson.HashText(content);
        rowJson["inputHash"] = CanonicalJson.HashText("The prompt as sent.");
        var text = new JsonObject { ["content"] = content, ["sent"] = "The prompt as sent.", ["value"] = content };
        Assert.Equal(["content", "sent", "value"], RecordRoutes.OfferedText("node", rowJson, text)!.Select(p => p.Key));
        text["sent"] = "Another prompt.";
        Assert.Equal(["content", "value"], RecordRoutes.OfferedText("node", rowJson, text)!.Select(p => p.Key));

        // a row that did not complete offers no member but those its hashes vouch for
        rowJson["status"] = "Failed";
        Assert.Equal(["content"], RecordRoutes.OfferedText("node", rowJson, text)!.Select(p => p.Key));
        rowJson["outputHash"] = null;
        Assert.Null(RecordRoutes.OfferedText("node", rowJson, text));
    }

    [Fact]
    public async Task AfterTheDrop_TheRowSaysItsTextWasDeleted()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var sealedHash = job.State.RecordHash;
        await job.Blobs.DeleteTextAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None);
        await job.Entity.DropText(new ConsultGenerationTextDrop(new DateTimeOffset(V31Job.Now.AddDays(7))));
        Assert.NotNull(job.State.TextDroppedAtUtc);
        var stored = (await job.Rows.ReadAsync(V31Job.JobId, "node.plan", CancellationToken.None))!;

        var body = JsonNode.Parse(FakeHttp.TextOf(await new Routes(job).RowsAsync("node.plan")))!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stored.Json), body["row"]));
        Assert.StartsWith("sha256:", (string?)body["row"]!["outputHash"], StringComparison.Ordinal);
        Assert.Null(body["text"]);
        Assert.True((bool)body["textDeleted"]!);

        // the record still downloads, and still hashes to recordHash
        var record = await new Routes(job).RecordAsync();
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        Assert.Equal(sealedHash, CanonicalJson.HashText(FakeHttp.TextOf(record)));
    }

    // ----- the record --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheRecord_DownloadsAsItsSealedBytes()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var stored = await job.Blobs.ReadRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None);

        var response = await new Routes(job).RecordAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        var bytes = ((MemoryStream)response.Body).ToArray();
        Assert.Equal(new UTF8Encoding(false).GetBytes(stored!), bytes);
        Assert.Equal(job.State.RecordHash, CanonicalJson.HashText(Encoding.UTF8.GetString(bytes)));
        Assert.Equal(job.State.RecordHash, Header(response, "X-Record-Hash"));
        Assert.Equal("application/json", Header(response, "Content-Type"));
        Assert.Equal($"attachment; filename=\"record-{V31Job.JobId}.json\"", Header(response, "Content-Disposition"));

        // a job still running: not yet
        var running = V31Job.Of("nested-note");
        running.State.Status = ConsultGenerationJobStatuses.Running;
        var refused = await new Routes(running).RecordAsync();
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The job hasn't finished.", ErrorOf(refused));
        running.State.Status = ConsultGenerationJobStatuses.Delivering;
        Assert.Equal(HttpStatusCode.Conflict, (await new Routes(running).RecordAsync()).StatusCode);
    }

    [Fact]
    public async Task ACancelledJobNeverSealed_IsSealedByTheRequest()
    {
        var job = V31Job.Of("nested-note");
        job.State.Status = ConsultGenerationJobStatuses.Scheduled;
        await job.Entity.Cancel();
        Assert.Null(job.State.RecordHash);
        var routes = new Routes(job);

        var response = await routes.RecordAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(job.State.RecordHash);
        Assert.Equal(job.State.RecordHash, Header(response, "X-Record-Hash"));
        Assert.Equal(job.State.RecordHash, CanonicalJson.HashText(FakeHttp.TextOf(response)));
        Assert.Equal(ConsultGenerationJobStatuses.Cancelled, (string?)JsonNode.Parse(FakeHttp.TextOf(response))!["status"]);
        Assert.Single(routes.Seals);

        // asked again: the same bytes, and no second seal
        var again = await routes.RecordAsync();
        Assert.Equal(FakeHttp.TextOf(response), FakeHttp.TextOf(again));
        Assert.Single(routes.Seals);
    }

    [Fact]
    public async Task TheRecord_IsServedAgain_AfterItsBlobWasDeleted()
    {
        // #1506 (Task 15): the text account's 30-day lifecycle policy deleted record.json; the rows and the stamp remain
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var stored = await job.Blobs.ReadRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None);
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));
        var routes = new Routes(job);

        var response = await routes.RecordAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stored, FakeHttp.TextOf(response));
        Assert.Equal(job.State.RecordHash, Header(response, "X-Record-Hash"));
        Assert.Equal(stored, await job.Blobs.ReadRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None));
        Assert.Empty(routes.Seals);
    }

    [Fact]
    public async Task ARowsStorageFault_OrACorruptRow_IsAServerError_WithCors()
    {
        // #1506 (final review M1): the rows route answers as the record route does: a JSON error a page can read
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        const string origin = "https://app.consultologist.ai";
        var log = new CapturingLogger<RecordRoutes>();
        var down = Substitute.For<IRecordRowStore>();
        down.ReadAsync(default!, default!, default).ReturnsForAnyArgs<Task<Consultologist.PackageFormat.V31.Run.RecordRow?>>(_ => throw new InvalidOperationException("The table service is down."));
        down.ListAsync(default!, default).ReturnsForAnyArgs<Task<IReadOnlyList<Consultologist.PackageFormat.V31.Run.RecordRow>>>(_ => throw new InvalidOperationException("The table service is down."));
        var faulty = new Routes(job, log, down);

        foreach (var response in new[] { await faulty.RowsAsync("node.plan", "GET", origin), await faulty.RowsAsync(null, "GET", origin) })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(origin, Header(response, "Access-Control-Allow-Origin"));
            Assert.Equal("The record isn't available yet. Try again.", ErrorOf(response));
        }

        Assert.All(log.Entries, e => Assert.Equal(
            $"V31 rows not available. JobId={V31Job.JobId}, ExceptionType={typeof(InvalidOperationException).FullName}", e.Message));
        Assert.Equal(2, log.Entries.Count);

        // a stored row that is not JSON: the same answer, and its text never logged
        await job.Rows.WriteAsync(V31Job.JobId,
            [new Consultologist.PackageFormat.V31.Run.RecordRow("node", "node.corrupt", "{\"Margaret Okafor (fictional)")], CancellationToken.None);
        var corruptLog = new CapturingLogger<RecordRoutes>();
        var routes = new Routes(job, corruptLog);
        foreach (var response in new[] { await routes.RowsAsync("node.corrupt", "GET", origin), await routes.RowsAsync(null, "GET", origin) })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(origin, Header(response, "Access-Control-Allow-Origin"));
        }

        Assert.DoesNotContain("Okafor", corruptLog.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordThatCannotBeRestored_IsAServerError_SaidPlainly()
    {
        // the blob gone and a row that is not the sealed record's: never a body of nothing, never another document
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var log = new CapturingLogger<RecordRoutes>();
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));
        await job.Rows.WriteAsync(V31Job.JobId,
            [new Consultologist.PackageFormat.V31.Run.RecordRow("decision", "decision:zz.extra", "{\"id\":\"decision:zz.extra\"}")],
            CancellationToken.None);

        var response = await new Routes(job, log).RecordAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("The record isn't available yet. Try again.", ErrorOf(response));
        var warning = Assert.Single(log.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Equal($"V31 record not available. JobId={V31Job.JobId}, ExceptionType={typeof(InvalidOperationException).FullName}", warning.Message);
        Assert.Null(await job.Blobs.ReadRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None));
    }

    // ----- CORS --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheNewRoutes_AnswerOptions_WithCors()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var routes = new Routes(job) { SignedIn = null };
        const string origin = "https://app.consultologist.ai";

        foreach (var preflight in new[] { await routes.RowsAsync("node.plan", "OPTIONS", origin), await routes.RecordAsync("OPTIONS", origin) })
        {
            Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
            Assert.Equal(origin, Header(preflight, "Access-Control-Allow-Origin"));
        }

        // every answer carries CORS: a refusal, a row, the record (whose hash header a page may read)
        Assert.Equal(origin, Header(await routes.RowsAsync("node.plan", "GET", origin), "Access-Control-Allow-Origin"));
        routes.SignedIn = AdminDrainTests.Admin.Account(UserId);
        Assert.Equal(origin, Header(await routes.RowsAsync("node.plan", "GET", origin), "Access-Control-Allow-Origin"));
        Assert.Equal(origin, Header(await routes.RowsAsync("node.unknown", "GET", origin), "Access-Control-Allow-Origin"));
        var record = await routes.RecordAsync("GET", origin);
        Assert.Equal(origin, Header(record, "Access-Control-Allow-Origin"));
        Assert.Contains("X-Record-Hash", Header(record, "Access-Control-Expose-Headers"), StringComparison.Ordinal);
    }

    // ----- row-finished ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheStream_ListsTheRows_OnlyWhenTheirCountMoves_AndAtTheEnd()
    {
        // #1506 (final review F6): per connection, the row count (and the terminal flag) of the last listing; an unchanged
        // count lists nothing, a changed one lists again, and the terminal materialisation always lists
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var rows = new FakeRecordRowStore();
        await rows.WriteAsync(V31Job.JobId, await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None), CancellationToken.None);
        var transport = Transport(new MemoryEventStore(), rows);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var connection = new ConsultGenerationJobs.SseConnectionState();
        var done = job.State.ToResponse();
        var running = done with { Status = ConsultGenerationJobStatuses.Running, Rows = 3 };
        var lists = 0;
        async Task MaterialiseAsync(ConsultGenerationJobResponse response)
        {
            var before = rows.Table.Calls;
            await transport.WriteMaterializedEventsAsync(channel.Writer, response, 0, connection, CancellationToken.None);
            lists += rows.Table.Calls - before;
        }

        await MaterialiseAsync(running);
        await MaterialiseAsync(running);
        Assert.Equal(1, lists);

        await MaterialiseAsync(running with { Rows = 4 });
        Assert.Equal(2, lists);

        await MaterialiseAsync(done with { Rows = 4 });
        Assert.Equal(3, lists);
    }

    [Fact]
    public async Task APoll_ListsNoRows_ForAJobWithNoneYet()
    {
        // #1506 (final review F6): the stateless materialisation skips the listing while the job has no stored row
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var rows = new FakeRecordRowStore();
        var transport = Transport(new MemoryEventStore(), rows);
        var response = job.State.ToResponse() with { Status = ConsultGenerationJobStatuses.Running };

        await transport.TryMaterializeEventsForPollingAsync(response with { Rows = null }, CancellationToken.None);
        await transport.TryMaterializeEventsForPollingAsync(response with { Rows = 0 }, CancellationToken.None);
        Assert.Equal(0, rows.Table.Calls);

        await transport.TryMaterializeEventsForPollingAsync(response with { Rows = 1 }, CancellationToken.None);
        Assert.Equal(1, rows.Table.Calls);
    }

    [Fact]
    public async Task EachStoredRow_IsAnnouncedOnce()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var all = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        var growing = new FakeRecordRowStore();
        var events = new MemoryEventStore();
        var transport = Transport(events, growing);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var connection = new ConsultGenerationJobs.SseConnectionState();
        var response = job.State.ToResponse();

        // the first half stored, the job running; then every row, the job done
        await growing.WriteAsync(V31Job.JobId, all.Take(all.Count / 2).ToList(), CancellationToken.None);
        var first = await transport.WriteMaterializedEventsAsync(
            channel.Writer, response with { Status = ConsultGenerationJobStatuses.Running }, 0, connection, CancellationToken.None);
        await growing.WriteAsync(V31Job.JobId, all, CancellationToken.None);
        await transport.WriteMaterializedEventsAsync(channel.Writer, response, first.HighestEmittedSequence, connection, CancellationToken.None);
        // a GET's materialisation adds nothing more
        await transport.TryMaterializeEventsForPollingAsync(response, CancellationToken.None);
        channel.Writer.Complete();

        var items = new List<SseItem<string>>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        var announced = items.Where(i => i.EventType == "row-finished").ToList();
        var rows = all.Where(r => r.Section != "decision").ToList();
        Assert.Equal(rows.Count, announced.Count);
        Assert.Equal(rows.Count, events.Stored.Count(e => e.EventType == "row-finished"));
        Assert.Equal(rows.Select(r => r.Key).Order(StringComparer.Ordinal), announced.Select(i => (string)JsonNode.Parse(i.Data)!["key"]!).Order(StringComparer.Ordinal));
        Assert.All(events.Stored.Where(e => e.EventType == "row-finished"), e => Assert.Equal($"row:{(string)JsonNode.Parse(e.PayloadJson)!["key"]!}", e.EventKey));
        foreach (var item in announced)
        {
            var payload = JsonNode.Parse(item.Data)!.AsObject();
            Assert.Equal(["key", "section", "status"], payload.Select(p => p.Key));
            var row = rows.Single(r => r.Key == (string)payload["key"]!);
            var stored = JsonNode.Parse(row.Json)!;
            Assert.Equal(row.Section, (string?)payload["section"]);
            Assert.Equal(row.Section switch { "deliverable" => (string?)stored["state"], "delivery" => (string?)stored["result"], _ => (string?)stored["status"] }, (string?)payload["status"]);
        }

        Assert.Equal("released", announced.Select(i => JsonNode.Parse(i.Data)!).Single(p => (string?)p["key"] == "deliverable.note")["status"]!.GetValue<string>());

        // in key order within each materialisation, and every one before done
        var firstBatch = announced.Take(rows.Count(r => all.Take(all.Count / 2).Contains(r))).Select(i => (string)JsonNode.Parse(i.Data)!["key"]!).ToList();
        Assert.Equal(firstBatch.Order(StringComparer.Ordinal), firstBatch);
        var types = items.Select(i => i.EventType).ToList();
        Assert.Equal("done", types[^1]);
        Assert.True(types.LastIndexOf("row-finished") < types.IndexOf("done"));
    }

    [Fact]
    public async Task A30JobsMaterialisation_ListsNoRows()
    {
        var rows = new FakeRecordRowStore();
        var transport = Transport(new MemoryEventStore(), rows);
        var channel = Channel.CreateUnbounded<SseItem<string>>();
        var response = new ConsultGenerationJobResponse(
            V31Job.JobId, UserId, ConsultGenerationJobStatuses.Completed, 1, 1, 0,
            new Dictionary<string, string> { ["hpi"] = "The plan." }, new Dictionary<string, string>(), true, SchemaVersion: 11);

        await transport.WriteMaterializedEventsAsync(channel.Writer, response, 0, new ConsultGenerationJobs.SseConnectionState(), CancellationToken.None);
        await transport.TryMaterializeEventsForPollingAsync(response, CancellationToken.None);

        Assert.Equal(0, rows.Table.Calls);
    }

    // ----- the harness -------------------------------------------------------------------------------------------------

    /// <summary>The body's one member, <c>error</c>.</summary>
    private static string? ErrorOf(HttpResponseData response)
    {
        var body = JsonNode.Parse(FakeHttp.TextOf(response))!.AsObject();
        Assert.Equal(["error"], body.Select(p => p.Key));
        return (string?)body["error"];
    }

    private static string? Header(HttpResponseData response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    private static ConsultGenerationJobs Transport(IConsultGenerationJobEventStore events, IRecordRowStore rows) => new(
        NullLogger<ConsultGenerationJobs>.Instance,
        Substitute.For<IAccountAuthorizer>(),
        events,
        Substitute.For<IConsultGenerationJobStarter>(),
        Substitute.For<Consultologist.Api.Email.IGraphMailClient>(),
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
        Substitute.For<IAccountSettingsStore>(),
        Substitute.For<IJobOutputsBlobStore>(),
        Substitute.For<IJobInputsBlobStore>(),
        Substitute.For<IConsultGenerationLinkStore>(),
        recordRows: rows);

    /// <summary>The event store's rule: one event per key, the first kept; an append answers every candidate's stored event.</summary>
    private sealed class MemoryEventStore : IConsultGenerationJobEventStore
    {
        public List<ConsultGenerationJobStoredEvent> Stored { get; } = [];

        public Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> AppendAsync(
            string jobId, string appUserId, IReadOnlyList<ConsultGenerationJobEventCandidate> candidates, CancellationToken cancellationToken)
        {
            var answered = new List<ConsultGenerationJobStoredEvent>();
            foreach (var candidate in candidates)
            {
                var existing = Stored.FirstOrDefault(e => e.EventKey == candidate.EventKey);
                if (existing == null)
                {
                    existing = new ConsultGenerationJobStoredEvent(jobId, appUserId, Stored.Count + 1, candidate.EventType, candidate.EventKey, candidate.PayloadJson, DateTimeOffset.UnixEpoch);
                    Stored.Add(existing);
                }

                answered.Add(existing);
            }

            return Task.FromResult<IReadOnlyList<ConsultGenerationJobStoredEvent>>(answered.OrderBy(e => e.Sequence).ToList());
        }

        public Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> ReadAfterAsync(string jobId, string appUserId, long sequence, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConsultGenerationJobStoredEvent>>(Stored.Where(e => e.Sequence > sequence).ToList());

        public Task<int> DeleteJobAsync(string jobId, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    /// <summary>The two functions over the job's entity (read and sealed as the Durable client would) and its stores.</summary>
    private sealed class Routes
    {
        private readonly V31Job _job;
        private readonly RecordRoutes _functions;

        public Routes(V31Job job, CapturingLogger<RecordRoutes>? log = null, IRecordRowStore? rows = null)
        {
            _job = job;
            TableCallsAtStart = job.Rows.Table.Calls;
            var authorizer = Substitute.For<IAccountAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(_ => SignedIn);
            var accounts = Substitute.For<IAccountStore>();
            accounts.GetAccountKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(V31Job.AccountKind);

            var entities = Substitute.For<DurableEntityClient>("test");
            Client.Entities.Returns(entities);
            entities.GetEntityAsync<ConsultGenerationJobState>(Arg.Any<EntityInstanceId>(), Arg.Any<CancellationToken>())
                .Returns(call => Missing || ((EntityInstanceId)call[0]).Key != V31Job.JobId
                    ? null
                    : new EntityMetadata<ConsultGenerationJobState>((EntityInstanceId)call[0], _job.State));
            entities.SignalEntityAsync(Arg.Any<EntityInstanceId>(), Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<SignalEntityOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Assert.Equal(nameof(ConsultGenerationJobEntity.RecordV31Seal), (string)call[1]);
                    var seal = (ConsultGenerationV31Seal)call[2]!;
                    Seals.Add(seal);
                    _job.Entity.RecordV31Seal(seal);
                    return Task.CompletedTask;
                });

            _functions = new RecordRoutes(
                authorizer, rows ?? job.Rows, job.Blobs, new RecordSealers(job.Rows, job.Blobs, accounts, NullLogger<RecordSealer>.Instance), accounts,
                (Microsoft.Extensions.Logging.ILogger<RecordRoutes>?)log ?? NullLogger<RecordRoutes>.Instance);
        }

        public DurableTaskClient Client { get; } = Substitute.For<DurableTaskClient>("test");

        public AppAccount? SignedIn { get; set; } = AdminDrainTests.Admin.Account(UserId);

        public bool Missing { get; init; }

        public int TableCallsAtStart { get; }

        public List<ConsultGenerationV31Seal> Seals { get; } = [];

        public Task<HttpResponseData> RowsAsync(string? key, string method = "GET", string? origin = null)
        {
            var url = new Uri($"https://api.example.test/api/ConsultGenerationJobs/{V31Job.JobId}/Rows" + (key == null ? "" : $"?key={Uri.EscapeDataString(key)}"));
            return _functions.GetRowsAsync(Request(method, url, origin), Client, V31Job.JobId);
        }

        public Task<HttpResponseData> RecordAsync(string method = "GET", string? origin = null) =>
            _functions.GetRecordAsync(Request(method, new Uri($"https://api.example.test/api/ConsultGenerationJobs/{V31Job.JobId}/Record"), origin), Client, V31Job.JobId);

        private static HttpRequestData Request(string method, Uri url, string? origin)
        {
            var request = FakeHttp.Request(method, null, url);
            if (origin != null)
                request.Headers.Add("Origin", origin);
            return request;
        }
    }
}
