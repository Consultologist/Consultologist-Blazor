# Async Delivery: Scheduled Runs, Email Intake, Encrypted Documents

**Status: the whole arc is implemented — #157 (scheduled runs), #158
(email intake), and #159 (encrypted delivery); see the decision records
in each section.** Settled in the 2026-07-20 discussion and filed as a
cross-linked arc. This doc is the arc's design record; the issues point
here.

Composition: **email in → scheduled batch overnight → link (or encrypted
document) back in the morning.** Each part also stands alone.

## Foundation: the engine is already asynchronous

A consult job is a Durable Functions orchestration: submission returns a job
id, the engine runs server-side, the record persists, and the done event
lives in the events table. SSE/polling is live *viewing*, never a
requirement — the client is optional after submission, and **History is the
canonical result surface**. Everything below builds on that fact rather
than adding queue infrastructure.

## 1. Scheduled runs (#157) — IMPLEMENTED 2026-07-25

Submit now, run later (overnight), result waiting in History.
Implementation decisions (settled 2026-07-25):

- **Mechanism**: `ScheduledAtUtc` on the job request (7-day horizon; past
  values simply run immediately — clock-skew friendly). The orchestrator
  sleeps on `context.CreateTimer` between entity Initialize and
  MarkRunning, so the job is visible as **Scheduled** (new status between
  Queued and Running) while sleeping; the guard uses
  `context.CurrentUtcDateTime` for replay determinism.
- **Surface**: a "Run overnight (~2:00 AM browser-local)" preset switch in
  the Consults setup phase — no arbitrary datetime picker in v1. A
  scheduled submit never enters the run phase (nothing to stream);
  History shows the amber Scheduled badge plus "runs {local time}".
- **Completion signal**: History always, plus part 2's reply machinery
  reused — the completion-email gate is simply "reply address present":
  email intake sets the sender; the HTTP endpoint sets the account's
  **verified delivery address** for every app job, immediate or scheduled
  (§4, #486 — until 2026-08-26 it set the unverified token-claim email,
  and only for scheduled jobs). Requires the `EmailIntake__*` settings
  (skipped with a warning otherwise).
- **Not in v1**: cancellation of a Scheduled job (deferred — a wrong
  schedule costs one harmless run; follow-up issue filed).
- **Retention statement** (the deliberate consideration): a scheduled
  draft rests in Durable orchestration state up to the 7-day horizon
  before processing — the same storage account, encryption posture, and
  access controls as running and completed jobs; scheduling extends
  duration, not exposure surface. Since #368 (2026-08-25)
the produced text of every terminal job — the record's documents and
sections, the orchestration history's prompts and outputs, the streamed
snapshots — is deleted `TextRetention__Days` after completion (7) by a
daily sweep; the record's hashes, nodes, refs and labels stay forever, and
the record says when the text went (`textDroppedAtUtc`).

## 2. Email intake (#158) — IMPLEMENTED 2026-07-25

Submit consults by email; results announced by reply; runs recorded in
History like any other. Implementation decisions (settled 2026-07-25;
files: `src/Consultologist.Api/Email/*`, settings in
`docs/CONFIGURATION.md` "Email consult intake"):

- **Polling, not webhooks**: a TimerTrigger (2-minute NCRONTAB) lists
  unread mail — zero public surface, no subscription lifecycle; the
  processor is webhook-reusable if latency ever matters.
- **Disposition**: accepted → `Inbox/Processed`, everything else →
  `Inbox/Rejected` (debuggable v1), and since #266 rate-limited →
  `Inbox/Queued`, with an Exchange retention policy expected on the
  mailbox — the folders hold PHI at rest.
