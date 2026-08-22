using System.Text.RegularExpressions;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Email;

/// <summary>
/// One inbound attachment, as bytes (#237). Nothing in the email path reads
/// it: the parser is the only thing that knows what a format is, and it runs
/// at job start for both doors (docs/DOCUMENT_INPUT.md § 1).
/// </summary>
public sealed record EmailInputAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Assigns an email's body and attachments to a package's declared input slots
/// (#210). Pure: the processor does the Graph and Durable work, this decides
/// where things land — and since #237, only that. It routes; it does not read.
///
/// The sender can never be told where a file went — replies carry no PHI, and a
/// filename can itself be PHI ("Smith_John_referral.pdf"). So a positional
/// assignment is unverifiable by construction: fine when one attachment has one
/// place to go, a silent wrong-data error when two could be swapped. Ambiguity
/// is therefore refused rather than guessed.
///
/// v9 (#428): a slot declared an array of text takes several documents, and
/// the sender names their order — <c>prior_notes-1.pdf</c>,
/// <c>prior_notes-2.docx</c> — because order is significant in the hash and
/// the only ordering signal a message carries is the one the sender wrote.
/// Numbered 1 to n without gaps or repeats, or refused: a guess here would be
/// the swap this class exists to refuse. Input ids are snake_case, so the
/// hyphen is never part of one.
/// </summary>
public static class EmailAttachmentInputs
{
    public const string ConsultDraftInputId = "consult_draft";

    /// <summary>
    /// Where the message's parts landed: <c>Inputs</c> is the body — the only
    /// thing here that is already text — and <c>Files</c> the attachments,
    /// keyed by the slot each one fills.
    /// </summary>
    public sealed record Resolution(
        IReadOnlyDictionary<string, string>? Inputs,
        // #428: a slot's documents in the sender's order; one document is a
        // one-element list.
        IReadOnlyDictionary<string, IReadOnlyList<EmailInputAttachment>>? Files,
        string? RejectReason,
        // #294: characters of body text that had nowhere to go. A named
        // attachment outranks the body for the slot it names, so the body is
        // simply dropped — silently, until now. Counted rather than kept: a
        // discarded body is exactly as likely to be PHI as any other.
        int DiscardedBodyCharacters = 0)
    {
        public static Resolution Rejected(string reason) => new(null, null, reason);
    }

    /// <summary><c>&lt;slot&gt;-&lt;n&gt;</c>: the stem of a numbered attachment.</summary>
    private static readonly Regex NumberedStem = new(@"^(?<slot>.+)-(?<n>[1-9][0-9]*)$", RegexOptions.Compiled);

