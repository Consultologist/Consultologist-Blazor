using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.Api.Models;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Email;

public sealed record EmailIntakeRunSummary(
    int Listed,
    int Accepted,
    int Rejected,
    int Skipped,
    int Repaired,
    // #266: parked in the Queued folder because the account was over its
    // submission limit, and given up on after MaxEmailDeferral respectively.
    int Queued = 0,
    int Expired = 0);

/// <summary>
/// The email-intake pipeline (#158, docs/ASYNC_DELIVERY.md §2). Per message:
/// claim FIRST (at-most-once for PHI jobs — a crash between claim and start
/// drops the message visibly rather than ever running it twice), then the
/// authentication floor, the sender gate, and the job start; disposition moves
/// the message to Processed/Rejected. Rejections are silent — no bounce, no
/// backscatter — and logged with metadata only, never subject or body.
/// </summary>
public sealed class EmailIntakeProcessor
{
    internal const string ProcessedFolder = "Processed";
    internal const string RejectedFolder = "Rejected";
    // #266: where a message waits out its account's rate limit. A folder
    // rather than an unread flag, because folder membership is a state an
    // operator can see and count, and read status is not a state machine.
    internal const string QueuedFolder = "Queued";
    private const int DefaultMaxMessagesPerPoll = 25;
    private const int DefaultMaxEmailDeferralHours = 2;
    private const int MaxDraftLength = 256 * 1024;
    // #237: bytes per attachment and across one message, matching the app
    // door (ConsultGenerationJobs.MaxInputFileBytes / MaxInputFilesTotalBytes).
    // The old 256 KB / 1 MB pair were text-shaped numbers from when this class
    // decoded attachments itself; a routine referral PDF exceeds both.
    //
    // The real ceiling may be lower and is not here: GraphMailClient reads
    // contentBytes inline from the attachments collection and silently skips
    // anything Graph declines to inline, so a message can arrive looking like
    // it had fewer attachments than it did.
    private const int MaxAttachmentLength = 10 * 1024 * 1024;
    private const int MaxTotalAttachmentBytes = 20 * 1024 * 1024;
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(10);

    private readonly IConfiguration _configuration;
    private readonly IGraphMailClient _mail;
    private readonly IEmailSenderResolver _senderResolver;
    private readonly IEmailIntakeClaimStore _claims;
    private readonly IConsultGenerationJobStarter _jobStarter;
    private readonly IWorkflowPackagePinResolver _pinResolver;
    private readonly IWorkflowPackageStore _packageStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailIntakeProcessor> _logger;

