# 10. Migration and tools

This part covers how every package reaches 31 and the tools that ship with the version: the
migration from 30 (#1447), summarised here with its step-by-step detail kept on the issue; the
condition converter's logic, which ships in the Milestone 39 library built in #1447; the byte-equivalence
harness and its fixtures; the snapshot test the migration writes (#1449); the conformance suite;
the author documentation to rewrite; the satellites' work; and what moved to Milestones 40 and 41.
The engine's preview setting and dev registry are in [part 09](09-engine-and-pages.md#2-the-dev-preview-setting-and-devs-registry).
The points this part left open were
[decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)
on 2026-11-18, and what that decision left to the build was [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609) the same day, with
`migration-report.json`'s shape [decided too](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967).

## 1. The migration in summary

The detail, and every later correction, is on #1447. In one paragraph: **one library rewrite from
30, chained from older versions; byte-equivalence reported, never a gate; account packages
migrated and published by the operator, with hand edits made by the operator, pins moved always,
and owners told before and after.**

### 1.1 The tool

- **`WorkflowPackageMigration30To31`** in `Consultologist.PackageFormat`, beside the 24 → 25,
  25 → 26 and 27 → 28 rewriters. **It reads specVersion 30;** an older package is first raised
  through the existing chain in the same run
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447),
  [class name](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)).
- **Callers in Milestone 39:**
  - `scripts/migrate-workflow-package.cs --to 31`, for the public repository;
  - the operator's bulk run over account packages (§ 1.5);
  - the byte-equivalence harness (§ 3).

  #1447 also names the Editor as a caller. The Editor can't show or edit 31 packages as editable
  until Milestone 41, so **the Editor's "upgrade to 31" arrives with Milestone 41** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609));
  the operator's bulk migration does it in Milestone 39.
