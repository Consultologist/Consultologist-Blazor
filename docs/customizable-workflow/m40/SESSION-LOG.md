# Milestone 40 planning: session log

Where the planning stands at the end of each session, newest first. Read `README.md` first, then this
file, then re-read the milestone on GitHub: issues are added to it while M39 is built.

## Session 2 (2026-11-26 and 2026-11-27): the inputs group, five rounds

Re-read the milestone, read what M39 built since 2026-11-21, and grilled the owner on the inputs
group. The decisions are in [`input-kinds.md`](input-kinds.md). No plan is written yet.

**The milestone on 2026-11-27:** 65 open issues, 3 closed.

- **Added:** #1534 (a macro's `condition`, a plain-language question compiled to a `judgement` node
  at publish), filed from the M39 build on 2026-11-25. #1458 and #1459, moved in as a measuring
  spike under #1477. #1536 (the 1 MiB row ceiling, and replies travelling the Durable history
  twice) and #1537 (a reaper for dead orchestrations), filed this session from M39's rulings on
  #1508 and #1509.
- **Closed as decided:** #1429 (no HTML text format), #1451 (one Markdown, Markdig with
  `UseAdvancedExtensions`) and #1427 (`channel` keeps its name).
- **Moved out:** #1433 (FHIR, HL7 v2, CDA), to the new Milestone 42 (structured clinical data).

**M39 since 2026-11-21:** areas 07 to 11 are merged into `m39-build` (PRs #1529, #1530, #1531,
#1532, #1535), read here at `94b66aec`. Area 12 is in progress on `m39-area-12` with no pull request;
area 13 has no branch. The area PRs and rulings were read through digests; of the built files, only
part 02 § 8 was read directly.

What bears on Milestone 40:

- **The document-to-Markdown converter is M40's,** under #1442. The record's `converter` is always
  `null`, and `effectiveInputHash` definition 7 is "extracted text, as at 30" (PR #1531 decision 12;
  PR #1535).
- **Extraction is recorded for each document input** in the job header: the parsers `text`, `vtt`
  and `srt`, and the OCR model with its dated API version. Image and ambient-note origins are
  written as `document`.
- **Blank inputs:** a blank optional input reads `null`; a blank required text input is refused at
  the start; trailing white space is kept where 30 trimmed it.
- **An absent optional list is `null`,** not empty. #1533 (Milestone 39) moves to M40 if M39's
  end-of-build review does not settle it.
- **`resolve.json` carries `propertyOrder`** beside every `properties`, compiled only. An input's
  `Format` is served as one compound JSON Schema document.
- **Per-run macro choices are boolean inputs,** sent by Create as `Inputs`.
- **The migration writes `date.to_string "%d %b %Y"`** for 30's date inputs, so English month names
  are in migrated packages' bytes (#1452).
- **Deliverables end `skipped`, `failed`, `held`, `blocked` or `released`.** The "refused" of
  #1524's title is `blocked` in the record, kept with its composed hash.
- **`emr` and `zip` have no delivery channel yet,** and the kind-to-endpoint mapping was named as
  M40 work under #1443.
- **A package that cannot migrate keeps running at 30** until 30 is retired (#1450), with no
  backfill.
- **`onInvalid` is `retry` or `fail`;** `flag` is gone.
- **#1483 gained four named sentences:** S05-025, S05-032, S05-046 and S05-052.
- **The emailed PDF and the Web's previews do not render with the spec's Markdown.**
  `ConsultMarkdown.Pipeline` is `DisableHtml`, `UsePipeTables` and
  `UseSoftlineBreakAsHardlineBreak`; part 02 § 5.2 and `PlainConversion` say
  `UseAdvancedExtensions`. The owner ruled it an M39 defect and carries it to the M39 build, for
  area 12 or 13; it is not posted on M39's issues from here.

**Found while planning:** a comparison of Corvus and json-everything (licence, JSON Schema, JSON
Logic, maintenance), summarised in the comments on #1458 and #1459. JSON Logic has no published
specification; the closest effort is the volunteer json-logic organisation.

**Next session:**

1. Re-read the milestone and its open issues, and check `m39-build` for areas 12 and 13.
2. Settle the open point in `input-kinds.md` (raw HTML in Markdown), and check whether M39 fixed
   its two Markdown pipelines.
3. Go on with the inputs group's remaining questions (the spreadsheet door's mapping rules, limits
   and their refusal sentences, mixed arrays, the Create page's widgets), then plan the converter
   as the first slice.

## Session 1 (2026-11-21): the milestone re-read

Read `README.md` and `AGENTS.md`, then re-read Milestone 40's description and open issues. Nothing was
planned or decided.

**The milestone on 2026-11-21:** 64 open issues, none closed. The description and the issue list
matched `README.md` § Scope, and no issue had been added since the README was written. The newest
issues, #1522, #1523, #1524 and #1527, were filed from the M39 build. (#1525 and #1526 are pull
requests.)

**Comments that change an issue's scope:**

- **#1439, the package author's ruling of 2026-11-18 (v31 design revision 4).** It replaces the
  `x-producer`/`genr8_*` ruling of the same day. Formats describe shape only, with no producer
  families and no prefixes, and every node header writes `producer`. So the names in the titles of
  #1438 (`ocrxt_*`) and #1439 (`atten`, `adata`, `agent`, `genr8`) are out of date. Plan with the
  newer ruling, and check it against what M39 built.
- **#1478.** The `modelDerived` and `runDependent` flags moved into M39 (every node header writes
  them). M40 keeps only letting a read require a flag's value, for example
  `"requires": { "modelDerived": false }`.
- **#1479.** `"producer"` is reserved in `via` at specVersion 31, and refused until this issue lands.

**Rulings already written into the issue bodies:**

- **#1522.** For M39, descriptions in the format sent to the model are joined with a newline (ruled on
  #1504). M40 decides between a space and a newline, based on what models actually do with each.
- **#1523.** For M39, the checker exempts registry and catalogue formats from the id pattern (ruled on
  #1504). M40 removes the exemption by renaming the fields (`isSnomedConcept`, `isActive`). That
  cascades into new hashes, new versions of the attested agents, and changes to the renderers.
- **#1524.** For M39, a refused deliverable is withheld, not erased (ruled on #1507). M40 lets a
  clinician release it after review, as a recorded override.
- **#1527.** For M39, a function's inner edges are missing from `resolve.json`'s `graph`, and `.items`
  loops get both a `for` and an `aligned` edge (area 05, PR #1525, decisions D7 and D40). Changing
  either is a contract change.

**Related issues in no milestone:** #1458 (moving the JSON Logic evaluator from Corvus to
json-everything) and #1459 (moving value validation from Corvus.JsonSchema to JsonSchema.Net). Both fit
the move towards standard components in #1477. Ask the owner whether to add them to M40.

**Next session:**

1. Re-read the milestone and its open issues. Note any new ones here.
2. `git fetch origin m39-build` and check which areas have been built since 2026-11-21 (area 07 was in
   progress then). Read each new area PR's "Decisions made during build" section.
3. Grill the owner on the first group: the inputs chain (#1442 and its sub-issues #1430–#1437, then
   #1429, #1451, #1452), unless they choose another.
