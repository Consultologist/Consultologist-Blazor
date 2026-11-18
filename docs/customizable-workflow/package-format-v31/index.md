# specVersion 31 — the package format, redesigned

> **Status:** design, **revision 1** (2026-11-17). Milestone 39, #1388. Decided with the package
> author from 2026-11-13 to 2026-11-17; nothing in it is built yet. This is a design, not a
> step-by-step plan: the Milestone 39 issues are its sections and take their decisions from it.
> Where an issue comment disagrees with this text, the later of the two is the decision, and this
> document is corrected. Every point is now decided (revision 6); refusal sentences follow the
> style rules of [§ 7](#7-refusal-sentences). The normative text follows in the package-format registry
> as `package-format-v31.md` with the release (§ 5, step 1).
>
> **Revision 2** (2026-11-17): `ret` is the child's full name, an empty or null list fails
> `all_pass`, and every job is refused until the account's time zone is set (§ 7); all three were
> Proposed in revision 1.
>
> **Revision 3** (2026-11-17): the eight cross-part points of § 7 decided — a loop's `.value`,
> `if` for a node's condition, every property in `required`, profile values in functions, the
> lock's pointer, renderer sub-row ids, built-in `$id`s and break points instead of a floor.
>
> **Revision 4** (2026-11-18): every contract point the design had marked **Proposed** is decided,
> after the package author went through them part by part. The largest changes: **formats are pure
> shape** and **a node's `producer`** (`computed` or `generated`) says what made its output; every
> folder is always present; every key is written with its root; inputs are read like nodes
> (`.value`, `.content`, `.title`); `reproducible` is written and checked; model-derived values are
> tracked.
>
> **Revision 5** (2026-11-18): `modelDerived` and `runDependent` written on every node header and
> checked (moved forward from #1478), and `via: "producer"` reserved.
>
> **Revision 6** (2026-11-18): every remaining Proposed point decided — the engine's retry and job
> limits, `Channel` for satellites, the evaluator's run-time guards, and the names and shapes of
> `resolve.json`, `lock.json`, the record and the reports; refusal sentences follow seven style
> rules, are used as written in Milestone 39, and are listed in
> [refusal-sentences.md](refusal-sentences.md) for review in #1483 (Milestone 40).
>
> **Revision 7** (2026-11-18): a second review's findings decided — function bodies inline, a
> clock-free `matches`, a drain before every deploy, integrity checks at load, total-work limits,
> the record in table rows with a canonical blob at job end, a profile-carrying flag written and checked on every
> node header as `profileCarrying`, `blocked`
> deliverables and delivery at the end, and grammar details (nullable enums, null subjects and
> sources, blank inputs, numbers) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
>
> **Revision 8** (2026-11-18): from an architecture evaluation, a go/no-go gate before the bump
> (smoke runs, the byte-equivalence report, a rehearsed rollback, and an estimate of the record's
> size and storage cost per job), and the cutover risk stated: the only live accounts are the
> author's own.
>
> **Revision 9** (2026-11-18): the operational layer — a record blob for every job that ends, idempotent row writes, minimal monitoring and alerts, retried deliveries, and a gate on the orchestration history's size and replay time.
>
> It replaces the [working notes](../package-format-redesign-notes.md) (2026-11-14), which are kept
> unchanged.

**One-line model:** up to 30, a package is one `manifest.json` plus prompt files, read by an
engine that interprets each version's rules. At 31 a package is **a folder of small, typed files**
that the registry compiles at publish into **one checked document** (`resolve.json`) and
**the toolchain that checked it** (`lock.json`); the engine runs only that document, and every job's
record proves both.
- **Files:** nodes are files, flows are folders, and every member of every declaration is written
  (`null` means "deliberately none").
- **Types:** every node declares its `kind` (how it runs), its `format` (the shape it returns, in one
  JSON Schema dialect that Azure OpenAI's strict mode accepts) and its `producer` (what makes its
  output: `computed`, or `generated` by a model).
- **Lineage:** whether a value came from a model's output, or depends on a run fact, is worked out
  before any run, written on every node header and checked, and recorded on every row.
- **Decisions:** every condition and computed value is JSON Logic, written directly, checked against
  those types, and shown to people as ordinary text (`input.kind = "narrative"`).
- **Text:** every prompt, renderer, snippet, part, macro and deliverable is a template in one
  published Scriban dialect.
- **Verification:** checks live apart from production, can run per item, and gate deliverables;
  packages carry their own offline tests, and a failing test blocks publish.

## Why

The format grew one version at a time from 5 to 30, each adding a shape to one manifest. By 30 a
package's meaning depended on rules spread across the manifest, string conditions parsed by the
engine, renderers written in C#, and toolchain versions recorded nowhere. The redesign makes each
of those explicit and checkable before a job runs, which is what the product's verifiability rests
on: a record can now prove **which package, as compiled, by which toolchain**, produced every
sentence, and why every branch was or wasn't taken.

The decisions are argued on their issues. This document states them precisely enough to build from
and links to each deciding comment instead of repeating it.

## 1. The parts

| Part | Covers | Issues |
|---|---|---|
| [01 Files and scopes](01-files-and-scopes.md) | the folder layout, file kinds, every member required, ids, namespaces and qualified keys, `SourceBounds`, `FormatBounds` | #1383, #1394, #1384, #1388 |
| [02 Types and inputs](02-types-and-inputs.md) | `kind` and `format`, the dialect, text formats, format `$id`s, inputs, enum labels | #1385, #1061 |
| [03 Expressions](03-expressions.md) | JSON Logic: conditions, `computed`, every operation, null rules, `run.*`, the printer, the evaluator, `matches` and `has_word` | #1386, #1400 |
| [04 Loops and functions](04-loops-and-functions.md) | `for`, `while`, `until`, branches, `.items`, functions and `call` | #1392, #1406, #1419 |
| [05 Templates and macros](05-templates-and-macros.md) | the Scriban dialect, renderers, snippets, parts, macros, whitespace, run spots | #1425, #1397, #1409, #1412 |
| [06 Checks, deliverables and the profile](06-checks-deliverables-profile.md) | checks and check loops, deliverables as files, gating, the profile | #1395, #1396, #1406, #1382, #1408 |
| [07 The compiled documents](07-compiled-documents.md) | `resolve.json`, `lock.json`, evidence and views, in-package tests, the publish report | #1389, #1390, #1449 |
| [08 The record](08-record.md) | record storage 12 and the provenance contract | #1384, #1389, #1395–#1397, #1412, #1419 |
| [09 The engine and the pages](09-engine-and-pages.md) | loading and scheduling, the per-job ceiling, the satellites' contract, Create, History, Profile, the Editor, the diagram | #1393, #1398, #1406, #1408 |
| [10 Migration and tools](10-migration-and-tools.md) | the migration from 30, the converter, byte-equivalence, the conformance suite, author documentation, what moved to later milestones | #1447, #1423, #1449 |
| [Refusal sentences](refusal-sentences.md) | every refusal, warning, job-start and History sentence, in one table for review (#1483) | #1388 |

## 2. What changes from 30

| At 30 | At 31 | Part |
|---|---|---|
| one `manifest.json` with every node, plus prompt files | a folder: `nodes/<id>.md` (JSON header between `---` lines, then a Scriban body), a folder with `index.json` per flow, and nine top-level folders (nodes, checks, functions, renderers, snippets, macros, formats, deliverables, tests), **always present**, each with its `index.json` | 01 |
| optional members with implied defaults | every member written, `reproducible` included; `null` means deliberately none; a missing key is an error | 01 |
| node ids unique across the package | ids may repeat in disjoint scopes; every key is written with its root (`node.sections.paragraphs.draft_paragraph`, `check.…`, `deliverable.emr.note`) | 01 |
| `type` and output contracts, `prose`, `agent_*` formats | `kind`, `format` and `producer` on every node; formats are **pure shape** in a JSON Schema dialect (`plaintext`, `markdown_block`, `boolean`…); `producer` is `computed` or `generated` | 02 |
| `input.x` reads the value | inputs read like nodes: `input.x.value`, `input.x.content`, `input.x.title` (a `oneOf` label); a bare `input.x` is refused | 02 |
| a node's `when` gate | `if`; a `case` arm keeps `when` for its values, and every `case` is exhaustive | 01, 04 |
| object formats list some properties in `required` | every property in `required`, optional ones nullable; strict mode's other rules apply to a model's output | 02 |
| (none) | **model-derived**, **run-dependent** and, since revision 7, **profile-carrying** values, worked out before a run, written on every node header as `modelDerived`, `runDependent` and `profileCarrying` and checked against the derived values ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), and recorded; the flags follow content only, never a condition ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)) | 02, 03 |
| string conditions parsed by the engine; `let` and operations with `op`/`with` | JSON Logic for every condition and every `computed` value, with typed custom operations; equality is `===`/`!==` only | 03 |
| `labelField` names an item | the loop's `itemLabel`; `label` is always the display name | 04 |
| (none) | functions: named, typed sub-flows called by a `call` node, their bodies run inline in the job's orchestration | 04 |
| renderers written in C# | renderers are Scriban templates: built-in ones in the registry, a package's own in `renderers/`; renderers may be chained | 05 |
| macro placements (`at: before/after/append`) | macros only inline in agent prompts; whether a macro is offered is a recorded decision | 05 |
| a check is a node | checks in `checks/` with their own root `check.x`; check loops; aggregate checks gate deliverables | 06 |
| deliverables declared in the manifest with `sources` | `deliverables/<id>.md` with a composition template; sources are what the template reads | 06 |
| `profile.name`, `profile.signature` | a closed, typed list of profile fields, read only in deliverables and macro bodies | 06 |
| the engine reads the manifest | the engine reads only `resolve.json`; `lock.json` pins the toolchain | 07 |
| (none) | `tests/<id>.json`, run offline at publish; a failing test blocks publish | 07 |
| record storage 11, in the job's Durable entity | record storage 12: keys with roots, check, renderer, macro, decision and call rows, `producer` and model-derived on every row, run facts and run spots; rows in a table, written once, and a canonical record blob at job end | 08 |
| Mermaid 11, default layout | Mermaid 12 with ELK; renderers, macros, functions and checks drawn | 09 |

