# Area 10: Migration (specVersion 30 → 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry every specVersion 30 package to 31: a converter from 30's Scriban conditions and `let`s to typed, canonical JSON Logic; a library rewrite (`WorkflowPackageMigration30To31`) that turns one manifest and its prompt files into 31's folder of typed files, with notices, hand edits and a snapshot test; an offline deterministic run of a 31 package; and the byte-equivalence comparison of that run with area 01's captures, written as `migration-report.json`.

**Architecture:** Four pure pieces in `Consultologist.PackageFormat`, no Azure. (1) `WorkflowConditionConverter` walks the Scriban tree `WorkflowConditionAt25.TryParse` gives, writes JSON Logic by part 10 § 2.2's table, and types the result with area 04's `ExpressionChecker` over a `ConversionScope`. (2) `WorkflowPackageMigration30To31` chains an older package to 30, then builds the 31 file set in passes (layout and inputs, nodes and flows, renames, templates, macros, checks and deliverables, derived flags), collecting notices; a construct it cannot decide is written in a form the 31 checker refuses, with one hand-edit notice. (3) `OfflineRun` (`V31/Harness/`) runs a compiled package through `PlanRunner` with `AgentTurn`, `RenderTurn` and `RuleTurn` and stubbed replies, and returns every sent prompt, render and deliverable with its hash; it is what the snapshot test and area 11's in-package tests run on. (4) `ByteEquivalence.Compare` holds a 30 capture document against an `OfflineRun` and writes the report, marking a difference expected only when a declared rule explains it exactly.

**Tech Stack:** C# / .NET 10, Scriban 7.2.5 (its parser, for reading 30), `System.Text.Json` (`JsonNode`), areas 02 to 09's services (`SourceCheck`, `CompiledPackage`, `ExpressionChecker`, `CorvusJsonLogicEvaluator`, `CanonicalJson`, `RunPlan`, `PlanRunner`, `AgentTurn`, `WorkflowStubOutputs`), xUnit in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`10-migration-and-tools.md`](../10-migration-and-tools.md) §§ 1, 2, 3.2, 4 and its *Refusals* (with § 3.1 for the harness and § 5's "migration vectors"), and [`02-types-and-inputs.md`](../02-types-and-inputs.md) § 11. Each task names the sections of parts 01 to 07 that define the 31 form it writes. Area issue #1507: **read every comment; they are requirements** (the `!x` decision, the two expected completions, the `skipped` expectation, the seven rulings, and the three rulings of 2026-11-22). Parent #1496, runbook [`BUILD-RUNBOOK.md`](../BUILD-RUNBOOK.md). What exists is in [`package-format-library.md`](../../package-format-library.md) (the "specVersion 31" sections) and `tests/Fixtures/ByteEquivalence/README.md` (the 30 side: captures, scenarios, condition vectors, and "What area 10 does"). **Read the spec sections a task names, the guide's sections for the services it calls, and the fixture README, before writing its tests.**

**Not this area's:** publishing a migrated version, moving pins, the publish record's *"migrated by the operator from …"*, telling owners (§ 1.5: they need the registry of area 11 and the user's deploy; this area gives the operator a folder-to-folder run and the report); running in-package tests at publish and the publish report (area 11, on this area's `OfflineRun`); the Editor's upgrade and the converter's page (Milestone 41); author documentation (§ 6, #1473); the satellites (§ 7); `OperatorAlertKinds.PublishFailed` (there is no publish path here; the PR flags its comment for area 11).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-10` on `m39-area-10`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; a pin under `external/` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions` and `WorkflowPackageStore.SupportedSpecVersions` do not change; `V31ContractsTests` and `SpecVersionSetTests` stay green.
- **The 5–30 path keeps its behaviour.** No 5–30 golden, capture, vector or expectation changes. The only edits outside `V31/` and the new files are the two moves this plan names (the stage to 24 into the library, Task 5; the 31 start's input building into the library, Task 13), each behind the tests that exist.
- **Area 01's fixtures are read, not rewritten:** nothing under `tests/Fixtures/ByteEquivalence/{vendored,fixture-packages,scenarios,captures,conditions/expressions.json,conditions/vectors.json,renderers}` changes. This area adds folders beside them.
- **The migration never writes an unknown member** (part 01), **never throws on package content** (a refusal or a notice instead; `ArgumentNullException` for a null argument only), and is **deterministic**: the same input gives the same bytes, file by file. JSON it writes is one object per file with every member present (`null` for none), `\n` line endings, in the member order of `SourceShapes`; a JSON Logic value is in canonical member order (RFC 8785).
- **A hand edit is written, never silently dropped.** What the rewrite cannot decide is written in a form `SourceCheck.Run` refuses, with exactly one notice `Hand edit needed in <file>, <member>: <reason>.` The invariant, tested on every package (Task 12): **no hand-edit notice ⇔ `CompiledPackage.Publish` refuses nothing.**
- **Sentences are used as written.** S10-001 to the last S10 row of `v31/sentences.json` are those rows with their `<…>` filled, held by `V31Contract.AssertSentence`. A notice with no row gets an area sentence `A10-NNN` (table below), added to `V31Contract` (a `MigrationAreaSentences` dictionary and an `A10-` branch, as A09 has) and to the conformance README's list, and listed in the PR. A converter refusal on type is the 31 checker's own sentence, unchanged.
- **No sentence, notice or report entry carries a value** from an input, a model or a profile: only ids, paths, members, package text (an expression as the author wrote it), counts, limits and hashes.
- **When in doubt, a difference is unexpected.** The comparison marks a difference `expected` only through a rule in `ExpectedDifferences` that names its ruling or spec section and whose test shows it explains the bytes. Everything else is listed `expected: false` and goes in the PR for the user. The report is never a gate, and no test asserts that a report has no differences: tests pin each report as a golden.
- **Invariant culture, ordinal comparison,** everywhere. Model JSON is read-only: `DeepClone` before placing a node in a new tree.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample).
- **Goldens come from code,** are read in full by the task that writes them, and are listed in the PR.
- **Commits:** small, by concern: `PackageFormat (#1507): …`, `Api (#1507): …`, `Tests (#1507): …`, `Docs (#1507): …`; in the content repo `Format spec (Consultologist-Blazor#1507): …`. Every message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task.
- **Tests:** `tests/V31Migration/` (namespace `Consultologist.Api.Tests.V31Migration`). Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Migration"`; a task that touches shared code also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`. Goldens are rewritten with `GOLDEN_UPDATE_V31=1`, as the other 31 goldens are.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner on #1507; the rest are where the spec is silent.

| # | Where | Choice |
|---|---|---|
| R1 | #1507, 2026-11-20; § 2.3 | `!x` on text converts to `blank(x)`. The difference from 30 (empty and whitespace-only text) is listed by the migration and marked expected by the comparison and the equivalence run. |
| R2 | #1507, 2026-11-20 | `example-prior-letters/no-prior-letters` and `example-structured-intake/all-optional-inputs-unset`, `Refused` at 30, complete at 31: expected. `example-conditional-documents/encounter-kind-unset` is still refused. |
| R3 | #1507, 2026-11-21 | A test expectation may be `{ "state": "skipped" }` alone. Part 07 § 8.3 and `test.schema.json` say so (Task 1); the snapshot writes it for a deliverable skipped by its `if`. |
| R4 | #1507, 2026-11-21, items 1 to 7 | 1 C (a skipped deliverable's `.content` reads `null`), 2 K, 3 C (a refused deliverable is withheld), 4 C1 (refused at publish), 5 C1 (numeric enum supported), 6 C (a required blank text refuses the start), 7 C (all refused is Completed). Each C is an `ExpectedDifferences` rule naming the ruling. |
| R5 | #1507, 2026-11-22, 1 | A macro body keeps 31's rule (one final line ending dropped); bodies whose text differs from 30's `TrimEnd()` are expected differences naming this ruling. |
| R6 | #1507, 2026-11-22, 2 | The migration **unrolls** a `for` over a macro folder into one placement per folder macro, **hoists** a body `if` around a placement into the placement's header `if`, and **rewrites** a bare read to `.content` / `.value`; each has a fixture from `example-macros`. Part 10 § 1.2 gains the three rows. |
| R7 | #1507, 2026-11-22, 3 | At 31 a `MacroChoices` entry keyed by a macro's id sets the one boolean input that macro's `if` reads, and is refused otherwise. Part 10 § 1.3 records it. |
| D1 | § 1.1 "the shape of the result matches the earlier rewriters'" | `Migration31Result` has the four members of `Migration26Result` (`ManifestJson`, `Files` without `manifest.json`, `Notices`, `Refusals`), and adds `HandEdits` (the S10-002 notices, a subset of `Notices`), `Report` (`MigrationNotes`: renames, `_macro` inputs, `blank` conversions, suggestions, `reproducible` claims that differed) and `Keys` (each 30 node id to its 31 rooted key, each deliverable's 30 key `"deliverable.<id>"` to its 31 key `deliverable.<31 id>`: keyed with its root, since a node and a deliverable may share an id, as `composer-sources`' `plan` does; Task 11). A refusal writes nothing. |
| D2 | § 1.1 "raised through the existing chain in the same run" | `WorkflowPackageMigrationChain.To30` runs the rungs the script and the Editor run today, in order; the script's `StageTo24` moves into the library unchanged (`WorkflowPackageMigrationTo24`) and the script calls it. A version below 5, or a manifest that does not read, is a refusal (the script's exit 2). A rung's own validation errors below 30 refuse the run, naming the rung. |
| D3 | § 1.4 | A hand edit leaves the 30 construct in the place where the 31 checker refuses it: an unconvertible condition stays a JSON string in its `if`/`while`/`until`/`expression`; a prompt's `profile.*` read and a body `if` too complex to lift stay in the body; a deliverable macro with a condition is written `{{ macro.<id>.content }}` in the deliverable's template; a node macro placed `before` or `after` is placed `{{ macro.<id> }}` at the end of the prompt with the macro's `instruction` written `[]`. |
| D4 | § 1.2 "deliverable macros"; part 05 § 10 | A deliverable macro with no `if` of its own, no placement `if`, not optional, becomes a snippet read in the template at its place (`snippets/<id>.md`, the macro's body; the macro file is kept too when a prompt still places it). Any condition makes it a hand edit (§ 1.4). A macro a 30 prompt reads only as `{{ macro.x.content }}` (fixed text) becomes a snippet the same way; one read both as `.content` text and as `.prompt` is a hand edit. |
| D5 | part 05 § 8.1, § 8.4; 30's `.prompt` | A migrated macro's `instruction` is 30's instruction, split into lines, then an empty line, then 30's mechanics sentence for that macro with its marker, so the rendered instruction has 30's bytes. A 30 instruction that is itself a template (`{{ "" }}`, a read of another macro) is a hand edit. |
| D6 | § 1.9 | Renames come from `Migration31Options.Renames` (hand-chosen: `{ "parts": "sections" }` for `example-nested-note`); an id with no entry takes its kind as a suffix (`parts_for`, `test_macro`; a macro that becomes a snippet is suffixed for what it becomes, `test_snippet`). Part 06's example reads `snippet.test`, which § 1.9 forbids (`test` is reserved); § 1.9 wins and the PR says so. |
| D7 | § 1.10 | `Migration31Options.WhenEmpty` maps a deliverable id to its value; the default is `"release"`. The fixtures' table gives `test/general` `"hold"` and `example-nested-note` `"fail"`. |
| D8 | part 06 § 6.1, § 6.4; part 05 § 5.3 | A migrated deliverable is `markdown_block`. A `for` source is read as the loop's `.content` (30's blocks); a scalar source as `.content`; sources are separated by one blank line; 30's `signature: true` becomes `{{ profile.signature }}` after a blank line. A plain-text source read into the Markdown deliverable is escaped (part 06 § 6.4): an expected difference, explained by the rule that unescaping the 31 text gives 30's bytes. Where 30's composer trims or drops a part and a template cannot (an empty part, a `while` with no pass, an item with no block), the template is written to give 30's bytes if the dialect allows it; what it cannot reproduce is an `ExpectedDifferences` rule only when a ruling or a spec section declares it, else it is reported unexpected. |
| D9 | § 3.2 "what it also lists"; area 00's schema | `migration-report.json` gains a required `notices` member, an array of strings: every notice that needs no edit (renames, `_macro` suffixes, `blank` conversions, suggestions, `reproducible` claims that differed, the worst-case note). The schema, its examples and the spec text change in the content repo (Task 1). |
| D10 | § 3.1; `tests/ByteEquivalence/CaptureRun` | The 30 side of a comparison is a capture document, as area 01 froze its shape. Today's engine runs offline only through the test project's `CaptureRun`, so the operator's run over a folder of packages is a test that does nothing without `BYTE_EQUIVALENCE_PACKAGES=<dir>` (as the conformance exports do): for each package folder it captures 30, migrates, runs 31 and writes the migrated folder and `migration-report.json` under `<dir>/_migrated/`. |
| D11 | § 3.2 `site`; the fixture README's proposal | A site is `<package>/<scenario>#prompts/<31 row key>`, `#renders/<31 row key>`, `#deliverables/<deliverable key>` or `#status`; a text only 30 has keeps its 30 key after `#prompts@30/`. For an account package the scenario is `migration_snapshot`. A text one side lacks has that side's hash as the hash of the empty text and `equal: false`. |
| D12 | § 3.1 "the stubs"; the fixture README | The 31 run replays the capture's `outputs`, mapped from 30's instance keys to 31's row keys through `Keys` and each loop's item numbering; a reply the capture lacks is generated by `WorkflowStubOutputs` seeded with 30's key. A node that offers macros has each offered marker appended to its stub in prompt order when the reply does not already hold it. |
| D13 | § 4; part 07 § 8.1 | The snapshot's inputs are generated from each input's format by `WorkflowStubOutputs` (seed `input.<id>`), every input given, a migrated macro choice left `null`; `profile` is `Dr A. Sample`'s fixed fictional values for exactly `needs.profile`; `run.date` is `{ "value": "2026-11-16", "timeZone": "America/Toronto" }`, other run facts fixed strings. `expect` holds every prompt and every deliverable by hash, and `ran` for nothing else. A package with hand edits gets no snapshot (`tests/index.json` lists none) and a notice; `WorkflowPackageMigration30To31.Snapshot` writes it once the edits are made. |
| D14 | § 3.2 the worst-case note; part 09 § 3.4 | The estimate uses whatever rule part 09 § 3.4 and `PublishNotes` give for a package's worst case; where they give a call count and no duration, one minute per round of `MaxAgentCalls` calls. The note is said only when the estimate passes `RunLimits.JobMinutes`. |
| D15 | part 02 § 1; `SourceDerived` | The flags are written from the derivation itself: `SourceDerived` gains an internal entry point that returns the derived `reproducible`, `modelDerived`, `runDependent` and `profileCarrying` of every header without holding the headers to them; the migration writes placeholders, derives, and rewrites. |
| D16 | § 1.2 the union row | Kept from 30 when 31 has the member: `onInvalid` (`flag` has no 31 form: `fail`, with a notice), `max`, `checkFirst`, `until`, labels, descriptions. Dropped with a notice when 31 has none. |

**Area sentences** (ids not in `sentences.json`). A reason fills S10-002's `<reason>`; a notice stands alone.

