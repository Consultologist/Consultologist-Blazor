namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1505 (specVersion 31, area 08; part 06 § 7): how a deliverable came out.</summary>
public enum DeliverableState
{
    /// <summary>Composed, and its check passed, was skipped by its own <c>if</c>, or it has none: it goes on to delivery.</summary>
    Released,

    /// <summary>Composed empty under <c>whenEmpty: hold</c>: not released, and the job goes on.</summary>
    Held,

    /// <summary>Its render failed, or it composed empty under <c>whenEmpty: fail</c>: the job fails with it.</summary>
    Failed,

    /// <summary>Its header <c>if</c> did not hold.</summary>
    Skipped,

    /// <summary>Composed, and its gate check did not pass or could not run: withheld, never delivered (R2).</summary>
    Blocked,
}

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 §§ 7, 8; part 08 § 10): one deliverable's outcome, for delivery. Key: its row key
/// (<c>deliverable.emr.note</c>). Label: its <c>label</c>. Format: its format's name (<c>plaintext</c>,
/// <c>markdown_block</c>, or an object format's bundle key). Folder: <c>""</c> for <c>deliverables/</c>, else the folder's
/// id. Reason: part 08 § 10's <c>stateReason</c> (<c>null</c> for a plain release). Check: the gate's row key, or
/// <c>null</c>. Text and ContentHash: the composed text and its hash, <c>null</c> when skipped or the render failed (kept
/// for a blocked or held one, for the record). Delivery and Bundle: its entry's <c>delivery</c> kinds and folder
/// <c>bundle</c>.
/// </summary>
public sealed record DeliverableOutcome(
    string Key,
    string Label,
    string Format,
    string Folder,
    DeliverableState State,
    string? Reason,
    string? Check,
    string? Text,
    string? ContentHash,
    IReadOnlyList<string> Delivery,
    string? Bundle);