- **Output:** the migrated package as a folder (part 01's layout), a list of notices, a migration
  report, and a snapshot test (§ 4). The shape of the result matches the earlier rewriters', so
  every caller treats every rung alike.
- **The migration never writes an unknown member** ([part 01](01-files-and-scopes.md)).

### 1.2 What the rewrite does mechanically

Every rewrite decided in parts 01 to 07 is listed here, with the part that defines the 31 form.

| At 30 | At 31 | Part |
|---|---|---|
| one `manifest.json` with every node, prompts as files | files and folders. **All nine top-level folders are written, each with its `index.json`, even when empty** (`"items": []`): `nodes/`, `checks/`, `functions/`, `renderers/`, `snippets/`, `macros/`, `formats/`, `deliverables/`, `tests/`; each function folder gets its `snippets/`, `macros/`, `formats/` and `renderers/`. Flow folders get their `index.json` | [01](01-files-and-scopes.md) |
| the manifest's `nodes`, `macros` and `snippets` lists | moved into `nodes/index.json`, `macros/index.json` and `snippets/index.json`, members by bare id; the manifest keeps identity and inputs | [01](01-files-and-scopes.md) |
| `templating` in the manifest | dropped; `lock.json` and `requires` record the Scriban version | [01](01-files-and-scopes.md) |
| optional members omitted | every member written, `null` for none | [01](01-files-and-scopes.md) |
| `\r\n` line endings | normalised to `\n`, as import and publish do | [01](01-files-and-scopes.md) |
| node headers | **`reproducible` written on every node**, from the derived value (`rendered` and `computed` `true`, `agent` `false`, a flow node from its body, a `call` from its function), and the migration report lists each node whose 30 claim differed ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)); **`producer` written on every node and check**, from its kind (`"generated"` for `agent`, `"computed"` for every other, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)); **`modelDerived`, `runDependent` and `profileCarrying` written on every node and check**, from the values the resolver derives ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1478#issuecomment-6982835861), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)); `format` written on flow nodes, from `ret` | [01](01-files-and-scopes.md), [02](02-types-and-inputs.md) |
| an id now reserved (`parts`, `items`, `value`, …) | renamed, with every reference rewritten (§ 1.9) | [01](01-files-and-scopes.md) |
| `format` with `producer` | `kind` and `format` on every node | [02](02-types-and-inputs.md) |
| `prose` and `agent_*` formats; `x-generated` | **the plain format of the same shape:** `prose` and `agent_plaintext` → `plaintext`, `agent_markdown_block` → `markdown_block`, `agent_markdown_inline` → `markdown_inline`; object formats keep their names. **`prose` that a deliverable composes is `markdown_block`** (a node with no format is `prose`): a node a deliverable's `sources` name, and every leaf a source that is a flow reaches through its `ret`s, so the Markdown 30's composer kept is not escaped in the deliverable ([part 06 § 6.4](06-checks-deliverables-profile.md#64-strict-formats)); prose that only prompts read stays `plaintext`; `agent_plaintext`, a `rendered` node and a `let` of `text` stay `plaintext` though a deliverable composes them, and are escaped there ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7082078968), [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7086202369)). `x-generated` is dropped: the node's `producer` says who wrote the value ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) | [02](02-types-and-inputs.md), [07](07-compiled-documents.md) |
| a format file | **`$schema` written** as `urn:consultologist:spec:31:dialect` | [02](02-types-and-inputs.md) |
| an object format (catalogue or package) whose `required` lists only some properties | every property listed in `required`, the optional ones made nullable (§ 1.8) | [02](02-types-and-inputs.md) |
| a classifier: a node of format `classification` whose `with` lists its values | **a package format of its answers**, `formats/<node id>_answer.json` (cut to 64 characters, A10-034; `_answer_2`, … when a format already has the name), listed after the package's other formats: `classification`'s shape with `value` an **`enum` of the `with` values in written order**, `{ "$schema": …, "type": "object", "additionalProperties": false, "required": ["value"], "properties": { "value": { "type": "string", "enum": [<values>] } } }`, one per classifier, even for two with the same values. A reply outside the values does not fit the format, so the node takes its `onInvalid`, as 30 refused it, never a silent match of no gate; reads keep `node.<id>.value.value` (§ 2.2), and a `case` on it lists its values as any enum's, with no hand edit. The prompt ends as 30's did before its last line; 30's *"Answer with exactly one of: <values>."* is dropped, so the prompt ends with the format's instruction only (A10-064, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 7), an expected difference (§ 3.2). A `with` with no values keeps `classification`, and A10-030 says `with` was dropped ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 1) | [02](02-types-and-inputs.md) |
| inputs as an array with `type`, `values`, `fields`, `items`, `expectedContent` | an object keyed by id: `text` → `markdown_block`; `enum` + `values` → **a plain `enum`, with no invented titles**; `date` → `"date"`; `fields` → `properties` + `required` listing every field, optional ones made nullable; `expectedContent` → `channel`; `description: null` added ([grammar](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6919612460), [enums](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466)) | [02](02-types-and-inputs.md) |
| 30's "one or many" union inputs | **their array form**, since inputs refuse a top-level `anyOf`; a `string.contains` on one is a hand edit, since 30 searched the list written as text where 31's `in` on a list is membership (§ 2.3, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 2) | [02](02-types-and-inputs.md) |
| a long free-text `string` field | unchanged; a notice **suggests** a text format, never applied automatically | [02](02-types-and-inputs.md) |
| bare `input.x` reads, in conditions, expressions and templates | **`input.x.value`**, or **`input.x.content`** for a text-format input, since a bare `input.<id>` is refused | [02](02-types-and-inputs.md) |
| `labelField` on an input | `itemLabel` on **each loop** that iterates the input (several loops get the same value) | [04](04-loops-and-functions.md) |
| a node's `when` gate | `if` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6959885064)); a `case` branch keeps `when` | [03](03-expressions.md) |
| a `case` with no `else` whose branches miss some values (which 30 skipped) | **an explicitly empty branch for the missing values**, `{ "when": [<values>], "nodes": [], "ret": null }`, so every `case` is exhaustive and those values still read as skipped, with the reason *"case '<id>': arm for <value> does nothing"*. The `case` keeps its shape and keys: `example-conditional-sections`' `route` gains `{ "when": ["omit"], "nodes": [], "ret": null }` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)) | [04](04-loops-and-functions.md) |
| string conditions (Scriban) | JSON Logic, by the converter (§ 2); `==`/`!=` written `===`/`!==`; `!x` on text → `blank(x)`, each listed in the migration report (§ 2.3); text compared with a literal → `in` a list of that literal, with `trim` on each text read of an input (an item of an input's list too; 30 trimmed an input's text at its end; `trim` trims both), and a nullable list's `size` compared → with its null test (§ 2.2) | [03](03-expressions.md) |
| `let` (a Scriban expression) | `kind: computed` with JSON Logic in `expression`, by the converter | [03](03-expressions.md) |
| a `terms_subset` check node | a file in `checks/`, `kind: computed`, `format: check_result`, `failWith` a literal; every reader of `node.x` rewritten to `check.x` | [06](06-checks-deliverables-profile.md) |
| deliverables with `sources`, composed by the engine | a file in `deliverables/` whose generated template **reproduces today's composer bytes**, with `whenEmpty` chosen as § 1.10 says; each source read as `{{ node.<id>.content | string.strip }}`, as 30's composer trimmed each part, never the whole ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 3); a macro's snippet is not stripped (R5) | [06](06-checks-deliverables-profile.md) |
| the deliverable header's signature | **piped through a package renderer that tests it trimmed** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1509#issuecomment-7207709261), item 2): the template ends `{{ profile.signature \| renderer.signature_after_blank_line }}` where 30 appended the block after a blank line, and the migration writes `renderers/signature_after_blank_line.md` (`input` and `format` `plaintext`), whose body is `{{ if value != null && (value \| string.strip) != "" }}`, a blank line, then `{{ value }}{{ end }}`: a blank or absent signature leaves the signature and the line before it out, as 30 appended nothing, and a signature is written as 30's own bytes (a renderer's text is never escaped, so a signature holding Markdown is read as Markdown, as at 30). A snippet read only by deliverables whose body, trimmed, ends with the read after one blank line or one line ending pipes it through `signature_after_blank_line` or `signature_after_line_end` (one line ending in place of the blank line; `composer-macros`' `author_line`, `example-v12-constructs`' `closing`); a signature with text after it, and a deliverable whose only piece is the signature, keep `{{ profile.signature }}`. Each renderer is written only into a package that needs it and listed in `renderers/index.json` in the order first used (the macros before the deliverables); a notice (A10-089: *"The signature is written through renderer '\<id\>', which leaves it and the line before it out when the signature is blank, as specVersion 30 did."*). A body `if` on the trimmed read is refused (S05-007, [part 05 § 2.5](05-templates-and-macros.md)), and so is a guard node, whose rule would read the profile (S03-018) | [05](05-templates-and-macros.md), [06](06-checks-deliverables-profile.md) |
| a deliverable none of whose sources ran (each an `if` with no `else`, or a `case` with an empty branch), which 30 skipped | **an `if` that says a source ran**: `{ "===": [ { "var": "node.<source>.state" }, "completed" ] }` for each source, joined by `or`, after 30's own `if` by `and`, so the deliverable is skipped as at 30 instead of composed empty and released; a notice says so (A10-079: *"\<file\>, if: written to skip the deliverable unless \<reads\> ran, as 30 skipped a deliverable none of whose sources ran."*). When 30's own `if` is a hand edit (the converter refused it), nothing is joined to it; a second hand edit on that `if` names the term, verbatim, for the operator to join to the condition by `and` (A10-084: *"every source may not run, and 30 skipped the deliverable when none ran; join \<term\> to 30's condition by and."*). The term is `.state === "completed"` where the deliverable's `if` can read the source's state, else `.value !== null`; a source whose run it cannot read at all is a hand edit (A10-086: *"every source may not run, and the migration cannot read whether \<sources\> ran; write the if that skips the deliverable when none of its sources ran, as 30 skipped it\<, joining \<term\> for the others\>."*, the last clause only when other sources have a term), and 30's own `if` is kept as converted ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 8). A source that always runs leaves the `if` as it was, and so does a `while`: 30 composed a while that ran no pass as empty text and released the deliverable, as 31 does; beside another source its blank-line separator is kept, which no `if` can drop: an expected difference, `while-no-pass-separator` (§ 3.2; `composer-sources` `summary_then_opening`). A new package is warned instead (A11-010) ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459), Q3) | [06](06-checks-deliverables-profile.md) |
| a node only deliverables with a condition need (30 ran only what an applicable deliverable needed) | **each leaf of it gets those deliverables' `if`**, one or joined by `or` (a `for` has no `if`, so its body's leaves take it); a classifier, what a condition reads and a `while` are never gated; a notice (A10-087: *"\<file\>, if: written so that the node runs only when deliverable \<ids, joined by " or "\> applies, as 30 ran only what an applicable deliverable needed."*) ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 1). A gated source runs whenever its deliverable applies, so R15's row above judges it by 30's always-valued and joins no term for it | [04](04-loops-and-functions.md) |
| 30's completion reply: the documents as password-protected PDFs, or the link-only email | **the root deliverables folder → `delivery: ["pdf"]`**, `bundle: null`, and no destination folder: the `pdf` kind sends the same message and falls back to the link-only email as 30 did (part 06 § 8; [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7082078968)) | [06](06-checks-deliverables-profile.md) |
| built-in renderers in C# (`concept_bullets`, `concept_context`) | **built-in Scriban renderers in the registry**, versioned and locked, their ids reserved; piped as `\| renderer.concept_bullets` | [05](05-templates-and-macros.md) |
| whitespace forms #1412 refuses, `-` on a tag | rewritten to the accepted forms | [05](05-templates-and-macros.md) |
| simple `{{ if }}…{{ end }}` blocks in prompts | lifted into parts | [05](05-templates-and-macros.md) |
| a `{{ for m in macro.<folder> }}… {{ m.prompt }}` loop over a macro folder | **unrolled into one placement per macro in the folder**, in the folder's order: a placement inside a `for` is refused (A07-009), so each macro is placed at the top level of its template, and its own `if` decides it. `example-macros`' `prompts/summary.md` is the fixture ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7073314647)) | [05](05-templates-and-macros.md) |
| a body `if` around a macro placement (`{{ if input.include_counselling == true }}{{ macro.x }}{{ end }}`) | **hoisted into the placement's header `if`**, joined with the macro's own `if` by `and`: a placement inside an `if` is refused (A07-009). `example-macros`' `standing_draft.md` is the fixture ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7073314647)) | [05](05-templates-and-macros.md) |
| a bare read of a node or an input in a template (`{{ input.consult_draft }}`) | **its `.content` form for text, its `.value` form otherwise**: a bare read of an object or a list is refused (A07-007, A07-008). `example-macros` is the fixture ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7073314647)); A date input read whole is written `{{ input.x.value | date.to_string "%d %b %Y" }}`, 30's `09 Nov 2026` ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 2); a new package keeps the ISO form (#357) | [05](05-templates-and-macros.md) |
| a `for` over a loop node (`{{ for e in node.<loop> }}`), whose 30 list held only the items that ran | **over `.value`**, the completed items' values ([part 04 § 2.5](04-loops-and-functions.md#25-reading-a-loop-from-outside)), each written as the item (`e.content` → `e`), where the body reads only each item's text (its `ret` a text format, or nothing of the item read); otherwise **over `.items`** with the body wrapped in `{{ if e.state == "completed" }} … {{ else }}{{ end }}`, inside the loop's own marks, unless it already tests the item's state (30's `skipped`), so an item its `until` or `max` left `not_run` writes no fixed text. A new package is warned instead (A11-011) ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459), Q4) | [04](04-loops-and-functions.md), [05](05-templates-and-macros.md) |
| a `for` whose `.content` is read (by a prompt, a rendered body, an expression or a deliverable), whose items 30 trimmed as it composed them | **a `rendered` node that strips each item**, written as the last node of the loop's body and made its `ret` ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1509#issuecomment-7207709261), item 1): its id the old `ret`'s with `_stripped` (fitted as a classifier's id is), `producer: computed`, the old `ret`'s label, `description: null`, the old `ret`'s `.content` text format (`markdown_block` or `markdown_inline`, else `plaintext`), its `if` `{ "===": [ { "var": "node.<old ret id>.state" }, "completed" ] }` and its body `{{ node.<old ret id>.content \| string.strip }}`, reading its sibling by bare id (a rooted key is refused inside the loop's own body, S03-014); the loop's `ret` names it by its rooted key (`composer-sources`' `node.topic_loop.topic_gate_stripped`). The reads of the loop's `.content` stay as written: the engine builds it from the stripped items. A loop returned by a loop is stripped at each level, the outer node reading the inner loop's `.content`, and the formats of the loops and flows that return it follow the new `ret`. A loop read only through `.items` or `.value` is left as it was. A notice (A10-088: *"Loop '\<id\>' returns each item through the new node '\<new id\>', which strips the item's text as specVersion 30 did."*). A loop whose items have no one text format (an `if` or a `case` of a Markdown and a plain branch) gets no node and keeps its `ret` and format: a hand edit on its `index.json`, member `ret` (A10-090: *"loop '\<id\>' returns items of more than one text format, so its items are not stripped as specVersion 30 stripped them; strip each branch's text where it is written."*), which, unlike the other hand edits of § 1.4, publish does not refuse (for the owner; no fixture has the shape). The node's texts are 31's alone, an expected difference (`loop-item-strip-node`, § 3.2) | [04](04-loops-and-functions.md), [05](05-templates-and-macros.md) |
| a member read through an object that can be null (`{{ input.patient.age }}` of an input not required; a field that can be null; a node's `.value` when it may not run), which rendered empty at 30 | **wrapped in its guard**, the form A11-009 accepts: `{{ if input.patient.value }}{{ input.patient.value.age }}{{ end }}`, each object the read steps through joined by `&&`, the tag's outer marks moved onto the guard; a `for` over such a list is wrapped whole. In a lifted part's text, which holds no `if` (A07-002), a path the part's own `if` does not assert is a hand edit (A10-080, § 1.4) ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459), Q5) | [05](05-templates-and-macros.md) |
| `onInvalid: flag` (a reply that did not fit was kept) | **`retry`**: one more attempt with the validation error, then the node fails; a notice (A10-013) says so, and the owner's before-and-after notification lists it ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 3) | [05](05-templates-and-macros.md) |
| `failIfEmpty` on agent nodes | the failure sentence, or null (part 05 § 1.2); 30's true, false or absent is what the migration converts from | [05](05-templates-and-macros.md) |
| optional macros: per-run choices with a `default` | **boolean inputs** (§ 1.3) | [02](02-types-and-inputs.md), [05](05-templates-and-macros.md) |
| a macro the model chose: a macro a prompt places whose instruction left the choice to the model (30's mechanics sentence begins *"If so, write …"*, so every one was optional, where 31 requires an offered macro's marker exactly once, part 05 § 8.6) | **a classifier gates it** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171840295), item 1): for each agent that places it (its prompt, or a snippet its prompt places), a `judgement` agent node `<macro id>_applies` (fitted to 64 characters, A10-034; numbered `_applies_2`, … when a node has the id, S10-006), written as that agent's sibling immediately before it (inside its loop or branch), its `if` the macro's offer condition (its own `if`, its placements', and a choice input's, § 1.3), its prompt the reads the placing prompt makes outside any `if` or `for` (an input, a node, a check, the item; never a part, a macro or a snippet, whose text would stand for the question), each on its own line, then an **empty question line**, a hand edit (A10-077, § 1.4) that publish refuses until it is written (A11-013). The macro's `if` gains `{ "===": [ { "var": "node.<classifier>.value.answer" }, true ] }`, joined by `and` after the rest (one per placing agent); with more than one, a condition 30 did not have (each node's model chose on its own), the macro's `if` is a hand edit too (A10-078). The "otherwise nothing" clause of the instruction is not rewritten by code, which cannot find it reliably: the hand edit says to remove it. A macro with an empty instruction, one whose instruction is a template (`{{ "" }}`, another macro's text: A10-042), one placed by the engine (`before`, `after`: D3, A10-003) and one whose placements disagree (A10-036) keep their hand edits and get no classifier. A `condition` member that compiles to this node at publish is #1534 (Milestone 40) | [05](05-templates-and-macros.md) |
| no tests | one snapshot test (§ 4) | [07](07-compiled-documents.md) |