- **The `Queued` folder (#266, 2026-08-01)**: when the sender's account
  is over its hourly submission limit the message is parked rather than
  rejected, because every other failure path here replies that the
  consult could not be processed and for a rate limit that would be
  false. The sender is told **once**, on the way in, that it is queued
  and needs no action — and which of those two things happens is decided
  by the folder the message was listed from, so replying exactly once
  needs no counter and no stored flag.

  A child folder's messages never appear in the Inbox listing, so each
  poll makes **two calls, `Queued` first**, sharing one
  `MaxMessagesPerPoll` budget. Queue-first is fairness: otherwise new
  arrivals spend the account's budget every window and the backlog never
  drains. The order on the queue path is **mark, then reply, then move**
  — every failure then either self-corrects or degrades to behaviour
  that already exists.

  `queued` is the first non-terminal value in `EmailIntakeOutcomes`:
  `RepairAsync` releases the claim and the message is retried in full,
  safe because a queued message started no job. A message that outlives
  `RateLimits__MaxEmailDeferralHours` is given up on with one further
  reply — checked at the `Queued` listing, before the message is claimed
  or read, and **never against the Inbox listing**, since after an
  outage every unread message is old and auto-rejecting that backlog
  would tell senders who had heard nothing that they had failed. Full
  rationale in `docs/DOCUMENT_INPUT.md` § 9.
- **Authentication floor** (corrected after the first production e2e):
  intra-tenant mail arrives via authenticated submission with NO
  SPF/DKIM/DMARC stamps, so the floor accepts
  `X-MS-Exchange-Organization-AuthAs: Internal` (EOP strips that header
  family from external mail — unforgeable) alongside the external
  `dmarc=pass` / `spf=pass`+`dkim=pass` paths.
- **Sender matching**: normalized equality against `AppUsers.Email`
  requiring exactly one match with Active status; zero, many, or
  non-Active → silent ignore (logged; no bounce — no backscatter).
- **Exactly-once**: an `EmailIntakeProcessed` claim row (keyed by
  internetMessageId) is written atomically BEFORE the job starts —
  at-most-once by design: a crash in the claim→start window drops the
  message visibly (stale-claim repair + warning) rather than ever
  running a PHI job twice.
- **Attachments as inputs (#210, 2026-07-28; reshaped by #237,
  2026-07-29)**: attachments fill the pinned package's declared input
  slots — a filename stem claims the slot it names and outranks the body
  for it (a body is often just a signature), the body then takes
  `consult_draft` if still free, and one leftover file fills one
  leftover slot. Wider ambiguity, or bytes over 10 MB per attachment
  (20 MB across all of them), rejects the message with
  `rejected-attachments`; inline parts (signature logos) are ignored
  entirely. A blank body is no longer fatal when an attachment carries
  the referral.
- **Email does not read attachments (#237)**: it routes them. Bytes
  travel to the job start as `inputFiles` and the parser reads them
  there, which is the same path the Consults page takes — so an emailed
  document records the same provenance an attached one does, and there
  is one place that knows what a format is
  (`docs/DOCUMENT_INPUT.md` § 1). Formats are no longer listed here:
  whatever the parser reads, email accepts. A package declaring no
  inputs (v5/v6) has one implicit slot and refuses attachments outright,
  saying so rather than silently concatenating them into the body. The
  reply now names the cause — a scan, a size, an ambiguous assignment —
  because that describes a file's format rather than its contents. The
  fax bridge (#188) stays blocked on OCR (#239) alone.
- **A refusal carries its reason when the reason is authored** (#369).
  The starter marks each start-failure sentence `SenderSafeDetail` when it
  is composed of manifest content and request structure — declared input
  ids, result labels, condition literals — and leaves it null when the
  sentence quotes a **supplied value**. The email door forwards that and
  nothing else. This is not an allowlist over error kinds and cannot be:
  `InputsMismatch` is safe as *"Required input(s) 'seen_on' missing"* and
  unsafe as *"…must be written YYYY-MM-DD; got '1965-03-02x'"*, a date of
  service being the plain case. An input id is likewise unsafe when it is
  the **caller's** — on this door an id is an attachment's filename stem,
  and a filename can itself be PHI (#217). Before this, three error kinds
  were allowlisted and the rest replied with no cause at all, so a refused
  fire set (#315) arrived as "could not be processed" while the server
  held the sentence naming every deliverable and what its condition
  wanted. Its claim outcome is `rejected-no-deliverable`, kept apart from
  `start-failed`: nothing went wrong, the package simply declares no
  document for those inputs.
- **Replies**: sent for Completed AND Failed from the orchestrator
  (activity after FinalizeJob); always a fresh message, never a Graph
  /reply (which would quote the PHI-bearing original); fixed subjects;
  body = boilerplate + `/history/{jobId}` deep link.

- **Mechanism**: a dedicated mailbox (e.g. `consults@…`) read via
  Microsoft Graph — the Function App holds `Mail.Read`/`Mail.Send` scoped
  to that one mailbox; a Graph change notification fires on new mail.
- **Identity**: the sender is matched to a registered account's email;
  **DKIM/SPF verification is the floor**; the activation gate applies
  (unregistered or inactive senders are ignored or bounced without
  detail).
- **Provenance**: the email body becomes the draft; the record stamps
  `source: email`; the input hash covers the body — History shows the run
  identically to app-submitted jobs.
- **Replies never carry PHI by default**: the reply is a History deep link
  ("your consult is ready"); the user clicks through and authenticates
  normally. The encrypted attachment (part 3) is the opt-in upgrade.
- **Honest boundary**: a From address is not an Entra identity. Spoofing a
  registered sender could inject junk runs into that user's history — but
  the reply goes to the real mailbox, so output never leaks to the
  attacker. Keep PHI out of subjects and bodies in both directions;
  metadata is never protected.

## 3. Encrypted single-document delivery (#159) — IMPLEMENTED 2026-07-25

Once format v6 (#152) made the deliverable one assembled document, email
delivery can attach it as an encrypted file. Implementation decisions
(settled 2026-07-25; files: `Email/ConsultDocumentPdf.cs`, the reply
activity, `Account/DeliveryPassword` endpoints):

- **16-character minimum** password (user decision — offline brute force
  is the threat model), max 128; stored under the write-only
  `delivery.documentPassword` settings key: dedicated PUT/DELETE
  endpoints, the generic settings routes refuse the key in both
  directions, and existence surfaces only as Account/Me's
  `DocumentPasswordSet`.
- **AES-256 via PDFsharp 6.2** (PDF 2.0 encryption V5,
  `SetEncryptionToV5`); MigraDoc composes from the Markdig AST with the
  client preview's pipeline semantics (HTML disabled, soft breaks hard),
  Liberation Sans embedded (PDFsharp Core resolves no Linux system
  fonts). Attachment filename `consult-{jobId8}.pdf` — no PHI.
- **Attaches on any completion reply** (email intake and scheduled runs)
  when the password is set and the run Completed with a document; Failed
  runs and password-less accounts get link-only replies; any failure in
  the attachment leg degrades to link-only, never silence.
- **Characters the font cannot draw are folded before rendering (#252,
  2026-08-02)** — `Email/RenderableText.cs`, and the one place this project
  edits the *delivered* clinical text.

  A font with no glyph does not leave a blank: it draws `.notdef`, and the
  PDF's ToUnicode map then correctly records that the character means
  nothing, so a reader copies a **control character into the chart**.
  Measured: Liberation Sans has no U+2011 NON-BREAKING HYPHEN, and a
  consult reading `hormone‑blocking` copied out of Outlook on the web as
  `hormone␂blocking`. `Email/FontGlyphCoverage.cs` reads the embedded
  font's `cmap` so the font itself is the authority on what it can draw.

  **The bar for a substitution is the same mark, not a similar one.**
  U+2011 → U+2010 differ only in whether a line may break there, and in a
  fixed-layout PDF that difference is already spent; figure/thin/narrow
  spaces → space; zero-width joiners and BOMs are dropped because they
  have no visual. That is the whole table. Deliberately **not** done, in
  the register of `DOCUMENT_INPUT.md` § 2's refusals: de-hyphenating,
  transliterating accents, normalising a µ or a ≤. Those would be
  corrections to clinical prose, and every one of those characters is in
  the font anyway.

  Each substitution is **conditional on the gap** — it applies only when
  the font genuinely lacks the original, so adopting a wider font retires
  entries rather than changing behaviour.

  **The defect was never a missing ToUnicode map**, which is what #252's
  title says and what its body proposed fixing. PDFsharp already emits a
  `/Type0` `/Identity-H` font carrying one for everything outside
  Windows-1252, and it works — a correction is recorded on the issue.

  **A character with no same-mark stand-in becomes U+25A1 WHITE SQUARE
  (#287, 2026-08-05)**, and is counted under its **original** codepoint —
  the count says which characters are arriving, and recording the mark
  would answer nothing. Codepoints only, never the surrounding prose
  (`Codepoints=U+XXXXxN`).

  Keeping it was the first answer and too generous: the font drew
  `.notdef`, whose ToUnicode entry correctly says the glyph means nothing,
  so the copy carried **U+0000** and Outlook on the web additionally
  dropped the character *following* it — `here` reaching a chart as `ere`.

  U+25A1 rather than U+FFFD because Liberation Sans has no U+FFFD
  (measured), and rather than `?` because a bare question mark in clinical
  prose reads as authored uncertainty — "dose ? mg". Liberation Sans's
  `.notdef` is itself a hollow rectangle (glyph 0, two contours), so the
  page barely moves while the copy buffer is fixed; the two outlines are
  not identical in proportion or weight, so this is near-identical rather
  than unchanged. The shape does not vary by reader — the font is embedded
  — though the *copy* behaviour did, which was the defect.

  **This is the one lossy edit in the class**: the original codepoint is
  gone from the delivered document. That is why it is counted and warned
  about at delivery, where every other entry in the table is a
  same-mark swap that changes nothing a clinician reads.
- **The document carries a generic `Consult` title** — it shows in a mail
  client's preview and a reader's title bar, so it can hold nothing about
  the patient — and `/Lang` `en-CA` for screen readers (#252).

### Per-deliverable delivery (#217) — IMPLEMENTED 2026-07-27

Format v7 (#209) made a package's deliverable a *set*, so the reply
carries one encrypted PDF per deliverable:

- **Filename `{resultId}-{jobId8}.pdf`** — the authored result id is
  package content, never patient data. v7's single-result sugar id is
  `consult`, so a single-deliverable job produces byte-identical
  filenames to v6's `consult-{jobId8}.pdf`.
- **The body names the documents** by authored label ("Consultation
  note, Patient letter are attached…") so the recipient can see the set
  is complete before decrypting anything.
- **Degrade whole, at a 2 MB raw budget.** Graph caps a sendMail request
  near 3 MB and base64 inflates bytes by ~1.33x, so the rendered set is
  dropped entirely when it exceeds 2 MB and the reply says the documents
  were too large to send by email. Attaching only what fits would
  misrepresent the consult; letting Graph reject the request would cost
  the reply itself, and the retry would fail identically.

- **Format**: password-protected PDF, AES-256 (PDF 2.0's KDF) — the flow
  clinicians already know, opening everywhere with no tooling. The
  assembled markdown renders to PDF server-side (license-friendly library,
  e.g. PDFsharp).
- **The password is user-set on the Profile page**, stored through the
  existing account-settings machinery but treated as a secret: write-only
  UI (shows set / not set, never echoed), excluded from setting reads,
  with genuine strength enforcement — an attachment can be brute-forced
  offline with no rate limiting, so length matters.
- **Explicit over default**: no password set → the reply contains only the
  deep link, never a document. The encrypted attachment is opt-in by
  setting the password. Replies always include the link as the
  forgotten-password fallback; rotation is safe by construction — History
  is canonical, so a lost password loses nothing.
- **Trust boundary, stated honestly**: the password protects the document
  in the mailbox (interception, compromise, mis-forwarding). It is not
  end-to-end secrecy from the server, which produced the plaintext and
  holds the password to encrypt — the same boundary the app has today.

## 4. A verified delivery address, and every job delivered (#486) — IMPLEMENTED 2026-08-26

The 2026-08-14 direction on #368 — *deliver the note, do not store it* —
now has its delivery half; the 7-day sweep (§1) stays the floor. Decisions
(user, 2026-08-25):

- **One address, confirmed by code.** `delivery.address` is set only by
  `POST Account/DeliveryAddress` (a six-digit CSPRNG code goes to the
  address; `delivery.addressPending` holds the address, the code's salted
  SHA-256, expiry and attempts) followed by `POST …/Confirm` within 15
  minutes and five attempts. Nothing is ever sent to an address that did
  not answer its code. The generic settings routes refuse both keys.
  `AppUsers.Email` — a token claim, unverified, not unique — is never an
  address any more, for scheduled jobs either: an account without a
  verified address gets no email, and is told so before it submits.
  > **Amended 2026-08-28 (#517).** One more way to verify: a user signed
  > in with an **organisation's** account may take the token's own email as
  > the address in one click (`POST Account/DeliveryAddress/UseSignedIn`),
  > because the organisation's sign-in already verified that mailbox is
  > theirs; a personal Microsoft account keeps the code. The account
  > records which trust the address rests on (`delivery.addressVerifiedBy`).
  > Nothing is sent to an address that neither answered a code nor was
  > vouched for by its organisation on the token that chose it.
- **Every app job is delivered** to that address — immediate and scheduled
  alike — by the same reply the email door sends: fixed subject,
  boilerplate plus the History link, the document only ever as the
  encrypted attachment (§3). **A link-only reply counts as delivered**
  when no delivery password is set; the record says whether the document
  was attached. Email-door jobs still reply to the matched sender.
  > **Amended 2026-08-28 (#518).** Unless the account chose otherwise:
  > the profile option *Email each run's PDF* (`delivery.emailPdf`,
  > `true` | `false`; absent = not chosen = sent, as before) turns the
  > completion email off for app-initiated runs, immediate and scheduled.
  > The choice is read **at start**, beside the address, and carried on
  > the orchestration input (`emailRequested`, appended last; a payload
  > from before reads as requested), so a choice changed mid-run does not
  > change what was promised; a reschedule is a new start and reads it
  > again. The reply leg then records `not-requested` before it looks at
  > the address — the user's choice is what History says, whatever the
  > address state — and never invokes the delivery activity. Consults
  > says so before submit; History shows *not emailed* / *Not emailed —
  > by your choice*. Email-door runs always reply to their sender.
- **Delivery is recorded on the job**: `deliveryOutcome` (`sent`,
  `failed`, `address-not-set`, `not-configured`, `not-requested` since
  #518), `deliveredAtUtc`,
  `deliveryDocumentAttached`, and a `delivery` History line — never the
  address, which is the account's. History badges every row
  `delivered` / `not delivered` and dates it in the provenance panel;
  Consults carries the same line on a completed run. A record from before
  says nothing.
- **The sweep clock is unchanged**: completion + `TextRetention__Days`,
  for every job. "N days after confirmed delivery" was considered and not
  taken — delivery happens at completion, so the two clocks differ by
  seconds, and the period itself is #368's policy.
