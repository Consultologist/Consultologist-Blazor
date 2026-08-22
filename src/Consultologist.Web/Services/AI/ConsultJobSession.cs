namespace Consultologist.Web.Services.AI;

public sealed record ConsultJobBlock(string Id, string Name);

/// <summary>
/// What Consults needs to rebuild its run view for a job started in this tab:
/// the job record carries no input text, so the supplied inputs (and the exact
/// block roster the run was submitted with) live only here. Keyed by declared
/// input id — v5/v6 runs carry the single consult_draft entry.
///
/// #360: deliberately no package ref. The job's own resolved version is on its
/// snapshot (ConsultGenerationJobResponse.WorkflowPackage — what History renders
/// as the provenance chip), which is present on every re-attach path including
/// the route-only one, across tabs, and after a reload. The copy that used to
/// live here was none of those things, and had exactly one reader: it overwrote
/// the page's current-pin ref, so the next submit ran against the previous
/// job's package. With the field gone that is no longer expressible.
/// </summary>
public sealed record ConsultJobMemento(
    string JobId,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<ConsultJobBlock> Blocks,
    // #429: the inputs as typed — a number, an object, an array of rows —
    // so a structured slot re-attaches as itself rather than as its text.
    // A document slot rides as the text the server read, the way the text
    // map always carried it. Trailing optional: a memento written before
    // this restores from the text.
    IReadOnlyDictionary<string, ConsultInputValue>? Values = null);

/// <summary>
/// #207: per-tab memory of the most recently submitted consult job, so
/// navigating away from Consults mid-run and returning re-attaches instead of
/// forgetting. Scoped DI in Blazor WASM = one instance per tab session; a new
/// submission overwrites it and navigation never clears it (that's the point).
/// </summary>
public sealed class ConsultJobSession
{
    public ConsultJobMemento? Current { get; set; }
}