### 1.3 Per-run macro choices become boolean inputs

Decided on [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427),
[#1385](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6957666238)
and [part 02](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6962669466):
inputs have no `default` member, so each optional macro at 30 becomes an optional boolean input,
and the macro's offer condition reads it so that a run that doesn't answer gets 30's default:

| 30's `default` | The macro's `if` at 31 | Unanswered (`null`) means |
|---|---|---|
| `false` | `{ "===": [ { "var": "input.<id>.value" }, true ] }` | not offered |
| `true` | `{ "!==": [ { "var": "input.<id>.value" }, false ] }` | offered |

When 30's macro already had an `if`, the two are joined with `and`. **The input takes the macro's
id and label.** An id that's already an input's id gets the suffix `_macro` (`letrozole` →
`letrozole_macro`), and the rename is in the migration report
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).
The Create page shows the input in three states (unset, yes, no), and unset is sent as `null`. The
new input is written:

```jsonc
"include_billing_note": {               // the macro's id
  "label": "Include the billing note",  // the macro's label
  "description": null,
  "format": "boolean",
  "required": false,
  "channel": null
}
```

Re-evaluating a `default` member is #1456 (Milestone 40).

**A `MacroChoices` entry keyed by a macro's id sets the one boolean input that macro's `if` reads, and is refused
otherwise** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7073314647)). At 30 a choice is keyed by the macro's id; at 31 the input may have another id, so
the entry is mapped through the macro's `if`, not through the id. For `letrozole`, renamed `letrozole_macro` because an input
`letrozole` exists (§ 1.9), a rerun's `{ "letrozole": true }` sets `letrozole_macro`, the input the macro's `if` reads, and
leaves the input `letrozole` alone. When the macro's `if` reads no single boolean input (or the id names no macro), the entry is
refused with a sentence. A migrated package with a
colliding choice is tested for this.

### 1.4 Hand edits, with notices

