# Byte-equivalence fixtures (#1498)

What today's workflow engine (specVersion 30) produces, frozen as in-repo files. Every file here
is read as exact bytes: `.gitattributes` holds `* -text`, so git never normalises a newline.

Area 01 (#1498) froze the 30 side. Area 10 (#1507) added the migration's goldens beside it, never
rewriting area 01's: `conditions/converted.json` and `conditions/equivalence.json`, `migrated/`,
`hand-edits/` and `reports/`.

## `vendored/`

The thirteen public workflow packages, each vendored from the tag that pins it in the
`consultologist-workflows` repo, as git holds the files (`manifest.json`, `dag.mmd`, prompts,
macros, snippets, everything under `packages/<name>/` at the tag). `test/general` sits at
`vendored/test/general/`.

`vendored/index.json` records, per package, the tag, the commit it resolved to, and the SHA-256 of
every file. The tags are the pins (the list lives in the script); the index is what CI checks:
`VendoredPackagesTests` fails if a file's bytes differ from its hash, or a file is on disk that
the index does not name.

Regenerate from a checkout of the content repo that has the tags:

```
dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]
```

The script deletes `vendored/` first, so a file removed upstream cannot linger, and writes nothing
if a tag does not resolve. Move a pin by editing the tag list in the script, then re-run it.

## `fixture-packages/`

Three hand-written specVersion 30 packages (`v2026.11.1`, fictional text) that exercise the
deliverable composer's rules one at a time (`ConsultDeliverableComposition`,
`ConsultAggregateRenderer`, `RenderDeliverableMacrosActivity` with `ComposeAt25`,
`ConsultSignatureAppend`, and the release in `ConsultGenerationEngine`), so a generated
deliverable template has today's bytes to reproduce rule by rule (Part 10 § 3.3). Each package
gives every rule its own deliverable, so one capture shows each rule's bytes beside the others.
`ByteEquivalenceFixtures.Load` finds them after `vendored/`; their captures have `"tag": null`.
They have no `dag.mmd`. Validate one with
`dotnet run -v q --file scripts/validate-workflow-package.cs -- tests/Fixtures/ByteEquivalence/fixture-packages/<name>`.