    public EmailIntakeProcessor(
        IConfiguration configuration,
        IGraphMailClient mail,
        IEmailSenderResolver senderResolver,
        IEmailIntakeClaimStore claims,
        IConsultGenerationJobStarter jobStarter,
        IWorkflowPackagePinResolver pinResolver,
        IWorkflowPackageStore packageStore,
        TimeProvider timeProvider,
        ILogger<EmailIntakeProcessor> logger)
    {
        _configuration = configuration;
        _mail = mail;
        _senderResolver = senderResolver;
        _claims = claims;
        _jobStarter = jobStarter;
        _pinResolver = pinResolver;
        _packageStore = packageStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EmailIntakeRunSummary> RunOnceAsync(DurableTaskClient client, CancellationToken cancellationToken)
    {
        var mailbox = _configuration["EmailIntake:MailboxAddress"];

        if (string.IsNullOrWhiteSpace(mailbox))
        {
            // The kill-switch: no mailbox configured (local dev, CI) → quiet no-op.
            _logger.LogDebug("Email intake mailbox not configured; poll skipped.");
            return new EmailIntakeRunSummary(0, 0, 0, 0, 0);
        }

        var top = _configuration.GetValue("EmailIntake:MaxMessagesPerPoll", DefaultMaxMessagesPerPoll);

        // #266: the Queued backlog first, and this ordering is a fairness
        // property rather than a tidiness one. Without it a steady stream of
        // new arrivals spends the account's budget every window and the
        // backlog behind it never drains.
        //
        // One budget across both listings. Twice `top` would double the work
        // a single poll can do, which is the number the timer cadence was
        // chosen against.
        var queued = await ListQueuedAsync(mailbox, top, cancellationToken);
        var refs = queued
            .Select(messageRef => (Ref: messageRef, Source: MessageSource.Queued))
            .Concat((await _mail.ListUnreadInboxMessagesAsync(mailbox, Math.Max(0, top - queued.Count), cancellationToken))
                .Select(messageRef => (Ref: messageRef, Source: MessageSource.Inbox)))
            .ToList();

        int accepted = 0, rejected = 0, skipped = 0, repaired = 0, requeued = 0, expired = 0;

        foreach (var (messageRef, source) in refs)
        {
            var outcome = await ProcessMessageAsync(client, mailbox, messageRef, source, cancellationToken);
            switch (outcome)
            {
                case MessageOutcome.Accepted: accepted++; break;
                case MessageOutcome.Rejected: rejected++; break;
                case MessageOutcome.Skipped: skipped++; break;
                case MessageOutcome.Repaired: repaired++; break;
                case MessageOutcome.Queued: requeued++; break;
                case MessageOutcome.Expired: expired++; break;
            }
        }

        var summary = new EmailIntakeRunSummary(refs.Count, accepted, rejected, skipped, repaired, requeued, expired);

        if (refs.Count > 0)
        {
            _logger.LogInformation(
                "Email intake poll complete. Listed={Listed}, Accepted={Accepted}, Rejected={Rejected}, Skipped={Skipped}, Repaired={Repaired}, Queued={Queued}, Expired={Expired}",
                summary.Listed,
                summary.Accepted,
                summary.Rejected,
                summary.Skipped,
                summary.Repaired,
                summary.Queued,
                summary.Expired);
        }

        return summary;
    }

    /// <summary>
    /// The Queued backlog, or nothing when the folder has never been created —
    /// which is the normal state until the first message is rate limited.
    /// EnsureInboxChildFolderAsync would create it eagerly on every poll, so
    /// this tolerates its absence rather than manufacturing an empty folder in
    /// every mailbox that never needs one.
    /// </summary>
    private async Task<IReadOnlyList<GraphMessageRef>> ListQueuedAsync(
        string mailbox,
        int top,
        CancellationToken cancellationToken)
    {
        try
        {
            var folderId = await _mail.FindInboxChildFolderAsync(mailbox, QueuedFolder, cancellationToken);

            return string.IsNullOrWhiteSpace(folderId)
                ? Array.Empty<GraphMessageRef>()
                : await _mail.ListFolderMessagesAsync(mailbox, folderId, top, cancellationToken);
        }
        catch (Exception ex)
        {
            // A failure here must not stop the Inbox being drained: new
            // referrals matter more than retries of old ones.
            _logger.LogError(ex, "Email intake could not list the Queued folder; continuing with the Inbox.");
            return Array.Empty<GraphMessageRef>();
        }
    }

    private enum MessageSource { Inbox, Queued }

    private enum MessageOutcome { Accepted, Rejected, Skipped, Repaired, Queued, Expired }

    private async Task<MessageOutcome> ProcessMessageAsync(
        DurableTaskClient client,
        string mailbox,
        GraphMessageRef messageRef,
        MessageSource source,
        CancellationToken cancellationToken)
    {
        var claimKey = messageRef.InternetMessageId ?? messageRef.Id;
        var now = _timeProvider.GetUtcNow();

        // #266: the first thing done to a queued message, before it is
        // claimed and before its body is fetched. That makes the bound a
        // property of the folder — nothing waits in Queued longer than
        // MaxEmailDeferral — rather than a property of still being over the
        // limit, and it costs an expired message no Graph reads at all.
        if (source == MessageSource.Queued && HasWaitedTooLong(messageRef, now))
        {
            return await ExpireAsync(mailbox, messageRef, claimKey, now, cancellationToken);
        }

        var claimed = await _claims.TryClaimAsync(
            new EmailIntakeClaim(claimKey, messageRef.Id, null, now),
            cancellationToken);

        if (!claimed)
        {
            return await RepairAsync(mailbox, messageRef, claimKey, now, cancellationToken);
        }

        var message = await _mail.GetMessageAsync(mailbox, messageRef.Id, cancellationToken);

        if (message == null)
        {
            await _claims.UpdateAsync(
                new EmailIntakeClaim(claimKey, messageRef.Id, null, now, Outcome: EmailIntakeOutcomes.Vanished),
                cancellationToken);
            return MessageOutcome.Skipped;
        }

        var verdict = EmailAuthenticationResults.Evaluate(message.InternetMessageHeaders);

        if (!verdict.Passes)
        {
            _logger.LogWarning(
                "Email intake rejected: authentication floor not met. From={From}, InternetMessageId={InternetMessageId}, Dmarc={Dmarc}, Spf={Spf}, Dkim={Dkim}",
                message.FromAddress,
                claimKey,
                verdict.Dmarc,
                verdict.Spf,
                verdict.Dkim);
            return await RejectAsync(mailbox, message, claimKey, now, EmailIntakeOutcomes.RejectedAuth, cancellationToken);
        }

        // The authenticated From — never Reply-To, which is attacker-choosable.
        var match = message.FromAddress == null
            ? new EmailSenderMatch(EmailSenderMatchOutcome.NoMatch)
            : await _senderResolver.ResolveAsync(message.FromAddress, cancellationToken);

        if (match.Outcome != EmailSenderMatchOutcome.Matched)
        {
            _logger.LogWarning(
                "Email intake rejected: sender gate. From={From}, InternetMessageId={InternetMessageId}, MatchOutcome={MatchOutcome}",
                message.FromAddress,
                claimKey,
                match.Outcome);
            return await RejectAsync(mailbox, message, claimKey, now, EmailIntakeOutcomes.RejectedSender, cancellationToken);
        }

        var draft = message.BodyText?.Trim();

        if (draft is { Length: > MaxDraftLength })
        {
            _logger.LogWarning(
                "Email intake rejected: body over the size bound. From={From}, InternetMessageId={InternetMessageId}, BodyLength={BodyLength}",
                message.FromAddress,
                claimKey,
                draft.Length);
            return await RejectAsync(mailbox, message, claimKey, now, EmailIntakeOutcomes.RejectedEmpty, cancellationToken);
        }

        // #210: attachments can carry the referral, so a blank body is only
        // fatal when nothing else arrived — the resolver decides that.
        var (attachments, attachmentError) = message.HasAttachments
            ? await FetchAttachmentsAsync(mailbox, message.Id, cancellationToken)
            : (Array.Empty<EmailInputAttachment>(), null);

        if (attachmentError != null)
        {
            _logger.LogWarning(
                "Email intake rejected: unusable attachment. From={From}, InternetMessageId={InternetMessageId}, Detail={Detail}",
                message.FromAddress,
                claimKey,
                attachmentError);
            return await RejectWithReplyAsync(
                mailbox, message, claimKey, now, match.AppUserId, EmailIntakeOutcomes.RejectedAttachments,
                cancellationToken, attachmentError);
        }

        var resolution = EmailAttachmentInputs.Resolve(
            await DeclaredInputIdsAsync(match.AppUserId!, cancellationToken),
            draft,
            attachments);

        if (resolution.RejectReason != null)
        {
            _logger.LogWarning(
                "Email intake rejected: inputs could not be assigned. From={From}, InternetMessageId={InternetMessageId}, Detail={Detail}",
                message.FromAddress,
                claimKey,
                resolution.RejectReason);
            var slug = attachments.Count > 0
                ? EmailIntakeOutcomes.RejectedAttachments
                : EmailIntakeOutcomes.RejectedEmpty;
            // The resolver's reasons are already written for the sender and
            // name only slot ids, which are authored package content rather
            // than patient data — the precedent #217 set for labels in replies.
            return attachments.Count > 0
                ? await RejectWithReplyAsync(
                    mailbox, message, claimKey, now, match.AppUserId, slug, cancellationToken, resolution.RejectReason)
                : await RejectAsync(mailbox, message, claimKey, now, slug, cancellationToken);
        }

        // #294: a named attachment outranks the body for the slot it names, so
        // a body can be dropped entirely. That is correct, and a covering note
        // is a legitimate thing to drop — but we had no idea how often a body
        // carrying real prose goes with it. The length only: a discarded body
        // is exactly as likely to be PHI as any other.
        if (resolution.DiscardedBodyCharacters > 0)
        {
            _logger.LogInformation(
                "Email intake discarded the message body: an attachment already filled its slot. InternetMessageId={InternetMessageId}, DiscardedBodyCharacters={DiscardedBodyCharacters}",
                claimKey,
                resolution.DiscardedBodyCharacters);
        }

        // #237: the body travels as text, the attachments travel as bytes.
        // The starter extracts them, so an emailed document records the same
        // origin the app door records — one mechanism, both doors.
        var start = await _jobStarter.StartAsync(
            client,
            new ConsultGenerationRequest(
                null,
                Inputs: resolution.Inputs is { Count: > 0 }
                    // Every emailed value is text — a body or a .txt
                    // attachment. A v8 package with a required boolean input
                    // is therefore unreachable by email, which is correct and
                    // named in package-format-v8-design.md § 4.
                    ? resolution.Inputs.ToDictionary(
                        pair => pair.Key,
                        pair => ConsultInputValue.OfText(pair.Value),
                        StringComparer.Ordinal)
                    : null,
                InputFiles: resolution.Files is { Count: > 0 }
                    ? resolution.Files.ToDictionary(
                        pair => pair.Key,
                        pair => new InputFilePayload(pair.Value.ContentType, pair.Value.Content),
                        StringComparer.Ordinal)
                    : null),
            match.AppUserId!,
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, message.FromAddress),
            cancellationToken);

        // #266: a rate limit is not a rejection. Every other start-failure
        // path below moves the message to Rejected and tells the sender their
        // consult could not be processed, which here would be false — the
        // message is fine and the account is merely ahead of its budget.
        if (start.Error == ConsultGenerationJobStartError.RateLimited)
        {
            return await QueueAsync(mailbox, message, claimKey, now, match.AppUserId, source, cancellationToken);
        }

        if (start.Error != null)
        {
            _logger.LogError(
                "Email intake job start failed. From={From}, InternetMessageId={InternetMessageId}, Error={Error}",
                message.FromAddress,
                claimKey,
                start.Error);
            // #237: an unreadable document is an attachment problem, not a
            // generic start failure. The claim table is the audit surface, and
            // this is the outcome anyone will actually come looking for.
            var outcome = start.Error switch
            {
                ConsultGenerationJobStartError.InputsMismatch => EmailIntakeOutcomes.RejectedInputs,
                ConsultGenerationJobStartError.InputFileUnreadable => EmailIntakeOutcomes.RejectedAttachments,
                // #290: the message arrived and parsed; there was simply no
                // referral in it to generate from.
                ConsultGenerationJobStartError.InputWithoutContent => EmailIntakeOutcomes.RejectedEmpty,
                ConsultGenerationJobStartError.InputBehindACloudLink => EmailIntakeOutcomes.RejectedCloudLink,
                // #369: the message and its inputs were all fine — the package
                // simply declares no document for them. Recorded distinctly
                // because "start-failed" reads as a fault, and this is the one
                // rejection where nothing went wrong.
                ConsultGenerationJobStartError.NoApplicableDeliverable => EmailIntakeOutcomes.RejectedNoDeliverable,
                _ => EmailIntakeOutcomes.StartFailed
            };
            await _claims.UpdateAsync(
                new EmailIntakeClaim(claimKey, message.Id, message.FromAddress, now, match.AppUserId, Outcome: outcome),
                cancellationToken);
            await DisposeMessageAsync(mailbox, message.Id, RejectedFolder, cancellationToken);
            await SendStartFailureReplyAsync(
                mailbox,
                message.FromAddress!,
                // #369: whatever the starter authorised for the sender, and
                // nothing else. This used to allowlist three error kinds, which
                // silently withheld the reason for the rest — a refused fire set
                // arrived as "could not be processed" and nothing more, though
                // the server had composed the sentence naming every deliverable
                // and what its condition wanted. An allowlist over kinds also
                // cannot express the actual rule: InputsMismatch is safe when it
                // names a missing declared input and unsafe when it quotes a
                // malformed value.
                start.SenderSafeDetail,
                cancellationToken);
            return MessageOutcome.Rejected;
        }

        await DisposeMessageAsync(mailbox, message.Id, ProcessedFolder, cancellationToken);
        await _claims.UpdateAsync(
            new EmailIntakeClaim(claimKey, message.Id, message.FromAddress, now, match.AppUserId, start.JobId, EmailIntakeOutcomes.Accepted),
            cancellationToken);

        _logger.LogInformation(
            "Email intake accepted. JobId={JobId}, InternetMessageId={InternetMessageId}",
            start.JobId,
            claimKey);

        return MessageOutcome.Accepted;
    }

