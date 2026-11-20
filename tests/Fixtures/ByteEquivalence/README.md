# Byte-equivalence fixtures (#1498)

What today's workflow engine (specVersion 30) produces, frozen as in-repo files. Every file here
is read as exact bytes: `.gitattributes` holds `* -text`, so git never normalises a newline.

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

### Known failure: `example-macros` and `example-macros-warnings`

Today's engine fails both packages (v2026.11.3) in every scenario, at node `summary`, with
`Closing summary failed: Node 'summary' rendered macro.notes/red_flags.prompt, which the package's
walk does not reach from its prompt; the walk and the render disagree.` The summary prompt reads
the `notes/` macro folder (`{{ for m in macro.notes }}… {{ m.prompt }}`); the render records those
`.prompt` reads, and `ConsultNodeCompletion.SanctionedBy` — the walk that cross-checks them — does
not reach them. This is the engine as it is; it is captured, not worked around.

The captures hold status `Failed`, that sentence, and the prompts this harness sent before it: the
three standing sections and the summary. Here `summary` is dispatched before the
`requested_sections` loop, so no `narrative_draft`, `table_draft` or `patient_line` prompt is sent
and neither deliverable is reached; the test's exception tables say so. A production orchestrator
may dispatch independent nodes together, so it could send the requested sections' prompts before
the job fails the same way at `summary`.

`outputs` records each reply as served, before the node's placed macros are composed around it,
and with no deliverable reached nothing shows the composed node texts — so these captures cannot
show what the node-placed macros (`counselling`, `section_caveat`, `table_note`, `preamble`,
`sign_off`) add.

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
