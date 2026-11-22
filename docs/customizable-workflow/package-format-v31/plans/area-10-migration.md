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
| D1 | § 1.1 "the shape of the result matches the earlier rewriters'" | `Migration31Result` has the four members of `Migration26Result` (`ManifestJson`, `Files` without `manifest.json`, `Notices`, `Refusals`), and adds `HandEdits` (the S10-002 notices, a subset of `Notices`), `Report` (`MigrationNotes`: renames, `_macro` inputs, `blank` conversions, suggestions, `reproducible` claims that differed) and `Keys` (each 30 node id to its 31 rooted key, each deliverable id to `deliverable.<id>`). A refusal writes nothing. |
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
| A10-003 | reason | `macro '<id>' was placed <before \| after> the reply by the engine; say in its instruction where the model writes its marker.` |
| A10-004 | reason | `macro '<id>' is read both as fixed text and as an instruction; split it into a snippet and a macro.` |
| A10-005 | reason | `the template uses <construct> at line <n>, which the 31 dialect doesn't have; rewrite it.` |
| A10-006 | notice | `Renamed <kind> '<id>' to '<new id>': '<id>' is reserved at 31.` |
| A10-007 | notice | `Macro '<id>' is now offered by the input '<input id>'.` (with S10-006's form when `_macro` was needed) |
| A10-008 | notice | `<file>, <member>: '!<path>' became blank(<path>); it now also holds for empty and white-space-only text.` |
| A10-009 | notice | `Input '<id>', field '<field>' holds free text; consider a text format.` |
| A10-010 | notice | `Node '<id>' declared reproducible <true \| false> at 30; <true \| false> is written, as its reads give it.` |
| A10-011 | notice | `No snapshot test was written: hand edits remain.` |
| A10-012 | refusal | `The package is at specVersion <n>; migration starts at 5.` |
| A10-013 | notice | `Node '<id>' kept a reply that did not fit its format (onInvalid: flag); 31 fails it instead.` |
| A10-014 | notice | `<package> could run for about <n> minutes in the worst case; this engine stops a job after <limit> minutes.` (part 10's own words; used only if `sentences.json` has no row for it) |

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

- [ ] **Step 1: Write the failing tests** in `V31ContractsTests`: `ATestExpectation_MayBeASkippedStateAlone` (`{ "state": "skipped" }` validates against `test.schema.json`; `{ "state": "released" }` alone does not); `TheMigrationReport_ListsItsNotices` (the schema requires `notices`, an array of strings; a report without it is invalid).
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Change the content repo** on `m39-build`: the two schemas, a valid and an invalid example for each change, the README's lines, chapter 10 and the test grammar in `package-format-v31.md`. Run `python3 scripts/check-v31.py`. Commit, push, move the pin.
- [ ] **Step 4: Edit part 10.** § 1.2: three rows after "simple `{{ if }}…{{ end }}` blocks" (a `for` over a macro folder → one placement per folder macro; a body `if` around a placement → the placement's header `if`; a bare read → `.content` / `.value`), each citing the ruling. § 1.3: a paragraph, "A `MacroChoices` entry keyed by a macro's id sets the one boolean input that macro's `if` reads, and is refused otherwise", with `letrozole` / `letrozole_macro`. § 3.2: an "Expected differences" list: the `blank` case, the two completions (R2), rulings 1, 3, 6, 7 of R4, macro bodies' trailing whitespace (R5), plain text escaped into a Markdown deliverable (part 06 § 6.4); and `notices` in the report's shape. Part 07 § 8.3: the `deliverables` row allows `{ "state": "skipped" }` alone.
- [ ] **Step 5: Run** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Contracts"`: green. **Commit** (`Docs (#1507): …`, `Tests (#1507): …`) and push.

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

- [ ] **Step 1: Write the failing tests.** `EveryRowOf2_2_ConvertsToItsLogic`: a theory over the `row.*` ids of `conditions/expressions.json` (all 41), each built into a `ConversionScope` from its `reads` (`MigrationFixture.ScopeOf(reads)`), asserting `Logic` is not null except where Task 3 decides (`row.not-text.*` are asserted there). Pinned by hand, as canonical JSON, at least: `row.eq` → `{"===":[{"var":"input.x.value"},…]}` in the entry's own operands; `row.and3` → one `and` of three; `row.size.pipe` and `row.size.member` → `{"size":[…]}`; `row.downcase` → `lower`; `row.strip.input` → `trim`; `row.contains` → `{"in":["<p>",<x>]}`; `row.date-parse.lt` → the literal string beside `<`; `row.let-object` → `object`; `row.let-array` → a JSON array inside it; `row.input.x` on a text input → `{"var":"input.x.content"}`, on any other → `.value`; a classifier read `node.x.value` → `{"var":"<Path>.value.value"}` (§ 2.2's second bullet). `TheOutput_IsCanonical` (`CanonicalJson.Write(Logic)` round-trips byte for byte; converting twice gives the same bytes). `ThePrinter_ReadsWhatTheConverterWrites` (`JsonLogicPrinter.TryParse(JsonLogicPrinter.Print(Logic))` gives the same tree, for every converted expression). `TheGolden_IsWhatTheConverterGives`: `converted.json` holds, per expression of `expressions.json` in order, `{ id, logic, resultFormat, reads, notices }`.
- [ ] **Step 2: Run; they fail** (no converter).
- [ ] **Step 3: Implement.** Parse with `WorkflowConditionAt25.TryParse`; walk the Scriban nodes (binary, unary, nested, member chains flattened as `WorkflowTemplateTokens.Flatten` does, pipes, function calls, literals, object and array initialisers). `==`/`!=` always write `===`/`!==`. `&&`/`||` chains flatten. A node's read goes through `scope.Nodes[id].Path`; an id not in scope is a notice. Anything outside the table returns null with one notice (worded in Task 3; a placeholder constant is fine here only if Task 3's test replaces it, so write S10-003 now through `MigrationSentences.NoJsonLogicForm(expression, construct)`).
- [ ] **Step 4: Run; green. Read `converted.json` in full.**
- [ ] **Step 5: Commit** (`PackageFormat (#1507): the condition converter writes § 2.2's table`, `Tests (#1507): …`) and push.

### Task 3: The converter's types, `!x` on text, and what it refuses

**Spec:** part 10 § 2.3 and its *Refusals*; part 03 §§ 5, 6 (types, null rules), its *Refusals* (S03-002 to S03-009, S03-018, S03-025).

**Files:** `V31/Migration/ConditionTyping.cs`, `V31/Migration/MigrationSentences.cs`, `WorkflowConditionConverter.cs`; `tests/V31Source/V31Contract.cs` (the A10 dictionary and branch); `ConditionConverterTests.cs`; `converted.json` regenerated.

**Interfaces:**
- Consumes: `ExpressionChecker.Check(JsonNode? rule, ExpressionSite site, IExpressionScope scope, FormatSet formats) : ExpressionCheck` (`Refusals`, `Type`, `Reads`).
- Produces: `ConversionResult.ResultFormat` (the checker's type, printed as `TypeStrings` prints a format: `boolean` for a condition), `Reads` (the checker's, in order), `BlankConversions`; `FromScriban(string expression, ConversionScope scope, ConversionKind kind = ConversionKind.Condition)` with `enum ConversionKind { Condition, Value }` (`Value` for a `let` and a `case` subject: any type is allowed).

- [ ] **Step 1: Write the failing tests.** `NotOnText_BecomesBlank` (`row.not-text.node` and `row.not-text.input` → `{"blank":[{"var":"…content"}]}`, each path in `BlankConversions`; `row.not-boolean` stays `{"!":[…]}`). `EveryRefusalOf2_3_GivesNoLogicAndASentence`: the eight `refuse.*` ids give `Logic == null` and one notice each; `refuse.text-less-than`'s notice is S03-008 as the checker says it; `refuse.profile` names `profile.name`; the builtins, the assignment, the template and the two statements are S10-003 with `<construct>` the builtin's name, `an assignment`, `a template` and `more than one statement`. `ACondition_YieldsABoolean` (a `Condition` whose type is not boolean is refused with S03-025; the same text as a `Value` converts). `TheReads_AreWhatTheLogicReads`. `AnExpressionNearTheTable_ConvertsOrRefuses_NeverThrows` (Review Focus 1's list, each asserted to its exact logic or to null with a notice). `EveryPackageExpression_Converts`: the 44 package expressions of `expressions.json` all give logic (if one cannot, the test names it and the task records why in the plan before going on). Every notice passes `V31Contract.AssertSentence`.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** Build an `IExpressionScope` over `ConversionScope` (inputs with `value`/`content`/`title` heads typed from their formats; nodes at their `Path`; `item`, `pass.number`, `run.*` as area 04's `LogicFixture` shows a scope is made). Decide `!x` by the operand's type before checking: a text format → `blank`. Run the checker on the written logic with a site of kind `NodeIf` (or `Expression` for a `Value`); a refusal becomes the notice, sentence unchanged, and `Logic` null.
- [ ] **Step 4: Run; green.** Regenerate and read `converted.json`.
- [ ] **Step 5: Commit and push.**

### Task 4: Semantic equivalence with 30's evaluator

**Spec:** part 10 § 2.4; the fixture README's `conditions/` section; R1.

**Files:** `tests/V31Migration/ConditionEquivalenceTests.cs`, `tests/V31Migration/MigrationFixture.cs` (the data builder), `tests/Fixtures/ByteEquivalence/conditions/equivalence.json` (golden).

**Interfaces:**
- Consumes: `vectors.json` (`id`, `refused`, `cases[] { scope, result, error }`), `converted.json`, `new CorvusJsonLogicEvaluator().Compile(rule).Evaluate(data)`.
- Produces: `MigrationFixture.DataOf(JsonObject scope, JsonObject reads) : JsonObject`, the 31 data for one case: an input's supplied value passed through 31's start rules (absent, `null` or blank text → not given, so `input.x.value` and `.content` read `null`; otherwise `{ value, content }`), a node at its 31 path (`"<absent>"` → skipped: `value` and `content` `null`), `item`, `pass.number`, `run.date`.

- [ ] **Step 1: Write the failing tests.** `EveryConvertedExpression_AnswersAs30Does`: for each expression with logic, each case with `error == null`: 31's value equals `result` (a boolean for a condition; canonical JSON for a `case` or `let`), **or** the case is in `equivalence.json` under that expression with a named reason. The only reasons allowed are `blank` (the expression has a `BlankConversions` path whose value in this case is `""` or white space, or an input not given) and reasons this task adds to `ExpectedDifferences.ConditionReasons` with a ruling or a spec section beside each. `ACaseWith30sError_IsListedNotCompared` (cases with an `error` appear in `equivalence.json` with 31's answer beside 30's sentence). `TheGolden_IsWhatTheRunGives`.
- [ ] **Step 2: Run; it fails** (no golden; and whatever disagrees).
- [ ] **Step 3: Make it true.** A disagreement is a converter fault until shown otherwise: fix the converter (Tasks 2 and 3's tests stay green). A disagreement that is 31's own null rule (part 03 § 6: for example an absent number compared with `<`) with no ruling is **not** silently accepted: it is listed in `equivalence.json` with reason `unruled`, counted by the test (`TheUnruledDifferences_AreTheseOnes`, pinned by id), and reported in the PR under *Decisions made during build* for the owner. If an `unruled` difference touches a condition of a public package (not only a `row.*` fixture), stop and record a block on #1507 as the runbook says.
- [ ] **Step 4: Run; green. Read `equivalence.json` in full.**
- [ ] **Step 5: Commit and push.**

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

- [ ] **Step 1: Write the failing tests.** `APackageBelow5_IsRefused` (A10-012; `Files` and `ManifestJson` are the input's). `AManifestThatDoesNotRead_IsRefused`. `A31Package_IsRefused` (*"The package is already at specVersion 31."*). `AnOlderPackage_IsRaisedThroughEveryRung` (a 24 fixture and a 27 fixture from the existing migration tests reach 30 by `To30`, equal to running the rungs by hand as `tests/WorkflowPackageMigration27To28Tests.cs` does). `TheStageTo24_IsTheScripts` (the moved code gives what the script's gave on the script's existing fixture, if one exists; otherwise on `EditorFixtures`' pre-23 package). `Migrate` on a 30 package returns `Succeeded` with a manifest whose `specVersion` is 31 (nothing else asserted yet).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement** the move, the chain and the entry point's gates.
- [ ] **Step 4: Run** the area's tests and `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`: green.
- [ ] **Step 5: Commit and push.**

### Task 6: The layout, the manifest, inputs and formats

**Spec:** part 10 § 1.2 rows 1 to 5, the format rows, the input rows, § 1.8; part 02 §§ 3.3, 8, 11; part 01 §§ 2, 3, 5 (every member written, the nine folders, line endings).

**Files:** `V31/Migration/Migrated.cs`, `Migrate.Layout.cs`, `Migrate.Inputs.cs`; `tests/V31Migration/MigrateLayoutTests.cs`, `MigrateInputsTests.cs`, `tests/V31Migration/ExampleChoices.cs`.

**Interfaces:**
- Produces (internal): `Migrated` — `Manifest30` (`WorkflowPackageManifest`), `Root30` (`JsonObject`), `Files30`, `Resolved30` (`WorkflowResolvedTexts`), `Walk30` (`WorkflowDerivedEdges.Walk`: every template and every condition site), `Files` (path → text, the 31 set), `Notice(string)`, `HandEdit(string file, string member, string reason)`, `Keys`, `Scope(site) : ConversionScope` (filled as later tasks add nodes). `Migrated.Json(JsonObject)` writes one file's JSON; `Migrated.Md(JsonObject header, string body)` writes `---\n<one-line header>\n---\n<body>`.

- [ ] **Step 1: Write the failing tests.** `AllNineFolders_AreWritten_EachWithItsIndex` (a package with no macros still has `macros/index.json` with `"items": []`; same for the other eight). `TheManifest_KeepsIdentityAndInputsOnly` (`name`, `version`, `specVersion: 31`, `derivedFrom`, `title`, `description` as lines, `tags`, `inputs`; no `templating`, `nodes`, `macros`, `snippets`, `formats`, `deliverables`). `EveryMember_IsWritten` (each written file passes `SourceMembers.Check` for its shape with no S01-021 and no S01-023). `CarriageReturns_AreNormalised`. Inputs, one test per row of part 02 § 11: `text` → `markdown_block`; no `type` → `markdown_block`; `enum` + `values` → `{ "type": "string", "enum": [...] }` with no titles; `date` → `"date"`; `boolean`, `number`; `object` + `fields` → `properties` with every field in `required`, an optional field nullable, `title` from the field's label; `array` + `items`; a union `[x, array]` → the array of `x`; `expectedContent` → `channel`; `description: null`; `required` kept; the 30 array's order kept as the object's member order. `AFreeTextStringField_IsSuggested_NotChanged` (A10-009 in `Report.Suggestions`). Formats: `APackageFormat_BecomesItsSchemaAlone` (`formats/<id>.json` is the schema with `$schema` `urn:consultologist:spec:31:dialect`, no `producer`, no `outputMacros`, the description kept as the schema's `description`); `EveryProperty_IsRequired_OptionalOnesNullable` (nested objects and array items too). `AnyBrokenPackage_IsRefusedOrMigrated_NeverThrows` (Review Focus 3). `SnippetsAndFolders_MoveWithTheir31Headers` (a single snippet: `label`, `description: null`, `if: null` for now, `format` `markdown_block`; a folder: `index.json` with `description`, `items`, `format`, each item's header a value of the folder's format, 30's item `label` carried as the member `title` of a package format `<folder>_item` unless the folder's items already share a declared shape; read part 05 § 6 and part 01's `test/general` example, which gives `standard_section`).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** Read 30 through `WorkflowPackageManifestJson.Read` and `WorkflowTextHeaders.Resolve`; a read that fails is a refusal.
- [ ] **Step 4: Run; green.**
- [ ] **Step 5: Commit and push.**

### Task 7: Nodes, flows and their conditions

**Spec:** part 10 § 1.2 (node headers, `format` with `producer`, `prose` and `agent_*`, `when` → `if`, the `case` row, string conditions, `let`, `labelField`, `failIfEmpty`); part 02 §§ 1, 11; part 04 §§ 2 (with 2.7 and 2.8), 3, 4, 5 (with 5.4); part 05 § 1.2; part 01 § 6 (keys, `ret` as the child's full name).

**Files:** `Migrate.Nodes.cs`, `Migrate.Flows.cs`; `tests/V31Migration/MigrateNodesTests.cs`, `MigrateFlowsTests.cs`.

**Interfaces:**
- Consumes: `Migrated`, `WorkflowConditionConverter.FromScriban`.
- Produces: every node written under `nodes/` at its nested path; `Migrated.Keys[<30 id>] = "node.<path>"`; `Migrated.Scope(site)` now knows every node's `ConversionRead` (a `terms_subset` node's path is `check.<id>`, Task 11) and each loop's item format.

- [ ] **Step 1: Write the failing tests.** Kinds: a 30 node with `format` of an attested or custom producer → `kind: "agent"`, `producer: "generated"`; `format: "rendered"` → `kind: "rendered"`, `producer: "computed"`, a text format; `let` + `type` → `kind: "computed"`, `format` from `type` (`text` → `plaintext`, `number`, `boolean`, a format id kept), `expression` the converted logic, an empty body. Formats: `prose` and `agent_plaintext` → `plaintext`, `agent_markdown_block` → `markdown_block`, `agent_markdown_inline` → `markdown_inline`; `classification`, `concept_list` and package formats keep their names. `failIfEmpty`: 30's sentence kept, absent → `null`. `onInvalid`: `fail`/`retry` kept, absent → 30's default as the engine applies it, `flag` → `fail` with A10-013. `parts: null`, `ordered: false` (Task 9 fills `parts`). Flows: the three loop headers of part 04 § 2.8 and the `case` of § 5.4, compared member for member with the spec's examples but for the flags (Task 12) (`example-nested-note`'s `parts` under its 30 id here; Task 8 renames it). `in` for an input fan is `input.<id>.value`, for a node's list `node.<id>.value.<field>`, for a snippet folder `snippet.<folder>`. `labelField` (the input's or the loop's) → `itemLabel` on each loop over it. A loop with `until` → `"parallel": false`, else `true`; `maxParallel: null`. A `while`: `while`, `max`, `until`, `checkFirst` (30's default written). An `if`: `then` and `else` bodies. `ACaseWithNoElse_GainsAnEmptyBranchForEachMissingValue` (`route` gains `{ "when": ["omit"], "nodes": [], "ret": null }`; a `case` whose subject is not an enum or a classifier keeps `else: null` and gets a hand edit, since 31 cannot list its values). `ret` values are full names (`node.sections_draft.route.narrative_draft`). A body node lives in its flow's folder; a flow's `index.json` is its header. `AWhenGate_BecomesIf`. `AConditionTheConverterRefuses_IsAHandEdit` (the 30 text stays as a JSON string in the member; one S10-002 whose reason is the converter's notice; D3). `ANotOnText_IsListed` (A10-008 in `Report.BlankConversions`). `EveryNodeFile_HasItsShape` (each passes `SourceMembers.Check`).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** Kind follows which member the 30 node has (`WorkflowImpliedPaths.IsFlow`). Flags are written as placeholders (`reproducible` as 30 claimed it or the kind's default; the three flags `false`) for Task 12. A flow's `format` is a placeholder too.
- [ ] **Step 4: Run; green.**
- [ ] **Step 5: Commit and push.**

