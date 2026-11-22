using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07): an <see cref="IRunHost"/> that runs the code the engine's activities run, with no
/// Azure. An agent call is prepared with <see cref="AgentTurn.Prepare"/> (its step budget <see cref="StepLimit"/>, strict
/// mode rejecting <c>format</c>), given the reply <paramref name="reply"/> writes for it, and finished with
/// <see cref="AgentTurn.Finish"/>; a prompt that could not be prepared is <see cref="AgentOutcomeKind.Failed"/> and gets
/// no reply. A render is <see cref="RenderTurn.Render"/> (<see cref="BodyComposer.Compose"/>); #1505 (R6) a rule's
/// evaluation is <see cref="RuleTurn.Evaluate"/>. Every answer is given at
/// once; the clock does not move.
/// </summary>
internal sealed class ComposingRunHost(RunPlan plan, Func<AgentCall, PreparedPrompt, string> reply) : IRunHost
{
    /// <summary>The step budget a prompt is composed in.</summary>
    public const int StepLimit = 100_000;

    private static readonly string[] StrictRejects = ["format"];

    /// <inheritdoc/>
    public DateTime UtcNow => FakeRunHost.Start;

    /// <summary>The agent calls made, in order.</summary>
    public List<AgentCall> Calls { get; } = [];

    /// <summary>Each agent call's prepared prompt, in the order of <see cref="Calls"/>.</summary>
    public List<PreparedPrompt> Prompts { get; } = [];

    /// <summary>The text each prepared prompt sent, in the order of <see cref="Calls"/> (<c>null</c> for one not sent).</summary>
    public IEnumerable<string?> Sent => Prompts.Select(p => p.Text);

    /// <summary>The renders asked for, in order.</summary>
    public List<RenderCall> Renders { get; } = [];

    /// <summary>The keys of the agent calls made, in order.</summary>
    public List<string> Dispatched => Calls.Select(c => c.Key).ToList();

    /// <summary>The prompt sent to the agent call keyed <paramref name="key"/> at <paramref name="attempt"/> (from 1).</summary>
    public PreparedPrompt Prompt(string key, int attempt = 1) =>
        Prompts[Calls.FindIndex(c => c.Key == key && c.Attempt == attempt)];

    /// <summary>
    /// A reply for a text format: <see cref="WorkflowStubOutputs.Text"/> of the row key, then each macro the prompt offers,
    /// in prompt order, by its marker in the form its instruction asks: a block marker alone on a line of its own, an inline
    /// one after a space.
    /// </summary>
    public static string EchoMarkers(AgentCall call, PreparedPrompt prompt)
    {
        var text = WorkflowStubOutputs.Text(call.Key);
        foreach (var macro in prompt.Composition.Macros)
            text += macro.Block ? $"\n{{{{~ {macro.Marker} }}}}\n" : $" {{{{ {macro.Marker} }}}}";
        return text;
    }

    /// <summary>A <c>judgement</c> reply: <paramref name="answer"/>, with a fixed reason.</summary>
    public static string Judgement(bool answer) => $$"""{"answer":{{(answer ? "true" : "false")}},"reason":"The plan says so."}""";

    /// <summary>#1506 (area 09): each agent call's outcome, in the order of <see cref="Calls"/>.</summary>
    public List<AgentOutcome> Outcomes { get; } = [];

    /// <summary>#1506 (area 09): each render's outcome, in the order of <see cref="Renders"/>.</summary>
    public List<RenderOutcome> RenderOutcomes { get; } = [];

    /// <inheritdoc/>
    public Task<AgentOutcome> CallAgentAsync(AgentCall call)
    {
        Calls.Add(call);
        var prepared = AgentTurn.Prepare(plan, call, StepLimit, StrictRejects);
        Prompts.Add(prepared);
        var outcome = prepared.Text == null
            ? AgentTurn.Finish(prepared, "")
            : AgentTurn.Finish(prepared, reply(call, prepared));
        Outcomes.Add(outcome);
        return Task.FromResult(outcome);
    }

    /// <inheritdoc/>
    public Task<RenderOutcome> RenderAsync(RenderCall call)
    {
        Renders.Add(call);
        var outcome = RenderTurn.Render(plan, call);
        RenderOutcomes.Add(outcome);
        return Task.FromResult(outcome);
    }

    /// <summary>#1505 (R6): the rule evaluations asked for, in order.</summary>
    public List<RuleCall> Rules { get; } = [];

    /// <inheritdoc/>
    public Task<RuleOutcome> EvaluateAsync(RuleCall call)
    {
        Rules.Add(call);
        return Task.FromResult(RuleTurn.Evaluate(plan, call));
    }

    /// <summary>
    /// A retry's wait (under a minute) ends at once; the job's timer (its minutes) ends only cancelled, when the run ends.
    /// </summary>
    public Task DelayAsync(TimeSpan wait, CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
            return Task.FromCanceled(cancel);
        if (wait < TimeSpan.FromMinutes(1))
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        cancel.Register(() => done.TrySetCanceled(cancel));
        return done.Task;
    }
}
