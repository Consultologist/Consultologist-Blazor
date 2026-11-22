using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.V31;
using Consultologist.Api.Tests.V31Run;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (D5): when a job's plan is refused at run time (its compiled documents no longer verify), the job shows part 09's
/// sentence and the operator's log keeps the store's part 07 sentence, in each activity that loads the plan. The S07
/// sentence is package text and hashes, no value.
/// </summary>
public class PlanRefusalLogTests
{
    [Fact]
    public async Task ARefusedPlan_LogsTheOperatorsSentence_InEveryActivity()
    {
        var registry = new FakeRegistry();
        var packageRef = registry.Put(RunFixture.Golden("nested-note")).Ref;
        var resolvePath = FakeRegistry.PathOf(packageRef, CompiledPackage.ResolveFile);
        registry.Blobs[resolvePath] = [.. registry.Blobs[resolvePath], (byte)' '];
        var store = V31Job.StoreOver(registry);
        var load = (await store.TryLoadAsync(packageRef, CancellationToken.None))!;
        Assert.Equal("S09-009", load.RefusalId);
        Assert.False(string.IsNullOrWhiteSpace(load.OperatorDetail));
        Assert.NotEqual(load.Refusal, load.OperatorDetail);

        var planLog = new CapturingLogger<LoadPlanV31Activity>();
        var planned = await new LoadPlanV31Activity(store, planLog)
            .RunAsync(new LoadPlanV31Input(packageRef.Name, packageRef.Version, "sha256:r", "sha256:l"), CancellationToken.None);
        Assert.Equal(load.Refusal, planned.Refusal);
        AssertLogged(planLog, load);

        var renderLog = new CapturingLogger<RenderNodeV31Activity>();
        var rendered = await new RenderNodeV31Activity(store, new FakeRecordBlobStore(), renderLog)
            .RunAsync(new RenderNodeV31Input(packageRef.Name, packageRef.Version, "sha256:r", new RenderCall("node.a", "node.a", null, new JsonObject(), 100)), CancellationToken.None);
        Assert.Equal(load.Refusal, rendered.Failure);
        AssertLogged(renderLog, load);

        var agentLog = new CapturingLogger<RunAgentNodeV31Activity>();
        await new RunAgentNodeV31Activity(store, Substitute.For<IAgentAttempts>(), new Lazy<V31AgentsCatalogue>(() => V31AgentsCatalogue.Load()), new FakeRecordBlobStore(), agentLog)
            .RunAsync(new RunAgentNodeV31Input(packageRef.Name, packageRef.Version, "sha256:r", 100, new AgentCall("node.a", "node.a", null, 1, false, new JsonObject())), CancellationToken.None);
        AssertLogged(agentLog, load);

        var inputs = new V31Job.FakeInputs();
        var pointer = await inputs.WriteAsync(null, "user-1", "job-1", new JsonObject { ["inputs"] = new JsonObject() }, CancellationToken.None);
        var inputsLog = new CapturingLogger<LoadRunInputsV31Activity>();
        var held = await new LoadRunInputsV31Activity(inputs, store, V31Job.Toolchain.Value, inputsLog).RunAsync(
            new LoadRunInputsV31Input(pointer, packageRef.Name, packageRef.Version, "sha256:r",
                new DateTimeOffset(2026, 11, 17, 3, 30, 0, TimeSpan.Zero), "America/Toronto", new JsonObject { ["id"] = "job-1" }),
            CancellationToken.None);
        Assert.Equal(load.Refusal, held.Refusal);
        AssertLogged(inputsLog, load);
    }

    private static void AssertLogged<T>(CapturingLogger<T> log, CompiledLoad load)
    {
        Assert.Contains(load.OperatorDetail!, log.Everything, StringComparison.Ordinal);
        Assert.Contains("S09-009", log.Everything, StringComparison.Ordinal);
    }
}