## 3. Scope

Every Milestone 39 issue ships in this one version and its release, **except #1422** (the
Serverless Workflow export), which moved to Milestone 40 on 2026-11-17: it is a view, so it can
follow without a new spec version.

| Kind | Issues |
|---|---|
| **The grammar** (what a valid package is) | #1383, #1384, #1385, #1386, #1392, #1394, #1395, #1396, #1397, #1400, #1406, #1408, #1409, #1412, #1419, #1061 |
| **The compiled documents** | #1389 (`resolve.json`), #1390 (`lock.json`), #1449 (tests) |
| **Engine and deployment**, shipping in the same release | #1393 (the per-job ceiling, retries and job limits) |
| **Tools and views**, shipping in the same release | #1398 (the diagram), #1425 (the dialect's published spec), #1447 (the migration) |
| **Closes through another issue** | #1382 (when #1406 is verified) |
| **The design itself** | #1388 |

What moved to Milestones 40 and 41, and the issues filed for later, are listed in
[part 10](10-migration-and-tools.md).

## 4. Libraries

| Library | Use | Pin |
|---|---|---|
| Scriban | every template | locked, caret range from the registry (#1390) |
| Markdig, with its advanced extensions | the Markdown formats | **exact** (it floats at `0.41.*` today) |
| `Corvus.Text.Json.JsonLogic` (Apache-2.0) | evaluating JSON Logic, with our custom operations and **our null rules overriding its defaults** | **exact**, behind our own interface |
| `Corvus.JsonSchema` (Apache-2.0) | validating values against formats; **our own checker** enforces the dialect | **exact**, behind our own interface |

Moving either Corvus library to json-everything later (paying its maintenance fee) is #1458 and
#1459; the swap must leave golden tests and a corpus re-render byte-identical
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).