When the rewrite can't decide, it still writes the package, with **one notice per edit**, and
validation fails with sentences until each is made, as the 25 migration did. The cases:
- a body `if` too complex to lift into a part;
- macros placed on deliverables with conditions or model judgement (#1406);
- a condition or `let` the converter can't map (§ 2.3);
- **the question of a classifier the migration wrote** for a macro 30's model chose (§ 1.2), on the prompt's last line,
  with the "otherwise" clause taken out of the macro's instruction (A10-077: *"node '<id>' decides whether macro '<macro
  id>' is offered, which 30's model chose; write its yes-or-no question on the prompt's last line, and remove the
  "otherwise" clause from the macro's instruction."*); until it is written, publish refuses the node (A11-013);
- **a macro placed by more than one agent**, which gets a classifier each (§ 1.2): its `if` needs every answer yes, so it
  is split into one macro per placing node or its condition chosen (A10-078: *"macro '<id>' is placed by <n> nodes, with a
  classifier each (<ids>), and its if needs every answer yes, where 30's model chose at each node on its own; split the
  macro into one per placing node, or choose its condition."*);
- **a member read through an object that can be null where no guard can stand** (§ 1.2): in a lifted part's text, which holds no
  `if`, a path the part's own `if` does not assert (A10-080: *"the template reads <path> at line <n> through <object path>, which can
  be null, where no {{ if <object path> }} can enclose the read; move the read where that guard can stand."*); until it is moved,
  publish refuses the read (A11-009);
- **a package whose worst case passes the instances one job may open** at the deployment's configured `RunLimits.JobInstances`
  (10,000 unless set) in the bulk publish (§ 1.5) and the weekly summary, the starting value in the script, which publish refuses (A11-001): the migration counts the package it wrote as publish does, and the hand edit is on the
  outermost loop's `max` on the heaviest path, with A11-001's count and limit (A10-081: *"up to <n> steps could run in one job
  through <loop key>, and this engine runs at most <limit> for one job, so publish refuses the package; the migration guesses no
  max: lower a loop's max or split the workflow."*). **No `max` is written** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459), B2, and
  [its clarification](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179842619)): until one is chosen the package keeps its 30 version and its pin, and the bulk publish
  records it *"not migrated: <reason>"*. `example-nested-note` is the fixture: two loops over lists known only at run time, each returning its items through
  its stripping node (§ 1.2), open 120,814;
- **a 30 prompt that reads `profile.*`** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): at 31 a prompt never reads the profile
  (part 05), so the read is a hand edit, and the owner is told about it in the after-notice
  (§ 1.5), with what replaced it.

Notices that need no edit (a renamed id, a `!x` converted to `blank(x)`, a suggested text format)
are listed in the migration report and block nothing.

**The choices live beside the hand edits** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), 6). The three choices the migration
cannot make itself, the renames (§ 1.9), each deliverable's `whenEmpty` (§ 1.10) and the format each
snippet folder's items take, are a package's `options.json`, kept beside its hand edits:
`{ "renames": {…}, "whenEmpty": {…}, "folderFormats": {…} }`, every member required, each an object of
text, `{}` for none, and no other member. The script reads it (`--options <file>`, in place of its
repeatable `--rename`, `--when-empty` and `--folder-format`, which are refused together with it), as do the
tests and the operator's bulk publish, so the choices have one source, and the migration report records it
(§ 3.2). A file the migration cannot read is refused, exit **2**: a member that is no option
(*"options.json has the member '<member>', which is not an option of the migration; its members are
renames, whenEmpty and folderFormats."*), a choice missing or not an object of text (*"options.json must
give <member> as an object whose values are text; write {} for none."*), an empty id or choice, which
the flags refuse too (A10-082: *"options.json gives <member> an empty id or an empty choice; name each id
and its choice."*), or a `whenEmpty` that is not `release`, `hold` or `fail` (*"options.json gives
deliverable '<id>' a whenEmpty that is not release, hold or fail."*). A leading byte-order mark is dropped,
as a package file's is, and a file that is not strict JSON is refused with A10-019's sentence (*"The file
'options.json' does not read: it names the member '<member>' twice; fix it, then migrate."*). A package with
no `options.json` takes the defaults (§§ 1.9, 1.10; part 01 § 3), and the bulk publish (§ 1.5) says so in
its record (A10-083: *"No options.json was given, so the migration's defaults were used: a reserved id takes
its kind as a suffix, every deliverable's whenEmpty is release, and a snippet folder's items take
<folder>_item."*).

The script exits **0** when the package migrated cleanly, **1** when hand edits remain, its summary
ending *"written; hand edits remain"* (as #1447 decided), and **2** when it refused, writing no package
(unreadable input, or a version below 5)
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6985122999)).
A dry run (no `--write`) that finds hand edits also exits **1**, though it writes nothing, and its
summary ends *"(dry run; pass --write to apply)"* in place of *"written; hand edits remain"*
([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), 8, D30).

Each notice reads:
> *"Hand edit needed in <file>, <member>: <reason>."*

### 1.5 Account packages, by the operator

- **The operator runs the migration** over every account package and **publishes the migrated
  version as a new version of each owner's package,** making any hand edits, so every package
  migrates. Each package's choices are its `options.json` beside its hand edits (§ 1.4), `{}` for
  each when the operator makes none.
- **Pins move always** for a package that publishes at 31: each account pin moves to the migrated
  version at publish.
- **A package that cannot be published at 31 keeps its 30 version and its pin**
  ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179842619), the
  clarification of B2): a publish refusal such as A11-001, or a migration refusal. Nothing is published at 31 for
  it, and no `max` or other value is guessed to force it through. It keeps running at 30 until 30 is retired
  (#1450). The bulk publish records it *"not migrated: <reason>"*, with the refusal's sentence, and names its
  owner in the before-and-after notification (#1447) with the reason and the deadline (A11-015: *"This package
  stops working when specVersion 30 is retired."*). Where its core idea is still wanted, a new 31 package that
  does it within the format is written (content work).
- **The bulk publish** is the engine's operator route `Admin/MigratePackage`, one account package a call, driven
  by `scripts/bulk-publish-v31.cs` over every account's active account package below 31 ([the engine at
  specVersion 31](../engine-v31.md), "The bulk publish"; [registry operations](../registry-operations.md), "The bulk
  publish"). It migrates the package the account's pin names (A11-016 when it does not: *"<package> is not the
  account's active account package; the bulk publish migrates only the account package an account's pin
  names."*), lays the operator's hand edits over it, and publishes it as that account's next version, or with
  no `--publish` runs that publish in memory and writes nothing. A hand edit counts as made only when the file its
  notice names is among the operator's edits; the migration and the snapshot count the package against the engine's
  configured limits; the record and the owners' list are written after every package; a package the engine did not
  answer for is recorded *"unknown: <fault>"*, since it may have been published, and listed for the operator to check
  (A11-017: *"The engine did not answer for this package; check whether it was published before sending this
  list."*); and a rerun carries the earlier record's published and unknown rows forward, so no package that was or may
  have been published drops out of the owners' list.
- **Recorded as a migration:** the version's publish record says *"migrated by the operator from
  <old version>"*, with the byte-equivalence report and the list of hand edits attached. The
  Editor and History show it ([part 09](09-engine-and-pages.md#62-the-job-header)).
- **Owners are told** (the standing "tell the user early" rule): before the bulk run, a visible
  notice says their packages will be migrated, their pins moved, and that editing returns with
  Milestone 41; after it, each owner sees what changed, the report and the hand edits. **The
  notice also says that 31 jobs are refused until the account's time zone is set, and when a
  profile field the package reads is missing** (part 06 §§ 10.4, 10.7); those refusals apply to
  31 jobs only ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **A package that can't be migrated faithfully may stay at 30** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): its pin is unchanged,
  and its owner is told why, until the Milestone 41 Editor lets them migrate it themselves. It
  keeps 30's load path until retirement (#1450).
- **No backfill:** old versions stay readable and old records keep their meaning; old specs keep
  their load path until retirement (#1450).

### 1.6 The order

The order is index [§ 5](index.md#5-phasing)'s, and its step numbers are used here; this section
only adds the migration's detail to the steps that concern it. Every deploy is preceded by a
drain, so no job spans two builds ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).

- **Index step 1, the contracts:** the content repositories release first
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924905094)),
  each one PR and its tags, with the engine's submodule pins moved to them:
  - `consultologist-package-format`: the spec text, its schemas (including `resolve.json`'s and
    `lock.json`'s), `bounds.json`, the built-in renderers and the conformance suite (§ 5);
  - `consultologist-agents`: the migrated formats catalogue (`prose.json` and the `agent_*.json`
    text formats retired in favour of the registry's plain formats, each format pure
    shape, `$schema` in every format file, `formats/index.json`, `agents/bindings.json` keyed by
    format and producer with no instructions (each format's instruction is its `description`,
    part 02 § 5.1), a `$id` on every format,
    `producer` removed,
    every property listed in `required` with the optional ones nullable, each rewritten format
    released as a new version, § 1.8),
    and **new Foundry versions of every attested agent whose embedded schema changes**, with the
    catalogue pins moved;
  - `consultologist-provenance`: the new record contract (storage 12).
- **Index step 2, the build, ends with a dry run on dev:** the build behind
  `WorkflowPackages__PreviewSpecVersions=31` on `canada-east-ai-function-dev`, with dev's own
  registry in `consultdevcaeast`: the example packages migrated and smoke-run there, and the bulk
  migration run over copies of account packages, all with fictional inputs
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427)).
  The copies are the account packages' sources only (they hold no patient text), published to
  dev's registry under the same owners' ids, readable only by the operator, and deleted when the
  dry run ends ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)).
  The dry run measures real durations against the 30-minute job limit (part 09 § 3.4; [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **Index step 3, the satellites,** are released and verified against migrated public packages
  (§ 7).
- **Index step 4, the bump:** the engine accepts 31 beside 30, released after a drain, as with 29
  and 30.
- **Index step 5, the public example packages,** migrate in one PR in `consultologist-workflows`,
  tagged one per push, with a live smoke run each.
- **Index step 6, account packages:** the notice to owners, the operator's bulk run (migrate, hand
  edits, publish, pins moved), then each owner's report (§§ 1.5, 1.11).
- **Index step 7, the production and operator pins move,** on the package author's word.
- **Afterwards, retirement of old specs** is #1450 (Milestone 40).

### 1.7 Rollback: pins back

Until old specs retire (#1450), the 30 load path stays, so **the operator can move account and
production pins back** to the pre-migration versions if the new release misbehaves. Each move is
recorded and announced to owners. The engine keeps running 30 throughout
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924497114)).

- **The rollback is rehearsed on dev before the bump** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993324671)): after the
  dry-run migration, the operator moves the pins back, runs a 30 job to completion, moves the
  pins forward again, and records how long each move took. A rollback that hasn't been
  rehearsed doesn't count towards the go/no-go gate (index § 5).
- **The dry run also estimates the record's size and storage cost per job:** rows per job, bytes
  per row and the canonical record blob, from the dry run's jobs, reported before the bump (index § 5, step 4). It's one of the gate's conditions.
- **The dry run also measures the orchestration history's size and replay time** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6993822677)) for the
  largest example package. The history must stay under 10 MB and no step's replay may take over
  2 seconds; if it's exceeded, the bump waits and #1493 (sub-orchestrations with a shared ceiling, Milestone 40) is pulled forward. (part 09 § 3.1). It's one of the gate's conditions.
- **A 30 version published after a rollback is migrated again before pins move** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), so a
  fix made at 30 while pins were back isn't lost when they move forward.
- **Library break points:** when an engine upgrade adds a break point (part 07 § 4.4), the
  operator has a republish path ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)): the affected 31 versions are republished, their
  `lock.json` taken at the new versions after the corpus re-render, and their pins moved, with
  the owners told as for the migration.

### 1.8 Every property required

At 31 every property of every format is listed in `required`, and an optional one is nullable
(part 02 § 3.3). The migration rewrites today's formats to match ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6959885810), [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6959888494)):
- **package formats and inputs' object fields** in each migrated package;
- **the catalogue's formats,** released as new versions in the contracts release (index § 5,
  step 1; § 1.6).

Agents then return explicit `null`s for optional fields where they used to leave the key out.
**The record and History treat an explicit `null` as absent,** and the byte-equivalence report
(§ 3.2) notes the difference rather than failing on it.

### 1.9 Reserved ids are renamed

An id that 31 reserves (`input(s)`, `node(s)`, `check(s)`, `function(s)`, `test(s)`, `snippet(s)`,
`macro(s)`, `part(s)`, `renderer(s)`, `format(s)`, `deliverable(s)`, `param(s)`, `item(s)`,
`pass(es)`, `run(s)`, `profile(s)`, `value(s)`, `content(s)`) is renamed by the migration
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1383#issuecomment-6961890910)).
**Object field names are never renamed:** they have no reserved list, since a field is always read
after `.value`, so 30's `title` fields keep their name
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6981863868)):

- **In example packages each rename is hand-chosen:** `example-nested-note`'s loop `parts` becomes
  **`sections`**, so `nodes/parts/…` becomes `nodes/sections/…` and `node.parts.…` becomes
  `node.sections.…` everywhere, `ret` values included. A hand-chosen rename is the `renames` of the
  package's `options.json` (§ 1.4): `{ "parts": "sections" }`.
