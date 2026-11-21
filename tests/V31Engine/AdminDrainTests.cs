using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Azure.Core.Serialization;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (part 09 § 3.6; D9): <c>POST Admin/Drain</c>, <c>POST Admin/Resume</c> and <c>GET Admin/Drain</c>, through their
/// functions over a request fake: gated as the <c>Operator/*</c> endpoints are (401 without an account, 403 for one not
/// an operator); the drain and the status say whether the engine drains, how many of its orchestrations are producing
/// (running, and neither waiting for an update nor asleep on a schedule) and how many jobs are parked; Resume schedules
/// every parked job, oldest first, once, and wakes every orchestration waiting for it.
/// </summary>
public class AdminDrainTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANonOperator_IsRefused()
    {
        var admin = new Admin();
        await admin.Drain.ParkAsync(EngineDrainTests.Parked("job-a", At), CancellationToken.None);

        foreach (var (account, expected) in new[] { (Admin.Account("user-1"), HttpStatusCode.Forbidden), (null, HttpStatusCode.Unauthorized) })
        {
            admin.SignedIn = account;
            Assert.Equal(expected, (await admin.DrainAsync()).StatusCode);
            Assert.Equal(expected, (await admin.ResumeAsync()).StatusCode);
            Assert.Equal(expected, (await admin.StatusAsync()).StatusCode);
        }

        // an operator's account that can't use the app is refused too, as Operator/* refuses it
        admin.SignedIn = Admin.Account(Admin.Operator) with { Status = AccountStatuses.Pending };
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DrainAsync()).StatusCode);

        Assert.False(await admin.Drain.IsDrainingAsync(CancellationToken.None));
        Assert.Equal(1, await admin.Drain.ParkedCountAsync(CancellationToken.None));
        Assert.Empty(admin.Scheduled);
    }

    [Fact]
    public async Task Drain_ReportsWhatIsProducing()
    {
        var admin = new Admin();
        admin.Instances.AddRange(
        [
            Admin.Instance(nameof(ConsultGenerationOrchestrator), "producing-30", OrchestrationRuntimeStatus.Running, """{"status":"Running","totalBlockCount":2}"""),
            Admin.Instance(nameof(ConsultGenerationOrchestratorV31), "producing-31", OrchestrationRuntimeStatus.Running, """{"status":"Running"}"""),
            Admin.Instance(nameof(ConsultGenerationOrchestratorV31), "about-to-start", OrchestrationRuntimeStatus.Pending, null),
            Admin.Instance(nameof(ConsultGenerationOrchestrator), "asleep", OrchestrationRuntimeStatus.Running, """{"status":"Scheduled"}"""),
            Admin.Instance(nameof(ConsultGenerationOrchestratorV31), "waiting", OrchestrationRuntimeStatus.Running, """{"status":"Waiting for an update to finish"}"""),
            Admin.Instance(nameof(ConsultGenerationOrchestrator), "done", OrchestrationRuntimeStatus.Completed, """{"status":"Completed"}"""),
            Admin.Instance("SomeoneElsesOrchestrator", "not-ours", OrchestrationRuntimeStatus.Running, null),
        ]);
        await admin.Drain.ParkAsync(EngineDrainTests.Parked("job-a", At), CancellationToken.None);

        var before = await admin.StatusAsync();
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal("""{"draining":false,"producing":3,"parked":1}""", FakeHttp.BodyOf(before));

        var drained = await admin.DrainAsync();
        Assert.Equal(HttpStatusCode.OK, drained.StatusCode);
        Assert.Equal("""{"draining":true,"producing":3,"parked":1}""", FakeHttp.BodyOf(drained));
        Assert.True(await admin.Drain.IsDrainingAsync(CancellationToken.None));

        // the producing jobs finish; the status says so, and a second drain changes nothing
        admin.Instances.RemoveAll(instance => instance.InstanceId.StartsWith("producing", StringComparison.Ordinal) || instance.InstanceId == "about-to-start");
        Assert.Equal("""{"draining":true,"producing":0,"parked":1}""", FakeHttp.BodyOf(await admin.StatusAsync()));
        Assert.Equal("""{"draining":true,"producing":0,"parked":1}""", FakeHttp.BodyOf(await admin.DrainAsync()));
        Assert.Empty(admin.Scheduled);
        Assert.Empty(admin.Raised);

        // every query fetches inputs and outputs (so the custom status comes back) and reads with the request's token
        Assert.All(admin.Queries, query =>
        {
            Assert.True(query.Query!.FetchInputsAndOutputs);
            Assert.Equal([OrchestrationRuntimeStatus.Running, OrchestrationRuntimeStatus.Pending], query.Query.Statuses);
            Assert.Equal(admin.RequestToken, query.Token);
        });
        Assert.Equal(4, admin.Queries.Count);
    }

    [Fact]
    public async Task Resume_SchedulesEveryParkedJob_OldestFirst()
    {
        var admin = new Admin();
        await admin.DrainAsync();
        var v30 = new ConsultGenerationOrchestrationInput(new Models.ConsultGenerationRequest("A fictional referral for Margaret Okafor."), "user-1", "test@v1", DrainAware: true);
        await admin.Drain.ParkAsync(EngineDrainJobs.Park("job-30", nameof(ConsultGenerationOrchestrator), v30, At.AddMinutes(2), SignInKinds.Personal), CancellationToken.None);
        var v31 = new ConsultGenerationOrchestrationInputV31("job-31", "user-1", "test", "v1", "sha256:r", "sha256:l", new JsonObject(), 2000,
            "user-1/job-31.json", new JsonObject { ["id"] = "job-31" }, "America/Toronto", null, true, SignInKinds.Organisation);
        await admin.Drain.ParkAsync(EngineDrainJobs.Park("job-31", nameof(ConsultGenerationOrchestratorV31), v31, At.AddMinutes(1), SignInKinds.Organisation), CancellationToken.None);
        await admin.Drain.ParkAsync(EngineDrainJobs.Park("job-late", nameof(ConsultGenerationOrchestrator), v30, At.AddMinutes(3), SignInKinds.Personal), CancellationToken.None);
        admin.Instances.Add(Admin.Instance(nameof(ConsultGenerationOrchestratorV31), "waiting", OrchestrationRuntimeStatus.Running, """{"status":"Waiting for an update to finish"}"""));
        admin.Instances.Add(Admin.Instance(nameof(ConsultGenerationOrchestrator), "asleep", OrchestrationRuntimeStatus.Running, """{"status":"Scheduled"}"""));

        var resumed = await admin.ResumeAsync();

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal("""{"draining":false,"started":3}""", FakeHttp.BodyOf(resumed));
        Assert.False(await admin.Drain.IsDrainingAsync(CancellationToken.None));
        Assert.Equal(["job-31", "job-30", "job-late"], admin.Scheduled.Select(s => s.Options!.InstanceId));
        Assert.Equal([nameof(ConsultGenerationOrchestratorV31), nameof(ConsultGenerationOrchestrator), nameof(ConsultGenerationOrchestrator)], admin.Scheduled.Select(s => s.Name));
        // each input is scheduled as it was parked, typed as its orchestrator reads it
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(v31), System.Text.Json.JsonSerializer.Serialize(Assert.IsType<ConsultGenerationOrchestrationInputV31>(admin.Scheduled[0].Input)));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(v30), System.Text.Json.JsonSerializer.Serialize(Assert.IsType<ConsultGenerationOrchestrationInput>(admin.Scheduled[1].Input)));
        // each parked job's record stops waiting; the orchestration waiting behind the drain is woken; nothing else is
        Assert.Equal(["job-31", "job-30", "job-late"], admin.Signals.Where(s => s.Operation == nameof(ConsultGenerationJobEntity.EndWaitingForUpdate)).Select(s => s.Key));
        Assert.Equal([("waiting", EngineDrain.ResumedEvent)], admin.Raised);
        var wake = Assert.Single(admin.Queries, query => query.Query!.Statuses!.SequenceEqual([OrchestrationRuntimeStatus.Running]));
        Assert.True(wake.Query!.FetchInputsAndOutputs);
        Assert.Equal(admin.RequestToken, wake.Token);
        Assert.Equal(0, await admin.Drain.ParkedCountAsync(CancellationToken.None));

        // a second Resume starts nothing twice
        Assert.Equal("""{"draining":false,"started":0}""", FakeHttp.BodyOf(await admin.ResumeAsync()));
        Assert.Equal(3, admin.Scheduled.Count);
    }

    [Fact]
    public async Task Resume_DoesNotStartAJobWhoseOrchestrationExists()
    {
        // a Resume that scheduled a job and stopped before forgetting it: the next one forgets it and schedules nothing
        var admin = new Admin();
        await admin.Drain.ParkAsync(EngineDrainTests.Parked("job-a", At), CancellationToken.None);
        admin.Existing.Add("job-a");

        Assert.Equal("""{"draining":false,"started":0}""", FakeHttp.BodyOf(await admin.ResumeAsync()));
        Assert.Empty(admin.Scheduled);
        Assert.Equal(0, await admin.Drain.ParkedCountAsync(CancellationToken.None));
    }

    /// <summary>The three functions over an <see cref="EngineDrain"/> in memory and a substituted Durable client.</summary>
    internal sealed class Admin
    {
        public const string Operator = "op-1";

        private readonly AdminDrain _functions;

        public Admin(IEngineDrain? drain = null)
        {
            Drain = drain ?? new EngineDrain(new EngineDrainTests.MemoryDrainBlobs());
            var authorizer = Substitute.For<IAccountAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(_ => SignedIn);
            _functions = new AdminDrain(authorizer, Drain, NullLogger<AdminDrain>.Instance, new HashSet<string>(StringComparer.Ordinal) { Operator });

            var entities = Substitute.For<DurableEntityClient>("test");
            Client.Entities.Returns(entities);
            entities.SignalEntityAsync(Arg.Any<EntityInstanceId>(), Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<SignalEntityOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Signals.Add((((EntityInstanceId)call[0]).Key, (string)call[1], call[2]));
                    return Task.CompletedTask;
                });
            Client.ScheduleNewOrchestrationInstanceAsync(Arg.Any<TaskName>(), Arg.Any<object?>(), Arg.Any<StartOrchestrationOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var options = (StartOrchestrationOptions?)call[2];
                    Scheduled.Add((((TaskName)call[0]).Name, call[1], options));
                    Existing.Add(options!.InstanceId!);
                    return Task.FromResult(options.InstanceId!);
                });
            Client.GetInstanceAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(call => Existing.Contains((string)call[0]) ? new OrchestrationMetadata("any", (string)call[0]) : null);
            // The query answers as a backend that leaves the custom status out unless inputs and outputs are fetched would:
            // the drain must ask for them, or every sleeping and waiting orchestration would read as producing.
            Client.GetAllInstancesAsync(Arg.Any<OrchestrationQuery?>())
                .Returns(call => Pageable.Create((string? _, CancellationToken token) =>
                {
                    var query = (OrchestrationQuery?)call[0];
                    Queries.Add((query, token));
                    var wanted = query?.Statuses?.ToHashSet();
                    return Task.FromResult(new Page<OrchestrationMetadata>(
                        Instances.Where(instance => wanted == null || wanted.Contains(instance.RuntimeStatus))
                            .Select(instance => query?.FetchInputsAndOutputs == true
                                ? instance
                                : new OrchestrationMetadata(instance.Name, instance.InstanceId) { RuntimeStatus = instance.RuntimeStatus })
                            .ToList(), null));
                }));
            Client.RaiseEventAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Raised.Add(((string)call[0], (string)call[1]));
                    return Task.CompletedTask;
                });
        }

        public IEngineDrain Drain { get; }

        public DurableTaskClient Client { get; } = Substitute.For<DurableTaskClient>("test");

        public AppAccount? SignedIn { get; set; } = Account(Operator);

        /// <summary>The orchestrations a query finds.</summary>
        public List<OrchestrationMetadata> Instances { get; } = new();

        /// <summary>The instance ids whose orchestrations exist.</summary>
        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

        public List<(string Name, object? Input, StartOrchestrationOptions? Options)> Scheduled { get; } = new();

        public List<(string Key, string Operation, object? Payload)> Signals { get; } = new();

        public List<(string InstanceId, string EventName)> Raised { get; } = new();

        /// <summary>Each instance query: its options and the token its pages were read with.</summary>
        public List<(OrchestrationQuery? Query, CancellationToken Token)> Queries { get; } = new();

        /// <summary>The token every request is made with.</summary>
        public CancellationToken RequestToken { get; } = new CancellationTokenSource().Token;

        public Task<HttpResponseData> DrainAsync() => _functions.DrainAsync(FakeHttp.Request("POST", RequestToken), Client);

        public Task<HttpResponseData> ResumeAsync() => _functions.ResumeAsync(FakeHttp.Request("POST", RequestToken), Client);

        public Task<HttpResponseData> StatusAsync() => _functions.StatusAsync(FakeHttp.Request("GET", RequestToken), Client);

        public static AppAccount Account(string appUserId)
        {
            var identity = new AccountIdentity("entra", "https://login.example.test", appUserId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            return new AppAccount(appUserId, "Dr A. Sample", null, AccountStatuses.Active, identity, [identity]);
        }

        public static OrchestrationMetadata Instance(string name, string instanceId, OrchestrationRuntimeStatus status, string? customStatus) =>
            new(name, instanceId) { RuntimeStatus = status, SerializedCustomStatus = customStatus };
    }
}

