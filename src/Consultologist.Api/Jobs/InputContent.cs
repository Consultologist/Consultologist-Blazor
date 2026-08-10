using System.Text.RegularExpressions;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Jobs;

/// <summary>
/// Whether a required input actually carries a referral (#290).
///
/// On 2026-08-04 a message arrived with the referral attached through
/// OneDrive. Outlook put it in the body as a **link**, Graph never listed it
/// as an attachment, and <c>consult_draft</c> was filled from the body — a
/// URL. The engine then generated a complete, well-formed oncology consult
/// in which every section read "not documented", and emailed it out as two
/// encrypted PDFs. Nothing signalled that the referral was missing.
///
/// <see cref="ConsultGenerationJobStarter.ResolveEffectiveInputs"/> already
/// requires declared inputs to be present and non-whitespace. That was
/// satisfied: the body was present and was not whitespace. Presence is not
/// content.
///
/// **The rule is deliberately about prose, not length.** A genuinely terse
/// referral is legitimate and must generate:
///
/// <code>
/// 65M, newly diagnosed adenocarcinoma of the lung, stage IIIA,
/// for consideration of chemoradiation. PMHx HTN.
/// </code>
///
/// That is ~95 characters once whitespace is removed. A SharePoint URL is
/// *longer* than that, so raw length cannot separate them — which is why
/// URLs come out before anything is counted. The failing body reduces to
/// nothing; the referral above is untouched.
///
/// **A known limit, stated rather than solved.** A signature block is prose
/// and counts, so a body carrying only a link and "Regards, Dr X, Oncology"
/// can still clear the floor. This is a floor, not a content check — it
/// catches the blatant case, and #290 records the rest.
/// </summary>
internal static class InputContent
{
    /// <summary>
    /// Below this many non-URL, non-whitespace characters, a required input
    /// is treated as carrying no referral.
    ///
    /// Forty, and the asymmetry is deliberate. Too high refuses a real terse
    /// referral: the sender is told to re-send something that was fine —
    /// annoying, visible, and recoverable. Too low lets an empty one through
    /// and a document reaches a chart with nothing behind it — invisible, and
    /// the failure that produced #290. Err toward refusing.
    ///
    /// An app setting so a wrong floor can be corrected without a deploy.
    /// </summary>
    internal static int MinimumCharacters { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("Inputs__MinimumRequiredCharacters"), out var configured)
        && configured >= 0
            ? configured
            : 40;

    // Bare URLs and the autolinked forms mail clients produce. Deliberately
    // greedy to the next whitespace: a SharePoint link is one long token and
    // the whole of it is noise.
    private static readonly Regex Urls = new(
        @"\b(?:https?://|ftp://|www\.|mailto:)\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Characters left once URLs and whitespace are removed — what a reader
    /// would call the prose.
    /// </summary>
    internal static int MeaningfulLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var withoutUrls = Urls.Replace(text, " ");
        var count = 0;

        foreach (var ch in withoutUrls)
        {
            if (!char.IsWhiteSpace(ch))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Hosts that mean "the document is somewhere we cannot reach" (#291).
    ///
    /// Deliberately file-storage hosts only. A link to a guideline or a
    /// journal article is ordinary clinical prose and must not trigger
    /// anything — the signal here is specifically a document the sender
    /// believes they have sent.
    /// </summary>
    private static readonly string[] CloudStorageHosts =
    [
        "sharepoint.com",
        "1drv.ms",
        "onedrive.live.com",
        "drive.google.com",
        "docs.google.com",
        "dropbox.com",
        "box.com",
        "wetransfer.com"
    ];

    /// <summary>
    /// The id of the first required input that carries a cloud-storage link
    /// and came from no document (#291).
    ///
    /// **Both halves are the rule.** #290's content floor shipped at 15:02 on
    /// 2026-08-04; at 17:32 a message whose body held only a OneDrive link
    /// and a signature still generated a consult, because a greeting and a
    /// sign-off clear forty characters easily. Raising the floor is not the
    /// answer — a full signature block would clear two hundred, and every
    /// increase risks refusing a terse but genuine referral. The link is the
    /// only unambiguous signal.
    ///
    /// **No document origin** is what makes it precise. An input filled by a
    /// real attachment has one, so a link sitting elsewhere in the same
    /// message is incidental and ignored. An input filled from an email body
    /// or a typed draft has none — and if that text is pointing at a file we
    /// cannot open, the referral is not here.
    ///
    /// The remaining false positive is a clinician who types a full referral
    /// *and* pastes a cloud link. They are refused, and that is still the
    /// better answer: the linked document would otherwise be dropped without
    /// anyone knowing.
    /// </summary>
    internal static string? FindInputBehindACloudLink(
        ConsultGenerationRequest request,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string>? effective,
        IReadOnlyDictionary<string, ConsultInputOrigin>? origins)
    {
        bool FromNoDocument(string id) => origins?.ContainsKey(id) != true;

        if (manifest.SpecVersion < 7 || effective == null)
        {
            return FromNoDocument(ConsultGenerationJobStarter.ConsultDraftInputId)
                && HasCloudStorageLink(request.ConsultDraft)
                    ? ConsultGenerationJobStarter.ConsultDraftInputId
                    : null;
        }

        foreach (var declared in manifest.Inputs ?? [])
        {
            if (declared.Required
                && FromNoDocument(declared.Id)
                && HasCloudStorageLink(effective.GetValueOrDefault(declared.Id)))
            {
                return declared.Id;
            }
        }

        return null;
    }

    internal static bool HasCloudStorageLink(string? text) =>
        !string.IsNullOrEmpty(text)
        && CloudStorageHosts.Any(host => text.Contains(host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The id of the first required input carrying no referral, or null when
    /// every one of them does. Declaration order, so the message names the
    /// same input every time for the same request.
    /// </summary>
    internal static string? FindInputWithoutContent(
        ConsultGenerationRequest request,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string>? effective,
        int minimum)
    {
        if (minimum <= 0)
        {
            return null;
        }

        // v5/v6 declare no inputs: the draft is the input.
        if (manifest.SpecVersion < 7 || effective == null)
        {
            return MeaningfulLength(request.ConsultDraft) < minimum
                ? ConsultGenerationJobStarter.ConsultDraftInputId
                : null;
        }

        foreach (var declared in manifest.Inputs ?? [])
        {
            if (!declared.Required)
            {
                continue;
            }

            // #290 asks "is there a referral in here", which is a question
            // about prose. A typed value is short by nature — a date is ten
            // characters, an enum value a word, a boolean four letters — and
            // measuring it against a referral's minimum would reject every v8
            // package that declares one (#313). Its own type already
            // constrains it far more tightly than a length ever could.
            if (WorkflowInputTypes.Of(declared) != WorkflowInputTypes.Text)
            {
                continue;
            }

            if (MeaningfulLength(effective.GetValueOrDefault(declared.Id)) < minimum)
            {
                return declared.Id;
            }
        }

        return null;
    }
}
