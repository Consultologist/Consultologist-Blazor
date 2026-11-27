# Milestone 40: inputs

What the owner has decided for the inputs group: the input kinds, the file model, the converter,
the locale and the input declaration. Decided in session 2 (2026-11-26 and 2026-11-27), in seven
rounds. The parent issue is
[#1442](https://github.com/Consultologist/Consultologist-Blazor/issues/1442).

Nothing here is a plan yet. What is still open is at the end.

## The kinds

| Kind | Issue | Decision |
|---|---|---|
| documents | #1431 | **Design:** PDF and DOCX as file values. Legacy `.doc` and macro-enabled files are refused. |
| images | #1430 | **Design:** JPEG and PNG as file values for multimodal agents. Embedding in deliverables comes later. |
| spreadsheets | #1434 | **Design as a door** into array-of-objects inputs: CSV and XLSX, with no new format. |
| DICOM | #1436 | **Out of scope,** with a refusal sentence. |
| archives | #1437 | **Refused,** with a refusal sentence. |
| audio and video | #1432 | **Stay outside the engine.** Transcripts remain the only door. |
| email | #1435 | **The mailbox stays the only door.** |
| FHIR, HL7 v2, CDA | #1433 | **Moved out of Milestone 40** to Milestone 42 (structured clinical data), beside the integrations work in Milestone 24. |

The four that are only ruled on (#1432, #1435, #1436, #1437) stay open until their refusal
sentences are in a spec.

## The file model

- **A file is a value, by reference only.** It is never inline base64.
- **Two catalogue formats, `document` and `image`.** Each is a closed object of `mediaType`, `size`
  and `sha256`, with the accepted media types as an `enum` on `mediaType`. A package narrows by
  declaring its own format with a shorter enum. There is no new dialect keyword.
- **No specVersion 31 package changes meaning.** Everything here arrives with the next spec version.
- **The template decides what a prompt gets,** by the read it places: `.value` sends the file as a
  message part, `.content` sends text. Publish refuses a file part when the bound agent does not
  declare that media type.
- **A file's text is its `.content`,** and it exists only where the input says how to extract it
  (next section).
- **Stripping at intake:** an image loses its EXIF, and the record keeps both hashes (as uploaded,
  as stored); the value's `sha256` is the stored one. A document is kept as uploaded, and the spec
  states which hidden content is not removed (tracked changes, comments, properties).
- **Retention:** file bytes are stored with the job's retained text, deleted after 7 days, never in
  telemetry, and the hash is kept permanently.

## Extraction

Extraction is explicit: a member of the input, not a node kind and not something the engine does
unasked.

- **An input has an `extract` member,** always written. `null` means no extraction.
- **It says two things:** the output `format` (`markdown_block` or `plaintext`) and `ocr` (`true`
  or `false`, with no default). The OCR model is the engine's choice, recorded on the row.
- **A file input with `extract: null` has only `.value`.** Publish refuses a `.content` read of it.
- **A `document` extracts to `markdown_block`** through the converter, or to `plaintext`. An
  **`image`** may set `extract` with `format: plaintext`, which gives its OCR text as `.content`.
- **Text inputs are explicit too.** At the next spec version a text-format input takes a document
  only when it has an `extract` member; 31's implicit extraction, and the `channel: image` door
  with it, become this member. The record changes from 31's accordingly.
- **One shape everywhere.** On a text input `extract` is the same `{ format, ocr }`, and publish
  refuses a `format` that differs from the input's own.
- **The migration from 31 writes `extract`** with the input's own format and `ocr: true` on every
  text-format input, and on each `channel: image` input, so a migrated package takes exactly the
  documents it took at 31.
- **A package test pins the extraction.** The test file gives the extracted text for an input
  beside its inputs, as it gives an agent's reply, and the run uses it exactly as written. A test
  that supplies a file with no pinned text is refused, so tests never run OCR.
- **The step runs once, at the start of the job,** before any node, so an unreadable or
  password-protected file refuses the job early. Publish warns when `extract` is set and
  `.content` is never read.
- **The step has its own record row,** tied to the input, in place of the header's list of
  extraction steps. #1438 adds OCR confidence to that row.

## The converter

- **The document-to-Markdown converter is the first slice of this group,** ahead of file values. It
  needs no file model: it changes what a document uploaded to a `markdown_block` input yields.
- **It arrives with the next spec version,** as `effectiveInputHash` definition 8. A 31 package
  keeps "extracted text, as at 30".
- **DOCX is converted by the engine's own deterministic code** (headings, lists, tables, emphasis),
  pinned by golden files. #1538 looks at this again, against the layout model and off-the-shelf
  libraries.
- **PDF and scanned pages go through the OCR service's layout model,** recorded with its dated
  version, and are marked best effort. The higher price is accepted.
- **`plaintext` does not pay for structure:** a `plaintext` extraction keeps `prebuilt-read`, and a
  PDF with a text layer is parsed locally, as today. For `markdown_block`, a PDF with a text layer
  goes to the layout model too.
- **Tables:** one with no spans is a pipe table, one with spans a grid table, for DOCX and PDF
  alike.
- **Page headers, footers, numbers and breaks are dropped,** and the extraction's record row says
  how many.
- **The service or an off-the-shelf component is preferred over custom code.** What was found about
  the layout model is in [`layout-model.md`](layout-model.md).

## The spreadsheet door

A CSV or XLSX file fills an array-of-objects input (#1434).

- **It is opened by the same `extract` member,** so it is explicit too.
- **Header cells match the properties' `title`s exactly,** after trimming.
- **An unknown column refuses the file,** and so does a missing column whose property is not
  nullable.
- **An empty cell is `null`.**
- **XLSX with more than one sheet is refused.** A formula gives its stored value and is never
  evaluated.
- **CSV is UTF-8 only,** RFC 4180.

## Arrays and limits

- **An input may be an array of `document` or `image`,** and every element is a file.
- **A text-format array with `extract`** still takes typed text, documents and earlier runs'
  deliverables, in the order sent.
- **No array mixes file values with typed rows.**
- **File limits are engine limits** (size, count, pages, image dimensions): configured per
  deployment, recorded in the job header beside Milestone 39's ten, each with a refusal sentence.
  A package cannot raise them. The numbers are set when the slice is planned, from what uploads
  look like today.

## Text formats

- **Markdown stays the only rich-text type.** No HTML text format is added; HTML exists only as a
  rendered stage at delivery (#1429, closed).
- **One Markdown: Markdig with `UseAdvancedExtensions`,** the nineteen extensions of that set at
  the pinned version and none of the others. No flavour member and no flavour checks (#1451,
  closed).
- **The same pipeline everywhere:** escaping, `plain()`, previews and every rendering at delivery.
- **Raw HTML is disabled in it** (`DisableHtml`): a tag typed into Markdown is shown as the
  characters typed, wherever Markdown is parsed or rendered.
- **Line endings are standard Markdown's:** a single line ending is not a break. Keeping
  line-per-line sources looking the same is Milestone 39's to decide.

## The locale

- **An account locale is added, as the run fact `run.locale`** (#1452). It covers formatting only:
  month and day names, and date and number formats. Comparisons, `matches` and hashing stay
  invariant.
- **No silent default:** a package that uses locale-dependent formatting on an account with no
  locale set is refused at the start, as for the time zone.
- **A package may fix its own locale** in the manifest, and that wins over the account's.
- **The supported locales are a closed list,** `en-CA` and `fr-CA` at first, with pinned ICU data
  in `lock.json` and golden outputs for each locale.

## The input declaration

Decided here, so that the declaration changes once.

- **`channel` keeps its name** (#1427, closed). It gains no values for files: a file's kind is said
  by its format.
- **A `default` member is added** (#1456), in the same spec version as the file model, as a
  prefill only:
  - Create shows the default already filled in, and the clinician can change or clear it.
  - What is submitted is what is recorded. A cleared field is blank and reads `null`.
  - The engine never applies it: a caller that omits the input gets `null`. The default is served
    on `WorkflowPackages/Current`, so a satellite can prefill too.
  - Publish checks that the default fits the input's format.
- **The record says when a value is the default.** The input's header entry gains a flag saying the
  submitted value equals the package's default. The engine computes it; Create does not report
  whether a field was touched.
- **Macro choices become required booleans with a default.** The migration to the next spec
  version rewrites the optional booleans Milestone 39 wrote for per-run macro choices, and their
  conditions become plain reads.
- **#1533** (the size of an absent optional list) waits on Milestone 39's end-of-build review.

## Standard components

- **#1458 and #1459 joined Milestone 40** as a measuring spike under #1477. When building starts,
  the spike runs first: the 31 conformance vectors and the golden corpus go through json-everything
  behind the engine's interface, and the adopt-or-stay decision is made on the differences counted.
- **`format` is asserted with either library.**

## Open

- **Two pipelines on `m39-build`, an M39 defect.** `PlainConversion` builds
  `UseAdvancedExtensions`, as the spec says. `ConsultMarkdown.Pipeline`, which renders the emailed
  PDF and the Web's previews, is `DisableHtml`, `UsePipeTables` and
  `UseSoftlineBreakAsHardlineBreak` (read at `94b66aec`). The owner carries it to the M39 build, for
  area 12 or 13. Milestone 40 plans against one pipeline.
- **How the converter gets from the layout model's output to the engine's Markdown,** and the
  model's price and availability in the engine's region: #1538, with the findings in
  [`layout-model.md`](layout-model.md).

## Milestone 39 facts relied on

| Fact | Source |
|---|---|
| Any text-format input takes typed text or a document; a data-format input is typed only, except an object filled from a `form`. | v31 design, part 02 § 8.2, as on `m39-build` at `94b66aec` |
| The converter is Milestone 40's; `effectiveInputHash` definition 7 is "extracted text, as at 30", so the converter is a new definition. | part 02 § 8.2; PR #1531 decision 12; PR #1535 |
| The record's header holds each document input's extraction steps, with `converter` always `null`. | PR #1531 decision 12 |
| Markdown means Markdig 0.41.3 with `UseAdvancedExtensions`. | part 02 § 5.2; `Directory.Packages.props`; `V31/PlainConversion.cs` |
| An input has the members `label`, `description`, `format`, `required` and `channel`, every one present, and no `default`. | part 02 § 8 |
| An input's origin in the record is one of `typed`, `document`, `previous-run`, `transcript`. | part 08 |
| A per-run macro choice is an optional boolean input, read as `=== true` or `!== false`. | PR #1532 R7; #1456 |
| Every render and operation runs in the invariant culture. | PR #1529 D13 |

These are checked again in the reconciliation pass (`README.md` § Planning against a moving M39).
