# Area 13: the diagram (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A specVersion 31 package is drawn the same way in three places (`dag.mmd`, the Editor's diagram and the run diagram) from one graph model built from `resolve.json`, on Mermaid 12 with ELK; and the owner's second fix-first group on the migration (a loop's trimmed items, a letter with no signature) is built first.

**Architecture:** One model in the library, `Consultologist.PackageFormat.V31.Diagram`: `DiagramModel.Build` reads the canonical `resolve.json` text into boxes, frames and edges, and `DiagramWriter.Write` turns a model (and, for a run, an overlay of extra label lines, classes and links per box) into Mermaid text. `PackageDiagram.Generate(string resolve)` is the `Func<string, string>` that `PublishOptions.Diagram` has waited for since area 11. The Web references the library already, so the Editor's 31 view and the run diagram call the same code; the run diagram's overlay (`RunDiagram31`) is a pure function from the model and a `Record31`. The 5–30 generators (`WorkflowDagDiagram`, `RunDagDiagram`) keep their text; they only change layout, because the vendored Mermaid moves from 11.16.0 to 12.3.0.

**Tech Stack:** C# / .NET 10, `System.Text.Json.Nodes`, Blazor WebAssembly; Mermaid 12.3.0 (full build, ELK bundled); xUnit (`tests/Consultologist.Api.Tests.csproj`), bUnit (`tests/Consultologist.Web.Tests`), Playwright (`tests/Consultologist.Web.E2E.Tests`).

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`09-engine-and-pages.md`](../09-engine-and-pages.md) § 10 in full (read it before any task of Parts A to C; its three examples in § 10.5 are the reference drawings); [`07-compiled-documents.md`](../07-compiled-documents.md) §§ 2.5, 2.7, 5, 6, 7 (what `resolve.json` holds, where `dag.mmd` is written, that it is a view); [`08-record.md`](../08-record.md) (what the run diagram overlays). Design issue #1398. **The owner's rulings for this area are the comments on #1510** (`gh issue view 1510 --comments`) and, for Part F, [the second fix-first group on #1509](https://github.com/Consultologist/Consultologist-Blazor/issues/1509#issuecomment-7207709261). For Part F also read [`10-migration-and-tools.md`](../10-migration-and-tools.md) §§ 1.2, 3 and [`05-templates-and-macros.md`](../05-templates-and-macros.md) §§ 2.5, 2.6, 3, and area 12's plan, Part F (`area-12-create-and-history.md`), which built the first group and says how the goldens are regenerated.