## 5. Phasing

The whole version ships at once ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6904411546)),
in this order:

1. **Release the contracts first:** the package-format spec (`package-format-v31.md`, its schemas,
   the conformance suite and `SourceBounds`), the agents catalogue (new attested agent versions,
   `bindings.json` keyed by format and producer and now holding each `x-instruction`, and its
   formats rewritten as pure shape with every property in `required`) and the provenance contract
   (record storage 12).
2. **Build, in thirteen areas,** each a sub-issue of one build parent issue filed from this design:
   1. byte-equivalence fixtures (today's prompts and notes, captured first);
   2. parser and file model;
   3. types;
   4. JSON Logic;
   5. resolver and lock;
   6. engine loading and scheduling;
   7. prompts and macros;
   8. checks, deliverables and the profile;
   9. the record;
   10. migration;
   11. registry and publish;
   12. Create and History;
   13. diagram.

   Every area lands with 31 **off in production**. 31 is admitted only on the dev deployment
   (`canada-east-ai-function-dev`), through `WorkflowPackages__PreviewSpecVersions=31`, against
   **dev's own package registry**, so production's registry never sees a 31 package before the
   bump. The smoke runs and a dry run of the migration happen there, with fictional inputs.
3. **Update the satellites** (the Copilot agent and the Zoom app) against the additive
   `WorkflowPackages/Current` contract, so they work on 30 and 31 alike, before the cutover.
4. **Bump the engine**, with a drain: 31 joins the compiled-in accepted versions and the preview
   setting is removed.
5. **Publish the example packages** at 31.
6. **The operator bulk-migrates every account package,** notifying owners before and after.
7. **Move the pins.**

Every deploy in these steps, not only the bump, is preceded by a drain, so no job spans two
builds ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660); part 09 § 3.6).

**Before the bump (step 4), a go/no-go gate.** The bump goes ahead only when all of these hold
on dev ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993324671)):
- **every smoke run is green,** one per construct (§ 6), with fictional inputs;
- **the byte-equivalence report has been reviewed,** every difference either expected and
  explained, or fixed ([part 10](10-migration-and-tools.md) § 3);
