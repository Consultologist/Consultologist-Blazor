# Area 03: types (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Consultologist.PackageFormat` the type system of specVersion 31: the formats dialect and its checker, format lookup, the two equalities and hashes, type strings, typed reads, what a model receives, value validation, agent bindings, and the type rules of a package source that need no expression or template, with nothing in the engine calling any of it yet.

**Architecture:** New files beside area 02's reader in `src/Consultologist.PackageFormat/V31/`. Small pure services work on a format as written (`JsonNode`: a name or an inline schema) against a `FormatSet` (the formats a scope can name): `FormatDialect` (is this schema in the dialect), `FormatTypes` (type form, full form, hashes, fits), `TypeStrings`, `FormatReads`, `SentFormat`, `IFormatValueValidator`. `SourceTypes.Check` walks a parsed `SourcePackage` with them and returns refusals plus `PackageTypes` for the areas that follow; `SourceCheck.Run` is parse then types, and is what the conformance suite's `refusal-packages` are held to from here on.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), `Corvus.Text.Json.Validator` 5.8.12 (value validation only, behind `IFormatValueValidator`), xUnit in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`02-types-and-inputs.md`](../02-types-and-inputs.md) §§ 1–9 and *Refusals*; [`01-files-and-scopes.md`](../01-files-and-scopes.md) § 8; [`07-compiled-documents.md`](../07-compiled-documents.md) § 1 (canonical JSON, hashes) and § 2.4 (the two hashes); [`refusal-sentences.md`](../refusal-sentences.md). The published contract is `external/consultologist-package-format/v31/` (`dialect.schema.json`, `formats/`, `constants.json`, `sentences.json`, `conformance/`) and `external/consultologist-agents/agents/v31/` (`formats/`, `bindings.json`, `agents.json`). Area issue #1500, parent #1496, runbook [`BUILD-RUNBOOK.md`](../BUILD-RUNBOOK.md). Area 02's plan, [`area-02-parser-and-file-model.md`](area-02-parser-and-file-model.md), says what the reader already does (its D-numbers are cited below as "02-D9").

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-03` on `m39-area-03`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin under `external/` moves only to a pushed commit.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo.
- **31 stays off:** no file under `src/Consultologist.Api`, `src/Consultologist.Web`, `src/Consultologist.Admin` or `src/Consultologist.UI` changes; no accepted-version set changes. Everything is reachable only from tests.
- **Nothing the engine at 30 reads changes:** outside `V31/`, the only edits are `Directory.Packages.props` (two pins), `Consultologist.PackageFormat.csproj` (one reference) and the library guide.
- **Sentences are used as written.** A sentence with a row in `v31/sentences.json` is that row character for character with its `<…>` filled in, under that row's id. A rule with no row gets an area sentence `A03-NNN` (table below), each listed in the PR under *Decisions made during build*.
- **No sentence and no validation fault carries a value** from an input or a model: only ids, member names, keywords, JSON pointers, counts, limits and package text (index § 7 rule 2).
- **Never throw on package content.** Every public method that takes package JSON returns refusals or a result for any `JsonNode`; `ArgumentNullException` for a null argument is the only exception. Registry files (`TypeRegistry`, `AgentBindings.Read`) are the deployment's and may throw `FormatException`.
- **Treat model JSON as read-only** (area 02's rule): clone (`DeepClone`) before building a new tree from part of another.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample).
- **Goldens come from code,** are read, and are listed in the PR.
- **Constants:** dialect id `urn:consultologist:spec:31:dialect`; `FormatBounds` passes 15, depth 15, parallel 15 (already in `SourceBounds.cs`; this plan corrects its doc comments); strict mode: 100 properties, 5 levels (D3); snake_case `^[a-z][a-z0-9_]*$`; producers accepted `generated`, `computed`, reserved `grounded`, `extracted`, `attention`; channels `transcript`, `image`, `ambient-note`, `form`.
- **Commits:** small, by concern: `PackageFormat (#1500): …`, `Tests (#1500): …`, `Docs (#1500): …`; in the content repo `Format spec (Consultologist-Blazor#1500): …`. Every message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task.
- **Tests:** `tests/V31Types/`, namespace `Consultologist.Api.Tests.V31Types`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"` (runs area 02's too, which must stay green).

## Decisions this plan takes where the spec is silent

Each goes in the PR body under *Decisions made during build*.

| # | Where | Choice |
|---|---|---|
| D1 | part 02 § 3.4, index § 4 "`Corvus.JsonSchema`" | No NuGet package has that id; it is the repository's name. Values are validated with its run-time evaluator, `Corvus.Text.Json.Validator`, pinned exactly at 5.8.12 (current when the build started, as part 03 § 9 says of the evaluator), referenced by `Consultologist.PackageFormat` (where part 03 § 9 puts the Corvus-backed evaluator). `lock.json`'s `libraries["Corvus.JsonSchema"]` (area 05) is this package's version. |
| D2 | § 5.2 | Markdig is pinned exactly at `0.41.3`, the version `constants.json` names and the one `0.41.*` resolves to today. |
| D3 | § 3.1 "the property count and the nesting depth Azure OpenAI documents" | 100 properties and 5 levels (Azure OpenAI structured outputs, page dated 2026-08-24: "up to 100 object properties total, with up to five levels of nesting"), held in `StrictModeBounds`. Counted on the sent copy (D8): **properties** is every key of every `properties` object, a referenced format counted at each place it is referenced; **levels** counts object schemas only, the root object being level 1 (a text or list root is level 0), arrays and `anyOf` adding none. `<path>` is the pointer of the first object, in document order, at level 6. The engine's envelope is not counted: whether it uses a level is for the area that builds it (06 or 07). |
| D4 | S02-015 against S04-014 | Two rows state one rule in different words. Part 02 owns it here, so S02-015 is produced and S04-014 is not. `<Flow>` is `Loop`, `While`, `If` or `Case` (02-D9); `<declared>` and `<derived>` are type strings. Likewise S02-019 here, S03-016 never from this area. |
| D5 | § 1.1, § 1.2 | Derived here: a leaf's `reproducible` from its kind (`agent` false, `rendered` and `computed` true), refused with S01-027 and `<kind \| body \| function>` = `kind`. A flow node's and a `call`'s `reproducible`, and the three flags (S01-029, S02-011), depend on what is read, so they are areas 04 (part 03 § 4.4–4.6) and 05 (the edges). |
| D6 | § 2.1, § 9 reads | `FormatReads` types a read and says S02-018, S02-019 and S02-020 when asked. Finding the reads (JSON Logic `var`, templates, reference strings) is areas 04, 05 and 07, so no package reaches those three sentences yet and the suite has no case for them. |
| D7 | § 2.4 type strings | A built-in data type prints by name (`string`, `number`, `integer`, `boolean`, `date`, `null`); a name or `$ref` prints the name; `#/$defs/x` prints what it names; an object prints `{ a: T, b: U }` in written order (`{}` when empty); a list `list<T>`; a union its members in written order joined by ` \| `; a nullable type array `T \| null`; an enum or labelled `oneOf` `string {a, b}` with its non-null values, then ` \| null` when nullable. |
| D8 | § 2.3, § 3.5 a `$ref` with siblings | In the full form and the sent copy a `$ref` site becomes the target's members (without `$schema`, `$id`, `$defs`), with the site's `title` replacing the target's and the descriptions joined target first: `"<target>\n<site>"` when both exist. `formatHash` hashes the full form without `$id` at any depth and without `$defs` (local references are inlined); `$schema` stays at the root. |
| D9 | § 2.3 type equality | The type form: `title`, `description`, `$schema`, `$id` dropped everywhere; `#/$defs` references inlined and `$defs` dropped; a format name `"x"` and `{ "$ref": "x" }` are one; `{ "type": "string" }`, `number`, `integer`, `boolean` and `{ "type": "string", "format": "date" }` become references to the built-ins of those names (`integer` stays `{ "type": "integer" }`: there is no built-in of that name); `"type": [T, "null"]` in either order becomes `anyOf` of T and `{ "type": "null" }`; `anyOf` members are sorted by their canonical JSON and de-duplicated; `required` is sorted. `enum` and `oneOf` keep their order. `typeHash` is the hash of the type form; for a named format, of its schema's type form. |
| D10 | § 2.1 `.content` | On a text format it gives that format; on an object, a list or a union, `plaintext` (its canonical JSON, or the member's text); on `string`, `number`, `integer`, `boolean`, `date` and enums it is a fault for the caller to word (S05-011 and its like). |
| D11 | *Refusals* `<Format>`, `<Subject>`, `<path>` | `<Format>` is `Format '<id>'` for a file, and for an inline schema its owner by 02-D9: `Input '<id>'`, `Node '<id>'`, `Check '<id>'`, `Loop`/`While`/`If`/`Case '<id>'`, `Function '<f>'`, `Function '<f>' parameter '<p>'`, `Renderer '<id>'`, `Snippet folder '<id>'`. `<path>` is the JSON pointer into the schema (`/properties/kind`), and `the root` for the schema itself. An enum's `<Subject>` is `<Format> at <path>`. S02-021's `<Format \| Input '<id>'>` takes any of those owners. |
| D12 | § 9 snake_case | Applies to enum values in package formats and in inline schemas anywhere in a package; not to registry or catalogue formats. |
| D13 | S02-039 | Said for the package's `formats/` in place of S01-009. A function's `formats/` keeps S01-009, since S02-039's words begin `formats/index.json`. |
| D14 | § 1, § 1.3 | `kind: "let"` gets S02-008 in place of S02-007. A reserved producer gets S02-010 in place of S01-028; any other wrong producer keeps S01-028 (S02-009 has the same words). |
| D15 | § 2, § 2.2 | An `agent` whose `format` is an object gets S02-014 in place of A02-001. A `rendered` node whose format is an object or not a text format gets S02-013, `<format>` its type string. A check's format is area 08's (S06-009). |
| D16 | § 8 | An input declaring `default` gets S02-046 in place of S01-023. |
| D17 | § 8.3 | "A text input" is a format that is `plaintext` or `markdown_block` (by name or `$ref`), or a list whose `items` is one. `form` needs an object (inline, or a named format) with at least one property; a list never qualifies. |
| D18 | § 8.1 titles | S02-045 is judged on what the input's format resolves to, through every reference, lists and nullable unions included: `<property>` is the dotted path of names (`sections.title`). |
| D19 | § 8.1 | S02-040 and S02-041 judge the input's own format (the top level): `markdown_inline` by name or `$ref`; any `anyOf` at the root. |
| D20 | § 4 "Packages define only object and array formats" | A package format whose root `type` is `string`, `number`, `integer` or `boolean` (nullable or not) gets S02-034 (`integer` prints `number`); any other root that is not an object or an array gets A03-001. |
| D21 | § 7 | A package's or a function's own format is bound through `custom.<producer>`, `via` `custom`; a built-in or catalogue format through `formats.<id>.<producer>`, `via` `format`, and never through `custom`. |
| D22 | § 3.5 step 6 | `SentFormat` stops before the envelope: the engine's envelope, and so the exact bytes hashed into `sentFormatHash` if the envelope is part of them, is the area's that sends (06 or 07). `SentFormat.Hash` hashes the prepared copy. |
| D23 | § 3.5 | In the sent copy every reference is inlined (`$defs` gone), a referenced text format becoming `{ "type": "string" }` with its description (D8). Removed keywords are reported as pointers into the **full form**, sorted ordinally. `title`, `$id`, `$schema`, `x-*` and `contentMediaType` are removed without being listed; only the agent's `strictRejects` keywords are listed (`/properties/onset/format`), as part 08's `notSent` example shows. |
| D24 | part 02 § 2.2 | A flow node's derived format: `for` is `{ "type": "array", "items": T }` with T the `ret` child's declared format written as a `$ref` when it is a name; `while` is the `ret` child's; `if` and `case` are the union of their non-empty bodies' `ret` formats, one format when all are the same type (D9), else `anyOf` in body order. Declared and derived are compared by type equality. A node whose `ret` child was itself refused is not judged. |
| D25 | S02-016 | Said when the declared format is an `anyOf` and a body's format is not one of its members; `<n>` counts bodies from 1 (`then` 1, `else` 2; a case's branches in order, its `else` last), `<union>` is the declared type string. Any other mismatch (a declared member no body returns, a single declared format that differs) is S02-015. |
| D26 | § 3.1 `$schema` in S02-023 | `<n>` is 31 and `<id>` the dialect id: this reader reads 31 only. |

**Area sentences** (ids not in `sentences.json`):

| Id | Sentence |
|---|---|
| A03-001 | `Package format '<id>' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name.` |
| A03-002 | `<Format> at <path> refers to #/$defs/<name>, which the file doesn't define.` |
| A03-003 | `<Format> at <path> writes '<keyword>' as <what it is>; it is <what it must be>.` (the words of A02-001: `text`, `a number`, `true or false`, `a list`, `an object`, `null`, `a list of text`) |
| A03-004 | `<Format> at <path> uses '<keyword>', which belongs on <an object \| a list \| a string>.` |
| A03-005 | `<Format> at <path> declares no type; write type, $ref or anyOf.` |
| A03-006 | `<Format> at <path> is an object without '<keyword>'; every object writes properties, required and additionalProperties: false.` |
| A03-007 | `<Format> at <path> is a list without items; write the one schema its items follow.` |
| A03-008 | `<Format> at <path> writes anyOf with <n> member; a union has two or more.` |
| A03-009 | `<Format> declares <keyword> '<value>' (accepted: <list>).` (`contentMediaType`: `text/plain, text/markdown`; `x-markdown`: `block, inline`) |
| A03-010 | `<Subject> lists null but is not nullable; write "type": ["string", "null"], or remove null.` |

## Files

```
Directory.Packages.props                         Markdig 0.41.3; Corvus.Text.Json.Validator 5.8.12     Task 1
src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj   + the Corvus reference           Task 1
src/Consultologist.PackageFormat/V31/
  CanonicalJson.cs         RFC 8785 text, sha256: hashes                                               Task 1
  TypeRegistry.cs          the registry's and catalogue's format files, bindings, renderer names       Task 1
  FormatSentences.cs       one method per S02 / A03 sentence                                           Tasks 1–9
  FormatDialect.cs         one schema against the dialect                                              Tasks 2–3
  FormatDialect.Enums.cs   enum, oneOf, annotations, the owner's rules                                 Task 3
  FormatSet.cs             FormatOrigin, NamedFormat, lookup, cycles                                   Task 4
  FormatTypes.cs           type form, full form, hashes, Same, Fits, ListOf, UnionOf, Shape            Task 5
  TypeStrings.cs           the printer                                                                 Task 5
  FormatReads.cs           .value / .content / .title / fields                                         Task 6
  AgentBindings.cs         bindings.json + agents.json                                                 Task 7
  SentFormat.cs            what a model receives; StrictModeBounds                                     Task 7
  FormatValues.cs          IFormatValueValidator, CorvusFormatValueValidator                           Task 8
  SourceTypes.cs           Check; formats, inputs, every format member                                 Task 9
  SourceTypes.Nodes.cs     kinds, bindings, reproducible, flow formats                                 Task 10
  SourceCheck.cs           parse, then types                                                           Task 10
  SourceBounds.cs, SourceParser*.cs, SourceLayout.cs, SourceSentences.cs   amended                     Tasks 1, 9
tests/V31Types/
  TypeFixture.cs           the published TypeRegistry; schema builders                                 Task 1
  *Tests.cs                one per source file
  Golden/format-hashes.json                                                                            Task 5
tests/V31Source/           V31Contract (A03 rows), RefusalPackages* (S02 cases, SourceCheck)            Tasks 1, 11
docs/customizable-workflow/package-format-library.md   + "specVersion 31: types"                        Task 12
consultologist-package-format (m39-build)   v31/conformance/refusal-packages/**, index.json            Task 11
```

## Review Focus

1. **A format pasted from ordinary JSON Schema:** `minLength`, `pattern`, `default`, `examples`, `$comment`, `nullable: true`, `allOf`, `definitions`. Expected: one S02-021 for each keyword, all in one check, the rest of the format still judged. (Task 2, `OrdinaryJsonSchemaKeywords_AreEachRefused`.)
2. **Garbage where a schema is expected:** `properties: null`, `required: [1]`, `type: []`, `enum: [{}]`, `$ref: 5`, `items: []`, a schema that is `true`, 40 levels of nesting. Expected: refusals, never an exception, never a stack overflow. (Task 3, `AnyMutationOfAValidFormat_NeverThrows`; Task 10, `AnyOneFormatBroken_IsRefused_AndNeverThrows`.)
3. **References that loop through several files** (`a` → `b` → `a`), through `#/$defs`, or through `anyOf` and `items`. Expected: S02-032 once for each format on the loop, and every later service (`TypeForm`, `FullForm`, `TypeStrings`, `SentFormat`) returns on such input instead of recursing forever. (Task 4, `ALoopThroughTwoFiles_IsRefusedOncePerFormat`; Task 5, `ALoop_DoesNotHangTheTypeServices`.)
4. **The same shape written differently:** members in another order, `["null","string"]` for `["string","null"]`, `"date"` for its schema, a changed `description`. Expected: one `typeHash` for all; `formatHash` moves only for the description. (Task 5, `TypeHash_IgnoresOrderAndWording_FormatHashDoesNot`.)
5. **A value that fails validation holds patient text,** and numbers and dates at the edges: `1.0` and `1e2` for an `integer`, `2026-02-30`, `2026-2-3`, a 2 MB string. Expected: faults name pointers and keywords only; `1.0` and `1e2` are integers, the two dates are refused. (Task 8, `Faults_NeverCarryTheValue`; `NumbersAndDates_AtTheEdges`.)

---

### Task 1: Pins, canonical JSON, the registry and the sentence frame

**Files:**
- Modify: `Directory.Packages.props`, `src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj`, `src/Consultologist.PackageFormat/V31/SourceBounds.cs` (doc comments only), `tests/V31Source/V31Contract.cs`
- Create: `V31/CanonicalJson.cs`, `V31/TypeRegistry.cs`, `V31/FormatSet.cs` (only `FormatOrigin`), `V31/FormatSentences.cs`, `tests/V31Types/TypeFixture.cs`, `tests/V31Types/CanonicalJsonTests.cs`, `tests/V31Types/TypePinsTests.cs`

**Interfaces:**
- Produces:
  - `public static class CanonicalJson { string Write(JsonNode? node); string Hash(JsonNode? node); string HashText(string text); }` — `Write` is RFC 8785: members sorted by UTF-16 code units, numbers as ECMAScript prints a double, minimal string escapes, no whitespace. `Hash` is `"sha256:" + 64 lowercase hex` over the UTF-8 of `Write`.
  - `public enum FormatOrigin { BuiltIn, Catalogue, Package, Function }`
  - `public sealed record TypeRegistry(IReadOnlyDictionary<string, JsonObject> BuiltInFormats, IReadOnlyDictionary<string, JsonObject> CatalogueFormats, JsonObject Bindings, JsonObject Agents, IReadOnlySet<string> BuiltInRenderers)` with `public SourceRegistry Names { get; }` (the three name sets, for `SourceParser.Parse`).
  - `internal static class FormatSentences`: one `public static SourceRefusal` method per sentence, as `SourceSentences` does; this task adds only `Subject(string format, string pointer)` → `"<Format> at <path>"` with `the root` for `""` (D11). Later tasks add their sentences here.
  - `tests`: `TypeFixture.Published()` → the `TypeRegistry` read from `external/consultologist-package-format/v31/formats/`, `…/renderers/index.json` and `external/consultologist-agents/agents/v31/` (`formats/`, `bindings.json`, `agents.json`), failing loudly when a file is missing; `TypeFixture.Schema(string json)` → `JsonObject`; `TypeFixture.FormatFile(string body)` → a format file's JSON text with the `$schema` line written.
  - `V31Contract.AssertSentence` accepts `A03-` ids from a second table holding the ten rows above.

- [x] **Step 1: Write the failing tests**

```csharp
// CanonicalJsonTests
[Fact] public void Write_SortsMembers_ByUtf16CodeUnits()   // RFC 8785 § 3.2.3's example: keys "€","\r","דּ","1","😀","\u0080","ö" come out as "\r","1","\u0080","ö","€","😀","דּ"
[Theory] // number, expected text
[InlineData("1.0", "1")] [InlineData("1e2", "100")] [InlineData("0.000001", "0.000001")] [InlineData("1e-7", "1e-7")]
[InlineData("1e21", "1e+21")] [InlineData("-0", "0")] [InlineData("333333333.33333329", "333333333.3333333")]
[InlineData("4.50", "4.5")] [InlineData("2e-3", "0.002")]
public void Write_PrintsNumbers_AsEcmaScriptDoes(string written, string expected)
[Fact] public void Write_EscapesOnlyWhatItMust()           // "€$\u000f\nA'B\"\\\\\"/" → "€$\u000f\nA'B\"\\\\\"/" (RFC 8785 § 3.2.2.2)
[Fact] public void Write_KeepsArrayOrder_AndWritesNoWhitespace()   // {"b":[3,1,2],"a":null} → {"a":null,"b":[3,1,2]}
[Fact] public void Hash_IsSha256OfTheCanonicalBytes()      // Hash(JsonNode.Parse("{}")) == "sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"
// TypePinsTests
[Fact] public void Markdig_IsTheVersionTheContractNames()  // typeof(Markdig.Markdown).Assembly's informational version starts with constants.json markdig.version ("0.41.3"); Directory.Packages.props holds Version="0.41.3" exactly
[Fact] public void Corvus_IsPinnedExactly()                // Directory.Packages.props holds <PackageVersion Include="Corvus.Text.Json.Validator" Version="5.8.12" />, no * and no range
[Fact] public void FormatBounds_AreTheContracts()          // 15, 15, 15 == bounds.json formatBounds
[Fact] public void ThePublishedRegistry_HoldsTheNineBuiltIns_AndTheCatalogue()  // BuiltInFormats keys == v31/formats/index.json items; CatalogueFormats keys == classification, concept_list
```

- [x] **Step 2:** Run `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Types"`; expected: does not compile.
- [x] **Step 3: Implement.** Pin both packages (comment beside each: why exact, D1 and D2); add `<PackageReference Include="Corvus.Text.Json.Validator" />` to the format library with a line in its header comment. In `SourceBounds.cs` rewrite `FormatBounds`' comments to say what part 01 § 8 says: a `while`'s `max` (1 to 15), nested function calls (0 to 15), a loop's `maxParallel` (2 to 15 or null); each starts at 15, may be raised alone, and is never written in a package. `CanonicalJson.Write` prints a number with `double.ToString("R", InvariantCulture)` reshaped to ECMAScript's rules (exponent form only below 1e-6 and from 1e21, `e+`/`e-`, no trailing `.0`).
- [x] **Step 4:** Run the filter above, then `dotnet build Consultologist.sln`; expected: green, and the Web project still builds with the new reference.
- [x] **Step 5: Commit** `PackageFormat (#1500): canonical JSON, the type registry, and the Markdig and Corvus pins` and `Tests (#1500): …`; push.

### Task 2: The dialect checker — keywords and structure

**Files:**
- Create: `V31/FormatDialect.cs`, `tests/V31Types/FormatDialectTests.cs`
- Modify: `V31/FormatSentences.cs`

**Interfaces:**
- Produces:
  - `public enum SchemaSite { PackageFormat, RegistryFormat, Inline }`
  - `public static class FormatDialect { public static IReadOnlyList<SourceRefusal> Check(JsonNode? schema, SchemaSite site, string subject, string id, string path); }` — `subject` is D11's `<Format>` (`Format 'verdict'`, `Input 'sections'`), `id` the bare id for the sentences that print `'<name>'` or `'<id>'` alone, `path` the file the refusals carry. It checks one schema document on its own: it never looks a named reference up (Task 4 does).
  - Sentences S02-021, S02-024 to S02-026, S02-028 to S02-031, A03-002 to A03-008.

Rules, each at every depth, in document order, all reported (part 02 § 3.1–3.3, § 5.3):

| Rule | Sentence |
|---|---|
| a schema that is not a JSON object | A03-003, keyword = the member that holds it (`items`, the property's name under `properties`, `anyOf`), "an object" |
| a keyword not in § 3.1's table; or `$schema`, `$id`, `$defs`, `contentMediaType`, `x-markdown` below the root (the root's are Task 3's) | S02-021 |
| a keyword whose value has the wrong JSON type (`type` not text or a list, `properties` not an object, `required` not a list of text, `enum`/`oneOf`/`anyOf` not a list, `title`/`description`/`$ref`/`format` not text) | A03-003; the keyword's other rules are skipped |
| `type` a list that is not exactly one of the seven types plus `"null"`, in either order; or a `type` string that is not one of the seven | S02-029, `<list>` the value as compact JSON |
| none of `type`, `$ref`, `anyOf` | A03-005 |
| `$ref` beside anything but `title` and `description` | S02-031, once per other keyword |
| `$ref` that is neither `^[a-z][a-z0-9_]*$` nor `#/$defs/<name>`; `#/$defs/<name>` with no such definition at the file's root (always, in an inline schema) | A03-002 (`<name>` is the reference as written when it has neither form) |
| `properties`, `required` or `additionalProperties` where `type` is not `object` (or `["object","null"]`); `items` where it is not `array`; `enum`, `oneOf` or `format` where it is not `string` | A03-004 — except `format`, which is S02-030 |
| an object without `properties`, `required` or `additionalProperties` | A03-006, one per missing keyword |
| `additionalProperties` present and not `false` | S02-024, `<value>` as compact JSON |
| a `required` name that is no property | S02-025 |
| a property not in `required` | S02-026 |
| a property name that fails `^[a-z][a-z0-9_]*$` or is longer than `SourceBounds.IdLength` | S01-030 / S01-031 through `SourceIds`' pattern and length rules only (no reserved names: § 5.3) |
| an array without `items` | A03-007 |
| `format` other than `"date"`, or beside a `type` other than `"string"` | S02-030 |
| `anyOf` with fewer than two members | A03-008 |
| `anyOf` of exactly a scalar schema (`{ "type": "string" \| "number" \| "integer" \| "boolean" }` and nothing else but annotations) and `{ "type": "null" }` | S02-028, `<type>` the scalar's |
| a `#/$defs` chain that reaches its own definition | S02-032 (cycles through named formats are Task 4's) |

- [x] **Step 1: Write the failing tests** — one `[Fact]` or `[Theory]` row per table row, each asserting the exact sentence, for example:

```csharp
[Fact] public void AMissingRequiredEntry_IsRefused() =>
    AssertOne(Check("""{ "type": "object", "required": ["title"], "additionalProperties": false,
                         "properties": { "title": { "type": "string" }, "notes": { "type": ["string","null"] } } }"""),
        "S02-026", "Format 'verdict' at the root leaves 'notes' out of required; every property is listed in required, and an optional one is written as nullable.");
[Fact] public void ANullableScalarWrittenWithAnyOf_IsRefused()  // at /properties/age: Format 'verdict' at /properties/age writes a nullable integer with anyOf; write it as "type": ["integer", "null"].
[Fact] public void ANullableReference_IsAccepted()              // anyOf [ { "$ref": "plaintext" }, { "type": "null" } ] → no refusal
[Fact] public void ATypeArrayInEitherOrder_IsAccepted()         // ["null","string"] and ["string","null"]
[Fact] public void OrdinaryJsonSchemaKeywords_AreEachRefused()  // Review Focus 1: eight keywords → eight S02-021, each naming its keyword and pointer, plus nothing else
[Fact] public void EveryPublishedFormat_IsInTheDialect()        // each built-in and catalogue schema, SchemaSite.RegistryFormat → no refusal from this task's rules
[Fact] public void EveryRefusal_MatchesItsRow()                 // V31Contract.AssertSentence on every refusal the tests above produced
```

- [x] **Step 2:** Run the filter; expected: fail (no `FormatDialect`).
- [x] **Step 3: Implement** `FormatDialect.Check` as one recursive walk carrying the JSON pointer (RFC 6901 escaping of `~` and `/`) and a depth guard at `SourceBounds.JsonDepth`.
- [x] **Step 4:** Run the filter; expected: pass.
- [x] **Step 5: Commit** `PackageFormat (#1500): the formats dialect, keyword by keyword`; push.

### Task 3: The dialect checker — enums, annotations and the owner's rules

**Files:**
- Create: `V31/FormatDialect.Enums.cs`, `tests/V31Types/FormatDialectEnumsTests.cs`, `tests/V31Types/FormatDialectRootTests.cs`
- Modify: `V31/FormatDialect.cs` (make it `partial`, call in), `V31/FormatSentences.cs`

**Interfaces:**
- Consumes: Task 2's walk and `SchemaSite`.
- Produces: the same `FormatDialect.Check`, now complete; sentences S02-022, S02-023, S02-033 to S02-038, S02-047 to S02-054, A03-001, A03-009, A03-010.

Enums (§ 3.2, § 9), `<Subject>` by D11:

| Rule | Sentence |
|---|---|
| both `enum` and `oneOf` | S02-052 (neither is judged further) |
| an `enum` entry that is not text or `null`; a `oneOf` branch that is not exactly `{ "const": <text>, "title": <non-empty text> }` or `{ "const": null }` | A03-003 for the enum entry (`a list of text`); S02-051 for the branch, `<n>` from 1, `<shape>` the branch as compact JSON with every string value replaced by `"…"` |
| fewer than two non-null values | S02-047 (said for none or one) |
| a value twice | S02-048, once per repeated value |
| a value that is not snake_case, when `site` is `PackageFormat` or `Inline` (D12) | S02-049 |
| two branches with one title | S02-050, once per repeated title |
| `type` is `["string","null"]` and the `enum` lists no `null` | S02-053 |
| `type` is `["string","null"]` and the `oneOf` has no `{ "const": null }` | S02-054 |
| `null` listed, or a `{ "const": null }` arm, when `type` is `"string"` | A03-010 |

The root, by `site`:

| Rule | Site | Sentence |
|---|---|---|
| no `$schema` | Package, Registry | S02-022 |
| `$schema` other than the dialect id | Package, Registry | S02-023 (D26) |
| `$schema`, `$id` or `$defs` present | Inline | S02-021 |
| `$id` present | Package | S02-033 |
| root `type` a scalar | Package | S02-034 (D20) |
| root neither object, array nor scalar (a `$ref`, an `anyOf`, `null`) | Package | A03-001 |
| `contentMediaType` or `x-markdown` anywhere | Inline | S02-038, `<Input '<id>' \| Node '<id>'>` the subject |
| `contentMediaType` or `x-markdown` at the root | Package | S02-037, `<path>` `the root` |
| `contentMediaType` or `x-markdown` below the root | Package, Registry | S02-037 (in place of Task 2's S02-021 for these two) |
| a value outside `text/plain`, `text/markdown` / `block`, `inline` | Registry | A03-009 |
| root is a string with `x-markdown` and no `contentMediaType` | Registry | S02-035 |
| root has `contentMediaType: text/markdown` and no `x-markdown` | Registry | S02-036 |
| root has `x-markdown` with `text/plain`, or either annotation on a root that is not a string | Registry | S02-037 |

- [ ] **Step 1: Write the failing tests** — one per row with the exact sentence; plus

```csharp
[Fact] public void ANullableEnum_ListsNull_AndCountsOnlyItsValues()   // ["string","null"] + ["new_patient","follow_up",null] → accepted; + ["new_patient", null] → S02-047
[Fact] public void ANullableLabelledEnum_HasANullArm()                 // accepted with { "const": null }; S02-054 without
[Fact] public void RegistryEnumValues_NeedNotBeSnakeCase()             // SchemaSite.RegistryFormat, "Follow-Up" → accepted
[Fact] public void S02_051_NeverPrintsTheBranchsText()                 // { "const": "a", "label": "Secret" } → the sentence holds "…" and not "Secret"
[Fact] public void EveryPublishedFormat_IsInTheDialect_Whole()         // the nine built-ins and the catalogue's two → no refusal at all
[Fact] public void TheDialectsOwnExamples_AgreeWithTheChecker()        // every file of v31/conformance/schemas/dialect/: valid-* → no refusal as RegistryFormat; invalid-* → at least one
[Fact] public void AnyMutationOfAValidFormat_NeverThrows()             // Review Focus 2: seeded sweep (seed 1500, 2,000 mutations) over the published formats and part 02 § 8.4's inline schema: each member in turn deleted, set to null, 5, "x", [], {}, true, or wrapped 40 deep
```

If `TheDialectsOwnExamples_AgreeWithTheChecker` finds an example the checker and the meta-schema disagree on, the meta-schema is the published contract: fix the checker when the design agrees with the meta-schema, and otherwise list the file and the disagreement in this plan's closing section for the PR.

- [ ] **Step 2:** Run; expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the filter; expected: pass.
- [ ] **Step 5: Commit** `PackageFormat (#1500): enums, annotations and the rules of each kind of format file`; push.

### Task 4: Format lookup

**Files:**
- Modify: `V31/FormatSet.cs`, `V31/FormatSentences.cs`
- Create: `tests/V31Types/FormatSetTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record NamedFormat(string Id, FormatOrigin Origin, JsonObject Schema);`
  - `public sealed class FormatSet`:
    - `static FormatSet ForPackage(TypeRegistry registry, IEnumerable<SourceFormat> package)`
    - `FormatSet ForFunction(IEnumerable<SourceFormat> own, IReadOnlyCollection<string> uses)` — sees the function's own formats (origin `Function`), then the package formats `uses` names, then catalogue and built-in; a package format not in `uses` is not found (part 02 § 6.3).
    - `NamedFormat? Find(string name)` — package (or function, then used package) first, then catalogue, then built-in.
    - `JsonObject? Resolve(JsonNode? format)` — a name or a `{ "$ref": name }`-only object gives the named schema; any other object gives itself; anything else null.
    - `static string? NameOf(JsonNode? format)` — the name when `format` is a string or an object whose only keyword besides `title` and `description` is a bare-name `$ref`.
    - `IReadOnlyList<SourceRefusal> CheckReferences(JsonNode? format, string subject, string path)` — S02-017 for every bare name (the format itself or any `$ref` at any depth) that `Find` does not know, once per name.
    - `IReadOnlyList<SourceRefusal> CheckCycles()` — S02-032 once for each package or function format that reaches itself through named references (through `properties`, `items`, `anyOf`, `$defs`), `<Format>` `Format '<id>'`, `<path>` the pointer of the `$ref` that starts the loop, the refusal's path the format's file.
    - `bool IsCyclic(string name)`.
  - Sentence S02-017.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AName_IsFoundPackageFirst_ThenCatalogue_ThenBuiltIn()
[Fact] public void AFunction_SeesItsOwnFormats_ThenWhatItUses_NeverTheRest()   // package has verdict and outline; uses ["verdict"] → Find("outline") is null, Find("plaintext") is found
[Fact] public void AnUnknownName_IsRefusedOncePerName()   // "Format 'vrdict' is not a built-in, catalogue or package format." for "vrdict" twice in one schema → one refusal
[Fact] public void ALoopThroughTwoFiles_IsRefusedOncePerFormat()   // Review Focus 3: a → b → a gives two S02-032, "Format 'a' at /properties/next refers to itself; recursive formats aren't supported."
[Fact] public void ALoopThroughAListOrAUnion_IsFound()
[Fact] public void AChainThatEnds_IsNotALoop()             // a → b → plaintext
[Fact] public void Resolve_And_NameOf_ReadBothWrittenForms()   // "plaintext", { "$ref": "plaintext" }, { "$ref": "plaintext", "title": "Notes" } → name plaintext; { "type": "number" } → no name, resolves to itself
```

- [ ] **Step 2–4:** Run (fail), implement, run (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): a format name is looked up package first, and no format reaches itself`; push.

### Task 5: The two equalities, the hashes and type strings

**Files:**
- Create: `V31/FormatTypes.cs`, `V31/TypeStrings.cs`, `tests/V31Types/FormatTypesTests.cs`, `tests/V31Types/TypeStringsTests.cs`, `tests/V31Types/FormatHashGoldenTests.cs`, `tests/V31Types/Golden/format-hashes.json`

**Interfaces:**
- Consumes: `FormatSet`, `CanonicalJson`.
- Produces:
  - `public enum FormatShape { Unknown, String, Enum, Number, Integer, Boolean, Date, Text, Object, List, Union, Null }`
  - `public static class FormatTypes`:
    - `JsonNode TypeForm(JsonNode? format, FormatSet formats)` (D9) and `string TypeHash(JsonNode? format, FormatSet formats)`; for a name, the named schema's type form (so `TypeHash("date")` equals `TypeHash` of its schema).
    - `JsonNode FullForm(JsonNode? format, FormatSet formats)` (D8) and `string FormatHash(JsonNode? format, FormatSet formats)`.
    - `bool Same(JsonNode? a, JsonNode? b, FormatSet formats)` — type equality: equal type forms, with a name and its `$ref` one.
    - `bool Fits(JsonNode? value, JsonNode? expected, FormatSet formats)` — `Same`, or: `integer` where `number` is expected; a text format where `string` is expected; a member where a union holding it is expected; a union where a union holding every member is expected; `list<A>` where `list<B>` and A fits B; `T` where `T | null` is expected. Never the reverse of any.
    - `JsonNode ListOf(JsonNode item)` — `{ "type": "array", "items": <item, a name written as a $ref> }`.
    - `JsonNode UnionOf(IReadOnlyList<JsonNode> members, FormatSet formats)` — one member when all are `Same`; else `{ "anyOf": [...] }` of the distinct members in first-seen order.
    - `FormatShape Shape(JsonNode? format, FormatSet formats)` — through names and `#/$defs`; `Text` for a string with `contentMediaType`; `Enum` for a string with `enum` or `oneOf`; `Date` for `format: date`; a nullable type array or an `anyOf` of one type and `null` reports the type's shape (`IsNullable` says the rest); `Union` for any other `anyOf`.
    - `bool IsNullable(JsonNode? format, FormatSet formats)`; `string? TextKind(JsonNode? format, FormatSet formats)` → `plaintext`, `markdown_block`, `markdown_inline` or null, by the root's annotations.
  - `public static class TypeStrings { public static string Print(JsonNode? format, FormatSet formats); }` (D7).
  - Every method returns (with `Unknown`, `"?"`, or the node as written) for an unknown name or a cyclic format; none recurses past `SourceBounds.JsonDepth`.

- [ ] **Step 1: Write the failing tests**

```csharp
// TypeStringsTests — part 02 § 2.4's table, row for row
[Theory]
[InlineData("\"plaintext\"", "plaintext")]
[InlineData("{ \"$ref\": \"plaintext\" }", "plaintext")]
[InlineData("{ \"type\": \"number\" }", "number")]
[InlineData("{ \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }", "list<plaintext>")]
[InlineData("{ \"type\": \"array\", \"items\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } } }", "list<list<plaintext>>")]
[InlineData("{ \"anyOf\": [ { \"$ref\": \"plaintext\" }, { \"$ref\": \"classification\" } ] }", "plaintext | classification")]
[InlineData("{ \"type\": [\"string\", \"null\"] }", "string | null")]
[InlineData("{ \"anyOf\": [ { \"$ref\": \"judgement\" }, { \"type\": \"null\" } ] }", "judgement | null")]
[InlineData("{ \"type\": \"object\", \"required\": [\"title\",\"notes\"], \"additionalProperties\": false, \"properties\": { \"title\": { \"type\": \"string\" }, \"notes\": { \"anyOf\": [ { \"$ref\": \"plaintext\" }, { \"type\": \"null\" } ] } } }", "{ title: string, notes: plaintext | null }")]
[InlineData("{ \"type\": \"string\", \"enum\": [\"narrative\", \"table\", \"omit\"] }", "string {narrative, table, omit}")]
[InlineData("{ \"type\": \"string\", \"oneOf\": [ { \"const\": \"narrative\", \"title\": \"Narrative\" }, { \"const\": \"table\", \"title\": \"Table\" } ] }", "string {narrative, table}")]
[InlineData("{ \"type\": \"string\", \"format\": \"date\" }", "date")]
[InlineData("{ \"type\": [\"string\", \"null\"], \"enum\": [\"a_b\", \"c_d\", null] }", "string {a_b, c_d} | null")]
public void Print_IsTheTablesText(string format, string expected)

// FormatTypesTests
[Fact] public void TypeHash_IgnoresOrderAndWording_FormatHashDoesNot()   // Review Focus 4: verdict.json four ways (members reordered; ["null","string"]; "date" for its schema in a field; a changed description) → one typeHash; formatHash differs only for the description
[Fact] public void ABuiltInsName_EqualsItsSchema()            // Same("date", {type:string, format:date}); Same("string", {type:string}); not Same("plaintext", {type:string})
[Fact] public void FormatHash_LeavesOutId()                   // the catalogue's classification with another $id → same formatHash
[Fact] public void FullForm_InlinesEveryReference_AndJoinsDescriptions()   // judgement: reason becomes plaintext's members; description == "Plain text, with no markup.\nWhy, in a sentence or two, from the text given." when the field carries the second sentence
[Fact] public void TypeForm_KeepsReferencesByName()           // judgement's reason stays { "$ref": "plaintext" }
[Theory] public void Fits_IsOneWay(...)                       // integer→number yes, number→integer no; markdown_block→string yes, string→markdown_block no; plaintext→plaintext|classification yes, the reverse no; list<integer>→list<number> yes; plaintext→plaintext|null yes
[Fact] public void UnionOf_OneFormat_IsThatFormat_AndTwoAreAnAnyOfInOrder()
[Fact] public void Shape_ReadsThroughNamesAndDefs()
[Fact] public void ALoop_DoesNotHangTheTypeServices()         // Review Focus 3: a → b → a through TypeForm, FullForm, Print, Shape, Fits: each returns
// FormatHashGoldenTests
[Fact] public void ThePublishedFormats_HashAsFrozen()         // Golden/format-hashes.json: { "<id>": { "typeHash", "formatHash", "typeString" } } for the nine built-ins and the catalogue's two
```

The golden is written by the test itself when the environment variable `UPDATE_V31_GOLDENS` is `1` (to the source tree, found from `Consultologist.sln` as `V31Contract.RepoRoot` does), and compared otherwise; read the file after generating it and check each type string by eye against the format's file.

- [ ] **Step 2–4:** Run (fail), implement, generate the golden, run (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): type equality and full equality, their hashes, and type strings` and `Tests (#1500): the published formats' hashes, frozen`; push.

### Task 6: Typed reads

**Files:**
- Create: `V31/FormatReads.cs`, `tests/V31Types/FormatReadsTests.cs`
- Modify: `V31/FormatSentences.cs`

**Interfaces:**
- Consumes: `FormatTypes`, `FormatSet`.
- Produces:
  - `public enum ReadFault { None, Bare, UnknownMember, NoContent, TitleOfPlainEnum, TitleOfNonEnum, NoSuchField, NotEveryMember, NotAnObject }`
  - `public sealed record ReadResult(JsonNode? Format, ReadFault Fault, bool Nullable, IReadOnlyList<string>? Titles);` — `Titles` is set for a `.title` read: the labelled enum's titles in order.
  - `public static class FormatReads`:
    - `ReadResult Read(JsonNode? format, IReadOnlyList<string> steps, FormatSet formats)` — `steps` is what follows the node, input, item or parameter: `[]` is bare (`Bare`); the first step is `value`, `content` or `title` (anything else `UnknownMember`: `.state`, `.items` and the rest are part 04's, area 05); after `value`, each step is a field of an object (`NoSuchField`, `NotAnObject`), and a last step `title` on a labelled enum gives `string` and its titles, on a plain `enum` `TitleOfPlainEnum`, on anything else that has no field `title`, `TitleOfNonEnum`. A field named `title` wins over the label read (§ 5.3). Through a union: `.content` always (D10); `.value` when every member has a value (every member but a `null` one); a field only when every member is an object that declares it with the `Same` format, else `NotEveryMember`. A nullable step makes the result `Nullable`.
    - `SourceRefusal BareInput(string id, string path)` (S02-018), `SourceRefusal TitleOfPlainEnum(string readPath, string path)` (S02-019), `SourceRefusal? TitleLiteral(string condition, string readPath, string literal, ReadResult read, string path)` (S02-020 when `literal` is not in `read.Titles`; `<titles>` joined by `, `).

- [ ] **Step 1: Write the failing tests** — on part 02 § 8.4's `sections` input and the published formats:

```csharp
[Fact] public void Value_GivesTheFormat_ContentItsText()         // markdown_block: value → markdown_block, content → markdown_block; judgement: content → plaintext; number: content → NoContent
[Fact] public void AFieldPath_IsTypedAtAnyDepth()                // judgement value.reason → plaintext; concept_list value.concepts → list<{…}>; value.nope → NoSuchField
[Fact] public void Title_ReadsALabelledEnum_AtAnyDepth()         // item format: value.kind.title → string, Titles Narrative, Table, Leave out
[Fact] public void Title_OfAPlainEnum_IsRefused()                // "'item.value.kind.title' reads the title of a plain enum, which has none; use a labelled oneOf, or read .value."
[Fact] public void AFieldCalledTitle_IsTheField()                // value.title on the sections item → string, Titles null
[Fact] public void ABareRead_IsRefused()                         // "'input.visit_type' is read bare; read input.visit_type.value, or input.visit_type.content for text."
[Fact] public void ATitleLiteral_MustBeOneOfTheTitles()          // "Node 'route' if compares input.visit_type.title with 'Followup', which is not one of its titles (New patient, Follow-up)."
[Fact] public void AUnion_ReadsContentAlways_AndAFieldOnlyWhenEveryMemberAgrees()
[Fact] public void ANullableField_MakesTheReadNullable()
```

- [ ] **Step 2–4:** Run (fail), implement, run (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): a read is typed by the format it reads`; push.

### Task 7: Agent bindings and what a model receives

**Files:**
- Create: `V31/AgentBindings.cs`, `V31/SentFormat.cs`, `tests/V31Types/AgentBindingsTests.cs`, `tests/V31Types/SentFormatTests.cs`
- Modify: `V31/FormatSentences.cs`

**Interfaces:**
- Consumes: `TypeRegistry.Bindings`, `.Agents`; `FormatTypes.FullForm`; `CanonicalJson`.
- Produces:
  - `public sealed record AgentBinding(string AgentName, int AgentVersion, string Via, IReadOnlyList<string> StrictRejects);` — `Via` is `format` or `custom`.
  - `public sealed class AgentBindings { static AgentBindings Read(JsonObject bindings, JsonObject agents); string Version { get; } AgentBinding? Find(string formatId, FormatOrigin origin, string producer); }` (D21). `Read` throws `FormatException` naming the member when the files do not have the published shape (`bindings.schema.json`, `agents.schema.json`), when a binding names an agent version `agents.json` does not hold, or when the two `version`s differ.
  - `public static class StrictModeBounds { public const int Properties = 100; public const int Levels = 5; }` (D3)
  - `public sealed record SentFormatResult(JsonObject Schema, IReadOnlyList<string> NotSent, string Hash, int Properties, int Levels, string? DeepestPath);`
  - `public static class SentFormat`:
    - `SentFormatResult Prepare(JsonNode? format, FormatSet formats, IReadOnlyList<string> strictRejects)` — part 02 § 3.5 steps 1 to 5 (D8, D22, D23): descriptions kept; `x-*`, `contentMediaType`, `title`, `$id`, `$schema`, `$defs` gone; references inlined, a text format as `{ "type": "string", "description": … }`; a labelled `oneOf` becomes `enum` of its `const`s in order, `null` included; each `strictRejects` keyword removed and listed. `Hash` is `CanonicalJson.Hash(Schema)`.
    - `IReadOnlyList<SourceRefusal> CheckLimits(string formatName, SentFormatResult sent, string path)` — S02-055, S02-056.
  - Sentences S02-012, S02-027, S02-055, S02-056.

- [ ] **Step 1: Write the failing tests**

```csharp
// AgentBindingsTests (on the published files)
[Fact] public void ABuiltInFormat_IsBoundByFormat()        // Find("plaintext", BuiltIn, "generated") → test-json 48, via format, StrictRejects ["format"]
[Fact] public void APackageFormat_IsBoundThroughCustom()   // Find("verdict", Package, "generated") → custom-json 1, via custom
[Fact] public void AnUnboundFormat_HasNoBinding()          // Find("check_result", BuiltIn, "generated") null; Find("plaintext", BuiltIn, "grounded") null
[Fact] public void AMalformedRegistryFile_Throws()         // no "custom"; an agent version agents.json lacks
// SentFormatTests
[Fact] public void Judgement_IsSentAsPlainJsonSchema()     // exact expected JSON: type object, description kept, reason { "type": "string", "description": "<plaintext's>\n<the field's>" }, no $schema/$id/contentMediaType
[Fact] public void ALabelledEnum_BecomesAPlainEnum()       // oneOf of narrative/table/omit → "enum": ["narrative","table","omit"], no titles
[Fact] public void ADate_LosesFormat_AndSaysSo()           // properties.onset { type: string, format: date } → { type: string }, NotSent == ["/properties/onset/format"]
[Fact] public void ATextRoot_IsSentAsAString()             // "markdown_block" → { "type": "string", "description": "Markdown text that stands on its own lines (a paragraph, a list or a table)." }
[Fact] public void TheHash_IsOfWhatIsSent()                // differs from FormatHash for judgement; same for two formats differing only in title
[Fact] public void Limits_AreCountedOnTheSentCopy()        // 100 properties → none; 101 → "Format 'wide' declares 101 properties; a format sent to a model declares at most 100."; an object five deep → none; six → "Format 'deep' nests 6 levels deep at /properties/a/properties/b/properties/c/properties/d/properties/e; a format sent to a model nests at most 5."
[Fact] public void AReferencedFormat_CountsWhereverItIsReferenced()   // a format referencing a 60-property format twice → 120 + its own
```

- [ ] **Step 2–4:** Run (fail), implement, run (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): agent bindings by format and producer, and the copy of a format a model receives`; push.

### Task 8: Validating values

**Files:**
- Create: `V31/FormatValues.cs`, `tests/V31Types/FormatValuesTests.cs`

**Interfaces:**
- Consumes: `FormatTypes.FullForm`, `CanonicalJson.Hash`.
- Produces:
  - `public sealed record FormatValueFault(string Pointer, string Keyword);` — `Pointer` into the value (`/concepts/0/term`, `""` for the root), `Keyword` the schema keyword that failed (`type`, `required`, `enum`, `additionalProperties`, `format`, …).
  - `public sealed record FormatValueResult(bool Valid, IReadOnlyList<FormatValueFault> Faults);`
  - `public interface IFormatValueValidator { FormatValueResult Validate(JsonNode? value, JsonNode? format, FormatSet formats); }`
  - `public sealed class CorvusFormatValueValidator : IFormatValueValidator` — validates against the **full form** (part 02 § 3.4), with `$schema` rewritten to `https://json-schema.org/draft/2020-12/schema` for Corvus, `format` asserted, no file or HTTP resolution, and the compiled schema cached under the canonical URI `urn:consultologist:validate:<the full form's hash without "sha256:">`. A labelled `oneOf` validates as written. Faults are sorted by pointer, then keyword, and de-duplicated; an invalid value always has at least one. A format that cannot be compiled (an unknown name, a cycle) gives `Valid: false` with one fault `("", "$ref")`.

Corvus's API, as probed on 5.8.12: `Corvus.Text.Json.Validator.JsonSchema.FromText(string text, string canonicalUri, JsonSchema.Options options, bool refreshCache)`; `new JsonSchema.Options(additionalSchemaFiles: null, allowFileSystemAndHttpResolution: false, defaultDialect: Corvus.Text.Json.RuntimeEvaluator.JsonSchemaDialect.Draft202012, alwaysAssertFormat: true, additionalDocumentResolver: null)`; `bool Validate(string json, IJsonSchemaResultsCollector? collector)`; `JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed, …)` with `EnumerateResults()` for the failures' locations. With those options the probe refused `2026-02-30` for `format: date`, a number where `anyOf [plaintext, null]` was expected, and an extra property.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AValueOfTheFormat_IsValid()                 // judgement { answer: true, reason: "…" }; concept_list with one concept; "markdown_block" with a string; list<plaintext>
[Fact] public void AMissingProperty_AWrongType_AnExtraProperty_AreFaults()   // pointers and keywords: ("", "required"), ("/answer", "type"), ("", "additionalProperties")
[Fact] public void ANullableField_TakesNull_ARequiredOneDoesNot()
[Fact] public void AnEnumValue_MustBeListed_PlainOrLabelled()
[Fact] public void NumbersAndDates_AtTheEdges()                // Review Focus 5: 1.0 and 1e2 are integers; 1.5 is not; "2026-02-28" is a date; "2026-02-30", "2026-2-3" and "28/02/2026" are not
[Fact] public void AReply_IsValidatedAgainstTheFullFormat_NotTheSentOne()    // a date field: the sent copy has no format, the value "soon" is still refused
[Fact] public void Faults_NeverCarryTheValue()                 // Review Focus 5: a wrong-typed 2 MB string of "Margaret Okafor …": no fault's Pointer or Keyword holds any word of it
[Fact] public void AnUnknownOrCyclicFormat_IsInvalid_AndDoesNotThrow()
[Fact] public void TheSameFormat_IsCompiledOnce()              // two validators, the same format, 1,000 values: completes, and both agree
```

- [ ] **Step 2–4:** Run (fail), implement, run (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): values are validated against the full format, with Corvus behind our own interface`; push.

### Task 9: The reader's amendments, formats and inputs in a package

**Files:**
- Modify: `V31/SourceParser.cs`, `V31/SourceParser.Nodes.cs`, `V31/SourceLayout.cs`, `V31/SourceSentences.cs`, `V31/FormatSentences.cs`, area 02's tests where a sentence changes
- Create: `V31/SourceTypes.cs`, `tests/V31Types/SourceReaderAmendmentsTests.cs`, `tests/V31Types/SourceTypesFormatsTests.cs`, `tests/V31Types/SourceTypesInputsTests.cs`

**Interfaces:**
- Consumes: Tasks 2–7.
- Produces:
  - In the reader (D13–D16): S02-008 for `kind: "let"`; S02-010 for a reserved producer on any node or flow node; S02-014 for an agent whose `format` is an object (through `SourceMembers.Check`'s `leave`); S02-046 for an input's `default`; S02-039 for a missing file of the package's `formats/`.
  - `public sealed record PackageTypes(FormatSet Formats, IReadOnlyDictionary<string, FormatSet> FunctionFormats, IReadOnlyDictionary<string, JsonNode> NodeFormats, IReadOnlyDictionary<string, AgentBinding> Bindings);` — `FunctionFormats` by function id; `NodeFormats` and `Bindings` by the node's `SourceNode.Path` (a key is not unique across functions), holding each node's declared format and each agent's binding.
  - `public sealed record TypeCheckResult(IReadOnlyList<SourceRefusal> Refusals, PackageTypes? Types);` — `Types` is null exactly when there is a refusal; refusals ordered as the reader's are (by path, then as found), each once.
  - `public static partial class SourceTypes { public static TypeCheckResult Check(SourcePackage package, TypeRegistry registry); }` — this task: the formats, the inputs and every `format` member; Task 10 adds the nodes.
  - Sentences S02-040 to S02-045.

What `Check` does in this task:

1. **Format files:** each package format and each function's own format through `FormatDialect.Check(…, SchemaSite.PackageFormat, "Format '<id>'", …)`; then `FormatSet.CheckCycles()`; then `CheckReferences` on each. A function's `uses.formats` naming no package format: S02-017.
2. **Every `format` member** — an input's, a node's, a flow node's, a function's `format` and its parameters', a renderer's `input` and `format`, a snippet's, a snippet folder's, a macro's, a deliverable's — in the scope that owns it (a function's body and definitions in `ForFunction`): a name through `CheckReferences`; an object through `FormatDialect.Check(…, SchemaSite.Inline, <subject by D11>, …)` and `CheckReferences`. Which formats each of those kinds may take is the owning area's, except nodes (Task 10) and inputs (below).
3. **Inputs** (part 02 § 8, D17–D19), in written order, each judged only when its format passed step 2:

| Rule | Sentence |
|---|---|
| the format is `markdown_inline` | S02-040 |
| the format's root (through its name) is an `anyOf` | S02-041 |
| `channel` is none of the four | S02-042 |
| `transcript`, `image` or `ambient-note` on an input that is not text or a list of text | S02-043 |
| `form` on anything but an object with at least one property | S02-044 |
| a property with no `title`, at any depth of an object input | S02-045, once per property |

- [ ] **Step 1: Write the failing tests** — the amendments each with its exact sentence and the proof that the old sentence is no longer said; the rules above one test each on `SourceFixture.Minimal()` / `Full()` edited with `WithJson`; and

```csharp
[Fact] public void Minimal_And_Full_PassTheTypeRules()            // SourceTypes.Check(parsed, TypeFixture.Published()) → no refusal, Types not null
[Fact] public void Part02s_ExampleInputs_AreAccepted()             // § 8.4's three inputs, written into Minimal's manifest
[Fact] public void AnInlineFormat_IsNamedByItsOwner()              // Input 'sections' uses keyword 'minItems' at the root, which is outside the formats dialect.
[Fact] public void AFunction_CannotNameAPackageFormatItDoesNotUse()   // S02-017 in functions/draft_section/…
[Fact] public void IndependentTypeFaults_AreAllReported_InPathOrder()
```

- [ ] **Step 2–4:** Run (fail), implement, run the whole `~V31` filter (area 02's tests updated where D13–D16 change what they expect); expected: pass.
- [ ] **Step 5: Commit** `PackageFormat (#1500): the reader says part 02's own sentences for let, a reserved producer, an inline agent format, default and a missing format file`, `PackageFormat (#1500): a package's formats and inputs are held to the dialect`; push.

### Task 10: Nodes — kinds, bindings, reproducible and flow formats

**Files:**
- Create: `V31/SourceTypes.Nodes.cs`, `V31/SourceCheck.cs`, `tests/V31Types/SourceTypesNodesTests.cs`, `tests/V31Types/SourceTypesFlowTests.cs`, `tests/V31Types/SourceCheckTests.cs`
- Modify: `V31/SourceTypes.cs`, `V31/FormatSentences.cs`, `V31/SourceSentences.cs` (S01-027)

**Interfaces:**
- Consumes: Task 9's `SourceTypes`, `AgentBindings`, `SentFormat`, `FormatTypes`.
- Produces:
  - `SourceTypes.Check`, complete: `PackageTypes.NodeFormats` and `.Bindings` filled.
  - `public sealed record SourceCheckResult(SourceParseResult Parse, IReadOnlyList<SourceRefusal> Refusals, PackageTypes? Types);` — `Refusals` is the parse's when it has any, else the type rules'.
  - `public static class SourceCheck { public static SourceCheckResult Run(IReadOnlyDictionary<string, byte[]> upload, TypeRegistry registry); }` — `SourceParser.Parse(upload, registry.Names)`, then `SourceTypes.Check` when it parsed.
  - Sentences S01-027, S02-013, S02-015, S02-016.

Rules, for every node of `nodes/`, `checks/` and each function's body (in that function's `FormatSet`), each judged only when its `format` passed Task 9:

| Node | Rule | Sentence |
|---|---|---|
| `agent` | its format and producer `generated` have no binding (D21) | S02-012, `<format>` the name, `<producer>` `generated` |
| `agent` | the format's root is an `anyOf` | S02-027 |
| `agent` | the sent copy (with the binding's `StrictRejects`) is over a limit | S02-055, S02-056, once per format name across the package |
| `rendered` (in `nodes/` or a function) | the format is not `plaintext`, `markdown_block` or `markdown_inline` | S02-013 (D15) |
| `agent`, `rendered`, `computed` | `reproducible` is not the kind's (D5) | S01-027: `Node '<key>' declares reproducible true, but its kind makes it false.` (`Check` for a check) |
| `for`, `while`, `if`, `case` | the declared format is not the derived one (D24) | S02-015, or S02-016 (D25) |

A `call`'s format against its function's, a `computed` node's against its expression, a check's `check_result` and a loop's `in` are areas 05, 04 and 08. To derive a flow node's format, a `call` child contributes its declared format, as every child does.

- [ ] **Step 1: Write the failing tests**

```csharp
// SourceTypesNodesTests
[Fact] public void AnAgent_NeedsABinding()        // format "check_result": Node 'plan' is kind agent and returns check_result, which has no binding for producer generated in agents/bindings.json.
[Fact] public void AnAgent_WithAPackageFormat_IsBoundThroughCustom()   // Types.Bindings["nodes/plan.md"].Via == "custom"
[Fact] public void AnAgentsUnionRoot_IsRefused()  // a package format can't be an anyOf root (A03-001), so: through a registry built for the test whose catalogue holds an anyOf-root format with a binding → S02-027
[Fact] public void ARenderedNode_ReturnsText()    // "number": Node 'billing_text' is kind rendered and returns number; a rendered node returns plaintext, markdown_block or markdown_inline.
[Fact] public void ALeafsReproducible_IsItsKinds()   // an agent declaring true; a rendered declaring false; a check declaring false → "Check 'check.all_sections' declares reproducible false, but its kind makes it true."
[Fact] public void AFormatOverAStrictLimit_IsRefusedOnce_HoweverManyAgentsReturnIt()
// SourceTypesFlowTests (on Full())
[Fact] public void ALoop_IsAListOfWhatItsRetReturns()   // paragraphs declared "plaintext": Loop 'paragraphs' declares format plaintext, but its ret gives list<plaintext>.
[Fact] public void NestedLoops_Nest()                    // sections is list<list<plaintext>>; declared list<plaintext> → S02-015 with both type strings
[Fact] public void AWhile_IsWhatItsRetReturns()
[Fact] public void AnIf_IsItsThen_AndAnElseMakesAUnion() // then plaintext, else verdict, declared plaintext → S02-015 "…but its ret gives plaintext | verdict."; declared anyOf[plaintext, outline] → S02-016 "Branch 2 of if 'billing_gate' returns verdict, which is not in the declared union plaintext | outline."
[Fact] public void ACase_IgnoresItsEmptyBranches()       // route: two plaintext branches and an empty one → plaintext
[Fact] public void ADeclaredUnion_InAnotherOrder_IsTheSameType()
[Fact] public void AFlowNode_WhoseRetWasRefused_IsNotJudgedTwice()
[Fact] public void ACheckLoop_IsAListOfCheckResults()
// SourceCheckTests
[Fact] public void Run_ParsesThenTypes()                 // Full() → no refusal, Types set; a parse fault → only the parse's refusals; a type fault → Parse.Package set, Types null
[Fact] public void AnyOneFormatBroken_IsRefused_AndNeverThrows()   // Review Focus 2: every format file and every inline format of Full() in turn replaced by {}, [], null, 5, "x", true, or its members deleted one at a time: Run returns, with at least one refusal wherever the replacement is not a valid format
```

- [ ] **Step 2–4:** Run (fail), implement, run the `~V31` filter (pass).
- [ ] **Step 5: Commit** `PackageFormat (#1500): agents are bound, rendered nodes return text, and a flow node's format is what its ret gives`, `PackageFormat (#1500): SourceCheck runs the reader, then the type rules`; push.

### Task 11: The suite's refusal packages for part 02

**Files:**
- Modify: `tests/V31Source/RefusalPackages.cs` (or a new `RefusalPackages.Types.cs` partial), `RefusalPackagesExport.cs`, `RefusalPackagesTests.cs`, `V31Contract.cs`
- Content repo (`m39-build`): `v31/conformance/refusal-packages/**`, `v31/conformance/index.json`, `v31/conformance/README.md` (one paragraph: the cases are now held to the reader and the type rules together)
- Modify: the `external/consultologist-package-format` pin

**Interfaces:**
- Consumes: `SourceCheck.Run`, `TypeFixture.Published()` (move it, or the part the suite needs, beside `V31Contract` so both test folders share one).
- Produces: a case (valid and invalid, exactly one sentence, built on `Minimal()` plus the one construct) for every sentence a package can reach through `SourceCheck.Run`:
  S01-027, S02-008, S02-010, S02-012 to S02-017, S02-021 to S02-026, S02-028 to S02-034, S02-037 to S02-056 except those below, A03-001 to A03-008, A03-010.
  **Not shipped, each with its reason in `RefusalPackagesTests`:** S02-018, S02-019, S02-020 (D6: no caller yet); S02-027, S02-035, S02-036, A03-009 (only a registry or catalogue format can reach them); S02-009 (same words as S01-028, which has its case); S02-011 (areas 04 and 05).

- [ ] **Step 1:** Change `RefusalPackagesTests` and the export to judge every case with `SourceCheck.Run`; extend `EverySentence_…_HasACase` to `FormatSentences`' methods with the not-shipped list above. Run; expected: area 02's 65 cases still pass on both sides (a valid side the type rules refuse is a fixture to fix, not a rule to relax), and the new sentences are reported as missing cases.
- [ ] **Step 2:** Write the cases, each `About` naming the chapter section (chapter 2 § 2.x of `package-format-v31.md`) and what the two sides are. For a limit, the valid side sits on the limit (100 properties, 5 levels).
- [ ] **Step 3:** Run `EveryCase_SaysWhatItClaims` and `EverySentence_…`; expected: pass.
- [ ] **Step 4:** Export into the content repo: `CONFORMANCE_EXPORT_V31=~/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport"` on its `m39-build` branch (create from `origin/main` only if missing; otherwise `git pull --ff-only` first). Run that repo's own check (`python3 scripts/check-v31.py`) and read `git status`: only `v31/conformance/**` changes; area 02's `expect.json` files change only where a fixture had to. Read every new `expect.json`. Commit `Format spec (Consultologist-Blazor#1500): refusal packages for chapter 2, the types`, push.
- [ ] **Step 5:** Move the pin to the pushed commit; run `ThePinnedSuite_IsWhatTheParserSays` and `ThePinnedSuite_HoldsEveryCase`; expected: pass. **Commit** `Tests (#1500): a refusal package for every type sentence a package can reach`, `Tests (#1500): the pin moves to the suite with chapter 2's packages`; push.

### Task 12: The library's documentation

**Files:**
- Modify: `docs/customizable-workflow/package-format-library.md`; this plan

- [ ] **Step 1:** Add "specVersion 31: types (preview)" after the source reader's section: what `SourceCheck.Run` takes and returns; the services one by one with a line each (`FormatDialect`, `FormatSet`, `FormatTypes`, `TypeStrings`, `FormatReads`, `AgentBindings`, `SentFormat`, `IFormatValueValidator`); the two hashes and what moves each; what is left to areas 04, 05, 07 and 08 (D5, D6, D15, D22); where the sentences come from; that nothing in the engine calls it yet.
- [ ] **Step 2:** Tick this plan's boxes and write "Where the build departed from this plan".
- [ ] **Step 3:** `dotnet build Consultologist.sln` and `dotnet test Consultologist.sln`; expected: both green.
- [ ] **Step 4: Commit** `Docs (#1500): the type system of 31 in the library's guide, and the plan ticked`; push.

## Spec coverage

| Spec | Task |
|---|---|
| part 01 § 8 `FormatBounds` | 1 (the constants against `bounds.json`, their comments); the three refusals were area 02's (`passes`, `parallel`) and are area 05's (`depth`) |
| part 02 § 1 kinds, `let` | 9 (S02-008); S02-007 was area 02's |
| § 1.1 `reproducible` | 10 for leaves; flow nodes and `call` in area 05 (D5) |
| § 1.2 the three flags | written and typed by area 02; derived and compared in areas 04 and 05 (D5) |
| § 1.3 producer | area 02 (S01-028); 9 (S02-010 reserved); 10 (the rules for a `generated` node: named format, no `anyOf` root, binding) |
| § 2 name or inline; an agent's format is named; inline annotations | 9 (S02-014, S02-017), 3 (S02-038) |
| § 2.1 reading a value | 6 (the typing and the three sentences); the read sites in areas 04, 05, 07 (D6) |
| § 2.2 which formats each kind may return | 10 (`agent`, `rendered` node, `for`, `while`, `if`, `case`); renderer, deliverable, macro: areas 07, 08; `computed`: 04; check: 08; `call`: 05 |
| § 2.3 two equalities, two hashes, `integer` and text fitting | 5 |
| § 2.4 type strings | 5 |
| § 3, § 3.1 the dialect's keywords, `$schema`, recursion, size limits | 2, 3, 4 (cycles across files), 7 and 10 (limits) |
| § 3.2 nullable | 2 (scalars, references), 3 (enums) |
| § 3.3 `required`, `additionalProperties`, the root | 2, 10 (S02-027) |
| § 3.4 validating values, `date` as an assertion | 8 |
| § 3.5 what a model receives, the sent format's hash, the removed keywords | 7; the envelope and the record's row in the sending and record areas (D22) |
| § 4 built-in and catalogue formats; packages define objects and lists | 1 (the registry), 3 (S02-034, A03-001), 5 (golden hashes) |
| § 5, § 5.1 data and text, the annotations | 3, 5 (`TextKind`) |
| § 5.2 Markdig | 1 (the exact pin, against `constants.json`) |
| § 5.3 fully typed objects, field names | 2 |
| § 6.1 format files and the index | area 02 (layout); 9 (S02-039) |
| § 6.2 `$id`s | 3 (S02-033); deriving a package format's `$id` at publish is area 05's bundle |
| § 6.3 references, no shadowing, function scope | 4; S01-034 was area 02's; the rewrite to `#/$defs` is area 05's |
| § 7 agent bindings | 7, 10 (S02-012); `via` in `resolve.json` and the reserved `producer` route are area 05's; the catalogue's own publish check lives in the agents repository |
| § 8 inputs: members, `default`, order | area 02 (members, order kept); 9 (S02-046); blank inputs reading `null` is the engine's (areas 04, 06) |
| § 8.1 formats and widgets | 9 (S02-040, S02-041, S02-045); the widgets are area 12's |
| § 8.2 documents | the engine's intake (area 06); nothing to check at publish |
| § 8.3 `channel` | 9 |
| § 8.4 the example | 9 (`Part02s_ExampleInputs_AreAccepted`), 6 |
| § 9 enum labels | 3 (the rules), 6 (`.title`, the literal), 7 (the plain `enum` a model receives) |
| part 02 *Refusals* | every sentence but S02-001 to S02-006 (History's and a satellite's words, areas 09 and 12), S02-007 (area 02), S02-009 (S01-028's words), S02-011 (areas 04, 05) |
| chapter 12, `refusal-packages` for area 03 | 11 |

## Where the build departed from this plan

What differs from the tasks above is written here as the build goes.
