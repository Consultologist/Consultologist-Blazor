# specVersion 31 — the package format, redesigned

> **Status:** design, **revision 1** (2026-11-17). Milestone 39, #1388. Decided with the package
> author from 2026-11-13 to 2026-11-17; nothing in it is built yet. This is a design, not a
> step-by-step plan: the Milestone 39 issues are its sections and take their decisions from it.
> Where an issue comment disagrees with this text, the later of the two is the decision, and this
> document is corrected. Points not yet decided are marked **Proposed** where they appear and
> gathered in [§ 7](#7-proposed-points). The normative text follows in the package-format registry
> as `package-format-v31.md` with the release (§ 5, step 1).
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
- **Types:** every node declares its `kind` (how it runs) and its `format` (what it returns), in one
  JSON Schema dialect that Azure OpenAI's strict mode accepts.
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

## 2. What changes from 30

| At 30 | At 31 | Part |
|---|---|---|
| one `manifest.json` with every node, plus prompt files | a folder: `nodes/<id>.md` (JSON header between `---` lines, then a Scriban body), a folder with `index.json` per flow, and separate folders for checks, functions, renderers, snippets, macros, formats, deliverables and tests | 01 |
| optional members with implied defaults | every member written; `null` means deliberately none; a missing key is an error | 01 |
| node ids unique across the package | ids may repeat in disjoint scopes; the record uses qualified keys (`parts.paragraphs.draft_paragraph`) | 01 |
| `type` and output contracts, `prose` | `kind` and `format` on every node; formats in a JSON Schema dialect; `agent_plaintext` and the other text formats | 02 |
| string conditions parsed by the engine; `let` and operations with `op`/`with` | JSON Logic for every condition and every `computed` value, with typed custom operations; equality is `===`/`!==` only | 03 |
| `labelField` names an item | the loop's `itemLabel`; `label` is always the display name | 04 |
| (none) | functions: named, typed sub-flows called by a `call` node | 04 |
| renderers written in C# | renderers are package templates in `renderers/` | 05 |
| macro placements (`at: before/after/append`) | macros only inline in agent prompts; whether a macro is offered is a recorded decision | 05 |
| a check is a node | checks in `checks/` with their own root `check.x`; check loops; aggregate checks gate deliverables | 06 |
| deliverables declared in the manifest with `sources` | `deliverables/<id>.md` with a composition template; sources are what the template reads | 06 |
| `profile.name`, `profile.signature` | a closed, typed list of profile fields, read only in deliverables and macro bodies | 06 |
| the engine reads the manifest | the engine reads only `resolve.json`; `lock.json` pins the toolchain | 07 |
| (none) | `tests/<id>.json`, run offline at publish; a failing test blocks publish | 07 |
| record storage 11 | record storage 12: qualified keys, check, renderer, macro, condition and call rows, run facts and run spots | 08 |
| Mermaid 11, default layout | Mermaid 12 with ELK; renderers, macros, functions and checks drawn | 09 |

## 3. Scope

Every Milestone 39 issue ships in this one version and its release, **except #1422** (the
Serverless Workflow export), which moved to Milestone 40 on 2026-11-17: it is a view, so it can
follow without a new spec version.

| Kind | Issues |
|---|---|
| **The grammar** (what a valid package is) | #1383, #1384, #1385, #1386, #1392, #1394, #1395, #1396, #1397, #1400, #1406, #1408, #1409, #1412, #1419, #1061 |
| **The compiled documents** | #1389 (`resolve.json`), #1390 (`lock.json`), #1449 (tests) |
| **Engine and deployment**, shipping in the same release | #1393 (the per-job ceiling and 429 retries) |
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
   the conformance suite and `SourceBounds`), the agents catalogue (new attested agent versions and
   `bindings.json`) and the provenance contract (record storage 12).
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

## 7. Proposed points

Each part ends with "Proposed in this part": the points this design had to state that no issue
has decided yet. They are gathered here for the package author to confirm or change.

There are **186** such points. Most are a member's exact name, a refusal sentence's wording or a
small rule no issue reached. These ones cut across parts or reinterpret an issue, so they need the
package author's eye first:

1. **When a missing time zone refuses a job** ([06 § 10.7](06-checks-deliverables-profile.md),
   [09 § 7.2](09-engine-and-pages.md)): written as *only jobs whose package reads `run.date`*, as
   [#1406](https://github.com/Consultologist/Consultologist-Blazor/issues/1406#issuecomment-6920512477)
   decided; the 2026-11-17 summary said "jobs are refused until it's set", which may be read as every job.
2. **An empty or null list fails `all_pass`** ([03 § 5.4](03-expressions.md),
   [06 § 2.2](06-checks-deliverables-profile.md)): a gate that judged nothing hasn't passed;
   `count_pass(list, 0, …)` accepts nothing judged.
3. **A loop's `.value` holds only the items that completed** ([04](04-loops-and-functions.md));
   items that didn't run are seen through `.items`.
4. **`ret` is a bare child id** (`"ret": "paragraphs"`), and **a production node's own gate is
   spelled `if`**, replacing 30's `when` ([04](04-loops-and-functions.md), [03](03-expressions.md)).
5. **Strict mode's rules (every property required, no root `anyOf`) apply to agent formats only**
   (`x-agent: true`); inputs keep a partial `required` list, and `if`/`case` unions stay `anyOf`
   ([02](02-types-and-inputs.md)).
6. **A profile value passed into a function keeps the profile's restriction** inside it
   ([04 § 7.7](04-loops-and-functions.md), [06 § 10.2](06-checks-deliverables-profile.md)).
7. **`lock.json` carries `resolve.json`'s hash, and `resolve.json` names its lock by file name**
   ([07](07-compiled-documents.md)): two documents can't each hold the other's hash.
8. **Renderer sub-rows inside loops are keyed after the item path**
   (`draft_paragraph:2/1/render:1`, [08](08-record.md)).
9. **Built-in formats' `$id`s are `urn:consultologist:spec:31:format:<name>`**; only catalogue and
   package URNs were decided ([02](02-types-and-inputs.md)).
10. **A per-component "floor" in `requires`**, so a library change adopted despite a byte
    difference can still refuse older packages inside a caret range ([07](07-compiled-documents.md)).

The full lists:

| Part | Points |
|---|---|
| [01 Files and scopes](01-files-and-scopes.md#proposed-in-this-part) | 24 |
| [02 Types and inputs](02-types-and-inputs.md#proposed-in-this-part) | 25 |
| [03 Expressions](03-expressions.md#proposed-in-this-part) | 20 |
| [04 Loops and functions](04-loops-and-functions.md#proposed-in-this-part) | 17 |
| [05 Templates and macros](05-templates-and-macros.md#proposed-in-this-part) | 19 |
| [06 Checks, deliverables and the profile](06-checks-deliverables-profile.md#proposed-in-this-part) | 17 |
| [07 The compiled documents](07-compiled-documents.md#proposed-in-this-part) | 17 |
| [08 The record](08-record.md#proposed-in-this-part) | 11 |
| [09 The engine and the pages](09-engine-and-pages.md#proposed-in-this-part) | 22 |
| [10 Migration and tools](10-migration-and-tools.md#proposed-in-this-part) | 14 |

Most refusal sentences are written here for the first time: the issues decide that each rule
gets a sentence, and give the exact words for only a few. Each part's "Refusals" section marks
which wording is decided and which is Proposed.