- **In account packages the kind is added as a suffix:** a `for` loop `parts` becomes `parts_for`.
- **A rename that collides** with an existing id gets a numeric suffix (`_2`, `_3`, …), the id
  shortened first so the whole stays within 64 characters, and is listed in the report ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)).
- **Every rename is reported,** and every reference to it is rewritten: reads, `ret`, `in`,
  conditions, templates, test keys.

### 1.10 `whenEmpty` for migrated deliverables

[Decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765):
- **Account packages:** `"release"`, which keeps 30's behaviour (30 produces an empty deliverable
  as is).
- **Example packages, hand-chosen:** `"hold"` for `test/general`, `"fail"` for
  `example-nested-note`, and `"release"` for the rest. A hand choice is the `whenEmpty` of the
  package's `options.json` (§ 1.4), by deliverable id: `{ "consult": "hold" }` for `test/general`
  (whose `folderFormats` is `{ "standards": "standard_section" }`), `{ "note": "fail" }` for
  `example-nested-note`; a deliverable it does not name takes `"release"`.

## 2. The converter

### 2.1 Where it lives

**The conversion logic ships in the Milestone 39 library** (`Consultologist.PackageFormat`),
because the migration uses it; only the Editor's screen (a page, and the converter inside every
condition builder) waits for Milestone 41
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1423#issuecomment-6957670275)).
The tool and the migration therefore never disagree. The converter isn't part of the format: the
format and the checker only ever see JSON Logic.

**Its surface** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)).

```csharp
public static class WorkflowConditionConverter
{
    // 30's Scriban condition or let expression → canonical JSON Logic, typed against the package.
    public static ConversionResult FromScriban(string expression, ConversionScope scope);
}

public sealed record ConversionResult(
    JsonNode? Logic,                    // null when it can't convert
    string? ResultFormat,               // the type the checker gives it (boolean for a condition)
    IReadOnlyList<string> Reads,        // what it reads (input.x.value, node.x.value, item, pass, run.x)
    IReadOnlyList<string> Notices);     // one sentence per reason it can't convert
```

`ConversionScope` carries what the expression can read at its place (the package's inputs and
their formats, the nodes in scope and their formats, the loop context) so the result is
type-checked as the 31 checker will check it. The reverse direction, JSON Logic to text, is the
printer (part 03), which the converter reuses; there is one printer.

### 2.2 What it maps

30's conditions are Scriban boolean expressions (since 25, `WorkflowConditionAt25`), and 30's
`let`s are single Scriban expressions over pure builtins (`WorkflowLetAt30`). The converter maps
the subset that has a counterpart in 31's operation set (part 03), **using the table below as
written** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765));
the operations themselves, their signatures and their null rules are part 03's.

| At 30 (Scriban) | At 31 (JSON Logic) |
|---|---|
| a read: `node.x.value.y`, `node.x.content`, `item.id`, `item.value.y`, `pass.number`, `run.x` | `{ "var": "<the same path, with its root>" }` |
| a read of an input: `input.x`, `input.x.y` | `{ "var": "input.x.value" }` (or `input.x.content` for a text-format input), `{ "var": "input.x.value.y" }` |
| `a == b`, `a != b` | `{ "===": [a, b] }`, `{ "!==": [a, b] }` |
| `x == "lit"`, `x != "lit"`, with `x` text | `{ "in": [ trim(x)…, ["lit"] ] }`, `{ "!": [ { "in": [ trim(x)…, ["lit"] ] } ] }` (text is never compared with a literal by `===`, part 03 § 7), with `trim` on each text read of an input in `x`, an item of an input's list too (30 trimmed an input's text at its end; `trim` trims both), with a notice; a literal that starts with white space is a hand edit (A10-085); with `""` and `x` a text input read whole, with its null test (below); another comparison of an input's text with blank text is a hand edit (§ 2.3) |
| `a < b`, `a <= b`, `a > b`, `a >= b` | the same operators, two operands |
| `a && b`, `a \|\| b` | `and`, `or` (flattened: `a && b && c` is one `and`) |
| `!a`, with `a` boolean | `{ "!": [a] }` |
| `!a`, with `a` text | `{ "blank": [a] }` (§ 2.3) |
| `+ - * / %` on numbers | the same operators |
| `(x \| array.size)`, `x.size` on a list | `{ "size": [x] }`; compared, for a list that can be `null`, with its null test (below) |
| `string.downcase x`, `string.strip x` | `{ "lower": [x] }`, `{ "trim": [x] }` |
| `string.starts_with x "p"`, `string.ends_with x "p"` | `starts_with`, `ends_with` |
| `string.contains x "p"`, with `x` text | `{ "in": ["p", x] }` (on a list: a hand edit, § 2.3) |
| `date.parse "YYYY-MM-DD"` | the literal string, typed `date` by its comparison |
| a number, `true`, `false`, a quoted string | the JSON literal |
| `{ a: x, b: y }` (a `let`'s object) | the `object` operation (part 03) |
| `[x, y]` | a JSON array |

- **Equality is always written strict** (`===`/`!==`); `==`/`!=` are never written (#1386).
- **A 30 classifier's reads keep `.value.value`:** its format is the package format of its answers
  (§ 1.2), `classification`'s shape (`{ value: string }`) with `value` an `enum` of its values
  ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 1), so a macro gated on a classifier's *yes* reads
  `{ "===": [ { "var": "node.<id>.value.value" }, "yes" ] }`. The built-in `judgement` (part 02
  § 4) is for classifiers written at 31; the migration doesn't rewrite a 30 classifier into it
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1385#issuecomment-6982429387)).
- **The output is canonical:** the same input always gives the same bytes (RFC 8785 ordering for
  any object), so migrated packages diff cleanly and the round-trip fixtures are stable.
- **The size of a list that can be `null` keeps 30's meaning**
  ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 2; revisited in #1533).
  30 read an absent list as `[]` and counted 0; 31's `size(null)` is `null`. A comparison of such a
  `size` is written so that a `null` list counts 0: `size(x) == 0` is
  `{ "or": [ { "===": [x, null] }, { "===": [ { "size": [x] }, 0 ] } ] }`; `size(x) > 0` and
  `size(x) != 0` are `{ "and": [ { "!==": [x, null] }, { ">": [ { "size": [x] }, 0 ] } ] }`; any
  other comparison with a number is the `or` form when it holds at 0 and the `and` form when it
  doesn't, and one with anything else is `{ "if": [ { "===": [x, null] }, c0, c ] }`, where `c` is
  the comparison and `c0` the same comparison with 0 in place of the `size`. The migration report still lists each such `size` (A10-027).
- **Text compared with a literal is compared on the input trimmed** (the same ruling; [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 6):
  `x == "lit"` on text is exact membership in a list of that literal, and each text read of an input
  in `x` is written `{ "trim": [read] }`, around the read itself where an operation stands over it
  (`input.x == "lit"` is `{ "in": [ { "trim": [ { "var": "input.x.content" } ] }, ["lit"] ] }`;
  `string.downcase input.x == "lit"` is `{ "in": [ { "lower": [ { "trim": [x] } ] }, ["lit"] ] }`). 30
  trimmed an input's text (a field's and an element's too) at its end before it compared; `trim`
  (part 03 § 5.2), the one trimming operation 31's JSON Logic has, trims both ends, so an input's text
  with trailing white space answers as at 30, and one with **leading** white space, which 30 kept,
  now matches where 30's did not. Production's 31 start already normalises an input's text as 30's
  did (`V31JobStarter` calls `ConsultGenerationJobStarter.NormalizeInputs`, whose
  `CanonicalText.Normalize` turns CRLF into LF and trims the end), so in production the written `trim`
  changes an answer only through leading white space (`" yes"` now matches `"yes"`); its end-trim
  matters where inputs reach the engine as given (the byte-equivalence harness, publish's in-package
  tests). An item of a loop over an input's list (`item.value` and its fields, and a nested loop's
  item over a list field of such an item) is an input's text, which 30 trimmed per element and per
  field, so its text reads are trimmed the same. A read the author already trimmed (`| string.strip`)
  gets no second `trim`; a node's text, or an item of a node's list or of a snippet folder, which 30
  compared as it was, gets none. A `null` text is in no list of text (`trim` of `null` is `null`), as
  30's `null` equalled no text. Each one is listed in the migration report (A10-074), whose clause on
  the trim is said only where a `trim` was written. A literal that starts with white space, compared with an input's
  text, could match at 30 and never matches a trimmed text, so no trim fits: a hand edit (§ 2.3).
- **Blank text is the exception.** 30 read an input's text not given, or given as white space alone,
  as `""` (a field's text too), where 31's start leaves out a text input absent or blank, which then
  reads `null` (part 02 § 8), and keeps a field as given. So a text input read whole compared with
  `""` is written with its null test, as a nullable list's `size` is: `input.x == ""` is
  `{ "or": [ { "===": [x, null] }, { "in": [x, [""]] } ] }` and `input.x != ""` is
  `{ "and": [ { "!==": [x, null] }, { "!": [ { "in": [x, [""]] } ] } ] }`, `x` the input's
  `{ "var": "input.x.content" }` (or `.value`), so an input not given or blank answers as at 30; each
  one is listed in the migration report (A10-075). Any other comparison of an input's text with
  blank text (`string.downcase input.x == ""`, `input.x == " "`, `input.x.y == ""`, and an item of an
  input's list's `item.value.y == ""`) is not written: a hand edit (§ 2.3). A node's text, or an item
  of a node's list or of a snippet folder, compared with `""` is `in [""]` as above: 30 kept it as
  written.

### 2.3 What it refuses, and `!x` on text

Anything outside the table gives `Logic: null` and one notice per reason, which the migration
turns into a hand edit (§ 1.4), with a notice. In particular:
- a builtin or pipe with no 31 operation;
- a comparison the 31 checker refuses by type (for example `<` on text), using **the checker's own
  sentence** (part 03);
- a read of `profile.*` in a condition, which 31 refuses (#1408);
- an assignment, a template, or more than one statement;
- `string.contains` on a list: 30 wrote the list as text and searched it, which 31's `in` on a list
  (membership) is not, so the hand edit's reason says so (A10-073,
  [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 2);
- a comparison of an input's text with blank text other than a text input read whole with `""`
  (§ 2.2): 30 read the input's text not given or blank as `""` and 31 reads it as `null` or as given,
  so no form is written that would answer otherwise, and the hand edit's reason says so (A10-076);
- a comparison of an input's text with a literal that starts with white space (§ 2.2): 30 kept the
  input's leading white space and could match it, and the `trim` written on the input never leaves
  any, so the hand edit's reason says so (A10-085, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 6).

**`!x` on text converts to `blank(x)`**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).
It differs from 30 for **empty and whitespace-only text**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7042190908)).
At 30 only `null` is falsy for a text: `""` is truthy, and an unset optional text input reads `""`,
so `!input.x` never holds at 30. `blank` counts `null`, `""` and whitespace-only text as blank,
which is what an author writing `!x` on a text meant. So **the migration report lists every such
conversion**.