### Task 8: Reserved ids and collisions

**Spec:** part 10 § 1.9 and its sentence S10-006; part 01 § 8 (the reserved list; `ReservedIds`); D6.

**Files:** `Migrate.Renames.cs`; `tests/V31Migration/MigrateRenamesTests.cs`.

**Interfaces:**
- Produces (internal): `Renames.Plan(Migrated) : IReadOnlyDictionary<(string Kind, string Id), string>`, applied before any file is written, so every later pass writes new ids; `Migrated.Keys` uses new ids.

- [ ] **Step 1: Write the failing tests.** `Parts_BecomesSections_InExampleNestedNote` (with `ExampleChoices`: the folder is `nodes/sections/`, every `ret`, `in`, condition and template reads `node.sections…`; A10-006 in `Report.Renames`). `WithNoChoice_TheKindIsTheSuffix` (`parts` → `parts_for`; a `while` → `_while`; an agent → `_node`; an input → `_input`; a macro → `_macro`; a snippet → `_snippet`; a format → `_format`; a deliverable → `_deliverable`). `ACollidingRename_GetsANumber` (`parts_for` taken → `parts_for_2`, S10-006). `ALongId_IsShortenedFirst` (the result is at most 64 characters). `ObjectFieldNames_AreNeverRenamed` (a field `title` or `value` of an input or a format stays). `ARename_TouchesReferencesOnly` (Review Focus 2). `AnIdThatFailsThe31Pattern_IsRenamedToo` only if 30 admits ids 31's pattern refuses (check `WorkflowPackageValidator`'s id rule against `^[a-z][a-z0-9_]*$`; if 30's rule is no wider, write `ThirtysIds_AlreadyFitThe31Pattern` instead).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement** over the token machinery the 30 renamers use (`WorkflowTemplateTokens`, `WorkflowNodeRename`, `WorkflowTextRename`), covering the sites `RenameInManifest` misses (`while`, `until`, `let`, `for`).
- [ ] **Step 4: Run; green.**
- [ ] **Step 5: Commit and push.**