| Id | Kind | Sentence |
|---|---|---|
| A10-001 | reason | `the body's if at line <n> does more than choose text to show; write it as parts with their own if.` |
| A10-002 | reason | `macro '<id>' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.` |
| A10-003 | reason | `macro '<id>' was placed <before \| after \| before and after> the reply by the engine; say in its instruction where the model writes its marker.` |
| A10-004 | reason | `macro '<id>' is read both as fixed text and as an instruction; split it into a snippet and a macro.` |
| A10-005 | reason | `the template uses <construct> at line <n>, which the 31 dialect doesn't have; rewrite it.` |
| A10-006 | notice | `Renamed <kind> '<id>' to '<new id>': '<id>' is reserved at 31.` |
| A10-007 | notice | `Macro '<id>' is now offered by the input '<input id>'.` (with S10-006's form when `_macro` was needed) |
| A10-008 | notice | `<file>, <member>: '!<path>' became blank(<path>); it now also holds for empty and white-space-only text.` |
| A10-009 | notice | ~~`Input '<id>', field '<field>' holds free text; consider a text format.`~~ Unused: 30's inputs have no data `string` (every text becomes `markdown_block`); a package format's free-text field is A10-021 (Task 6). |
| A10-010 | notice | `Node '<id>' declared reproducible <true \| false> at 30; <true \| false> is written, as its reads give it.` (part 02 § 11's last bullet: only an agent node declares it at 30; Task 12) |
| A10-011 | notice | `No snapshot test was written: hand edits remain.` |
| A10-012 | refusal | `The package is at specVersion <n>; migration starts at 5.` |
| A10-013 | notice | `Node '<id>' kept a reply that did not fit its format (onInvalid: flag); 31 fails it instead.` |
| A10-014 | notice | `<package> could run for about <n> minutes in the worst case; this engine stops a job after <limit> minutes.` (part 10's own words; used only if `sentences.json` has no row for it) |
| A10-015 | refusal | `The package is already at specVersion <n>.` (the earlier rungs' sentence; Task 5) |
| A10-016 | refusal | `The manifest does not read as a package: it is not a JSON object with a whole-number specVersion.` (Task 5) |
| A10-017 | refusal | `At specVersion <n>: <refusal>` (an earlier rung's own refusal, unchanged, naming the rung as the Editor does, or a rung's validation error when 30's catalogue is given; D2, Task 5) |
| A10-018 | refusal | `At specVersion <n>: the rung could not read this package; fix the manifest, then migrate.` (a rung below 30 threw on the package's content; D2, Task 5) |
| A10-019 | refusal | `The file '<file>' does not read: <fault>; fix it, then migrate.` (`<fault>`: `it is not JSON`, `it names the member '<member>' twice`, `it nests deeper than 32 levels`, or the same of `its header`: a 30 file or text header that is not strict JSON; Task 6) |
| A10-020 | refusal | `The package does not read at specVersion 30; fix it, then migrate.` (the 30 reader, validator or walk threw on the package's content; Task 6) |
| A10-021 | notice | `Format '<id>', field '<field>' holds free text; consider a text format.` (a package format's `string` field, `<field>` its path of property names; 30's inputs have no data `string`, so A10-009 has no 30 source; Task 6) |
| A10-022 | reason | `a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.` (Task 6) |
| A10-023 | reason | `the input takes one of several types, which a 31 input cannot; choose one format.` (a 30 union input with no array arm, or whose array holds another type; Task 6) |
| A10-024 | reason | `the field takes one of several types, which 31 writes as an anyOf; choose one.` (a 30 type union of several types in a package format, `<member>` its JSON pointer; Task 6) |
| A10-025 | reason | `the format is nullable at its root, which a format a model writes cannot be at 31; make the root an object.` (Task 6) |
| A10-026 | notice | `<file>: a carriage return that didn't end a line became a line end.` (part 01 § 2.4; Task 6) |
| A10-027 | notice | `<file>, <member>: size(<list>) is null when <list> is null, where 30 counted 0.` (a converted `size` over a list that can be null, the Task 4 ruling; Task 7) |
| A10-028 | reason | `the case switches on <subject>, whose values 31 cannot list; switch on an enumerated string.` (a `case` whose subject is neither an enum nor a classifier; 30 refuses one, so it is held by its sentence only; Task 7) |
| A10-029 | reason | `<subject> can be null, which a 31 case sends to its else; add an else body.` (part 04 § 5.2: a `case` with no `else` over an optional input or a node not always valued; Task 7) |
| A10-030 | notice | `Node '<id>' declared <member> at 30, which 31 has no member for; it was dropped.` (D16: a classifier's `with`, a computed or rendered node's `onInvalid` or `failIfEmpty`; Task 7) |
| A10-031 | reason | `a package format's root is an object or a list at 31; make it one, or use a registry format.` (part 02 § 3.3: a package format whose root is `null` alone or a scalar; Task 7, fixing Task 6) |
| A10-032 | reason | `the case switches on classifier '<id>', whose answers are not typed at 31; give it a package format whose value is an enum of its answers.` (a `case` on a 30 classifier: `classification`'s `value` is a plain string at 31 and its `with` is dropped, so S04-044 refuses the subject; the branches are still written from 30's values; Task 7, fix round 1) |
| A10-033 | refusal | `The chosen rename of '<id>' to '<new id>' cannot be made: '<new id>' <is already taken \| is not a 31 id>; choose another.` (a `Migration31Options.Renames` entry the run cannot use; Task 8) |
| A10-034 | notice | `Renamed <kind> '<id>' to '<new id>': an id is at most 64 characters at 31.` (30 has no bound on an id's length; part 01 § 7; Task 8) |
| A10-035 | reason | `the body's if at line <n> decides what a <snippet \| macro \| rendered node> body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.` (a body `if` testing a value in a body that has no parts, part 05 §§ 2.5, 7; Task 9) |
| A10-036 | reason | `macro '<id>' is placed under different conditions in <files>; a macro has one if, so place it under one condition, or split it into one macro per condition.` (A10-002's sibling: the macro's placements, in one body or several, prompts and snippets, do not all carry the same hoisted condition; R6; Task 9, fix round 1) |
| A10-037 | reason | `the template writes <path> at line <n>, which is <an object \| a list> at 31; read a member, loop over it or pipe it into a renderer.` (a 30 bare read written whole whose `.value` is an object or a list, A07-007; Task 9) |
| A10-038 | reason | `the template can't be read at line <n>; close every tag, if and for.` (a body Scriban cannot parse, A07-005; Task 9) |
| A10-039 | reason | `the template reads <path> at line <n>, which a 31 <prompt \| rendered node \| macro body \| deliverable> doesn't read; rewrite it.` (part 05 § 3.1's "never" column: a prompt's `deliverable.*`, a `rendered` node's `profile.*` or `deliverable.*`, Task 9; a macro body's `deliverable.*`, `deliverable` when D4 made the macro a snippet a deliverable places, part 06 § 6.3, Task 11) |
| A10-040 | reason | `macro '<id>' is read as fixed text at line <n>, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.` (D4: a `.content` read of a macro that does not become a snippet, a marker at 31; Task 10) |
| A10-041 | reason | `the body places macro '<id>', which a 31 macro body cannot; place it in the prompt instead.` (part 05 § 8.3, S05-042; 30 refuses it first; Task 10) |
| A10-042 | reason | `the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.` (D5: `silent_reminder`'s `{{ "" }}`, `wrap_up`'s read of another macro; Task 10) |
| A10-043 | refusal | `MacroChoices names '<id>', which is not a macro of the package.` (R7, part 10 § 1.3: at the 31 start; A07-015's words no longer fit; quotes the caller's id, so the email sender is not told it; Task 10) |
| A10-044 | refusal | `MacroChoices names macro '<id>', whose if does not read exactly one boolean input; send the input in Inputs instead.` (R7, part 10 § 1.3: at the 31 start; Task 10) |
| A10-045 | reason | `macro '<id>' is read as fixed text at line <n>, but a macro folder's item is never fixed text at 31; place its text as a snippet.` (D4: an unrolled folder macro's `.content` read with no `.prompt` beside it; written `macro.<folder>.<id>_fixed_text`, which 31 refuses; Task 10, fix round 1) |
| A10-046 | reason | `check '<id>' ran inside the <for \| while \| if \| case> node '<flow>' at 30, but a 31 check is never inside a node flow; take it out of this list and gate checks/<id>.md with an if, or make it a check loop over the loop's .items.` (part 06 §§ 1, 3: the flow's list keeps the id, refused S01-009; Task 11, fix round 1) |
| A10-047 | reason | `'<id>' is placed on this deliverable but is not a single macro of the package; place a snippet here instead.` (D4; 30 refuses such a placement first; Task 11, fix round 1) |
| A10-048 | notice | `Macro '<id>' is read on deliverables as the snippet '<new id>': a snippet '<id>' already exists.` (D4: a snippet copy whose id a 30 snippet has; Task 11, fix round 1) |
| A10-049 | notice | `<file>: <flags> <is \| are> written false without the bodies that need a hand edit; check <it \| them> once the edits are made.` (D15: a header made of a body the dialect refused, whose `false` flags are not known; Task 12, fix round 1) |
| A10-050 | notice | `<file>, <member>: '<comparison>' became <rule>; it now <also holds \| no longer holds> when <path> is null.` (part 05 § 7: `trim(x) == ""` is written `blank(x)`, `!=` and its negation `not blank(x)`, the conversion row the controller ruled in Task 12's fix round 1; said when `x` can be null at 31 where 30 compared a null text as not empty: a node that did not run, a field of an object input; a text input read whole is not said, as 30 read it `""` when not given. The difference is unruled: listed `unruled` in `equivalence.json` and in the PR for the owner, never an expected difference. Listed in `Report.BlankConversions` beside A10-008; Task 12, fix rounds 1 and 2) |
| A10-051 | reason | `macro '<id>' is placed at line <n>, but a snippet folder's item read in a loop places no macro at 31; place it in the prompt that loops over the folder.` (part 05 § 6.2, A07-012: a placement in a folder item's body, hoisted off its body `if` by R6 or not; Task 12, fix round 1) |
| A10-052 | refusal | `<Node '<id>' gave a reply \| Input '<id>' was given a value> that isn't <format>: markdown_inline text sits inside a line and holds no line ending.` (R10, D21, part 02 § 5: a value that fits its format but for a `\n` or `\r` in a `markdown_inline` text, at any depth; A06-005's and A06-012's heads with the reason; `RunSentences.ReplyBreaksALine`, `InputBreaksALine`; Task 20) |
| A10-053 | reason | `the <if \| case> '<id>' that a deliverable composes returns Markdown from one branch and plain text from another, which the deliverable would escape as one string; give its branches one text format, then write that format here and on each flow that returns this one.` (R9, D20, D23, part 06 § 6.4: a composed `if` or `case` that a deliverable reaches through no `for` (through a `while`, or any chain with no `for`), whose branches mix 30 prose with a `let` of text, a `rendered` node or `agent_plaintext`; written `markdown_block`, refused by S02-015 against the union; `MigrationSentences.ComposedBranchesMix`; Task 21 fix round 2. Under a `for` the deliverable reads the loop's `.content`, `markdown_block` and never escaped, so the union is written as derived with no hand edit (`NodeTree.ComposedDirect`; Task 24 fix round 1)) |
| A10-054 | reason | `no document password` (R8, D17, part 06 § 8: a delivery row's `reason` when a `pdf` destination with no document password went as the link-only email, as 30's reply did; the row `sent`, its attempt `sent link-only`; `DeliveryPlan.NoDocumentPassword`; Task 22) |
| A10-055 | reason | `attachments over the budget` (R8, D17, part 06 § 8: the same, for a `pdf` send whose attachments passed the 5–30 reply's 2 MiB budget, the set dropped whole; `DeliveryPlan.OverTheBudget`; Task 22) |
| A10-056 | reason | `every deliverable blocked` (D19, the owner's words on #1507, 2026-11-22T21:38Z: the row's `reason` for the link-only email of a job that released nothing, with a deliverable blocked or held; `DeliveryPlan.EveryDeliverableBlocked`; Task 22) |
| A10-057 | notice | `No document could be sent: <labels> <was \| were> not released.` (D19: the line of that email after the completed reply's opening; labels are package text; `MailDeliveryChannel.NoDocumentSent`; Task 22) |
| A10-058 | notice | `Outstanding migration hand edits: not counted; <the type registry \| the accounts' active packages> could not be read.` (R11, D22, part 09 § 3.9: the weekly summary's hand-edits line when a pin, a stored package or the bundled 31 type registry could not be read: an error line, never a zero; `MigrationFigures.NotRead`; Task 23) |

Before using an A10 sentence, search `sentences.json` for a row that says the rule. Add an A10 sentence only when no row does; number on from A10-014 and add it to this table in the task's commit.

## Files

```
src/Consultologist.PackageFormat/
  WorkflowConditionConverter.cs            the surface of § 2.1: FromScriban, ConversionResult, ConversionScope      Tasks 2, 3
  V31/Migration/ConditionTree.cs           Scriban tree → JSON Logic, row by row                                      Task 2
  V31/Migration/ConditionTyping.cs         the scope for ExpressionChecker, !x on text, refusals, Reads              Task 3
  WorkflowPackageMigrationTo24.cs          the script's StageTo24, moved                                              Task 5
  WorkflowPackageMigrationChain.cs         To30                                                                       Task 5
  WorkflowPackageMigration30To31.cs        Migrate, Snapshot, Migration31Result, Migration31Options, MigrationNotes   Tasks 5–12, 14
  V31/Migration/MigrationSentences.cs      S10 rows and A10 sentences                                                 Tasks 3, 5–12
  V31/Migration/Migrated.cs                the working set: the 30 model, the 31 files, notices, the key map          Task 6
  V31/Migration/Migrate.Layout.cs          manifest, folders, indexes, formats                                        Task 6
  V31/Migration/Migrate.Inputs.cs          part 02 § 11's input rows                                                  Task 6
  V31/Migration/Migrate.Nodes.cs, Migrate.Flows.cs       headers, kinds, formats, flows, conditions                   Task 7
  V31/Migration/Migrate.Renames.cs         reserved ids, collisions                                                   Task 8
  V31/Migration/Migrate.Templates.cs, TemplateRewriter.cs    bodies: reads, pipes, whitespace, parts, unroll, hoist   Task 9
  V31/Migration/Migrate.Macros.cs          macro files, choices as inputs, placements                                 Task 10
  V31/Migration/Migrate.Checks.cs, Migrate.Deliverables.cs                                                            Task 11
  V31/Migration/Migrate.Derived.cs         the flags, flow formats                                                    Task 12
  V31/SourceDerived*.cs                    the internal derivation entry point (D15)                                  Task 12
  V31/Run/RunInputsBuilder.cs              raw inputs → RunInputs, moved from the Api                                 Task 13
  V31/Harness/OfflineRun.cs, OfflineHost.cs, OfflineStubs.cs                                                          Task 13
  V31/Harness/SnapshotTest.cs              the test file of § 4                                                       Task 14
  V31/Harness/ByteEquivalence.cs, ExpectedDifferences.cs, MigrationReport.cs, KeyMap.cs                               Task 15
src/Consultologist.Api/V31/V31StartChecks.cs    Inputs delegates (Task 13); Choices by the macro's if (Task 10)
scripts/migrate-workflow-package.cs        --to 31, the chain, exits 0 / 1 / 2, --snapshot                            Task 17
tests/V31Migration/     *Tests.cs, MigrationFixture.cs, ExampleChoices.cs, Golden/                                    all
tests/Fixtures/ByteEquivalence/
  conditions/converted.json                each expression's JSON Logic, type, reads, notices                         Tasks 2, 3
  conditions/equivalence.json              31's answer beside 30's, case by case, with the expected exceptions        Task 4
  migrated/<package>/**                    the migrated source of each of the 16 packages, and notices.json           Tasks 6–12
  hand-edits/<package>/**                  the files an operator edits, for the packages that need it                 Task 16
  reports/<package>.json                   migration-report.json per package, over its scenarios                      Tasks 15, 16
docs/customizable-workflow/package-format-v31/10-…, 07-…, 02-…      the rulings                                       Task 1
docs/customizable-workflow/package-format-library.md   + "specVersion 31: migration (preview)"                       Task 19
consultologist-package-format (m39-build)   package-format-v31.md ch. 10, v31/schemas/migration-report, test,
                                            their examples, v31/conformance/migration-vectors/**, README, check-v31   Tasks 1, 18
```

## Review Focus

1. **A 30 expression the table half-covers.** `!(a == b)`, `!input.flag` on an optional boolean (absent at 30 reads null), `input.x == ""` on a text, a comparison of an enum with a value it does not list, `a - -1`, `(x | array.size) > 0` on an absent array, a string with a quote or `\n`, nested parentheses, `x.size` on text. Expected: converted exactly or refused with a notice, never converted to something that answers differently without being listed. (Task 3 `AnExpressionNearTheTable_ConvertsOrRefuses_NeverThrows`; Task 4's run covers the answers.)
2. **A package built to collide.** A node named `parts_for` beside a loop `parts`; a macro `letrozole` beside an input `letrozole` and an input `letrozole_macro`; an id of 64 characters that needs a suffix; a rename whose old id appears inside a longer id (`parts` in `counterparts`), in a string literal, in prose text of a prompt. Expected: every reference rewritten and nothing else touched; each collision reported with S10-006. (Task 8 `ARename_TouchesReferencesOnly`.)
3. **A manifest a hand broke.** `inputs` an object, a node with both `for` and `while`, a `ret` naming no node, a prompt file missing, a format file that is not JSON, a macro header with no `}`, `\r\n` everywhere, a BOM, a 2 MiB prompt. Expected: a refusal sentence (nothing written) or a migrated package with notices; never an exception, never a half-written result. (Task 6 `AnyBrokenPackage_IsRefusedOrMigrated_NeverThrows`.)
4. **Text that looks like syntax.** A prompt with `{{ "{{" }}`, a raw block, `input.x` inside prose or a code fence outside any tag, a comment tag, a snippet body holding `node.parts`. Expected: only code inside tags is rewritten; text outside tags is byte for byte the same. (Task 9 `TextOutsideTags_IsNeverRewritten`.)
5. **A run the stubs cannot feed.** A capture whose pinned output is for a node the migration renamed or moved under a loop; a format with a labelled `oneOf`, a `date`, a `pattern`; a node that offers two macros in a fixed order; a `while` whose check never stops. Expected: the offline run finishes or stops with a sentence under `RunLimits`, a stub fits its format, and a key that cannot be mapped is reported, not dropped. (Task 13 `AnOfflineRun_AlwaysEnds`; Task 15 `AKeyThatCannotBeMapped_IsReported`.)

---

### Task 1: The rulings, written into the spec and the contracts

**Spec:** #1507's comments of 2026-11-21 and 2026-11-22; part 10 §§ 1.2, 1.3, 3.2; part 07 § 8.3; R3 to R7, D9.

**Files:** `docs/…/10-migration-and-tools.md`, `07-compiled-documents.md`, `refusal-sentences.md` only if a listed sentence's words change (none should); `~/Projects/consultologist-package-format`: `package-format-v31.md` (chapter 10 and the test grammar), `v31/schemas/test.schema.json`, `v31/schemas/migration-report.schema.json`, their examples under `v31/conformance/schemas/`, `v31/conformance/index.json` (through that repo's own generator or check, as area 00 left it), `scripts/check-v31.py` if it names the members; this repo's `external/consultologist-package-format` pin; `tests/V31ContractsTests.cs`.

- [x] **Step 1: Write the failing tests** in `V31ContractsTests`: `ATestExpectation_MayBeASkippedStateAlone` (`{ "state": "skipped" }` validates against `test.schema.json`; `{ "state": "released" }` alone does not); `TheMigrationReport_ListsItsNotices` (the schema requires `notices`, an array of strings; a report without it is invalid).
- [x] **Step 2: Run them; they fail.**
- [x] **Step 3: Change the content repo** on `m39-build`: the two schemas, a valid and an invalid example for each change, the README's lines, chapter 10 and the test grammar in `package-format-v31.md`. Run `python3 scripts/check-v31.py`. Commit, push, move the pin.
- [x] **Step 4: Edit part 10.** § 1.2: three rows after "simple `{{ if }}…{{ end }}` blocks" (a `for` over a macro folder → one placement per folder macro; a body `if` around a placement → the placement's header `if`; a bare read → `.content` / `.value`), each citing the ruling. § 1.3: a paragraph, "A `MacroChoices` entry keyed by a macro's id sets the one boolean input that macro's `if` reads, and is refused otherwise", with `letrozole` / `letrozole_macro`. § 3.2: an "Expected differences" list: the `blank` case, the two completions (R2), rulings 1, 3, 6, 7 of R4, macro bodies' trailing whitespace (R5), plain text escaped into a Markdown deliverable (part 06 § 6.4); and `notices` in the report's shape. Part 07 § 8.3: the `deliverables` row allows `{ "state": "skipped" }` alone.
- [x] **Step 5: Run** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Contracts"`: green. **Commit** (`Docs (#1507): …`, `Tests (#1507): …`) and push.

### Task 2: The converter's tree — every row of § 2.2

**Spec:** part 10 §§ 2.1, 2.2; part 03 §§ 2, 3 (the operation set and written form), § 8 (the printer).

**Files:** Create `src/Consultologist.PackageFormat/WorkflowConditionConverter.cs`, `V31/Migration/ConditionTree.cs`; `tests/V31Migration/ConditionConverterTests.cs`, `tests/Fixtures/ByteEquivalence/conditions/converted.json` (golden).

**Interfaces:**
- Produces, as § 2.1 gives them:
  ```csharp
  public static class WorkflowConditionConverter
  { public static ConversionResult FromScriban(string expression, ConversionScope scope); }
  public sealed record ConversionResult(JsonNode? Logic, string? ResultFormat, IReadOnlyList<string> Reads, IReadOnlyList<string> Notices);
  public sealed record ConversionScope(
      IReadOnlyDictionary<string, JsonNode> Inputs,          // input id → its 31 format (a name or a schema)
      IReadOnlyDictionary<string, ConversionRead> Nodes,     // 30 node id → where 31 reads it
      JsonNode? Item,                                        // the loop element's format; null outside a loop
      bool ItemIsFolderItem,                                 // item.id and item.content of a snippet folder's item
      bool InWhile,                                          // pass.number is readable
      V31.FormatSet Formats);
  public sealed record ConversionRead(string Path, JsonNode Format);   // "node.sections.outline", "check.coverage"
  ```
  `ConversionResult.BlankConversions` (`IReadOnlyList<string>`, the paths whose `!x` became `blank`) is added as an init-only member in Task 3.
- Internal: `ConditionTree.Write(ScriptExpression expression, ConversionScope scope, List<string> notices) : JsonNode?`.

- [x] **Step 1: Write the failing tests.** `EveryRowOf2_2_ConvertsToItsLogic`: a theory over the `row.*` ids of `conditions/expressions.json` (all 41), each built into a `ConversionScope` from its `reads` (`MigrationFixture.ScopeOf(reads)`), asserting `Logic` is not null except where Task 3 decides (`row.not-text.*` are asserted there). Pinned by hand, as canonical JSON, at least: `row.eq` → `{"===":[{"var":"input.x.value"},…]}` in the entry's own operands; `row.and3` → one `and` of three; `row.size.pipe` and `row.size.member` → `{"size":[…]}`; `row.downcase` → `lower`; `row.strip.input` → `trim`; `row.contains` → `{"in":["<p>",<x>]}`; `row.date-parse.lt` → the literal string beside `<`; `row.let-object` → `object`; `row.let-array` → a JSON array inside it; `row.input.x` on a text input → `{"var":"input.x.content"}`, on any other → `.value`; a classifier read `node.x.value` → `{"var":"<Path>.value.value"}` (§ 2.2's second bullet). `TheOutput_IsCanonical` (`CanonicalJson.Write(Logic)` round-trips byte for byte; converting twice gives the same bytes). `ThePrinter_ReadsWhatTheConverterWrites` (`JsonLogicPrinter.TryParse(JsonLogicPrinter.Print(Logic))` gives the same tree, for every converted expression). `TheGolden_IsWhatTheConverterGives`: `converted.json` holds, per expression of `expressions.json` in order, `{ id, logic, resultFormat, reads, notices }`.
- [x] **Step 2: Run; they fail** (no converter).
- [x] **Step 3: Implement.** Parse with `WorkflowConditionAt25.TryParse`; walk the Scriban nodes (binary, unary, nested, member chains flattened as `WorkflowTemplateTokens.Flatten` does, pipes, function calls, literals, object and array initialisers). `==`/`!=` always write `===`/`!==`. `&&`/`||` chains flatten. A node's read goes through `scope.Nodes[id].Path`; an id not in scope is a notice. Anything outside the table returns null with one notice (worded in Task 3; a placeholder constant is fine here only if Task 3's test replaces it, so write S10-003 now through `MigrationSentences.NoJsonLogicForm(expression, construct)`).
- [x] **Step 4: Run; green. Read `converted.json` in full.**
- [x] **Step 5: Commit** (`PackageFormat (#1507): the condition converter writes § 2.2's table`, `Tests (#1507): …`) and push.

### Task 3: The converter's types, `!x` on text, and what it refuses

**Spec:** part 10 § 2.3 and its *Refusals*; part 03 §§ 5, 6 (types, null rules), its *Refusals* (S03-002 to S03-009, S03-018, S03-025).

**Files:** `V31/Migration/ConditionTyping.cs`, `V31/Migration/MigrationSentences.cs`, `WorkflowConditionConverter.cs`; `tests/V31Source/V31Contract.cs` (the A10 dictionary and branch); `ConditionConverterTests.cs`; `converted.json` regenerated.

**Interfaces:**
- Consumes: `ExpressionChecker.Check(JsonNode? rule, ExpressionSite site, IExpressionScope scope, FormatSet formats) : ExpressionCheck` (`Refusals`, `Type`, `Reads`).
- Produces: `ConversionResult.ResultFormat` (the checker's type, printed as `TypeStrings` prints a format: `boolean` for a condition), `Reads` (the checker's, in order), `BlankConversions`; `FromScriban(string expression, ConversionScope scope, ConversionKind kind = ConversionKind.Condition)` with `enum ConversionKind { Condition, Value }` (`Value` for a `let` and a `case` subject: any type is allowed).

- [x] **Step 1: Write the failing tests.** `NotOnText_BecomesBlank` (`row.not-text.node` and `row.not-text.input` → `{"blank":[{"var":"…content"}]}`, each path in `BlankConversions`; `row.not-boolean` stays `{"!":[…]}`). `EveryRefusalOf2_3_GivesNoLogicAndASentence`: the eight `refuse.*` ids give `Logic == null` and one notice each; `refuse.text-less-than`'s notice is S03-008 as the checker says it; `refuse.profile` names `profile.name`; the builtins, the assignment, the template and the two statements are S10-003 with `<construct>` the builtin's name, `an assignment`, `a template` and `more than one statement`. `ACondition_YieldsABoolean` (a `Condition` whose type is not boolean is refused with S03-025; the same text as a `Value` converts). `TheReads_AreWhatTheLogicReads`. `AnExpressionNearTheTable_ConvertsOrRefuses_NeverThrows` (Review Focus 1's list, each asserted to its exact logic or to null with a notice). `EveryPackageExpression_Converts`: the 44 package expressions of `expressions.json` all give logic (if one cannot, the test names it and the task records why in the plan before going on). Every notice passes `V31Contract.AssertSentence`.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** Build an `IExpressionScope` over `ConversionScope` (inputs with `value`/`content`/`title` heads typed from their formats; nodes at their `Path`; `item`, `pass.number`, `run.*` as area 04's `LogicFixture` shows a scope is made). Decide `!x` by the operand's type before checking: a text format → `blank`. Run the checker on the written logic with a site of kind `NodeIf` (or `Expression` for a `Value`); a refusal becomes the notice, sentence unchanged, and `Logic` null.
- [x] **Step 4: Run; green.** Regenerate and read `converted.json`.
- [x] **Step 5: Commit and push.**

  *Recorded in the build:* of the 44 package expressions, two give no logic, each with its notices: `composer-macros.section_draft.section_close.if` (`item.value.title != "Plan"`: a 30 text field is `markdown_block` at 31, part 02 § 11, and text compared with a literal is S03-002, part 03 § 7) and `composer-sources.topic_tally.let` (`string.append` and `string.size`, which § 2.2's table has no row for: S10-003 twice). Both are hand edits (§ 1.4). Eight `row.*` fixtures are refused once typed (`row.read.node-content`, `row.input.x`, `row.input.x-y`, `row.downcase`, `row.strip.input`, `row.strip.node`: S03-002; `row.let-array`: A04-003, a list mixing `markdown_block` and `plaintext`; `row.let-object`: A04-009, its optional `input.age` is `number | null` where `triage_summary` declares `number`); Task 2's row tests assert the written tree, before typing. The surface gains `FromScriban(…, JsonNode? declared = null)` (a `let`'s format at 31, so an `object` is typed against it), the init-only `ConversionResult.NoticeIds` (each notice's sentence id) and `ConversionResult.Blanks` (each `!x` that became `blank`: `x` as written at 30, as printed at 31, and its reads; `BlankConversions` is their 31 read paths), and the init-only `ConversionScope.RequiredInputs` and `ConversionRead.AlwaysValued` (an input not named, and a node not always valued, read as nullable, as 31's scope reads them). *2026-11-22 (Task 12, fix round 1):* `row.strip.input` and `row.strip.node` now convert: a trimmed text compared with `""` is `blank(x)` (`!=`: `not blank(x)`), as part 05 § 7's migrated `test/general` writes it (the controller's ruling); `ConversionResult.TrimBlanks` lists each such conversion whose `x` can be null at 31 (A10-050), so six `row.*` fixtures are refused once typed. *2026-11-22 (Task 12, fix round 2):* only a text input read whole is left out of `TrimBlanks`; a field of an object input, null at 30 when it or its object is absent, is listed too. `row.strip.input-field` (`(input.patient.name | string.strip) == ""`) is added to `expressions.json`, with its vectors, so § 2.2 has 42 `row.*` fixtures.

### Task 4: Semantic equivalence with 30's evaluator

**Spec:** part 10 § 2.4; the fixture README's `conditions/` section; R1.

**Files:** `tests/V31Migration/ConditionEquivalenceTests.cs`, `tests/V31Migration/MigrationFixture.cs` (the data builder), `tests/Fixtures/ByteEquivalence/conditions/equivalence.json` (golden).

**Interfaces:**
- Consumes: `vectors.json` (`id`, `refused`, `cases[] { scope, result, error }`), `converted.json`, `new CorvusJsonLogicEvaluator().Compile(rule).Evaluate(data)`.
- Produces: `MigrationFixture.DataOf(JsonObject scope, JsonObject reads) : JsonObject`, the 31 data for one case: an input's supplied value passed through 31's start rules (absent, `null` or blank text → not given, so `input.x.value` and `.content` read `null`; otherwise `{ value, content }`), a node at its 31 path (`"<absent>"` → skipped: `value` and `content` `null`), `item`, `pass.number`, `run.date`.

- [x] **Step 1: Write the failing tests.** `EveryConvertedExpression_AnswersAs30Does`: for each expression with logic, each case with `error == null`: 31's value equals `result` (a boolean for a condition; canonical JSON for a `case` or `let`), **or** the case is in `equivalence.json` under that expression with a named reason. The only reasons allowed are `blank` (the expression has a `BlankConversions` path whose value in this case is `""` or white space, or an input not given) and reasons this task adds to `ExpectedDifferences.ConditionReasons` with a ruling or a spec section beside each. `ACaseWith30sError_IsListedNotCompared` (cases with an `error` appear in `equivalence.json` with 31's answer beside 30's sentence). `TheGolden_IsWhatTheRunGives`.
- [x] **Step 2: Run; it fails** (no golden; and whatever disagrees).
- [x] **Step 3: Make it true.** A disagreement is a converter fault until shown otherwise: fix the converter (Tasks 2 and 3's tests stay green). A disagreement that is 31's own null rule (part 03 § 6: for example an absent number compared with `<`) with no ruling is **not** silently accepted: it is listed in `equivalence.json` with reason `unruled`, counted by the test (`TheUnruledDifferences_AreTheseOnes`, pinned by id), and reported in the PR under *Decisions made during build* for the owner. If an `unruled` difference touches a condition of a public package (not only a `row.*` fixture), stop and record a block on #1507 as the runbook says.
- [x] **Step 4: Run; green. Read `equivalence.json` in full.**
- [x] **Step 5: Commit and push.**
  *Recorded in the build:* every case of every converted expression agrees with 30 but six. Five are `blank` (R1): `row.not-text.node` on `""` and `" "`, and `row.not-text.input` on an input absent, `""` and `" "` (31's start leaves all three not given). One is `unruled`, pinned in `TheUnruledDifferences_AreTheseOnes`: `row.size.member#0`, `input.problems.size == 0` with the optional list absent, `true` at 30 (an absent list reads `[]`) and `false` at 31 (not given reads `null`, `size` of `null` is `null`, part 03 §§ 6.1, 6.2). It touches no public package: `example-structured-intake.notes_index.if`, the package expression of that shape, agrees on every case. The one case 30 answered with an error (`row.read.node-value#0`, a member of a skipped node) is listed with 31's `false` beside it. The ten expressions with no logic (the two package expressions and eight `row.*` fixtures above) are listed as not converted with their notice ids and pinned in `TheUnconvertedExpressions_AreTheseOnes`. `ExpectedDifferences` (`V31/Harness/`) is created here with `ConditionReasons` only (`blank`, R1); Task 15 extends it. *2026-11-22 (Task 12, fix rounds 1 and 2):* with `row.strip.*` converted, eight expressions give no logic. `row.strip.input` agrees on every case. `row.strip.node#0` (the node absent) and `row.strip.input-field#0` and `#2` (the object absent, the field left out) are `false` at 30 and `true` at 31 (`blank(null)`): no ruling declares them, so they are `unruled`, pinned in `TheUnruledDifferences_AreTheseOnes` beside `row.size.member#0`, and go to the owner in the PR. No public package's condition has that shape (their `strip == ""` reads are a text input read whole and a folder item's text, which agree). `ExpectedDifferences.ConditionReasons` keeps `blank` only.

### Task 5: The result, the options and the chain to 30

**Spec:** part 10 §§ 1.1, 1.4 (the exits); D1, D2.

**Files:** Create `WorkflowPackageMigrationTo24.cs`, `WorkflowPackageMigrationChain.cs`, `WorkflowPackageMigration30To31.cs`; modify `scripts/migrate-workflow-package.cs` (call the moved stage; nothing else yet); `tests/V31Migration/MigrationChainTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public sealed record Migration31Options(
      V31.TypeRegistry Registry, V31.Toolchain Toolchain,
      WorkflowFormatCatalog Formats30,                         // 30's catalogue: each format's shape, a classifier's values
      Func<JsonNode, string?>? ResolveContract = null,         // the 24 → 25 rung's
      IReadOnlyDictionary<string, string>? Renames = null,     // D6
      IReadOnlyDictionary<string, string>? WhenEmpty = null);  // D7
  public sealed record Migration31Result(string ManifestJson, Dictionary<string, string> Files,
      IReadOnlyList<string> Notices, IReadOnlyList<string> Refusals)
  { public bool Succeeded => Refusals.Count == 0;
    public IReadOnlyList<string> HandEdits { get; init; } = [];
    public MigrationNotes Report { get; init; } = MigrationNotes.None;
    public IReadOnlyDictionary<string, string> Keys { get; init; } = new Dictionary<string, string>(); }
  public sealed record MigrationNotes(IReadOnlyList<string> Renames, IReadOnlyList<string> MacroInputs,
      IReadOnlyList<string> BlankConversions, IReadOnlyList<string> Suggestions, IReadOnlyList<string> Reproducible);
  public static class WorkflowPackageMigration30To31
  { public static Migration31Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files, Migration31Options options); }
  public static class WorkflowPackageMigrationChain
  { public static Migration26Result To30(string manifestJson, IReadOnlyDictionary<string, string> files, Func<JsonNode, string?>? resolveContract); }
  ```
- `tests/V31Migration/MigrationFixture.cs`: `Options(string package)` (the published registry as `V31Contract.PublishedTypes()`, `CompiledGoldenTests.Fixed`, `TestFormats.Catalog`, `ExampleChoices` for the package), `Migrate(string package)` over `ByteEquivalenceFixtures.ReadPackage`.

- [x] **Step 1: Write the failing tests.** `APackageBelow5_IsRefused` (A10-012; `Files` and `ManifestJson` are the input's). `AManifestThatDoesNotRead_IsRefused`. `A31Package_IsRefused` (*"The package is already at specVersion 31."*). `AnOlderPackage_IsRaisedThroughEveryRung` (a 24 fixture and a 27 fixture from the existing migration tests reach 30 by `To30`, equal to running the rungs by hand as `tests/WorkflowPackageMigration27To28Tests.cs` does). `TheStageTo24_IsTheScripts` (the moved code gives what the script's gave on the script's existing fixture, if one exists; otherwise on `EditorFixtures`' pre-23 package). `Migrate` on a 30 package returns `Succeeded` with a manifest whose `specVersion` is 31 (nothing else asserted yet).
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement** the move, the chain and the entry point's gates.
- [x] **Step 4: Run** the area's tests and `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`: green.
- [x] **Step 5: Commit and push.**

### Task 6: The layout, the manifest, inputs and formats

**Spec:** part 10 § 1.2 rows 1 to 5, the format rows, the input rows, § 1.8; part 02 §§ 3.3, 8, 11; part 01 §§ 2, 3, 5 (every member written, the nine folders, line endings).

**Files:** `V31/Migration/Migrated.cs`, `Migrate.Layout.cs`, `Migrate.Inputs.cs`; `tests/V31Migration/MigrateLayoutTests.cs`, `MigrateInputsTests.cs`, `tests/V31Migration/ExampleChoices.cs`.

**Interfaces:**
- Produces (internal): `Migrated` — `Manifest30` (`WorkflowPackageManifest`), `Root30` (`JsonObject`), `Files30`, `Resolved30` (`WorkflowResolvedTexts`), `Walk30` (`WorkflowDerivedEdges.Walk`: every template and every condition site), `Files` (path → text, the 31 set), `Notice(string)`, `HandEdit(string file, string member, string reason)`, `Keys`, `Scope(site) : ConversionScope` (filled as later tasks add nodes). `Migrated.Json(JsonObject)` writes one file's JSON; `Migrated.Md(JsonObject header, string body)` writes `---\n<one-line header>\n---\n<body>`.

- [x] **Step 1: Write the failing tests.** `AllNineFolders_AreWritten_EachWithItsIndex` (a package with no macros still has `macros/index.json` with `"items": []`; same for the other eight). `TheManifest_KeepsIdentityAndInputsOnly` (`name`, `version`, `specVersion: 31`, `derivedFrom`, `title`, `description` as lines, `tags`, `inputs`; no `templating`, `nodes`, `macros`, `snippets`, `formats`, `deliverables`). `EveryMember_IsWritten` (each written file passes `SourceMembers.Check` for its shape with no S01-021 and no S01-023). `CarriageReturns_AreNormalised`. Inputs, one test per row of part 02 § 11: `text` → `markdown_block`; no `type` → `markdown_block`; `enum` + `values` → `{ "type": "string", "enum": [...] }` with no titles; `date` → `"date"`; `boolean`, `number`; `object` + `fields` → `properties` with every field in `required`, an optional field nullable, `title` from the field's label; `array` + `items`; a union `[x, array]` → the array of `x`; `expectedContent` → `channel`; `description: null`; `required` kept; the 30 array's order kept as the object's member order. `AFreeTextStringField_IsSuggested_NotChanged` (A10-009 in `Report.Suggestions`). Formats: `APackageFormat_BecomesItsSchemaAlone` (`formats/<id>.json` is the schema with `$schema` `urn:consultologist:spec:31:dialect`, no `producer`, no `outputMacros`, the description kept as the schema's `description`); `EveryProperty_IsRequired_OptionalOnesNullable` (nested objects and array items too). `AnyBrokenPackage_IsRefusedOrMigrated_NeverThrows` (Review Focus 3). `SnippetsAndFolders_MoveWithTheir31Headers` (a single snippet: `label`, `description: null`, `if: null` for now, `format` `markdown_block`; a folder: `index.json` with `description`, `items`, `format`, each item's header a value of the folder's format, 30's item `label` carried as the member `title` of a package format `<folder>_item` unless the folder's items already share a declared shape; read part 05 § 6 and part 01's `test/general` example, which gives `standard_section`).
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** Read 30 through `WorkflowPackageManifestJson.Read` and `WorkflowTextHeaders.Resolve`; a read that fails is a refusal.
- [x] **Step 4: Run; green.**
- [x] **Step 5: Commit and push.**

### Task 7: Nodes, flows and their conditions

**Spec:** part 10 § 1.2 (node headers, `format` with `producer`, `prose` and `agent_*`, `when` → `if`, the `case` row, string conditions, `let`, `labelField`, `failIfEmpty`); part 02 §§ 1, 11; part 04 §§ 2 (with 2.7 and 2.8), 3, 4, 5 (with 5.4); part 05 § 1.2; part 01 § 6 (keys, `ret` as the child's full name).

**Files:** `Migrate.Nodes.cs`, `Migrate.Flows.cs`; `tests/V31Migration/MigrateNodesTests.cs`, `MigrateFlowsTests.cs`.

**Interfaces:**
- Consumes: `Migrated`, `WorkflowConditionConverter.FromScriban`.
- Produces: every node written under `nodes/` at its nested path; `Migrated.Keys[<30 id>] = "node.<path>"`; `Migrated.Scope(site)` now knows every node's `ConversionRead` (a `terms_subset` node's path is `check.<id>`, Task 11) and each loop's item format.

- [x] **Step 1: Write the failing tests.** Kinds: a 30 node with `format` of an attested or custom producer → `kind: "agent"`, `producer: "generated"`; `format: "rendered"` → `kind: "rendered"`, `producer: "computed"`, a text format; `let` + `type` → `kind: "computed"`, `format` from `type` (`text` → `plaintext`, `number`, `boolean`, a format id kept), `expression` the converted logic, an empty body. Formats: `prose` and `agent_plaintext` → `plaintext`, `agent_markdown_block` → `markdown_block`, `agent_markdown_inline` → `markdown_inline`; `classification`, `concept_list` and package formats keep their names. `failIfEmpty`: 30's sentence kept, absent → `null`. `onInvalid`: `fail`/`retry` kept, absent → 30's default as the engine applies it, `flag` → `fail` with A10-013. `parts: null`, `ordered: false` (Task 9 fills `parts`). Flows: the three loop headers of part 04 § 2.8 and the `case` of § 5.4, compared member for member with the spec's examples but for the flags (Task 12) (`example-nested-note`'s `parts` under its 30 id here; Task 8 renames it). `in` for an input fan is `input.<id>.value`, for a node's list `node.<id>.value.<field>`, for a snippet folder `snippet.<folder>`. `labelField` (the input's or the loop's) → `itemLabel` on each loop over it. A loop with `until` → `"parallel": false`, else `true`; `maxParallel: null`. A `while`: `while`, `max`, `until`, `checkFirst` (30's default written). An `if`: `then` and `else` bodies. `ACaseWithNoElse_GainsAnEmptyBranchForEachMissingValue` (`route` gains `{ "when": ["omit"], "nodes": [], "ret": null }`; a `case` whose subject is not an enum or a classifier keeps `else: null` and gets a hand edit, since 31 cannot list its values). `ret` values are full names (`node.sections_draft.route.narrative_draft`). A body node lives in its flow's folder; a flow's `index.json` is its header. `AWhenGate_BecomesIf`. `AConditionTheConverterRefuses_IsAHandEdit` (the 30 text stays as a JSON string in the member; one S10-002 whose reason is the converter's notice; D3). `ANotOnText_IsListed` (A10-008 in `Report.BlankConversions`). `EveryNodeFile_HasItsShape` (each passes `SourceMembers.Check`).
  *Recorded in the build:* a node is read at `node.<id>` (a reference resolves outward by bare id, part 01 § 9; part 04 § 3.5's `node.judge`), its rooted key (`Keys`) is its chain of flow ids; a `terms_subset` node is read at `check.<id>` as `check_result`, gets no file and is not listed in `nodes/index.json` (Task 11). A loop over a snippet folder takes `itemLabel: "title"`, the field of the folder's format that carries 30's item label (part 04 § 2.7's `test/general`). A `rendered` node's format is `plaintext`, as `prose`'s is. A classifier's `with`, and a `let`'s `onInvalid` (and a non-agent node's `failIfEmpty`), are dropped with A10-030 (D16). A `case` on a classifier is a hand edit (A10-032). A node `when` is retired since 26 (the 25 → 26 rung wraps it in an `if` node), so `AWhenGate_BecomesIf` runs a 25 package through the chain. The converter's `+1` for the header holds at every site: each rule is a member of a header's root object (level 1), in a `.md` header and in a flow's `index.json` alike. A `let`'s S10-003 keeps its row's `The condition '<expression>'`; the hand edit's file and member name the node.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** Kind follows which member the 30 node has (`WorkflowImpliedPaths.IsFlow`). Flags are written as placeholders (`reproducible` as 30 claimed it or the kind's default; the three flags `false`) for Task 12. A flow's `format` is a placeholder too.
- [x] **Step 4: Run; green.**
- [x] **Step 5: Commit and push.**

### Task 8: Reserved ids and collisions

**Spec:** part 10 § 1.9 and its sentence S10-006; part 01 § 8 (the reserved list; `ReservedIds`); D6.

**Files:** `Migrate.Renames.cs`; `tests/V31Migration/MigrateRenamesTests.cs`.

**Interfaces:**
- Produces (internal): `Renames.Plan(Migrated) : IReadOnlyDictionary<(string Kind, string Id), string>`, applied before any file is written, so every later pass writes new ids; `Migrated.Keys` uses new ids.

- [x] **Step 1: Write the failing tests.** `Parts_BecomesSections_InExampleNestedNote` (with `ExampleChoices`: the folder is `nodes/sections/`, every `ret`, `in`, condition and template reads `node.sections…`; A10-006 in `Report.Renames`). `WithNoChoice_TheKindIsTheSuffix` (`parts` → `parts_for`; a `while` → `_while`; an agent → `_node`; an input → `_input`; a macro → `_macro`; a snippet → `_snippet`; a format → `_format`; a deliverable → `_deliverable`). `ACollidingRename_GetsANumber` (`parts_for` taken → `parts_for_2`, S10-006). `ALongId_IsShortenedFirst` (the result is at most 64 characters). `ObjectFieldNames_AreNeverRenamed` (a field `title` or `value` of an input or a format stays). `ARename_TouchesReferencesOnly` (Review Focus 2). `AnIdThatFailsThe31Pattern_IsRenamedToo` only if 30 admits ids 31's pattern refuses (check `WorkflowPackageValidator`'s id rule against `^[a-z][a-z0-9_]*$`; if 30's rule is no wider, write `ThirtysIds_AlreadyFitThe31Pattern` instead).
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement** over the token machinery the 30 renamers use (`WorkflowTemplateTokens`, `WorkflowNodeRename`, `WorkflowTextRename`), covering the sites `RenameInManifest` misses (`while`, `until`, `let`, `for`).
- [x] **Step 4: Run; green.**
- [x] **Step 5: Commit and push.**

  *Recorded in the build:* the pass (`Migrate.Renames`, run between `Migrated.Read` and the layout) renames on the 30 package and reads it again, so every later pass sees only new ids. `Keys` stays keyed by the id as given (D1, D12), its values the new keys (`parts` → `node.sections`). Besides the reserved words it renames the Windows names (`ReservedIds.Windows`, `con` → `con_node`) and an id past 64 characters, which 30 admits (cut to 64, A10-034). A flow takes its member as its suffix (`_for`, `_while`, `_if`, `_case`), a leaf `_node`, a `terms_subset` node `_check`; a macro that D4 makes a snippet `_snippet` (`test/general`'s `test` → `test_snippet`). An id is taken when its namespace holds it (nodes; inputs; macros and snippets together; one folder's items; formats with the registry's names; deliverables) or the file it implies exists. A hand choice that is no 31 id or is taken refuses the run (A10-033). 30's id pattern is 31's, so `ThirtysIds_AlreadyFitThe31Pattern` was written.

### Task 9: Bodies — reads, pipes, whitespace, parts, unroll and hoist

**Spec:** part 10 § 1.2 (bare reads, built-in renderers, whitespace forms, `{{ if }}` blocks lifted, and Task 1's three rows), § 1.4; part 05 §§ 2 (the dialect: 2.2 built-ins, 2.5 body `if`, 2.6 placements), 3, 5.2, 6.3, 7, 10; part 04 § 2.6 (`.items`, `state`); `external/consultologist-package-format/v31/scriban-dialect.md`; R6.

**Files:** `V31/Migration/TemplateRewriter.cs`, `Migrate.Templates.cs`; `tests/V31Migration/TemplateRewriterTests.cs`, `MigrateTemplatesTests.cs`.

**Interfaces:**
- Produces (internal): `TemplateRewriter.Rewrite(string body, TemplateSite30 site, Migrated migrated) : RewrittenBody` with `Text`, `Parts` (id → `{ if, text }` for the node's `parts` member), `HandEdits`; edits are made by source span, last to first, so text outside tags keeps its bytes.

- [x] **Step 1: Write the failing tests**, each on the smallest body that shows it, then on the named file of `vendored/`:
  - `ABareInputRead_GainsContentOrValue`: `{{ input.consult_draft }}` → `{{ input.consult_draft.content }}` (`example-macros/prompts/summary.md`); a number input → `.value`; `input.patient.sex` → `input.patient.value.sex`.
  - `ANodeRead_KeepsItsMember`, `AClassifierValue_ReadsItsValueMember`.
  - `AConceptPipe_BecomesARendererPipe`: `{{ node.x | concept_bullets }}` → `{{ node.x.value | renderer.concept_bullets }}`.
  - `ALoopOverANode_ReadsItsItems`: `{{ for e in node.sections_draft }}` with `e.skipped`, `e.item.value.title` → the `.items` form with `state` (part 04 § 2.6), for `example-conditional-sections` and `example-prior-letters`.
  - `AWhitespaceForm31Refuses_IsRewritten`: `{{- … -}}` → the accepted form; `{{~ if … ~}}` of `patient_line.md` kept.
  - `ASimpleBodyIf_IsLiftedIntoAPart`: `{{ if <cond> }}text{{ end }}` on its own lines → `{{ part.<id> }}` at that place and `parts.<id> = { "if": <logic>, "text": [lines] }`; with an `else`, two parts with the condition and its negation; the part ids are `when_<n>` unless that is taken. A body `if` that tests a recorded fact the dialect lets a body test (§ 2.5: an item's `state`, `pass.number`) stays in the body, its reads rewritten.
  - `AnIfAroundAPlacement_IsHoisted`: `standing_draft.md`'s `{{ if input.include_counselling == true }}{{ macro.counselling.prompt }}{{ end }}` → `{{ macro.counselling }}`, and that condition is that one placement's rule (`MacroPlacement.Rule`; each placement carries its own, never joined with another's). Task 10 writes it into the macro's `if` when every placement of the macro carries the same rule; a macro whose placements disagree, within one prompt or across several (one hoisted, another or none on another), is a hand edit, A10-036 (A10-002's sibling).
  - `AForOverAMacroFolder_IsUnrolled`: `summary.md`'s `{{ for m in macro.notes }}- {{ m.label }}: {{ m.prompt }}\n{{ end }}` → one line per folder macro in the folder's order, `- <the macro's label>: {{ macro.<item id> }}` (the exact placement form of a folder macro as part 05 § 8 gives it).
  - `AProfileReadInAPrompt_IsAHandEdit` (S10-002 with S10-004; the read stays). `ABodyIfTooComplex_IsAHandEdit` (A10-001: an `else if`, a nested `if`, an `if` inside a line of text). `ABuiltinOutsideTheDialect_IsAHandEdit` (A10-005; `array.join` is checked against the dialect's list first).
  - `TextOutsideTags_IsNeverRewritten` (Review Focus 4). `ABodyThatDoesNotParse_IsAHandEdit_NeverAThrow`.
  - `EveryVendoredPrompt_RewritesOrSaysWhy`: over every prompt, snippet and macro body of the 16 packages, the rewritten body passes the dialect (`TemplateSyntax`/`DialectRules` report nothing) or carries a hand edit.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement** with a Scriban visitor over each tag's tree for the reads, and a tag scanner (`TemplateTags`) for whitespace marks.
- [x] **Step 4: Run; green.**
- [x] **Step 5: Commit and push.**

  *Recorded in the build:* the pass (`Migrate.Templates`, after the nodes pass) rewrites each agent's prompt (its lifted `if`s its `parts`, `null` when none), each `rendered` body, each snippet's and folder item's body, and each macro body into `Migrated.MacroBodies` (by file, for Task 10). It also rewrites `{{ macro.x.prompt }}` to the placement `{{ macro.x }}` everywhere (part 05 § 10), leaving `macro.x.content` reads to D4 (Task 10). `item.label` is `item.value.<itemLabel>` of the innermost loop, else a hand edit (A10-005). Every macro placement of a prompt or a snippet body is in `Migrated.MacroPlacements`, with the condition hoisted off it (converted with `Condition` on the macro's file, member `if`) or `null`; a macro whose placements all carry one condition has it in `Migrated.PlacementRules[macro]` (a folder macro keyed `<folder>/<id>`), and one whose placements differ, within one body or across several (`example-macros`' `counselling`: `standing_draft` and `patient_line`), is A10-036 with no entry. A state test rewritten from 30's `skipped` with no `else` gains an empty `{{ else }}` (S05-009). `patient_line.md`'s `{{~ if … ~}}` holds a placement, so it is hoisted rather than kept (A07-009); `~` stays on every statement that stays. `pass.number` is not one of part 05 § 2.5's recorded facts (the dialect has loop position, state and a check's pass), so `example-nested-note`'s `{{ if pass.number > 1 }}` is lifted into a part, not kept. `(x | string.strip) == ""` is refused by the converter (S03-002, Task 3), so `test/general`'s, `general`'s and `example-two-documents`' lifted parts are hand edits on `parts.when_<n>.if`. A part's text keeps 30's bytes: the branch less what its tags' marks removed, split on `\n`. A body still faulted by `TemplateSyntax` gets one A10-005 per fault. Task 8's leftover is fixed: the renames split a header only off the files 30 read headers of (`Resolved30.Headers`, and a snippet or macro file a rename moved). *2026-11-22 (Task 12, fix round 1):* `(x | string.strip) == ""` now converts to `blank(x)` (Task 3's note), so those three packages' lifted parts carry their rule and no hand edit; and R6's hoist applies in a snippet body as in a prompt (a simple `if` with no `else` around one placement, `AnIfAroundAPlacement_InASnippetBody_IsHoisted`). A placement in a snippet folder's item, hoisted or not, is A10-051 on its body (A07-012), so `test/general`'s `assessment_plan.md` gives `letrozole_prescription_text` its `if` and keeps one hand edit. An `if` around text, not a placement, in a snippet body (`example-macros-warnings`' `kept_note.md`, `composer-macros`' `review_leaflet.md`, both D4 snippets from 30 macro bodies) has nothing to hoist and stays A10-035.

### Task 10: Macros, choices as inputs, and `MacroChoices`

**Spec:** part 10 §§ 1.2 (optional macros), 1.3 (with Task 1's paragraph), 1.4; part 05 § 8 (8.1 to 8.4, 8.6); part 02 § 10; D3 to D5; R5, R7.

**Files:** `Migrate.Macros.cs`; `src/Consultologist.Api/V31/V31StartChecks.cs` (`Choices`); `tests/V31Migration/MigrateMacrosTests.cs`, `tests/V31Engine/` (the existing `Choices` tests, updated to the ruling).

**Interfaces:**
- Produces: `macros/<id>.md` and `macros/<folder>/…`; boolean inputs in the manifest; `V31StartChecks.Choices` keeps its signature.

- [x] **Step 1: Write the failing tests.** `AMacro_BecomesItsFile` (header `label`, `description: null`, `if`, `format` `plaintext` or as its body's use needs (part 05 § 8.3), `instruction` lines per D5; the body kept byte for byte but its final line ending as R5 says). `ThePromptRead_BecomesAPlacement` (`{{ macro.x.prompt }}` → `{{ macro.x }}`). `TheRenderedInstruction_Has30sBytes` (compose a node that places the macro with `BodyComposer` and compare with 30's `.prompt` text for the same macro, from `example-macros`' capture). `AnOptionalMacro_BecomesABooleanInput`: default `false` → `{ "===": [ { "var": "input.<id>.value" }, true ] }`, default `true` → `{ "!==": [ … , false ] }`; joined with `and` after the macro's own converted `if`; the input is § 1.3's object exactly, with the macro's label. `ACollidingChoice_TakesTheMacroSuffix` (`letrozole` → input `letrozole_macro`; S10-006's form; in `Report.MacroInputs`). `ANodeMacroPlacedBeforeOrAfter_IsAHandEdit` (D3, A10-003). `AMacroReadAsFixedText_BecomesASnippet`, `AMacroReadBothWays_IsAHandEdit` (D4, A10-004). `ATemplatedInstruction_IsAHandEdit` (`silent_reminder`, `wrap_up`). `AMacroFolder_KeepsItsItems` (the folder's `index.json` with `if`). At the start: `AChoiceKeyedByTheMacro_SetsTheInputItsIfReads` (a migrated package with `letrozole` and `letrozole_macro`: `MacroChoices { letrozole: true }` sets `letrozole_macro` and leaves the input `letrozole` alone); `AChoiceForAMacroWhoseIfReadsNoOrTwoBooleanInputs_IsRefused`; `AChoiceNamingNoMacro_IsRefused` (A07-015's sentence where its words still fit, else an A10 sentence numbered on); A07-016 unchanged.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** `Choices` reads the plan's macro entry and the boolean inputs its `if` reads (`resolve.json`'s `macros`; `ExpressionCheck.Reads` are in the compiled document or re-derived from the rule's `var`s).
- [x] **Step 4: Run** the area's tests and `--filter "FullyQualifiedName~V31"`: green.
- [x] **Step 5: Commit** (`PackageFormat (#1507): …`, `Api (#1507): a MacroChoices entry sets the input its macro's if reads`) and push.

  *Recorded in the build:* the pass (`Migrate.Macros`, after the templates pass) writes each macro that stays one as `macros/<id>.md` (a folder as `macros/<folder>/index.json` with `if: null` and one file per item): `label`, `description: null`, its `if` (its own 30 `if` converted at the top level, then the rule all its placements carry, then the choice test, joined with `and`), its `format` (the placing agents', a snippet's placement counted as the agents whose prompts place that snippet: `markdown_inline` if one is, `markdown_block` if all are, else `plaintext`; every 30 placer is prose, so `plaintext`), its `instruction` (D5: 30's lines, `""`, 30's mechanics sentence with the inline marker; the sentence alone when 30 had none), its body as 30 had it (R5). A macro D4 makes fixed text (`Renames.BecomesSnippet`, which now sees prompts: the 30 walk reads each node's implied prompt file) is `snippets/<id>.md` (`markdown_block`) instead, and every `macro.<id>.content` read of it is `snippet.<id>.content`; a `.content` read of a macro that stays one is a hand edit (A10-004 when it is also read as `.prompt`, else A10-040; A10-045 for an unrolled folder macro), written `macro.<id>.fixed_text` (a folder's: `macro.<folder>.<id>_fixed_text`, since 31 does not check a member after a folder item), which 31 refuses (S05-011). An optional macro's input takes the macro's id, else `<id>_macro` (A10-007 names it, and it is in `Report.MacroInputs`), else `<id>_macro_2`, … (S10-006, which only that case fits, then A10-007, both in `Report.MacroInputs`). A node instance (D3) is placed `{{ macro.<id> }}` at the end of the node's prompt unless the prompt already places it, its `if` that placement's rule, the instruction `[]`, one A10-003 per macro naming its sides (`before and after` when both); a macro whose placements then disagree is A10-036, and its `if` is written as a JSON string naming the conditions (S03-025 refuses it). A templated 30 instruction (D5) is A10-042, written as 30's lines alone (no marker of its own: S05-044), with the sentence added only when they hold its own marker (twice: S05-044). A macro body placing a macro is A10-041 (30 refuses it first, so no 30 package reaches it). `V31StartChecks.Choices` maps a `MacroChoices` entry through the plan's `macro.<id>` entry to the one boolean input its `if` reads (R7); no macro is A10-043, no or several boolean inputs A10-044, A07-016 unchanged; A07-015 is no longer said.

### Task 11: Checks and deliverables

**Spec:** part 10 § 1.2 (the `terms_subset` row, the deliverable rows), § 1.10; part 06 §§ 2, 3 (checks), 6 (6.1, 6.3 to 6.6), 7, 8, 10; part 03 § 7's check example; D4, D7, D8; the fixture README's three `composer-*` sections (30's bytes, rule by rule).

**Files:** `Migrate.Checks.cs`, `Migrate.Deliverables.cs`; `tests/V31Migration/MigrateChecksTests.cs`, `MigrateDeliverablesTests.cs`.

**Interfaces:**
- Produces: `checks/<id>.md`; `deliverables/<id>.md` and `deliverables/index.json` (`delivery`, `bundle` as area 08's fixtures write a package with no destination folder); `Migrated.Keys["deliverable.<id>"] = "deliverable.<id>"` (built keyed with the root, D1); a `terms_subset` node's `ConversionRead.Path` is `check.<id>`.

- [x] **Step 1: Write the failing tests.** `ATermsSubsetNode_BecomesACheck` (`kind: "computed"`, `format: "check_result"`, `expression` a `terms_subset` over its two operands with `failWith` as the literal, as part 03's migrated example writes it; no file under `nodes/`; removed from its parent's `nodes`). `EveryReaderOfTheNode_ReadsTheCheck` (`node.x` → `check.x` in conditions, templates and a deliverable's `check`). Deliverables: `ADeliverable_BecomesItsFile` (header exactly part 06 § 6.1's members; `format` `markdown_block`; `if` converted; `check` `check.<id>` or `null`; `whenEmpty` from the options, default `release`). One template test per composer rule, each asserting the template's text: one scalar source; two sources (one blank line between); a `for` (`{{ node.<loop>.content }}`); a `while`, an `if`, a `case`, a `let`; `signature: true` (`{{ profile.signature }}` after a blank line); an unconditional `prepend` and `append` macro as snippet reads in placement order (D4); `ADeliverableMacroWithACondition_IsAHandEdit` (D3, A10-002; a placement `if`, a macro `if`, an optional macro). `TheTestMacro_OfTestGeneral_BecomesASnippetReadAtTheEnd` (under its renamed id, D6). `ADeliverableThatReadsAnotherDeliverable_IsAHandEdit` if a 30 macro placed on it reads `deliverable.*` (part 06 § 6.3 refuses the read). `EveryDeliverableFile_PassesItsShape`.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** Whether each template reproduces 30's bytes is measured in Task 15, not here; this task writes the forms D8 names.
- [x] **Step 4: Run; green.**
- [x] **Step 5: Commit and push.**

  *Recorded in the build:* the checks pass (`Migrate.Checks`, after the macros pass) writes each top-level `terms_subset` node as `checks/<id>.md` in 30's order (the Check shape: `kind`/`producer` `computed`, `format: check_result`, flags as placeholders for Task 12, `if: null`, `expression: { "terms_subset": [ <of>.value, <in>.value, "<failWith>" ] }`, body empty); each operand goes through `Migrate.Condition` as a value declared `concept_list`, so one 31 cannot type would be a hand edit on `expression` (30 refuses such an operand first; `ACheckOperandThatIsNoConceptList_Is30sToRefuse` pins it); 30's `onInvalid` on a check is dropped with A10-030 (D16). The deliverables pass (`Migrate.Deliverables`, last) writes each deliverable as part 06 § 6.1's header (`if` through `Migrate.Condition`; `check` the gate's `check.<id>`; `whenEmpty` from the options, default `release`) and a body of pieces joined by one blank line with no final line ending (30's composed text has none): the prepends, each source's `.content`, the appends, `{{ profile.signature }}`. A macro with no condition is `{{ snippet.<id>.content }}` (the D4 snippet, or one written from the macro's body beside its macro file when a prompt or node also places it: the 31 checker accepts a snippet and a macro of one id); one with a condition (placement `if`, own `if`, optional) is A10-002 once per macro and deliverable, written `{{ macro.<id>.fixed_text }}` (`TemplateRewriter.FixedText`), which 31 refuses (S05-047: a deliverable places no macro). A check that 30 ran inside an `if` or `case` is written to `checks/` too, its id left in the flow's list (S01-009) with A10-046 (30 refuses a check inside a loop). A snippet copy whose id a 30 snippet has takes `<id>_snippet` (A10-048), and each hand edit on the macro body is said again on the copy. A macro body's `deliverable.*` read is A10-039 (`macro body`; `deliverable` on a D4 snippet), refused by S05-020 / S05-022. `Keys` holds a deliverable under its 30 key `deliverable.<id>` (value `deliverable.<31 id>`), not its bare id as D1 and the brief wrote: `composer-sources` has a node and a deliverable both named `plan`, and a bare key would overwrite the node's entry.

### Task 12: The derived flags, and every package through the checker

**Spec:** part 10 § 1.2 (the node-headers row), part 02 §§ 1.1 to 1.3, § 11's last bullet; part 04 § 4 (a flow's format from `ret`); D15; the invariant of *Global Constraints*.

**Files:** `Migrate.Derived.cs`, `V31/SourceDerived*.cs` (the internal entry point), `WorkflowPackageMigration30To31.cs` (the passes in order); `tests/V31Migration/MigrateDerivedTests.cs`, `MigratedPackagesTests.cs`, `tests/Fixtures/ByteEquivalence/migrated/**` (goldens).

**Interfaces:**
- Produces (internal): `SourceDerived.Values(SourcePackage, PackageTypes, Resolution, PackageExpressions, ITemplateReads) : IReadOnlyDictionary<string, DerivedFlags>` (path of the header's file → `Reproducible`, `ModelDerived`, `RunDependent`, `ProfileCarrying`), with no header check; `MigratedPackagesTests.Migrated(package)` for later tasks.

- [x] **Step 1: Write the failing tests.** `TheFlags_AreTheDerivedOnes` (after `Migrate`, `SourceCheck.Run` on a package with no hand edits says no S03-036, S04-015 or S04-016). `A30ClaimThatDiffers_IsListed` (a 30 node with `reproducible: true` that reads a model's output: `false` written, A10-010 in `Report.Reproducible`). `AFlowsFormat_ComesFromItsRet` (the loop formats of part 04 § 2.8, now exactly the spec's JSON, flags included). **The invariant**, a theory over the 16 packages: `NoHandEdit_MeansPublishRefusesNothing` and `AHandEdit_MeansPublishRefuses` (`CompiledPackage.Publish(files, registry, toolchain)`; on failure the message lists the refusals and the hand edits). `TheMigratedSource_IsTheGolden`, per package: every file under `migrated/<package>/` equals `Migrate`'s output byte for byte, with `notices.json` (`{ "notices", "handEdits", "refusals" }`); no file on disk the result lacks. `MigratingTwice_GivesTheSameBytes`. `NothingUnknownIsWritten` (no S01-023 from any migrated package, hand edits or not).
- [x] **Step 2: Run; they fail.** Expect the invariant to fail for several packages: each failure is a gap in Tasks 6 to 11 or a construct with no rule.
- [x] **Step 3: Close the gaps.** For each refusal `Publish` gives on a package with no hand edit: fix the rewrite if the spec gives the 31 form (add the unit test beside its task's tests first); otherwise make it a hand edit with an A10 reason, numbered on. Do not weaken the invariant. If the 31 checker refuses a form the spec says the migration writes (a contradiction between part 10 and what areas 02 to 08 built), stop and record a block on #1507.
- [x] **Step 4: Run; green. Read every golden under `migrated/`** against the spec's examples (part 01's `test/general` and `example-nested-note` layouts; part 04's loops and `route`; part 06's deliverables). List the packages that have hand edits, with their notices, in this plan under *Results* for Task 16.
- [x] **Step 5: Commit** (the goldens in their own commit, `Tests (#1507): the sixteen packages, migrated`) and push.

  *Recorded in the build:* the pass (`Migrate.Derived`, last) reads the 31 set as an upload and runs the checker's steps up to the derivation's inputs (the reader, the shapes of `checks/`, the type rules, the resolver), then the dialect, then `SourceDerived.Values(SourcePackage, Resolution, ITemplateReads?, PackageTemplates?)` (`V31/SourceDerived.Values.cs`, internal; `DerivedFlags(Reproducible, ModelDerived, RunDependent, ProfileCarrying, Known)` by header file), the derivation `Check` runs with no header held to it; it takes no `PackageExpressions` (the flags follow content, never a condition, part 02 § 1.2) and is empty when nodes read each other in a circle. A hand edit the reader, the shapes, the types or the resolver refuse leaves every placeholder (S01-009 for A10-046's list, a union format); one the expressions refuse (S03-025: a condition left as 30's text) or the dialect refuses (a body reading `deliverable.*`, `macro.<id>.fixed_text`) still lets the flags be derived, a refused body counting as unknown, so a flag that is then `false` is the placeholder's `false`. A `reproducible` with no rule keeps its placeholder. Task 7's placeholder `reproducible` for a leaf is now its kind's value, not 30's claim: a leaf declaring another is refused by the type rules, before the derivation could run. A 30 claim that differs from the value written is A10-010, in `Report.Reproducible` (30 lets only an agent node declare it). A flow's format stays Task 7's (the list of its `ret`'s, its `ret`'s, or the union), which the checker holds it to. The invariant held with no further gap: every package with no hand edit publishes with no refusal once the flags are derived, and every package with one is refused. `MigratedPackagesTests.Migrated(package)` migrates once per run; `Upload(result)` is the checker's upload. The goldens are `tests/Fixtures/ByteEquivalence/migrated/<package>/**` with `notices.json`, rewritten with `GOLDEN_UPDATE_V31=1`. *2026-11-22 (fix round 1):* the dialect's refusals no longer make every body unknown: `SourceTemplates.Readable` gives every body but those of the files it refuses (a snippet's or macro's text is unknown only when its own text is faulted, not when only its instruction or `if` is refused), so one refused body leaves the others read (`example-macros`' `nodes/summary.md` is now `profileCarrying: true`, through `sign_off`'s `profile.name`); a header made of a refused body says which of its `false` flags are not known (A10-049). The converter's `strip == ""` row (A10-050) took the hand edits off `general` and `example-two-documents`, which now publish with no refusal, and the snippet-body hoist moved `test/general`'s to A10-051; the invariant holds on all sixteen. `MigrateDeliverablesTests.ADeliverable_BecomesItsFile` used `string.strip == ""` as its refused condition and now uses `string.upcase`, which has no row.

## The rulings of 2026-11-22 (16:26Z), from the review of areas 08 and 09

Tasks 20 to 23 build #1507's last comment ("Apply them before the area's remaining tasks"). **They run after Task 12 and before Task 13** (Task 24, added during the build, runs with them, after Task 21); they are numbered on so earlier numbers stay as the ledger and the commits name them. They touch areas 08 and 09's code, by the owner's ruling: that is the one exception to *Global Constraints*' "the only edits outside `V31/` and the new files" (the 5–30 path still keeps its behaviour).

| # | Where | Choice |
|---|---|---|
| R8 | #1507, 2026-11-22 (2), 1 | Delivery for migrated accounts keeps 30's promise: `pdf` falls back to the link-only email with no password or over the attachment budget, recorded as sent link-only; the delivery settings are read once, at the start; the migration writes `delivery: ["pdf"]` on the root deliverables folder; one message per deliverable is an expected difference; `emr`, `zip` and the kind-to-endpoint section are Milestone 40 (#1443). |
| R9 | #1507, 2026-11-22 (2), 2 | A 30 prose value that a deliverable composes is `markdown_block`; prose read only by prompts stays `plaintext`. |
| R10 | #1507, 2026-11-22 (2), 3 | Formats fail closed in a `markdown_block` deliverable: a union or untyped read is escaped as a string; D9's check (area 08) refuses a block marker opened by an `if`; `markdown_inline` is held to one line; `DialectGoldensExport` recomputes every vector's `escaped`. |
| R11 | #1507, 2026-11-22 (2), 4 | The weekly summary's "outstanding migration hand edits" line is filled by this area. |
| D17 | R8 "recorded as sent link-only"; part 08 § 10 | The delivery row keeps part 08 § 10's three results: a link-only send is `result: "sent"`, its attempt's `outcome` is `sent link-only`, and the row's `reason` says why (no document password; attachments over the budget), an area sentence. No new result word, so the record's contract does not change unless its schema lists the outcome words (then it moves in the provenance repo's `m39-build`, pinned here). |
| D18 | R8 "read once, at job start, as in 30" | What 30 reads at the start is the delivery address and `delivery.emailPdf` (#518); they are carried in the 31 orchestration's input from the job's origin and the channels read them from there. The document password is read at the attempt, as 30 reads it at reply time: a secret does not belong in an orchestration's history. |
| D19 | R8 "or every deliverable blocked"; **ruled by the owner on #1507, 2026-11-22T21:38Z** | A job whose every deliverable is blocked (refused or held) still sends the link-only email, as 30's reply did: it says that no document could be sent, with the History link, and holds no clinical text (document labels only, package text). The delivery row records it `result: "sent"`, the attempt's `outcome` `sent link-only`, and the row's `reason` `every deliverable blocked` (an area sentence, beside D17's two). It is a 30 behaviour kept, so it is not an expected difference. D17, D18, D20, D21 and D22 were accepted as planned in the same comment. |
| D20 | R9 | "Composes" follows the deliverable's `sources` through flows: a source that is a flow makes every leaf its `ret` chain gives `markdown_block`. Only 30's `prose` (and an agent node with no format, which is prose) changes; `agent_plaintext` was declared plain text by its author and stays, and a `rendered` node and a `let` of `text` stay `plaintext` (escaped in the deliverable: Task 15's `escaped-plain-text`). A prose node both a prompt and a deliverable read is `markdown_block`. |
| D21 | R10 "`markdown_inline` is held to one line" | A `markdown_inline` value holds no line ending. A model's reply or an input given in that format that holds one does not fit the format (refused where a text format's value is validated, an area sentence), and a `markdown_inline` read in a `markdown_block` deliverable is otherwise unchanged. If the code shows "inside a line" can only be a publish rule on where the read sits, the implementer builds that instead and says so. |
| D22 | R11; part 09 § 3.9; part 10 §§ 1.4, 1.5 | "Outstanding migration hand edits" is the number of hand-edit notices `WorkflowPackageMigration30To31.Migrate` gives, summed over every account's active package still below 31, computed by the weekly summary itself (no store of migration reports exists until area 11's publish path); a package that does not migrate (a refusal) is counted beside it: `Outstanding migration hand edits: <n> in <p> packages; <q> packages do not migrate`. When the summary cannot read the packages or the registry, the line stays `not measured in this build`'s honest sibling, an error line, never a zero. |
| D23 | part 04 § 2.5, part 05 § 5.3 against `ScopeTree.ContentOf` (area 05 D10) | A `for` loop's `.content` is typed `markdown_block`, as the spec says twice, whatever its items' format: 30's bytes are kept, so a `markdown_block` deliverable does not escape a composed loop (Task 24). The cost, **flagged for the owner in the PR**: plain text inside a loop's items (an `agent_plaintext` or `rendered` body node, an item label from an input) reaches a Markdown deliverable unescaped through the loop's `.content`, as at 30, where a direct read of the same value is escaped (part 06 § 6.4; R10's "fail closed"). |

### Task 20: Formats fail closed in a `markdown_block` deliverable (R10)

**Spec:** #1507's comment of 2026-11-22T16:26Z, item 3; part 06 § 6.4; part 02's text formats (`markdown_inline` "sits inside a line"); area 08's plan D7, D9 and PR #1530's "Later cleanups" (`gh pr view 1530`); D21.

**Files:** `src/Consultologist.PackageFormat/V31/Templates/SourceTemplates.Deliverables.cs` (`Reads`, `Escapes`, `Escaped`, `MarkdownLines`, `WritesMarkdown`), `V31/Templates/BodyComposer*.cs` (`IsEscaped`, `WriteValue`) as needed, wherever a text format's value is validated (D21); `tests/V31Templates/DeliverableFormatsTests.cs`, `tests/V31Templates/DialectGoldensExport.cs` (`MergedVectors`), `DialectGoldenTests.cs`; `tests/V31Source/V31Contract.cs` for a new area sentence.

- [x] **Step 1: Write the failing tests.** `AnUntypedRead_InAMarkdownDeliverable_IsEscapedAsAString` and `AUnionRead_InAMarkdownDeliverable_IsEscapedAsAString` (compose a `markdown_block` deliverable reading a value whose format is null and one whose format is a union, holding `# not a heading` and `**x**`: the composed text is `MarkdownEscape.Escape` of the value's string form; the compiled document lists the read among the deliverable's escapes). `ABlockMarkerOpenedByAnIf_IsRefused` (a `plaintext` deliverable whose line is `{{ if x }}# Heading{{ end }}`, and the same with `-`, `*`, `>` and a `**` pair: S06-022, as D9 refuses the bare line; a tag that writes a value at the line's start is not a marker). `AMarkdownInlineValue_IsOneLine` (D21: a reply and an input with a line ending are refused with the area sentence; one line passes). `TheExport_RecomputesAKeptVectorsEscaped` (a kept foreign vector whose `escaped` is stale is exported with `MarkdownEscape.Escape(plain)`; `TheExport_KeepsTheCasesItDoesNotProduce` still holds for the vector's presence).
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** Keep every existing 31 golden green or regenerate it with `GOLDEN_UPDATE_V31=1` and read what moved (a compiled document's escapes may gain entries). If the conformance vectors in the content repo change, export with `CONFORMANCE_EXPORT_V31`, run `python3 scripts/check-v31.py` there, commit, push, move the pin.
- [x] **Step 4: Run** `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`: green.
- [x] **Step 5: Commit** (`PackageFormat (#1507): …`, `Tests (#1507): …`) and push.

  *Recorded in the build:* `SourceTemplates.Escaped` escapes every output whose format is not known to be Markdown (`markdown_block` or `markdown_inline`, alone or with `null`), an object or a list (S06-021 refuses those) or `null`: an untyped output (the one a published package can hold is an interpolated string, `{{ $"{node.x.content}" }}`), a union, and a format that cannot be read. The union publish let through was `plaintext | null` (an item's `.content` through `.items`): `FormatTypes.TextKind` does not see through the `anyOf`, so the old rule (`Text` → `TextKind == "plaintext"`) wrote it as Markdown; `TextKindOf` reads the text kind through such a union, and a `plaintext` deliverable now refuses a nullable Markdown read too (S06-021). A union of data (`string | number`, `section_plan | string`) was already escaped. The run needs no change: `BodyComposer` escapes what the compiled document's `escapes` lists, writing the value's string form (D13) escaped. D9 finds a line's first character past spaces, tabs and the tags that write nothing (an `if`, `else`, `end`, a `for`), so `{{ if x }}# Heading` is S06-022, quoted as written; an output at the line's start stands before what follows it and is no marker. `SubstitutionTests`' three plain-text notes wrote `{{ for … }}- {{ s }}` (a list item in plain text, refused now) and write `• ` instead. D21 is built as a value rule, as accepted, not as a publish rule on where a read sits: `CorvusFormatValueValidator.ValidateExpanded`, the one place the engine, the start checks and test stubs validate a value, holds every `markdown_inline` text in a value that fits its format as written to no `\n` or `\r`, at any depth (a `not: { pattern: "[\r\n]" }` on each `markdown_inline` schema of a second, held copy of the schema), and a value wrong only in that has the fault keyword `x-markdown` (`FormatValueResult.BreaksALine`). A reply then fails with A10-052 (after its repair under `retry`), an input refuses the start with A10-052; a computed value or a test stub that breaks a line keeps its own sentence. `DialectGoldensExport.MergedVectors` writes a kept vector as `{ plain, escaped }` with `escaped` from `MarkdownEscape.Escape`. No 31 golden moved and the conformance export into a copy of the pinned content repository changed no file, so the pin stays. The conformance README lists no A10 sentence (none of Tasks 3 to 12 added one), so A10-052 is not listed there either.

### Task 21: 30 prose a deliverable composes is `markdown_block` (R9)

**Spec:** the same comment, item 2; part 10 § 1.2's `prose` row (and its echoes in part 02 § 11 and part 05); D20.

**Files:** `V31/Migration/Migrate.Nodes.cs` (`LeafFormat`, `AgentFormat`), a helper computing the composed set from `Manifest30.EffectiveDeliverables[].Sources` before the nodes pass (as `Migrate.Deliverables.SourceRead` walks them); `Migrate.Flows.cs` and `Migrate.Macros.cs` (`MacroFormat`) only if a format they derive moves; `docs/…/10-migration-and-tools.md`, `02-types-and-inputs.md`, `05-templates-and-macros.md` (the row); the content repo's chapter 10 if it repeats the row; `tests/V31Migration/MigrateNodesTests.cs`, `MigrateDeliverablesTests.cs`; `migrated/**` regenerated.

- [x] **Step 1: Write the failing tests.** `ProseADeliverableComposes_IsMarkdownBlock` (a scalar source; a source inside a `for`, a `while`, an `if`, a `case`, reached through `ret`; a node with no format). `ProseOnlyPromptsRead_StaysPlaintext`. `AgentPlaintext_ARenderedNode_AndATextLet_StayPlaintext_EvenWhenComposed`. `AMacroPlacedOnAComposedNode_TakesItsFormat` (part 05 § 8.3: the macro's format follows its placers'). `AFlowOfComposedAndUncomposedProse_IsNotAUnion` or, if it is one, the test pins what is written and that `Publish` accepts it. The invariant of Task 12 still holds over the sixteen packages.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement; edit the spec rows,** each citing the ruling.
- [x] **Step 4: Run; green.** Regenerate `migrated/`; read every golden that moved.
- [x] **Step 5: Commit** (goldens in their own commit) and push.
  *Recorded in the build:* `Migrate.ComposedLeaves` (in `Migrate.Deliverables.cs`) follows each deliverable's `sources` through the flows' `ret`s (a loop's or a while's, an `if`'s `then` and `else`, a `case`'s branches and `else`) to every leaf before the nodes pass works out a format; `NodeTree.Composed` holds the set, and `LeafFormat` passes it to `AgentFormat(format, composed)`, which writes `prose` (or no format) that is composed as `markdown_block`. Flow formats, and the macros' formats (`MacroFormat`, from the placing agents), follow with no change to their code. 30 refuses an agent node with no format since specVersion 26 ("must declare a format"), so that case is reached only through `AgentFormat` and the test pins 30's refusal. 30 names only a top-level node as a source ("runs inside …; name node.<owner>") and a flow's `ret` leaves are composed together, so no flow holds both composed and uncomposed prose: `AFlowOfComposedAndUncomposedProse_IsNotAUnion` pins the case flow as one format either way. 30's catalogue here has no `agent_plaintext`, so its test gives the catalogue one (prose's entry under that name). A deliverable macro no agent places keeps `plaintext` (it is a hand edit, A10-002). 114 goldens moved, each one line from `plaintext` to `markdown_block`; no `notices.json` moved and the invariant holds on all sixteen. Part 04's "(migrated)" examples (§§ 2.8, 3.5, 5.4), which `MigrateFlowsTests` holds the migration to, now show `markdown_block`. The content repo's chapter 10 repeats the row and moved with it (the pin is at that commit); its chapter 5 echo of the row (§ 10) did not, as the dispatch limited the content repo to chapter 10. Test agents that place a migrated macro (`MigrateMacrosTests`, `V31StartChecksTests`) now take the macro's format. *Fix round 2:* 30 holds only a flow named as a source to one format, so a deliverable may compose a `for` or `while` whose `ret` is an `if` or `case` mixing 30 prose with a `let` of text, a `rendered` node or `agent_plaintext`. That `if` would be `markdown_block | plaintext`, which the deliverable escapes as one string (R10), so it is a hand edit, A10-053 on its `format`, written `markdown_block` (refused by S02-015); `ComposedNodes` (was `ComposedLeaves`) now holds every node the sources' `ret`s reach, flows included. No golden moved: no fixture package has the shape. Found while probing it, and left for the owner: a loop's `.content` is typed `plaintext` by the checker (`ScopeTree.ContentOf`, area 05's D10), not `markdown_block` as part 05 § 5.3 says, so every `for` a deliverable composes is escaped whole, its prose leaves' Markdown with it. *Task 24 (D23) and its fix round 1:* a `for`'s `.content`, and a flow's whose `ret` chain reaches a `for`, is now `markdown_block`, so A10-053 is said only where the mixed `if` or `case` reaches the deliverable through no `for`.

### Task 24: A loop's `.content` is typed `markdown_block`, as the spec says

**Why this task exists:** added during the build (controller's ruling, found in Task 21's fix round 2). Part 04 § 2.5's table and part 05 § 5.3 both say a `for` loop's `.content` is "the standard rendering (`## <item name>`, then the item's text, joined by blank lines)" **typed `markdown_block`**, "so today's readers keep their bytes" (#1406). The checker types it `plaintext` (`ScopeTree.ContentOf`: "a text format itself, otherwise plaintext"; a loop's format is a list), so a `markdown_block` deliverable escapes every composed loop whole, `## ` headers included: R9 has no effect on loop sources, which most migrated deliverables are, and their bytes cannot equal 30's. The spec is final and says it twice; the code is brought to it. It runs before Task 22 so the delivery tests and Task 15's comparison see the spec's type.

**Spec:** part 04 § 2.5 (the `.content` row), § 2.6 (an item's `.content`), § 3 (a `while`'s `.content` is its last pass's `ret`: unchanged); part 05 § 5.3's last bullet; part 06 § 6.4 (what a `markdown_block` and a `plaintext` deliverable take); area 05's plan D10 and area 06's plan D11 (what was built).

**Files:** `src/Consultologist.PackageFormat/V31/ScopeTree.cs`, `ScopeTree.Items.cs` (where a `for` loop's own `.content` is typed; not an item's, not a `while`'s, not an object-valued node's); whatever in `V31/Templates/` or `V31/Run/` reads that type; `tests/V31Resolve/` or wherever `ScopeTree`'s types are tested; `tests/V31Templates/DeliverableFormatsTests.cs`; `tests/V31Migration/MigrateDeliverablesTests.cs`; the 31 goldens that move (compiled documents' `escapes`), regenerated with `GOLDEN_UPDATE_V31=1`; `docs/customizable-workflow/package-format-library.md` where it states the type.

- [x] **Step 1: Write the failing tests.** `ALoopsContent_IsMarkdownBlock` (the checker types `node.<for>.content` as `markdown_block` whatever its items' format; an item's `.content`, a `while`'s `.content` and an object-valued node's `.content` are typed as before). `ALoopsContent_InAMarkdownDeliverable_IsNotEscaped` (compose a `markdown_block` deliverable reading `{{ node.<loop>.content }}` over two items whose text holds `**bold**`: the composed text is `## <label>`, a blank line, the text, joined by a blank line, byte for byte, and the compiled document lists no escape for the read). `ALoopsContent_InAPlaintextDeliverable_IsRefused` (part 06 § 6.4: the sentence a `markdown_block` read in a plain-text deliverable already gets). `AMigratedLoopSource_ComposesAs30Did` (in `MigrateDeliverablesTests`: a migrated package's deliverable over a `for` of composed prose publishes with no escape on the loop's read). The invariant of Task 12 still holds over the sixteen packages.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement** the smallest change that types a `for` loop's `.content` `markdown_block`. Existing tests of areas 05 to 09 that read a loop's `.content` into a plain-text deliverable, or pin its type or an escape of it, change only where this rule changes them, each change named in the report. If the 31 conformance vectors in the content repo move, export with `CONFORMANCE_EXPORT_V31`, run `python3 scripts/check-v31.py` there, commit, push, move the pin.
- [x] **Step 4: Run** `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`: green. Regenerate the goldens that moved; read them.
- [x] **Step 5: Commit** (`PackageFormat (#1507): …`, `Tests (#1507): …`, goldens in their own commit) and push.

### Task 22: Delivery keeps 30's promise (R8)

**Spec:** the same comment, item 1; part 06 § 8; part 08 § 10 (the delivery row); area 08's plan D10 and Task 11; `docs/customizable-workflow/engine-v31.md`'s delivery section; 30's `Email/SendEmailIntakeReplyActivity.cs` (`TryCreateAttachmentsAsync`, `ApplyBudget`, `EmailIntakeReply.Compose`) and #518's reads at the start (`ConsultGenerationTransport.EmailRequestedAsync`, the origin's `ReplyToAddress` and `EmailRequested`); D17 to D19.

**Files:** `src/Consultologist.Api/V31/DeliveryChannels.cs` (`MailDeliveryChannel`, `PdfDeliveryChannel`), `DeliverV31Activity.cs`, `DurableDeliveryHost.cs`, `ConsultGenerationOrchestratorV31.cs` (the input record), `V31JobStarter.cs`; `src/Consultologist.PackageFormat/V31/Run/Delivery.cs`, `DeliveryRunner.cs` (the outcome word and the reason); `V31/Migration/Migrated.cs` (the root `deliverables/index.json`), `Migrate.Deliverables.cs`; `docs/…/10-migration-and-tools.md` (§ 1.2's row; § 3.2's expected differences), `06-checks-deliverables-profile.md` (§ 8), `engine-v31.md`; `tests/V31Engine/DeliveryChannelsTests.cs`, `DeliverV31ActivityTests.cs`, `OrchestratorV31Tests.cs`, `tests/V31Run/DeliveryRunnerTests.cs`, `tests/V31Migration/MigrateDeliverablesTests.cs`; `migrated/**` regenerated.

- [x] **Step 1: Write the failing tests.** `Pdf_WithNoPassword_SendsLinkOnly` and `Pdf_OverTheAttachmentBudget_SendsLinkOnly` (replacing `Pdf_WithNoPassword_IsNotSetUp` and `Pdf_OverTheAttachmentBudget_Fails`: one message with no attachment and 30's link-only words for that case, the send's outcome link-only with its reason). `ALinkOnlySend_IsRecordedAsSentLinkOnly` (D17: the row's `result` `sent`, the attempt's `outcome` `sent link-only`, `reason` the sentence; the row validates against the record's contract). `TheDeliverySettings_AreReadAtTheStart` (D18: the address and `emailPdf` of the start reach the channel though the account's settings change before the attempt; a retry sees the same). `ThePassword_IsReadAtTheAttempt`. `AMigratedPackage_DeliversPdf` (the root `deliverables/index.json` has `"delivery": ["pdf"]`; a destination folder there is none). `AnAccountWithEmailOff_GetsNothing_AsAt30`. `AJobWhoseEveryDeliverableIsBlocked_SendsTheLinkOnlyEmail` (D19, ruled: one link-only message saying no document could be sent, with the History link and the documents' labels only, no clinical text; the row `result: "sent"`, the attempt's `outcome` `sent link-only`, `reason` `every deliverable blocked`). The existing delivery tests stay green or change only where the ruling changes them, each change named in the report.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** The 30 path's code is called or shared, never changed in behaviour. A new orchestration-input member is optional with a default, so an orchestration already running replays.
- [x] **Step 4: Edit the spec:** part 10 § 1.2 gains "the root deliverables folder → `delivery: ["pdf"]`"; § 3.2's list gains "one message per deliverable, where 30 sent one per job" (said in the report's `notices` by Task 15, since no compared text carries it); part 06 § 8 names `emr`, `zip` and the account's kind-to-endpoint section as Milestone 40 (#1443) and the `pdf` fallback; `engine-v31.md` says the settings are read at the start.
- [x] **Step 5: Run** the solution's tests (not E2E): green. Regenerate `migrated/`; read what moved. **Commit and push.**

  *Recorded in the build:* `DeliverySendOutcome` gains a trailing optional `LinkOnly` (why a send went as the link-only email) and `DeliverySend` a trailing optional `EveryDeliverableBlocked`; the runner records a `LinkOnly` send `sent`, its attempt `sent link-only`, its row's `reason` the sentence (A10-054 to A10-056, `DeliveryPlan`). `DeliveryPlan.Sends` gives a job that released nothing, with a deliverable blocked or held, one send for the job (`email` when one lists it, else `pdf`), each document by label with no text; an `emr`- or `zip`-only blocked deliverable and a skipped one are not in it. Either mail channel sends that email (the completed reply with A10-057 after its opening), and each mail destination each document lists gets a row from that one send (fix round 1, the controller's ruling: one message per job and address). It is sent only when the orchestration's input has `Delivery` (this build started it), so one already running replays as before (fix round 1). The `pdf` channel falls back on 30's two cases only (no password; over the budget); a render fault stays a failed send `error`, where 30 degraded any attachment fault to link-only. The start carries `V31DeliverySettings(origin.ReplyToAddress, origin.EmailRequested)` in `ConsultGenerationOrchestrationInputV31.Delivery` (optional, `null` for a running orchestration, whose channels read the settings at the attempt as before), and `DeliverV31Input.Delivery` takes it to the channel; the password stays read at the attempt. The record's contract moved in `consultologist-provenance` (`m39-build` 31c2beb): `reason` was "null for sent", and a delivery row was written only for a released deliverable. Changed tests: `Pdf_WithNoPassword_IsNotSetUp` and `Pdf_OverTheAttachmentBudget_Fails` replaced; the root `deliverables/index.json` assertions of `MigrateLayoutTests` and `MigrateDeliverablesTests` read `["pdf"]`. The 16 migrated goldens moved one member each.

### Task 23: The weekly summary's migration line (R11)

**Spec:** the same comment, item 4; part 09 § 3.9; part 10 §§ 1.4, 1.5; area 08's plan D14; D22.

**Files:** `src/Consultologist.Api/V31/OperatorWeeklySummary.cs` (`Compose`, `RunOnceAsync`); whatever reads an account's active packages (`WorkflowPackageStore`) and the published types in the Api; `tests/V31Engine/OperatorWeeklySummaryTests.cs`; `engine-v31.md`.

- [x] **Step 1: Write the failing tests.** `TheMigrationLine_CountsHandEditsOfPackagesBelow31` (two account packages at 30, one with hand edits, one clean, and one at 31: the line is D22's, counting only the first two). `APackageThatDoesNotMigrate_IsCountedApart`. `WhenThePackagesCannotBeRead_TheLineSaysSo_NeverZero`. `TheLargestRecordLine_IsUnchanged` (area 11's). The line carries only counts: no package text, no value (*Global Constraints*).
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement.** `Compose` takes the migration figures as a parameter; `RunOnceAsync` gathers them, one package's failure never failing the summary. If the Api cannot build `Migration31Options` (the registry, the toolchain, 30's catalogue) where the summary runs, stop and report BLOCKED with what is missing.
- [x] **Step 4: Run** the solution's tests (not E2E): green.
- [x] **Step 5: Commit** (`Api (#1507): …`) and push.

  *Recorded in the build:* the Api could not build `Migration31Options` where the summary runs (no type registry in its output); the controller ruled to bundle it. `Consultologist.Api.csproj` copies the package format's `v31/formats/*.json` and `v31/renderers/*` (to `package-format/v31/`) and the agents catalogue's `v31/formats/*.json` (to `agents/v31/formats/`): 16 files, 6,834 bytes. `PublishedTypeRegistry` (`V31/`) reads them once, on first use, by `TypeRegistry.Read`, a failure not kept; area 11's publish path reuses it; `PublishedTypeRegistryTests` holds the build output's copy equal to `V31Contract.PublishedTypes()`. The raw manifest: the store kept only the parsed model, so `IWorkflowPackageSources.ReadSourceAsync` (on `WorkflowPackageStore`) returns the stored `manifest.json` text and, at a version the 5–30 load takes, `SourceFiles` and the load's formats catalogue (31, below 5, or a version that does not read: the text alone). `OutstandingHandEdits` gathers the figures: each account's pin (`IWorkflowPackagePinResolver`), each distinct package read once, each below 31 migrated in memory; `<p>` is the packages with at least one hand edit; a refusal or a throw from `Migrate` is one of `<q>`; a pin, a package read or the registry (or the toolchain) that fails is A10-058. `EngineToolchain.Current()` gives the toolchain without an output contracts catalogue. D22's words are used as written, so one package reads `1 packages`.

### Task 13: The offline run

**Spec:** part 10 § 3.1 (the harness, the stubs); part 07 § 8 (what a test run needs: 8.1's `inputs`, `profile`, `run`, 8.2's stub keys); the guide's "The seams"; D12.

**Files:** Create `V31/Run/RunInputsBuilder.cs`, `V31/Harness/OfflineRun.cs`, `OfflineHost.cs`, `OfflineStubs.cs`; modify `src/Consultologist.Api/V31/V31StartChecks.cs` (`Inputs` delegates); `tests/V31Migration/OfflineRunTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public static class RunInputsBuilder   // the body of V31StartChecks.Inputs, moved; same refusals (A06-011 to A06-013)
  { public static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Build(RunPlan plan,
        IReadOnlyDictionary<string, JsonNode?> supplied, JsonObject runFacts, IReadOnlyDictionary<string, string?> profile); }
  public sealed record OfflineRequest(IReadOnlyDictionary<string, JsonNode?> Inputs, IReadOnlyDictionary<string, string?> Profile,
      JsonObject RunFacts, IReadOnlyDictionary<string, JsonNode?> Outputs,   // by 31 row key; a missing key is generated
      DateTime StartedAtUtc, RunLimits? Limits = null, Func<string, string>? StubSeed = null);   // row key → the seed (D12: 30's key)
  public sealed record OfflineText(string Key, string Text, string Hash);
  public sealed record OfflineResult(RunState? State, string? Sentence, IReadOnlyList<SourceRefusal> StartRefusals,
      IReadOnlyList<OfflineText> Prompts, IReadOnlyList<OfflineText> Renders, IReadOnlyList<DeliverableOutcome> Deliverables,
      IReadOnlyDictionary<string, JsonNode?> Outputs,   // every reply served, by row key
      IReadOnlyList<RunRow> Rows, IReadOnlyList<RunDecision> Decisions, IReadOnlyList<string> UnusedOutputs);
  public static class OfflineRun
  { public static Task<OfflineResult> RunAsync(RunPlan plan, OfflineRequest request); }
  ```
  `State` is null when the start refused (`StartRefusals`). A prompt's `Hash` is the row's `inputHash` (the sent text's); a render's the same member of its detail.

- [ ] **Step 1: Write the failing tests.** `TheStartsInputs_AreBuiltInTheLibrary` (`V31StartChecks.Inputs` and `RunInputsBuilder.Build` agree on the existing start tests' cases; the existing tests stay as they are). `AnOfflineRun_GivesWhatTheComposingHostGives` (on the `macros` and `checked-note` golden packages: the same prompts, renders and deliverable texts as `ComposingRunHost` with the same replies). `AMissingOutput_IsGeneratedFromTheFormat` (seeded by the row key, or by `StubSeed`; valid against the node's format, for a text format, `judgement`, `classification`, `concept_list` and an object format with nullable properties). `AnOfferedMarker_IsAppendedToAStub` (D12; in prompt order for an `ordered` node; not twice). `APinnedOutputThatNeverRuns_IsReported` (`UnusedOutputs`). `ARefusedStart_HasNoState`. `AnOfflineRun_AlwaysEnds` (Review Focus 5: a `while` that never stops ends at its `max`; a stub for a labelled `oneOf`, a `date`, a `pattern` fits or the node fails with its sentence). `TheRun_ReadsNoClock` (two runs give the same rows, decisions and hashes).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** `OfflineHost : IRunHost` calls `AgentTurn.Prepare`/`Finish`, `RenderTurn.Render`, `RuleTurn.Evaluate`, as `tests/V31Run/ComposingRunHost.cs` does; `UtcNow` is `StartedAtUtc`; `DelayAsync` completes at once. Extend `WorkflowStubOutputs.FromSchema` only where a 31 format needs it (`oneOf` of constants, `format: date`), keeping every existing seed's output (its tests and area 01's captures stay green).
- [ ] **Step 4: Run** the area's tests, `--filter "FullyQualifiedName~V31"` and `--filter "FullyQualifiedName~ByteEquivalence"`: green.
- [ ] **Step 5: Commit and push.**

### Task 14: The snapshot test

**Spec:** part 10 § 4; part 07 §§ 8.1 to 8.4 (with Task 1's `skipped`); D13; A10-011.

**Files:** `V31/Harness/SnapshotTest.cs`, `WorkflowPackageMigration30To31.cs` (`Snapshot`, and its call at the end of `Migrate`); `tests/V31Migration/SnapshotTestTests.cs`; the `migrated/` goldens regenerated.

**Interfaces:**
- Produces:
  ```csharp
  public static class SnapshotTest
  { public static JsonObject Write(RunPlan plan, OfflineRequest request, OfflineResult result);        // the test file
    public static OfflineRequest Fictional(RunPlan plan); }                                            // D13's inputs, profile, run
  // WorkflowPackageMigration30To31:
  public static Migration31Result Snapshot(string manifestJson, IReadOnlyDictionary<string, string> files, Migration31Options options);
  ```
  `Snapshot` takes a 31 source, publishes it in memory, runs it and returns the same files with `tests/migration_snapshot.json` and `tests/index.json` listing it; a source that does not publish is a refusal with the checker's sentences.

- [ ] **Step 1: Write the failing tests.** `TheSnapshot_FollowsTheGrammar` (the file validates against `test.schema.json`; `inputs` lists every input; `profile` exactly `needs.profile`; `run` exactly the plan's run facts; `outputs` one stub per agent row that ran, by row key; `expect.prompts` every agent row by `{ "hash" }`; `expect.deliverables` every deliverable by `{ "state", "hash" }`, or `{ "state": "skipped" }` alone). `TheSnapshot_PassesOnItsOwnPackage` (running the package again with the file's `inputs`, `profile`, `run` and `outputs` gives the hashes in `expect`). `ItIsWrittenByMigrate` (`tests/migration_snapshot.json` is in `Files` and `tests/index.json` lists `migration_snapshot`, for a package with no hand edit). `WithHandEdits_NoSnapshot_AndANotice` (A10-011; `tests/index.json` has `"items": []`). `TheSnapshot_HoldsNoLongText` (no `text` member under `expect`). `ItCountsAgainstSourceBounds` (the migrated package with its snapshot still passes intake; a package whose snapshot would pass `FileBytes` gets no snapshot and a notice numbered on).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; green.** Regenerate `migrated/`; read three snapshots in full.
- [ ] **Step 5: Commit and push.**

### Task 15: The comparison and the report

**Spec:** part 10 §§ 3.1, 3.2 (with Task 1's list and `notices`); the fixture README's "What area 10 does with the captures"; R1, R2, R4, R5; D8 to D12, D14; part 10's worst-case sentence.

**Files:** `V31/Harness/ByteEquivalence.cs`, `ExpectedDifferences.cs`, `MigrationReport.cs`, `KeyMap.cs`; `tests/V31Migration/ByteEquivalenceTests.cs`, `ExpectedDifferencesTests.cs`, `tests/Fixtures/ByteEquivalence/reports/<package>.json` (goldens).

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ComparedText(string Site, string OldHash, string NewHash, bool Equal, bool Expected, string? Explanation);
  public sealed record MigrationReport(string OldVersion, string NewVersion, IReadOnlyList<string> HandEdits,
      IReadOnlyList<string> Notices, IReadOnlyList<ComparedText> Texts)
  { public string ToJson(); }                                             // migration-report.json, canonical, valid against the schema
  public static class KeyMap    // 30's instance key ↔ 31's row key, through Migration31Result.Keys and each loop's numbering
  { public static string? To31(string key30, Migration31Result migrated, RunPlan plan);
    public static string? To30(string key31, Migration31Result migrated, RunPlan plan); }
  public static class ByteEquivalence
  { public static IReadOnlyList<ComparedText> Compare(string package, string scenario, JsonObject capture30,
        OfflineResult run31, Migration31Result migrated, RunPlan plan);
    public static OfflineRequest RequestFor(JsonObject scenario30, JsonObject capture30, Migration31Result migrated, RunPlan plan); }
  public static class ExpectedDifferences   // each rule: an id, the ruling or section it rests on, the explanation, the predicate
  { public static string? Explain(DifferenceContext context); public static IReadOnlyList<string> ConditionReasons { get; } }
  ```
- Consumes: `OfflineRun`, the capture document's shape (`status`, `failure`, `outputs`, `prompts[] { key, hash, text }`, `renders`, `deliverables[] { id, state, reason, hash, text }`), scenario files (`inputs`, `profile`, `macroChoices`, `outputs`).

- [ ] **Step 1: Write the failing tests.** `EqualTexts_AreEqual` (hashes from the capture and the run; `explanation` null). `ADifferenceNoRuleExplains_IsUnexpected`. One test per rule of `ExpectedDifferences`, each with a pair of texts the rule explains and a near pair it must not: `escaped-plain-text` (unescaping the 31 deliverable, `\` before ASCII punctuation and `&#32;`/`&#9;`, gives 30's bytes; part 06 § 6.4); `macro-trailing-whitespace` (R5: the texts differ only in white space at the end of a macro's body); `blank-conversion` (R1: the text hangs on a decision listed in `BlankConversions` whose read was empty or blank); `empty-input-list-runs` (R2, the two named captures: `#status` `Refused` → `Completed`, and every text only 31 has); `skipped-deliverable-reads-null` (R4 item 1); `refused-deliverable-withheld` (R4 item 3: a text that read a deliverable its check refused); `required-blank-refused` (R4 item 6); `all-refused-completes` (R4 item 7: `#status` `Failed` with a check's sentence → `Completed`); `explicit-null-for-optional` (§ 1.8: a prompt differs only by `null` members an agent's stub now carries). A rule is added only with its ruling or section in its `Basis`; `EveryRule_NamesItsBasis`. `TheStatus_IsCompared` (D11's `#status`: 30's `Completed`/`Failed`/`Refused` beside 31's). `MacroChoices_GoThroughTheStart` (a scenario's `macroChoices` reach the migrated inputs by Task 10's rule). `ScenarioInputs_GoThrough31sStart` (a supplied `" "` is not given, as the README's § on scopes says). `AKeyThatCannotBeMapped_IsReported` (Review Focus 5: a site under `#prompts@30/`). `TheReport_IsValidAgainstItsSchema`. `TheWorstCaseNote_IsSaidPastTheLimit` (D14; the sentence's row, or A10-014). **The goldens**, a theory over the packages of Task 12 with no hand edit: for each scenario, capture → `RequestFor` → `OfflineRun` → `Compare`; `reports/<package>.json` is the report over all its scenarios. No assertion on how many differ.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement,** then work the unexpected differences down: each one is (a) a fault of the migration (fix it in its task's file, with a unit test there; the `migrated/` goldens move), (b) a difference a ruling or the spec declares (a rule, with its test), or (c) neither: it stays `expected: false`. Keep a list of (c) in this plan under *Results*, each with its site, both texts' first differing line described in words (no hashes needed), and the suspected cause, for the PR.
- [ ] **Step 4: Run; green. Read every report.**
- [ ] **Step 5: Commit** (reports in their own commit) and push.

### Task 16: The hand edits an operator would make, so every package is compared

**Spec:** part 10 §§ 1.4, 1.5 ("making any hand edits, so every package migrates"), § 3.2 (`handEdits`).

**Files:** `tests/Fixtures/ByteEquivalence/hand-edits/<package>/**` and `hand-edits/<package>/README.md`; `tests/V31Migration/HandEditsTests.cs`; `reports/<package>.json` for those packages; `tests/V31Migration/OperatorRunTests.cs` (D10).

**Interfaces:**
- Consumes: Task 12's list of packages with hand edits; `Snapshot`; `ByteEquivalence`.
- Produces: `MigrationFixture.Edited(package)`: the migrated files with every file under `hand-edits/<package>/` laid over them (a file named `<path>.deleted` removes `<path>`).

- [ ] **Step 1: Write the failing tests.** `EveryPackageWithHandEdits_HasItsEdits` (a folder per package of Task 12's list, and none for a package with no hand edit). `TheEditedPackage_Publishes` (`Publish` refuses nothing). `EachNotice_IsAnswered` (the README lists every hand-edit notice of the package, each beside the file that answers it; the test holds the README's notices to `notices.json`). `TheEditedPackage_GetsItsSnapshot` (`Snapshot` succeeds). The report goldens for these packages, with `handEdits` the notices. `TheOperatorsRun_WritesEveryPackage` (D10: with `BYTE_EQUIVALENCE_PACKAGES` set to a temporary folder holding two vendored packages, `_migrated/<package>/` and `_migrated/<package>/migration-report.json` are written; without the variable the operator's test does nothing).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Make each edit** as its notice instructs and as the spec's own examples do it (a conditional deliverable macro as a snippet with an `if`; a `before`/`after` macro as an instruction saying where; a prompt's profile read moved to the deliverable). Change the fewest files. An edit that needs a design choice the notices do not settle is not made: the package is listed in the PR with the notice, and its test row is in an exception table with the reason.
- [ ] **Step 4: Run; green.** Work the new reports' unexpected differences as Task 15, Step 3 says.
- [ ] **Step 5: Commit and push.**

### Task 17: The script

**Spec:** part 10 §§ 1.1 (callers), 1.4 (exits 0, 1, 2 and *"written; hand edits remain"*); D2, D6, D7.

**Files:** `scripts/migrate-workflow-package.cs`; `tests/V31Migration/MigrateScriptTests.cs` (if the repo's script tests run a script as a process, follow them; otherwise test the script's helper methods through the library and run the script by hand in Step 4).

- [ ] **Step 1: Write the failing tests** (or the hand-run checklist): `--to 31` on a 30 folder, dry run: prints notices and the file plan, writes nothing. `--write`: the folder holds exactly the 31 files (`manifest.json`, the nine folders; 30's `prompts/`, flat `macros`, `dag.mmd` handling as the 28 stage deletes what is no longer named), exit 0 when no hand edit, exit 1 with `written; hand edits remain` (S10-001) when some. A folder below 30 is chained in the same run. A version below 5, an unreadable manifest: exit 2, nothing written. `--rename old=new` and `--when-empty <deliverable>=<value>` (repeatable) fill the options. `--snapshot` on a 31 folder writes `tests/migration_snapshot.json` (after hand edits). The usage comment lists `--to 31`. `dag.mmd` is not written for 31 (area 13's), and the script says so in one line.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement** the 31 block before the "already at" guard, with a write path that does not assume `MigrationRunner.Write`'s 30 layout. The registry is read with `TypeRegistry.Read` from `external/` as `V31Contract.PublishedTypes` does; the toolchain is `Toolchain.Current` with the pinned names.
- [ ] **Step 4: Run** the script on a copy of `vendored/example-nested-note` (`--rename parts=sections --when-empty note=fail --write`) and on `vendored/example-macros`; compare the written folders with `migrated/`. Record both commands and exits in the PR's *Verification*.
- [ ] **Step 5: Commit and push.**

### Task 18: Migration vectors for the conformance suite

**Spec:** part 10 § 5 ("Migration vectors": 30 packages with their expected 31 output and notices); the conformance README's layout; `index.json`'s `migration-vectors` family (exported by area 10).

**Files:** `tests/V31Migration/MigrationVectorsExport.cs`; `~/Projects/consultologist-package-format`: `v31/conformance/migration-vectors/**`, `v31/conformance/index.json`, `README.md`, `scripts/check-v31.py` (a `migration-vectors` group: each case has `source/`, `expected/` and `notices.json`; `expected/manifest.json` is at 31; every `tests/migration_snapshot.json` validates against `test.schema.json`), `package-format-v31.md` chapter 12's line; this repo's pin; the area sentences in the conformance README.

- [ ] **Step 1: Write the failing test** `ThePinnedVectors_AreWhatMigrateGives` (each case under the pinned `migration-vectors/` equals `Migrate` on its `source/` with its `options.json`), and the export test that does nothing without `CONFORMANCE_EXPORT_V31`.
- [ ] **Step 2: Run; it fails** (the family is empty).
- [ ] **Step 3: Export** a case per construct, small where possible: the three `composer-*` fixture packages, `example-nested-note` (renames, a `while`, a `let`, formats), `example-conditional-sections` (`case`, `labelField`), `example-macros` (macros, unroll, hoist, hand edits), `test/general` (a snippet folder, concept renderers, a deliverable macro), and one hand-built package for `_macro`, a union input and a refused condition. Each case: `source/` (the 30 files), `options.json` (renames, `whenEmpty`), `expected/` (the 31 files), `notices.json`, `about.txt`. Run `python3 scripts/check-v31.py`; commit and push there; move the pin.
- [ ] **Step 4: Run** the area's tests and `--filter "FullyQualifiedName~V31Contracts|FullyQualifiedName~Conformance"`: green.
- [ ] **Step 5: Commit and push.**

### Task 19: The guide, the fixture README and the coverage table

**Files:** `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: migration (preview)" after "checks, deliverables and delivery": the converter, the rewrite pass by pass, hand edits and the invariant, the offline run and who else uses it, the snapshot, the comparison and its rules, the script, the operator's run, the goldens and how to regenerate each, what it leaves to areas 11 to 13); `tests/Fixtures/ByteEquivalence/README.md` (the four new folders; "What area 10 does" rewritten as what it did); `docs/customizable-workflow/engine-v31.md` (the `MacroChoices` rule at the start); this plan (every checkbox, *Results*, the coverage table's commits).

- [ ] **Step 1: Write the docs** from the code as built, not from this plan.
- [ ] **Step 2: Run the whole verification** of the runbook's step 6 once here, to find what step 6 would: `dotnet build Consultologist.sln`; `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`.
- [ ] **Step 3: Commit and push.**

## Results

*(Filled during the build: Task 12's packages with hand edits; Task 4's `unruled` differences; Task 15's and Task 16's unexpected differences; A10 sentences added.)*

### Task 12: the packages with hand edits (for Task 16)

Migrated by `Migrate` (goldens `tests/Fixtures/ByteEquivalence/migrated/<package>/`, each with `notices.json`). 7 packages have none and publish with no refusal: `example-classifier-scope`, `example-conditional-documents`, `example-conditional-sections`, `example-nested-note`, `example-prior-letters`, `example-two-documents`, `general`. 9 have hand edits, each refused by `CompiledPackage.Publish` until edited; their hand-edit notices, in the order said (regenerated in Task 12's fix round 1, 2026-11-22: `general` and `example-two-documents` lost their `string.strip == ""` edits and have none; `test/general`'s two went too, and its `assessment_plan.md` edit is now A10-051):

- **`example-macros`** (13):
  - Hand edit needed in macros/header_line.md, body: the template uses case at line 1, which the 31 dialect doesn't have; rewrite it.
  - Hand edit needed in macros/counselling.md, if: macro 'counselling' is placed under different conditions in nodes/standing_sections/standing_draft.md and nodes/patient_line.md; a macro has one if, so place it under one condition, or split it into one macro per condition.
  - Hand edit needed in macros/counselling.md, instruction: macro 'counselling' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/section_caveat.md, instruction: macro 'section_caveat' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/table_note.md, instruction: macro 'table_note' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/preamble.md, instruction: macro 'preamble' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/sign_off.md, instruction: macro 'sign_off' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/silent_reminder.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Hand edit needed in macros/wrap_up.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'header_line' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'followup_only' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'brief_plan' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'follow_up_reminder' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
- **`example-macros-warnings`** (15):
  - Hand edit needed in macros/header_line.md, body: the template uses case at line 1, which the 31 dialect doesn't have; rewrite it.
  - Hand edit needed in snippets/kept_note.md, body: the body's if at line 1 decides what a macro body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.
  - Hand edit needed in macros/counselling.md, if: macro 'counselling' is placed under different conditions in nodes/standing_sections/standing_draft.md and nodes/patient_line.md; a macro has one if, so place it under one condition, or split it into one macro per condition.
  - Hand edit needed in macros/counselling.md, instruction: macro 'counselling' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/section_caveat.md, instruction: macro 'section_caveat' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/table_note.md, instruction: macro 'table_note' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/preamble.md, instruction: macro 'preamble' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/sign_off.md, instruction: macro 'sign_off' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/silent_reminder.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Hand edit needed in macros/wrap_up.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Hand edit needed in macros/self_read.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'header_line' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'followup_only' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'brief_plan' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult_note.md, body: macro 'follow_up_reminder' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
- **`example-problem-list`** (1):
  - Hand edit needed in snippets/sections/follow_up_plan.md, if: a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.
- **`example-structured-intake`** (1):
  - Hand edit needed in nodes/sections_senior_review_section/senior_review_section.md, body: the body's if at line 1 does more than choose text to show; write it as parts with their own if.
- **`example-v12-constructs`** (5):
  - Hand edit needed in nodes/extract_note_terms.md, body: the template reads deliverable.consult.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Hand edit needed in nodes/extract_letter_terms.md, body: the template reads deliverable.ack_letter.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Hand edit needed in deliverables/consult.md, body: macro 'formal_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult.md, body: macro 'friendly_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/consult.md, body: macro 'followup' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
- **`test/general`** (1):
  - Hand edit needed in snippets/standards/assessment_plan.md, body: macro 'letrozole_prescription_text' is placed at line 5, but a snippet folder's item read in a loop places no macro at 31; place it in the prompt that loops over the folder.
- **`composer-gates`** (4):
  - Hand edit needed in nodes/extract_note_terms.md, body: the template reads deliverable.note.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Hand edit needed in nodes/extract_copy_terms.md, body: the template reads deliverable.record_copy.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Hand edit needed in nodes/cover_note.md, parts.when_1.text: the template reads deliverable.patient_copy.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Hand edit needed in nodes/cover_note.md, body: the template reads deliverable.note.content at line 2, which a 31 prompt doesn't read; rewrite it.
- **`composer-macros`** (11):
  - Hand edit needed in snippets/review_leaflet.md, body: the body's if at line 1 decides what a macro body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.
  - Hand edit needed in snippets/visit_echo.md, body: macro 'new_visit_note' is read as fixed text at line 1, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.
  - Hand edit needed in macros/quoted_review.md, body: the template reads deliverable.review_summary.content at line 1, which a 31 macro body doesn't read; rewrite it.
  - Hand edit needed in macros/section_close.md, if: The condition 'item.value.title != "Plan"' compares item.value.title (markdown_block) with a literal; text is tested by whether it is blank, its size, or in.
  - Hand edit needed in macros/section_intro.md, instruction: macro 'section_intro' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in macros/section_close.md, instruction: macro 'section_close' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Hand edit needed in deliverables/gated.md, body: macro 'new_visit_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/gated.md, body: macro 'review_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/optional.md, body: macro 'followup_offer' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/optional.md, body: macro 'leaflet_offer' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Hand edit needed in deliverables/echoes.md, body: macro 'quoted_review' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
- **`composer-sources`** (2):
  - Hand edit needed in nodes/topic_tally.md, expression: The condition '"Topic section length: " | string.append (node.topic_loop.content | string.size)' uses string.append, which has no JSON Logic form at 31: rewrite it by hand. The condition '"Topic section length: " | string.append (node.topic_loop.content | string.size)' uses string.size, which has no JSON Logic form at 31: rewrite it by hand.
  - Hand edit needed in nodes/refine/draft_summary.md, body: the body's if at line 1 does more than choose text to show; write it as parts with their own if.

The flags of a package with hand edits are derived as far as the checker reads it (Task 12, *Recorded in the build*); an edit that changes what a value is made of (a rewritten expression, a prompt that no longer reads a deliverable, a body the dialect now reads) can change a header's derived flags, which the checker then names (S03-036, S04-015, S04-016).

## Spec coverage

| Requirement | Task |
|---|---|
| 10 § 1.1 the tool, its callers, its output, never an unknown member | 5, 12 (`NothingUnknownIsWritten`), 17; the operator's run 16 (D10); the Editor is Milestone 41 |
| 10 § 1.2, row by row: files and folders, lists into indexes, `templating`, every member, `\r\n` | 6 |
| … node headers (`reproducible`, `producer`, the three flags, flow `format`) | 7, 12 |
| … reserved ids | 8 |
| … `kind` and `format`; `prose`, `agent_*`, `x-generated`; format files; every property required | 6, 7 |
| … inputs; union inputs; a long free-text field; bare `input.x` reads | 6, 9 (and 2 for conditions) |
| … `labelField`; `when`; the `case` with no `else` | 7 |
| … string conditions; `let` | 2, 3, 7 |
| … `terms_subset`; deliverables; the signature | 11 |
| … built-in renderers; whitespace forms; `{{ if }}` blocks; unroll, hoist, bare reads (R6) | 9 |
| … `failIfEmpty` | 7 |
| … optional macros; § 1.3; `MacroChoices` (R7) | 10 |
| … one snapshot test; § 4 | 14 |
| 10 § 1.4 hand edits, notices, the exits | 3, 7, 9, 10, 11, 12 (the invariant), 17 |
| 10 § 1.5 account packages | the folder run and the report 16; publish, pins and owners are area 11's and the user's (*Not this area's*) |
| 10 § 1.6, § 1.7 | the user's (the runbook's *After area 13*) |
| 10 § 1.8 every property required; explicit nulls noted | 6, 15 (`explicit-null-for-optional`) |
| 10 § 1.9 renames | 8 |
| 10 § 1.10 `whenEmpty` | 11 (D7), 17 |
| 10 § 2.1 the surface | 2, 3 |
| 10 § 2.2 the table, strict equality, the classifier gate, canonical output | 2 |
| 10 § 2.3 refusals, `!x` on text | 3 |
| 10 § 2.4 fixtures for every row and refusal; semantic equivalence | 2, 3, 4 |
| 10 § 3.1 the harness, the stubs | 13 |
| 10 § 3.2 the report: differences, expected, what it lists, the worst case, its shape | 15, 16; `notices` 1 (D9) |
| 10 § 5 migration vectors | 18 |
| 10 *Refusals* (S10 rows) | 3 (S10-003, the type sentence), 9 (S10-004), 8 and 10 (S10-006), 15 (worst case), 17 (S10-001), every hand edit (S10-002); *"stays at version 30"* and *"migrated by the operator"* are said by the publish path (area 11) |
| 02 § 11, row by row | 6 (inputs, formats), 7 (nodes, `let`), 9 (reads), 10 (choices), 11 (checks), 12 (flags and the `reproducible` list) |
| #1507: `!x` (R1); two completions (R2); `skipped` (R3); seven rulings (R4); 2026-11-22's three (R5 to R7) | 1, 3, 4, 9, 10, 14, 15 |