**`strip(x) == ""` on text converts to `blank(x)`** (part 05 § 7), and `!=` to its negation. Where
`x` can be `null` at 31 and 30 read it as text (a node that did not run, a field of an object input
left out), `blank(null)` holds where 30's comparison did not; that difference is **expected**
([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 8),
and the migration report lists each such conversion (A10-050).

### 2.4 How it's tested

- **Fixtures for every row** of § 2.2 and every refusal of § 2.3, with the expected bytes.
- **Semantic equivalence** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)). For every condition in the public packages and the
  fixture set, 30's evaluator on the Scriban string and 31's evaluator on the converted JSON Logic
  are run on generated fictional inputs, including `null` and absent values, and **must agree,**
  except the documented `blank(x)` cases (§ 2.3: `!x` on empty and whitespace-only text, `strip(x) == ""` on a
  `null` text), which are listed as expected
  differences in the byte-equivalence report (§ 3), never silently accepted.
- **The Editor's round trip** (expression → JSON Logic → expression, over a fixture set covering
  every operation) is Milestone 41's, with its screen (#1423).

## 3. Byte-equivalence

### 3.1 The harness

- **Built first** (build area 1), as the main safety net for an all-at-once release
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6905990797)).
- **What it does:** renders the 30 package and the migrated 31 package on fixed fictional inputs,
  with model outputs **stubbed deterministically from the formats**, and compares **every prompt
  and every deliverable**, byte for byte.
- **The stubs** are generated from each node's format, deterministically, seeded by the node's
  key, item path and pass, so the same package and inputs always give the same stubs
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).
  Format examples are Milestone 40 (#1402), so stubs don't rely on them.
- **The same deterministic harness** runs in-package tests offline (#1449) and the corpus re-render
  that must be byte-identical before any library upgrade (#1390).

### 3.2 The report

- **Reported, never a gate.** Each difference is listed with its file, its site and both texts'
  hashes, and marked **expected** where the rewrite declares and explains it (for example a
  whitespace form #1412 refuses, or a `!x` converted to `blank(x)`). An optional field that 30 left
  out and 31 sends as `null` (§ 1.8) is noted, never counted as a failure. The report **never
  blocks publish**, for public or account packages
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447)).
- **What it also lists:** every renamed id (§ 1.9), every `_macro` suffix (§ 1.3), every `!x`
  converted to `blank(x)` (§ 2.3), every suggestion made, and every node whose `reproducible` claim at 30 differed. Each is a string of `notices` in the report (below), so a notice that needs no edit is listed, never lost between the hand edits and the compared texts.
- **Expected differences.** A difference is marked **expected** only when the rewrite declares it and
  rules explain all of its bytes, each naming its ruling or section. Each difference within a text is credited on its own
  ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 5): each rule
  undoes the difference it declares wherever it finds it, whatever else differs, and is credited only where that brings
  31's text nearer 30's; what is left after every rule is the unexplained remainder. A text with a rule credited and a
  remainder is **partly explained**, named with its rules and not expected; the report counts the explained, the partly
  explained and the unexplained. The rulings so far, each listed in the report with its reason:
  - **`!x` on text → `blank(x)`:** the two differ for **empty and whitespace-only text** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7042190908); § 2.3).
  - **Two captures refused at 30 complete at 31:** `example-prior-letters` / `no-prior-letters` and
    `example-structured-intake` / `all-optional-inputs-unset`, because an empty list settles at 0 items and the job runs
    ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7042190603); part 04 § 2.4). `example-conditional-documents` / `encounter-kind-unset` is still refused: no deliverable's
    `if` holds.
  - **Reading `.content` of a skipped deliverable** reads `null` and no longer fails the job (ruling 1, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055767482)).
  - **A refused deliverable is withheld, not erased** (ruling 3, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055767482)): the rest of the job reads it as `null`,
    its state and check result stay readable, it is never delivered, and it is kept in the record under the usual retention.
    Only checks read a deliverable (part 06 § 6.7), so no compared text of a published package holds it, and no rule of the
    comparison names it (retired, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 8).
  - **A required text input supplied blank** is refused at the start, as a missing one is (ruling 6, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055767482)).
  - **Every deliverable refused by its check:** the job is **Completed**, each deliverable refused with its sentence, not
    Failed (ruling 7, [decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055767482)).
  - **A macro body's trailing whitespace:** 31 drops exactly one final line ending (part 05 § 8.6), 30 trims all trailing
    whitespace (`TrimEnd()`); each body whose text differs is listed, naming this ruling ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7073314647), 1).
  - **Plain text escaped into a Markdown deliverable** (part 06 § 6.4): unescaping the 31 text gives 30's bytes. A `let` of text is escaped too, and its computed value unescapes it ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #13); so are plain-text items joined into a Markdown loop's `.content`, which a prompt that reads the loop holds escaped (D23, [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 8). A rendered or computed node's text is read stripped as well as whole, since a migrated deliverable composes each source stripped ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 3). The rule is `escaped-plain-text`.
  - **A format's description sent** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #3): 31 sends the
    `description` of a format, at the top or on a property, where 30's sent format had none at that place; beside § 1.8's
    change the rule `sent-format` sets it aside. A description that differs from 30's, a title, `$schema` or `$id` stay
    unexpected.
  - **A classifier's answers sent as its format** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #3, format descriptions sent): 30 sent
    a classifier no format and ended its prompt with *"Answer with exactly one of: <values>."*, which the migration drops
    ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966), item 7); 31 sends the answers' format (§ 1.2) after the prompt, as every `custom` binding is sent.
    The rule `sent-format` explains exactly that: 30's last line taken off 30's text, and the custom trailer and a format of
    `value` alone, closed, an `enum` of the values 30's line lists in its order, with nothing else sent but a description
    (no title or `$id`), off 31's.
  - **A reply is never a template** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #5): 30 rendered a
    reply holding `{{` as a template before it composed; 31 never does. The rule `reply-never-a-template` puts back, where
    31's text holds such a reply as written, or stripped as a migrated deliverable reads a source (`| string.strip`; [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508), 2026-11-26, item 3), the reply as 30 rendered it (with no variable), and explains a job 30 failed
    because a reply did not render (*"Node '<id>' reply failed to render"*) where 31 completed that node and the job: the
    status, and each deliverable only 31 composed (`composer-sources` `awkward-texts`, `unparseable-reply`).
  - **An input's trailing white space is kept** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #6): where
    31's text holds an input's text value whole and 30's holds it with its trailing white space trimmed
    (`input-trailing-whitespace`; `composer-sources` `item-names-and-bounds`).
  - **A failed job's prompts follow its dispatch order** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #9):
    both jobs failed at the same reply, refused at both (an empty one by the node's `failIfEmpty`, or one its format
    refused under `onInvalid: fail`, as an answer outside a classifier's values), and each sent what its dispatch order
    reached first; a text only one side has is explained (`failed-job-dispatch-order`; `composer-sources` `failed-item`,
    `edge-answer-outside-set` `answer-outside-set`).
  - **`onInvalid: flag` is retried** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 3; § 1.2):
    30 kept a reply that did not fit its format and ran on; at 31 the node asks once more with the validation error, then
    fails. The status and each text only 30 has are explained (`flag-becomes-retry`; `edge-flag`), and in that node's
    prompt the retry's text at its end, so another rule may credit the rest (31 sends the format's description).
  - **A `before`/`after` node macro is a marker** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 7, #10; D3, § 1.4):
    30's engine placed the macro's body before or after the node's reply, joined by a blank line; at 31 the model writes
    its marker where the hand-edited instruction says. 30's reply never held a marker for it, so the comparison writes
    the marker into the captured reply where the instruction asks, alone on the reply's first line (before) or its last
    (after); a macro the model chose is never given one (above). The rule `before-after-macro-marker` explains the
    placing prompt's instruction, and in a text that holds the body only the white space between the body and the reply
    (`composer-macros`).
  - **A `while` that ran no pass beside another source** (the controller's ruling on #1508, as the owner
    [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179739459) for a flow that ran
    nothing, Q3; § 1.2): both compose the while as empty text, but 30's composer dropped its part with its separator, where
    31's template keeps the blank line between it and the next source; no `if` can drop a separator and keep the text
    (`while-no-pass-separator`; `composer-sources` `summary_then_opening`).
  - **A loop's item, stripped by the node the migration wrote** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1509#issuecomment-7207709261),
    item 1; § 1.2): a `for` whose `.content` is read returns each item through a `rendered` node that strips it, as 30
    trimmed each item it composed, so the texts that read the loop are 30's (`composer-sources` `awkward-texts`: the
    prompts `opening`, `closing`, `plan_gate.draft_plan`, `tone_case.plain_line` and the deliverables `topics` and
    `topics_then_opening` equal); the node's own text is 31's alone. The rule `loop-item-strip-node` credits it exactly
    where the node is one the migration wrote (`Migration31Result.StripNodes`, or an edit's own laid over it from
    `strip-nodes.json`) and its text is the old `ret`'s text for that item, trimmed. It is not a rule that loop items are
    trimmed: no text that 30 and 31 both produce is excused by it.
  - **An item that did not run** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966),
    item 4; Q4 stands): past a loop's `max`, or with its body skipped, an item writes nothing at 31 (§ 1.2's guard), where
    30's template loop wrote its fixed text for it; that text is 30's alone (`loop-item-not-run`; `edge-items-not-run`).
    30's fixed text for an item that never ran was 30's defect.
  - **A chosen macro's final line ending** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966),
    item 5; no migration change; R5): a macro the model chose whose body ends in a line ending stands where its marker
    stood with that one line ending dropped, as 31 renders every body (part 05 § 8.6), where 30 wrote the body whole: the
    white space after the body's last word is 30's less one line ending (`chosen-macro-final-line-ending`;
    `edge-unruled-whitespace`).
  - **An empty read's trailing space** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7199237966),
    item 5; no migration change): a 30 macro written as a snippet (D4) whose line ends in text, white space and one read
    keeps that white space at 31 where the read rendered empty, which 30's macro trimmed: the spaces or tabs before the
    line ending, or the text's end, are 31's alone (`empty-read-trailing-space`; `edge-unruled-whitespace`).
  - **An item named by its position** (part 04 § 2.7; an empty `itemLabel` field too,
    [ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171415045), item 4): the loop's
    label and the position at 31, where 30 gave the input's label (`position-names-from-the-loop`).
  - **A macro the model chose, gated by its classifier** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171840295), items 1
    and 2; § 1.2): the classifier's prompt is 31's alone, and the comparison answers it from the capture, `answer: true`
    exactly where 30's reply for the placing node held the macro's marker (`macro.<id>.content`, with the macro's 30 id where the migration renamed it); a captured reply is
    replayed as 30's model wrote it, no marker added for a macro the model chose (for one 30's engine placed before or after
    the reply, below, 30's reply never held a marker, and the comparison writes it in). The rule `macro-choice-classifier` explains the classifier's prompt, and the
    placing prompt only where it holds the macro's 31 instruction in place of its 30 one, that one or it with one
    "otherwise" clause removed, where the classifier answered yes, and lacks that 30 instruction where it answered no.
  - **One message per deliverable, where 30 sent one per job** ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7082078968), 1): a migrated package delivers each
    deliverable by `pdf` in its own message. No compared text carries it, so the report says it in its `notices`. A job whose
    every deliverable is blocked still sends the link-only email, as 30's reply did: a 30 behaviour kept, not a difference ([ruled](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7086202369)).

  Rulings 2, 4 and 5 of the same list ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1507#issuecomment-7055767482)) are not differences of the comparison: a failed loop item still
  fails the job (2, 31 keeps 30), a `while` with `checkFirst` reading its own body is refused at publish (4), and a numeric
  `enum` is supported (5).