    private async Task<MessageOutcome> RepairAsync(
        string mailbox,
        GraphMessageRef messageRef,
        string claimKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _claims.GetAsync(claimKey, cancellationToken);

        if (existing == null)
        {
            // Claim add raced with a delete-less 409 anomaly; leave for next tick.
            return MessageOutcome.Skipped;
        }

        if (existing.Outcome == EmailIntakeOutcomes.Queued)
        {
            // #266: the one non-terminal outcome. The message is parked in
            // Queued and started no job, so releasing the claim is safe and
            // the next poll retries it in full.
            //
            // Deleting here rather than at queueing time is what makes the
            // cycle self-healing: if the move to Queued failed, the row still
            // reads `queued` and this clears it, so the message is retried
            // from wherever it actually is instead of being repaired into the
            // Rejected folder by the stale-claim branch below.
            await _claims.DeleteAsync(claimKey, cancellationToken);
            return MessageOutcome.Queued;
        }

        if (existing.Outcome == null && now - existing.ClaimedAtUtc < StaleClaimAge)
        {
            // The winner is still in flight (this tick or a concurrent host).
            return MessageOutcome.Skipped;
        }

        if (existing.Outcome == null)
        {
            // The winner crashed between claim and start: the message never ran
            // and never will — the visible cost of the at-most-once bias.
            _logger.LogWarning(
                "Email intake repairing a crashed claim; the message was NOT processed. InternetMessageId={InternetMessageId}, ClaimedAtUtc={ClaimedAtUtc}",
                claimKey,
                existing.ClaimedAtUtc);
        }

        var folder = existing.JobId != null ? ProcessedFolder : RejectedFolder;
        await DisposeMessageAsync(mailbox, messageRef.Id, folder, cancellationToken);
        return MessageOutcome.Repaired;
    }

