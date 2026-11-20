using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>#1498: one deliverable as the run left it — <c>composed</c> with its final text, or <c>skipped</c> or <c>failed</c> with the engine's sentence.</summary>
internal sealed record CapturedDeliverable(string Id, string State, string? Reason, string? Text);

/// <summary>
/// #1498: a whole job run the way production runs it, with no model. The wiring is
/// <see cref="EngineRun"/>'s — the real starter (its stores substituted), a real entity, the
/// real orchestrator on a <see cref="FakeOrchestrationContext"/> — with the scenario's profile
/// and macro choices on the starter, and every activity answered by its production pure half.
/// A prompt node runs <c>RunPromptNodeActivity.RunAsync</c>'s order with the one model call
/// replaced by a reply: the scenario's pinned output, or a stub made from the node's format
/// (<see cref="WorkflowStubOutputs"/>, seeded by the instance key). What is captured is what
/// production would have SENT and what the entity holds at the end. A job that fails is a
/// result; a pin that never ran, or a generated stub the engine flags, is refused.
/// </summary>
internal sealed class CaptureRun
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset SignatureUpdated = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ConsultGenerationJobEntity _entity;

    private CaptureRun(
        FakeOrchestrationContext context,
        ConsultGenerationJobEntity entity,
        IReadOnlyList<(string Key, JsonNode? Value)> outputs,
        IReadOnlyList<(string Key, string Text)> prompts,
        IReadOnlyList<(string Key, string Text)> renders,
        IReadOnlyList<CapturedDeliverable> deliverables)
    {
        Context = context;
        _entity = entity;
        Outputs = outputs;
        Prompts = prompts;
        Renders = renders;
        Deliverables = deliverables;
    }

    public FakeOrchestrationContext Context { get; }

    public ConsultGenerationJobState State => (ConsultGenerationJobState)StateProperty.GetValue(_entity)!;

    /// <summary>The job's status (<see cref="ConsultGenerationJobStatuses"/>).</summary>
    public string Status => State.Status;

    /// <summary>
    /// The job's failure sentence, null unless it failed: the failed node's (<c>MarkNodeFailed</c>),
    /// else the finalised job's, else the deciding stage's.
    /// </summary>
    public string? Failure => Status == ConsultGenerationJobStatuses.Failed
        ? State.AnalysisError ?? State.FailureError ?? State.StartFailure
        : null;

    /// <summary>Every reply served to a prompt node, pinned or generated, by instance key in served order.</summary>
    public IReadOnlyList<(string Key, JsonNode? Value)> Outputs { get; }

    /// <summary>The text each agent node was sent (<c>ConsultNodeDispatch.Plan(...).Sent</c>), by instance key in dispatch order.</summary>
    public IReadOnlyList<(string Key, string Text)> Prompts { get; }

    /// <summary>Each rendered node's output — its render — by instance key in order.</summary>
    public IReadOnlyList<(string Key, string Text)> Renders { get; }

    /// <summary>The deliverables the run settled, in the manifest's order.</summary>
    public IReadOnlyList<CapturedDeliverable> Deliverables { get; }

    /// <summary>Starts <paramref name="package"/> as <paramref name="scenario"/> says and runs it to the end.</summary>
    public static async Task<CaptureRun> StartAsync(WorkflowPackage package, CaptureScenario scenario)
    {
        // ---- the starter, its stores substituted (EngineRun's wiring) ------------------------------
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

        // The scenario's profile: the display name, and one signature block, chosen.
        var accounts = Substitute.For<IAccountStore>();
        accounts.GetDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scenario.Profile.Name);
        var settings = Substitute.For<IAccountSettingsStore>();
        if (scenario.Profile.Signature is { } signature)
        {
            var blocks = new SignatureBlocks.SignatureBlockSet(
                new List<SignatureBlocks.SignatureBlock> { new("sig-1", "Clinic", signature, SignatureUpdated) }, "sig-1");
            settings.GetAsync(Arg.Any<string>(), AccountSettingKeys.ProfileSignatures, Arg.Any<CancellationToken>())
                .Returns(new AccountSetting(AccountSettingKeys.ProfileSignatures, JsonSerializer.Serialize(blocks), "application/json", SignatureUpdated));
        }

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
            accounts,
            settings,
            Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IConsultGenerationLinkStore>(),
            Substitute.For<Consultologist.Api.Forms.IFormResponseStore>(),
            Substitute.For<Consultologist.Api.Forms.IFormResponseBlobStore>(),
            Substitute.For<IDocumentOcr>(),
            TestFormats.Catalog,
            null,
            null);

        var inputs = scenario.Inputs
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => ConsultInputValue.FromJson(pair.Value!.ToJsonString()), StringComparer.Ordinal);
        var macroChoices = scenario.MacroChoices.Count > 0
            ? new Dictionary<string, bool>(scenario.MacroChoices, StringComparer.Ordinal)
            : null;

        var outcome = await starter.StartAsync(client, new ConsultGenerationRequest(null, Inputs: inputs, MacroChoices: macroChoices), "user-1",
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

        // ---- the orchestrator, on the fake context, each activity its production pure half ------
        var outputs = new List<(string Key, JsonNode? Value)>();
        var prompts = new List<(string Key, string Text)>();
        var renders = new List<(string Key, string Text)>();
        var flagged = new List<(string Key, string Sentence)>();
        var context = new FakeOrchestrationContext(jobId, FakeOrchestrationContext.RoundTrip<ConsultGenerationOrchestrationInput>(input), entity, Now)
            .On(ConsultGenerationActivityNames.RunPromptNode, payload =>
                RunPromptNode((ConsultPromptNodeActivityInput)payload!, package, scenario, outputs, prompts, renders, flagged))
            .On(ConsultGenerationActivityNames.RunComputedNode, payload =>
            {
                // A let alone resolves its package (RunComputedNodeActivity.RunAsync).
                var node = (ConsultComputedNodeActivityInput)payload!;
                return RunComputedNodeActivity.Compute(node, node.Op == WorkflowLetAt30.Op ? package : null);
            })
            .On(ConsultGenerationActivityNames.DecideFlowItem, payload =>
                DecideFlowItemActivity.Decide(package, (ConsultDecideFlowItemInput)payload!))
            .On(ConsultGenerationActivityNames.RenderDeliverableMacros, payload =>
                RenderDeliverableMacrosActivity.Render((ConsultDeliverableMacrosActivityInput)payload!, package))
            .On(ConsultGenerationActivityNames.DecideDeliverables, payload =>
            {
                var decide = (ConsultDecideActivityInput)payload!;
                var supplied = decide.SuppliedInputs?.ToDictionary(pair => pair.Key, pair => ConsultInputValue.FromJson(pair.Value), StringComparer.Ordinal);
                return DecideActivity.Decide(package, supplied, decide.Classifications, TestFormats.Catalog,
                    decide.EffectiveInputs, decide.NodeTexts, decide.Run, decide.Profile);
            });

        try
        {
            await new ConsultGenerationOrchestrator().RunAsync(context);
        }
        catch (Exception ex) when (!IsUnanswered(ex) && JobFailed(entity))
        {
            // The orchestrator's own failure path: it finalised the job Failed, then rethrew
            // for Durable to record. The job's failure is the capture's; nothing is lost.
        }

        var run = new CaptureRun(context, entity, outputs, prompts, renders, Settled(package, (ConsultGenerationJobState)StateProperty.GetValue(entity)!));

        foreach (var key in scenario.Outputs.Keys)
        {
            if (!outputs.Any(output => output.Key == key))
            {
                throw new InvalidOperationException($"Scenario pins {key}, which did not run.");
            }
        }

        if (flagged.Count > 0)
        {
            throw new InvalidOperationException($"Generated stub for {flagged[0].Key} is not valid for its format: {flagged[0].Sentence}");
        }

        return run;
    }

    /// <summary>
    /// <c>RunPromptNodeActivity.RunAsync</c> for a node with a format, minus the model call:
    /// the 25 render; a rendered node's render returned as its answer; otherwise the dispatch
    /// planned, the reply chosen, settled under the node's onInvalid and completed.
    /// </summary>
    private static NodeRunResult RunPromptNode(
        ConsultPromptNodeActivityInput node,
        WorkflowPackage package,
        CaptureScenario scenario,
        List<(string Key, JsonNode? Value)> outputs,
        List<(string Key, string Text)> prompts,
        List<(string Key, string Text)> renders,
        List<(string Key, string Sentence)> flagged)
    {
        if (node.Format is null)
        {
            throw new NotSupportedException($"Prompt node '{node.NodeId}' carries no format; the capture runs the 25-and-later path only.");
        }

        if (package.Prompts is null || !package.Prompts.TryGetValue(node.PromptId, out var prompt))
        {
            throw new InvalidOperationException($"Workflow package {package.Ref} has no prompt '{node.PromptId}' for node '{node.NodeId}'.");
        }

        var trace = new TemplateRenderer.TemplateRenderTrace();
        var rendered = ConsultRenderPayload.RenderAt25(node, package, prompt.TemplateText, trace);

        if (node.Template == true)
        {
            renders.Add((node.NodeId, rendered));
            var template = RunPromptNodeActivity.TemplateResult(rendered, node.OutputContract, node.ConceptSource, node.NodeId);
            return template with { HashVersion = ConsultGenerationProvenance.NodeHashVersionAt25, AgentOutputHash = template.OutputHash };
        }

        var plan = ConsultNodeDispatch.Plan(node, package, rendered, V29Catalog.Catalog, TestFormats.Catalog);
        prompts.Add((node.NodeId, plan.Sent));

        var generated = !scenario.Outputs.TryGetValue(node.NodeId, out var pinned);
        var value = generated
            ? plan.Kind switch
            {
                ConsultNodeDispatch.ReplyKind.Enum => new JsonObject { ["value"] = WorkflowStubOutputs.Member(plan.Values, node.NodeId) },
                ConsultNodeDispatch.ReplyKind.Structured => WorkflowStubOutputs.FromSchema(plan.Schema, node.NodeId),
                _ => JsonValue.Create(WorkflowStubOutputs.Text(node.NodeId)),
            }
            : pinned?.DeepClone();
        outputs.Add((node.NodeId, value));
        var reply = value is JsonValue text && text.TryGetValue(out string? replyText) ? replyText : value?.ToJsonString() ?? "null";

        NodeRunResult settled;
        try
        {
            settled = ConsultNodeDispatch.Settle(plan, node.NodeId, reply, ConsultGenerationProvenance.Sha256Hex(plan.Sent), null);
        }
        catch (ConsultNodeInvalidReplyException ex) when (generated)
        {
            // The node fails as production's would; the run is refused once it ends.
            flagged.Add((node.NodeId, ex.Message));
            throw;
        }

        if (generated && settled.Validation is { Valid: false } validation)
        {
            flagged.Add((node.NodeId, string.Join(" ", validation.Errors)));
        }

        return ConsultNodeCompletion.Complete(settled, node, package, trace.PromptReads.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Each deliverable of the manifest the run settled — composed, skipped or failed — in the manifest's order.</summary>
    private static List<CapturedDeliverable> Settled(WorkflowPackage package, ConsultGenerationJobState state)
    {
        var deliverables = new List<CapturedDeliverable>();

        foreach (var deliverable in package.Manifest.EffectiveDeliverables)
        {
            if (state.AssembledDocuments?.FirstOrDefault(d => d.ResultId == deliverable.Id) is { } composed)
            {
                deliverables.Add(new CapturedDeliverable(deliverable.Id, "composed", null, composed.Text));
            }
            else if (state.SkippedDocuments?.FirstOrDefault(d => d.ResultId == deliverable.Id) is { } skipped)
            {
                deliverables.Add(new CapturedDeliverable(deliverable.Id, "skipped", skipped.Reason, null));
            }
            else if (state.FailedDocuments?.FirstOrDefault(d => d.ResultId == deliverable.Id) is { } failed)
            {
                deliverables.Add(new CapturedDeliverable(deliverable.Id, "failed", failed.Reason, null));
            }
        }

        return deliverables;
    }

    private static bool IsUnanswered(Exception ex) =>
        ex is InvalidOperationException && ex.Message.StartsWith("The test answers no activity '", StringComparison.Ordinal);

    private static bool JobFailed(ConsultGenerationJobEntity entity) =>
        ((ConsultGenerationJobState)StateProperty.GetValue(entity)!).Status == ConsultGenerationJobStatuses.Failed;

    private sealed class NoTerminology : ITerminologyAttestationSource
    {
        public ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult<TerminologyAttestation?>(null);
    }
}