- **It notes a package whose estimated worst case passes the job limit** (30 minutes, or a
  total-work limit, part 09 § 3.4) ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), with the estimate. The dry run on dev measures real
  durations (§ 1.6).
- **Where it goes:** attached to the publish record of each migrated account version, and shown by
  the Editor and History; for the public packages, attached to the migration PR.
- **Its shape** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6984743967)). One JSON document (`migration-report.json`) beside the publish
  report in the version's folder: the old and new versions, the hand edits made, the **`notices`** (an array of strings, `[]` for none), the
  **`options`** the package was migrated with (its `options.json`, § 1.4), the **`counts`** of the texts that differ
  (`{ "explained", "partlyExplained", "unexplained" }`), and one entry per compared text with
  `{ "site", "oldHash", "newHash", "equal", "expected", "explanation", "rules", "unexplained" }`, with
  `explanation` `null` unless `expected` is true, `rules` the ids of the rules credited (`[]` for none) and `unexplained`
  whether a remainder no rule explains is left. Like the other derived files, it never lives in source.

### 3.3 The fixtures

Captured from today's engine **before** any 31 code changes it:
- every public package's prompts and notes on fictional inputs (`example-nested-note`,
  `example-conditional-sections`, `test/general` and the rest);
- **generated deliverable templates** reproducing today's composer bytes (#1406);
- **the built-in Scriban renderers matching the C# renderers** (`concept_bullets`,
  `concept_context`, #1397);
- **the converter's** fixtures and equivalence vectors (§ 2.4).

The Editor's round trip (#1410) is no longer one of Milestone 39's safety nets; it moved to
Milestone 41 with the Editor
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924328714)).

## 4. The snapshot test

**The migration writes one test per package** from its migrated run
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6924070749)),
so every migrated package starts with one test pinning its behaviour. It follows #1449's grammar
([part 07](07-compiled-documents.md)): fictional `inputs`, every input listed; fixed `profile` and
`run` values for exactly what the package reads; `outputs` stubbed per agent node, item and pass
from the harness (§ 3.1), keyed by key with root; and `expect` holding the resulting prompts and
deliverables **by hash**, since exact text would put a whole note into the package and a hash pins
the same bytes ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6980651765)).

- **Its id is `migration_snapshot`, at `tests/migration_snapshot.json`**
  ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1389#issuecomment-6979256096)).
- Like every test, it runs offline in CI, and at publish, where a failure blocks publish. Its
  result goes in the publish report, never in a job's record.

## 5. The conformance suite

Released with the spec in `consultologist-package-format` (index § 5, step 1; § 1.6), exported by the
library's generators as for every spec bump
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)):

- **A valid and an invalid package for every refusal sentence** in parts 01 to 07; each invalid
  package carries the exact sentence it must produce.
- **Golden tests:** every built-in operation's output (any change to output bytes is a major
  version, #1390); the printer's text for every operation, with `=`/`≠`; the built-in renderers;
  Scriban dialect features (#1425).
- **Null and absence vectors** for every JSON Logic operation, one per rule in part 03's table.
- **Text-handling vectors** for `lower`, `trim`, `words` and `has_word` ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6992455660)), pinned to the
  .NET runtime and Unicode version `lock.json` records (part 07 § 4.1), so a runtime change that
  moves case mapping or word characters is caught.
- **Number vectors:** IEEE doubles, non-finite results giving `null`, and `integer` as a kind of
  `number` (part 03 § 7).
- **Markdown escape vectors:** CommonMark's backslash escapes for ASCII punctuation, and the
  first space or tab of each line as its character reference ([part 06](06-checks-deliverables-profile.md)).
- **Fixtures for the compiled documents:** fixture packages with their expected `resolve.json` and
  `lock.json` bytes and hashes, `requires` and `needs`, and the publish report (including the
  ceiling note and test results).
