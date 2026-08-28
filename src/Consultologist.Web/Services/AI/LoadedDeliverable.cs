namespace Consultologist.Web.Services.AI;

/// <summary>
/// #510: a deliverable of one of the account's previous runs, chosen for an
/// input slot. Text is what the picker fetched, shown as a preview; what runs
/// is what the server copies from the run when the job starts — the request
/// carries the reference, not this text.
/// </summary>
public sealed record LoadedDeliverable(
    string JobId,
    string ResultId,
    string Label,
    DateTimeOffset? RunAtUtc,
    string Text)
{
    public ConsultInputRef Reference => new(JobId, ResultId);

    public string RunName => RunAtUtc is { } at ? $"run of {at.ToLocalTime():MMM d, yyyy}" : "run " + (JobId.Length > 8 ? JobId[..8] + "…" : JobId);
}