### Task 9: Bodies — reads, pipes, whitespace, parts, unroll and hoist

**Spec:** part 10 § 1.2 (bare reads, built-in renderers, whitespace forms, `{{ if }}` blocks lifted, and Task 1's three rows), § 1.4; part 05 §§ 2 (the dialect: 2.2 built-ins, 2.5 body `if`, 2.6 placements), 3, 5.2, 6.3, 7, 10; part 04 § 2.6 (`.items`, `state`); `external/consultologist-package-format/v31/scriban-dialect.md`; R6.

**Files:** `V31/Migration/TemplateRewriter.cs`, `Migrate.Templates.cs`; `tests/V31Migration/TemplateRewriterTests.cs`, `MigrateTemplatesTests.cs`.

**Interfaces:**
- Produces (internal): `TemplateRewriter.Rewrite(string body, TemplateSite30 site, Migrated migrated) : RewrittenBody` with `Text`, `Parts` (id → `{ if, text }` for the node's `parts` member), `HandEdits`; edits are made by source span, last to first, so text outside tags keeps its bytes.

- [ ] **Step 1: Write the failing tests**, each on the smallest body that shows it, then on the named file of `vendored/`:
  - `ABareInputRead_GainsContentOrValue`: `{{ input.consult_draft }}` → `{{ input.consult_draft.content }}` (`example-macros/prompts/summary.md`); a number input → `.value`; `input.patient.sex` → `input.patient.value.sex`.
  - `ANodeRead_KeepsItsMember`, `AClassifierValue_ReadsItsValueMember`.
  - `AConceptPipe_BecomesARendererPipe`: `{{ node.x | concept_bullets }}` → `{{ node.x.value | renderer.concept_bullets }}`.
  - `ALoopOverANode_ReadsItsItems`: `{{ for e in node.sections_draft }}` with `e.skipped`, `e.item.value.title` → the `.items` form with `state` (part 04 § 2.6), for `example-conditional-sections` and `example-prior-letters`.
  - `AWhitespaceForm31Refuses_IsRewritten`: `{{- … -}}` → the accepted form; `{{~ if … ~}}` of `patient_line.md` kept.
  - `ASimpleBodyIf_IsLiftedIntoAPart`: `{{ if <cond> }}text{{ end }}` on its own lines → `{{ part.<id> }}` at that place and `parts.<id> = { "if": <logic>, "text": [lines] }`; with an `else`, two parts with the condition and its negation; the part ids are `when_<n>` unless that is taken. A body `if` that tests a recorded fact the dialect lets a body test (§ 2.5: an item's `state`, `pass.number`) stays in the body, its reads rewritten.
  - `AnIfAroundAPlacement_IsHoisted`: `standing_draft.md`'s `{{ if input.include_counselling == true }}{{ macro.counselling.prompt }}{{ end }}` → `{{ macro.counselling }}`, and that condition joined by `and` into the placement's rule (Task 10 writes it into the macro's `if` when the macro is placed once; a macro placed under different conditions in several nodes is a hand edit, A10-002's sibling, numbered on).
  - `AForOverAMacroFolder_IsUnrolled`: `summary.md`'s `{{ for m in macro.notes }}- {{ m.label }}: {{ m.prompt }}\n{{ end }}` → one line per folder macro in the folder's order, `- <the macro's label>: {{ macro.<item id> }}` (the exact placement form of a folder macro as part 05 § 8 gives it).
  - `AProfileReadInAPrompt_IsAHandEdit` (S10-002 with S10-004; the read stays). `ABodyIfTooComplex_IsAHandEdit` (A10-001: an `else if`, a nested `if`, an `if` inside a line of text). `ABuiltinOutsideTheDialect_IsAHandEdit` (A10-005; `array.join` is checked against the dialect's list first).
  - `TextOutsideTags_IsNeverRewritten` (Review Focus 4). `ABodyThatDoesNotParse_IsAHandEdit_NeverAThrow`.
  - `EveryVendoredPrompt_RewritesOrSaysWhy`: over every prompt, snippet and macro body of the 16 packages, the rewritten body passes the dialect (`TemplateSyntax`/`DialectRules` report nothing) or carries a hand edit.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement** with a Scriban visitor over each tag's tree for the reads, and a tag scanner (`TemplateTags`) for whitespace marks.
- [ ] **Step 4: Run; green.**
- [ ] **Step 5: Commit and push.**

### Task 10: Macros, choices as inputs, and `MacroChoices`

**Spec:** part 10 §§ 1.2 (optional macros), 1.3 (with Task 1's paragraph), 1.4; part 05 § 8 (8.1 to 8.4, 8.6); part 02 § 10; D3 to D5; R5, R7.

**Files:** `Migrate.Macros.cs`; `src/Consultologist.Api/V31/V31StartChecks.cs` (`Choices`); `tests/V31Migration/MigrateMacrosTests.cs`, `tests/V31Engine/` (the existing `Choices` tests, updated to the ruling).

**Interfaces:**
- Produces: `macros/<id>.md` and `macros/<folder>/…`; boolean inputs in the manifest; `V31StartChecks.Choices` keeps its signature.

- [ ] **Step 1: Write the failing tests.** `AMacro_BecomesItsFile` (header `label`, `description: null`, `if`, `format` `plaintext` or as its body's use needs (part 05 § 8.3), `instruction` lines per D5; the body kept byte for byte but its final line ending as R5 says). `ThePromptRead_BecomesAPlacement` (`{{ macro.x.prompt }}` → `{{ macro.x }}`). `TheRenderedInstruction_Has30sBytes` (compose a node that places the macro with `BodyComposer` and compare with 30's `.prompt` text for the same macro, from `example-macros`' capture). `AnOptionalMacro_BecomesABooleanInput`: default `false` → `{ "===": [ { "var": "input.<id>.value" }, true ] }`, default `true` → `{ "!==": [ … , false ] }`; joined with `and` after the macro's own converted `if`; the input is § 1.3's object exactly, with the macro's label. `ACollidingChoice_TakesTheMacroSuffix` (`letrozole` → input `letrozole_macro`; S10-006's form; in `Report.MacroInputs`). `ANodeMacroPlacedBeforeOrAfter_IsAHandEdit` (D3, A10-003). `AMacroReadAsFixedText_BecomesASnippet`, `AMacroReadBothWays_IsAHandEdit` (D4, A10-004). `ATemplatedInstruction_IsAHandEdit` (`silent_reminder`, `wrap_up`). `AMacroFolder_KeepsItsItems` (the folder's `index.json` with `if`). At the start: `AChoiceKeyedByTheMacro_SetsTheInputItsIfReads` (a migrated package with `letrozole` and `letrozole_macro`: `MacroChoices { letrozole: true }` sets `letrozole_macro` and leaves the input `letrozole` alone); `AChoiceForAMacroWhoseIfReadsNoOrTwoBooleanInputs_IsRefused`; `AChoiceNamingNoMacro_IsRefused` (A07-015's sentence where its words still fit, else an A10 sentence numbered on); A07-016 unchanged.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** `Choices` reads the plan's macro entry and the boolean inputs its `if` reads (`resolve.json`'s `macros`; `ExpressionCheck.Reads` are in the compiled document or re-derived from the rule's `var`s).
- [ ] **Step 4: Run** the area's tests and `--filter "FullyQualifiedName~V31"`: green.
- [ ] **Step 5: Commit** (`PackageFormat (#1507): …`, `Api (#1507): a MacroChoices entry sets the input its macro's if reads`) and push.

### Task 11: Checks and deliverables

**Spec:** part 10 § 1.2 (the `terms_subset` row, the deliverable rows), § 1.10; part 06 §§ 2, 3 (checks), 6 (6.1, 6.3 to 6.6), 7, 8, 10; part 03 § 7's check example; D4, D7, D8; the fixture README's three `composer-*` sections (30's bytes, rule by rule).

**Files:** `Migrate.Checks.cs`, `Migrate.Deliverables.cs`; `tests/V31Migration/MigrateChecksTests.cs`, `MigrateDeliverablesTests.cs`.

**Interfaces:**
- Produces: `checks/<id>.md`; `deliverables/<id>.md` and `deliverables/index.json` (`delivery`, `bundle` as area 08's fixtures write a package with no destination folder); `Migrated.Keys[<deliverable id>] = "deliverable.<id>"`; a `terms_subset` node's `ConversionRead.Path` is `check.<id>`.

- [ ] **Step 1: Write the failing tests.** `ATermsSubsetNode_BecomesACheck` (`kind: "computed"`, `format: "check_result"`, `expression` a `terms_subset` over its two operands with `failWith` as the literal, as part 03's migrated example writes it; no file under `nodes/`; removed from its parent's `nodes`). `EveryReaderOfTheNode_ReadsTheCheck` (`node.x` → `check.x` in conditions, templates and a deliverable's `check`). Deliverables: `ADeliverable_BecomesItsFile` (header exactly part 06 § 6.1's members; `format` `markdown_block`; `if` converted; `check` `check.<id>` or `null`; `whenEmpty` from the options, default `release`). One template test per composer rule, each asserting the template's text: one scalar source; two sources (one blank line between); a `for` (`{{ node.<loop>.content }}`); a `while`, an `if`, a `case`, a `let`; `signature: true` (`{{ profile.signature }}` after a blank line); an unconditional `prepend` and `append` macro as snippet reads in placement order (D4); `ADeliverableMacroWithACondition_IsAHandEdit` (D3, A10-002; a placement `if`, a macro `if`, an optional macro). `TheTestMacro_OfTestGeneral_BecomesASnippetReadAtTheEnd` (under its renamed id, D6). `ADeliverableThatReadsAnotherDeliverable_IsAHandEdit` if a 30 macro placed on it reads `deliverable.*` (part 06 § 6.3 refuses the read). `EveryDeliverableFile_PassesItsShape`.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** Whether each template reproduces 30's bytes is measured in Task 15, not here; this task writes the forms D8 names.
- [ ] **Step 4: Run; green.**
- [ ] **Step 5: Commit and push.**

### Task 12: The derived flags, and every package through the checker

**Spec:** part 10 § 1.2 (the node-headers row), part 02 §§ 1.1 to 1.3, § 11's last bullet; part 04 § 4 (a flow's format from `ret`); D15; the invariant of *Global Constraints*.

**Files:** `Migrate.Derived.cs`, `V31/SourceDerived*.cs` (the internal entry point), `WorkflowPackageMigration30To31.cs` (the passes in order); `tests/V31Migration/MigrateDerivedTests.cs`, `MigratedPackagesTests.cs`, `tests/Fixtures/ByteEquivalence/migrated/**` (goldens).

**Interfaces:**
- Produces (internal): `SourceDerived.Values(SourcePackage, PackageTypes, Resolution, PackageExpressions, ITemplateReads) : IReadOnlyDictionary<string, DerivedFlags>` (path of the header's file → `Reproducible`, `ModelDerived`, `RunDependent`, `ProfileCarrying`), with no header check; `MigratedPackagesTests.Migrated(package)` for later tasks.

- [ ] **Step 1: Write the failing tests.** `TheFlags_AreTheDerivedOnes` (after `Migrate`, `SourceCheck.Run` on a package with no hand edits says no S03-036, S04-015 or S04-016). `A30ClaimThatDiffers_IsListed` (a 30 node with `reproducible: true` that reads a model's output: `false` written, A10-010 in `Report.Reproducible`). `AFlowsFormat_ComesFromItsRet` (the loop formats of part 04 § 2.8, now exactly the spec's JSON, flags included). **The invariant**, a theory over the 16 packages: `NoHandEdit_MeansPublishRefusesNothing` and `AHandEdit_MeansPublishRefuses` (`CompiledPackage.Publish(files, registry, toolchain)`; on failure the message lists the refusals and the hand edits). `TheMigratedSource_IsTheGolden`, per package: every file under `migrated/<package>/` equals `Migrate`'s output byte for byte, with `notices.json` (`{ "notices", "handEdits", "refusals" }`); no file on disk the result lacks. `MigratingTwice_GivesTheSameBytes`. `NothingUnknownIsWritten` (no S01-023 from any migrated package, hand edits or not).
- [ ] **Step 2: Run; they fail.** Expect the invariant to fail for several packages: each failure is a gap in Tasks 6 to 11 or a construct with no rule.
- [ ] **Step 3: Close the gaps.** For each refusal `Publish` gives on a package with no hand edit: fix the rewrite if the spec gives the 31 form (add the unit test beside its task's tests first); otherwise make it a hand edit with an A10 reason, numbered on. Do not weaken the invariant. If the 31 checker refuses a form the spec says the migration writes (a contradiction between part 10 and what areas 02 to 08 built), stop and record a block on #1507.
- [ ] **Step 4: Run; green. Read every golden under `migrated/`** against the spec's examples (part 01's `test/general` and `example-nested-note` layouts; part 04's loops and `route`; part 06's deliverables). List the packages that have hand edits, with their notices, in this plan under *Results* for Task 16.
- [ ] **Step 5: Commit** (the goldens in their own commit, `Tests (#1507): the sixteen packages, migrated`) and push.

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
