# Area 05: Resolver and Lock (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Consultologist.PackageFormat` the resolver of specVersion 31: every reference of a package source resolved to its rooted key and typed, part 04's publish-time rules, the derived values (`reproducible` and the three flags) held against the headers, and the two compiled documents, `resolve.json` and `lock.json`, with the checks an engine makes of them; nothing in the engine calls any of it yet.

**Architecture:** New files beside areas 02 to 04's in `src/Consultologist.PackageFormat/V31/`. `SourceResolve` builds a `ScopeTree` (who encloses whom) from the parsed package and the types, judges every reference string and flow rule, and hands `SourceExpressions.Check` the real `IExpressionScopes`, so areas 04's deferred reads become typed. `SourceDerived` then works out reads, `reproducible` and the flags over the resolved graph. `ResolveCompiler` writes `resolve.json` from all of it as a `JsonObject`, canonical by `CanonicalJson`; `LockCompiler` writes `lock.json` from a `Toolchain`; `CompiledPackage` pairs, hashes and verifies them; `RequiresCheck` is what an engine asks before a job. What Scriban bodies read is area 07's: it comes through one seam, `ITemplateReads`, and until it is supplied a body's reads are *unknown*, not empty (D1).

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), the services of areas 02 to 04 (`SourceParser`, `FormatSet`, `FormatTypes`, `FormatReads`, `TypeStrings`, `ExpressionChecker`, `LineageFlags`, `CanonicalJson`), xUnit in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`07-compiled-documents.md`](../07-compiled-documents.md) §§ 1 to 4 and 7 (and § 10's sentences for them); [`04-loops-and-functions.md`](../04-loops-and-functions.md) §§ 2 to 7 and 10; [`01-files-and-scopes.md`](../01-files-and-scopes.md) § 9; with [`03-expressions.md`](../03-expressions.md) §§ 4.1 to 4.6 and [`02-types-and-inputs.md`](../02-types-and-inputs.md) §§ 1.1 to 1.3, 2.2 for what the resolver derives; [`refusal-sentences.md`](../refusal-sentences.md). The published contract is `external/consultologist-package-format/v31/` (`sentences.json`, `constants.json`, `schemas/resolve.schema.json`, `schemas/lock.schema.json`, `conformance/`). Area issue #1502, parent #1496, runbook [`BUILD-RUNBOOK.md`](../BUILD-RUNBOOK.md). What exists is described in [`package-format-library.md`](../../package-format-library.md) (the three "specVersion 31" sections) and in the plans of areas 02 to 04 (their D-numbers are cited as "04-D2"). **Read the spec sections a task names, and the guide's sections, before writing its tests.**

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-05` on `m39-area-05`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin under `external/` moves only to a pushed commit.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo.
- **31 stays off:** no file under `src/Consultologist.Api`, `src/Consultologist.Web`, `src/Consultologist.Admin` or `src/Consultologist.UI` changes; no accepted-version set changes. Everything is reachable only from tests.
- **Nothing the engine at 30 reads changes:** outside `V31/`, the only edits are tests and docs.
- **Sentences are used as written.** A sentence with a row in `v31/sentences.json` is that row character for character with its `<…>` filled in, under that row's id; `V31Contract.AssertSentence` holds each. A rule with no row gets an area sentence `A05-NNN` (table below), added to `V31Contract` and to the conformance README's list, and listed in the PR.
- **No sentence carries a value** from an input or a model: only ids, keys, paths, member names, type strings (`TypeStrings.Print`), counts, limits and package text.
- **Never throw on package content.** Every public method that takes a package, a reference, a compiled document or bytes returns for anything; `ArgumentNullException` for a null argument is the only exception. Recursion over package structure is bounded (the reader bounds nesting; the call graph is walked with a visited set).
- **Treat model JSON as read-only:** `DeepClone` before placing a node of the source model in a new tree.
- **Deterministic:** the same source and the same toolchain give the same bytes. No clock, no `Guid`, no dictionary-order dependence: iterate in source order or ordinal-sorted order only.
- **Invariant culture, ordinal comparison,** everywhere.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample).
- **Goldens come from code,** are read, and are listed in the PR.
- **Commits:** small, by concern: `PackageFormat (#1502): …`, `Tests (#1502): …`, `Docs (#1502): …`; in the content repo `Format spec (Consultologist-Blazor#1502): …`. Every message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task.
- **Tests:** `tests/V31Resolve/`, namespace `Consultologist.Api.Tests.V31Resolve`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"` (areas 02 to 04's too, which must stay green).
- **Refusal packages.** A new refusal sentence a package can reach gets a case in `tests/V31Source/RefusalPackages.Resolve.cs` (a `partial` of `RefusalPackages`, added to `All()`), in the style of the existing cases: `Minimal()` plus the one construct, the invalid side one text replacement away, exactly one refusal. A task that adds or changes cases re-exports (`CONFORMANCE_EXPORT_V31=~/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport"`), reads the diff, commits and pushes there, runs that repo's `scripts/check-v31.py`, and moves the pin, in the same task. A sentence no package can reach yet (it needs template reads) is listed in `RefusalPackagesTests` as not shipped, with the reason.

## Decisions this plan takes where the spec is silent

Each goes in the PR body under *Decisions made during build*.

| # | Where | Choice |
|---|---|---|
| D1 | part 07 § 2.5 `reads`, `places`, `renders`, `runSpots`; part 03 §§ 4.4–4.6 | What a Scriban body reads is area 07's (the dialect), a check's and a deliverable's rules area 08's. They reach the resolver through `ITemplateReads`. Without it (this area's state) a non-empty body's reads are **unknown**: the entry's `reads` hold its header's reads only, `places`, `renders` and `runSpots` are `[]`, a flag that depends on an unknown body is not judged and is written as declared, and `ResolveResult.Complete` is `false`. Rules that need body reads (S04-035's "read from a body node", S04-052 and S04-074 for snippets, macros and renderers, S04-063, S04-069's prompt and part, `needs.profile` and `run` from bodies, the features `macro-markers` and `run-spots`, built-in renderers in `lock.builtins`) are built against the seam and tested with a stub provider. |
| D2 | sentences that state one rule twice | A flow node's format: S02-015/S02-016 (area 03 says them; S04-014 is not said). A flow node's or call's `reproducible`: S04-015; a leaf's stays S01-027. A flow node's or call's flag: S04-016; a leaf's or check's: S03-036 (04-D3). A function's format: S04-061; a call's: S04-070. A call with a body: the reader's sentence stays if it already refuses it, else S04-071. |
| D3 | part 01 § 9.3 "the resolver writes each resolved reference as its key" | In `resolve.json` every `node.<id>` and `check.<id>` at the start of a reference string or a `var` / `missing` path is written with the resolved key (`node.sections.outline.value.points`); `pass.node.<id>`, `pass.previous.node.<id>` and `item.nodes.<id>` keep the bare id (they are members, not references). Inside a function keys are local to it. |
| D4 | part 07 § 2.5 `reads` "canonical and sorted" | A read through `node`, `check`, `input` or `param` is the key and its first member (`node.plan.value`, `input.consult_draft.content`, `node.sections.items`); a read through `item`, `pass`, `run` or `profile` is the path as written. Distinct, ordinal-sorted. |
| D5 | part 07 § 2.4 bundle; `resolve.schema.json` (`$defs` keys are ids) | A function's own format is bundled under `<function>__<format>` with `$id` `urn:consultologist:package:<name>@<version>:function:<function>:format:<format>`; a collision with another bundle key is A05-004. A package format's `$id` is `urn:consultologist:package:<name>@<version>:format:<id>`. `$schema` is added to a package format when absent. |
| D6 | part 07 § 2.2, § 2.5 `functions` | A function's entry: its index members as written (`label`, `description`, `inputs` with formats rewritten, `uses`, `format`), `inputOrder`, `ret` (its key), `body` (its top-level children's keys in order; the index's `nodes` list), `nodes` (the flat map, as the schema requires), `renderers`, `snippets`, `macros` (its own, keyed by rooted key), `reproducible`, `formatHash`, `typeHash`. |
| D7 | part 07 § 2.7 `graph` | Edges, `from` what is read, `to` the reader, keys without members: `reads` (a content read of a node, check, input, `run.<fact>` or `profile.<field>`), `if` (any condition's read, and a `case` subject), `for` (a loop's source), `ret` (the `ret` child to its flow node), `calls` (`function.<id>` to the call node), `aligned` (a production loop to the loop that iterates its `.items`), `placed` and `gates` (areas 07 and 08, through the seam and the deliverable's `check`; `gates` is written here from a deliverable's `check` member when it names a check). Distinct, in the order found. `order` is a topological order of every key of `inputs`, `nodes`, `checks` and `deliverables`, ties by that source order; `run.*`, `profile.*` and `function.*` are not in it. A cycle among nodes is A05-003, so the order exists. *Amended during the build:* `order` is level by level: a flow node comes before its body, and each level is in a topological order of the edges lifted to it, ties by source order; edges inside a body (`ret`, and a `while`'s condition reading its own body) order only that body's level. The graph holds no edges between a function's own nodes (its keys are local to it): only `calls` from `function.<id>` to each call outside every function. |
| D8 | part 07 § 3.1 | `requires.evaluator` is `^` + `JsonLogicOperations.EvaluatorVersion`; `scriban` is `^` + the locked Scriban version; each operation used anywhere is `^` + its version; `kinds` and `features` are in `constants.json`'s order; `features`: `parallel` (a `for` with `parallel: true`), `check-loops` (a `for` under `checks/`), `parts` (an agent with a non-empty `parts`), `macro-markers` and `run-spots` from the seam; `agents` is each bound agent, name to version as a string. |
| D9 | part 07 § 4.1 | `Toolchain` is the caller's: the registry release (`package-format@v…`), the catalogue versions and the engine's library versions. `Toolchain.Current(release, formats, agents, outputContracts)` reads the four library versions from the loaded assemblies (`Scriban`, `Markdig`, `Corvus.Text.Json.JsonLogic`, and `Corvus.Text.Json.Validator` under the key `Corvus.JsonSchema`, 03-D1), `Environment.Version` for `runtime.dotnet`, and `runtime.unicode` from a table by .NET major version (10 → `16.0`; an unknown major is `unknown`). `resolver.dialect` is `constants.json`'s `dialects.templates`. `outputContracts` is `null` at 31 unless the caller names one. |
| D10 | part 07 §§ 3.1, 4.2, 4.4 "the engine checks" | The checks are pure functions here (`RequiresCheck`, `CompiledPackage.Verify`); calling them at load and at job start is area 06's. A caret range `^a.b.c` admits `>= a.b.c` and `< (a+1).0.0`. A break point is `(component, version)`: a package whose locked version is below it, with the engine at or above it, is refused with S07-003. |
| D11 | part 04 § 10.2 notes | The notes need the engine's numbers, so they take a `PublishLimits(Ceiling, ItemsPerLoop, CallsPerJob)` from the caller; with none, no note is given. A list "could hold more than the limit" unless its length is known at publish (a snippet folder). *Amended during the build:* the `maxItems` clause is dropped: the formats dialect refuses the keyword (S02-021), so only a snippet folder's known length bounds a list. |
| D12 | part 04 § 7.3 `uses` (03-D36) | `uses.formats` may list a built-in or catalogue name (they are "always available"); listed and never named, it gets S04-074 like any other. |
| D13 | part 04 § 2.2, § 5.2, § 7.7 references | A reference string is a path by `JsonLogicShape.IsPath`, with no position segment. One that is not is A05-001; one whose head names nothing is S03-014 with the site worded as 04-D4 (`Loop 'x'`, `Case 'x'`, `Call 'x'`); a member the thing does not have is A05-002. A `with` value that is not text and not `null` is S04-017 when it is an object or array, else A05-005. |
| D14 | part 04 §§ 2.5, 2.6, 3.3, 3.4 member types | `.state` is `string {completed, skipped, not_run}`; a `for`'s `.stopped` `string {until, bound, end}`, a `while`'s `string {check, until, bound}`; `.passes` and `pass.number` `integer`; `.items` a list of the element of § 2.6 (an inline object schema built by `ScopeTree`, `nodes` an object keyed by body id, each with `value`, `content`, `state`); `.history` and `pass.history` a list read only through `size` (04's S03 row stays); `item.id` `string`; a `.value` or `.content` of a node that may be skipped (an `if` with no `else`, a `case` with an empty branch, a leaf with an `if`, anything inside a `checkFirst` while, read from outside) is nullable. |

**Area sentences** (ids not in `sentences.json`):

| Id | Sentence |
|---|---|
| A05-001 | `<Kind> '<id>' <member> '<text>' is not a reference; a reference is a root, then names joined by dots.` |
| A05-002 | `<site> reads <reference>; a <kind> has no <member> (it has <members>).` |
| A05-003 | `Node '<a>' reads node '<b>', which reads node '<a>': nodes can't read each other in a circle. Break the circle.` (a longer circle is named in full, as S04-054 is) |
| A05-004 | `Function '<f>' format '<id>' is compiled as '<f>__<id>', which the package already uses: rename one.` |
| A05-005 | `Call '<id>' with <param> is <what it is>; an argument is a reference, written as text, or null.` |

Before using an A05 sentence, search `sentences.json` for a row that says the rule; a row found is used instead and the table above amended.

## Files

```
src/Consultologist.PackageFormat/V31/
  ReferenceString.cs     a reference string parsed: root, id, member, fields                      Task 1
  ScopeTree.cs           frames (package, check, function, loop, while, branch), visibility,
                         a thing's members and their types (D14)                                  Task 1
  ResolvedScope.cs       IExpressionScope / IExpressionScopes over the tree                       Task 1
  ResolveSentences.cs    one method per S04 / S07 / A05 sentence this area says                   Tasks 1–8
  SourceResolve.cs       the step: references, loops, branches, functions, calls                  Tasks 1–4
  SourceResolve.Loops.cs / .Branches.cs / .Functions.cs                                           Tasks 2–4
  SourceCheck.cs         + the resolve step before expressions, the derived step after           Tasks 1, 5
  TemplateReads.cs       ITemplateReads, TemplateReads (the seam of D1)                           Task 5
  SourceDerived.cs       reads, reproducible, the flags, the header checks, the warnings          Task 5
  CompiledFormats.cs     the bundle, $ids, hashes, the #/$defs rewrite, bindings                  Task 6
  SourceHash.cs          sourceHash                                                               Task 6
  ResolveCompiler.cs     resolve.json                                                             Task 7
  PublishNotes.cs        § 10.2's notes                                                           Task 7
  Toolchain.cs, CaretRange.cs, LockCompiler.cs                                                    Task 8
  CompiledPackage.cs     the pair, their hashes, Verify                                           Task 8
  RequiresCheck.cs       requires, needs and break points against an engine                       Task 8
tests/V31Resolve/        *Tests.cs one per source file; ResolveFixture.cs (packages built on
                         SourceFixture: nested loops, a while, an if, a case, a function + call)  Task 1 on
tests/V31Resolve/Golden/ nested-note.resolve.json, nested-note.lock.json, functions.resolve.json  Task 9
tests/V31Source/         RefusalPackages.Resolve.cs, V31Contract (A05 rows), RefusalPackagesTests
docs/customizable-workflow/package-format-library.md   + "specVersion 31: the resolver and the lock"   Task 10
consultologist-package-format (m39-build)   v31/conformance/refusal-packages/**, compiled-documents/**,
                                            README.md, index.json, scripts/check-v31.py
```

## Review Focus

1. **One id in two places:** two sibling loops each holding `draft`; a function node named as a top-level node is; a node and a check sharing an id. Expected: every reference resolves to the one key its own scope sees, never to the other. (Task 1 `SiblingScopes_ResolveToTheirOwn`.)
2. **Hostile reference strings:** `""`, `"node"`, `"node..x"`, `"item.parent.parent.parent"` in a single loop, a 300-segment path, `"node.x.value.0"`, non-ASCII, a `with` value of `5`, `true`, `{}`. Expected: one refusal each, never an exception. (Task 1 `AnyReference_IsResolvedOrRefused_NeverThrows`; Task 4 `WithValues_OfAnyJsonKind`.)
3. **Circles:** two nodes reading each other, a node reading itself, three functions calling in a ring, a format that names itself in the bundle. Expected: a refusal naming the circle; no stack overflow, no hang. (Task 4 `CallRings_AreRefused`; Task 5 `NodesReadingEachOther_AreRefused`; Task 6 `ACyclicFormat_NeverReachesTheBundle`.)
4. **The same package, differently delivered:** files in another dictionary order, `\r\n` line ends, a dropped `resolve.json` beside the source, compiled twice. Expected: byte-identical `resolve.json` and `lock.json` and equal hashes. (Task 7 `Compile_IsDeterministic`; Task 6 `SourceHash_IgnoresLineEndsAndEvidence`.)
5. **Compiled documents that were touched:** `resolve.json` re-indented, a member reordered, a trailing newline, one byte changed, a `lock.json` naming another hash, bytes that are not JSON, an empty file. Expected: `Verify` refuses with S07-006 or S07-017, accepts only the exact canonical bytes, never throws. (Task 8 `Verify_AcceptsOnlyTheBytesPublished`.)

---

### Task 1: References, the scope tree and the real scope

**Spec:** part 01 § 9; part 03 §§ 4.1, 4.2; part 04 §§ 2.2, 2.5, 2.6, 3.3, 3.4, 4, 7.4; the sentences S04-036 to S04-040, S04-050, S04-051.

**Files:** Create `ReferenceString.cs`, `ScopeTree.cs`, `ResolvedScope.cs`, `ResolveSentences.cs`, `SourceResolve.cs`; modify `SourceCheck.cs`; tests `tests/V31Resolve/{ResolveFixture,ReferenceStringTests,ScopeTreeTests,ResolvedScopeTests,SourceResolveTests}.cs`.

**Interfaces — Produces:**
- `public sealed record ReferenceString(string Text, IReadOnlyList<string> Segments)` with `public static ReferenceString? Parse(string? text)` (null when it is not a path, D13).
- `public sealed class ScopeTree` with `public static ScopeTree Build(SourcePackage package, PackageTypes types)`; `ScopeFrame FrameOf(SourceNode node)` and `ScopeFrame FunctionRoot(SourceFunction f)`, `ScopeFrame PackageRoot`, `ScopeFrame ChecksRoot`.
- `public sealed class ScopeFrame` — `Parent`, `Function` (null outside one), `Owner` (the `SourceFor`, `SourceWhile`, `SourceIf`, `SourceCase` whose body it is, null at a root), `Nodes` (its body's nodes); `SourceNode? Visible(string root, string id)` (`root` `node` or `check`: this body, then each enclosing body, stopping at the function's root; in `checks/`, `node.<id>` sees the top level of `nodes/`); `SourceFor? Loop(int up)` (`item`, `item.parent`, …, skipping whiles); `SourceWhile? While(int up)` (skipping loops); `SourceNode? Hidden(string root, string id)` (a node of that id that exists but runs inside a loop the reader is outside of, for S04-039).
- `public sealed record Resolved(ScopeHeadKind Kind, string? Key, int Length, JsonNode? Format, bool Nullable, SourceRefusal? Refusal)` and `public Resolved ScopeTree.Resolve(ReferenceString reference, ScopeFrame frame, string subject, string path, bool condition)` — the one resolution every caller uses: a `var` path, an `in`, a `case`, a `with`. `Key` is the rooted key for `node`, `check`, `input` (`input.<id>`), `param` (`param.<id>`), null otherwise. `Length` is how many segments the head consumed; the rest go to `FormatReads.Read` for a `Format` head (the head is the thing, read through `.value`, `.content`, `.title`) or are fields for a `Value` head (the head included the member: `node.x.items`, `node.x.state`, `item.value`, `item.id`, `pass.number`, `pass.node.x.value`, `run.date`).
- `public sealed class ResolvedScopes : IExpressionScopes` — `For(SiteContext)` gives an `IExpressionScope` whose `Head` is `Resolve` at the site's frame. A `While` or `Until` site reads its own body's nodes (the frame of the body); a macro's or snippet's `if` is read where it is placed, which is area 07's: its scope stays `PackageScope`'s (deferred), said in the summary.
- `public sealed record Resolution(ScopeTree Tree, IExpressionScopes Scopes, IReadOnlyDictionary<SourceNode, string> SourceKeys)` and `public static (IReadOnlyList<SourceRefusal> Refusals, Resolution? Resolution) SourceResolve.Check(SourcePackage package, PackageTypes types)`; `SourceCheckResult` gains `Resolution? Resolution`, and `SourceCheck.Run` runs `SourceResolve.Check` after the types, stops on its refusals, then passes `Resolution.Scopes` to `SourceExpressions.Check`.

Rules: inside a function `input.*`, `run.*`, `check.*` and an outer `item` or `pass` are `Refused` with S04-050 / S04-051 (`profile.*` never reaches the scope: S03-018 is said first); `param.<id>` outside a function, or an id the function does not declare, is `Unknown`. `item` with no enclosing loop is S04-026's sibling for expressions: `Unknown` unless a row fits (S04-040 for `item.parent`; S04-036, S04-037, S04-038 for `pass`). A node of that id that exists inside a loop the site is outside of is S04-039. Member types are D14's. A loop over `.items` gives `item` the element of § 2.6 (`item.state`, `item.value`, `item.label`, `item.nodes.<id>.value`).

- [x] **Step 1: Write the failing tests.** `ResolveFixture` builds, on `SourceFixture.Minimal()`, the packages the tests name: `NestedNote()` (part 04 § 2.8's two loops and § 3.5's while, with the formats of part 07 § 2.9), `Conditional()` (§ 5.4's `case` and `if` over an array input), `Functions()` (§ 7.11's `draft_section`, its call in a loop over a snippet folder, and the check loop over `.items`). Tests:
  - `ReferenceStringTests.Parse` (theory): `node.plan.value.sections` → 4 segments; `""`, `"node..x"`, `"node.x.value.0"`, `" node.x"`, null → null.
  - `ScopeTreeTests.Visible_ResolvesOutward`: in `NestedNote()`, from `draft_paragraph`'s frame `node.outline` is `node.sections.outline`, `node.plan` is `node.plan`, `node.judge` is null; from the top level `node.outline` is null and `Hidden` gives it.
  - `ScopeTreeTests.SiblingScopes_ResolveToTheirOwn`: two sibling loops each with `draft` and a reader beside it: each reader resolves `node.draft` to its own loop's key; a function with a node `plan` resolves `node.plan` to `node.plan` of the function, and its `Function` is set; `node.coverage` and `check.coverage` resolve to different nodes.
  - `ResolvedScopeTests.Heads` (theory over `Resolve`): `node.plan.value.sections` → `Format`, key `node.plan`, length 2; `node.sections.items` → `Value`, length 3, a list; `node.refine.passes` → `integer`; `node.refine.stopped` → `string {check, until, bound}`; `node.sections.stopped` → `string {until, bound, end}`; `item.value.title` inside `sections` → `Value` length 2 with the element schema; `item.parent.value.title` inside `paragraphs` → the outer element; `pass.number` inside `refine` → `integer`; `pass.previous.node.judge.value` → nullable; `param.standard` in `draft_section` → `Format`, key `param.standard`; `input.consult_draft` → as `PackageScope` gave it.
  - `ResolvedScopeTests.Refusals` (theory: reference, where, id): `item.parent.value.x` in an outermost loop → S04-040; `pass.number` outside a while → S04-036; `pass.parent.number` in an outermost while → S04-037; `pass.previous.node.plan` naming a node outside the while → S04-038; `node.outline.value` from the top level → S04-039; `input.consult_draft.value` inside a function → S04-050; `run.date`, `check.coverage`, an outer `node.plan` inside a function → S04-051; `node.nothing` → `Unknown`. Each refusal passes `V31Contract.AssertSentence`.
  - `ResolvedScopeTests.AnyReference_IsResolvedOrRefused_NeverThrows`: for 2,000 strings built by `Random(31)` from the segments `node check input item pass param run parent previous history number value content items state plan sections outline x 0` joined by 0 to 300 dots, at every frame of `NestedNote()` and `Functions()`, `Parse` then `Resolve` returns.
  - `SourceResolveTests.TheExamples_Resolve`: `SourceCheck.Run` on the three fixtures has no refusal and `Expressions` holds no site with a null `Type.Format` that reads `node`, `item`, `pass` or `param` (nothing is deferred any more, macro and snippet sites aside).
  - `SourceResolveTests.AnExpressionReadingAnUnknownNode_IsS03014`: `{"var":"node.nothing.value"}` in a computed node.
- [x] **Step 2: Run them; they fail to compile.**
- [x] **Step 3: Implement.** Correct, as part of the step, the valid packages of areas 02 to 04's refusal cases that read things the real scope now refuses (04-D44 names S03-020 and S03-030), and `SourceExpressionsTests` cases that asserted deferral; add refusal cases for S04-036 to S04-040, S04-050, S04-051 and re-export (Global Constraints).
- [x] **Step 4: Run `--filter "FullyQualifiedName~V31"`; all pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): references resolved to keys, and the scope every expression is read in`

### Task 2: Loops

**Spec:** part 04 §§ 2, 3, 10.1 (`for`, `while`, "Every flow node" rows S04-017, S04-025 to S04-030, S04-035).

**Files:** Create `SourceResolve.Loops.cs`, `tests/V31Resolve/SourceResolveLoopsTests.cs`; modify `SourceResolve.cs`, `ScopeTree.cs`, `RefusalPackages.Resolve.cs`.

**Interfaces — Consumes** `ScopeTree.Resolve`. **Produces** `ScopeTree.Source(SourceFor loop)`: a `LoopSource(Resolved Resolved, string Reference, JsonNode? Element, int? KnownLength, bool OverItems, SourceFor? Production)` (the loop's `in` resolved against the frame *outside* the loop; `Element` the list's `items` schema, a snippet folder's `format` for `snippet.<folder>`; `KnownLength` a snippet folder's item count; `OverItems` and `Production` when it iterates a loop's `.items`), which Task 1's `item` typing now reads.

Rules: `in` that the reader held as non-text → S04-017 if the reader does not already refuse it (find what the reader says for an object `in`; keep one sentence, D2's rule of thumb: the row of part 04). `in` not a list → S04-025 (`<format>` by `TypeStrings.Print`). `in` reading `item.value.…` with no enclosing loop → S04-026. `in` naming a node that runs inside another loop's body the loop is not in → S04-027. `snippet.<folder>` is a list of the folder's format; a single snippet or an unknown folder is S04-025 / S03-014. `itemLabel` non-null: not a property of the element → S04-028 (`fields:` the element's properties in written order); not in `required`, or nullable → S04-029; not a string (a text format or enum counts as string) → S04-030; over `.items` the element's own `label` is accepted. S04-035: a `while` body node that is not `ret`, not read by the `while` or `until` rule, and not read by another body node's header (expression, `if`, `in`, `case`, `with`) — judged only when no body node of that while has an unknown body (D1): with an agent or rendered node in the body the rule is not judged until the seam is supplied (Task 5 re-judges it).

- [x] **Step 1: Write the failing tests** (`SourceResolveLoopsTests`, one fact per sentence above, each a `ResolveFixture` package with one change, asserting the single refusal's id, its sentence through `V31Contract.AssertSentence`, and its path; plus `ValidSources` (theory): an array input, `snippet.standards`, `node.plan.value.sections`, `item.value.paragraphs`, `node.sections.items`, `param.sections.value` each resolve with the element type expected; `ItemLabel_OverItems_IsLabel`; `AWhileOfComputedNodes_WithAnUnreadNode_IsS04035`; `AWhileWithAnAgent_IsNotJudgedYet`).
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement; add the refusal cases; re-export.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): a loop's source and itemLabel, typed`

### Task 3: Branches

**Spec:** part 04 § 5, § 10.1 (`if` and `case` rows S04-044 to S04-047, S04-075), § 7.6's "always valued" notion.

**Files:** Create `SourceResolve.Branches.cs`, `tests/V31Resolve/SourceResolveBranchesTests.cs`; modify `ScopeTree.cs`.

**Interfaces — Produces** `public static bool ScopeTree.AlwaysValued(SourceNode node)` (a leaf with `if` null; a `for`; a `while` with `checkFirst` false; an `if` with an `else` whose two `ret`s are always valued; a `case` with no empty branch whose branches' `ret`s are; a call) and `public static IReadOnlyList<string> ScopeTree.EmptyValues(SourceCase c)`; Task 1's nullability (D14) now uses `AlwaysValued`.

Rules: the `case` subject resolved like `in` (S04-017, A05-001, S03-014); not an enumerated string (`enum`, or `oneOf` of `const`s; nullable allowed) → S04-044; a `when` value not among the subject's values → S04-045 (`<values>` in the format's order, quoted as the row shows); a value in two branches → S04-046; values unlisted with no `else` → S04-047; a subject that can be null (a nullable format, an input not required, a node not `AlwaysValued`) with no `else` → S04-075.

- [x] **Step 1: Failing tests:** one fact per sentence; `TheMigratedExamples_Pass` (§ 5.4's three files verbatim); `ASubjectFromASkippableNode_NeedsAnElse`; `AlwaysValued` (theory over each kind).
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement; refusal cases; re-export.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): a case's subject and its values, held to the subject's enum`

### Task 4: Functions and calls

**Spec:** part 04 § 7, § 10.1 "Functions and calls", § 6 (`depth`).

**Files:** Create `SourceResolve.Functions.cs`, `tests/V31Resolve/SourceResolveFunctionsTests.cs`.

**Interfaces — Produces** on `Resolution`: `IReadOnlyDictionary<SourceLeaf, CallBinding> Calls` with `public sealed record CallBinding(SourceFunction Function, IReadOnlyDictionary<string, Resolved?> Arguments, int Depth)` (a `null` argument is the literal `null`), and `IReadOnlyList<SourceRefusal> Warnings`.

Rules, in this order per call, then per function: unknown function → S04-064; a parameter not passed → S04-065; an extra one → S04-066 (`<params>` in declared order, comma-joined); each argument a reference (S04-017, A05-005, A05-001, S03-014) — allowed heads are § 7.7's list, including `snippet.<folder>.<item>` (its value is the folder's format), `run.<fact>` and `profile.<field>` (typed by `v31/profile.json`), a whole `node.<id>`, `check.<id>` or `item` (its `.value` type); the literal `null` needs a nullable parameter; each typed against the parameter by `FormatTypes.Fits` → S04-068; the call's `format` against the function's by `FormatTypes.Same` → S04-070; a body → D2. Per function: its `format` against its `ret`'s → S04-061; `ret` an `if` with no `else` → S04-057; `ret` inside... (a `ret` whose own value may be skipped through an inner `if`) → S04-058; a `case` with an empty branch → S04-059; `ret` an `if` or `case` whose bodies return different formats → S04-060; own definition sharing an id with a `uses` entry → S04-053; a header reference inside the function to a package snippet not in `uses` → S04-052 (formats are area 03's S02-017); a `uses.formats` entry never named by the function → S04-074 as a warning (snippets, macros, renderers: only through the seam, Task 5). The call graph (function → functions its body calls, at any depth): a ring → S04-054 / S04-055, said once per ring, naming it from its ordinal-first member; then depth: a call outside every function is 0, inside a function reached at depth *d* is *d*+1, the deepest chain over `FormatBounds.Depth` → S04-056 on the outermost call of the chain. A parameter bound anywhere to `profile.<field>` (or to a parameter that is) *carries the profile*: a condition inside the function that reads it is S04-069 (`a condition`); prompts and parts through the seam.

- [x] **Step 1: Failing tests:** one fact per sentence; `TheExample_Passes` (`Functions()`); `CallRings_AreRefused` (a→a; a→b→a; a→b→c→a, each one refusal naming the ring; and 40 functions in a ring terminates); `Depth_IsMeasured` (a chain of 16 functions passes: depths 0 to 15; 17 is S04-056); `WithValues_OfAnyJsonKind` (theory: `5`, `true`, `{}`, `[]`, `""`, `"node"`, `null` for a non-nullable and for a nullable parameter); `AProfileParameter_IsNotReadByACondition`.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement; refusal cases; re-export.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): calls bound to their functions, recursion refused, depth measured`

### Task 5: The derived values

**Spec:** part 02 §§ 1.1 to 1.3; part 03 §§ 4.4 to 4.6, § 11.1 (S03-036, S03-044), § 11.2; part 04 § 1 (the four written members), § 4 "contagious flags", § 7.7; part 07 § 2.5 (`reads`, the three flags).

**Files:** Create `TemplateReads.cs`, `SourceDerived.cs`, `tests/V31Resolve/SourceDerivedTests.cs`; modify `SourceCheck.cs`, `SourceResolve.Loops.cs` / `.Functions.cs` (the seam's rules).

**Interfaces — Produces:**
- `public sealed record TemplateReads(IReadOnlyList<string> Reads, IReadOnlyList<string> Places, IReadOnlyList<TemplateRender> Renders, IReadOnlyList<TemplateRunSpot> RunSpots, bool PlacesProfileMacro)`; `TemplateRender(int N, string Source, string Renderer)`; `TemplateRunSpot(int Site, string At, string Reads)`; `public interface ITemplateReads { TemplateReads? Of(object declaration, SourceFunction? function); }` (the declaration as `SiteContext.Declaration` names it; null: unknown). `Reads` are paths as written; the resolver resolves them with `ScopeTree.Resolve`.
- `public sealed record DerivedEntry(string Key, IReadOnlyList<string> Reads, IReadOnlyList<LineageRead> ContentReads, IReadOnlyList<LineageRead> ConditionReads, bool BodyKnown, bool? Reproducible, bool ModelDerived, bool RunDependent, bool ProfileCarrying, bool FlagsKnown)`.
- `public sealed record PackageDerived(IReadOnlyDictionary<string, DerivedEntry> Entries, IReadOnlyDictionary<string, IReadOnlyDictionary<string, DerivedEntry>> FunctionEntries, IReadOnlyList<string> RunFacts, IReadOnlyList<string> ProfileFields, IReadOnlyList<SourceRefusal> Warnings, bool Complete)` — `Entries` keyed by rooted key over nodes, checks, deliverables, and the package's renderers, snippets and macros (`renderer.<id>`, `snippet.<id>`, `snippet.<folder>.<item>`, `macro.<id>`, …).
- `public static (IReadOnlyList<SourceRefusal> Refusals, PackageDerived? Derived) SourceDerived.Check(SourcePackage package, PackageTypes types, Resolution resolution, PackageExpressions expressions, ITemplateReads? templates = null)`; `SourceCheck.Run(upload, registry, templates = null)` runs it last and `SourceCheckResult` gains `PackageDerived? Derived`.

Rules: content reads (what the flags spread along): a computed node's expression reads; a body's reads (the seam); a `call`'s arguments, and inside the function each parameter stands for its arguments' union across every call of it; a flow node's `ret`; a loop's `in` (its items); never a condition (`if`, `while`, `until`, a `case` subject) and never `pass.previous…` or `pass.history`. Condition reads are kept apart for the graph and the warnings. Starts: an agent is model-derived; a node whose `TemplateReads.PlacesProfileMacro` is profile-carrying; an argument `profile.<field>` makes its parameter profile-carrying, `run.<fact>` run-dependent. The flags come from `LineageFlags.Derive` over this graph. A node whose content closure reaches an unknown body has `FlagsKnown` false for `runDependent` and `profileCarrying` (and for `modelDerived` unless something known already makes it true); an unknown flag is not judged and takes the declared value. Header checks: a leaf's or check's flag → S03-036 (`LineageFlags.Disagreement`); a flow node's or call's → S04-016; a flow node's `reproducible` (true only if its `ret`, transitively through flow nodes and calls, is) and a call's (the function's `ret`'s) → S04-015. A condition, and (through the seam) a prompt or part, reading a profile-carrying value → S03-044. Nodes reading each other in a circle (content or condition reads, `pass.previous` aside, a while's own body reading its own passes aside) → A05-003. Warnings: S03-037 and S03-038 for every run-dependent decision and computed value, not only the direct readers (replacing `PackageExpressions.Warnings` for those sites, said once each); S04-074 for snippets, macros and renderers, S04-052 for them, S04-035 and S04-063 when the seam is supplied. `RunFacts` and `ProfileFields`: sorted, from expressions, `with` arguments and the seam.

- [x] **Step 1: Failing tests** (`SourceDerivedTests`; `StubTemplates : ITemplateReads` maps a node's key to its reads):
  - `Flags_SpreadAlongContent_NeverThroughAConditionOrAnEarlierPass`: with stub reads for every body of `NestedNote()`, every node's three flags are as part 07 § 2.9 shows; a node chosen by a model-derived `if` and reading nothing model-derived is not model-derived.
  - `ACallsResult_TakesItsArgumentsFlags`: `"with": { "today": "run.date" }` makes the call and the function's readers of `param.today` run-dependent; a second call without it stays clean at the call, and the parameter is run-dependent inside (the union).
  - `HeaderChecks` (theory: change, id): a computed node declaring `modelDerived: false` over an agent → S03-036 naming the read; a loop declaring `modelDerived: false` over an agent `ret` → S04-016; a loop of computed nodes declaring `reproducible: false` → S04-015; a call declaring `reproducible: true` to a function returning an agent → S04-015.
  - `WithoutTheSeam_AFlagThatNeedsABody_IsNotJudged`: an agent declaring `runDependent: true` passes with no provider, `FlagsKnown` false, `Complete` false; with a stub that reads nothing run-dependent it is S03-036. An agent declaring `modelDerived: false` is S03-036 either way.
  - `AConditionReadingProfileText_IsS03044`; `NodesReadingEachOther_AreRefused` (two computed nodes; a node reading itself; a ring of three; A05-003 naming the ring); `AWhileReadingItsOwnPass_IsNoCircle`.
  - `Warnings_NameEveryRunDependentDecision`: an `if` reading a computed node that reads `run.date` gets S03-037 though it reads no fact itself.
  - `Reads_AreCanonical`: D4's forms, sorted, for § 2.9's `draft_paragraph` (through the stub).
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement; refusal cases for S03-036 (a computed node), S03-044, S04-015, S04-016, A05-003; re-export; update `LogicNotShipped`.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): reproducible and the three flags, derived and held against every header`

### Task 6: The formats bundle, the bindings and the source hash

**Spec:** part 07 §§ 1, 2.3, 2.4, 2.6; part 02 §§ 2.3, 6.2, 6.3, 7.

**Files:** Create `CompiledFormats.cs`, `SourceHash.cs`, `tests/V31Resolve/{CompiledFormatsTests,SourceHashTests}.cs`.

**Interfaces — Produces:**
- `public sealed record CompiledFormats(JsonObject Bundle, JsonObject FormatHashes, JsonObject TypeHashes, JsonObject Bindings, IReadOnlyList<SourceRefusal> Refusals)` with `public static CompiledFormats Build(SourcePackage package, PackageTypes types, TypeRegistry registry)` and `public static JsonNode? Rewrite(JsonNode? format, FormatSet scope, SourceFunction? function)` (a name becomes `{ "$ref": "#/$defs/<key>" }`, every `$ref` inside an inline schema likewise, a clone; a function's own format's key is D5's).
- `public static string SourceHash.Of(IReadOnlyDictionary<string, string> files)` (`SourceParseResult.Files`: the listing of § 2.6, `dag.mmd` excluded, each file's hash over its UTF-8 bytes after normalisation).

Rules: the bundle holds every format *used* — named by any `format` member (inputs, nodes, checks, functions, parameters, definitions, deliverables, snippet folders), and every format those reach — from the three homes, each with its `$id`, its `$schema` and its annotations, references inside rewritten to `#/$defs/…`; hashes are `FormatTypes.FormatHash` / `TypeHash` of the format in its own scope (unchanged by the rewrite); bundle and hash maps keyed by bundle key. `Bindings`: for each agent node, `PackageTypes.Bindings` → `format key → producer → { agentName, agentVersion, via }`; a `via` of `producer` is refused by the row part 07 § 2.4 points to if `sentences.json` has one, else left to `AgentBindings` (which never gives it). A05-004 on a key collision.

- [x] **Step 1: Failing tests:** `Bundle_HoldsOnlyWhatIsUsed` (`NestedNote()`: `markdown_block`, `plaintext`, `number`, `section_plan`, `outline`, `verdict`; no `concept_list`); `PackageFormats_GetTheirUrn` (§ 2.4's example `$id`); `References_AreRewritten` (an inline `{ "type": "array", "items": { "$ref": "plaintext" } }` → `#/$defs/plaintext`; a package format naming another); `Hashes_AreTheTypeServices` (each equals `FormatTypes.FormatHash` / `TypeHash`); `Bindings_AreByFormatAndProducer` (§ 2.9's: `plaintext` via `format`, `section_plan` via `custom`); `AFunctionsOwnFormat_IsKeyedByItsFunction`, `…_Colliding_IsA05004`; `ACyclicFormat_NeverReachesTheBundle` (`SourceCheck` refuses it first; `Build` on the raw package still returns); `SourceHashTests.Listing` (the hash equals SHA-256 of the RFC 8785 text of the sorted `{path, hash}` array, computed independently in the test); `SourceHash_IgnoresLineEndsAndEvidence` (the same package with `\r\n`, with a `resolve.json`, with a `dag.mmd`: equal hashes; a changed `README.md` or test file: different).
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): the formats bundle, its hashes and bindings, and the source hash`

### Task 7: `resolve.json`

**Spec:** part 07 §§ 1, 2 (all), 3; part 04 § 8, § 10.2; `schemas/resolve.schema.json`.

**Files:** Create `ResolveCompiler.cs`, `PublishNotes.cs`, `tests/V31Resolve/{ResolveCompilerTests,PublishNotesTests}.cs`.

**Interfaces — Produces:**
- `public sealed record PublishLimits(int Ceiling, int ItemsPerLoop, int CallsPerJob)`.
- `public sealed record ResolveResult(JsonObject? Document, string? Canonical, string? Hash, IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<SourceRefusal> Warnings, bool Complete)`.
- `public static ResolveResult ResolveCompiler.Compile(SourceCheckResult check, TypeRegistry registry, string scribanVersion, PublishLimits? limits = null)` — `check.Refusals` non-empty gives them back with no document; `Canonical` is `CanonicalJson.Write(Document)`, `Hash` its `sha256:`.
- `public static IReadOnlyList<SourceRefusal> PublishNotes.Of(SourcePackage package, Resolution resolution, PublishLimits limits)` (S04-072, S04-073, S04-079, S04-080; `Path` `""` → `at: null`).

Document: § 2.2's members, every one, in the shapes of §§ 2.3 to 2.8 and D3 to D8. Entries: every header member as written with formats rewritten (`CompiledFormats.Rewrite`), conditions and expressions with `var` / `missing` paths rewritten (D3), `in`, `case`, `with`, `ret` rewritten; then § 2.5's derived members that apply to the entry's kind (`nodes` and `ret` for flow nodes — an `if`'s and a `case`'s bodies keep their written structure with keys inside, and the derived `nodes` is every child in order; `body` / `bodyHash` with `null` for the body-less kinds; `parts`, `places` for agents; `renders`, `runSpots` for templated kinds; `formatHash`, `typeHash`), the flags from `PackageDerived`. `title` is the manifest's, or `<name>@<version>` when it is null (the document's `title` is never null); `description` the lines joined by `\n`, or null. `warnings`: the expression warnings, `PackageDerived.Warnings`, `Resolution.Warnings` and the notes, as `{ sentence, at }` with `at` the path or `null`, ordered by `at` (null first) then as found. `run`, `needs.profile`: `PackageDerived`'s.

- [x] **Step 1: Failing tests:**
  - `TopLevel_HasEveryMember`: the document's keys equal `resolve.schema.json`'s `required`, and `requires` / `needs` theirs; every entry has the schema's `entry.required`.
  - `NestedNote_IsTheSpecsExample`: against part 07 § 2.9, member by member where the example is not elided: `package`, `lock`, `requires` (with `scribanVersion` `7.2.5`), `needs`, `inputs`, `inputOrder`, `bindings`, the entries `node.plan`, `node.section_count`, `node.sections`, `node.refine` (keys, `parent`, `nodes`, `ret`, the rewritten `in` and `while`), `deliverable.note`'s key, `graph.edges` ⊇ the example's five (with a stub `ITemplateReads` giving the bodies' reads), `graph.order` starting `input.consult_draft, node.plan, node.section_count, node.sections`.
  - `Functions_AreSealedMaps`: `functions.draft_section.nodes` keyed `node.standard_draft`…, `ret` `node.apply_instructions`, a `calls` edge `function.draft_section` → `node.standards.section`, an `aligned` edge `node.standards` → `check.section_coverage`.
  - `Requires_IsMinimal`: no `call` in `kinds` without a call; `operations` only those written; `features` `parallel` and `check-loops` as present.
  - `Compile_IsDeterministic`: compile twice; compile from an upload built in reverse insertion order; from `\r\n` files: identical `Canonical`. `Canonical` parses and re-writes to itself, has no timestamp-like member, and `Document` has no `null`-valued top-level member but `description`.
  - `ARefusedPackage_CompilesNothing`; `WithoutTheSeam_IsNotComplete`.
  - `PublishNotesTests`: two nested loops with `maxParallel` 4 and 5 over agents and a ceiling of 16 → S04-072 with 20; a `maxParallel: null` loop over a node's list → S04-073 naming it; over a snippet folder of 3 → counted as 3; a list with no `maxItems` → S04-079; 3 × 200 × agents over 500 → S04-080; no limits → no notes.
- [x] **Step 2: Run; fail.**  - [x] **Step 3: Implement.**  - [x] **Step 4: Run all V31; pass.**
- [x] **Step 5: Commit and push.** `PackageFormat (#1502): resolve.json compiled from a checked source`

### Task 8: `lock.json`, the pair, and what an engine checks

**Spec:** part 07 §§ 1, 3.1 (last paragraph), 3.2, 4 (all), 7, § 10 ("At start", "At load", the two evidence warnings); `schemas/lock.schema.json`.

**Files:** Create `Toolchain.cs`, `CaretRange.cs`, `LockCompiler.cs`, `CompiledPackage.cs`, `RequiresCheck.cs`, tests for each; modify `SourceIntake.cs` / `SourceModel.cs` (expose the dropped copies).

**Interfaces — Produces:**
- `public sealed record Toolchain(string PackageFormat, string Dialect, IReadOnlyDictionary<string, string> Libraries, string Evaluator, string DotNet, string Unicode, string FormatsCatalogue, string AgentsCatalogue, string? OutputContracts)` with `static Toolchain Current(string packageFormat, string formatsCatalogue, string agentsCatalogue, string? outputContracts = null)` (D9) and `string Scriban => Libraries["Scriban"]`.
- `public static class CaretRange { static string From(string exact); static bool Admits(string range, string version); static int Compare(string a, string b); }` (three numeric parts; anything else never admits and compares as lowest; never throws).
- `public static JsonObject LockCompiler.Compile(JsonObject resolve, string resolveHash, Toolchain toolchain)` — § 4.1's members; `operations` the exact version of each name in `resolve.requires.operations`; `builtins`: `formats`, `markdown_escape`, `plain_conversion`, `printer` from `constants.json`'s versions (constants in code, held to the file by a test), plus each built-in renderer in any entry's `renders`.
- `public sealed record CompiledPackage(string Resolve, string ResolveHash, string Lock, string LockHash, string SourceHash, IReadOnlyList<SourceRefusal> Warnings, bool Complete)` with `static (IReadOnlyList<SourceRefusal> Refusals, CompiledPackage? Compiled) Publish(IReadOnlyDictionary<string, byte[]> upload, TypeRegistry registry, Toolchain toolchain, PublishLimits? limits = null, ITemplateReads? templates = null)` (steps 1, 2 and 4.1–4.3 of § 5: check, compile, hash, lock, hash; no tests, no writing: area 11's) and `static SourceRefusal? Verify(string package, string version, byte[] resolve, byte[] @lock, string stampedResolveHash, string stampedLockHash)` — S07-006 when the resolve bytes are not canonical JSON of themselves or their hash is not the stamp's; S07-017 when the lock's are not, or its `resolve.hash` is not the resolve's hash, or the resolve's `lock` is not `lock.json`; null when all hold.
- `public sealed record EngineCapabilities(string Evaluator, string Scriban, IReadOnlyDictionary<string, string> Operations, IReadOnlySet<string> Kinds, IReadOnlySet<string> Features, IReadOnlyDictionary<string, IReadOnlySet<int>> Agents, IReadOnlyList<BreakPoint> BreakPoints)`; `BreakPoint(string Component, string Version)`; `public static IReadOnlyList<SourceRefusal> RequiresCheck.Engine(JsonObject resolve, JsonObject @lock, EngineCapabilities engine)` (S07-003 per component out of range or locked before a break point — `<component>` is `Scriban`, `the evaluator` or the operation's name; S07-004 per kind or feature; S07-005 per agent) and `RequiresCheck.Profile(JsonObject resolve, IReadOnlyDictionary<string, string?> profile)` (the signature's and the other fields' sentences of § 10, by their rows; an empty or whitespace value is not set; the field label from `v31/profile.json`). A document missing a member it reads is refused as out of range, never thrown on.
- `SourceParseResult.Dropped` (`IReadOnlyList<string>`: the compiled copies intake dropped) and `public static SourceRefusal? ResolveSentences.ImportIgnored(IReadOnlyList<string> dropped)` (S07-016, when `resolve.json` or `lock.json` was dropped), `ResolveSentences.EvidenceCommitted(string path)` (S07-015).

- [ ] **Step 1: Failing tests:** `LockCompilerTests.HasEveryMember` (keys equal the schema's `required`; `libraries` the four names; `culture` `invariant`; `resolve.hash` the hash given; no time); `…IsTheSpecsExample` (§ 4.1's block from a `Toolchain` built by hand with its values, for `NestedNote()`); `ToolchainTests.Current_ReadsTheLoadedLibraries` (each version equals the `Version` pinned in `Directory.Packages.props`, read by the test; `DotNet` starts `10.`; `Unicode` `16.0`); `CaretRangeTests` (theory: `^7.2.5` admits `7.2.5`, `7.2.6`, `7.9.0`; not `7.2.4`, `8.0.0`, `""`, `7.2`, `x`; `From("1.0.0")` `^1.0.0`); `CompiledPackageTests.Publish_PairsTheDocuments` (`lock.resolve.hash` is `ResolveHash`; `resolve.lock` is `lock.json`; both hashes are of the returned text; publishing twice gives equal texts); `Verify_AcceptsOnlyTheBytesPublished` (accepts the published bytes; S07-006 for the resolve re-indented, with a trailing newline, with two members swapped in the text, with one byte changed, empty, not JSON, and for a wrong stamp; S07-017 for the same done to the lock, for a lock of another package's resolve, and for a resolve whose `lock` member was edited (with the stamp re-computed)); `RequiresCheckTests` (an engine at Scriban `7.2.6` admits a lock at `7.2.5`; at `8.0.0` → S07-003; a break point `Scriban 7.3.0` with the lock at `7.2.5` and the engine at `7.3.1` → S07-003; a missing kind → S07-004; a missing agent version → S07-005; `needs.profile` `["signature"]` unset or `"  "` → the signature sentence; `["closing"]` → the field sentence with its label; `{}` given as either document → refusals, no exception); `EvidenceTests` (an upload with `resolve.json` → `Dropped` names it and `ImportIgnored` gives S07-016; without → null). Every sentence through `V31Contract.AssertSentence`.
- [ ] **Step 2: Run; fail.**  - [ ] **Step 3: Implement.**  - [ ] **Step 4: Run all V31; pass.**
- [ ] **Step 5: Commit and push.** `PackageFormat (#1502): lock.json, the published pair verified, and requires held against an engine`

### Task 9: The compiled documents as goldens, and the conformance family

**Spec:** part 07 § 1 (canonical, same bytes), § 4.3 (goldens); part 10 § 5 (the family's shape is the exporting area's).

**Files:** Create `tests/V31Resolve/Golden/*.json`, `tests/V31Resolve/CompiledGoldenTests.cs`, `tests/V31Resolve/CompiledDocumentsExport.cs`; content repo: `v31/conformance/compiled-documents/**`, `README.md`, `index.json`, `scripts/check-v31.py`.

**Interfaces — Consumes** `CompiledPackage.Publish`. The fixed toolchain of the goldens: `package-format@v2026.12.1`, `formats@v2026.12.1`, `agents@v2026.12.1`, libraries and runtime written by hand (`Scriban 7.2.5`, `Markdig 0.41.3`, both Corvus `5.8.12`, `dotnet 10.0.0`, `unicode 16.0`), so a machine's patch level never moves a golden.

- [ ] **Step 1: Write `CompiledGoldenTests`:** for each of `NestedNote()`, `Conditional()`, `Functions()`: `Publish` with the fixed toolchain gives exactly the bytes of `Golden/<name>.resolve.json` and `<name>.lock.json` (canonical, one line, no final newline); `GOLDEN_UPDATE_V31=1` rewrites them. `EveryGolden_VerifiesAgainstItsOwnHashes`.
- [ ] **Step 2: Run with the variable set; read each golden** member by member against part 07 §§ 2 and 4 (pretty-print a copy to read; commit only the canonical bytes); fix the compiler, not the golden, where they disagree with the spec.
- [ ] **Step 3: Write `CompiledDocumentsExport`** (does nothing without `CONFORMANCE_EXPORT_V31`): per case a folder with `source/` (the upload's files), `toolchain.json`, `resolve.json`, `lock.json`, `about.txt`; rewrites the family's `cases` in `index.json` (`RefusalPackagesExport.WithCases`); a pinned-equals-generated test like `RefusalPackagesTests`' holds the pin.
- [ ] **Step 4: In the content repo:** export; add the family to `README.md` and `index.json` (`exportedBy`: `area 05, the resolver and the lock`), the A05 sentences to the README's list; extend `scripts/check-v31.py` with a group that validates every `compiled-documents/*/resolve.json` and `lock.json` against `resolve.schema.json` and `lock.schema.json` and recomputes `lock.resolve.hash` from the resolve's bytes; run it; commit, push, move the pin.
- [ ] **Step 5: Run all V31; pass. Commit and push.** `Tests (#1502): the compiled documents of three packages, frozen and exported`

### Task 10: The guide, and the plan brought up to date

**Files:** Modify `docs/customizable-workflow/package-format-library.md` (a section "specVersion 31: the resolver and the lock (preview)", in the shape of the three before it: what `SourceCheck.Run` now does, the types, the scope, the derived values and the seam, the two documents, what an engine checks, sentences, what it leaves to later areas — the seam's rules to 07 and 08, calling the checks to 06, tests and writing to 11, `dag.mmd` to 13 — the conformance cases, "Not wired in"); correct the earlier sections' "leaves to area 05" lines; this plan (decisions taken during the build, checkboxes).

- [ ] **Step 1: Write the section and the corrections.**
- [ ] **Step 2: Run `dotnet build Consultologist.sln` and all V31 tests; pass.**
- [ ] **Step 3: Commit and push.** `Docs (#1502): the guide's section on the resolver and the lock`

## Spec coverage

| Section | Task |
|---|---|
| part 01 § 9.1 roots and namespaces | 1 (who a root names); uniqueness was area 02's |
| part 01 § 9.2 disjoint scopes | 1 (resolution outward, sealed functions); the duplicate-id refusals were area 02's |
| part 01 § 9.3 keys | 1 (`Key`), 7 (references written as keys, D3); sub-row keys are area 09's |
| part 04 § 1 flow members, `ret` | area 02 (shape); 5 (`reproducible`, the flags); format by area 03 (D2) |
| part 04 § 2 `for` | 2 (`in`, `itemLabel`), 1 (`item`, `.items`, `.value`, `.stopped`, `.state`); running a loop is area 06's |
| part 04 § 3 `while` | 1 (`pass`, its members), 2 (S04-035) |
| part 04 § 4 reading across levels | 1; the flags across levels 5 |
| part 04 § 5 `if`, `case` | 3; the union format was area 03's |
| part 04 § 6 `FormatBounds` | `passes`, `parallel` area 02's; `depth` 4 |
| part 04 § 7 functions and calls | 4; § 7.10 macros in functions through the seam (5), placed by area 07 |
| part 04 § 10.1 refusals | 1 to 5 (each row not already said by areas 02 and 03; D2 for the doubled rows) |
| part 04 § 10.2 notes and warnings | 7 (`PublishNotes`), 4 and 5 (S04-074) |
| part 07 § 1 two documents | 7, 8 (canonical, hashes, no time, the pointers) |
| part 07 § 2 `resolve.json` | 6 (formats, bindings, `sourceHash`), 7 (the rest) |
| part 07 § 3 `requires`, `needs` | 7 (derived), 8 (checked) |
| part 07 § 4 `lock.json` | 8; § 4.3 goldens 9; § 4.4's corpus re-render is the registry's (area 11), its break points 8 |
| part 07 § 7 evidence and views | area 02 (dropped unread); 8 (the two warnings); a `dag.mmd` that differs is area 13's |