**Not this area's:** the interactive graph (#1399), Graphviz (#1454), the Serverless Workflow export (#1422); edges inside a function in `resolve.json`'s `graph` (#1527, Milestone 40); the content repo `consultologist-workflows` (read-only: its example packages' committed `dag.mmd` files are the user's, after the bump); editing a 31 package (Milestone 41).

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-13` on `m39-area-13`. The submodules under `external/` are initialised. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin under `external/` moves only to a pushed commit. `~/Projects/consultologist-workflows` is read-only.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo. A shell command naming one of those paths with a `>` in it is refused by the guard: write files with the file tool. Never force-push; a pushed commit is never amended.
- **31 stays off in production:** `WorkflowPackageValidator.AcceptedSpecVersions`, `WorkflowPackageStore.SupportedSpecVersions` and the Editor's `NewestSpecVersion` do not change. Every new Api 31 branch asks `SpecVersionAdmission`.
- **The 5–30 diagrams keep their text:** `WorkflowDagDiagram.Generate` and `RunDagDiagram.Build` are not edited, and none of their tests' expectations change. A 31 path is reached only through `SpecVersion >= 31` (a package) or `SchemaVersion >= 12` (a job).
- **One drawing:** no code outside `V31/Diagram/` writes Mermaid for a 31 package. The run diagram adds to a box (label lines, a class, a link); it never adds, removes or renames a box, a frame or an edge.
- **Labels carry package text and stored words only:** ids, labels, formats, printed conditions, statuses as stored (one vocabulary, #1509), hashes, counts, a check's `failWith`. Never a value from an input, a model or the profile.
- **Every condition, loop line and expression in a label is printed by `JsonLogicPrinter.Print`.**
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
- **Commits:** small, by concern: `PackageFormat (#1510): …`, `Api (#1510): …`, `Web (#1510): …`, `Tests (#1510): …`, `Docs (#1510): …`; in the content repo `Format spec (Consultologist-Blazor#1510): …`. Every message ends with exactly `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task (`git push -u origin m39-area-13` the first time).
- **Tests:** library and Api: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~<Class>"`; Web: `dotnet test tests/Consultologist.Web.Tests`; a task that touches shared code also runs `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`; the browser suite is `.github/scripts/run-e2e.sh` (read its output directly or through `| tail`; never redirect it).
- **Each task ticks its own checkboxes in this file** in its last commit, and adds a line to *Recorded in the build* (at the end) for any choice it took that this plan does not state. A golden that is new or changed is named in the commit message body.
- **A task that cannot be built as written** (the checker refuses the form, the spec and the code disagree) stops and reports to the controller with the evidence; it does not invent a third form.

## Decisions this plan takes

Each goes in the PR body under *Decisions made during build*. R = ruled by the owner; D = where the spec is silent.

| # | Where | Choice |
|---|---|---|
| R1 | ruled by the owner on #1510 (2026-11-21) | A function's inside is drawn from its entry's `nodes` and `reads` in `resolve.json`, not from `graph.edges` (which holds none inside a function). |
| R2 | #1510 (2026-11-26, 2026-11-27) | Area 12 built items 1 to 9 of the first fix-first group (item 3 except a loop's items); this area builds the two items of the second group (Part F). |
| D1 | part 09 § 10.1 "the upgrade lands early … waits for the go-ahead" | The early PR never happened, so the upgrade lands here, on `m39-build`: `wwwroot/lib/mermaid.min.js` becomes Mermaid **12.3.0**'s `dist/mermaid.min.js` from the npm tarball (sha256 `336df803d87a38b70a01e736e666c8f7601d2ce42f5e47c8fafe5d721074b9a3`; it holds ELK, `doc` and `tag-rect`). `mermaid.initialize` also sets `layout: "elk"`, so every diagram the app draws uses ELK whether or not its text has frontmatter. |
| D2 | § 10.1 the frontmatter | `PackageDiagram.Generate` always starts with the five frontmatter lines and `flowchart TD`. Line ends are `\n`; four spaces of indent per nesting level, as § 10.5. |
| D3 | § 10.4 "Mermaid ids"; § 10.5 the function example | Inside a function, a box's id is `fn_<function>__` + its local key with `.` → `__` (as the example), **and a `param.*` pill is `fn_<function>__param__<id>`**, not the example's bare `param__<id>`: two functions with a parameter of the same name would otherwise share one pill across two frames. |
| D4 | § 10.3 "the label names the member read" | An edge's label is the member the reader reads of the source (`value`, `content`, `items`, `state`, `title`, `pass`…): the first step after the source's key in the reader's `reads`. One edge per distinct (source, reader, member). A read with no member (`run.*`, `profile.*`, `param.*`, a renderer's output, a snippet) is unlabelled. |
| D5 | § 10.3 `aligned`; area 05's D40 | `-.-o\|aligned\|` is drawn only from a **check loop's** frame to the production loop's frame. A production loop over another loop's `.items` gets its `for` edge only, although `graph.edges` holds an `aligned` edge for it. |
| D6 | § 10.4, what `graph.edges` lacks | Edges are derived from the entries (`reads`, `in`, `ret`, `renders`, `placements`, `check`, `function`), not copied from `graph.edges`; a test asserts that every `graph.edges` edge between two drawn boxes, other than D5's, is drawn. Reads of `item`, `pass` and `renderer.*` draw no edge. A snippet read draws from a `snippet.x` pill; a loop over a snippet folder gets `-->\|for\|` from that pill. |
| D7 | § 10.4 "Only nodes that use a renderer or a macro are wrapped" | Any entry with `renders` or a placed macro gets a `uses_<box id>` frame: nodes, checks and deliverables alike. The frame's title is `<id> · <label>`. A renderer box is `<box id>__render_<n>` (`n` from `renders`); a chain's later step is fed by the step before it. A macro box is `<box id>__macro_<macro id with . → __>`; a macro's `if` reads come from the placement's `if` and point at the macro box. |
| D8 | § 10.4 labels | In a label `"` is `#quot;`, `<` is `#lt;` and `>` is `#gt;` (a printed `a < b` would otherwise be read as markup), and a line break in package text is one space. The only markup a label holds is the writer's own `<br/>`. |
| D9 | § 10.4 "Model-derived … dashed border" | `classDef modelDerived stroke-dasharray: 5 5` and one `class <ids> modelDerived` line, written only when a box is model-derived. Checks' heavier border is `style checks stroke-width:3px`, written only when the package has a check. |
| D10 | § 10.6, loops | A loop's summary box lists its items (a `while`: its passes) one line each, `<n> · <item name>: <status>`, the first 12, then `… and <k> more`. A box inside a loop body shows a tally of its rows' statuses (`3 Completed · 1 Skipped`) and takes the class of the worst (Failed, then Cancelled, Skipped, NotRun, Completed). Hashes, the renderer and macro sub-rows and a classifier's answer are shown only for a box with exactly one row. |
| D11 | § 10.6 "Classifier: true or false" | A classifier's answer is the `result` of a stored decision whose only read is that node's value; when no decision reads it alone, the box shows no answer. No row text is fetched to draw the diagram. |
| D12 | § 10.6 "clicking it opens that call's rows in History" | Mermaid stays at `securityLevel: "strict"` (which disables `click`), so the link is bound by the app after rendering: the writer's result names, per box, the History anchor (`row-<key>`, `RowLinks31`); the dialog closes and the row scrolls into view. On the Create page, which has no History rows, a call box is not a link. |
| D13 | § 10.1 "where `dag.mmd` lives" | Publish writes `dag.mmd` (the Api passes `PackageDiagram.Generate`). `scripts/check-workflow-package-v31.cs` fails when a committed `dag.mmd` differs from a fresh one and writes it with `--write-dag`; the 30→31 migration script writes `dag.mmd` when the migrated package compiles and says so, and otherwise says that it is written once the hand edits are done. |
| D14 | § 10.1 "the Editor's Graph pane" | `V31PackageView`'s "Diagram" entry draws `PackageDiagram.Generate(Resolve)` when the content holds `Resolve`, and the stored `dag.mmd` only when it does not; the two are the same bytes for a version published from here on. `GET WorkflowPackages/Current/Diagram` answers the same text at 31. |
| D15 | § 10.5 "to be checked … how GitHub renders them tested" | `doc` and `tag-rect` are in Mermaid 12.3.0 (checked in the bundle, and drawn in the browser suite). How GitHub draws them and whether it honours `layout: elk` is **not** tested by this build (it would publish a file outside the repos this build owns): left for the owner. |

Part F's decisions are in Part F.

## Review Focus

1. **A label an author wrote with Mermaid's own syntax in it** (`"`, `<b>`, `]`, `|`, a backtick, `end`, a line break): the diagram still renders, and the text shows as written. Task 2 (writer tests), Task 7 (the browser draws it).
2. **Two things with the same id in different scopes** (a node `draft` at the top level and in a loop; a parameter `draft` in two functions; a node inside a function named like one outside): each has its own box. Task 2, Task 4.
3. **A job that failed early or is still running** (most boxes have no row; a loop has no items; a deliverable has no row): every box is drawn, with `NotRun` or no status, and nothing throws. Task 9.
4. **A large package** (a loop of 40 items in the record; the largest migrated package, `example-macros`): the summary box stays bounded (D10), and the diagram renders in the browser within the E2E timeout. Task 7, Task 9.
5. **A `resolve.json` the model does not expect** (a member missing, a kind it does not know, an older stored version without `renders`): `Generate` draws what it can and never throws at publish; an unknown kind is a plain rectangle. Task 2.

---

## Part F: the second fix-first group (before everything else)

The principle (the owner): **31 stays strict; the migration writes the explicit thing that gives 30's output.** Code: `src/Consultologist.PackageFormat/V31/Migration/` and `…/V31/Harness/`; tests: `tests/V31Migration/`; fixtures: `tests/Fixtures/ByteEquivalence/` (`FX` below). Each item is built with its failing test first.

### Decisions Part F takes

| # | Where | Choice |
|---|---|---|
| F-D1 | item 1 "a `rendered` node for each such loop's `ret`" | For every migrated **`for`** whose `.content` is read anywhere in the migrated package (a prompt, a rendered body, an expression, a deliverable), the migration writes one new `rendered` node as the last node of the loop's body and makes it the loop's `ret`. Its body is `{{ <the old ret's key>.content \| string.strip }}`. This is area 12's option (b), which the ruling chose. The loop's `.content` is then built by the engine from the stripped items, so **the reads of `.content` stay as written** and reach the node through the loop (how this plan reads "reads that node instead"). |
| F-D2 | the new node's header | `kind: rendered`, `producer: computed`, `label`: the old ret's label, `description: null`, `format`: the old ret's text format when it is `markdown_block` or `markdown_inline`, else `plaintext` (so the engine escapes or does not, exactly as for the old ret); `if`: `{"===":[{"var":"<old ret key>.state"},"completed"]}`, so an item whose old ret did not complete is still not completed; the flags and `reproducible` are left to `Derived`. Its id is `<old ret id>_stripped`, fitted with `Renames.Fit` against the taken ids as `ClassifierId` does (same notices). It is not added to `m.Keys` (it has no 30 id). |
| F-D3 | the harness, texts only 31 has | The new node's per-item texts have no 30 counterpart, so `Compare` would count them unexplained. A **case rule** `loop-item-strip-node` (as `macro-choice-classifier` is for the classifier prompts only 31 sends) credits a 31-only render exactly when its node is one the migration wrote under F-D1 **and** its text equals the old ret's text for that item, trimmed. This is not the `loop-items-trimmed` rule the ruling refuses: no text that 30 and 31 both produce is excused by it. Flag it under *For the owner* in the PR. |
| F-D4 | item 2, the form | The dialect refuses the ruled guard in a deliverable and in a snippet (a body `if` never tests a piped value: S05-007, part 05 § 2.5), so the migration uses the nearest accepted form that still tests the trimmed value explicitly, as the ruling allows: **a package renderer**, whose own body may test its value. The migration writes `renderers/signature_after_blank_line.md` with the body below and replaces `\n\n{{ profile.signature }}` by `{{ profile.signature \| renderer.signature_after_blank_line }}`; for a snippet whose signature follows one line ending (`author_line`, `example-v12-constructs`' `closing` where it applies), `renderers/signature_after_line_end.md` with one newline in place of the blank line. A renderer is written only into a package that needs it, and listed in `renderers/index.json`. Ids are fitted with `Renames.Fit`. Never `-` or `~}}` on the read. |
| F-D5 | item 2, bytes with a signature | With a signature the composed text must be what it is today, byte for byte, in every scenario of every package. If the renderer form changes a with-signature text (escaping, a format the renderer cannot declare) or the checker refuses `profile.signature` piped to a renderer at a site, **stop and report**: that is a block for the owner, not a reason to try a fourth form. |

The renderer's body (F-D4), `signature_after_blank_line`; `~` on the guard's own tags, so the guard leaves no lines of its own:

```scriban
{{~ if (value | string.strip) != "" ~}}

{{ value }}
{{~ end ~}}
```

### Task F1: a `for` whose content is read returns each item stripped (item 1)

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Migration/Migrate.Flows.cs` (the `for` case of `WriteFlow`), `Migrated.cs` (a set `StripNodes` of the new nodes' keys, each with its old ret's key, exposed on the result for the harness), `MigrationSentences.cs` (a notice, A10-088)
- Modify: `src/Consultologist.PackageFormat/V31/Harness/ExpectedDifferences.cs`, `ByteEquivalence.cs` (F-D3)
- Test: `tests/V31Migration/MigrateFlowsTests.cs`, `ExpectedDifferencesTests.cs`, `ByteEquivalenceTests.cs`; `tests/V31Source/V31Contract.cs` (`MigrationAreaSentences`, A10-088)

**Interfaces:**
- Produces: `Migrated.StripNodes : IReadOnlyDictionary<string, string>` (new node key → old ret key), carried on the migration result as `StripNodes`; the notice **A10-088**: *"Loop '<id>' returns each item through the new node '<new id>', which strips the item's text as specVersion 30 did."*; the rule id `loop-item-strip-node`.

- [x] **Step 1: failing tests.** In `MigrateFlowsTests`: `AForWhoseContentIsRead_ReturnsItsItemsThroughAStrippingNode` (migrate `composer-sources`: `nodes/topic_loop/index.json` has `ret == "node.topic_loop.topic_gate_stripped"` and `nodes` ending with `topic_gate_stripped`; the file `nodes/topic_loop/topic_gate_stripped.md` has `kind: rendered`, `producer: computed`, `format: markdown_block`, the `if` of F-D2 and the body `{{ node.topic_loop.topic_gate.content | string.strip }}`; a notice A10-088 names both ids); `AForWhoseContentNobodyReads_IsLeftAsItWas` (a loop read only through `.items` or `.value` keeps its ret); `AStrippingNodesId_AvoidsATakenId` (a body that already holds `<ret>_stripped`). In `ByteEquivalenceTests`: `ALoopsItems_AreTrimmedAs30Did` (the seven `composer-sources/awkward-texts` sites of the ruling are equal, with no rule) and `AStrippingNodesText_IsCreditedOnlyWhenItIsTheItemTrimmed` (F-D3, both ways). Replace `AComparedText_NamesTheRulesCredited`'s `tally` expectation by what the ruling gives.
- [x] **Step 2:** run them (`--filter "FullyQualifiedName~V31Migration"`); they fail because no such node is written.
- [x] **Step 3:** implement F-D1 to F-D3. The migrated package must still publish: `MigratedPackagesTests.NoHandEdit_MeansPublishRefusesNothing` and `HandEditsTests.TheEditedPackage_Publishes` stay green for every package (the hand-maintained `FX/hand-edits/composer-sources/` files read `node.topic_loop.content` and need no change under F-D1; check).
- [x] **Step 4:** run `--filter "FullyQualifiedName~V31&FullyQualifiedName!~TheMigratedSource_IsTheGolden&FullyQualifiedName!~TheReport_IsTheGolden&FullyQualifiedName!~TheGolden_IsWhat&FullyQualifiedName!~ThePinnedVectors_AreWhatMigrateGives"`: green.
- [x] **Step 5:** commit (`PackageFormat (#1510): …`, `Tests (#1510): …`), push.

### Task F2: a signature is written through a renderer that tests its trimmed value (item 2)

**Files:**
- Modify: `src/Consultologist.PackageFormat/V31/Migration/Migrate.Deliverables.cs` (`Composition`), `Migrate.Macros.cs` (`AsSnippet`/`SnippetBody`), a new `Migrate.Signature.cs` for writing the renderer files, `MigrationSentences.cs` (A10-089)
- Test: `tests/V31Migration/MigrateDeliverablesTests.cs`, `MigrateMacrosTests.cs` (or where snippets are tested), `ByteEquivalenceTests.cs`; `tests/V31Source/V31Contract.cs` (A10-089)

**Interfaces:**
- Produces: the notice **A10-089**: *"The signature is written through renderer '<id>', which leaves it and the line before it out when the signature is blank, as specVersion 30 did."*

- [ ] **Step 1: failing tests.** `ASignature_IsWrittenThroughTheRendererThatTestsItTrimmed` (replacing `ASignature_IsTheProfilesSignatureAfterABlankLine`: `composer-macros` `deliverables/signed_only.md`'s body is `{{ node.letter_body.content | string.strip }}{{ profile.signature | renderer.signature_after_blank_line }}`, `signed_letter.md` likewise after its `cc_line` read, and `renderers/signature_after_blank_line.md` holds the body above); `ASnippetsSignatureAfterALineEnd_UsesTheLineEndRenderer` (`snippets/author_line.md`); `APackageWithoutASignature_GetsNoRenderer`; in `ByteEquivalenceTests`, `ALetterWithNoSignature_EndsAs30Did` (`composer-macros/no-signature`: `signed_letter` and `signed_only` equal with no rule; `authored` no longer unexplained) and `ALetterWithASignature_IsUnchanged` (every other scenario's texts for those deliverables, and `example-classifier-scope`'s `decline_letter`, equal what the current goldens hold).
- [ ] **Step 2:** run; they fail on the current templates.
- [ ] **Step 3:** implement F-D4. Find the renderer's `input` and `format` that make the with-signature bytes unchanged (F-D5); stop and report if none does.
- [ ] **Step 4:** the same filter as F1 step 4: green; every migrated package still publishes.
- [ ] **Step 5:** commit, push.

### Task F3: one regeneration, the vectors, the documents

**Files:** `FX/migrated/**`, `FX/reports/**`, the condition goldens if they move; the content repo (`v31/conformance/migration-vectors/`, the A10 table of `v31/conformance/README.md`, its chapter 10 twin); `docs/customizable-workflow/package-format-v31/10-migration-and-tools.md` § 1.2 and § 3.2, `docs/customizable-workflow/package-format-library.md` (the deliverables pass row), `FX/README.md`.

- [ ] **Step 1:** `GOLDEN_UPDATE_V31=1 dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Migration"`, then the same without the variable: green except `ThePinnedVectors_AreWhatMigrateGives`.
- [ ] **Step 2:** `CONFORMANCE_EXPORT_V31=$HOME/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~MigrationVectorsExport"`; add A10-088, A10-089 and the rule `loop-item-strip-node` to the content repo's README and chapter 10; `python3 scripts/check-v31.py` there is ok; commit `Format spec (Consultologist-Blazor#1510): …` on `m39-build`, push; move the pin (`git -C external/consultologist-package-format fetch origin m39-build`, check out the pushed commit, `git add external/consultologist-package-format`).
- [ ] **Step 3:** read `git diff --stat -- tests/Fixtures/ByteEquivalence` and every report's counts; **every report's unexplained and partly-explained counts are equal to or lower than before**, the ten sites of the ruling are explained or equal, and nothing else became unexplained. If something did, stop and report.
- [ ] **Step 4:** correct the spec text and the documents to say what is written (the `for` row, the signature row, the new rule), and write the **Part F, what moved** line under *Recorded in the build* (files per package, each report's counts before → after).
- [ ] **Step 5:** `dotnet build Consultologist.sln` and `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`: green. Commit (the goldens named in the body), push.

---

## Part A: Mermaid 12, and the drawing

### Task 1: Mermaid 12.3.0 with ELK (D1)

**Files:**
- Replace: `src/Consultologist.Web/wwwroot/lib/mermaid.min.js` (from `https://registry.npmjs.org/mermaid/-/mermaid-12.3.0.tgz`, `package/dist/mermaid.min.js`; check the sha256 of D1 before committing). Add `src/Consultologist.Web/wwwroot/lib/mermaid.LICENSE.txt` (the tarball's `package/LICENSE`, MIT) and a first line in it naming the version.
- Modify: `src/Consultologist.Web/wwwroot/js/mermaid-interop.js` (`layout: "elk"` in `initialize`)
- Test: `tests/Consultologist.Web.E2E.Tests/Mermaid12E2ETests.cs` (new), a fixture page route or the existing mocked History page as `RunDiagramModalE2ETests` uses

**Interfaces:**
- Produces: for later tasks' E2E tests, a helper `MermaidProbe.RenderAsync(IPage page, string diagram) : Task<string>` (in the E2E project) that calls `window.consultologistMermaid.render(text)` in the served app and returns the SVG, throwing with Mermaid's message when it fails.

- [ ] **Step 1: failing E2E tests** in `Mermaid12E2ETests`: `TheVendoredMermaid_Is12` (`window.mermaid.version` starts with `12.` after `ensure()`); `TheSpecsThreeExamples_Render` (the three diagrams of part 09 § 10.5, copied verbatim into the test as constants, each returns an SVG holding one of its labels, e.g. `macro.letrozole_monitoring`, `call standard_section`, `Every requested section is present`); `ElkLaysOutADiagramWithoutFrontmatter` (no console warning that ELK is missing; the log is read through `page.Console`). The existing `RunDiagramModalE2ETests` stay as they are.
- [ ] **Step 2:** `.github/scripts/run-e2e.sh | tail -40`: the new tests fail on 11.16.0.
- [ ] **Step 3:** vendor the file, set the layout. If an example of § 10.5 does not render on 12.3.0 as written (a shape name, an edge to a frame under ELK), **stop and report** with Mermaid's message: the drawing rules depend on it.
- [ ] **Step 4:** `dotnet test tests/Consultologist.Web.Tests` and `.github/scripts/run-e2e.sh | tail -40`: green (every existing diagram test included).
- [ ] **Step 5:** commit (`Web (#1510): Mermaid 12.3.0, the full build, with ELK`), push.

### Task 2: the model and the writer: production nodes, pills, loops and branches

**Files:**
- Create: `src/Consultologist.PackageFormat/V31/Diagram/DiagramModel.cs` (the records and `Build`), `DiagramModel.Production.cs` (nodes, loops, branches, pills, edges), `DiagramWriter.cs`, `PackageDiagram.cs`
- Test: `tests/V31Diagram/DiagramModelTests.cs`, `DiagramWriterTests.cs`, `DiagramFixture.cs`

**Interfaces:**
- Produces (namespace `Consultologist.PackageFormat.V31.Diagram`):

```csharp
public enum BoxShape { Pill, Rectangle, DoubleBox, Parallelogram, Doc, TagRect, Hexagon }
public enum EdgeStyle { Read, If, Reference, Gate }   // -->, -.->, -.-o, ==>
public sealed record DiagramBox(string Id, string Key, BoxShape Shape, IReadOnlyList<string> Lines, bool ModelDerived);
public sealed record DiagramFrame(string Id, string Title, IReadOnlyList<DiagramBox> Boxes, IReadOnlyList<DiagramFrame> Frames, IReadOnlyList<DiagramEdge> Edges);
public sealed record DiagramEdge(string From, string To, EdgeStyle Style, string? Label);
public sealed record DiagramModel(DiagramFrame Root, IReadOnlyList<DiagramEdge> Edges, bool HasChecks)
{
    public static DiagramModel Build(string resolve);
    public IEnumerable<DiagramBox> AllBoxes();          // every box, any depth
    public static string IdOf(string key);              // "node.a.b" → "node__a__b"
}
public sealed record BoxOverlay(IReadOnlyList<string> Lines, string? Class, string? Anchor);
public sealed record DiagramText(string Mermaid, IReadOnlyDictionary<string, string> Anchors); // box id → History anchor
public static class DiagramWriter
{
    public static string Write(DiagramModel model);
    public static DiagramText Write(DiagramModel model, IReadOnlyDictionary<string, BoxOverlay> overlay,
        IReadOnlyList<(string Name, string Style)> classDefs);   // overlay keyed by DiagramBox.Key
}
public static class PackageDiagram { public static string Generate(string resolve); }
```

  `DiagramBox.Key` is the rooted key a record row carries (`node.sections.paragraphs.draft`, `check.coverage`, `deliverable.note`); a renderer box's key is `<owner key>#render:<n>` and a macro box's `<owner key>#macro:<macro id>`; a pill's is its read (`input.x`, `run.date`). `Frame.Edges` are the edges written inside that frame (a uses frame's, a function's); `Model.Edges` are written after every frame, in the order built. Order is deterministic: `graph.order` for top-level entries, the written `nodes` order inside a body, pills first in order of first read, deliverables last.

- [ ] **Step 1: failing tests** on `DiagramFixture.Resolve(string golden)` (the text of `tests/V31Resolve/Golden/<golden>.resolve.json`) and on small `resolve.json` documents built in the test (a `JsonObject` with only the members the model reads):
  - `Generate_StartsWithTheElkFrontmatter` (the exact six lines of § 10.1);
  - `IdOf_ReplacesEveryDotWithTwoUnderscores` (the three ids of § 10.4);
  - `AnAgentNode_IsARectangleWithItsFormatAndProducer` (`["draft<br/>Drafting<br/>agent → markdown_block · generated"]`), `AComputedNode_IsADoubleBox`, `ARenderedNode_IsARectangle` (third line `rendered → plaintext · computed`), `AnUnknownKind_IsAPlainRectangle`;
  - `AnInput_IsAPillAtTheTopLevel_OncePerPackage`, `ARunFactAndAProfileField_ArePills` (`run__date(["run.date"])`), `ASnippet_IsAPill`;
  - `ARead_IsLabelledWithTheMemberRead` (D4: `-->|"content"|`, `-->|"value"|`, `-->|"items"|`; `run__date --> …` unlabelled);
  - `ANodesIf_IsADottedIfEdgeFromWhatItReads`;
  - `AFor_IsAFrameASummaryBoxAndForAndRetEdges` (on `nested-note`: the frame `for_<id>["<id>: per <list> item"]`, the box's third line `item in <list>`, `-->|"for"|` from the list's box **to the frame**, `-->|"ret"|` from the body's result to the summary box, as § 10.5);
  - `AWhile_CarriesItsConditionAndBound` (`while <printed> — at most 3`), `AnUntil_IsPrintedOnItsLoop` (a built document; third line gains `until <printed>`), with `-.->|"if"|` from what each condition reads;
  - `AnIf_DrawsAFramePerBranch_AndRetNamesTheBranch` and `ACase_DrawsEachBranch_ExceptAnExplicitlyEmptyOne` (on `conditional`: no frame for the `omit` branch; `-->|"ret (narrative)"|`; frame ids `if_<id>_then`, `case_<id>_branch_<n>`);
  - `ADeliverable_IsAHexagon_FedByWhatItsTemplateReads`;
  - `AModelDerivedNode_GetsTheDashedClass` (D9), and no `classDef` line when none is;
  - `ALabelWithMermaidSyntax_IsEscaped` (Review Focus 1: a label `He said "<b>stop</b>" | end ]` and one with a line break; D8's exact output);
  - `TheSameIdInTwoScopes_GetsTwoBoxes` (Review Focus 2);
  - `AResolveWithMembersMissing_StillDraws` (Review Focus 5: an entry with no `label`, no `reads`, no `renders`; `Generate` does not throw);
  - `EveryGraphEdgeBetweenDrawnBoxes_IsDrawn` (D6, over the five goldens, D5's `aligned` excepted; in this task for kinds `reads`, `if`, `for`, `ret`).
- [ ] **Step 2:** run `--filter "FullyQualifiedName~V31Diagram"`: they fail to compile, then fail.
- [ ] **Step 3:** implement. `Build` walks the `JsonObject` (`nodes`, `deliverables`, `inputs`, `graph.order`); it does not use `RunPlan` (the Web calls it, and a stored older `resolve.json` must not be refused by a loader). Call, renderer, macro, function and check handling arrive in Tasks 3 to 5: until then a `call` is a plain rectangle.
- [ ] **Step 4:** run the class; green.
- [ ] **Step 5:** commit (`PackageFormat (#1510): …`), push.

### Task 3: renderers and macros, inside the frame of what uses them (D7)

**Files:** Create `src/Consultologist.PackageFormat/V31/Diagram/DiagramModel.Uses.cs`; Test `tests/V31Diagram/DiagramUsesTests.cs`.

**Interfaces:** Consumes Task 2's records. Produces nothing new: boxes with `BoxShape.Parallelogram` / `Doc` inside frames `uses_<box id>`.

- [ ] **Step 1: failing tests** (on `macros` and built documents): `ARendererUse_IsOneBoxPerUse_InsideTheNodesFrame` (the frame `uses_node__x["x · <label>"]`; the box `node__x__render_1[/"renderer.concept_bullets<br/>concept_list → markdown_block"/]`; the source's edge enters the renderer box with its member label; the renderer box feeds the node, inside the frame, as § 10.5's second example); `AChainOfRenderers_FeedsStepToStep`; `ADirectRead_KeepsAPlainEdge_AndNoFrame`; `AMacro_IsADocBox_PlacedIntoTheNode` (`@{ shape: doc, label: "macro.<id><br/><format> · placed verbatim" }`, `-->|"placed"|`); `AMacrosIf_PointsAtTheMacroBox_NeverAtTheNode`; `AMacroFolderPlacement_IsOneBox`; `AUsesFrameInsideALoop_SitsInTheLoopsFrame`; `ADeliverableThatUsesARenderer_IsFramedToo`; `AnOlderResolveWithoutRenders_DrawsNoFrame`.
- [ ] **Step 2 to 5:** fail, implement, pass, commit and push, as Task 2.

### Task 4: calls and functions (R1, D3)

**Files:** Create `DiagramModel.Functions.cs`; Test `tests/V31Diagram/DiagramFunctionsTests.cs`.

- [ ] **Step 1: failing tests** (on `functions` and built documents): `ACall_IsATagRect_WithItsArgumentsRunningIn` (`@{ shape: tag-rect, label: "hpi_section<br/>call standard_section" }`; an edge from each `with` argument's source); `ACall_RefersToItsFunctionsFrame` (`-.-o|"calls"|` to `fn_<function>`); `AFunction_IsDrawnOnce_HoweverManyCalls`; `AFunctionsFrame_NamesItsInputsAndResult` (`"function standard_section (standard, draft) → titled_section"`, inputs in `inputOrder`); `AFunctionsInside_IsDrawnFromItsNodesAndReads` (R1: pills `fn_<f>__param__<x>(["param.x"])`, boxes `fn_<f>__node__<id>`, the edges between its own nodes from their `reads`, `-->|"ret"|`… as the body's result reaches the function's `ret`: the example draws the read into the `ret` node and no extra edge, so assert that); `AParameterOfTheSameNameInTwoFunctions_GetsTwoPills` (D3); `AFunctionsLoopsAndUses_AreDrawnAsOutside` (a loop and a renderer use inside a function body: Task 2's and 3's rules, ids prefixed); `EveryGraphEdgeBetweenDrawnBoxes_IsDrawn` now covers `calls`.
- [ ] **Step 2 to 5:** as Task 2.

### Task 5: checks, check loops, gates

**Files:** Create `DiagramModel.Checks.cs`; Test `tests/V31Diagram/DiagramChecksTests.cs`.

- [ ] **Step 1: failing tests** (on `checked-note`, `functions`): `EveryCheck_IsInsideTheChecksFrame_ApartFromProduction` (`subgraph checks["Checks"]`, holding every `check.*` box; `style checks stroke-width:3px` last, and absent for a package with no check); `ACheck_IsADoubleBox_WithOrdinaryReadEdges` (`check__coverage[["coverage<br/>…<br/>computed → check_result"]]`; a check reading a deliverable: `deliverable__x -->|"content"| check__y`); `ACheckLoop_IsAFrameInsideChecks_AlignedWithItsProductionLoop` (title `<id>: per <list> (aligned)`; `-.-o|"aligned"|` from the check loop's frame to `for_node__<loop>`; `-->|"for"|` from the list); `AProductionLoopOverItems_GetsNoAlignedEdge` (D5); `AGate_IsTheThickEdgeFromTheCheckToItsDeliverable` (`==>|"gates"|`); `EveryGraphEdgeBetweenDrawnBoxes_IsDrawn` now covers `aligned` (check loops) and `gates`.
- [ ] **Step 2 to 5:** as Task 2.

### Task 6: the goldens

**Files:** Create `tests/V31Diagram/Golden/<name>.dag.mmd` for `nested-note`, `conditional`, `functions`, `checked-note`, `macros`, and for the migrated packages `example-v12-constructs`, `composer-sources`, `example-macros` (compiled in the test as `MigratedPackagesTests.Publish` does); Test `tests/V31Diagram/DiagramGoldenTests.cs`.

- [ ] **Step 1:** `TheDiagram_IsTheGolden` (a theory over the eight; `GOLDEN_UPDATE_V31=1` writes, as the other 31 goldens do) and `TheSpecsExample_IsDrawnAsTheSpecDrawsIt` (a built `resolve.json` for § 10.5's first example, abridged as the spec says; the lines of the spec's drawing are each present, D3 and D4 allowing).
- [ ] **Step 2:** generate, **read each golden in full** against § 10.2 to § 10.4, fix the model where a drawing is wrong (with a failing unit test first), regenerate.
- [ ] **Step 3:** run `--filter "FullyQualifiedName~V31Diagram"`: green. Commit (each golden named in the body), push.

---

## Part B: where the drawing goes

### Task 7: publish, the route, the tools; and the browser draws every golden (D13, D14)

**Files:**
- Modify: `src/Consultologist.Api/V31/V31PackagePublisher.cs` (`OptionsOf`: `Diagram: PackageDiagram.Generate`), `src/Consultologist.Api/Workflow/WorkflowPackages.cs` (the `Current/Diagram` function's 31 branch, behind `SpecVersionAdmission`), `scripts/check-workflow-package-v31.cs`, `scripts/rerender-workflow-packages-v31.cs` if it publishes, `src/Consultologist.PackageFormat/Migration31Script.cs` (`NoDiagram`)
- Test: `tests/V31Engine/V31PackagePublisherTests.cs` (the two assertions that say no diagram), a `Current/Diagram` test beside the content route's 31 tests, `tests/V31Migration/MigrateScriptTests.cs`; `tests/Consultologist.Web.E2E.Tests/Mermaid12E2ETests.cs`

- [ ] **Step 1: failing tests:** `APublishedPackage_HoldsItsDiagram` (the stored `dag.mmd` equals `PackageDiagram.Generate` of the stored `resolve.json`); `TheDiagramRoute_AnswersThe31Drawing` and `…_RefusesWhen31IsNotAdmitted` (S09-001, as the content route); `TheMigrationScript_WritesTheDiagram_WhenThePackageCompiles` and `…_SaysWhenItCannotYet`; in the E2E project `EveryGoldenDiagram_RendersInTheBrowser` (each file of `tests/V31Diagram/Golden/`, linked into the E2E project as content, through `MermaidProbe.RenderAsync`: an SVG, no error; Review Focus 1 and 4: also a generated diagram whose labels hold Mermaid syntax, built by a tiny document in the test through `PackageDiagram.Generate`).
- [ ] **Step 2:** run; fail.
- [ ] **Step 3:** implement. If a golden does not render in Mermaid 12, the writer is wrong, not the test: fix the writer with a unit test, regenerate the goldens.
- [ ] **Step 4:** the V31 filter, `dotnet test tests/Consultologist.Web.Tests`, `.github/scripts/run-e2e.sh | tail -40`: green.
- [ ] **Step 5:** commit (`Api (#1510): …`, `Tests (#1510): …`), push.

### Task 8: the Editor's 31 view draws from `resolve.json` (D14)

**Files:** Modify `src/Consultologist.Web/Shared/WorkflowEditor/V31/V31PackageView.razor`; Test `tests/Consultologist.Web.Tests/V31/TemplatesV31Tests.cs`.

- [ ] **Step 1: failing tests:** `TheDiagramEntry_DrawsThePackageFromItsCompiledDocument` (`WorkflowDagView.Instance.Diagram` equals `PackageDiagram.Generate(resolve text)`), `…_FallsBackToTheStoredFile_WhenThereIsNoCompiledDocument`, `…_IsAbsentWithNeither` (the existing `TheDiagramEntry_AppearsOnlyWithADagFile` is rewritten into these).
- [ ] **Step 2 to 5:** fail, implement, `dotnet test tests/Consultologist.Web.Tests`, commit (`Web (#1510): …`), push.

---

## Part C: the run diagram

### Task 9: the record laid over the drawing (pure) (D10, D11)

**Files:** Create `src/Consultologist.Web/Services/Provenance/V31/RunDiagram31.cs`; Test `tests/Consultologist.Web.Tests/V31/RunDiagram31Tests.cs`.

**Interfaces:**
- Consumes: `DiagramModel.Build`, `DiagramWriter.Write(model, overlay, classDefs)`, `BoxOverlay`, `DiagramText` (Task 2); `Record31`, `Row31`, `RowLinks31` (area 12).
- Produces: `public static DiagramText RunDiagram31.Build(string resolve, Record31 record, bool linkCalls)`. Classes: `done`, `skipped`, `ranNot`, `failed`, `running`, with `RunDagDiagram`'s colours (so `RunDagOverlay`'s legend holds).

- [ ] **Step 1: failing tests**, on a `resolve.json` golden and rows built in the test with the stored members (`RecordRows.cs` is the shape): 
  - `TheRunDiagram_HasTheBoxesFramesAndEdgesOfThePackageDiagram` (strip the overlay's lines and `class` lines: the same ids and the same edge lines as `PackageDiagram.Generate`);
  - `ANode_ShowsItsStoredStatusItsOutputHashAndItsRetries` (status word verbatim; the hash shortened as History shortens it; `retries 2` only when the row's `retries` is not empty); 
  - `ALoop_ListsItsItemsByNameWithTheirStatus` and `…_StopsAtTwelve` (D10), `AWhile_ListsItsPasses`; `ABoxInsideALoop_ShowsATallyAndTheWorstClass`;
  - `ARendererBox_ShowsThatUsesInputAndOutputHashes`, `AMacroBox_SaysOfferedAndPlacedAndItsContentHash` (from the row's `renders` and `macros`);
  - `AClassifier_ShowsTheAnswerADecisionRecorded` and `…_ShowsNothingWithoutOne` (D11);
  - `AConditionSummaryBox_ShowsTheDecisionAndThePrintedCondition` (an `if`: the decision's `result`; a `case`: the arm as stored);
  - `ACallBox_ShowsItsStatusAndHashes_AndLinksToItsRows` (`Anchors[<box id>] == "row-<key>"` when `linkCalls`, none otherwise); `TheFunctionsFrame_ShowsNoStatus`;
  - `ACheck_ShowsPassOrFail`, `ACheckLoop_ListsItsItems`, `AnAggregate_ShowsItsResult_AndItsFailWithWhenItFailed`;
  - `ADeliverable_ShowsItsStoredState` (released, held, failed, skipped, blocked);
  - `AJobWithNoRows_DrawsEveryBoxAsNotRun` and `ADeliverableWithNoRow_ShowsNoState` (Review Focus 3); `NoLabelCarriesARowsText` (a row given a `text` member: it appears nowhere).
- [ ] **Step 2 to 5:** fail, implement, `dotnet test tests/Consultologist.Web.Tests`, commit, push.

### Task 10: the run diagram on History and on the Create page (D12)

**Files:**
- Modify: `src/Consultologist.Web/Pages/History.razor` (the "Run diagram" button for a storage 12 job, beside `RecordView31`), `src/Consultologist.Web/Shared/Create31/RunView31.razor` (the same button; rows are fetched with `GetJobRowsAsync` when the dialog opens), `src/Consultologist.Web/Shared/WorkflowEditor/RunDagOverlay.razor` (a second way in: a `DiagramText` instead of a 5–30 `detail`; its legend unchanged), `src/Consultologist.Web/Shared/WorkflowEditor/WorkflowDagView.razor` and `wwwroot/js/mermaid-interop.js` (an optional `Anchors` map: after rendering, each named box gets `cursor: pointer`, `role="link"`, `tabindex="0"` and a click and Enter handler that calls back)
- Test: `tests/Consultologist.Web.Tests/V31/RunDiagram31PageTests.cs`; `tests/Consultologist.Web.E2E.Tests/RunDiagram31E2ETests.cs` (on the mocked 31 History page area 12's E2E tests use)

- [ ] **Step 1: failing tests.** bUnit: `AStorage12Job_OffersTheRunDiagram` (History), `TheOverlay_DrawsRunDiagram31sText`, `AStorage11Job_KeepsTodaysRunDiagram` (`RunDagDiagram.Build`'s text, unchanged), `TheCreatePage_OffersTheRunDiagram_AndFetchesTheRowsOnOpening`, `WithoutTheCompiledDocument_TheButtonIsNotShown`. E2E: `TheRunDiagramOfA31Job_Renders` (an SVG holding a status word and a call box) and `ClickingACallBox_ClosesTheDialogAndShowsThatCallsRows` (the element `#row-<key>` is in the viewport and focused or scrolled to), `ACallBox_IsReachableByKeyboard`.
- [ ] **Step 2 to 4:** fail, implement, `dotnet test tests/Consultologist.Web.Tests` and `.github/scripts/run-e2e.sh | tail -40` green.
- [ ] **Step 5:** commit (`Web (#1510): …`), push.

---

## Part D: the documents

### Task 11: the documents and the self-check

**Files:** `docs/customizable-workflow/engine-v31.md` (publish writes `dag.mmd`; the `Current/Diagram` route at 31), `docs/customizable-workflow/package-format-library.md` (a section for `V31/Diagram`), `docs/customizable-workflow/package-format-v31/09-engine-and-pages.md` § 10 (corrected where a decision above changes its text: D1's "lands here", D3's parameter pill id in the example, D5, D15's "checked against 12.3.0"), the content repo's twin of part 09 § 10 if it has one (then a commit on its `m39-build`, pushed, and the pin moved), `docs/README.md` if it indexes the library document's sections, `src/Consultologist.PackageFormat/Migration31Script.cs`'s usage text if Task 7 changed what it says.

- [ ] **Step 1:** write them; every statement checked against the code as built.
- [ ] **Step 2:** the self-check: each row of *Spec coverage* below names a task whose tests exist and pass; each decision D1 to D15 and F-D1 to F-D5 is either built as written or corrected here and in *Recorded in the build*.
- [ ] **Step 3:** `dotnet build Consultologist.sln`; `dotnet test Consultologist.sln --filter "FullyQualifiedName!~Consultologist.Web.E2E.Tests"`; `.github/scripts/run-e2e.sh | tail -40`. Commit (`Docs (#1510): …`), push.

## Spec coverage

| Spec | Task |
|---|---|
| § 10.1 one drawing, three places | 2 (the model), 7 (`dag.mmd`), 8 (the Editor), 9 and 10 (the run diagram) |
| § 10.1 Mermaid 12, the full build, ELK; the upgrade | 1 |
| § 10.1 `dag.mmd` declares ELK | 2 |
| § 10.1 where `dag.mmd` lives; a view, not pinned | 7 (publish, the tools; nothing hashes it: unchanged) |
| § 10.1 no "show renderers" toggle | 3 (frames always drawn; no option exists) |
| § 10.2 shapes: pills (`input`, `snippet`, `profile`, `run`) | 2; `param` | 4 |
| § 10.2 agent, `computed`, loop and branch summary boxes, deliverable | 2 |
| § 10.2 renderer, macro | 3; `call` | 4 |
| § 10.2 frames: loop body, branch | 2; uses | 3; function | 4; Checks | 5 |
| § 10.3 read, condition read, `for`, `ret` | 2; `placed` | 3; `calls` | 4; `aligned`, `gates` | 5 |
| § 10.4 labels, the one printer, quotes | 2 |
| § 10.4 Mermaid ids and frame prefixes | 2 (`for_`, `while_`, `if_`, `case_`), 3 (`uses_`), 4 (`fn_`), 5 (`checks`) |
| § 10.4 each construct's rule | 2 (agent, computed, rendered, loops, `if`/`case`, model-derived, deliverable), 3 (renderer use, macro), 4 (call, function), 5 (checks, check loop, gate) |
| § 10.4 where pills go | 2, 4 |
| § 10.5 the examples; shape names checked against Mermaid 12 | 1 (they render), 6 (drawn as the spec draws them), D15 |
| § 10.6 the run diagram, each row of its table | 9; the call box's link | 10 |
| § 10.6 a storage 2–11 record keeps today's run diagram | 10 |
| § 10.7 later | not built (Not this area's) |
| #1510: the function's inside from `nodes` and `reads` | 4 (R1) |
| #1509 second group, item 1 (a loop's trimmed items) | F1, F3 |
| #1509 second group, item 2 (no signature block) | F2, F3 |
| #1510: say which items were built where | the PR body (R2) |

## Recorded in the build

(Each task adds its line here.)

- **Task F1:** ruled by the controller (2026-11-27): the new node reads the old `ret` as the body sees its sibling, by bare id (a rooted key is refused inside the loop's own body, S03-014): its `if` is `{"===":[{"var":"node.<old ret id>.state"},"completed"]}` and its body `{{ node.<old ret id>.content | string.strip }}`; the loop's `ret` keeps the rooted key (`node.topic_loop.topic_gate_stripped`). F-D2's `if` and F-D1's body are corrected so.
- **Task F1:** ruled by the controller: `composer-sources/awkward-texts`' `deliverable.tally` is not equal; it is fully explained by `escaped-plain-text` alone (its count now 30's). `ALoopsItems_AreTrimmedAs30Did` asserts six sites equal with no rule and `tally` expected by exactly that rule; `AComparedText_NamesTheRulesCredited` expects `(Equal false, Expected true, Unexplained false)`, rules `["escaped-plain-text"]`.
- **Task F1:** ruled by the controller: the hand-edit packages whose hand-maintained loop header overrides the migrated one (`example-problem-list`, `test/general`) get the stripping node in their `nodes` and `ret`, said in their READMEs; `example-problem-list`'s, `applies_stripped`, strips the edit's own `ret`, `applies`, and the migration's `section_draft_stripped` is deleted (`.deleted`). A new hand-edit file, `strip-nodes.json` (not a package file, like `keys.json`), lays that node over `StripNodes` so its texts are credited by `loop-item-strip-node` (`MigrationFixture.Overlay`; a stripping node whose file is deleted leaves `StripNodes`).
- **Task F1:** F-D2's "the old ret's text format" is the text kind of its `.content` as the checker types it (`ScopeTree.RenderedContent`): a `for`'s is `markdown_block`, a `while`'s its `ret`'s, an `if`'s or `case`'s its `ret`s' when one kind; else `plaintext` (so an `if` of a Markdown and a plain branch under a `for` gives `plaintext` items: a known limit, no fixture capture has it). Accepted by the controller.
- **Task F1:** a read of a loop's `.content` is detected by a regex over every migrated file (header JSON and body) for the loop's rooted key or its outward-resolving suffix forms, followed by `.content`. Accepted by the controller.
- **Task F1:** the new node is written with the placeholder `reproducible: true` and `false` flags, which `Derived` rewrites. Accepted by the controller.
- **Task F1:** the node is written by a pass of its own, `Migrate.StripLoopItems`, after `Deliverables` and before `Derived` (not in `WriteFlow`'s `for` case, as the plan's file list says), because a loop's readers are known only once the deliverables are written; it repeats until no loop is left, so a loop returned by a loop is stripped at each level (the new node reads the inner loop's `.content`), and the formats of the loop and of the flows returning it follow the new `ret` (`Reformat`). The id is fitted by `FreeNodeId`, which `ClassifierId` now calls. `ByteEquivalence.cs` needed no change: the rule reads `Migration31Result.StripNodes` through `DifferenceContext.Migrated`.
- **Task F1:** consequences in other tests: `example-nested-note` opens 120,814 instances (was 80,614), so the tests of a configured larger limit use `JobInstances` 200,000 (was 100,000); `macro-trailing-whitespace` loses its only golden hit (edge-unruled-whitespace's `problem_close`, now stripped with each item) and is listed in `RulesWithoutAHit`; `edge-tests/edge-unruled-whitespace.json`'s `expect` for `deliverable.problems` is 30's text between the two items. The part 04 § 2.8 spec examples no longer match the migrated loops (F3 corrects them).
- **Task F1:** `HandEditsTests.EachNotice_IsAnswered(example-nested-note)` holds the README to the golden `FX/migrated/example-nested-note/notices.json` as well as to the migration: its README says the migration's 120,814, the golden still 80,614, so it stays red with the goldens until F3 regenerates them (Step 4's filter is otherwise green).
- **Task F1 (fix round 1):** ruled by the controller, replacing the `plaintext` fallback recorded above. When the old `ret`'s `.content` has no single text kind (an `if` or `case` whose branches are Markdown and plain text), no stripping node is written: the loop keeps its `ret` and its format (bytes as before F1), and the migration says the hand edit **A10-090** on the loop's `index.json`, member `ret`: *"loop '<id>' returns items of more than one text format, so its items are not stripped as specVersion 30 stripped them; strip each branch's text where it is written."* It is written in lower case, as every A10 reason after `Hand edit needed in <file>, <member>:` is. No fixture has the shape, so no hand-edit README answers it. This hand edit is one publish does not refuse (part 10 § 1.4 says a hand edit is written in a form the checker refuses; `AHandEdit_MeansPublishRefuses` would fail for a fixture with the shape): for the owner.