    /// <param name="declaredInputs">
    /// The package's declared slots in declaration order. Empty for v5/v6,
    /// whose only slot is the frozen consult_draft convention.
    /// </param>
    public static Resolution Resolve(
        IReadOnlyList<WorkflowInputSpec> declaredInputs,
        string? body,
        IReadOnlyList<EmailInputAttachment> attachments)
    {
        var declaredInputIds = declaredInputs.Select(input => input.Id).ToList();

        var trimmedBody = body?.Trim() ?? string.Empty;
        var hasBody = trimmedBody.Length > 0;

        if (!hasBody && attachments.Count == 0)
        {
            return Resolution.Rejected("The message carried neither a usable body nor an attachment.");
        }

        // v5/v6 declare nothing, so there is one implicit slot and a file has
        // nowhere of its own to go. This used to concatenate attachments into
        // the body; #237 refuses instead. The concatenation only worked while
        // email decoded files itself, and "this workflow has no slot for that"
        // is the honest answer rather than a silent merge.
        if (declaredInputIds.Count == 0)
        {
            if (attachments.Count > 0)
            {
                return Resolution.Rejected(
                    "This workflow accepts a single input and cannot take an attachment. "
                    + "Paste the referral into the message instead.");
            }

            return new Resolution(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ConsultDraftInputId] = trimmedBody
                },
                null,
                null);
        }

        var files = new Dictionary<string, IReadOnlyList<EmailInputAttachment>>(StringComparer.Ordinal);
        var numbered = new Dictionary<string, List<(int Number, EmailInputAttachment Attachment)>>(StringComparer.Ordinal);

        // Stems are matched before the body claims anything: naming a file
        // after a slot is a deliberate act, while a body may be nothing but
        // the signature a mail client appended. A named attachment therefore
        // outranks the body for the slot it names.
        var unmatched = new List<EmailInputAttachment>();

        foreach (var attachment in attachments)
        {
            var stem = Path.GetFileNameWithoutExtension(attachment.FileName);
            var plain = Declared(declaredInputs, stem);

            if (plain != null)
            {
                if (numbered.ContainsKey(plain.Id))
                {
                    return Resolution.Rejected(BothPlainAndNumbered(plain.Id));
                }

                if (files.ContainsKey(plain.Id))
                {
                    // Two attachments naming the same slot — genuinely
                    // ambiguous, unlike a body the sender may not have
                    // written. An array slot included: unnumbered, their
                    // order is not something we can confirm back.
                    return Resolution.Rejected($"More than one input was supplied for '{plain.Id}'.");
                }

                files[plain.Id] = new[] { attachment };
                continue;
            }

            // #428: a numbered stem for a declared slot. For any name that
            // is not one, the file is unmatched exactly as before — a sender
            // whose "fax-2.pdf" is their only attachment is not numbering.
            var match = NumberedStem.Match(stem);

            if (match.Success && Declared(declaredInputs, match.Groups["slot"].Value) is { } spec)
            {
                if (!TakesSeveral(spec))
                {
                    return Resolution.Rejected(
                        $"Input '{spec.Id}' takes one document and cannot be numbered. Name the file '{spec.Id}' instead.");
                }

                if (files.ContainsKey(spec.Id))
                {
                    return Resolution.Rejected(BothPlainAndNumbered(spec.Id));
                }

                // A number too large for an int is a gap by any reading.
                var number = int.TryParse(match.Groups["n"].Value, out var parsed) ? parsed : int.MaxValue;

                if (!numbered.TryGetValue(spec.Id, out var list))
                {
                    numbered[spec.Id] = list = new List<(int, EmailInputAttachment)>();
                }

                list.Add((number, attachment));
                continue;
            }

            unmatched.Add(attachment);
        }

        // Numbered 1 to n, each once: the sender's order, as written. The
        // sender's own numbers are filename fragments and are not echoed.
        foreach (var (slot, list) in numbered)
        {
            var numbers = list.Select(entry => entry.Number).Order().ToList();

            if (numbers.Distinct().Count() != numbers.Count)
            {
                return Resolution.Rejected(
                    $"Input '{slot}' has more than one document with the same number. Number each file once.");
            }

            if (!numbers.SequenceEqual(Enumerable.Range(1, numbers.Count)))
            {
                return Resolution.Rejected(
                    $"Input '{slot}' is numbered with gaps. Number its files 1 to {numbers.Count} without gaps.");
            }

            files[slot] = list.OrderBy(entry => entry.Number).Select(entry => entry.Attachment).ToList();
        }

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);

        var bodyUsed = hasBody
            && declaredInputIds.Contains(ConsultDraftInputId, StringComparer.Ordinal)
            && !files.ContainsKey(ConsultDraftInputId);

        if (bodyUsed)
        {
            inputs[ConsultDraftInputId] = trimmedBody;
        }

        // #294: the body is dropped whenever an attachment already claimed
        // the slot it could have filled. That is correct — a named file
        // outranks a body that may be nothing but a signature — but until now
        // it happened without a trace.
        //
        // A dropped body carrying a cloud-storage link is the one case where
        // it is evidence rather than noise: the sender attached a document
        // through OneDrive, Graph never listed it (#291), and the consult ran
        // without it. That produced a clinically detailed note whose
        // Medications section read "not documented" while reading as complete
        // — the failure #294 records.
        //
        // Everything else dropped here is counted, not refused. A covering
        // note is legitimate and must keep working; the count is what will
        // tell us, with evidence rather than a guess, whether more is needed.
        var discarded = hasBody && !bodyUsed ? trimmedBody.Length : 0;

        if (discarded > 0 && InputContent.HasCloudStorageLink(trimmedBody))
        {
            return Resolution.Rejected(
                "The message points at a file stored in the cloud rather than attaching it. "
                + "We cannot open linked files — please attach the document to the message and re-send.");
        }

        if (unmatched.Count == 0)
        {
            return new Resolution(inputs, files, null, discarded);
        }

        // A slot the body already took is not free: the same slot cannot be
        // supplied as both text and a file, and the job start refuses it.
        var free = declaredInputIds
            .Where(id => !files.ContainsKey(id) && !inputs.ContainsKey(id))
            .ToList();

        if (unmatched.Count > free.Count)
        {
            return Resolution.Rejected(
                "More attachments were supplied than the workflow has inputs for. Name each file after the input it belongs to.");
        }

        // One file only. Two or more is where a swap becomes possible and
        // unconfirmable, so it is refused.
        if (unmatched.Count > 1)
        {
            return Resolution.Rejected(
                "Several attachments could fill several inputs and the order is not something we can confirm back to you. Name each file after the input it belongs to.");
        }

        // A lone attachment fills the FIRST free slot, which is not the same
        // as having only one place to go — there may be several free. It is
        // still the right reading: one document and no name is a referral, and
        // declaration order puts the required slot first (the fax-bridge shape
        // pinned by BlankBody_LetsTheAttachmentBecomeTheDraft).
        //
        // A package declaring an optional input before the required one would
        // send it to the optional slot, but that fails loudly rather than
        // quietly: the required slot stays empty and the job start refuses with
        // "Required input(s) '…' missing." (#232).
        files[free[0]] = new[] { unmatched[0] };
        return new Resolution(inputs, files, null, discarded);
    }

    private static WorkflowInputSpec? Declared(IReadOnlyList<WorkflowInputSpec> declaredInputs, string stem) =>
        declaredInputs.FirstOrDefault(input => string.Equals(input.Id, stem, StringComparison.OrdinalIgnoreCase));

    /// <summary>An array of text is the one slot that takes several documents (v9 § 7).</summary>
    private static bool TakesSeveral(WorkflowInputSpec spec) =>
        WorkflowInputTypes.Of(spec) == WorkflowInputTypes.Array
        && (spec.Items ?? WorkflowInputTypes.Text) == WorkflowInputTypes.Text;

    private static string BothPlainAndNumbered(string slot) =>
        $"Input '{slot}' was supplied both as '{slot}' and as numbered files. Number every file for it, or send one.";
}