At 30 the validator refuses a deliverable that does not transitively include a `for`, so every
"scalar" source here (a prose node, the while's body, the if's and case's nodes, the let) reads
a loop's text in its prompt or expression; the composer still reads each as one scalar part.

### `composer-sources`

One deliverable per kind of source: `single` (one prose node), `pair` (two), `topics` (a `for`
over an input with an `if` inside), `sections` (a `for` over a node's list, `labelField` and
`max: 3`, with a `for` inside), `summary` (a `while` with `checkFirst`, `until` and `max: 3`),
`plan` (an `if`), `tone_line` (a `case`), `tally` (a `let` of type text), and
`summary_then_opening` and `topics_then_opening` (the `while` and the `for`, each followed by
`opening`). The input's label (`Requested topics`) differs from its loop's (`Topics`).

| Scenario | Rules it reaches |
|---|---|
| `every-source` | one scalar source verbatim; two joined by one blank line in `sources` order; a `for` as `## <name>` blocks in item order, named by `labelField`, an item whose inner `if` took no branch left out; a nested `for` (a section heading, then its points' blocks); a `while`'s last pass's text (two passes); an `if` and a `case` that took a branch, as their returned node's text; a `let` of type text as its plain text; a `while` and a `for` each joined to a following source |
| `branches-not-taken` | a `for` whose every item's inner `if` took no branch, composed empty; a `for` over a node's list pinned empty (zero items), composed empty; a `while` that ran no pass (`checkFirst`), composed empty; an `if` and a `case` that took no branch: the deliverable is skipped, `source node.<flow> took no branch`; beside another source, the zero-pass `while` contributes no part (the text is the other source alone) but the all-skipped `for` contributes an empty part (the text starts with a blank line) |
| `awkward-texts` | an empty source (composed empty; in a join it still takes its place, so the text starts with a blank line); leading and trailing newlines and spaces trimmed from each part; a part holding its own `##` headings, verbatim; a reply holding `{{` rendered as a template before it composes (`{{ 3 * 2 }}` → `6`, `{{ "{{" }}` → `{{`) |
| `item-names-and-bounds` | `labelField` text trimmed, cut to 120 characters with `…` (mid-word, and where the character before the cut is a space, which is trimmed: 119 characters), and a blank one falling back to the input's label plus position (`Requested topics 4`) for an input fan and the loop's label (`Sections 2`, `Points 1`) for a node's list; an inner `for` with zero items inside an outer item (its block is the heading and an empty text); a `for`'s `max` leaving an item unrun and without a block; a `while` stopped at its bound, the third pass's text |
| `unparseable-reply` | a reply whose `{{` reads what the scope does not hold fails the node and the job; deliverables not yet released are unsettled |
| `failed-item` | a loop item that failed (`failIfEmpty`): the composer's `<label> could not assemble: <key> did not complete` fails the job |

### `composer-macros`

One deliverable per macro rule, all over `node.letter_body` except `sections_note` (a `for` over
an input whose body node places `section_intro` before and `section_close` after, gated by the
item): `prepended`, `appended`, `both` (two prepends, two appends), `gated` (a macro's own `if`,
a placement's `if`, a macro that renders empty), `optional` (`followup_offer`, default on;
`leaflet_offer`, default off), `authored` (reads `profile.name`, `profile.signature` and
`run.date`), `signed_letter` (`signature: true` after an appended macro), `signed_only`
(`signature: true`, no macro), `review_summary` (an `if` on `input.visit`, a prepended
letterhead) and `echoes`, whose placed macros read `node.letter_body.content` (`body_echo`),
`macro.new_visit_note.content` (`visit_echo`) and `deliverable.review_summary.content`
(`quoted_review`, optional, default off). `letter_body` is pinned with a trailing blank line.

| Scenario | Rules it reaches |
|---|---|
| `new-visit-defaults` | prepend, append and both, each one blank line from the sections; two at one place in placement order; a macro's own `if` holding and a placement's `if` not (not placed); a placed macro rendering empty, its empty piece still joined (four newlines); optional macros defaulted (on placed, off not); `profile.name`, `profile.signature` and `run.date` (`2026-11-10`) in one macro; node macros before and after a loop item's reply, the after one gated off for one item, the reply's own trailing blank line kept inside the item; the signature block appended last, after a macro and with none; a placed macro reading a source node's text untrimmed (the composed part is trimmed); a placed macro reading a live macro's `.content` (with its file's trailing newline); a deliverable skipped by its `if` |
| `review-visit-choices` | a macro's own `if` not holding and a placement's `if` holding; the empty-rendering macro rendering text; optional macros chosen on (default off) and off (default on); a placed macro reading an inert macro's `.content` as empty; a placed macro reading another deliverable's composed text (its sections, without that deliverable's own macros) |
| `no-signature` | `signature: true` with no signature on the profile: nothing appended, never refused; `profile.signature` rendering empty, the macro's trailing whitespace trimmed |
| `empty-body` | macros and signature around empty sections: a prepend leaves a trailing blank line, an append and the signature a leading one, prepends and appends four newlines apart |
| `skipped-deliverable-read` | a placed macro reading `deliverable.<id>.content` of a deliverable skipped by its `if` fails the macro's render and the job (`Cannot get the member … for a null object`); the deliverables waiting on it are unsettled |

The validator refuses `signature: true` beside a placed macro that reads `profile.signature`
("a deliverable is signed once"), so `authored` is not signed.

### `composer-gates`

`note` and `record_copy` source the same nodes (a `for` over a text array whose label, `Problem
list`, differs from the loop's, `Problems`, then a prose node),
each gated by a `terms_subset` check; `patient_copy` has an `if` on `input.audience`;
`cover_letter`'s node reads `deliverable.note.content` (and `deliverable.patient_copy.content`
under the same `if`) in its prompt.

| Scenario | Rules it reaches |
|---|---|
| `clinician-checks-pass` | two deliverables over the same nodes, the same bytes; input items named by the input's label plus position (`Problem list 1`); both checks passing; a deliverable whose `if` is false, `skipped` with ``needs `input.audience == "patient"`; it does not hold``; a prompt reading another deliverable's composed text |
| `patient-copy-check-fails` | one check passing and one refusing, `failed` with its `failWith` sentence; the `if` holding |
| `note-check-fails` | the other deliverable's check refusing; a prompt reading a deliverable whose check refused it still reads its composed text |

A prompt that reads `deliverable.<id>.content` of a deliverable skipped by its `if`, unguarded,
fails the job today (`Cannot get the member deliverable.<id>.content for a null object`), and a
bare `deliverable.<id>` is refused at publish; `cover_note` gates the read on the same input.

### The edge-case packages (#1508)

Seven more hand-written 30 packages (`v2026.11.1`, fictional text), added by area 11 (#1508, Task 35) as the owner ruled
([comment 6, item 3](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171840295), and
comment 7, "Edge-case packages"): one per case the review of area 10 found, and one for the differences that
shaping those six would have hidden (the controller's ruling, fix round 1), each two or three nodes over a loop (30's
validator requires one), captured like the composer packages. Each shows 30's output after the migration, and the warning
or refusal a new 31 package gets without the migration's explicit thing (`tests/V31Migration/EdgeCasePackagesTests.cs`).
An eighth, `edge-source-no-branch`, was added by area 14 (#1543, ruling 3, Task A6) the same way: a deliverable with two
sources, one a flow that takes no branch.

| Package | Scenarios | The case |
|---|---|---|
| `edge-missing-marker` | `reply-leaves-macro-out`, `reply-holds-macro` | the letter's prompt offers `follow_up`, which 30's model chose (its instruction says "otherwise nothing"); the reply leaves it out, or writes its marker. The macro's body has no final line ending, as 30 wrote a marker's body whole and 31 drops one |
| `edge-no-branch` | `plan-left-out`, `plan-included` | the deliverable `plan` reads only the `if` `plan_gate`, which takes no branch unless `include_plan` is true; 30 skips the deliverable |
| `edge-items-not-run` | `third-problem-not-run` | the loop has `max: 2` and three problems; the note, composed from the loop, has a block for the two that ran; `problem_list` reads the rendered node `problem_lines`, whose template loop writes a line per item, the item not run too (30's text), which R16's state guard leaves out at 31 (unruled, for the owner) |
| `edge-unset-object` | `intake-unset` | the optional object input `intake`, unset; its `reason` is read by the problem prompt and by the note's prepended macro `intake_line` (a snippet at 31) |
| `edge-answer-outside-set` | `answer-outside-set`, `answer-in-set` | the classifier `triage` (`routine`, `urgent`) answers `soon`: 30 refuses the reply (`onInvalid: fail`) and fails the job |
| `edge-flag` | `reply-does-not-fit` | `summary` declares `onInvalid: flag` and its reply does not fit its format (`line` a number): 30 keeps it and runs on |
| `edge-source-no-branch` | `no-branch`, `branch-taken` | added by area 14 (#1543, ruling 3): the deliverable `note` reads `summary`, which always runs, and the `case` `letter_case` on the enum input `route`, whose one branch is `letter`; with `route: none` the case takes no branch and 30 skips `note` though `summary` ran. `summary_note` (the problem loop and `summary`) keeps the start from being refused, as a job with no deliverable to compose is |
| `edge-unruled-whitespace` | `default` | two differences no ruling covers, shown: the letter's reply places `follow_up` (a macro the model chose) whose body ends with a line ending, which 31 drops; the problems deliverable prepends `clinic_line`, `Seen at: {{ input.clinic }}` with the clinic unset, which 30 trimmed and 31's snippet keeps. Each paragraph is followed by `problem_close`, placed `after` by 30's engine, its body ending in two extra line endings, which the node that strips each item of the problem loop takes off as 30 trimmed them (#1510), so `macro-trailing-whitespace` has no hit here |

`edge-tests/<package>.json` is each package's pinned test: an in-package test (part 07 § 8) of the edge scenario's
capture, its replies under their 31 keys as the comparison replays them (`EdgeCasePackagesTests` holds the file to
`ByteEquivalence.RequestFor`), and 31's deliverable text as its `expect` (30's, but at the sites `EdgeCasePackagesTests.Unruled` lists, each a
difference no ruling covers, left `expected: false` with its reason). `edge-unruled-whitespace`'s replies hold
`problem_close`'s marker, which the comparison writes into the captured reply. It is not in `migrated/`: the tests add it to
the migrated package as `tests/edge.json` and publish it offline with `PackagePublish.CheckAsync`. Two of them pin the
reply 31 refuses as a stub (S07-009, D6; their `expect` is `{}`): `edge-answer-outside-set`'s (its problem stubs are `answer-in-set`'s, since 30's job
stopped at `triage`) and `edge-flag`'s.

## `scenarios/` and `captures/`

`scenarios/<package>/<scenario>.json` says how to run one package; `captures/<package>/<scenario>.json`
is what today's engine did with it. `PackageCaptureGoldenTests` runs every scenario through
`CaptureRun` (the real starter, entity and orchestrator, the activities' production halves, no
model) and compares the capture document byte for byte. A package name may hold `/`
(`test/general` is `scenarios/test/general/<scenario>.json`).

A scenario has exactly these members, each present:

```json
{
  "description": ["What the run reaches, in a line or a few."],
  "inputs": { "consult_draft": "Fictional text.", "include_billing": null },
  "profile": { "name": "Dr A. Sample", "signature": "Dr A. Sample\nInternal Medicine" },
  "macroChoices": {},
  "outputs": { "judge:1": { "verdict": "revise", "feedback": "…" }, "scope": { "value": "in_scope" } }
}
```

- `inputs` names every declared input; `null` leaves an optional one unset. Values are what the
  starter accepts, and always fictional.
- `profile` is the display name and the chosen signature block's text. Every scenario uses the
  one above, except `composer-macros/no-signature`, whose `signature` is `null` (no block chosen).
- `macroChoices` sets optional macros on or off by id; `{}` takes the defaults.
- `outputs` pins a reply by instance key: `node`, or `node:path`, where the path is item ids and
  while passes joined by `/` (`draft_paragraph:2/3`, `judge:2`, `read_letter:0`; a text array's
  items count from 0, a node's list from 1, a folder's by item id). A text format's pin is a JSON
  string, a structured format's the JSON value, a classifier's `{ "value": "<member>" }`. Every
  other reply is a stub made from the node's format, seeded by its key. A pin that never runs,
  a `null` pin, and a stub the engine flags are refused.

Pin only what a branch needs. To learn the keys, run the scenario with `outputs: {}` first and
read the capture's `outputs`.

A capture holds `package`, `tag` (the vendored tag, or null), `scenario`, `status`, `failure`,
`outputs` (every reply served), `prompts` (each agent node's sent text, trailers included),
`renders` (each rendered node's text), and `deliverables`: one row per manifest deliverable,
`composed` with its final text, `skipped` or `failed` with the engine's reason, or `unsettled` in
a failed job. Every text sits beside its `sha256:` hash.

`status` is the job's (`Completed` or `Failed` in every capture here), or `Refused` for a refused
start (below).
`failure` is null unless the job failed. It is the engine's sentence as the test context surfaces
it: the raw message of the failing activity, where production's Durable runtime wraps it (`Task
'…' (#N) failed with an unhandled exception: …`). `CaptureRun.Failure` takes the first non-null of
the job state's `AnalysisError`, `FailureError` and `StartFailure`, in that order.

In a failed job, which deliverable rows are `composed` and which `unsettled`, and which prompts
were sent before the failure, follows the test context's one-at-a-time dispatch order;
production may get further before failing the same way.

### Adding a scenario

Write the scenario file and run
`dotnet test tests/Consultologist.Api.Tests.csproj --filter FullyQualifiedName~PackageCaptureGoldenTests`.
The missing capture is written and its row fails once with `Wrote <path>; review it and commit
it.` Read the capture in full: the prompts carry the scenario's inputs, the branches the
description names were taken, each deliverable has its headings, macros and signature. If it did
not do what the description says, fix the scenario, delete the capture, and run again.

### Regenerating

Delete the capture and run its test. A capture is never rewritten in place: a run whose bytes
differ fails.

### Coverage

Every vendored package has a `default` scenario. Every deliverable of a package with scenarios
is `composed` in at least one of its captures, and every node with a prompt has a `prompts` or
`renders` entry in one. A deliverable or node no scenario can reach on today's engine goes in the
test's exception table with the reason; an entry a capture does reach is stale and fails the rule.

A start the engine refuses is captured too, when it is the refusal a well-formed request can get:
`NoApplicableDeliverable` (no deliverable's condition holds, or a fanned input has no entries).
Its capture has `status` `Refused` (the runner's word, not a job status), `failure`
`NoApplicableDeliverable: <the starter's sentence>`, no outputs, prompts or renders, and every
deliverable `unsettled`. Any other start error (inputs that do not fit the manifest, an undeclared
macro choice, a rate limit) throws, so a mistake in a scenario is never frozen as a refusal. A
refused capture composes nothing, so it adds nothing to the coverage rules.

### `example-macros` and `example-macros-warnings`: fixed, now complete

Area 01 first captured both packages (v2026.11.3) failing in every scenario at node `summary`.
Their summary prompt reads the `notes/` macro folder (`{{ for m in macro.notes }}… {{ m.prompt }}`),
and two engine bugs stopped it:

1. the completion cross-check (`ConsultNodeCompletion.SanctionedBy`) walked the package without its
   macro folders, so no folder item was a read it sanctioned (#1514, engine v2026.11.6);
2. the mechanics sentence named a folder item by its bare id (`{{ macro.red_flags.content }}`),
   which the reply cannot render; it now names `macro.<folder>.<item>` (#1516, engine v2026.11.7).

Both fixes were merged from `main` into `m39-build` and these captures regenerated: all sixteen
complete, every requested section, the summary and both deliverables composed. The exception
tables are empty. A live run of `example-macros` on v2026.11.7 completed too.

### `example-v12-constructs`: `default` fails, a lone refusal does not

The `default` capture is `Failed`, with the acknowledgement's `failWith` sentence. With generated
stubs both `terms_subset` checks refuse (`consult` and `ack_letter` are `failed`), and a job whose
every deliverable is refused by its check fails (`ConsultDeliverables.FinalOutcome`). A lone
refusal leaves the job `Completed`: `letter-coverage-fails` (`ack_letter` failed, `consult`
composed) and `note-coverage-fails` (`consult` failed, `ack_letter` composed).

## `renderers/`

`concept-renderers.json` freezes the bytes of the two C# concept renderers, `ConceptRenderers.Bullets`
and `ConceptRenderers.Context`, so the package-format registry's Scriban templates
(`v31/renderers/concept_bullets.md`, `concept_context.md`) can be held to them after the C#
renderers are gone (Part 10 § 3.3). It holds twenty cases: the thirteen of
`V31BuiltInRenderersTests` and seven more (`two in order`, `non-ASCII term`, `empty term`,
`null type on a SNOMED concept`, `null id on a SNOMED concept`, `newline in support`, `long list`).
Each case has its `name`, its `concepts` (`term`, `type`, `id`, `isSnomedConcept`, `isActive`,
`support`, `source`; a null `type`, `id` or `support` is JSON `null`, and the C# renderers receive
`""` for a null type or id) and the two renderings, `concept_bullets` and `concept_context`.

`ConceptRendererCaptureTests` writes it from the C# renderers and compares byte for byte, then
renders the registry's templates on each case's concepts and compares them with the strings read
from this file.

Regenerate: delete the file and run
`dotnet test tests/Consultologist.Api.Tests.csproj --filter FullyQualifiedName~ConceptRendererCaptureTests`.
The first test writes it and fails once; review it and commit it.

`ConceptRenderers` joins its lines with `Environment.NewLine`, so the fixture's bytes depend on the
platform. It was generated on Linux, as CI runs; regenerate it only there.

`ConceptRendererCaptureTests` compiles against `V31BuiltInRenderersTests.Concept`, `.Cases`,
`.ToConcepts` and `.Render`. When the C# renderers and `V31BuiltInRenderersTests` are deleted,
`Concept` and `Render` move into `ConceptRendererCaptureTests`, and its first test
(`TheCSharpRenderers_GiveTheCapturedBytes`, the C# side, with the case table and `ToConcepts` it
uses) is deleted with them.

## What area 10 did with the captures and the renderer fixture

This folder is the specVersion 30 side of every comparison in part 10 § 3. Area 10 (#1507) built the 31 side:
`ByteEquivalence` (`src/Consultologist.PackageFormat/V31/Harness/`) holds each capture against the offline run of the
migrated package and writes `migration-report.json` (`reports/`, below). The captures are read, never rewritten.

- **Sites.** The proposal above was adopted, with one change (the plan's D11): a site names the 31 row key, not 30's
  key or a deliverable's bare id. It is `<package>/<scenario>#prompts/<31 row key>`, `#renders/<31 row key>`,
  `#deliverables/deliverable.<id>`, or `#status` for the job's status word (30's `Completed`, `Failed` or `Refused`
  beside 31's). A 30 text whose key cannot be mapped keeps its 30 key after `#prompts@30/` (or `#renders@30/`,
  `#deliverables@30/`). A side that lacks a text has the hash of the empty text. An `unsettled` or `failed` row,
  and a 31 deliverable its check blocked (withheld, never delivered), count as no text; a deliverable neither side
  has text for is not compared.
- **Keys.** `KeyMap.To31` and `KeyMap.To30` map 30's instance keys to 31's row keys and back, through the migration's
  `Keys` and each loop's numbering: an array input's items, which 30 counted from 0, count from 1 at 31; a while's
  pass is `#<n>` where its while sits.
- **The stubs.** Both of the contract's ways are used (D12). `ByteEquivalence.RequestFor` replays every value of the
  capture's `outputs` under its 31 key, with what 30's engine added after the reply (a `concept_list`'s `source`) and
  every member the 31 format lists (`null` where it takes `null`); a reply the capture lacks is generated by
  `WorkflowStubOutputs` seeded with 30's key. A scenario's inputs and `macroChoices` go through 31's start, so a
  supplied `" "` is not given, as the scopes below say; the run date is the captures' `2026-11-10`.
- **Expected differences.** A difference is expected only when a rule of `ExpectedDifferences` explains it exactly,
  naming its ruling or spec section; the rest stay `expected: false`, for the owner (the plan's *Results*).

The renderer fixture is unchanged: the C# renderers are still there, and its strings are what the registry's
Scriban templates must equal once they are gone.

## `migrated/`

`migrated/<package>/` is the migrated 31 source of each of the twenty-four packages (the thirteen vendored ones, the
three composer packages and (#1508, #1543) the eight edge-case packages; `test/general` at `migrated/test/general/`), as
`WorkflowPackageMigration30To31.Migrate` writes it with the package's hand choices (`tests/V31Migration/ExampleChoices.cs`:
`parts` renamed `sections` in `example-nested-note`; `whenEmpty` `fail` for its `note` and `hold` for
`test/general`'s `consult`; `test/general`'s snippet folder `standards` typed `standard_section`). Each folder holds
`manifest.json`, every 31 file, and `notices.json`, which is not a package file:

```json
{
  "notices": ["every notice, in the migration's order"],
  "handEdits": ["the hand-edit notices among them (S10-002)"],
  "refusals": []
}
```

A package with no hand edit carries its snapshot test, `tests/migration_snapshot.json`; one with hand edits carries
none, and its last notice is A10-011. `MigratedPackagesTests.TheMigratedSource_IsTheGolden` holds each folder to the
migration, file for file and byte for byte, with no file on disk the migration does not write. The same class holds
the invariant (no hand-edit notice ⇔ `CompiledPackage.Publish` refuses nothing), that nothing unknown is written, and
that migrating twice gives the same bytes. `migrated/general/` is the vendored package `general`, not `test/general`.

Two families of files have no 30 counterpart (#1510, Part F, the owner's second fix-first group): each `for` whose
`.content` is read returns its items through a `rendered` node the migration writes, `nodes/<loop>/<ret>_stripped.md`
(A10-088; 39 of them, in 23 packages), and a signature 30 appended is piped through a package renderer,
`renderers/signature_after_blank_line.md` or `renderers/signature_after_line_end.md` (A10-089; in `composer-macros`,
`example-classifier-scope` and `example-v12-constructs`). The comparison credits a stripping node's texts by
`loop-item-strip-node`.

Area 14 (#1543) moved three things here, each regenerated once (its Task A9). The signature renderers' header takes
`input` and `format` `markdown_block`, as the profile's signature now is (ruling 2). A `markdown_block` read with text
beside it on its line is separated from that text by a blank line (A10-093; part 10 § 1.2's moved-read row):
`composer-macros` `snippets/body_echo.md`, `edge-unruled-whitespace` `snippets/clinic_line.md` and `edge-unset-object`
`snippets/intake_line.md` (whose snapshot test's `note` hash follows); no vendored package has the shape and none gets
an A10-094. A10-079 says *"when a source did not run (…)"* (ruling 3), in `composer-sources` and `edge-no-branch`. No
migrated body holds a `\` the migration wrote: no fixture has a single line ending inside a paragraph at a site.

## `hand-edits/`

`hand-edits/<package>/` holds the edits an operator makes after the migration (part 10 §§ 1.4, 1.5): edits and a
`README.md` for each package whose migration says a hand edit, and none for any other (`HandEditsTests.EveryPackageWithHandEdits_HasItsEdits`).
Every package's folder holds its `options.json` (#1508, R18): the migration's choices, `{ "renames", "whenEmpty",
"folderFormats" }`, each `{}` for none, read by `ExampleChoices` and `Migration31Options.FromJson` (`MigrationOptionsTests`)
and recorded in `reports/<package>.json`'s `options`; `test/general`'s and `example-nested-note`'s are the only ones
with a choice. `MigrationFixture.Edited` lays a folder over `migrated/<package>/`:

- a file replaces or adds the file of its path (`manifest.json` the manifest);
- a file named `<path>.deleted` removes `<path>`, which must be a file the migration wrote (its content is not read);
- `README.md` is not a package file: it is the account of the edits (below);
- `options.json` is not a package file: it holds the choices the package was migrated with (above);
- `keys.json` is not a package file either: when an edit moves a node, it maps each moved 30 node id to its new 31
  rooted key (`{ "section_draft": "node.sections.applies.section_draft" }`), laid over the migration's `Keys` so the
  comparison maps 30's keys to the moved rows; each id must be one the migration keyed. Only `example-problem-list`
  has one;
- `strip-nodes.json` is not a package file either (#1510, item 1): when an edit writes its own node to strip a loop's
  items in place of the migration's, it maps that node's rooted key to the rooted key of the `ret` it strips
  (`{ "node.sections.applies_stripped": "node.sections.applies" }`), laid over the migration's `StripNodes` so the
  comparison credits its texts as the migration's (`loop-item-strip-node`); a stripping node whose file a `.deleted`
  removes leaves them. Only `example-problem-list` has one.

The README is parsed by `HandEditsTests.EachNotice_IsAnswered`. Each hand-edit notice is one line that starts
`- Hand edit needed in `, exactly as the migration says it, in `notices.json`'s `handEdits` order, and the line after
it is either

```
  - Answered in `<path>`, `<path>`: how the edit answers it.
```

naming in backticks, before the first `` `: ``, the files of the folder that answer it (each a file of the folder or
the `<path>` of a `<path>.deleted`), or, for a package not edited,

```
  - Not made: why.
```

The notices must be exactly the package's hand edits, in order. Every file of an edited folder, `keys.json` and
`strip-nodes.json` included, must be named in backticks somewhere in its README. The edited package must publish with no refusal
(`TheEditedPackage_Publishes`) and get its snapshot test from `WorkflowPackageMigration30To31.Snapshot`
(`TheEditedPackage_GetsItsSnapshot`); the snapshot is not stored here.

Nine of the twelve packages are edited (#1508: `edge-missing-marker` and `edge-unruled-whitespace` among them). `example-macros` and
`example-macros-warnings` are not (`HandEditsTests.NotEdited`, with the reason): some of their 30 instructions are
templates whose 31 form needs a choice the notices do not settle; nor is `example-nested-note`, whose one edit is a
`max` the owner ruled is not guessed (it is compared as migrated). Their folders hold only a README, each notice followed by a `Not made:` line, and
`APackageNotEdited_StaysRefused` holds that they still do not publish. An edit is made as its notice says and as the
spec's own examples make it, changing the fewest files (the plan's D28).

## `reports/`

`reports/<package>.json` is `migration-report.json` (part 10 § 3.2, `MigrationReport.ToJson`, canonical JSON) for each
package that is compared, over all its scenarios in ordinal order: the twelve with no hand edit
(`ByteEquivalenceTests.TheReport_IsTheGolden`) and the nine edited ones and `example-nested-note`, compared on their edited files with the
migration's keys and notices (`HandEditsTests.TheReport_IsTheGolden`). `handEdits` is the migration's hand-edit
notices (so an edited package's report still lists them), `notices` every other notice, then A10-063 for a package
with more than one deliverable, then the worst-case note when it is said; `texts` one entry per compared text, with
`site`, `oldHash`, `newHash`, `equal`, `expected` and `explanation` (`null` unless expected). The report is never a
gate: no test asserts how many texts differ. `example-macros` and `example-macros-warnings` have no report.

Area 14's rules moved three reports' counts (explained / partly explained / unexplained), and raised no unexplained or
partly explained count: `composer-macros` 38/0/0 → 42/0/0 (each scenario's `echoes`, by `block-read-own-line`),
`edge-unset-object` 2/0/0 → 3/0/0 (`intake-unset`'s `note`, equal before, now differs by the blank lines alone),
and `composer-sources` 80/1/0 → 81/0/0 (`item-names-and-bounds`' `tally`, by `count-over-accepted-name`); the new
`edge-source-no-branch` is 4/0/0. `edge-unruled-whitespace` stays 8/0/0, its `problems` now credited by
`block-read-own-line` with its other rules.

## Regenerating area 10's goldens

`conditions/converted.json`, `conditions/equivalence.json`, `migrated/` and `reports/` are written from code. With
`GOLDEN_UPDATE_V31=1` their tests rewrite them in place instead of comparing:

```
GOLDEN_UPDATE_V31=1 dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Migration"
```

Then run it again without the variable, and read every file that moved (`git diff`) before committing it. The reports
are built from the migration in memory, so one run is enough. `hand-edits/` is written by hand: when a package's
hand-edit notices change, its README is edited to list them, and an edit that no longer answers its notice is
remade. Nothing under `vendored/`, `fixture-packages/`, `scenarios/`, `captures/` or `renderers/` is rewritten by
area 10. Its one change to area 01's files is an addition: the entry `row.strip.input-field` in
`conditions/expressions.json` and its vectors in `conditions/vectors.json` (written by `ConditionVectorTests`, as above),
added in its Task 12; no existing entry or vector moved.

## `conditions/`

What today's condition and `let` evaluator answers, for the converter from 30's Scriban
expressions to JSON Logic (Part 10 § 2.4). `expressions.json` is hand-written; `vectors.json` is
written by `ConditionVectorTests` from today's evaluator. Area 10 added `converted.json` and
`equivalence.json`, the converter's side (below).

### `expressions.json`

One entry per expression, in this order: every condition, `case` and `let` of the vendored and
fixture packages, then one or more per row of § 2.2's table, then one or more per refusal of
§ 2.3. Each has:

- `id`; `kind`, one of `condition`, `case` (the expression a `case` node switches on: a value,
  not a boolean) and `let`; `expression`, the text as written; `letType`, a `let`'s declared type
  (`text`, `number`, `boolean` or a format id), else `null`.
- `origin`: `package <name>, <site>` for a package's, the site one of `node <id>, if|when|while|until|case|let`,
  `node <id> macro <macro>, if`, `deliverable <id>, if`, `deliverable <id> macro <macro>, if`, or
  `file <path>, if` (the header of a snippet, folder item, macro or macro-folder item file);
  `§ 2.2: <the row's left cell>` (a cell's `\|` written `|`); `§ 2.3: <the bullet>` (without its
  closing `;` or `.`). `EveryConditionAndLetOfThePackages_IsInTheExpressions` finds the package
  sites from the files (the typed manifest and the text headers) and fails on a site the file
  lacks or an entry naming no site; `EveryRowOf2_2_AndEveryRefusalOf2_3_HasAnExpression` holds
  the origins to the table's sixteen cells and the four bullets.
- `reads`: what the expression reads, in order, each with its type. A type is a name or an object:
  - `input.<id>`: `text`, `enum` (`values`), `number`, `boolean`, `date`, `array` (`items`, a type)
    or `object` (`fields`, name to type);
  - `node.<id>`: `text` (its content), `classification` (`values`) or `object` with `schema`, the
    node's format's JSON Schema (its fields are read from the schema);
  - `item`: `{ "id": "text" }`, a snippet folder's item, or `{ "value": <type> }`, an input fan's
    element;
  - `pass.number`; and, beyond the brief's list, `run.date` (`date`) and `profile.name` (`text`).

  An input or a field may carry `"required": true`, and a node read `"settled": true`; either
  takes the absent value away (below). A property a node's schema lists in `required` is required
  too: a reply without it is refused by the node's format. Every such node read today is
  `onInvalid: fail` (`judge` in `composer-sources` and `example-nested-note`, `plan` in
  `example-nested-note`, `read_letter` in `example-prior-letters`); a node with `onInvalid: flag`
  would keep an invalid reply, so a property its schema requires could be absent there. The § 2.2
  expressions' own `node.triage` schemas list no `required` properties, so their absent-field
  cases remain.
- `note`, optional: why a read is `settled`.

  `APackageExpressionsReads_AreItsPackagesDeclarations` holds a package expression's reads to its
  package: an input's type, values, `required` and fields (each field's type, values and
  `required`) as declared; an `item.value`'s fields as the input its loop runs over declares them;
  a node's type as the engine types its format (`ConsultRenderPayload.FormatTypeOf`), a
  classifier's values, an object node's schema its format's; and a `note` beside every `settled`.

#### `required` and `settled`

A package expression's `required` is its package's declaration: the start refuses a run without a
required input or field, so no condition ever sees one absent. A § 2.2 or § 2.3 expression's reads
are the fixture's own, all optional, so they keep their absent cases.

A node read is `settled` when the engine only judges that site after the node has an output. The
package reads marked so, each with its `note`: `node.judge` by `composer-sources`' `until` (an
`until` is judged after a pass); `node.judge` by `example-nested-note`'s `while` (no `checkFirst`,
so its check comes after a pass); `node.read_letter` by `example-prior-letters`' `until` (judged
per item once that item's body has settled); `node.plan` and `node.topic_loop` read by a `let` (a
let runs once its reads have settled). A node a run can really meet without an output stays
unsettled: a classifier a deliverable's or snippet's `if` reads (when it failed or was skipped),
a node gated by an `if`, a `while` body under `checkFirst`.

### The scopes

Each read's values (the absent value written `"<absent>"`; a `required` or `settled` read has none):

| Type | Values |
|---|---|
| `text` | absent, `""`, `" "`, `"lorem"`, each quoted string literal of the expression |
| `enum`, `classification` | absent, each value |
| `number` | absent, `0`, and `n - 1`, `n`, `n + 1` for each number in the expression |
| `boolean` | absent, `true`, `false` |
| `date` | absent, `"2026-11-09"`, `"2026-11-10"`, `"2026-11-11"` |
| `array` | absent, `[]`, one item, three items (the item type's present values in order, repeated as needed) |
| `object` | absent, every field at its first present value, then each field through its own values (absent is the field left out) with the others there |
| `item` | an id: `"1"` and each quoted literal; a value: the value type's present values (an element is never absent; its fields may be) |
| `pass.number` | `1`, `2`, `3` |
| `run.date`, `profile.name` | the type's values but absent: the run's and profile's facts are always there |

The cases are the product over the reads in order, the first 200
(`TheCases_AreTheProductInDeclaredOrder_CutAt200`).

Each case's scope is built by the engine's own functions (`ConditionScopes.Build`): a package
declaring exactly what is read, required as the read says, and `consult_draft` supplied so the
start has an input. The inputs pass through `ConsultGenerationJobStarter.NormalizeInputs` and
`ConsultGenerationJobStarter.ResolveEffectiveInputs`; the scope is
`ConsultRenderPayload.ScopeFor` over that effective map, typed by `ConsultConditionScope.TypesOf`.
So:

- an absent input is the effective map's `""`, which the render reads as `""` (text, enum), null
  (number, date, boolean), an empty array or an empty object
  (`AnAbsentInput_ReadsAsTheEngineMakesIt` pins each);
- an input is normalised first: trailing whitespace is cut, so a supplied `" "` reaches the
  condition as `""` (an input never carries whitespace-only text); a required text field of an
  element supplied `""` or `" "` is accepted by the start and reads `""`. A case's `scope` in
  `vectors.json` writes an input's value as supplied, before the start normalises it (its `" "`
  reaches 30 as `""`): area 10 passes the inputs through 31's own start, or compares against the
  normalised value;
- an input cannot carry null at 30: it is supplied or absent, the start refuses a JSON `null` for
  a scalar input, and absence is the effective map's `""`. A number, date or boolean read sees
  null only as that absence; a text read never sees null;
- a node is `prose` (text) or `classification` from the bundled catalogue, or a package-local
  object format with the read's schema; an absent node is on the skipped list, as the engine
  carries a flow that took no branch or a while's body before its first pass: `.content` and
  `.value` read null, and a member of its value fails (`Cannot get the member … for a null object`);
- an item is the fan's: an input fan's element through `WorkflowInputFans.Items` over
  `input.items`, a snippet folder's item over `snippet.sections`;
- the pass is pass `n` after `n - 1` earlier ones; the run date is `2026-11-10` unless read; the
  profile name is `Dr A. Sample` unless read.

### `vectors.json`

Per expression, in order: `id`; `refused`, 30's sentence when the expression is not one (a
template as the walk refuses it, anything else as `WorkflowConditionAt25.TryParse` does), else
`null`; and `cases`, none when refused. Each case has its `scope` (read to value), its `result`
and its `error`:

- a `condition`: `WorkflowConditionAt25.Holds`'s boolean, the subject `Condition '<id>'`;
- a `case`: the value it switches on, as JSON. `WorkflowFlowDecision` does not expose that value,
  so it is evaluated with `WorkflowLetAt30.Evaluate` in the same context (the same parse, scope
  and pipes), not taken from the decision itself; `error` is `WorkflowFlowDecision.Decide`'s
  sentence when it is not a text (`Node '<id>' case must yield a declared value; it yields …`);
- a `let`: what `WorkflowLetAt30.ValueOf` stores, as JSON (a text bare, as a JSON string), the
  subject `Let '<id>'`; `error` is its errors joined as the activity joins them. A format-typed
  `let` here has no schema, so only its being an object is checked.

An `error` (else `null`) is the sentence 30 throws when it judges this expression over this scope;
`result` is then `null` (a `case` keeps its value). The renderer pipes are the engine's
(`TemplatePipes.Engine`).

A `let` cannot be a bare list at 30, so `[x, y]` appears here only inside an object
(`row.let-array`): the walk refuses a list it can type against every declared type (`Let '<id>'
yields an array; it declares type <type>.`), and `ValueOf` fails any list
(`Let '<id>' yielded a list; it declares type <type>.`); `ABareListLet_IsRefusedAt30` pins both.

### What area 10 did with them

Each `expression` is converted with `WorkflowConditionConverter.FromScriban` (part 10 § 2.1) over the 31 scope its
`reads` describe (`MigrationFixture.ScopeOf`); a refused one, and every § 2.3 one, converts to no logic. Every other
is evaluated with 31's evaluator on each case's `scope` (passed through 31's start, so a supplied `" "` is not given)
and compared with `result`. A case with an `error` is not an equivalence requirement: 31's result is listed beside
30's sentence. Area 10 added `row.strip.input-field` (`(input.patient.name | string.strip) == ""`) to
`expressions.json`, with its vectors, during its build.

### `converted.json`

What the converter gives for every entry of `expressions.json`, in its order:
`{ "converted": [ { "id", "logic", "resultFormat", "reads", "blankConversions", "blanks", "trimBlanks", "notices" } ] }`.
`logic` is the canonical JSON Logic, or `null` when the expression does not convert, with its `notices` (S10-003, or
the 31 checker's own sentence); `resultFormat` the type the checker gives it; `reads` its 31 paths; `blanks` each `!x`
on text written `blank(x)` (R1) and `trimBlanks` each `trim(x) == ""` written `blank(x)` whose `x` can be null at 31
(A10-050). `ConditionConverterTests` holds it to the converter.

### `equivalence.json`

31's answer beside 30's, expression by expression: `{ "reasons", "equivalence" }`. `reasons` is
`ExpectedDifferences.ConditionReasons` (`blank`, R1). Each `equivalence` entry has `id`, `kind`, `converted`,
`noticeIds` (for one that does not convert, which is not compared), `cases`, `agreed` (`null` when not converted) and
`listed`: each case that differs, or that 30 answered with an error, with its `case` index, `scope`, `at30`,
`error30`, `at31` and `reason`, which is `blank` (R1), another reason of `ConditionReasons`, `unruled`, or `null` for
a case 30 erred on. `ConditionEquivalenceTests` holds it to the run; `TheBlankDifferences_AreR1sCases` and
`TheUnruledDifferences_AreTheseOnes` pin the two lists. Four cases are `unruled`, for the owner:
`row.size.member#0` (`input.problems.size == 0` with the list absent: `true` at 30, `false` at 31, where `size` of
`null` is `null`), and `row.strip.node#0`, `row.strip.input-field#0` and `#2` (the node or the object absent, or the
field left out: `false` at 30, `true` at 31, as `blank(null)` holds). No public package's condition has either
shape.

### `!x` on a text, at 30

At 30 only null, `false`, an empty array or object and a skipped node are falsy
(`PromptTemplateRenderer.RenderingContext.ToBool`); a text is truthy whatever it holds. So `!x` on a
text holds only when `x` is null. Against a `blank(x)`, value by value:

| `x` | 30's `!x` | `blank(x)` | |
|---|---|---|---|
| null | true | true | agree |
| `""` | false | true | differ |
| whitespace-only | false | true | differ |
| other text | false | false | agree |

Which reads can carry each value: an input's text is never null (absent reads `""`) and never
whitespace-only (trimmed to `""`), so `!input.x` on a text is `false` in every case
(`row.not-text.input`); a node's text, an item's and a `let`'s value can be null (a node with no
output), `""` or whitespace-only (`row.not-text.node`). `AWhitespaceOnlyText_IsTruthyAt30`,
`AnEmptyText_IsTruthyAt30_Too` and `OnlyANullText_IsFalsyAt30` pin 30's side.

Part 10 § 2.3 named only the whitespace-only case as a difference, which contradicted today's
engine, which differs on `""` too. Area 01 raised it on #1498 and #1507, and the owner ruled
(#1507, R1, 2026-11-20): `!x` on text converts to `blank(x)`, and the difference (empty and
whitespace-only text, and an input not given) is listed by the migration (A10-008), marked
`blank` in `equivalence.json` and expected by the comparison (`blank-conversion`).

Its practical weight: no package uses `!` on a text today (no package expression uses `!` at all).
Where a conversion would meet one, the difference from `blank(x)` is wider than whitespace: it
covers every unset optional text input (absent reads `""`, so `!input.x` is `false` and
`blank(input.x)` is `true`) as well as whitespace-only text.

### Regenerating

Edit `expressions.json`, delete `vectors.json`, and run
`dotnet test tests/Consultologist.Api.Tests.csproj --filter FullyQualifiedName~ConditionVectorTests`.
The vectors are written and the test fails once; review them and commit them. Then rewrite
`converted.json` and `equivalence.json` with `GOLDEN_UPDATE_V31=1` (*Regenerating area 10's
goldens* above).