- **The schemas** of the 31 file kinds, of `resolve.json` and `lock.json` (with their media types,
  `application/vnd.consultologist.resolve+json` and `application/vnd.consultologist.lock+json`),
  of the publish report (`application/vnd.consultologist.publish-report+json`), and the dialect's
  meta-schema (`urn:consultologist:spec:31:dialect`).
- **The published constants:** `FormatBounds`, `SourceBounds` (as `bounds.json`,
  [part 01](01-files-and-scopes.md)), the reserved ids, the producer values (with those reserved
  for Milestone 40), and the Markdig extension list
  for the pinned Markdig version.
- **Migration vectors** ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609)). 30 packages with their expected 31 output and notices, so an
  outside implementation of the migration can be checked too.

## 6. Author documentation

**Rewritten before the example packages publish at 31**
([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6957680891)),
with JSON Logic throughout and `===`/`!==` in every example:

- the workflows repository's README (`consultologist-workflows`);
- the app's Help page;
- the spec's own examples (`package-format-v31.md` in the package-format registry);
- the pattern of computing a list, subject or argument in a `computed` node and naming it (#1473);
- the recipes in `docs/customizable-workflow/` that teach a 30 construct ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1393#issuecomment-6984396609))
  (for example `macro-classifier-gating.md`, whose `when: node:<id> == yes` becomes a macro `if`
  in JSON Logic), each updated or marked as applying to 30, and the 31 migration documented for
  authors of the public repository (`migrate-workflow-package.cs --to 31`, the notices, the
  report).

The converter's own expression syntax is documented on its page, with the screen, in
Milestone 41 (#1423).

## 7. The satellites

Decided on [#1447](https://github.com/Consultologist/Consultologist-Blazor/issues/1447#issuecomment-6957669427):
not in lockstep any more. The contract is `WorkflowPackages/Current`, which changes **by addition
only** ([part 09 § 4](09-engine-and-pages.md#4-the-satellites-contract-workflowpackagescurrent)).
Each satellite is updated **before the cutover** to work with 30 and 31 alike:

| Satellite | Today | Its work |
|---|---|---|
| **Copilot agent** (`consultologist-copilot-agent`) | `IntakeCardBuilder` builds Teams intake cards from `Id`, `Label`, `Required`, `Type`, `Values`, `Items`, `Fields`; offers `Macros` as per-run choices | add the trailing `Format` member to its mirrored `WorkflowPackageInputResponse`; use `oneOf` titles from `Format` for enum choices where present, and the bare values for a plain `enum`; offer an optional boolean as three states (unset, yes, no), sending unset as `null`; treat an empty `Macros` as "no choices"; show *"This workflow needs the web app"* with a link for any input it can't render |
| **Zoom app** (`consultologist-zoom-app`) | mirrors the engine DTOs | the same |
| **SMART panel, email intake** | read the same endpoint or start jobs | checked against the contract; no change expected |

- **Filed as issues in their own repositories,** linked from the build parent issue.
- **Released and verified against migrated public packages** (on dev first) before the engine
  bump (§ 1.6).

## 8. What moved to Milestones 40 and 41

Milestone 39 ships one version; these were decided out of it, or filed for later, during the
design.

**Milestone 40: inputs to delivery, typed and verifiable**

| Issue | What |
|---|---|
| #1422 | Export `resolve.json` to CNCF Serverless Workflow (`workflow.sw.json`), a descriptive view; moved 2026-11-17, since a view needs no spec version |
| #1453 | Revisit null in JSON Logic: required guards, or a library's default null behaviour |
| #1455 | Review a `semantics` block for `resolve.json`; Milestone 39 relies on `specVersion` alone |
| #1456 | Re-evaluate a `default` member for inputs; Milestone 39 migrates macro choices to optional booleans |
| #1457 | The same macro placed twice in one node's template |
| #1399 | The interactive graph: Cytoscape.js compound nodes, drawn from `resolve.json` |
| #1454 | Graphviz as a diagram renderer, with ports |
| #1439 | The producer values reserved at 31 (`grounded`, model text verified against the input), and recording each value's model, agent version and settings; the node's `producer` member is already in 31 |
| #1438, #1440, #1441 | the `extracted` and `attention` producers, a name for `adata`, and grounding |
| #1479 | `via: "producer"`: an agent chosen by producer alone (the value is reserved, and refused, at 31) |
| #1445 | Recording an agent's tool calls |
| #1448 | `x-phi`: marking which values can contain patient data |
| #1450 | Retiring old spec versions: refuse publish, refuse new jobs, remove load paths; old records always readable |
| #1426, #1427 | Revisit inline schemas in `format`; revisit the name `channel` |
| #1451 | Markdown formats by flavour (CommonMark, GFM, Markdig) |
| #1420 | Importing functions from other packages |
| #1417 | Personal (account-level) macros |
| #1402, #1415 | Formats carrying fictional examples; closed formats verifiable; live-model tests (#1449) |
| #1442 | Input kinds beyond text and typed values |
| #1465 | Normalising line endings to Windows `\r\n` instead |
| #1466 | Revisiting the `reproducible` member |
| #1467 | Data types for snippets |
| #1468 | Dynamic sizes for snippet folders |
| #1469 | Scoping between built-in, catalogue and package ids |
| #1471 | Reviewing the allowed JSON Logic operation set |
| #1472 | A broader `has_word` |
| #1473 | Author documentation for computing a value and naming it |
| #1474 | Requiring a non-null `maxParallel` |
| #1475 | Revisiting the `for` forms refused in templates |
| #1476 | Narrowing the snippet guard to refuse only recursion |
| #1477 | Revisiting `requires.evaluator`, towards off-the-shelf components throughout |
| #1478 | Requiring model-derived or run-dependent values in reads (both flags are already written on every node header at 31) |

**No milestone (eventually)**

| Issue | What |
|---|---|
| #1458 | Move the JSON Logic evaluator from Corvus to json-everything's JsonLogic, paying its fee |
| #1459 | Move value validation from Corvus.JsonSchema to JsonSchema.Net, paying its fee |

Either swap must leave golden tests and a corpus re-render byte-identical.

**Milestone 41: the Editor at the new format**

| Issue | What |
|---|---|
| #1423 (UI) | The condition converter's page and its place in every condition builder, with the pattern tester; the logic is the Milestone 39 library built in #1447 |
| #1410 | Deliverables as tree items with their own panes, built from blocks; its byte-identical round trip |
| #1411 | The three-column deliverable builder |
| #1413 | The step-by-step debugger |
| #1414 | The lineage view (debugger and History) |
| #1418 | The macro pane |
| #1421 | A pane for every folder |
| #1470 | The Editor's suggestion to switch between `enum` and `oneOf` |
| (editing) | Editing and publishing 31 packages at all: until then they are view-only ([part 09 § 8](09-engine-and-pages.md#8-the-editor)), "Run tests" in the Editor (#1449), and the Editor's "upgrade to 31" |

## Refusals

Sentences this part's tools write. The grammar's sentences are in parts 01 to 07; the engine's
and the pages' in part 09. The wording below is the starting text. It follows the style rules in [index § 7](index.md#7-refusal-sentences) and is used as written in Milestone 39 and reviewed in #1483 (Milestone 40); every sentence is listed in [refusal-sentences.md](refusal-sentences.md).

- The script, when hand edits remain (#1447): *"written; hand edits remain"*
- A hand-edit notice: *"Hand edit needed in <file>, <member>: <reason>."*
- The hand-edit reason for a prompt reading the profile: *"the prompt reads profile.<field>, which a 31 prompt doesn't read; move it to a deliverable or a macro body."*
- A package left at 30, in the owner's notice: *"<package> stays at version 30 for now: <reason>. Its pin is unchanged; you can migrate it in the Editor when Milestone 41 arrives."*
- A colliding rename, in the report: *"Renamed '<id>' to '<new id>'; '<id>_<kind>' was already taken."*
- The worst-case note: *"<package> could run for about <n> minutes in the worst case; this engine stops a job after <limit> minutes."*
- The publish record of a migrated version (#1447): *"migrated by the operator from <old
  version>"*
- The bulk publish's record, for a package it cannot publish (§ 1.5): *"not migrated: <reason>"*, the reason the
  refusal's sentence; in the owners' list, beside the notice above: *"This package stops working when specVersion 30
  is retired."* (A11-015)
- The bulk publish, asked for a package no account's pin names (§ 1.5): *"<package> is not the account's active
  account package; the bulk publish migrates only the account package an account's pin names."* (A11-016)
- The bulk publish's owners' list, for a package the engine did not answer for (§ 1.5): *"The engine did not answer
  for this package; check whether it was published before sending this list."* (A11-017)
- A converter refusal on type: the 31 checker's own sentence (part 03), unchanged.
- A converter refusal for anything else: *"The condition '<expression>' uses
  <construct>, which has no JSON Logic form at 31: rewrite it by hand."*

## Proposed in this part

Nothing. Every point is decided; refusal sentences are used as written and reviewed in #1483 ([refusal-sentences.md](refusal-sentences.md)).