/// <summary>
/// #1503: an <see cref="HttpRequestData"/> and its response for calling a function directly: no headers, no body, the
/// worker's JSON serializer at its defaults (anonymous objects keep their casing).
/// </summary>
internal static class FakeHttp
{
    public static HttpRequestData Request(string method, CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer());
        var context = Substitute.For<FunctionContext>();
        context.InstanceServices.Returns(services.BuildServiceProvider());
        context.CancellationToken.Returns(cancellationToken);
        return new FakeRequest(context, method);
    }

    /// <summary>The response's body as written.</summary>
    public static string BodyOf(HttpResponseData response)
    {
        response.Body.Position = 0;
        return JsonNode.Parse(response.Body)!.ToJsonString();
    }

    private sealed class FakeRequest(FunctionContext context, string method) : HttpRequestData(context)
    {
        public override Stream Body { get; } = new MemoryStream();

        public override HttpHeadersCollection Headers { get; } = new();

        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();

        public override Uri Url { get; } = new("https://api.example.test/api/Admin/Drain");

        public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();

        public override string Method { get; } = method;

        public override HttpResponseData CreateResponse() => new FakeResponse(FunctionContext);
    }

    private sealed class FakeResponse(FunctionContext context) : HttpResponseData(context)
    {
        public override HttpStatusCode StatusCode { get; set; }

        public override HttpHeadersCollection Headers { get; set; } = new();

        public override Stream Body { get; set; } = new MemoryStream();

        public override HttpCookies Cookies { get; } = Substitute.For<HttpCookies>();
    }
}
