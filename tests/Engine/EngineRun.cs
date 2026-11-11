using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.Engine;

/// <summary>
/// #1341 B2a: a whole job, end to end, in a test — the real starter builds the
/// orchestration input and the entity's Initialize from a package (its stores
/// substituted), a real entity takes them, and the real orchestrator runs on a
/// <see cref="FakeOrchestrationContext"/>. Prompt nodes are rendered by the real
/// renderer and answered from a script (or a stand-in text); per-item decisions
/// are decided by the real activity's logic. <see cref="Trace"/> is the whole run
/// as text — the activity payloads, the entity operations, the rendered prompts,
/// the served record and its events — the pin B2b–B2d keep.
/// </summary>
internal sealed class EngineRun
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private EngineRun(FakeOrchestrationContext context, ConsultGenerationJobEntity entity, List<(string NodeId, string Prompt)> prompts, string jobId)
    {
        Context = context;
        Entity = entity;
        Prompts = prompts;
        JobId = jobId;
    }

    /// <summary>A scripted reply that makes the prompt node's activity fail.</summary>
    public const string Fails = "<fails>";

    public FakeOrchestrationContext Context { get; }

    public ConsultGenerationJobEntity Entity { get; }

    public string JobId { get; }

    /// <summary>Each prompt node's instance key and the prompt the renderer made for it, in order.</summary>
    public List<(string NodeId, string Prompt)> Prompts { get; }

    public ConsultGenerationJobState State => (ConsultGenerationJobState)StateProperty.GetValue(Entity)!;

    /// <summary>
    /// Starts <paramref name="package"/> with <paramref name="inputs"/> and runs it to
    /// the end. <paramref name="outputs"/> answers a prompt node by its instance key
    /// (<c>node</c> or <c>node:path</c>); unanswered nodes reply with a stand-in text.
    /// </summary>
    public static async Task<EngineRun> StartAsync(
        WorkflowPackage package,
        Dictionary<string, ConsultInputValue> inputs,
        Func<string, string?>? outputs = null)
    {
        // ---- the starter, its stores substituted (ConsultGenerationJobStarterTests' idiom) ----
        var packageStore = Substitute.For<IWorkflowPackageStore>();
        var pinResolver = Substitute.For<IWorkflowPackagePinResolver>();
        var rateLimiter = Substitute.For<IAccountRateLimiter>();
        var client = Substitute.For<DurableTaskClient>("test");
        var entities = Substitute.For<DurableEntityClient>("test");
        client.Entities.Returns(entities);
        rateLimiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitDecision(true, 60, 59, TimeSpan.FromMinutes(37)));
        pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>()).Returns(WorkflowPackageRef.TryParse(package.Ref, out var pinned) ? pinned! : new WorkflowPackageRef("test", "v1"));
        packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(package);

        ConsultGenerationOrchestrationInput? input = null;
        ConsultGenerationJobInitialize? initialize = null;
        await entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => input = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(((StartOrchestrationOptions?)call[2])!.InstanceId!));

        var starter = new ConsultGenerationJobStarter(
            NullLogger<ConsultGenerationJobStarter>.Instance,
            packageStore,
            pinResolver,
            V29Catalog.Catalog,
            rateLimiter,
            new FakeOwnership(),
            EngineAttestation.Current(V29Catalog.Catalog, null),
            new NoTerminology(),
            Substitute.For<IAccountStore>(),
            Substitute.For<IAccountSettingsStore>(),
            Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IConsultGenerationLinkStore>(),
            Substitute.For<Consultologist.Api.Forms.IFormResponseStore>(),
            Substitute.For<Consultologist.Api.Forms.IFormResponseBlobStore>(),
            Substitute.For<IDocumentOcr>(),
            TestFormats.Catalog,
            null,
            null);

        var outcome = await starter.StartAsync(client, new ConsultGenerationRequest(null, Inputs: inputs), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        if (outcome.Error is not null || input is null || initialize is null)
        {
            throw new InvalidOperationException($"The job did not start: {outcome.Error}.");
        }

        var jobId = initialize.JobId;

        // ---- the entity, Initialized as the starter signalled it --------------------------------
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create(jobId, initialize.AppUserId, initialize.Items));
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(initialize));

        // ---- the orchestrator, on the fake context ----------------------------------------------
        var prompts = new List<(string NodeId, string Prompt)>();
        var context = new FakeOrchestrationContext(jobId, FakeOrchestrationContext.RoundTrip<ConsultGenerationOrchestrationInput>(input), entity, Now)
            .On(ConsultGenerationActivityNames.RunPromptNode, payload =>
            {
                var node = (ConsultPromptNodeActivityInput)payload!;
                if (node.Format is not null && package.Prompts is { } bodies && bodies.TryGetValue(node.PromptId, out var prompt))
                {
                    prompts.Add((node.NodeId, ConsultRenderPayload.RenderAt25(node, package, prompt.TemplateText, new TemplateRenderer.TemplateRenderTrace())));
                }

                var text = outputs?.Invoke(node.NodeId) ?? $"[{node.NodeId}]";
                if (text == Fails)
                {
                    throw new InvalidOperationException($"{node.NodeId} failed in the test.");
                }

                // A classifier's answer rides beside its text, as the activity normalises it.
                return new NodeRunResult(text, null, $"in:{node.NodeId}", $"out:{node.NodeId}", 6,
                    Classification: node.Format == "classification" ? text : null);
            })
            .On(ConsultGenerationActivityNames.RunComputedNode, payload =>
                RunComputedNodeActivity.Compute((ConsultComputedNodeActivityInput)payload!, package))
            .On(ConsultGenerationActivityNames.DecideFlowItem, payload =>
                DecideFlowItemActivity.Decide(package, (ConsultDecideFlowItemInput)payload!));

        await new ConsultGenerationOrchestrator().RunAsync(context);
        return new EngineRun(context, entity, prompts, jobId);
    }

    /// <summary>The whole run as text, the job id and the clock masked.</summary>
    public string Trace()
    {
        var text = new StringBuilder();
        text.AppendLine("# calls");
        foreach (var (kind, name, json) in Context.Calls)
        {
            text.AppendLine($"{kind} {name} {json}");
        }

        text.AppendLine("# prompts");
        foreach (var (nodeId, prompt) in Prompts)
        {
            text.AppendLine($"## {nodeId}");
            text.AppendLine(prompt);
        }

        var served = State.ToResponse();
        text.AppendLine("# the served record");
        text.AppendLine(JsonSerializer.Serialize(served, new JsonSerializerOptions { WriteIndented = true }));
        text.AppendLine("# the event candidates");
        foreach (var candidate in ConsultGenerationJobs.CreateSemanticEventCandidates(served))
        {
            text.AppendLine($"{candidate.EventType} | {candidate.EventKey} | {candidate.PayloadJson}");
        }

        // The job id (whole, and as the run's short id) and the starter's clock are the only
        // things that move between runs.
        var masked = text.ToString().Replace(JobId, "<job>").Replace(JobId[..8], "<job8>");
        masked = Regex.Replace(masked, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?", "<time>");
        masked = Regex.Replace(masked, @"\b\d{4}-\d{2}-\d{2}\b", "<date>");
        // The build stamps its commit into the attestation; it moves with every commit.
        masked = Regex.Replace(masked, @"""EngineCommit"":\s?""[0-9a-f]+""", @"""EngineCommit"":""<commit>""");
        return masked.Replace("\r\n", "\n");
    }

    private sealed class NoTerminology : ITerminologyAttestationSource
    {
        public ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult<TerminologyAttestation?>(null);
    }
}