- **the rollback has been rehearsed and timed:** migrate, move the pins back, run a 30 job, and
  move them forward again ([part 10](10-migration-and-tools.md) § 1.7);
- **the record's size and storage cost per job are estimated** from the dry run's jobs (rows per
  job, bytes per row, the canonical record blob), and found acceptable;
- **the orchestration history's size and replay time are measured** on the largest example
  package ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677)): the history stays under 10 MB and no step's replay takes over 2 seconds.
  If either is exceeded, large function bodies move back to sub-orchestrations with a shared
  ceiling (part 09 § 3.1).

**Cutover risk.** The only live accounts are the package author's own, so the cutover puts no
outside user at risk. If a real user arrives before the cutover, the choice to ship all at once is
re-weighed (#1487).

The migration's step-by-step detail is on #1447 and summarised in
[part 10](10-migration-and-tools.md). Early and independent of all this, the client moves to
Mermaid 12 with ELK on today's diagrams (#1398), to take that risk out of the critical path.

## 6. Done

Milestone 39 is done when version 31 is live, every example package is at 31, every account
package is migrated and the pins are moved, the satellites are updated, and a live smoke run
passes for every construct, with fictional inputs: nesting, `while` and `until`, `computed`,
functions, check loops, macros, profile reads, run facts and in-package tests. Retiring old spec
versions is Milestone 40 (#1450).

This design is filed from #1388, which the package author closes after reviewing it; the build
parent issue and its thirteen sub-issues are filed from it.

## 7. Refusal sentences

Every point of the design is decided. The package author went through all of them, part by part,
on 2026-11-17 and 18; each part cites its decision. Refusal sentences are settled this way
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6984925237)).
Every sentence follows these style rules:

1. **It names the thing, says what's wrong, and says what to do.** For example: *"Node 'draft' in
   sections.paragraphs reuses the id of node 'draft' at the top level, which sections.paragraphs
   can read: rename one."*
2. **It never contains patient text or values from inputs:** only package text, ids, paths, counts
   and limits.
3. **Plain words.** No internal terms where a plain one exists ("compiled package", not
   "resolve.json"); British/Canadian spelling.
4. **One sentence, two at most, ending with the fix as an instruction** ("Rename one.", "Set it in
   Profile.").
5. **Ids in single quotes** ('draft'); member names and paths without quotes.
6. **The same rule gets the same words everywhere:** the Editor, publish, import, CI and the Create
   page.
7. **Sentences clinicians see** (the Create page, job start, History) **avoid format terms
   entirely:** *"This workflow needs your signature. Choose one in Profile."*

**Milestone 39 uses the sentences as written** in the parts, with no manual review, so the build
is never held up by wording; the conformance suite pins each one to a valid and an invalid test
package. **All 372 are listed in [refusal-sentences.md](refusal-sentences.md)**, each with its part,
section and audience (author or clinician), and **reviewed in #1483 (Milestone 40)**, concurrently
with the build. Changing a sentence's words needs no new spec version
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6985257872)).

**Issues filed while deciding them,** for later milestones unless noted:

| Issue | About |
|---|---|
| #1465 | line endings normalised to Windows `\r\n` instead of `\n` |
| #1466 | revisiting the `reproducible` member |
| #1467 | snippets carrying data types |
| #1468 | dynamic snippet folder sizes |
| #1469 | scoping and namespaces for catalogue and package ids |
| #1470 | the Editor suggesting `oneOf` for code-like enums (Milestone 41) |
| #1471 | the whole JSON Logic operation set |
| #1472 | a broader `has_word` |
| #1473 | author documentation for "compute it in a node, then name it" (Milestone 39) |
| #1474 | requiring a non-null `maxParallel` |
| #1475 | the `for` forms refused in templates |
| #1476 | narrowing the snippet guard to refuse only recursion |
| #1477 | `requires.evaluator` and off-the-shelf components throughout |
| #1478 | requiring model-derived or run-dependent values in reads |
| #1479 | `via: "producer"` |
| #1484 | revisiting empty snippet-folder item bodies |
| #1485 | positional pairing across the format (index access is refused in templates meanwhile) |
| #1483 | reviewing every refusal sentence ([refusal-sentences.md](refusal-sentences.md)) |

Earlier: #1453 (null semantics), #1455 (a `semantics` block), #1456 (an input `default`), #1457
(a macro placed twice), #1458 and #1459 (json-everything), #1461 (an empty `all_pass`).

Most refusal sentences are written in this design for the first time: the issues decide that each
rule gets a sentence, and give the exact words for only a few.