    /// <summary>
    /// The declared input ids of the account's pinned package, in declaration
    /// order — what positional assignment counts against. Empty for v5/v6 and
    /// whenever the package cannot be resolved: the starter re-resolves and is
    /// the authority on legality, so a miss here degrades to legacy assignment
    /// rather than failing the message. The store caches resolved versions, so
    /// this is a cache hit by the time the starter asks again.
    /// </summary>
    private async Task<IReadOnlyList<string>> DeclaredInputIdsAsync(string appUserId, CancellationToken cancellationToken)
    {
        try
        {
            var packageRef = await _pinResolver.ResolvePinAsync(appUserId, cancellationToken);
            var package = await _packageStore.ResolveAsync(packageRef, cancellationToken);

            return package.Manifest.Inputs?.Select(input => input.Id).ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email intake could not read the pinned package's declared inputs; assigning as legacy.");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Fetches the message's non-inline attachments as bytes (#237). It does
    /// not read them: what a format is, and whether these bytes are one, is
    /// the parser's question and it is asked once, at job start, for both
    /// doors (docs/DOCUMENT_INPUT.md § 1).
    ///
    /// Only size is judged here, because that is a property of the message
    /// rather than of a format.
    /// </summary>
    private async Task<(IReadOnlyList<EmailInputAttachment> Attachments, string? Error)> FetchAttachmentsAsync(
        string mailbox,
        string messageId,
        CancellationToken cancellationToken)
    {
        var listing = await _mail.ListAttachmentsAsync(mailbox, messageId, cancellationToken);

        // #249: something was attached that we could not read. Refuse the
        // message rather than proceeding on the part that did arrive — we
        // cannot know whether the unread part was the referral, and a consult
        // built from half of one and presented as whole is the failure this
        // is about. The same reasoning makes an ambiguous slot assignment a
        // rejection in #210 rather than a guess.
        if (listing.UnreadableKinds.Count > 0)
        {
            _logger.LogWarning(
                "Email intake found attachments it could not read. MessageId={MessageId}, Kinds={Kinds}",
                messageId,
                string.Join(", ", listing.UnreadableKinds));

            return (Array.Empty<EmailInputAttachment>(), DescribeUnreadable(listing.UnreadableKinds));
        }

        var attachments = new List<EmailInputAttachment>();
        var totalBytes = 0L;

        foreach (var attachment in listing.Files)
        {
            if (attachment.Content.Length > MaxAttachmentLength)
            {
                return (
                    Array.Empty<EmailInputAttachment>(),
                    $"An attachment is larger than {MaxAttachmentLength / (1024 * 1024)} MB.");
            }

            totalBytes += attachment.Content.Length;

            if (totalBytes > MaxTotalAttachmentBytes)
            {
                return (
                    Array.Empty<EmailInputAttachment>(),
                    $"The attachments come to more than {MaxTotalAttachmentBytes / (1024 * 1024)} MB in total.");
            }

            attachments.Add(new EmailInputAttachment(
                attachment.Name,
                attachment.ContentType,
                attachment.Content));
        }

        return (attachments, null);
    }

    /// <summary>
    /// The sentence the sender gets when something they attached could not be
    /// read (#249). Names the **kind**, never the file: a filename can itself
    /// be PHI, and replies on this path carry none (§ 6 of
    /// docs/DOCUMENT_INPUT.md).
    ///
    /// Each sentence has to tell them what to do differently, because "one
    /// attachment could not be read" alone would leave a clinician re-sending
    /// the identical message.
    /// </summary>
    internal static string DescribeUnreadable(IReadOnlyList<string> kinds)
    {
        if (kinds.Any(kind => kind.Contains("referenceAttachment", StringComparison.OrdinalIgnoreCase)))
        {
            return "An attachment is a link to a file rather than the file itself. "
                + "Please attach the document directly and re-send.";
        }

        if (kinds.Any(kind => kind.Contains("itemAttachment", StringComparison.OrdinalIgnoreCase)))
        {
            return "An attachment is a forwarded email rather than a file. "
                + "Please attach the document itself and re-send.";
        }

        return "An attachment could not be read. Please re-send it as a file attachment.";
    }

    /// <summary>
    /// Parks a message until its account's window resets (#266).
    ///
    /// **Order is load-bearing: mark, then reply, then move.** If the mark
    /// fails nothing has happened and the message is still an unread Inbox
    /// message with a null claim, which the existing stale-claim repair
    /// already handles. If the reply fails it is swallowed and logged and the
    /// move still happens. If the move fails the claim reads <c>queued</c>
    /// while the message sits in the Inbox, and the next poll's RepairAsync
    /// clears the row and retries it. Moving first would leave a message
    /// parked in Queued with a claim row saying nothing about why.
    /// </summary>
    private async Task<MessageOutcome> QueueAsync(
        string mailbox,
        GraphMessage message,
        string claimKey,
        DateTimeOffset now,
        string? appUserId,
        MessageSource source,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Email intake queued: the account is over its submission limit. From={From}, InternetMessageId={InternetMessageId}, FirstTime={FirstTime}",
            message.FromAddress,
            claimKey,
            source == MessageSource.Inbox);

        await _claims.UpdateAsync(
            new EmailIntakeClaim(claimKey, message.Id, message.FromAddress, now, appUserId, Outcome: EmailIntakeOutcomes.Queued),
            cancellationToken);

        // Exactly once, on the way in. The source folder is the whole test: a
        // message queued off the Inbox listing is a first-time queueing, one
        // re-listed from Queued is a retry. Replying on every retry would send
        // roughly thirty emails over a two-hour wait, and silence for two
        // hours is indistinguishable from a black hole — so it is neither.
        if (source == MessageSource.Inbox && message.FromAddress != null)
        {
            await SendQueuedReplyAsync(mailbox, message.FromAddress, cancellationToken);
        }

        var folderId = await _mail.EnsureInboxChildFolderAsync(mailbox, QueuedFolder, cancellationToken);
        await _mail.MoveMessageAsync(mailbox, message.Id, folderId, cancellationToken);

        return MessageOutcome.Queued;
    }

    /// <summary>
    /// Whether a queued message has waited longer than we are willing to make
    /// a clinician wait (#266).
    ///
    /// Received time is the clock, and it is the right one rather than a
    /// compromise: it measures what the sender experiences — how long since
    /// they sent it — not how long we happened to hold it. A null defers
    /// forever and never expires, because refusing a referral over missing
    /// metadata is the wrong failure direction.
    /// </summary>
    private bool HasWaitedTooLong(GraphMessageRef messageRef, DateTimeOffset now) =>
        messageRef.ReceivedDateTime is { } received
        && now - received > TimeSpan.FromHours(
            _configuration.GetValue("RateLimits:MaxEmailDeferralHours", DefaultMaxEmailDeferralHours));

    /// <summary>
    /// Gives up on a queued message and says so. The second and last reply of
    /// its life — the first said it was queued, so this completes a
    /// conversation rather than starting one, which is precisely why the same
    /// age test is never applied to the Inbox listing: after a poller outage
    /// every unread message is hours old, and auto-rejecting that backlog
    /// would tell senders who had heard nothing that they had failed.
    /// </summary>
    private async Task<MessageOutcome> ExpireAsync(
        string mailbox,
        GraphMessageRef messageRef,
        string claimKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Email intake giving up on a queued message: it waited longer than the deferral bound. InternetMessageId={InternetMessageId}, ReceivedAtUtc={ReceivedAtUtc}",
            claimKey,
            messageRef.ReceivedDateTime);

        var existing = await _claims.GetAsync(claimKey, cancellationToken);

        // Upsert-merge, so this lands whether or not the row survived the
        // retry cycle. The address comes from the claim: the message body was
        // never fetched, which is the point of checking at the listing.
        await _claims.UpdateAsync(
            new EmailIntakeClaim(
                claimKey,
                messageRef.Id,
                existing?.FromAddress,
                existing?.ClaimedAtUtc ?? now,
                existing?.AppUserId,
                Outcome: EmailIntakeOutcomes.RejectedRateLimit),
            cancellationToken);

        if (existing?.FromAddress != null)
        {
            await SendExpiredReplyAsync(mailbox, existing.FromAddress, cancellationToken);
        }

        await DisposeMessageAsync(mailbox, messageRef.Id, RejectedFolder, cancellationToken);
        return MessageOutcome.Expired;
    }

    /// <summary>
    /// A rejection the sender is told about: attachment problems are things
    /// they can fix by resending, unlike the silent auth and sender gates.
    /// </summary>
    private async Task<MessageOutcome> RejectWithReplyAsync(
        string mailbox,
        GraphMessage message,
        string claimKey,
        DateTimeOffset now,
        string? appUserId,
        string outcome,
        CancellationToken cancellationToken,
        string? cause = null)
    {
        await _claims.UpdateAsync(
            new EmailIntakeClaim(claimKey, message.Id, message.FromAddress, now, appUserId, Outcome: outcome),
            cancellationToken);
        await DisposeMessageAsync(mailbox, message.Id, RejectedFolder, cancellationToken);

        if (message.FromAddress != null)
        {
            await SendStartFailureReplyAsync(mailbox, message.FromAddress, cause, cancellationToken);
        }

        return MessageOutcome.Rejected;
    }

    private async Task<MessageOutcome> RejectAsync(
        string mailbox,
        GraphMessage message,
        string claimKey,
        DateTimeOffset now,
        string outcome,
        CancellationToken cancellationToken)
    {
        await _claims.UpdateAsync(
            new EmailIntakeClaim(claimKey, message.Id, message.FromAddress, now, Outcome: outcome),
            cancellationToken);
        await DisposeMessageAsync(mailbox, message.Id, RejectedFolder, cancellationToken);
        return MessageOutcome.Rejected;
    }

    private async Task DisposeMessageAsync(string mailbox, string messageId, string folder, CancellationToken cancellationToken)
    {
        await _mail.MarkReadAsync(mailbox, messageId, cancellationToken);
        var folderId = await _mail.EnsureInboxChildFolderAsync(mailbox, folder, cancellationToken);
        await _mail.MoveMessageAsync(mailbox, messageId, folderId, cancellationToken);
    }

    /// <param name="cause">
    /// #237: the sentence naming why, when there is one worth saying. It
    /// describes a file's format, never its contents — the sender already
    /// knows what they attached, so this leaks nothing and is the difference
    /// between resending the same fax and exporting a readable PDF. Filenames
    /// are still never echoed (docs/DOCUMENT_INPUT.md § 6).
    /// </param>
    /// <summary>
    /// Tells the sender their consult is queued (#266), so a wait is a wait
    /// rather than a silence. Says there is nothing to do, because there is
    /// not — re-sending would only add another message to the same queue.
    ///
    /// Same constraints as every reply on this path: no subject or body
    /// echoed, no filenames, no clinical content of any kind.
    /// </summary>
    private async Task SendQueuedReplyAsync(string mailbox, string toAddress, CancellationToken cancellationToken)
    {
        try
        {
            var appBaseUrl = _configuration["EmailIntake:AppBaseUrl"]?.TrimEnd('/');
            var body = "Your consult submitted by email has been received and is queued.\n\n"
                + "This account has submitted a lot in a short time, so this one is waiting its turn. "
                + "It will be processed automatically and you do not need to re-send it.\n\n"
                // Promise a follow-up rather than an outcome. Queued mail can
                // still be given up on (see ExpireAsync), and the first
                // production run of that path sent "you do not need to
                // re-send it" and then "please re-send it" ninety seconds
                // apart. Whichever way it goes, this sentence stays true, and
                // the second email completes a promise instead of reversing
                // one. Deliberately vague about the interval: naming hours
                // here would couple the copy to a setting that can change
                // without a deploy.
                + "If it does not go through, we will write again and ask you to re-send.\n"
                + (appBaseUrl == null ? string.Empty : $"\nYou can also use the app directly:\n{appBaseUrl}\n")
                + "\nThis message intentionally contains no clinical content.";

            await _mail.SendMailAsync(mailbox, toAddress, "Your consult email is queued", body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email intake queued reply could not be sent.");
        }
    }

    /// <summary>
    /// Tells the sender their queued consult was given up on (#266) — and
    /// says plainly that nothing is wrong with what they sent.
    ///
    /// It has its own subject and opening line rather than reusing
    /// <see cref="SendStartFailureReplyAsync"/>, which the first production
    /// run of this path did. That opened with "your consult could not be
    /// processed" under the subject "Your consult email could not be
    /// processed" — byte-identical to the reply for a scan with no text
    /// layer, and so a clinician whose message was fine was told to go
    /// looking for a fault in their document.
    ///
    /// The milestone already settled this elsewhere: <c>busy</c> says
    /// "Nothing is wrong with this one" and the preview endpoint's 429 says
    /// "Nothing is wrong with this file". A refusal that is about us rather
    /// than the document has to say so, on every door.
    /// </summary>
    private async Task SendExpiredReplyAsync(string mailbox, string toAddress, CancellationToken cancellationToken)
    {
        try
        {
            var appBaseUrl = _configuration["EmailIntake:AppBaseUrl"]?.TrimEnd('/');
            var body = "Your consult submitted by email is no longer queued.\n\n"
                + "It waited longer than this account's submission limit allowed. "
                + "Nothing is wrong with the message or its attachments — please re-send it.\n"
                + (appBaseUrl == null ? string.Empty : $"\nYou can also use the app directly:\n{appBaseUrl}\n")
                + "\nThis message intentionally contains no clinical content.";

            await _mail.SendMailAsync(mailbox, toAddress, ExpiredReplySubject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email intake expiry reply could not be sent.");
        }
    }

    internal const string ExpiredReplySubject = "Your queued consult email was not processed";
    internal const string StartFailureReplySubject = "Your consult email could not be processed";

    private async Task SendStartFailureReplyAsync(
        string mailbox,
        string toAddress,
        string? cause,
        CancellationToken cancellationToken)
    {
        try
        {
            var appBaseUrl = _configuration["EmailIntake:AppBaseUrl"]?.TrimEnd('/');
            var body = "Your consult submitted by email could not be processed.\n\n"
                + (cause == null ? string.Empty : cause + "\n\n")
                + "You can re-send the consult email to try again"
                + (appBaseUrl == null ? "." : $", or use the app directly:\n{appBaseUrl}\n")
                + "\nThis message intentionally contains no clinical content.";

            await _mail.SendMailAsync(mailbox, toAddress, StartFailureReplySubject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email intake start-failure reply could not be sent.");
        }
    }
}
