# Document Input: One Parser, Two Sources

**Status: design record for #234 (settled 2026-07-28). Implemented and
verified in production — #235 the parser, #236 the app source, #237 the
email source, #238 files at job start, #240 DOCX, #241 hardening, #242
the decoding defect, plus #251 lone-CR normalisation, #253 the extractor
id, and #254/#256 the verification tooling. One thing is left, and it is
tracked in #266: per-account rate limiting.** v7 declared named input slots (#209) and
both intake paths then stalled at `.txt`/`.md`, which is not the shape
referrals arrive in. Decisions taken with the operator: the file itself
is submitted rather than text extracted from it in the browser;
extraction is text-layer only, so OCR and #188's fax parity stay
deliberately blocked; and a second format (DOCX) ships in the same
milestone so the parser's seam is tested rather than asserted.

## Foundation: extraction is a pre-step, not a format change

Every intake path converges on `Dictionary<string, string>` before
anything else happens. `EmailAttachmentInputs.Resolve` took text that
was already decoded (until #237 made it route bytes instead);
`ConsultGenerationRequest.Inputs` is a string map;
`ConsultGenerationJobStarter.ResolveEffectiveInputs` validates strings
against the package declaration; `ComputeDeclaredInputsHash` hashes
strings. Nothing downstream of that map is format-aware.

So a component that turns bytes into a string, running before the map is
built, changes nothing else. `package-format-v7-design.md` already fixed
this boundary from both ends: § 7's closure set puts *"non-text inputs
(files bind through extraction … never as binary inputs)"* out of scope,
and § 11 names extraction as the thing that *"feeds text into slots and
stays outside the format"*.

**There is no `specVersion` 8 here.** The package format does not learn
about documents; the intake layer does.

## 1. The seam (normative)

```
                    PARSER  (sole format authority)
              sniff → decode/extract → named outcome
                 owns: txt · md · pdf · docx
                      ↑                ↑
              SOURCE: app        SOURCE: email
              (bytes only)       (bytes only)
```

Decisions (settled 2026-07-28; files:
`src/Consultologist.Api/Documents/*`, the two sources at
`src/Consultologist.Web/Pages/Consults.razor` and
`src/Consultologist.Api/Email/EmailIntakeProcessor.cs`):

- **One parser, because two would diverge.** The email path must parse
  server-side — nothing else can read a mailbox. A second copy in the
  browser would mean the same PDF yields two different strings depending
  on which door it came through, and the effective-input hash would then
  describe the door as much as the document. One implementation is not a
  tidiness preference; it is what makes the hash mean something.
- **The parser is the only thing that knows a format exists.** It owns
  the supported-type set, the sniffing, the per-format extractors, the
  per-format caps, and the outcome vocabulary. Sources hand it bytes and
  render or map what comes back.
- **Sniffing beats filenames.** Dispatch is on content. A source-side
  filename gate would be a second authority that can disagree with the
  first — a `.txt` full of PDF bytes is the case that exposes it — so
  the source-side gates are **deleted**, not extended:
  `UploadExtensions` in `Consults.razor` and
  `ReadableAttachmentExtensions` in `EmailIntakeProcessor`. A declared
  content type (the browser's, or Graph's
  `GraphInboundAttachment.ContentType` — captured since #210 and, until
  #237, consulted by nothing) is a hint passed to the sniffer, never an
  authority.
- **What the app source keeps**: one format-agnostic byte cap, checked
  before bytes move so a 500 MB file is not uploaded merely to be
  refused; and `accept` on the `InputFile`, which stays a file-picker
  hint with no code gate behind it. The existing comment already says
  the attribute is only a hint; after this it is the whole truth of it.

> **Amended 2026-07-29 during #236.** `accept` is **gone**, not kept.
> Leaving it would have put a format list back in the client — and #240
> would then have had to edit `Consults.razor` to add DOCX, which is
> precisely the diff the acceptance criterion below forbids. A hint that
> only stays correct if a later PR breaks the rule is not a hint worth
> keeping. The picker now shows every file and an unsupported one is
> refused after a round trip, which the file model pays for anyway.
- **What the email source keeps**: the per-message total-attachment
  budget, which is a property of the message rather than of a format —
  one 8 MB PDF and eight 1 MB PDFs cost a mailbox the same.
- **Acceptance criterion**: adding a format touches the parser only,
  with zero edits to either source. #240 is the test — if that PR
  reaches `Consults.razor` or `EmailIntakeProcessor.cs`, the seam leaked
  and the abstraction is wrong. Better to learn that while it is one
  week old.

## 2. The parser (normative)

`Documents/` under `src/Consultologist.Api/`, namespace mirroring the
folder as every other folder does. It does not belong inside `Email/`
merely because `ConsultDocumentPdf` (the *rendering* side) lives there —
that class exists solely for email delivery, whereas the parser has
three callers.

- **Pure core, thin wrapper.** The sniff-and-extract logic is a pure
  function over bytes, testable the way `EmailAttachmentInputs.Resolve`
  and `SendEmailIntakeReplyActivity.ApplyBudget` are. `InternalsVisibleTo`
  is already set for the test project, so `internal static` is enough.
- **A registry keyed by sniffed type**, not a `switch` on extension.
  Registering an extractor is the whole cost of a new format.
- **A closed outcome vocabulary**, following `EmailIntakeOutcomes` in
  `Email/EmailIntakeClaimStore.cs`: a `public static class` of `public
  const string`, PascalCase members, kebab-case values, per-member `//`
  comments citing the issue that motivated them. The repo's rule, worth
  stating because it is nowhere written down: **persisted entity
  statuses are PascalCase** (`AccountStatuses`,
  `ConsultGenerationJobStatuses`), **dispositions and wire identifiers
  are kebab-case** (`EmailIntakeOutcomes`, `IdentityProviders`). A parse
  result is a disposition.

  ```text
  extracted            text came out, and there is some of it
  unsupported-type     the bytes are not a format we read
  corrupt              the right format, structurally broken
  password-protected   encrypted with a user password
  no-text-layer        a PDF with pages but no text — a scan or a fax
  empty                parsed cleanly, contained no text at all
  too-large            over the per-file byte cap
  too-many-pages       over the page cap
  expands-too-large    a container whose decompressed size is over cap
  too-much-text        extracted more characters than an input may hold
  timed-out            the parse outran its wall clock
  ```

> **Amended 2026-07-28 during #235.** `timed-out` was added when the
> wall-clock timeout moved into #235 from § 9: a hang was otherwise the
> one way the parser could fail without returning a named outcome, which
> made the vocabulary a promise it did not keep. `expands-too-large`
> arrives with the container format that needs it (#240).

- **Normalisation is conservative and universal.** `\r\n` → `\n`, a lone
  `\r` → `\n`, and trailing whitespace trimmed — the shape already used
  by `AgentAttestationService` for canonicalising text before
  comparison. Nothing semantic: line structure, hyphenation and hard
  wrapping survive untouched.
- **Applied to every input, not only extracted ones.** Today
  `ResolveEffectiveInputs` normalises nothing, so a CRLF `.txt` and the
  same text pasted by hand are already different input to the hash.
  Normalising only what the parser produces would preserve that split
  across the two doors, which is the exact property this milestone is
  trying to guarantee. So normalisation belongs at job start, over the
  whole supplied map.
- **No hash-version bump.** `ComputeDeclaredInputsHash` and
  `DeclaredInputsHashVersion = 3` are unchanged. The *definition* —
  SHA-256 over the canonical JSON of the ordinal-sorted supplied map —
  still holds; only the text reaching it is cleaner. `provenance.md` is
  explicit that definitions are never compared as equals across
  versions, so a bump would cost comparability against every existing v7
  job to record nothing new. *(True of #238. v8 and v9 did move the
  version — to 4 for typed values and 5 for structured ones — for reasons
  of their own, never for normalisation; since #422 the normaliser also
  runs over every text scalar inside an array or object.)*

> **Amended 2026-07-31 during #251.** The rule covers **a lone `\r` as
> well as `\r\n`**. It did not, and the gap was exactly the failure this
> section argues against: classic Mac endings — still emitted by older
> dictation systems, and what some PDF viewers put on the clipboard —
> hashed differently from the same referral typed by hand, so the claim
> above was broader than the code. Latent rather than live, since no
> shipped extractor emits bare CR.
>
> **One normaliser now, not four copies.** `CanonicalText.Normalize` is
> called by the job starter, the parser and `AgentAttestationService`.
> The rule having been written out four times is what let two of them
> drift from it.
>
> **The agent-definition redaction is deliberately not a caller**, which
> the paragraph above should not be read as claiming. That transform's
> contract is byte-equivalence with a `sed` expression, and `sed`'s line
> model is `\n`-only: canonicalising a lone `\r` first would split a
> bare-CR manifest into many lines where `sed` sees one, so the two would
> produce different documents.
>
> *Corrected 2026-07-31 (#259).* This first said startup attestation
> enforces published == `Redact(manifest)`, repeating a claim from that
> class's own summary. No code path did that, and `Redact` had no
> production caller — the equivalence was held by tests, not at runtime.
>
> *Superseded 2026-08-05 (#259).* `AgentDefinitionRedaction` is deleted.
> The redaction lives once, in the agents repo's
> `publish-agent-definition.sh`; the reason above is why that script does
> not canonicalise, and is kept here because it is the same judgement
> `CanonicalText` makes about how far to go.
>
> Still no hash-version bump, for the reason given above: the definition
> is unchanged and only the text reaching it is cleaner.
- **Not in v1**: semantic repair — de-hyphenating words split across a
  line break, rejoining hard-wrapped lines into paragraphs, stripping
  repeated page headers. Each would produce better prompt input, and
  each can corrupt clinical text: rejoining lines merges a one-per-line
  medication list into a run-on, and a dropped "header" may be the
  letterhead date. The read-only preview (§ 5) means the clinician sees
  the ragged text and can choose to paste instead, which is a safer
  answer than a clever one.

## 3. Formats

### Text — `.txt`, `.md`

Decoding is the parser's job, and it is a **fix**, not a port. #242
records the defect: `EmailIntakeProcessor` decodes with a blind
`Encoding.UTF8.GetString`, which substitutes U+FFFD rather than
throwing, so it fails silently. Measured against a realistic line:

| Source encoding | What a consult would be generated from |
|---|---|
| UTF-8, no BOM | correct |
| UTF-8 with BOM | an invisible U+FEFF prepended to the input |
| UTF-16 LE | `��R\x00e\x00f\x00…` — complete garbage |
| Windows-1252 | `R�sum� of prior notes � see attached.` |

The browser's `StreamReader` detects BOMs by default and consumes them,
so **the server currently reads text files worse than the browser does**
— and the same `.txt` through the two doors produces two different
effective-input hashes. Detect the BOM, fall back sensibly without one,
and both doors agree.

### PDF

> **Amended 2026-07-28 during #235**, once the library was measured
> rather than read about. Confirmed as designed: an owner-password-only
> PDF opens with no special handling, so the decision below needs none;
> and `NumberOfPages` is available after `Open` before any page content
> is decoded, so the page cap lands where § 4 wants it. Changed by what
> was found: extraction uses **`ContentOrderTextExtractor.GetText`**,
> because PdfPig's own documentation says not to use `page.Text` unless
> you know what you are doing — it returns content-stream order, not
> reading order. `Open` is **eager** — header, cross-reference table
> including a brute-force rescan of the whole file when that table is
> damaged, and the page-tree walk — which is where the library's
> historical hangs lived, so the § 9 timeout wraps the whole parse and
> not a page loop. `ParsingOptions.MaxStackDepth` is lowered well below
> its default of 256: a crafted PDF can drive deep recursion into
> `StackOverflowException`, which .NET **cannot catch** — it takes the
> worker process down along with every invocation sharing it. PdfPig
> 0.1.15 guards the one known path; that residual risk is real, accepted
> for now, and bounded by the fact that only an activated account can
> reach the parser until #237. Finally the exception taxonomy is wider
> than two types: PdfPig's open issues #1268 and #1277 document
> `IndexOutOfRangeException` and `NullReferenceException` escaping from
> files whose cross-reference offsets go unchecked, so everything except
> `OutOfMemoryException` maps to `corrupt`.

- **UglyToad PdfPig**, Apache 2.0, fully managed, no native assets. The
  deciding axis is managed-versus-native, not licence: PDFsharp's Core
  build resolving no fonts on Linux cost an embedded-font workaround
  (`Consultologist.Api.csproj`, the `Fonts/` folder), and that lesson
  should not be re-bought on a PHI path maintained by one person.
  Managed code also turns a memory-safety bug in a hostile file into an
  exception rather than heap corruption inside the Function App.
- **Pin the exact version.** PdfPig is pre-1.0 and does not follow
  SemVer below 1.0, so a floating range could change extracted bytes for
  identical input — which is provenance-affecting (§ 7). Central Package
  Management pins every Api package exactly already.
- **Password-protected, two cases, not one.**
  - *A user password* → refuse, naming the cause. Do not prompt for a
    password on a clinical intake form; that teaches clinicians to type
    passwords into whatever asks.
  - *Encrypted with an empty user password* (owner-password or
    permissions-only, which hospital systems emit constantly) →
    **extract**. Every viewer opens these without prompting, and
    permission flags are advisory on a document the recipient already
    holds. Refusing would reject a large share of legitimate referrals.
- **Honest boundary**: the parser must never try the account's delivery
  password against an inbound encrypted PDF. The app *sends* documents
  encrypted with that password (#159), so a user replying with one of
  our own PDFs would appear to "work" — which turns intake into a
  decryption oracle and quietly makes the delivery password an ingest
  credential. This is written down because it is a helpful-looking
  feature somebody would otherwise add.

### DOCX (#240)

- **`DocumentFormat.OpenXml`**, MIT, fully managed — the same rule that
  chose PdfPig.
- **Sniffing cannot stop at magic bytes.** A `.docx` opens
  `PK\x03\x04` like every zip, so a byte-prefix table cannot tell it
  from an `.xlsx` or a plain archive. The sniffer opens the package and
  confirms the OPC content types and the `word/document.xml` part. This
  is the case that proves the sniffer is a dispatcher rather than a
  lookup table.
> **Amended 2026-07-30 during #240**, after measuring rather than reading.
> The naive walk is worse than the design assumed: `InnerText` on a
> document whose author changed a dose from 5 mg to 10 mg yields **"Dose
> is 10 mg5 mg daily."** — both values, adjacent, in clinical text — and
> runs a medication table together as "Amlodipine5 mgRamipril10 mg".
> Hidden text comes through too. So the walk is explicit about
> paragraphs, cells and rows, not only about revisions.
>
> **Headers and footers are included**, which § 1 left unsaid. A
> referral's date and clinic often live only in the letterhead, and
> dropping content the sender supplied is the silent loss refused
> everywhere else here; a `.docx` stores them once rather than per page.
>
> **The record says when a revision layer was resolved.** Taking the
> accepted view drops content that was in the file — correct, but still a
> drop, so `ConsultInputOrigin` carries it and History shows it.
>
> Two library facts, both measured: a valid `.xlsx` **opens** as a
> `WordprocessingDocument` and yields a `MainDocumentPart`, so the
> content type is the discriminator and a spreadsheet is
> `unsupported-type` rather than `corrupt`; and `OpenSettings` leaves
> `MaxCharactersInPart` **unbounded**, which the two-argument `Open`
> passes straight through, so the three-argument overload is used
> deliberately. The decompression bound lands here rather than in § 9 —
> deferring it would have shipped the vector and deferred the mitigation.

- **Extract the accepted view.** A `.docx` can carry `w:del` runs — text
  the author deleted and believes is gone — as well as comments and
  hidden text. Honour `w:ins`, drop `w:del`, exclude comments and hidden
  runs: what the sender sees on screen is what the consult reads.
  Surfacing deleted clinical text would be silent wrong data, the
  failure class #210 refused positional assignment over and #242 records
  as a live bug.

> **Amended 2026-07-30 during #254.** The two Word documents these claims
> were verified against are built by
> `scripts/make-input-fixtures.cs` from `scripts/fixtures/`, so the runs
> can be repeated rather than remembered. `consult_draft.docx` carries
> the same text as the `.txt` and `.pdf` fixtures, which is what makes a
> cross-door hash comparison mean anything, and puts the clinic and date
> in a header part so the header decision above is visible in extracted
> text. `prior_notes.docx` is the only fixture carrying both `w:del` and
> `w:ins` runs, so it is the only one that exercises the accepted view
> end to end.

> **Amended 2026-07-31 during #256.** The rest of the verification is
> committed too, so every claim in this record can be re-checked from a
> clean checkout and a token:
>
> | | |
> | --- | --- |
> | `scripts/make-pdf-fixtures.cs` | `referral-text.pdf`, `referral-scan.pdf` |
> | `scripts/make-input-fixtures.cs` | `consult_draft.docx`, `prior_notes.docx`, `prior_notes_utf16.txt`, `prior_notes_big.txt` |
> | `scripts/show-extraction.sh` | what the parser reads out of any file |
> | `scripts/verify-document-provenance.sh` | the § 7 provenance run (three real consults) |
> | `scripts/show-job-provenance.sh` | which documents an existing consult actually read (read-only) |
>
> **`referral-scan.pdf` is the only artifact that reaches
> `no-text-layer`**, which § 8's copy and the provenance run both depend
> on. It had one witness and no way to make another; it is now a page
> bearing a 1×1 JPEG and no glyphs, built in code like every other
> fixture here.
>
> **`referral-text.pdf` is laid out by MigraDoc**, not drawn with
> `XGraphics.DrawString`, which does not wrap — the text has to flow into
> lines for this to be a realistic text layer. Its extracted text is
> therefore *not* identical to `consult_draft.txt`, which is why the
> cross-format hash comparison uses the `.txt` and `.docx` rather than
> the PDF.
>
> **The provenance run builds its request bodies from the fixtures at
> send time.** It used to post four `payload-*.json` files kept beside
> it: a stored copy of a fixture can go stale against it silently, and a
> run posting the wrong bytes still passes. `--print-payload` shows what
> would be sent without sending it.

## 4. Limits

One number (256 KB) currently serves both "bytes of file" and
"characters of input", in three places that reference each other by
comment. That stopped working the moment a document could be a
container: a 5 MB scan may yield 20 KB of text, a 200 KB text PDF may
yield 400 KB, and a 1 MB zip may expand to a gigabyte.

| Bound | Value | Why it is separate |
|---|---|---|
| Bytes per file | 10 MB | What arrives |
| Pages parsed | 100 | Parse cost is per page, not per byte |
| Decompressed size | 100 MB | A container's cost is not its size |
| Characters extracted | 256 KB | `ConsultGenerationJobs.MaxInputLength` |
| Characters per slot, all its documents | 256 KB | The same bound, summed after extraction (#428) |
| Bytes per email, all attachments | 20 MB | A message-level property |
| Bytes per job request, all files | 20 MB | Likewise, per request |

- **Fail loudly on the character cap; never truncate.** Half a referral
  silently generating a whole consult is a clinical wrong-data error.
- **Caps stay code constants.** `CONFIGURATION.md` already states this
  for the email bounds, and there is no `IOptions` binding anywhere in
  the Api — a settings-shaped cap would be a new pattern for no gain.
- **Starting values, not measurements.** These are sized from fax and
  EMR-export norms; revise them against real referral samples rather
  than defending them.

> **Amended 2026-07-28 during #235.** The byte cap bounds **input, not
> memory**. PdfPig's `ObjectLocationProvider` caches every object it
> resolves for the document's lifetime with no eviction and no bound —
> its issue #371 reports 4–6 GB of working set for a 15 MB file — so
> peak memory is a multiple of the cap rather than a fraction of it. The
> mitigations available here are keeping the document's lifetime as
> tight as possible and treating out-of-memory as reachable; an actual
> memory bound is #241's.

> **Amended 2026-08-22 during #428.** Two character caps, and they
> compose. The parser bounds each document at 256 KB as before; a slot
> filled by **several** documents (v9, `package-format-v9-design.md`
> § 7) is also held as a whole to `MaxInputLength` — the same bound the
> door holds typed text to — summed at the starter **after** extraction,
> where the total is first knowable. Over is refused as `InputTooLong`
> (422; the email door files it as a rejected attachment), never
> truncated: *"Input 'prior_notes' exceeds 256 KB across its 3
> documents."* Raw extracted lengths, before normalisation, as the door's
> are. A slot may list at most 256 documents, the array bound.

## 5. The app source

- **A slot holds either typed text or an attached file, never both.**
  Attaching replaces the textarea with a file chip and a **read-only
  preview** of the extracted text; removing the file restores the
  textarea with whatever was there before.
- **Why the text is not editable.** If the client submitted text, then
  "this slot was machine-read from a PDF" would be a claim the client
  makes about itself, while the same statement on the email path is a
  fact the server observed. Asymmetric trust in a provenance record is
  worse than no record. Submitting the file makes both doors mean the
  same thing — and removes the single-use receipt and state table an
  earlier draft of this design needed to work around the asymmetry.
- **Why there is a preview at all.** Extraction is lossy on columns,
  tables, headers and footers. A clinician must be able to see what the
  machine read before it becomes the basis of a consult; if it is
  garbled they remove the file and paste instead. The preview is also
  load-bearing elsewhere: the run-phase draft bar and
  `ConsultJobMemento.Inputs` (#207 re-attach) both need the input text,
  and under this model the client only has it because the preview
  returned it.

### Preview endpoint

```text
POST /api/DocumentExtractions
```

- `AuthorizationLevel.Anonymous` with the standard prologue —
  `IAccountAuthorizer.AuthorizeAsync`, then `AccountAuthorizer.IsActive`,
  then `FunctionCors.Apply` on every response, and `"options"` handled
  as an extra verb. Anonymous is the house meaning of "bearer token, not
  a Functions key".
- Body is the **raw bytes** with the declared content type. Not
  multipart — that would add a parser for untrusted input to serve one
  file per request — and not base64, which inflates a PHI-bearing body
  by a third.
- Returns the extracted text, the extractor identity, and the page
  count. **Persists nothing.** It is a POST because it carries a body,
  not because it creates a resource.
- **The filename never reaches the server.** The client keeps it for the
  "loaded X" label; the API sees bytes. A filename can itself be PHI —
  `Smith_John_referral.pdf` — and a request-scoped one would land in
  Functions request logging. Free PHI minimisation.
- The Function App origin is already in `staticwebapp.config.json`'s
  `connect-src`, so no CSP change is needed.

### Submission

> **Amended 2026-07-29 during #238.** One thing the design above did not
> account for: `ConsultGenerationJobStarter` carries the **whole request**
> into `ConsultGenerationOrchestrationInput`, which Durable persists to
> the storage account and spills to blob past the inline limit. Passing
> the request through unchanged would therefore put every attached
> document at rest — a 10 MB file becomes ~13 MB of base64 per job — in
> a store with no retention story and no place in the PHI posture
> `ACCOUNTS.md` and `CONFIGURATION.md` describe. **The starter clears
> `InputFiles` before constructing the orchestration input**, which is
> what keeps the "bytes are never persisted" promise true rather than
> merely intended. Nothing downstream needs them: the extracted text is
> already carried by `Inputs`. A test asserts it, because the next person
> to touch that constructor will not otherwise see it.
>
> Also added here, since the app path had no equivalent of the email
> budget: a per-request total across `InputFiles`, at the same 20 MB. A
> per-file cap does not bound a request carrying several.

The job request carries the **file**, not the text, as a trailing
optional field beside `Inputs`:

```text
POST /api/ConsultGenerationJobs
{ "inputs":     { "consult_draft": "typed text…" },
  "inputFiles": { "prior_notes": { "contentType": "application/pdf",
                                   "content": "<base64>" } } }
```

- `System.Text.Json` serialises `byte[]` as base64 natively, so this
  rides the existing JSON path — no multipart parser, no blob-plus-SAS
  upload, no staging table, and **no PHI at rest** between attaching and
  submitting.
- Supplying both text and a file for one slot is **rejected**; file ids
  are validated against the declared set exactly as
  `ResolveEffectiveInputs` validates unknown ids.
- The server extracts again at job start. That is deterministic for the
  same bytes and the same pinned extractor, so the preview was honest,
  and the server's result is what runs and what gets hashed.
- `ConsultGenerationJobs.StartAsync` buffers the whole body with
  `ReadToEndAsync` before deserialising. A 10 MB file becomes ~13 MB of
  base64 and then a ~26 MB UTF-16 string; deserialise from the stream
  instead so the bytes are parsed once.

> **Amended 2026-08-22 during #428.** `inputFiles` maps a slot to a
> **list** of documents, in the order supplied; one document is a
> one-element list:
>
> ```text
> { "inputFiles": { "prior_notes": [ { "contentType": "application/pdf", "content": "<base64>" },
>                                    { "contentType": "text/plain",      "content": "<base64>" } ] } }
> ```
>
> Only a slot declared `type: array, items: text` takes several — each
> document becomes one element, in that order, which is the order the
> elements hash in. A `text` slot takes exactly one and the starter
> refuses more by name once it knows the declaration: *"Input
> 'consult_draft' is a text and takes one document; declare it an array
> of text to supply several."* One document into an array slot is a
> one-element array; one into a text slot is the text it always was, so
> hash definitions 3 and 4 are unchanged. The door bounds each document
> as it bounded the one and names a document by its position — counted
> from one, only when there is more than one: *"Input file 'prior_notes'
> document 2 is empty."* An empty list is refused like an empty file.
> The #290 content floor measures the **slot**: an array of text is
> prose, however supplied, and one thin document among four is not an
> empty referral. The setup form still attaches one file per slot; the
> multi-file picker is #429's.

## 6. The email source

- `GraphMailClient.ListAttachmentsAsync` reads each non-inline attachment's
  bytes and `EmailIntakeProcessor.FetchAttachmentsAsync` maps the outcome
  onto `EmailIntakeOutcomes`. Inline parts (signature logos) are skipped
  before the parser sees them, as today. (The design text below said
  `ReadAttachmentsAsync`, which never shipped under that name; where the
  bytes go changed with #237, amended below.)
- **One bad attachment still fails the whole message.** #210's reasoning
  is unchanged and stronger for documents: the alternative generates a
  consult from a body reading only "please see attached". A PDF that
  yields no text is exactly that case.
- **The reply names the cause, never the file.** Today every attachment
  failure sends one generic line. Once PDFs are accepted, a scanned fax
  will be the most common rejection, and a sender who is told nothing
  will simply resend the same fax. The failure is a property of the
  format, not of the contents, and the sender already knows what they
  attached — so naming it leaks nothing and makes the problem fixable.
  Filenames are still never echoed, and the subject is unchanged.
> **Amended 2026-07-29 during #237.** Email now sends attachment **bytes**
> to the job start rather than text, so the parser reads them in the same
> place it reads the app door's — one extraction site, and an emailed
> document records the same origin an attached one does. Two consequences
> the design above did not state. A package declaring **no inputs**
> (v5/v6) now **refuses attachments**: it has one implicit slot, the old
> behaviour concatenated them into the body, and that only worked while
> email decoded files itself. And the reply names the cause for every
> rejection here, not only the unreadable ones — the resolver's reasons
> were already written for the sender and name only slot ids, which are
> authored package content.

- **Honest boundary**: this is the one place the no-PHI reply rule bends
  toward saying more, and it is worth stating why it does not break.
  "We could not read one of your attachments" describes our capability;
  it says nothing about a patient. Metadata was never protected anyway —
  `ASYNC_DELIVERY.md` § 2 says so — and the reply already confirms that
  a message arrived.

> **Amended 2026-08-05 during #249.** An attachment Graph **lists but yields
> no bytes for** is now a rejection rather than a silent skip, and the two
> assumptions that rests on are measured rather than assumed. Both were
> unverifiable by unit test: every processor test substitutes
> `IGraphMailClient` wholesale, so the tests assert our reaction to a listing
> we invented.
>
> **A forwarded email arrives as `#microsoft.graph.itemAttachment` with no
> `contentBytes`.** Confirmed in production 2026-08-05 against a message
> carrying a readable `.docx` *and* a forwarded email: the message was refused
> and no consult was generated. Refusing here rather than proceeding on the
> part that did arrive is the whole point — the alternative is a consult built
> from half a referral and presented as whole.
>
> **Outlook sets `isInline: true` on signature images.** The skip at the top
> of `GraphMailClient.Classify` is therefore load-bearing in both directions.
> Were it wrong, every signed email would bounce; and because a lone unmatched
> readable attachment is assigned to the one free slot
> (`EmailAttachmentInputs.cs`), a signature logo could otherwise land in
> `prior_notes` as a silent wrong input. Verified by a signed message whose
> provenance shows `consult_draft` read and `prior_notes` empty.
>
> **`referenceAttachment` is unreached, not verified.** `DescribeUnreadable`
> has a branch for it, and no client tested so far produces one: Outlook on
> the web's OneDrive attach puts a link in the message **body** and Graph
> lists no attachment at all (#291). The branch is covered by unit test only
> (`GraphAttachmentClassificationTests`, `DescribeUnreadable`). Stated
> explicitly because a branch that reads as covered when it is not is the
> failure this milestone is named for. If Outlook Desktop — whose *Attach
> file → Share link* is the likeliest producer — becomes available, this is
> the one case left to measure.

> **Amended 2026-08-22 during #428.** Several attachments for one slot
> are accepted when the slot is an array of text and the sender names
> their order: `prior_notes-1.pdf`, `prior_notes-2.docx`, … numbered 1 to
> n with no gaps and no repeats. The numbers are read as numbers (ten
> after two), the order is the sender's, and it is the order the elements
> hash in — the only ordering signal a message carries is the one the
> sender wrote, since a no-PHI reply can confirm nothing back. Refused,
> each by a sentence naming only the slot: a numbered stem for a slot
> that takes one (*"takes one document and cannot be numbered"*), a gap
> (*"numbered with gaps"*), a repeat (*"more than one document with the
> same number"*), a plain stem beside a numbered one (*"supplied both as
> 'prior_notes' and as numbered files"*). Two **plain** stems for one
> slot keep today's refusal, array slots included. A numbered stem naming
> no declared slot — `fax-2.pdf` — is the unmatched file it always was.
> The resolver now receives the declarations rather than the ids, since
> which slot takes several is a question about its type.

## 7. Provenance

- **Record the origin, beside the hash and not inside it.** Per slot:
  whether the text was typed or extracted, the extractor's
  `name/version`, and the page count. `Source` today is only `app` or
  `email` (`ConsultGenerationJobSources`), which cannot express that a
  slot was machine-read from a document.
- **Why it matters here specifically.** `provenance.md` says the
  record's job is that *"the record fully identifies what produced the
  output"*. If a consult says something the referral did not, the first
  question is whether a machine misread a two-column layout — and today
  the record cannot answer it.
- **Why the extractor version is part of it.** A pinned pre-1.0 library
  can change its output for identical input across versions. Recording
  the version is what makes "re-run this record" a meaningful request.

> **Amended 2026-07-30 during #253.** The separator is `/` and always
> has been — `text/1`, `pdfpig/0.1.15+f131f642`, `openxml/3.5.1+3139fdfd`.
> The `name@version` above was wrong about code that shipped, and the
> ids are persisted, so the doc is what changed.
>
> **The id is normalised where it is built** (`ExtractorIdentity`), not
> where it is shown. Both extractors used to take
> `AssemblyInformationalVersionAttribute` verbatim, which read acceptably
> for PdfPig and put
> `3.5.1+Branch.main.Sha.<sha>.<sha>` — a branch name and two copies of a
> commit — into the panel for OpenXml. Now: everything before the `+`,
> then the first build-metadata token that is hex and at least 32
> characters, shortened to 8. No qualifying token means the version
> alone. The floor matters: build metadata is mostly not commits, and a
> branch named `deadbee` is hex.
>
> **One value, stored and displayed.** The commit stays in both rather
> than being trimmed for the panel, so a reviewer comparing a screenshot
> against the record sees one string. Records written before this keep
> the value they were written with — a past record displays what it said.
- **Durable replay safety**: the annotation travels as trailing optional
  record parameters on the orchestration input, deserialising null for
  jobs already in flight — the discipline #215 and #217 followed.

> **Amended 2026-08-22 during #428.** Per **document**, positionally —
> `origins[id][i]` describes element `i` of the slot — rather than per
> slot. Stored beside the #238 field, not in its place: records at rest
> and instances in flight hold the single shape, so
> `InputDocumentOrigins` is a new trailing slot on both durable records
> and a new state field, and new jobs write only it. The response has
> one map, `inputOrigins`, of slot → list; a record written before this
> projects its single origin as a one-element list. History lists one
> row per document under the slot, in the order supplied; a slot read
> from one keeps its sentence. A document that extracted to nothing is
> still one element and one origin, so the record says which one it was.
- **Not in v1**: retaining the source file. The record references
  extracted text, so a better extractor cannot be re-run over the
  original later. Keeping the bytes would mean PHI at rest with its own
  retention and deletion story, which is a larger decision than this
  milestone; the cost is that extraction is a one-way step.
  > **Amended 2026-08-28 (#512).** The bytes are still not kept, but they
  > leave a digest: a document's origin carries `fileSha256` over the file
  > as received and `textSha256` over the text as it entered the
  > effective-input map, beside the extractor's identity. A holder of the
  > original can verify it against the record later, and two records over
  > the same file under different extractor builds read as *same file,
  > different reading* rather than as different referrals.
  > **Amended 2026-08-29 (#510).** A second origin kind, `previous-run`:
  > an input copied from one of the same account's completed runs. The
  > request names the run and the deliverable (`InputRefs`, one per
  > element — a text slot one, an array of text one per entry), never the
  > text: the starter reads the run (this account's, completed, text still
  > held — a foreign or unknown run is *not found*, never forbidden; a run
  > past the retention sweep is refused naming the day), copies the
  > deliverable's text into the slot the way it copies a document's, and
  > records `{ kind: previous-run, sourceJobId, sourceResultId, textSha256 }`
  > with the copy's canonical digest. The copy is the new job's own input
  > with its own retention clock. The form shows the text as a preview
  > only; *Edit text* turns it into typed text and drops the reference, so
  > `previous-run` always means byte-for-byte the deliverable. Only a
  > `document` origin exempts a slot from the cloud-link check (§ 4): a
  > previous run's text was typed by someone once.

## 8. Failure copy (normative)

The app renders these inline in the field's error slot. Each names what
happened and what to do instead.

| Outcome | App |
|---|---|
| `unsupported-type` | We can read .txt, .md, .pdf and .docx files — that one is something else. |
| `no-text-layer` | This PDF has no text layer, so it is a scan or a fax. Paste the text instead, or attach a PDF exported from your system. |
| `password-protected` | This PDF is password-protected. Remove the password and try again, or paste the text instead. |
| `corrupt` | This file could not be read — it may be damaged or incomplete. |
| `empty` | There is no text in this file. |
| `too-large` | That file is larger than 10 MB. |
| `too-many-pages` | That document has more than 100 pages. |
| `expands-too-large` | That file unpacks to more than we can read. |
| `too-much-text` | That document holds more text than one input can take. Attach the relevant part instead. |

The email reply keeps its fixed subject and its existing opening line,
and appends one sentence naming the cause — the scan case being the one
that matters:

```text
We could not read one of your attachments: it looks like a scanned
document with no text layer. Please attach a PDF exported from your
system, or paste the referral into the message itself.
```

## 9. Security posture

The parser is the first place in this app where **an attacker-controlled
binary is parsed in-process**. Everything before it was JSON with a
length bound, or text. Three entry points reach it: the preview endpoint,
job start, and email intake.

> **Corrected 2026-07-31 during #241.** This said the email door needs no
> sign-in, because a message is parsed before the sender is matched. That
> is backwards. `EmailIntakeProcessor` gates in order — authentication
> floor (`:145`), sender match against an Active account (`:162`), fetch
> attachments (`:188`), extract at job start — so the parser is reachable
> only for an **authenticated, matched, active sender**, on all three
> entry points. Anyone who can merely send mail to the intake address
> never reaches it.
>
> The same claim was this section's stated justification and #241's. It
> was corrected on the issue on 2026-07-29 and survived here. The work
> still matters — a crafted PDF can take the worker down uncatchably, and
> the exposure grows the moment external users are activated — but it is
> not the open-to-the-internet risk this paragraph described.

- **A wall-clock timeout on every parse.** A malformed object graph can
  spin without allocating, so a byte cap does not bound time.
- **An explicit memory bound**, so a page bomb fails the request and not
  the worker.
- **Container bounds for DOCX**: decompressed size and entry count (zip
  bombs are bytes-cheap and expansion-expensive), and a **disabled XML
  resolver** so an external entity cannot reach the network or the
  filesystem.
- **Per-account rate limiting** on every door that reaches the parser —
  not only the preview endpoint, since a limit on one of three is not a
  limit. This was the first rate limiting of any kind in the app.
- **Bytes are never persisted and never logged**, including on the
  exception paths. The filename never being sent is only half of PHI
  minimisation; the other half is what our own error handling writes
  down.
- **A malformed corpus is part of the definition of done** — truncated
  xref, recursive object graph, absurd `MediaBox`, encrypted with
  garbage, a `.txt` renamed `.pdf`, a truncated `word/document.xml`, a
  zip that is not an OPC package, an XXE payload. Each must return a
  named outcome rather than an exception, a hang, or a 500. Tracked in
  #241.
- **Test fixtures**: the repo has never checked a binary in — the
  closest analogue round-trips through `ConsultDocumentPdf.Render` in
  memory. Generate what can be generated, including the encrypted-PDF
  case and the zip bomb (built in-test, so repository scanners are not
  tripped), and add `tests/TestData/documents/` only for what genuinely
  cannot be — a real scan, a two-column layout, a truncated file.

> **Status, 2026-07-31 (#241).** Where each bullet stands, because three
> of them were met by other issues and reading the list as outstanding
> work overstates what is left:
>
> | | |
> | --- | --- |
> | wall-clock timeout | **done** in #235 — `MaxParseDuration`, 20s |
> | container bounds | **done** in #240 — `MaxExpandedBytes`, `MaxArchiveEntries` |
> | disabled XML resolver | **nothing to do** — the SDK prohibits DTD processing in three places, so an external entity never resolves |
> | malformed corpus | **done** in #241 — every case above has a test asserting a named outcome |
> | never logged | **done** in #241 — audited and pinned |
> | never logged — the prompt path | **done** in #245 — the prompt-node console lines carry length and SHA-256, never the rendered prompt or the response; the #241 audit covered the parser paths only |
> | memory bound | **done** in #241 — a concurrency gate; see below for what that does and does not bound |
> | per-account rate limiting | **done** in #266 — a fixed hourly window per account; see below |
>
> **Amended 2026-08-01 (#241): the memory bound is a concurrency gate.**
> A true per-parse cap is not achievable in-process, so what ships bounds
> how many parses run at once — peak parse memory becomes N × worst case
> instead of unbounded.
>
> **N is 4, and CPU decides that as much as memory.** Measured: the app
> runs on Linux Flex Consumption at **2048 MB**, which is **one CPU
> core**, with HTTP concurrency at the platform default of **16**. On
> Flex that default comes from instance size and **cannot be set in
> `host.json`** — an earlier sketch of this work put
> `maxConcurrentRequests` there, where it would have been silently
> ignored. Past a handful of concurrent parses on one core there is no
> throughput to gain and only memory to lose; four still finish inside
> the 20 s timeout where sixteen would not.
>
> Parse cost, measured against PdfPig 0.1.15: **10–15 MB retained** for
> documents we accept, because both caps bind — the page cap right after
> `Open`, the character cap inside the page loop. Cost tracks **pages and
> extracted text, not file size**; the ratio to file size ranged 49×–236×
> across fixtures and means nothing.
>
> **The email door is never refused, and that is the load-bearing part.**
> Every start-failure path in `EmailIntakeProcessor` moves the message to
> the Rejected folder, writes a claim and **replies to the sender** —
> there is no "leave it for the next poll" branch. A transient `busy`
> there would permanently reject a referral and tell a clinician their
> document could not be read, which would be false. So the wait budget
> comes from `ConsultGenerationJobOrigin`: interactive callers wait 5 s
> and then get `busy` as 503; the poller waits 5 minutes. A background
> poller can afford to be slow; it cannot afford to be wrong.
>
> **What it does not bound**, stated because the bullet above says
> "memory bound" and that would otherwise be read as more than it is:
>
> - It does not stop one crafted file allocating its own worst case. That
>   needs process isolation.
> - The **request body is buffered before the gate** — `CopyToAsync` at
>   the preview endpoint, base64 decode at job start — so up to
>   16 × 10 MB of buffers is bounded by platform concurrency, not by N.
> - `PdfDocument.Open` resolves the xref and page tree before any cap of
>   ours, so a pathological object graph is bounded only by the timeout.
> - A slot is released when the **work** finishes, not when the caller
>   stops waiting, so an orphaned parse keeps its slot. That is correct —
>   it still holds the memory — but it means saturation is reachable, and
>   it is why the background wait is bounded rather than infinite.
>
> `busy` is the first outcome here that is about us rather than the
> document, and its copy says so: *"Nothing is wrong with this one."*
>
> Tunable without a deploy via `DocumentExtraction__MaxConcurrentParses`
> (`docs/CONFIGURATION.md`). **Set it to `1` to watch the gate fire**: with
> one slot, two simultaneous uploads race for it and the loser gets 503. At
> the default of 4 the gate is invisible to a single operator — which is
> intended, and is why `1` is the only practical way to exercise the refusal
> path in production.
>
> **The corpus outcomes were measured, not predicted**, and one is worth
> recording: a zip that is not an OPC package comes back
> `unsupported-type`, not `corrupt` — it opens as a `WordprocessingDocument`
> with no main part, so nothing is wrong with the file and it simply is
> not one we read. The recursive graph, the absurd `MediaBox` and the
> garbage `/Encrypt` are all `corrupt`.
>
> **The logging audit found nothing, which is the finding.** No filename
> and no document content reaches a log on any of the three paths.
> `InputFilePayload` has no filename field at all, so the app and job
> doors have nothing to leak; email is the only door carrying one, and
> its rejection paths log `Detail`, which holds either an attachment
> **size** message or a reject reason naming an input **slot**
> (`consult_draft`), never a file. That is now asserted rather than
> observed — the tests capture structured values and scopes, not only
> rendered messages, because Application Insights stores template
> arguments as customDimensions.
>
> **Rate limiting, added 2026-08-01 (#266).** The unit is **one
> submission** — one preview call or one job start, whatever it carries.
> That is deliberately *not* the CPU unit: a submission with three
> attachments costs exactly what a 20 KB text file costs. It bounds how
> often an account can ask; the concurrency gate above bounds what each
> ask costs. Fixed window on the UTC hour, so a burst straddling a
> boundary can reach twice the limit — accepted, because sliding would
> cost a row per submission and a pruning story for an edge that does not
> matter at this scale.
>
> **Table-backed, not in-memory.** Flex Consumption scales to many
> instances and recycles workers, so an in-process counter would mean N
> instances × the stated limit, reset on every recycle — the stated limit
> would not be the enforced one.
>
> **Two enforcement points cover three doors**, because the email poller
> reaches the parser *through* the job starter: the check lives in
> `ConsultGenerationJobStarter.StartAsync` and at the preview endpoint,
> the same split `GateWaitFor` already makes. It is **not** an extraction
> outcome — every value in that vocabulary describes what became of an
> attempt to read a document, and a rate-limited caller made no attempt.
> Interactive callers get `429` with `Retry-After`.
>
> **It fails open.** A storage fault allows the submission and logs. The
> asymmetry is the justification: losing the limit during an outage costs
> CPU, refusing during one costs a clinician their referral.
>
> **The email door queues instead of being refused, and that is the
> load-bearing part — again.** #265 could simply never refuse that door;
> a rate limit cannot be waited out inside one poll when the window is an
> hour. So a rate-limited message moves to a **`Queued` Inbox child
> folder** beside `Processed` and `Rejected`, and its sender is told
> **once, on the way in**, that it is queued and needs no action. Silence
> for two hours is indistinguishable from a black hole; a reply on every
> retry would be about thirty emails. Which one happens is decided by
> *the folder the message was listed from* — Inbox means first time,
> `Queued` means retry — so replying exactly once needs no counter and no
> stored flag.
>
> A child folder's messages never appear in the Inbox listing, so the
> poll makes **two calls, `Queued` first**, sharing one
> `MaxMessagesPerPoll` budget. Queue-first is fairness, not tidiness:
> otherwise a steady stream of new arrivals spends the budget every
> window and the backlog never drains. The `Queued` listing carries no
> `isRead` filter — membership is the state — and that absence is also
> what makes `$orderby=receivedDateTime` legal on it. Graph requires
> every property in `$orderby` to also appear in `$filter` for messages,
> so asking the Inbox listing to sort would return `InefficientFilter`
> rather than sorted results; it is left unordered.
>
> **Order on the queue path is load-bearing: mark, then reply, then
> move.** A failed mark leaves an unread Inbox message with a null claim,
> which the existing stale-claim repair already handles; a failed move
> leaves a `queued` claim that the next poll's `RepairAsync` clears.
> `queued` is the first non-terminal value in `EmailIntakeOutcomes` —
> the row is released and the message retried in full, which is safe
> precisely because a queued message started no job.
>
> **Giving up is checked on the way in**, as the first thing done to a
> message listed from `Queued`, before it is claimed and before its body
> is fetched. That makes the bound a property of the folder — nothing
> waits in `Queued` longer than `RateLimits__MaxEmailDeferralHours` —
> rather than a property of still being over the limit, and an expired
> message costs no Graph reads at all. **Received time is the clock**: it
> measures what the sender experiences, not how long we held it, and
> after that long a fresh re-send is safer than silently generating a
> consult from stale clinical context. An unknown received time never
> expires — refusing a referral over missing metadata is the wrong
> failure direction.
>
> **That check never runs against the Inbox listing**, and the asymmetry
> is the whole justification: after a poller outage every unread message
> is hours old, and auto-rejecting that backlog would tell senders who
> had heard nothing at all that they had failed. A queued message has
> already been told it is queued, so the rejection completes a
> conversation rather than starting one. This must not be "helpfully"
> generalised to both listings later; there is a test whose only job is
> to fail if it is.
>
> **Two guards cannot be mutation-tested and should not be claimed as
> though they were.** The stack-depth bound's failure mode is an
> uncatchable `StackOverflowException` that kills the process, so
> removing it to watch a test fail would take the test host with it. And
> the XXE case pins SDK behaviour rather than code of ours — it is a
> regression detector against a package upgrade, not proof of a guard we
> wrote.

## 10. Not in this milestone

- **OCR for image-only PDFs** (#239). Text-layer extraction adds **zero
  new data processors** — the bytes never leave the Function App and are
  never persisted. OCR adds one: a cloud service is a new PHI
  subprocessor, a new data-flow arrow, and a new vendor-management
  evidence item in an audit that is in progress, with per-page cost on
  top; native Tesseract avoids that and brings native binaries and
  fax-quality accuracy, where a misread dose is a clinical error rather
  than a typo. **#188's fax parity therefore stays blocked** — but on a
  narrower and more honest reason than before: not "PDF is unreadable"
  but "image-only PDFs need OCR". #188's own doctrine is to defer until
  demand picks a target, and this is the same shape. The `no-text-layer`
  outcome is built now regardless, because even after OCR ships
  something must detect the scan in order to route to it.
- **Retaining source files** (§ 7), which would make extraction
  re-runnable at the cost of a PHI-at-rest retention story.
- **Binary inputs in the package format.** Files bind through extraction
  and reach the workflow as text, exactly as
  `package-format-v7-design.md` § 7 already states.
- **Per-source input routing** — an input that only email may fill, or
  only the app. No package has asked for it.

## 11. Future steps (sketched, not promised)

Recorded so the roadmap is legible; nothing above depends on this
section.

- **More formats** — RTF and HTML email bodies are the next plausible
  pair, and by construction each is one extractor and no source edits.
- **Structured extraction** — PdfPig exposes word positions, so table
  and column reconstruction is reachable. It would improve prompt input
  markedly and needs its own accuracy argument before it touches
  clinical text.
- **Extraction as a workflow node** — today extraction is intake
  plumbing. If a package ever wanted to choose how a document is read,
  it would become a node kind, and the provenance annotation from § 7 is
  the seam it would grow from.
