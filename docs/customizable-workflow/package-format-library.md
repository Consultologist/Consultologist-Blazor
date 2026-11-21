# The package format as a library

*Landed 2026-08-24 (#450), decided out of #409: not the registry — the
conformance suite already makes `package-format@vYYYY.MM.N` executable from
the outside — but the app-repo half #261 called its territory.*

`src/Consultologist.PackageFormat` holds what **defines** the format;
`src/Consultologist.Api` holds what **runs** it and references the library.
The line is the one the design records draw: a rule about what a package
*is* lives in the library; a rule about what the engine *does* with it
stays with the engine.

## In the library (namespace `Consultologist.PackageFormat`)

- The manifest and its sections — `WorkflowPackageManifest`, the input,
  field, node, result, prompt and templating specs, `WorkflowInputTypes`
  (with the version-keyed `ScalarsFor`/`ElementTypesFor`, #492),
  `WorkflowElementSpec` and its converter (v10's `items` as a type name or
  an element spec), `WorkflowNodeKinds` (`prompt`, `classifier`),
  the data-index records, `WorkflowBindingValue` and its converter,
  `WorkflowPackageRef`, `CalVerVersion` — and the strict read
  (`WorkflowPackageManifestJson`, #416).
- `WorkflowResultConditions` (#494): the expression grammar — the tree
  types (`WorkflowConditionExpression`, its clause/not/and/or, the terms),
  the tokenizer with the whitespace rule, the v9 clause parser as the leaf,
  the three-valued evaluator and `Explain`.
- `WorkflowPackageValidator`: every sentence the conformance suite pins,
  `AcceptedSpecVersions`, `CanonicalizeSchema`, and the publish-time probe.
- The wire value (`ConsultInputValue` and its kinds, canonical forms and
  converter — § 4's table), the condition grammar **and** its evaluation
  (`WorkflowResultConditions`), fans (`WorkflowInputFans`), the data
  resolver, node contracts and closure, variable declarations,
  `PromptTemplateRenderer` (§ 4 *Rendering* is normative), the diagram,
  the metadata limits, and the publication stamp (`WorkflowPackageStamp`,
  #433) with `IOutputContractResolver` as what it asks of a catalog.
- The two exceptions format code throws: `WorkflowPackageSpecVersionException`,
  `WorkflowPackageContentException`.
- The concept list as the engine reads and renders it (#1138): `ClinicalConcept`,
  `ConceptOutputContract` (the typed read of a `concept_list` node's output,
  with `ConceptOutputContractException`), `ConceptRenderers` (`Context` and
  `Bullets` — the byte-stable renderings the `concept_context` and
  `concept_bullets` pipes produce), `TemplatePipes.Engine` (those pipes as
  `TemplateRenderer.Render` takes them) and `ConceptSamples` (the one
  concept-list fixture the editor's renderer pane and the tests render). The
  engine and the editor render through the same functions, so the editor's
  sample is the engine's bytes.

## With the engine

`WorkflowPackage` and `WorkflowResolvedResult` (the *resolved* package),
the wire responses and publish request, `WorkflowPackageBlocks` (block
expansion over the resolved package), `WorkflowPackageNaming` (account
prefixes and access — #447's territory), the
listings, the store and its `SupportedSpecVersions`, the publisher, the
registry writer, lineage, the pin resolver, the public chain, and
`OutputContractCatalog` (which implements `IOutputContractResolver`).
`Supported ⊆ Accepted` stays a single-build assertion across the two
(`SpecVersionSetTests`).

## Scriban

The library references Scriban because the validator's probe parses and
renders every prompt. The one deployment fact the validator asserts —
*this engine's Scriban*, against `templating.engineVersion` — is read from
the loaded assembly; the Api and the library share one Scriban in one
process, so the fact is the engine's by construction rather than by a
parameter. Central package management keeps the version single.

## What this does not do, yet

The Blazor client still hand-mirrors the ladder (`SpecVersionMirrorTests`'
facts, `WorkflowManifestReader`, the client's `ConsultInputValue`). A
library with no Azure or engine dependency is one the client *could*
reference, retiring the mirror — a follow-up decided on its own evidence
(WASM payload, trimming, Scriban in the browser). #261's preferred split
remedy — publish the validator as a package — is now one `dotnet pack`
away.

## specVersion 31: the source reader (preview)

*Area 02 of the Milestone 39 build (#1499). Built from
[files and scopes](package-format-v31/01-files-and-scopes.md) and the file shapes of
[loops and functions](package-format-v31/04-loops-and-functions.md).*

`Consultologist.PackageFormat.V31` reads a 31 package **source** — the files of an upload — and
says what it holds, or every way it is wrong. It reads the files and their shape. It does not
judge what the files mean.

`SourceParser.Parse(upload, registry)` takes the upload as a path-to-bytes dictionary and a
`SourceRegistry`: the built-in and catalogue format names and the built-in renderer names, which
the caller owns, so the reader holds no list of its own. It returns a `SourceParseResult`: a
`SourcePackage` or a list of refusals, never both; `Normalised`, the files whose `\r\n`
intake turned into `\n` (the publish report notes them); and `Files`, every file's text as
intake read it (after line-ending normalisation, without the dropped compiled copies), whatever
the outcome, for the steps that hash and write back the files (`dag.mmd` among them). It throws
only for a null argument; nothing an upload holds throws.

**The upload's contract.** Each key is a `/`-separated path from the package root, and only
files are keys. The import layer strips a zip's directory entries and a top-level folder that
wraps the whole package before it calls the reader; anything else it passes through is read as
written, so an unexpected folder prefix makes every file a stray. `resolve.json`, `lock.json`
and `publish-report.json` at the root are dropped unread.

### The steps

1. **Intake** (`SourceIntake`): bytes to text, the encoding and the four size bounds
   (`SourceBounds`). Any intake refusal ends the parse, so a hostile package costs a count, not
   a parse.
2. **Strict JSON and front matter** (`StrictJson`, `FrontMatter`): duplicate members, depth
   (`jsonDepth`) and the split of a file into header and body.
3. **Layout** (`SourceLayout`, `SourceIds`): the manifest, the nine folders, each folder's
   `index.json` against the files and folders beside it, ids, and the files nobody listed.
4. **Declarations** (`SourceShapes`, `SourceMembers`): each declaration's member table, which
   members are required, allowed or forbidden, and their types.
5. **Flow nodes and functions** (`SourceParser`, in its `Nodes`, `Flow` and `Functions` files):
   leaves, `for`, `while`, `if`, `case`, `ret`, and a function's folder.
6. **Scopes and keys** (`SourceScopes`): ids judged per scope (`nodes/`, `checks/`, each
   function) and never across them, and the expression-size bound. This step runs on whatever
   tree the earlier steps built, so a scope fault comes with independent faults elsewhere.

Only intake is a gate. Steps 2 to 5 are not passes over the whole package: they interleave file
by file as the walk reaches each folder, so a fault in one file does not stop the others from
being read and judged.

### The model

| `SourcePackage` holds | |
|---|---|
| `Manifest` | name, version, `derivedFrom`, title, description, tags, inputs; the whole file as `Json` |
| `Nodes`, `Checks` | bodies of `SourceNode`s in listed order: `SourceLeaf`, `SourceFor`, `SourceWhile`, `SourceIf`, `SourceCase`, each with its rooted key |
| `Functions` | `SourceFunction`: index, parameters, body and its own definitions |
| `Definitions` | renderers, snippets, macros, formats |
| `Deliverables`, `Tests` | deliverables (one level of folders) and tests |
| `Readmes` | every `README.md`, path to text |

Kept as raw JSON for later areas: every header (`JsonObject`), a manifest input, a function's
parameter, a format's schema, a test, and an expression (`until`, `while`, `if`) as a
`JsonNode`. Bodies are kept byte for byte after line-ending normalisation. Treat the JSON in the
model as read-only: it is the parsed file, shared by the records that point into it. A
`JsonNode` belongs to one parent, so to place one elsewhere, `DeepClone()` it first.

### Refusals

A `SourceRefusal(Id, Sentence, Path)` carries the id of a row of the format registry's
`v31/sentences.json`, the sentence, and the file it is about (`""` for the package as a whole).
The `A02-NNN` sentences exist for rules the design gives no sentence of its own; the list is in
the conformance README,
`external/consultologist-package-format/v31/conformance/README.md`. One fault gives one
sentence, said once. Refusals are ordered by path (ordinal), then in the order found; when intake
refuses, its refusals keep its own order (each file's, then the bounds in the table's order).
The reader does not cap how many refusals it returns or how long a sentence is (a sentence may
quote a long id or path), so a caller that shows them caps them.

**Part 02's own sentences.** Since area 03, the reader says part 02's sentence where part 02 gives
one for a fault it finds: `kind: "let"` is S02-008 (not S02-007), a reserved producer S02-010 (not
S01-028), an agent's inline format S02-014 (not A02-001), a file the package's
`formats/index.json` lists but `formats/` lacks S02-039 (not S01-009; a function's `formats/` keeps
S01-009), and an input that declares `default` S02-046 (not S01-023). A `rendered` node's inline
format (outside `checks/`) is no longer A02-001: the reader reads on, and the type rules judge it.

S01-017's `<parser message>` is this reader's own: `line <n>, column <m>`, the 1-based line of
the file (a header's first line is the file's line 2) and the 1-based column in characters,
for example *"The header of 'renderers/x.md' is not valid JSON: line 2, column 14."* An empty or
truncated header is placed where it ends.

### What it leaves to later areas

Types and formats, with a leaf's `reproducible` and a flow node's format (03, the next section);
JSON Logic (04); the resolver and the derived values — a flow node's and a `call`'s
`reproducible`, `modelDerived`, `runDependent`, `profileCarrying`, call depth (05, now built: see
"the resolver and the lock" below); prompt members and macros (07, now built: see "templates" below); checks, deliverables and the
profile (08); names and versions (11). The reader types the members that carry these and holds
their values; it does not compare them. Since area 05 the reader says part 04's sentence for an
`in`, a `case` or a `with` value written as an object or a list (S04-017), and A05-005 for a `with`
value of another JSON kind, where it said A02-001. `SourceParseResult.Dropped` names the compiled
copies it dropped.

### The conformance cases

`tests/V31Source/RefusalPackages*.cs` generates the `refusal-packages` family: for each rule, a
valid and an invalid package with the checker's own result as `expect.json` (from area 03 on,
`SourceCheck.Run`: the reader, then the type rules). They are pinned
under `external/consultologist-package-format/v31/conformance/refusal-packages/`. After a
sentence changes, re-export into the content repository's checkout and commit it there:

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport"
```

The export deletes and rewrites the family and the `cases` of `v31/conformance/index.json`;
without the variable the test does nothing.

### Not wired in

Nothing in the engine calls the reader yet. 31 stays outside `AcceptedSpecVersions`.

## specVersion 31: types (preview)

*Area 03 of the Milestone 39 build (#1500). Built from
[types and inputs](package-format-v31/02-types-and-inputs.md), § 8 of
[files and scopes](package-format-v31/01-files-and-scopes.md), and § 1 and § 2.4 of
[compiled documents](package-format-v31/07-compiled-documents.md).*

The type system of 31 sits beside the reader in `Consultologist.PackageFormat.V31`. It holds a
package's formats to the formats dialect, looks names up in the right scope, compares and hashes
formats, prints them, types a read, binds an agent, prepares the copy a model receives, and
validates a value. It also holds a package source to every type rule that needs no expression or
template.

`SourceCheck.Run(upload, registry)` is the whole check. It takes the upload as the reader does and
a `TypeRegistry`. It runs `SourceParser.Parse` against the registry's names and, when the reader
accepts the package, `SourceTypes.Check`. It returns a `SourceCheckResult`: `Parse`, the reader's
result whatever it was; `Refusals`, the reader's when it has any, else the type rules'; and
`Types`, a `PackageTypes`, null exactly when there is a refusal. It throws only for a null
argument.

**The registry.** `TypeRegistry` is the deployment's: the nine built-in formats, the agents
catalogue's formats, the catalogue's `bindings.json` and `agents.json`, and the built-in renderer
names. `TypeRegistry.Read` reads them from the published files. A package never supplies these, so
a missing or malformed file throws `FormatException` instead of being refused (`AgentBindings.Read`
likewise). `Names` gives the reader its `SourceRegistry`.

**What the check gives.** `PackageTypes` holds, for the areas that follow: `Formats`, the
package scope's `FormatSet`; `FunctionFormats`, each function's, by function id; `NodeFormats`,
each node's declared format by its `SourceNode.Path` (a key is not unique across functions); and
`Bindings`, each agent's binding by path. The formats in `NodeFormats` are the reader's own nodes,
not clones: treat them as read-only.

### The order of the check

1. **Format files**, the package's, then each function's after its `uses.formats` (a name no scope
   knows is S02-017). In each scope: each file against the dialect, then the scope's loops, then
   each name a file writes. A file with no refusal, that reaches no file with one, and whose full
   form still cannot be built, is A03-011.
2. **Every `format` member**, in the scope that owns it: an inline schema against the dialect
   under its owner's name (`Input 'sections'`, `Node 'plan'`, `Function 'f' parameter 'p'`), and
   every name it holds looked up. Each input whose format passed, and reaches no refused format
   file, is then held to the input rules (part 02 § 8: S02-040 to S02-045).
3. **The nodes**, of `nodes/`, `checks/` and each function body. A node is judged only when its
   format passed step 2 and reaches no refused format file. An agent needs a binding (S02-012), a
   format without `anyOf` at its root (S02-027), and a sent copy within the strict limits (S02-055,
   S02-056). A `rendered` node returns
   a text format (S02-013). A leaf's `reproducible` follows from its kind: `agent` false,
   `rendered` and `computed` true (S01-027). A flow node's declared format must be the one its
   `ret` gives (S02-015, S02-016). A flow node is not judged when a `ret` child it reads got a
   format refusal; S01-027 and the strict limits do not stop it.

Refusals are said once each and ordered as the reader's are: by path (ordinal), then in the order
found.

### The services

Each takes a format as written (a name or an inline schema) and a `FormatSet`, and returns for any
`JsonNode`: an unknown name, a loop, garbage. None throws on package content.

- **`FormatDialect.Check(schema, site, subject, id, path)`**: one schema against the dialect
  (part 02 §§ 3, 5.3, 9), every refusal in document order. `SchemaSite` says where it is written:
  a package format, a registry format or an inline schema. The site decides the root's rules, and
  whether field names and enum values are held to snake_case (package and inline only).
- **`FormatSet`**: the formats a scope can name, and the lookup. `ForPackage` finds the package's
  own formats, then the catalogue's, then the built-ins. `ForFunction` finds the function's own
  formats, then the package formats it `uses`, then the package formats those reach through their
  names, then the catalogue's and the built-ins. `Find` and `Resolve` look a name up; `MayName`
  says whether the scope may write it; `CheckReferences` (S02-017) and `CheckCycles` (S02-032) are
  its two rules.
- **`FormatTypes`**: the type form and the full form, their hashes, `Same` (type equality), `Fits`
  (one-way: `integer` where `number` is expected, a text format where `string` is, `T` where
  `T | null` is), `ListOf`, `UnionOf`, `Shape`, `IsNullable`, `TextKind`, and `Expands`.
- **`TypeStrings.Print`**: how people see a format (part 02 § 2.4): `{ title: plaintext, n: number }`,
  `list<verdict>`, `plaintext | null`, `string {good, revise}`. A text format's own schema prints its
  name (`plaintext`).
- **`FormatReads.Read(format, steps, formats)`**: the format a read gives (`.value`, `.content`,
  `.title`, then fields), or a `ReadFault` for the caller to word. `BareInput`, `TitleOfPlainEnum`
  and `TitleLiteral` give S02-018, S02-019 and S02-020.
- **`AgentBindings`**: `bindings.json` and `agents.json` read together. `Find(formatId, origin,
  producer)` gives the `AgentBinding` (agent name, version, `via`, the keywords its strict mode
  rejects). A package's or function's own format is bound through `custom` (`via` `custom`); a
  built-in or catalogue format through `formats.<id>` (`via` `format`), never through `custom`.
- **`SentFormat.Prepare(format, formats, strictRejects)`**: the copy of a format a model receives
  (part 02 § 3.5 steps 1 to 5). It is the full form with `title`, `$id`, `$schema`, `$defs`, `x-*`
  and `contentMediaType` removed, descriptions kept, a labelled `oneOf` as a plain `enum`, and the
  agent's rejected keywords removed. `NotSent` lists those keywords as pointers into the full form.
  `Hash` hashes the copy. `CheckLimits` gives S02-055 and S02-056.
- **`IFormatValueValidator.Validate(value, format, formats)`**: is a value one the format describes
  (part 02 § 3.4). `CorvusFormatValueValidator` is the implementation, on
  `Corvus.Text.Json.Validator` 5.8.12: draft 2020-12, `format` asserted (so `date` is checked),
  no file or HTTP resolution. It validates against the full form, compiled once per process. A
  format that does not expand gives the one fault `("", "$ref")`.
- **`CanonicalJson`**: RFC 8785 text, and `sha256:` hashes over it (part 07 § 1). A number no
  finite double holds, and anything nested past 256 levels, is written `null`.

### Equality and the two hashes

**Type equality** (`Same`) compares type forms. The type form drops `title`, `description`,
`$schema`, `$id` and `$defs`; inlines `#/$defs` references; writes a nullable type array as an
`anyOf` with `null`; flattens a union of unions; sorts and de-duplicates `anyOf` members; and sorts
`required`. `enum` and `oneOf` keep their order.

**Names compare by name.** A name stays `{ "$ref": name }` at every depth, the root included, so
`"verdict"` and `{ "$ref": "verdict" }` are one type. Two package formats with identical schemas
are two types. A built-in's name equals its schema: `{ "type": "string" }` is `string`, and
`{ "type": "string", "format": "date" }` is `date`. `integer` has no built-in, so it stays
`{ "type": "integer" }`.

**`typeHash`** (`FormatTypes.TypeHash`) is the hash of the type form. For a name, it is the hash
of the named schema's type form. So two formats with identical schemas share a `typeHash` without
being `Same`: compare types with `Same`, never through this hash. It moves when the type moves. It
does not move for a title or a description, the order of `anyOf` members or of `required`, or
`["null", "string"]` written for `["string", "null"]`.

**`formatHash`** (`FormatTypes.FormatHash`) is the hash of the full form without `$id`. The full
form inlines every reference: a `$ref` site becomes its target, the site's `title` in place of the
target's, the descriptions joined target first. It keeps every other keyword as written, except
that a nullable type array is written type first. So `formatHash` moves for a description, a title
or `required` reordered, and not for `$id` or the order of a nullable type array.

Both hashes, and both forms, mean something only for a format that `Expands`: one whose full form
can be built within `FormatTypes.SchemaBudget` (10,000 schemas) and the depth bound, with no unknown
name and no loop. The check refuses a package or function format that is otherwise sound and does
not expand (A03-011).

**A nullable date** is written `anyOf [ { "$ref": "date" }, { "type": "null" } ]`.
`"type": ["string", "null"]` beside `format` is S02-030: `format` sits on a plain string.

### Strict mode's limits

`StrictModeBounds`: 100 properties and 5 levels (Azure OpenAI's structured outputs). They are
counted on the sent copy, not on the format as written. **Properties** is every key of every
`properties` object, a referenced format counted at each place it is referenced. **Levels** counts
object schemas only: the root object is level 1, a text or list root is level 0, and arrays and
`anyOf` add none. S02-056's `<path>` is the first object, in document order, at level 6. Each
format name is held to the limits once across the package, on the first agent in walk order that
has a binding. The engine's envelope is not counted.

### A function's scope

What a function itself writes (its `format`, its parameters, its body's formats, its own
definitions and format files) may name only its own formats, the package formats its
`uses.formats` lists, the catalogue's and the built-ins (`MayName`); any other name is S02-017. A
name written inside a package format belongs to the package's scope, so the function's
`FormatSet` also finds the package formats that its used ones reach, for lookup, forms, hashes,
printing and reads. A `uses.formats` name that no scope knows is S02-017.

### Sentences and faults

A sentence with a row in `v31/sentences.json` is that row, character for character, under its id.
Where part 02 and another part state one rule, part 02's row is said here: S02-015, not S04-014;
S02-019, not S03-016. A rule with no row gets an `A03-NNN` sentence (A03-001 to A03-011, listed in
the conformance README). `FormatSentences` has one method per sentence.

No sentence and no validation fault carries a value from an input or a model. A sentence carries
ids, member names, keywords, JSON pointers, counts, limits and package text. A
`FormatValueFault` is a pointer into the value and the keyword that failed. For `required` and
`additionalProperties` the pointer is the object, so an extra member's name never appears.

### What it leaves to later areas

- **A flow node's and a `call`'s `reproducible`, and the three flags** (part 02 § 1.1, § 1.2; D5):
  they follow from what a node reads. Area 05 derives them and holds them against every header
  (`SourceDerived`, in "the resolver and the lock" below), with S04-015, S04-016 and S03-036.
- **Finding the reads** (D6): JSON Logic `var` (area 04) and reference strings (area 05) are now
  found and typed, and since area 07 so are templates' (`SourceTemplates`, in "templates" below). A
  bare `input.<id>` as a loop's source, a `case` subject or a call's argument is S02-018, which now has
  a case. S02-019 is said of a template's read of a plain enum's title, and has a case; no package
  reaches S02-020 yet.
- **A check's format** (D15): area 08's (S06-009). A rendered check's inline format is still
  A02-001.
- **The envelope** (D22): `SentFormat` stops before it. Area 07 built it (`EnvelopedFormat`, 07-D8):
  `sentFormatHash` is the hash of the canonical JSON sent, envelope included.
- **`uses.formats` listing a built-in or catalogue name** (D36): part 04 § 7.3's rule. Area 05
  allows it, and warns with S04-074, as for any other name, when the function never names it
  (05-D12).
- **The other kinds' formats** (part 02 § 2.2): a renderer's and a macro's (07, now built: S05-028,
  S05-046, S05-049, S05-050), a deliverable's (08), a `computed` node's (04), a `call`'s (05: S04-070,
  the call's against its function's).

The suite's cases for these sentences are in the `refusal-packages` family, beside area 02's (see
the reader's section above).

### Not wired in

Nothing in the engine calls any of it yet. 31 stays outside `AcceptedSpecVersions`.

## specVersion 31: JSON Logic (preview)

*Area 04 of the Milestone 39 build (#1501). Built from
[expressions](package-format-v31/03-expressions.md), with part 02 for the formats a rule is typed
against and [the plan](package-format-v31/plans/area-04-json-logic.md) for the decisions (D1 to D29,
and those taken during the build).*

JSON Logic at 31 sits beside the type system in `Consultologist.PackageFormat.V31`. It holds the
operations of part 03 § 5, judges the written form of a rule, types a rule against the formats,
prints it for people, compiles and evaluates it, and checks every place a package writes one. It
also holds the algorithm of the three flags and the conversion `plain` uses.

`SourceCheck.Run(upload, registry)` now has a third step. After the reader and the type rules, when
they gave no refusal, `SourceExpressions.Check` checks every expression site of the package.
`SourceCheckResult` has a fourth member, `Expressions`, a `PackageExpressions`, null exactly when
there is a refusal (`Types` stays set when only an expression is refused). `Refusals` is the
reader's when it has any, else the type rules', else the expressions'.

### The types

- **`JsonLogicOperations`**: the 39 operations of § 5 (`All`: 21 standard, then 18 custom, each a
  `JsonLogicOperation(Name, Min, Max, Custom)`), `Find`, `AllowedList`, `Takes` (the arity words of
  S03-010), `RunFacts` (`date`, `id`, `package`, `engine`) and the three versions (`1.0.0`).
- **`IRegexp`**: a pattern held to RFC 9485's grammar, to 200 code points and to repetition counts of
  at most 100 (`Check` gives an `IRegexpCheck`: the `IRegexpFault`, a `Reason` that is a phrase from a fixed
  list with the position and the character filled in (plan D30), a `Count`, and the .NET `Regex`
  when it is sound), and translated to .NET (`ToDotNet`), compiled `NonBacktracking` with no
  time-out. `Check` caches at most 1,024 patterns, and only those past the grammar (plan D46). `IsMatch` searches; `TooLong` says a text is over 1 MiB.
- **`JsonLogicShape`**: the written form with no types. `Check` gives every `ShapeFault`
  (`ShapeFaultKind`: an object that is not a rule, a loose `==`, an unknown operation, a wrong
  operand count, a bad `var` default, a pattern that is not I-Regexp, and so on). `Paths` lists the
  paths a rule reads (`var` and `missing`), `IsPath` and `IsPosition` judge one. `MaxDepth` is 64.
- **`JsonLogicPrinter`**: a rule as people see it. `Print` is total. `TryParse` reads back only text
  `Print` itself writes: it parses, prints again and compares, and gives `false` for anything else.
  `Reason` words a decision's reason for the record. `Version` is 1.0.0.
- **`PlainConversion.ToPlain(markdown)`**: the conversion `plain` applies (Markdig's plain text with
  the advanced extensions, `\n` line breaks, no spaces, tabs or line breaks at the very end). It
  returns `null`, never the source, when Markdig cannot convert the text (Markdown nested too
  deeply, such as 200 nested `>`); it never throws. `Version` is 1.0.0.
- **`JsonLogicFunctions`**: the meaning of each operation on values (`Apply`), truthiness (`Truthy`),
  reading a path (`Read`), `Missing`. `Truthy` is JSON Logic's operand truthiness (`0` and `""`
  are false); a condition's result is tested by `IJsonLogicRule.Decide`, which is stricter. A
  `JsonLogicFailure(Kind, Detail)` is a run-time failure (`JsonLogicFailureKind`: `Declined`,
  `MatchesTextTooLong`, `NotABoolean`, `Fault`, `PlainNotConverted`). A `plain` told to convert
  Markdown that `ToPlain` cannot convert fails the rule with `PlainNotConverted`, worded as A04-011:
  `<site> <member> could not be evaluated: plain was given Markdown nested too deeply to convert.`
- **`IJsonLogicEvaluator`**, **`CorvusJsonLogicEvaluator`**, **`IJsonLogicRule`**: compile and run
  (below). `JsonLogicValue` and `JsonLogicDecision` are what a rule gives.
- **`ExpressionSite`**, **`IExpressionScope`**, **`ScopeHead`**, **`ExpressionType`**: one place a
  rule is written, who can be read there, and a rule's type (below).
- **`ExpressionChecker`**: types one rule at one site. `ExpressionCheck` is its result.
- **`SourceExpressions`**, **`PackageScope`**, **`IExpressionScopes`**, **`SiteContext`**,
  **`SiteExpression`**, **`PackageExpressions`**, **`ExpressionCheckResult`**: every site of a
  package.
- **`LineageFlags`**, **`LineageFlag`**, **`LineageNode`**, **`LineageRead`**, **`LineageValue`**:
  the three flags, worked out over a graph the caller supplies.

### Checking, printing, compiling and evaluating a rule

**Check.** `ExpressionChecker.Check(rule, site, scope, formats)` returns an `ExpressionCheck`
(`Refusals`, `Type`, `Reads`, `Printed`, `PlainConverts`). It never throws on any rule. It works in
four steps: the written form (each `ShapeFault` as its sentence, stopping there when there is any);
every read, through the root and site rules of part 03 and then the scope and `FormatReads`; every
operation's operands and result; and the result against the site (a condition yields a boolean,
S03-025; a `computed` node its declared format, S03-026). `Type` is null exactly when there is a
refusal. `ExpressionType` is a format (null: not known), whether it is declared nullable, and, for a
`.title` read, the titles a literal is held to. The checker tracks declared nullability only, and a
nullable operand is not refused.

**Print.** `JsonLogicPrinter.Print(rule)` gives the text the record and the refusals show (`Printed`
is the same). It is versioned: a change to its output is a major version.

**Compile and evaluate.** `new CorvusJsonLogicEvaluator().Compile(rule, plainConverts)` gives an
`IJsonLogicRule`. A rule with any fault of its written form is declined before Corvus sees it:
`Declined` says why, and `Evaluate` and `Decide` give that failure. Otherwise `Evaluate(data)` gives a
`JsonLogicValue` and `Decide(data)` a `JsonLogicDecision` (a condition: `true`, `false`, `null` and a
list decide; a number, text or an object is a `NotABoolean` failure). `data` is one object whose
members are the roots (`input`, `run`, `node`, ...). Neither throws on any data: a failure is in the
result. A compiled rule is cached by its text and its `plainConverts`.

**Corvus is serialised.** Corvus 5.8.12 is not thread-safe (it keeps the last rule it ran in two
fields). Every call into it is under one lock per evaluator, so one evaluator is safe to share, and
two evaluators do not exclude each other. Rules, data and results are read to 512 JSON levels deep;
data deeper than that is a `Fault`.

**One evaluator per compiled package version.** Part 03 § 9 compiles a package's rules once per
package version: hold one evaluator for each compiled package version, never one for the whole
process. Every call into Corvus is serialised per evaluator, so a slow `matches` would stall every
other rule run on a shared one, and its compiled-rule cache is never emptied: it lives as long as
the evaluator does (plan D47).

**`PlainConverts`.** `plain` converts Markdown to plain text and returns a plain-text operand
unchanged. At run time a value is a string with no format, so the evaluator cannot tell which it has.
`ExpressionCheck.PlainConverts` is that choice made at publish: for each `plain` in the rule,
`true` for a Markdown operand or one whose type is not known, `false` for plain text or a string.
Pass it to `Compile`. The `plain`s are numbered as the evaluator numbers them: a `plain` before
those inside its operand, operands left to right, an `object`'s fields as written. Without a list, or
past its end, a `plain` converts.

### A scope

A scope says who can be read at a site and their types. `IExpressionScope.Head(segments)` gives a
`ScopeHead` for the start of a path: a `Format` (read through `.value`, `.content` or `.title`, the
rest going to `FormatReads.Read`), a `Value` (already a value, a run fact), `Deferred` (no type yet:
a read through it has no type and fits any operand), `Unknown` (nothing there: S03-014), or `Refused`
(the scope refuses the read with its own sentence, `ScopeHead.Refusal`). The root
and site rules of part 03 (S03-013, S03-015, S03-018 to S03-022) are judged before a scope is asked.

`PackageScope` is the scope of this area: `input.<id>` and `run.<fact>` are typed; every other root
(`node`, `check`, `item`, `pass`, `param`), and every root inside a function, is `Deferred`. Area 05
supplies the real scope through `IExpressionScopes` (`SourceExpressions.Check(package, types,
scopes)`): `SourceCheck.Run` passes `ResolvedScopes`, so every read is now judged where it is
written. `PackageScope` stays the scope when no scopes are given, and for a macro's or a snippet's
`if`, which is read where it is placed: since area 07 the derived step reads it in each placing body's frame, and
`resolve.json` writes it rooted for each placing entry in that entry's `placements`. A read of `run.<fact>` at a site gives S03-037
(a condition) or S03-038 (a computed node) as a warning, in `PackageExpressions.Warnings`; a
function's site that reads `run.*` warns the same way. `resolve.json` takes the derived step's
warnings in their place: they name every run-dependent decision and value, not only the direct
readers.

### Where a rule is written

`SourceExpressions.Check` visits, in the order `SourceScopes` does, the `if` of a production leaf, of
an `if` flow node, of a check, of a deliverable, of a part, of a macro and of a snippet; the `while`
and `until` of a flow node; and every `computed` node's `expression` (always checked). A condition
site whose member is null or absent is skipped; at an `expression` a `null` rule is the `null`
literal, held to the node's declared format. Each site is checked against its own function's
`FormatSet` when it is in one. A `SiteExpression` holds a `SiteContext` (the `ExpressionSite`, the
declaration that holds it, its function if any, and the part of a `parts.<id>.if`), its rule (null
for the `null` literal) and its `ExpressionCheck`. Refusals are said once and ordered by path.

### The flags

`LineageFlags.Derive(nodes)` works out `modelDerived`, `runDependent` and `profileCarrying` for a
graph of `LineageNode`s the caller supplies, in rounds against a snapshot of the one before; a cycle
ends. A flag's cause (`LineageValue.Because`) is the read that made it true first. `OfReads` gives a
decision's flags, and `Disagreement` gives S03-036 when a node's declared flag is not what its
reads make it. Area 05's `SourceDerived` gives it the package's graph, so S03-036 and S03-044 are
now said, and each has a case. What templates read is area 07's (D28), now built: `SourceCheck.Run` gives the
derived step the dialect's reads, so a flag that depends on a body is judged. Only a caller that passes a seam
knowing no body leaves such a flag unjudged.

### Sentences

The sentences are `ExpressionSentences`' (internal), one method each. A sentence with a row in
`v31/sentences.json` is that row, character for character; the rules with no row are `A04-001` to
`A04-011`, listed in the conformance README. Where part 03 and part 02 say one rule, the sentence is
the part 03 row at a rule's site (D3). A run-time failure (S03-040, S03-041, A04-011) is never said
at publish.

### What it leaves to later areas

- **The scope of `node`, `check`, `item`, `pass` and `param`, and the rules of what a site may see**
  (D2): built by area 05 (`ResolvedScopes`). Only a macro's and a snippet's `if` stay `Deferred` here;
  the derived step reads them where they are placed (area 07).
- **The edges the flags spread along, and the header check** (D28): built by area 05
  (`SourceDerived`). The reads of templates are area 07's, now built (`PackageTemplates`).
- **Recording a run's facts and the record's rows**: area 09's.

### The conformance cases

Two families come out of this area, each exported by a test that does nothing without
`CONFORMANCE_EXPORT_V31=<content repo root>`:

- `tests/V31Source/RefusalPackagesExport.cs` writes the `refusal-packages` family, now with the
  cases of chapter 3 (`tests/V31Source/RefusalPackages.Logic.cs`), pinned under
  `external/consultologist-package-format/v31/conformance/refusal-packages/`.
- `tests/V31Logic/LogicVectorsExport.cs` writes six families from `tests/V31Logic/LogicVectors.cs`:
  `operation-goldens` (one file per custom operation), `printer-goldens`, `null-vectors`,
  `text-vectors`, `number-vectors` and `date-vectors`. A file is `{ "about", "cases" }`; a case
  holds `rule`, `data` and `result`, an `output` (an operation golden: the result's canonical JSON,
  as a string) or a `text` (a printer golden).

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~LogicVectorsExport|FullyQualifiedName~RefusalPackagesExport"
```

Each export deletes and rewrites its folders and the `cases` of `v31/conformance/index.json`.

### Not wired in

Nothing in the engine calls any of it yet. 31 stays outside `AcceptedSpecVersions`.

## specVersion 31: the resolver and the lock (preview)

*Area 05 of the Milestone 39 build (#1502). Built from
[compiled documents](package-format-v31/07-compiled-documents.md) §§ 1 to 4 and 7,
[loops and functions](package-format-v31/04-loops-and-functions.md) §§ 2 to 7 and 10, § 9 of
[files and scopes](package-format-v31/01-files-and-scopes.md), with
[expressions](package-format-v31/03-expressions.md) §§ 4.1 to 4.6 and
[types and inputs](package-format-v31/02-types-and-inputs.md) § 1 for what is derived, and
[the plan](package-format-v31/plans/area-05-resolver-and-lock.md) for the decisions (D1 to D15, and
those taken during the build).*

The resolver of 31 sits beside the JSON Logic code in `Consultologist.PackageFormat.V31`. It resolves
every reference of a package source to its rooted key and types it, holds the source to part 04's
publish-time rules, derives `reproducible` and the three flags and holds them against every header,
and writes the two compiled documents, `resolve.json` and `lock.json`. It also holds the checks an
engine makes of the stored pair at load and before a job.

`SourceCheck.Run(upload, registry, templates = null)` now has five steps, each run only when the
ones before it refused nothing: the reader, the type rules, the resolve step
(`SourceResolve.Check`), the expressions (`SourceExpressions.Check`, each site read in the scope the
resolution gives), and the derived step (`SourceDerived.Check`). `Refusals` is the first refusing
step's. `SourceCheckResult` gains two members:

- `Resolution` is null when the reader, the type rules or the resolve step refused. It stays set
  when only an expression or the derived step refuses.
- `Derived`, a `PackageDerived`, is null exactly when there is a refusal.

`Expressions` stays set when only the derived step refuses. `templates` is the seam of D1 (below).
Without it, the check is right but not complete.

After the check, `ResolveCompiler.Compile` writes `resolve.json` and `LockCompiler.Compile` writes
`lock.json`. `CompiledPackage.Publish` runs the check and both compilers in one call.
`CompiledPackage.Verify` and `RequiresCheck` are what an engine asks of the stored pair.

### The types

- **`ReferenceString`**: a reference string (`in`, `case`, `with`) parsed into `Text` and
  `Segments`. `Parse` gives null for anything that is not a path by `JsonLogicShape.IsPath`, for a
  position segment, and for white space or a control character in a name (D13).
- **`ScopeTree`**, **`ScopeFrame`**, **`Resolved`**, **`LoopSource`**: who encloses whom, and what a
  reference names at a place (below). The static `ScopeTree.AlwaysValued(node)` and
  `ScopeTree.EmptyValues(case)` give part 04 § 7.6's "always valued" and a `case`'s empty branches.
- **`ResolvedScope`**, **`ResolvedScopes`**: the real `IExpressionScope` and `IExpressionScopes` of
  area 04, over the tree.
- **`SourceResolve`**, **`Resolution`**, **`CallBinding`**: the resolve step. A `Resolution` holds
  the `Tree`, the `Scopes`, every node's key (`SourceKeys`; inside a function the key is local to
  it), every `call` bound to its function (`Calls`), and the step's `Warnings`.
- **`ITemplateReads`**, **`TemplateReads`**, **`TemplateRender`**, **`TemplateRunSpot`**: the seam
  through which area 07 says what a Scriban body reads (its `PackageTemplates`; `TemplateReads` gained
  `Substituted` and `TemplateRunSpot` gained `Unit` and `Neutral` there).
- **`SourceDerived`**, **`DerivedEntry`**, **`PackageDerived`**: the derived step and what it gives.
- **`CompiledFormats`**, **`SourceHash`**: the formats bundle, its hashes and the bindings (part 07
  § 2.4), and `sourceHash` (§ 2.6).
- **`ResolveCompiler`**, **`ResolveResult`**, **`PublishNotes`**, **`PublishLimits`**: `resolve.json`
  and part 04 § 10.2's notes.
- **`Toolchain`**, **`CaretRange`**, **`LockCompiler`**, **`CompiledPackage`**: `lock.json`, the
  pair and its hashes, and `Verify`.
- **`RequiresCheck`**, **`EngineCapabilities`**, **`BreakPoint`**: `requires`, the lock and
  `needs.profile` held against an engine and an account.
- **`SourceParseResult.Dropped`** (and `SourceIntakeResult.Dropped`): the compiled copies and the
  publish report that intake dropped from an upload, ordinal-sorted, whatever the outcome.

### The scope

`ScopeTree.Build(package, types)` makes one `ScopeFrame` per body: the top of `nodes/`
(`PackageRoot`), the top of `checks/` (`ChecksRoot`), each function's root (`FunctionRoot`), each
`for` and `while` body, and each branch of an `if` or a `case`. `FrameOf(node)` is the body a node
is listed in. A frame has `Parent` (null at a root, a function's root included), `Function`,
`Owner` (the flow node whose body it is) and `Nodes`.

- **`Visible(root, id)`** looks for the node in this body, then in each enclosing body, and stops at
  the function's root. In `checks/`, `node.<id>` sees the top level of `nodes/`. In `nodes/`,
  `check.<id>` sees the top of `checks/`. No check is visible inside a function.
- **`Loop(up)`** and **`While(up)`** give the loop that `item`, `item.parent`, … names (skipping
  whiles) and the while that `pass`, `pass.parent`, … names (skipping loops).
- **`Hidden(root, id)`** gives a node of that id that exists inside a loop or while the reader is
  outside of. Reading one is S04-039.

**`Resolve(reference, frame, subject, path, condition, reader = null)`** is the one resolution every
caller uses: a `var` or `missing` path, an `in`, a `case`, a `with`. It gives a `Resolved`:

- `Kind`: area 04's `ScopeHeadKind` (`Format`, `Value`, `Deferred`, `Unknown`, `Refused`).
- `Key`: the rooted key for `node`, `check`, `input` (`input.<id>`) and `param` (`param.<id>`);
  null for the other roots.
- `Length`: how many segments the head took. For a `Format` head the rest go to `FormatReads.Read`;
  for a `Value` head they are fields.
- `Format`, `Nullable`, and `Refusal` (set exactly for `Refused`).

`reader` is the node that reads, whose id S04-050 and S04-051 name. `condition` does not change the
resolution: callers use it to tell a rule's read from a reference string.

**Members and their types** (D14):

- `.state` is `string {completed, skipped, not_run}`.
- A `for`'s `.stopped` is `string {until, bound, end}`; a `while`'s is `string {check, until, bound}`.
- `.passes` and `pass.number` are `integer`; `pass.previous.number` is a nullable `integer`.
- `.history` and `pass.history` are lists, read only through `size`.
- `item.id` is `string`. `item.value` is the element of the loop's source (`ScopeTree.Source`, below).
  `item.content` exists only over a snippet folder.
- A loop over another loop's `.items` gets the element of part 04 § 2.6 as its `item`: `item.state`,
  `item.value`, `item.label`, `item.nodes.<id>.value`, ….
- `pass.node.<id>` and `pass.previous.node.<id>` read as `node.<id>` does, for any node in the while.
  `pass.previous…` is nullable.
- A `.value` or `.content` is nullable when its node is not `AlwaysValued`: a leaf with an `if`, an
  `if` with no `else`, a `case` with an empty branch, a `checkFirst` while, or such a node down a
  `ret`.

**Refusals of a read.**

- `pass.number` with no while is S04-036. `pass.parent` past the outermost while is S04-037.
  `pass.previous.node.<id>` naming a node outside the while is S04-038. `item.parent` past the
  outermost loop is S04-040.
- Inside a function the scope is sealed. `input.*` is S04-050. `run.*`, `check.*`, `profile.*` (as an
  argument), a node outside the function, and an outer `item` or `pass` are S04-051.
- Any other `item` or `pass` with no loop or while, an unknown head, or a member the thing does not
  have is `Unknown`, which the checker words as S03-014. There is no A05-002.

**`ScopeTree.Source(loop)`** gives a `LoopSource`, cached per loop:

- `Resolved`: the loop's `in`, resolved at the frame outside the loop.
- `Element`: the list's element. Over `snippet.<folder>` it is the folder's `format`, looked up in
  the function's own folders first, then the package's.
- `KnownLength`: a snippet folder's item count.
- `OverItems` and `Production`: set when the loop iterates a loop's `.items`.

A loop whose source reads itself is not typed (its `item.value` is `Deferred`), and A05-003 refuses
it later.

**`ResolvedScopes.For(site)`** reads each expression site at its place:

- A node's rule is read at the node's frame.
- A `while`'s `while` and `until`, and a `for`'s `until`, are read in their own body's frame.
- A deliverable's `if` is read at the top of `nodes/`.
- A macro's and a snippet's `if` are read where they are placed, which only a body says. Their
  sites stay in `PackageScope`, deferred; with the seam (always, since area 07), the derived step
  judges them where each body places them (below).

### The resolve step

`SourceResolve.Check(package, types)` returns the tuple
`(IReadOnlyList<SourceRefusal> Refusals, Resolution? Resolution)`. It runs these rules, then sorts
its refusals by path. `Resolution` is null exactly when there is a refusal.

- **Loops** (`SourceResolve.Loops.cs`). For a loop's `in`:
  - A05-001 when it is not a reference; S03-014 when it names nothing (`Loop 'x' reads …`);
    S02-018 for a bare `input.<id>`; `Resolve`'s refusals.
  - S04-025 when it is not a list, a single snippet included. S04-026 for `item.value.…` with no
    loop. S04-027 for `node.<y>.value.…` where `y` runs inside another loop.
  - `itemLabel` (S04-028 to S04-030), judged only when the source is typed. Over `.items`, `label`
    is accepted.
  - S04-035 for a while body node that nothing reads. Without the seam it is judged only for a while
    with no agent or rendered node in its body.
- **Branches** (`SourceResolve.Branches.cs`). A `case` subject is read as an `in` is.
  - S04-044 when the subject is not an enumerated string (`enum`, or a `oneOf` of `const`s; nullable
    allowed).
  - Then S04-045 for a value that is not the subject's, and S04-046 for a value in two branches. A
    value listed twice in one branch names that branch twice.
  - Then, with no `else`, S04-047 for values no branch lists and S04-075 for a subject that can be
    null.
- **Calls** (`SourceResolve.Calls.cs`, typing in `ScopeTree.Calls.cs`), in source order:
  - S04-064 when the function is unknown, and nothing else is said of that call.
  - S04-065 and S04-066 for a parameter left out or one too many.
  - Each argument held to part 04 § 7.7's heads: `node.<id>`, `check.<id>`, `item` (whole, as their
    `.value`), `snippet.<folder>.<item>`, `run.<fact>`, `profile.<field>` (typed by `v31/profile.json`),
    `param.<id>`, with fields. Its own refusal comes first; then S04-068 when it does not fit by
    `FormatTypes.Fits` (in the caller's scope; a literal `null` needs a nullable parameter).
  - S04-070 for the call's `format` against the function's, by `Same`.
  - The reader now says S04-017 for an `in`, `case` or `with` written as an object or list, and
    A05-005 for a `with` value of another JSON kind.
- **Functions** (`SourceResolve.Functions.cs`):
  - S04-053 for an own definition that `uses` also lists. S04-052 for a package snippet a header
    reads that `uses.snippets` does not list.
  - The `ret` rules, each only when none before it was said: S04-057, S04-059 or S04-058, then
    S04-060, then S04-061.
  - S04-074 (a warning, in `Resolution.Warnings`) for a `uses.formats` entry no format member of the
    function names, directly or through another format. A built-in or catalogue name may be listed
    (D12).
  - The call graph. One S04-055 or S04-054 per strongly connected set, named as the shortest ring
    through its ordinal-first function. With no ring, `CallBinding.Depth` is 0 outside every function
    and *d*+1 inside a function reached at *d*; S04-056 goes on each outside call deeper than
    `FormatBounds.Depth`.
  - The profile. A parameter bound to `profile.<field>`, or to a parameter that is, carries it. A
    condition inside its function reading it is S04-069 (`a condition`); a part's `if`, S04-069
    (`a part`).
- **The bundle.** A function's own format whose key `<function>__<id>` another used format has is
  A05-004 (`CompiledFormats.Collisions`), so the check says it, not only the compiler.

### The derived values and the seam

`SourceDerived.Check(package, types, resolution, expressions, templates = null)` returns the tuple
`(IReadOnlyList<SourceRefusal> Refusals, PackageDerived? Derived)`, with `Derived` null exactly when
there is a refusal (ordered by path). It builds a graph with
one vertex per entry: each node, check and deliverable, and each renderer, snippet and macro (folders
and their items included). Each read is resolved with `ScopeTree.Resolve` where it is read.

**Content reads** are what a value is made from. They are what the flags spread along:

- a computed node's expression;
- a body's reads (through the seam);
- a call's arguments;
- a flow node's `ret`, and a loop's `in`.

A condition (`if`, `while`, `until`, a `case` subject, a part's `if`) is never content. Nor is a
read through `pass.previous…` or `pass.history`. A read of `.state`, `.stopped`, `.passes`, `item.id`
or `item.state` carries no flag: those are the engine's facts about a run. The member after a node's id
decides however the node is named: `node.x.state`, `pass.node.x.state`, `pass.previous.node.x.state`
and, over a loop's `.items`, `item.nodes.x.state` alike (D33).

**Inside a function each entry is worked out twice:**

- With its parameters reading nothing: what a call takes from the function
  (`PackageDerived.Functions`, by function id).
- With each parameter standing for the union of its arguments across every call:
  `PackageDerived.FunctionEntries`.

So a call's flags are its arguments' plus its function's own, and a second call that passes nothing
run-dependent stays clean.

**The flags** come from `LineageFlags.Derive`. Starts:

- an agent is model-derived;
- a body whose `TemplateReads.PlacesProfileMacro` is set is profile-carrying;
- `profile.<field>` is profile-carrying and `run.<fact>` run-dependent, where read or passed.

A flag derived `true` is final. A flag derived `false` whose value reaches a body the seam did not
say is not known (`FlagsKnown` false). It is then not judged, and it takes the header's declared
value.

**`reproducible`**:

- A flow node's is true only if everything in its body that its result depends on is reproducible
  (part 04 § 1), earlier passes included.
- A call's is its function's `ret`'s.
- It is set for every node and check, null for other entries.

**Held against the headers**, in this order:

1. A05-003 for nodes that read each other in a circle, said first and alone. A loop iterating its
   own `.items` is one; a while reading its own earlier passes (`pass.previous…`, `pass.history`) is
   not. Since #1504 ruling 7 a read of this pass's node (`pass.node.<id>`, `pass.parent.node.<id>`) is
   an edge as `node.<id>` is, so two body nodes reading each other that way, or a node reading through
   it a flow node that encloses it, are A05-003; one naming a node inside an inner loop that does not
   enclose the reader is S04-039.
2. S03-036 for a leaf's or check's flag. S04-016 for a flow node's or call's flag. S04-015 for a
   flow node's or call's `reproducible`.
3. S03-044 for a decision reading a profile-carrying value. Through the seam, a prompt reading one is
   S04-069 (`a prompt`) when it is a profile-carrying parameter, else S03-044.

**Warnings** (`PackageDerived.Warnings`): S03-037 for every run-dependent decision and S03-038 for
every run-dependent computed node, not only those that read a run fact directly, each with the fact
traced through its causes. These replace `PackageExpressions.Warnings` in `resolve.json`.

`DerivedEntry` holds, for each entry:

- `Key`.
- `Reads`: D4's canonical form, distinct and sorted. A read through `node`, `check`, `input` or
  `param` is the key and its first member (`node.plan.value`); through `item`, `pass`, `run` or
  `profile` it is the path as written.
- `ContentReads` and `ConditionReads`: `LineageRead`s with the key read. These also hold the
  structural reads (a `ret`, a call's `function.<id>`, a folder's items, a renderer piped into, a
  macro placed) that the graph's edges come from.
- `BodyKnown`, `Reproducible`, the three flags, `FlagsKnown`, and `Body` (what the seam said).

`PackageDerived` also holds `RunFacts` (`run.date`, …), `ProfileFields` (bare names, for
`needs.profile`) and `Complete`.

**The seam (D1).** `ITemplateReads.Of(declaration, function)` says what one body reads. The
declaration is a `SourceNode` (an agent's prompt, a rendered node's template), a
`SourceDeliverable`, or a `SourceText` (a renderer, a snippet, a macro, a folder's item), as
`SiteContext.Declaration` names them. It returns a `TemplateReads`, or null for not known:

- `Reads`: paths as written; the resolver resolves them where the body is.
- `Places`: rooted macro ids.
- `Renders`: one `TemplateRender(N, Source, Renderer)` per pipe step.
- `RunSpots`: one `TemplateRunSpot(Site, At, Reads, Unit, Neutral)` per run spot (`Unit` and
  `Neutral` since area 07).
- `PlacesProfileMacro`.
- `Substituted` (since area 07): the reads only a macro's body makes, which are never sent to a model,
  so the prompt rules (S03-044, S04-069) skip them while the flags still spread along them.

An empty or white-space body is known and reads nothing (`TemplateReads.None`).

Since area 07, `SourceCheck.Run` and `CompiledPackage.Publish` always have a seam: the dialect's
`PackageTemplates` (see "templates" below), unless the caller passes its own. What follows is the state of
a caller that passes a seam knowing no body, as the derived step's own unit tests do.

**Without the seam (area 05's state), the derived values are incomplete:**

- Every non-empty body's reads are unknown. An entry's `reads` hold its header's reads only, and
  `places`, `renders` and `runSpots` are `[]`.
- A flag that depends on a body is written as declared, not derived. A declared `true` is never
  refused.
- `PackageDerived.Complete`, `ResolveResult.Complete` and `CompiledPackage.Complete` are `false`.
- These rules are not judged:
  - S04-035 for a while holding an agent or rendered node;
  - S04-052 and the S04-074 warning for snippets, macros and renderers;
  - S04-063 (a macro placed in a function, reading a node that is not there);
  - S03-044 and S04-069 for a prompt;
  - the `node.*` and `check.*` reads of a macro's or snippet's `if` (S03-044, and S03-037 beyond a
    direct `run.*` read, which is still warned of);
  - A05-003 through a body.
- These are missing from `resolve.json` and `lock.json`: `needs.profile` and `run` from bodies; the
  features `macro-markers` and `run-spots`; the built-in renderers in `lock.builtins`.

With a provider, `SourceDerived` re-judges S04-035, applies S04-052, S04-074 and S04-063 to bodies,
and reads each placed macro's or snippet's `if` in the placing body's frame. A provider that knows
only node bodies still leaves `Complete` false, because `Complete` counts every body: snippets,
macros, renderers and deliverables too.

### The two documents

**`resolve.json`.** `ResolveCompiler.Compile(check, registry, scribanVersion, limits = null)` gives a
`ResolveResult`:

- `Document`, a `JsonObject`;
- `Canonical`, its RFC 8785 text: the bytes to store;
- `Hash`, its `sha256:`;
- `Refusals`, `Warnings`, `Complete`.

A check with refusals comes back with them and no document. The document has every member of part
07 § 2.2, in that order, with the manifest's `derivedFrom` (`name@version` or null) after `package`
(see the plan's contract corrections). `title` is the manifest's, or the package's name when that is
null (#1504 ruling 5). There is no time anywhere: the same source and toolchain give the same bytes, whatever the
upload's order or line ends.

- **Formats** (D5). `CompiledFormats.Build(package, types, registry)` returns a `CompiledFormats`
  record: `Bundle`, `FormatHashes`, `TypeHashes` and `Bindings` (each a `JsonObject`), and
  `Refusals` (A05-004). The bundle holds every format a `format` member names
  (or a renderer's `input`), and every format those reach, each with its `$id` and `$schema`.
  - A package format's `$id` is `urn:consultologist:package:<name>@<version>:format:<id>`.
  - A function's own format is keyed `<function>__<id>`, with `$id`
    `…:function:<function>:format:<id>`.
  - Names inside are rewritten to `#/$defs/<key>` (`CompiledFormats.Rewrite`).
  - `formatHashes` and `typeHashes` are the type services' hashes of each format in its own scope.
  - `bindings` is format key, then `generated`, then `{ agentName, agentVersion, via }`.
- **Entries.** Every entry is its header as written, cloned, with:
  - every `format` rewritten in the scope that owns it;
  - every `var` and `missing` path, and every `in`, `case` and `with`, that starts `node.<id>` or
    `check.<id>` written with the resolved key (D3). A macro's or snippet's `if` stays as written.
  - then the derived members: `id`, `parent`, `reads` and the three flags on every entry.

  The members that depend on the kind:
  - Every node also gets `body`, `bodyHash` (null for kinds with no body), `renders`, `runSpots`,
    `formatHash` and `typeHash`; an agent also gets `parts` and `places`.
  - Since area 07 an agent, a `rendered` node and a deliverable also get `placements` and `names`,
    each `runSpots` entry has `unit` and `neutral`, and the built-in renderers used are embedded in
    `renderers` (see "templates" below).
  - A `for` or `while` gets `nodes` and `ret` as keys. An `if` or a `case` keeps `then`, `else` and
    `branches` as written with keys inside, gets a derived `nodes` (every child, in order), and has
    `"ret": null`.
  - A deliverable carries `delivery` and `bundle` as its folder's `index.json` writes them (`[]` and
    `null` when absent; D15).
- **Functions** (D6 as amended). Each is its index as written (`inputs` and `format` rewritten in
  its scope), with `inputOrder`, `ret`, `body` (its top-level keys), `nodes` (its own flat map, keys
  local to it), and its own `renderers`, `snippets` and `macros`. It also has the members of every
  entry:
  - `id`, and `parent` (null);
  - `reads`: its `param.*` and definition reads;
  - the three flags of `PackageDerived.Functions`;
  - `formatHash` and `typeHash`.

  It has no `reproducible`.
- **`graph`** (D7 as amended).
  - `edges` go from what is read to its reader: `reads`, `if`, `for`, `aligned` (a production loop to
    the loop over its `.items`, beside the `for`), `ret`, `calls` (`function.<id>` to each call
    outside every function), `placed` (through the seam) and `gates` (a deliverable's `check`).
  - There are no edges between a function's own nodes.
  - `order` is level by level: a flow node before its body, each level in a topological order, ties
    by source order.
- **`requires`** (D8 as amended). `evaluator` is `^1.0.0` and `scriban` is `^` plus the locked
  version. `operations` holds each custom operation any rule calls (part 07 § 3.1). `kinds` and
  `features` follow `constants.json`'s order. `agents` is each bound agent, name to its integer version (one per agent, A07-014).
- **`needs.profile`** and **`run`** are `PackageDerived`'s.
- **`warnings`** are `{ sentence, at }`, `at` null for the package as a whole. They come from the
  derived step, the resolve step's S04-074, and, when `limits` are given, `PublishNotes.Of`: S04-072
  or S04-073, S04-079, S04-080, measured against the caller's
  `PublishLimits(Ceiling, ItemsPerLoop, CallsPerJob)` (D11). They are distinct, and sorted by `at`
  with null first. The notes work each function out once and apply it at every call, so a chain
  of functions each calling the next several times costs one walk per function, not one per path.

`SourceHash.Of(files)` hashes the listing of § 2.6: `manifest.json`, every `README.md` and every file
under the nine folders, each hash taken after `\r\n` → `\n`. `dag.mmd`, the compiled copies and the
publish report are not in it.

**`lock.json`.** `LockCompiler.Compile(resolve, resolveHash, toolchain)` returns the document as a
`JsonObject` (`CanonicalJson.Write` gives its bytes). It never refuses, and has § 4.1's members in
§ 4.1's order: `documentVersion` (`1`), `package` (`resolve.json`'s name and version), `resolve.hash`,
`resolver` (the `Toolchain`'s package format release and dialect), `libraries` (ordinal), `evaluator`,
`operations` (each custom operation `requires.operations` names, at its exact version),
`builtins` (`formats`, `markdown_escape`, `plain_conversion`, `printer`, plus each built-in renderer
an entry `renders` through; ordinal), `culture` `invariant`, `runtime` (`dotnet`, `unicode`) and
`catalogues` (`agents`, `formats`, `outputContracts`).

`Toolchain.Current(packageFormat, formatsCatalogue, agentsCatalogue, outputContracts = null)` reads
the four library versions off the loaded assemblies (`Scriban`, `Markdig`,
`Corvus.Text.Json.JsonLogic`, and `Corvus.Text.Json.Validator` under `Corvus.JsonSchema`).
`Environment.Version` is the runtime, and `Unicode` is `16.0` on .NET 10 (`unknown` otherwise). A test
builds a `Toolchain` by hand, as the goldens do.

**The pair.** `CompiledPackage.Publish(upload, registry, toolchain, limits = null, templates = null)`
is steps 1, 2 and 4.1 to 4.3 of part 07 § 5. It checks, compiles `resolve.json` and hashes it, then
builds `lock.json` with that hash and hashes it. It returns the tuple
`(IReadOnlyList<SourceRefusal> Refusals, CompiledPackage? Compiled)`. `Compiled` is null whenever
`Refusals` is not empty: the check's, or the compiler's. Its guard also gives null, with no
refusal, if the compiler wrote no document, which a check that refused nothing never leads to. So in
practice `Compiled` is null exactly when there is a refusal. A `CompiledPackage` holds `Resolve`,
`ResolveHash`, `Lock`, `LockHash`, `SourceHash`, `Warnings` and `Complete`. `Publish` returns the
pair even when `Complete` is false: the caller decides, and a pair
that is not complete is not the one to store. The rules run over the source model before compiling,
not over the compiled document (part 07 § 5 step 2 reads the other way). The source's in-package
tests are not run here.

### What an engine checks

All of these are pure functions here. None throws on a document or on bytes; a member that is
missing or of another shape is refused.

- **At load, `CompiledPackage.Verify(package, version, resolve, lock, stampedResolveHash,
  stampedLockHash)`.**
  - S07-006 when the resolve bytes are not exactly the UTF-8 of their own canonical text (no BOM, no
    re-indent, no repeated member, no trailing newline), or do not hash to the stamp.
  - Else S07-017 when the lock fails the same test, or its `resolve.hash` is not the resolve's hash,
    or the resolve's `lock` is not `lock.json`.
  - Else null.
  - It does not compare the document's `package` with the arguments.
- **At job start, `RequiresCheck.Engine(resolve, lock, engine)`.** The checks are D10's.
  - S07-003 for Scriban, the evaluator and each operation of `requires.operations` when:
    - the engine's version is outside the caret range;
    - the operation is missing (the engine's version is then said as `none`);
    - the locked version is not a version;
    - or the package is locked before one of the component's break points, with the engine at or
      past it.
  - Every other break point is classed by where the lock holds its component: a library of
    `libraries` (Markdig, the two Corvus libraries), held against `EngineCapabilities.Libraries`, or a
    member of `builtins` (the machinery, a built-in renderer), held against
    `EngineCapabilities.Builtins`. S07-003 for such a component only when all of these hold:
    - the engine has a break point for it;
    - the locked version is before that break point (a missing locked version counts as before);
    - the engine's own version of it is at or past the break point, or is missing or not a version.

    A component the lock holds nowhere: a library the engine lists in `Libraries` is refused (the
    lock is before every break point of it); an operation or a builtin the package does not use is
    not held; a name neither knows is refused. A missing or invalid version is said as `unknown`.
    With no break point for a component, nothing is said of it.
  - S07-004 for each kind or feature the engine lacks, and S07-005 for each agent version it lacks.
  - At most one refusal per component. Where the document says nothing, the sentence says `unknown`.
    A document that cannot be read (parsed from text that repeats a member, which throws when first
    read) is one S07-003 naming `unknown`, on `resolve.json` if it is that one, else on `lock.json`.

  `CaretRange.Admits("^a.b.c", v)` admits `>= a.b.c` and `< (a+1).0.0`. A version is three numeric
  parts; anything else never admits, and compares lowest. `requires.spec` is not read here.
- **At job start, `RequiresCheck.Profile(resolve, profile)`.** For each field of `needs.profile`
  that is missing, empty or white space: S06-031 for the signature, S06-032 with the field's label
  (`closing line`) for any other. A `needs.profile` that is not a list of names, or a `resolve.json`
  that cannot be read, is read as needing every field.
- **At import, `ResolveSentences.ImportIgnored(dropped)`** gives S07-016 when `resolve.json` or
  `lock.json` was dropped. **In CI, `EvidenceCommitted(path)`** gives S07-015. `ResolveSentences` is
  internal, so the Api and the tests can call these, but the Editor cannot.

### Sentences

`ResolveSentences` (internal) has one method per sentence the area says:

- S03-014 for reference strings;
- S04-015 to S04-017, S04-025 to S04-030, S04-035 to S04-040, S04-044 to S04-047, S04-050 to
  S04-061, S04-063 to S04-066, S04-068 to S04-070, S04-072 to S04-075, S04-079 and S04-080;
- S06-031, S06-032, S07-003 to S07-006 and S07-015 to S07-017;
- the area sentences `A05-001`, `A05-003`, `A05-004` and `A05-005`. The area sentences are listed in the conformance README; there is no
`A05-002` (D13's "a member the thing does not have" is S03-014). Where two rows state one rule,
D2 says which is said. S03-036 and S03-044 are now said, in `ExpressionSentences`' words.

The sentences no source package reaches are listed in `RefusalPackagesTests.ResolveNotShipped`, each
with its reason:

- S04-063 needed the seam; since area 07 it has its case (a function's own macro placed by its agent, whose `if`
  reads a node outside the function).
- S04-072, S04-073, S04-079, S04-080 and S04-074: notes and warnings.
- Part 07 § 10's: job start, load, import, CI.

### What it leaves to later areas

- **Area 07, the seam: now built** ("templates" below). `PackageTemplates` is the `ITemplateReads`
  `SourceCheck.Run` and `CompiledPackage.Publish` use; the stub reads (`ResolveFixture.*Reads()`) are
  gone, and the goldens were regenerated from the dialect's reads (they gained `placements` and
  `names`, and nothing else moved). Area 07 also built a renderer's and a macro's formats, where a
  macro or a snippet is placed, and the envelope. A deliverable's format is area 08's.
- **Area 08: a check's and a deliverable's rules.** A check's format is S06-009. The `gates` edge is
  written here from a deliverable's `check`, and D15's `delivery` and `bundle` on each deliverable
  entry are for areas 06 and 08 to confirm.
- **Area 06: calling the checks.**
  - `Verify` at load.
  - `RequiresCheck.Engine` and `Profile` when the Create page asks and when a job starts.
  - Building `EngineCapabilities`: `Libraries` is required, so the engine must give its own library
    versions.
  - Comparing `package` and `version` if it wants that.
  - Reading `requires.spec`.
  - Running loops, whiles and calls as `resolve.json` describes them.
  - Loading only `resolve.json`.
- **Area 11: tests and writing.** Step 3 of part 07 § 5 (the in-package tests against the in-memory
  `resolve.json`), the publish report, writing the folder and the stamp, storing only a pair that is
  `Complete`, `Toolchain.Current` with the release's names, `PublishLimits` from the engine's
  numbers, and § 4.4's corpus re-render. The break points belong to the engine's configuration and
  are checked here.
- **Area 13: `dag.mmd`.** Generating it, and failing a committed one that differs. `SourceHash`
  leaves it out. `graph` has no edges inside a function; a diagram that needs them would add them
  with function-prefixed keys.
- **Area 09: the record.** Sub-row keys and recording run facts.
- **The sentence review (#1483).** S07-003 for a break point names a caret range the package never
  declared, and an engine version that can look in range. The `unknown` and `none` fillers also
  need a look.

### The conformance cases

- **`refusal-packages`** gains a case for each sentence of this area that a source reaches:
  `tests/V31Source/RefusalPackages.Resolve.cs`, exported as before (see the reader's section). Some
  earlier cases changed so that their valid side reads what the real scope now types.
- **`compiled-documents`** is new: `tests/V31Resolve/CompiledDocumentsExport.cs`. For each of
  `nested-note`, `conditional` and `functions` it writes a folder with the upload's `source/`,
  `toolchain.json`, `resolve.json`, `lock.json` and `about.txt` (with both hashes). The cases are
  compiled with a fixed toolchain and, since area 07, the dialect's reads. `ThePinnedFamily_IsWhatPublishGives` holds the pin
  to what `Publish` gives.

  The same bytes are the goldens in `tests/V31Resolve/Golden/`: canonical, one line, no final
  newline. `GOLDEN_UPDATE_V31=1` rewrites them. The content repository's `scripts/check-v31.py` has
  a `compiled-documents` group. It validates each pair against `resolve.schema.json` and
  `lock.schema.json`, and recomputes `lock.resolve.hash` from the resolve's bytes.

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport|FullyQualifiedName~CompiledDocumentsExport"
```

### Not wired in

No publish path compiles a 31 package yet, and 31 stays outside `AcceptedSpecVersions`. Since area 06
(the next section) the engine calls `CompiledPackage.Verify`, `RequiresCheck.Engine` and
`RequiresCheck.Profile` when it loads and starts a 31 job, which it does only where
`WorkflowPackages__PreviewSpecVersions` lists 31.

## specVersion 31: the run (preview)

*Area 06 of the Milestone 39 build (#1503). Built from
[the engine and the pages](package-format-v31/09-engine-and-pages.md) §§ 1, 2.1, 3.1 to 3.4 and 3.6,
[loops and functions](package-format-v31/04-loops-and-functions.md) §§ 2 to 8 as they run,
[expressions](package-format-v31/03-expressions.md) §§ 5.3, 6, 9 and 11.3,
[the record](package-format-v31/08-record.md) §§ 3 to 5 for what a row and a decision carry, and
[the plan](package-format-v31/plans/area-06-engine-loading-and-scheduling.md) for the decisions (the rulings R1
to R5, D1 to D13, and those taken during the build). How the engine loads, starts, limits and drains a 31 job
is in [the engine at specVersion 31](engine-v31.md).*

`Consultologist.PackageFormat.V31.Run` runs a published 31 package. It builds the in-memory plan from
`resolve.json` alone and runs it: leaves, `for`, `while`, `if`, `case`, and `call` with its function's body
inline, under the per-job ceiling, the retries and the limits. It is a pure library. The run talks to the
outside only through two interfaces, `IRunHost` (agent calls, renders, timers, the clock) and `IRunRecorder`
(rows and decisions), so the same runner can serve the engine's orchestration, area 10's byte-equivalence run and
area 11's in-package tests. Nothing here reads a clock, a file or the network.

### The stamp

`PublishedStamp(string ResolveHash, string LockHash)` is the "published" stamp of a 31 version (D1):
`publish.json` (`PublishedStamp.FileName`) in the version's folder,
`{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }`. `ToJson()` writes it canonically.
`PublishedStamp.TryRead(string? json)` gives null unless the text is a JSON object with `specVersion` the
integer 31 and two hashes, each `sha256:` and 64 lowercase hex digits. It ignores other members (area 11 writes
the stamp and may add some), refuses a repeated member, and never throws. The engine chooses the load path by
it: a 31 stamp takes the 31 path, and anything else, no stamp included, takes today's.

### The plan

```csharp
public static (RunPlan? Plan, SourceRefusal? Refusal) Build(string resolve, string resolveHash, string lockHash, TimeSpan? matchTimeLimit)
```

`RunPlan.Build` takes the text of a `resolve.json` the caller has already verified (`CompiledPackage.Verify`) and
gives the plan, which is never stored. A `RunPlan` holds:

- `Package`, `Version`, `ResolveHash`, `LockHash`, and `Document` (the compiled document, read-only);
- `Entries`: every node, check and deliverable, by key;
- `TopLevel`: the keys with no parent, in `graph.order`;
- `Functions`;
- `Inputs` and `InputOrder`, `NeedsProfile` (`needs.profile`), `RunFacts` (the `run` list), and `Snippets`;
- `Formats`, a `PlanFormats` over the bundle;
- `Evaluator`, a `CorvusJsonLogicEvaluator` of its own (04-D47), with `matchTimeLimit` as its limit on
  `matches` and `has_word` (below).

Every rule (`if`, `expression`, `while`, `until`) is compiled on that evaluator with its entry's
`plainConverts` list (D3).

**A `PlanEntry`** has these members:

- `Key`, `Id` and `Kind`. `PlanKind` is `Agent`, `Rendered`, `Computed`, `For`, `While`, `If`, `Case`, `Call` or
  `Deliverable`.
- `Parent`, and `Function`. `Function` is the function a `call` calls, and null on every other kind. An entry's
  own function is known from the `PlanFunction.Entries` that holds it.
- `Order`: the position in `graph.order`. For a node of a function it is the position in the walk of the
  function's body: each node, then its children.
- `Json` and `Format`; the compiled `If`, `Expression`, `While` and `Until`; and `Printed(member)`.
- `Body`: a `for`'s or `while`'s children. `Branches`: an `if`'s then and else, or a `case`'s branches and then
  its else, each a `PlanBranch(Nodes, Ret, When)`.
- `Ret`.
- `Reads`: the compiled `reads`, every `var` of its rules, and its `in`, `case` subject and `with` values.
- `Depends`: the keys those reads name, through the longest key at a dot.
  `pass.(parent.)*node.<id>` names that pass's node (part 04 § 3.3). Other reads through `item`, `pass`, `param`,
  `run`, `input`, `profile` or `snippet` name nothing. An entry never depends on itself, but a `while`'s
  `Depends` can hold its own body nodes, which the runner skips.

A `PlanFunction` has `Id`, `Inputs` (in `inputOrder`), `Body`, `Ret`, `Entries` (its own map, with keys local to
it), `Json` and `Format`.

**What `Build` refuses.** A document `Build` cannot make a plan from is not what publish wrote. It is refused
with S07-006 for its package. These are the cases:

- a member missing or of the wrong kind, or an unknown kind;
- a key named and absent, a read under `node.`, `check.` or `deliverable.` that names no entry of its scope, or
  a key missing from `graph.order`;
- a `ret` outside its body, or a body holding an ancestor or the entry itself;
- reads that form a circle, or an entry that depends on its own ancestor. Each flow node depends on its
  children for this check, so a read of a descendant is allowed;
- a function that calls itself, directly or through others, or a call whose `with` is not exactly its
  function's parameters. The literal `null` argument is accepted;
- a deliverable inside a flow node, or an input not in `inputOrder`;
- a rule the evaluator declines.

`Build` never throws on the document. A null argument, and a time limit the evaluator refuses, are the
caller's errors.

**`PlanFormats`** holds values to the bundle. `Expand(format)` inlines every `#/$defs/<key>` and drops `$id`,
`$schema` and `$defs` from the inlined entries, to depth 64 (`PlanFormats.MaxDepth`) within
`FormatTypes.SchemaBudget`. A cycle, a missing key or any other reference gives `false`, the schema nothing
fits.

- **A format file's own `$defs`.** The bundle keeps a file's definitions and its `#/$defs/<name>` references as
  written, beside the names it rewrote to `#/$defs/<bundle key>`. So inside an inlined entry a reference is
  first one of the entry's own `$defs`, then the bundle's entry of that key. Each entry's definitions are its
  alone: an entry inlined inside another reads its own.
- **A known gap.** A file that names a format `x` and also defines `x` in its own `$defs` publishes, and both
  references are written `#/$defs/x`. The run cannot tell them apart and reads both as the file's own definition,
  where publish read the bare name as the format. The compile would have to refuse such a file or rename the
  definition; it does neither today.
- **The schema budget** counts schemas as publish counts them for A03-011 (the format, each value of
  `properties`, `items`, each member of `anyOf`, each entry or definition inlined), so a format file publish
  accepted is never past it at run. The schemas a site's format writes itself are counted apart from those of
  the entries it refers to.
- **The node budget.** One expansion counts the nodes it copies against `PlanFormats.NodeBudget` (1,048,576:
  half of `SourceBounds.TotalBytes`, as many JSON nodes as a whole source can hold); past it, `false`. The
  object written for each inlined reference and an `allOf` list are not counted, so what is written can pass
  the budget by a small multiple, never without bound. It stops a forged document that repeats a very large
  `enum` from thousands of places. Publish has no such bound, so a package can publish and still pass it: a
  record of 4,500 fields, each referring to a format with a 250-value `enum`, is within the schema budget and
  writes about 1.1 million nodes. Such a format fits nothing at run.

`Validate(value, format)` is `Expand` followed by `CorvusFormatValueValidator.ValidateExpanded`.
`IsText(format)` is true for `plaintext`, `markdown_block` and `markdown_inline`, by their bundle `$id`.
`Read(reply, format)` gives a text reply as a string. Any other reply is parsed as JSON, to 512 levels, with a
repeated member refused; `Parsed` is false when it does not parse.

### The runner

```csharp
public PlanRunner(RunPlan plan, RunLimits limits, IRunHost host, IRunRecorder recorder)
public Task<RunResult> RunAsync(RunInputs inputs, DateTime startedAtUtc)
```

`RunInputs(IReadOnlyDictionary<string, JsonNode?> Inputs, JsonObject RunFacts, IReadOnlyDictionary<string, string?> Profile)`
is what a job starts with:

- `Inputs` holds each supplied input by id, in its read form `{ value, content, title }`. An input not supplied
  is absent.
- `RunFacts` holds the `run.*` facts by name, without `run.`.
- `Profile` holds the profile's fields.

`RunResult(RunState State, string? Sentence, string? FailedKey, IReadOnlyDictionary<string, JsonNode?> Values, int AgentCalls, int PeakInFlight)`
says how the run ended:

- `State` is `Completed`, `Failed` or `TimedOut`;
- `Sentence` and `FailedKey` say what failed;
- `Values` holds every top-level entry's settled read form;
- `AgentCalls` counts the calls made;
- `PeakInFlight` is the most calls in flight at once, for telemetry only.

**`RunLimits`** is a record with nine members. Each defaults to the spec's starting value:
`MaxAgentCalls = 16`, `CapacityWaitMinutes = 10`, `ServiceAttempts = 5`, `JobMinutes = 30`, `DeliveryMinutes = 15`,
`DeliveryAttempts = 3`, `LoopItems = 200`, `JobAgentCalls = 500`, `RenderSteps = 100_000`.

- `ToJson()` writes the header's `limits`: exactly part 08 § 3's nine members, in its order (D8).
- `RunLimits.FromJson` reads them back, so a job's limits are rebuilt from what its orchestration input recorded.
- A limit the runner counts against that is below 1 throws `ArgumentOutOfRangeException`, through `with` too.
  `DeliveryMinutes` and `DeliveryAttempts` are not checked here, but the engine's settings refuse them below 1.

**A round.** The runner does three things in each round:

1. It settles, in the fixed order, everything that needs no call, until nothing more settles: an `if` deciding,
   a `computed` node evaluating and its value checked against its format, a skip.
2. It dispatches the ready agent and render calls in the fixed order. An agent call goes out only while fewer
   than `MaxAgentCalls` are in flight. Renders and deliverables take no slot.
3. It awaits `Task.WhenAny` of what is in flight and the job's timer, then settles what finished.

The run ends when nothing is in flight and nothing is ready. A failed node fails the job: nothing more is
dispatched, and nothing in flight is awaited. If nothing is in flight, nothing is ready and something is still
unsettled, the document was not what publish wrote. The first unsettled instance in the fixed order then fails
with S07-006: this is the stuck guard.

**The fixed order** (part 09 § 3.2) ranks an instance by its entry's `Order`, then its item positions (outermost
first), then its passes. Inside a function, the rank begins with the `Order`s of the enclosing calls, then the
entry's walk position, then the whole item path (the caller's items, then the function's own), then the passes.
So a function's agents go out at their call's place, before later package nodes.

**Readiness.** An instance is ready when every key in its `Depends` that it can see has settled. A loop never
waits on its own descendants. A key in a frame not yet opened makes the reader wait for the flow node that holds
it. A loop over another loop's `.items` waits only until the production loop has read its source. After that,
check item *i* opens when production item *i* settles, so checking overlaps production (part 09 § 3.1).

**Leaves.**

- An `if` that is false or `null` skips the leaf, with `JsonLogicPrinter.Reason`'s printed reason and a
  `RunDecision`. A condition that fails fails the node with `ExpressionSentences.RunTime`.
- A computed `null` is a value (04-D49). Any other computed value that does not fit its format is A06-003.
- A rendered node or a deliverable renders through the host. A deliverable settles `Composed`, and area 08 takes
  it from there. A render past its steps is S05-062.
- `.content` is a text format's text, else the canonical JSON of the value (D11). `ValueHash` is
  `CanonicalJson.HashText` of `.content`.

**Agent replies** (`PlanRunner.Agents.cs`).

- A reply is read by `PlanFormats.Read` and validated against its format. An invalid one under `onInvalid: "retry"`
  is asked once more. That repair goes out at once in the slot the node holds, and it is not a new call in
  `AgentCalls`.
- An invalid reply under `onInvalid: "fail"`, or a second invalid reply, fails the node with A06-005.
- `failIfEmpty` is the package's failure sentence, used as written (part 05 § 1.2). It is judged after
  validation and never repaired. Empty is judged as at version 30: a blank string or an empty list.
- `Throttled` adds its wait to the node's capacity wait. A wait that would pass `CapacityWaitMinutes` fails the
  node with S09-010 instead of being taken.
- `ServiceFailure` counts an attempt. At `ServiceAttempts` the node fails with S09-011.
- Between attempts the runner waits on `host.DelayAsync`, holding its slot. A wait of 0 or less is taken as
  5,000 ms.
- `Failed` (a call that could not be made: `ErrorKind` `plan`, `steps` or `compose`) fails the node at once, with
  S07-006, S05-062 or A06-008, and is never retried.
- The agent row's `Detail` carries `retries` in part 08 § 4's shape (`{ atUtc, waitMs, kind, error }`, `kind`
  `capacity` or `service`, `error` `timeout`, `5xx` or `connection`) and `invalid` (`{ reason, action, attempts }`,
  or null).

**The job limit.** One timer for the time left is made the first time the run waits. When it ends, or the host's
clock is `JobMinutes` past `startedAtUtc`, the run is `TimedOut` with S09-012. Every instance in flight gets a
`Cancelled` row with S08-009, late answers are not read, and abandoned tasks are observed so that a late fault is
never unobserved. `DelayAsync` takes a `CancellationToken`. The run cancels every timer it made when it ends,
because an orchestration does not complete while a durable timer is outstanding.

**`for`** (`PlanRunner.Loops.cs`).

- **The source.** An array input gives item ids from 0. Every other list gives ids from 1: a node's list,
  `item.value.<field>` and `param.<id>.value`. A snippet folder's ids are its items' stems; `item.value` holds
  the members its format declares, and `item.content` holds the body. The function's own folder is used before
  the package's. A loop over `.items` takes the production items' ids.
- **Sizes.** An empty or `null` list settles at 0 items with `.stopped` `"end"` (R1), and `null` gives the reason
  S04-077. More than `LoopItems` items fails the loop with S04-078. A value that is not a list fails it with
  A06-006.
- **`maxParallel`** caps the open items of each loop instance, so caps multiply along nesting (part 04 §§ 2.3,
  8). An item is open from the time its frame opens until it settles.
- **`parallel: false`** opens item *n*+1 only when item *n* has settled.
- **`max` and `until`.** Items past `max` are `NotRun` with S04-002, and `.stopped` is `"bound"`. `until` is
  decided after each item of a sequential loop (`until.<n>`, 1-based). When it holds, the rest are `NotRun` with
  S04-001, named by position.
- **Items.** An item's state is its `ret`'s. Its name is the `itemLabel` field, trimmed and cut to 120 characters
  ending `…`, or else `<loop label> <n>`. Every row inside an item carries `itemName` and `positionName`.
- **What a loop reads:** `.value` (the completed items' values), `.items` (part 04 § 2.6's element), `.content`
  (D11), `.state` and `.stopped`.

**`while`, `if`, `case`** (`PlanRunner.Flow.cs`).

- **Passes.** A `while`'s passes run one after another, each in its own frame binding `pass`:
  `{ number, previous, history }`. A pass record holds the body's nodes and its branches' nodes by id, but not
  inner loops'. `previous` is one level deep and `history` is `{ number, node }`, so a record's size grows
  linearly with the passes.
- **The check.** With `checkFirst: false` the check is decided after each pass (`pass.<n>`), then `until`
  (`until.<n>`); the check wins a tie. With `checkFirst: true`, `until` runs after pass *n* and the check before
  pass *n*+1. At the bound the check is decided once more (`pass.<max+1>`), so `"bound"` means the check never
  stopped holding (S04-005).
- **Zero passes.** A while with zero passes reads as skipped.
- **`if` and `case`.** An `if` or a `case` opens its taken branch as a frame that siblings can see into (only
  loops hide their bodies). A `case` records its branch as its 1-based position, or `else`. An empty branch is
  skipped with S04-006 / S04-049's form. An `else` taken when it is empty (a value that escaped its format) fails
  the case with A06-007.
- **Branches not taken.** A flow node inside a branch not taken reads `skipped`, and a leaf there reads
  `not_run`; neither gets a row.

**`call`** (`PlanRunner.Calls.cs`). A call's `if` is decided as a leaf's is. The call then opens a frame over its
function's own map, sealed from the caller's `node`, `item` and `pass`, with `param.<id>` bound to each argument:

- a field path, `run.<fact>` or `profile.<field>` binds `{ value, content }`, `content` being its text;
- a whole reference (`node.<id>`, `check.<id>`, `item`, `item.parent`, `param.<id>`) binds the `.value` and
  `.content` the caller reads;
- `snippet.<folder>.<item>` binds as a loop over the folder would;
- `null` binds `null`.

The call settles as its function's `ret`, with its whole read form. Its row carries `function`, `depth` and
`arguments` (`[{ param, hash }]`, never a value). `depth` on every row is the call depth. A frame chain deeper
than `FormatBounds.Depth` + 1 fails the call with S07-006.

### Keys (D13)

```csharp
public static string Of(string entryKey, IReadOnlyList<string> itemPath, IReadOnlyList<(int Position, int Pass)> passes)
```

`RunKeys.Of` is the one writer of an instance's row key: the entry's key, then `:` and the item ids from the
outermost loop inward joined by `/`, each while's pass written `#<n>` at its position in that path. Examples:
`node.sections.paragraphs.draft_paragraph:2/1`, `node.refine.judge#2`, `node.sections.refine.judge:2#1`. Inside a
function the key goes through the call node: `node.standards.section.patient_draft:hpi`, nested
`node.hpi_section.refine_call.judge`. An item's row is the loop's key with its path (`node.sections:2`), and a
pass's row is the while's (`node.refine#2`). Area 09 owns the record's final form.

`AgentCall(string Key, string EntryKey, string? Function, int Attempt, bool Repair, JsonObject Scope)` carries
three keys:

- `Key`: the row key;
- `EntryKey`: the entry's key, local to its function inside one (`node.patient_draft`);
- `Function`: the function the entry lives in, or null.

So the entry is `plan.Functions[call.Function].Entries[call.EntryKey]`, or `plan.Entries[call.EntryKey]` at the
package's level. `Scope` is `RunScope.Data` over exactly the entry's compiled `reads` (since area 07, plus
`PlanEntry.NeutralReads`). Area 07 adds two optional members, `Placed` and `Faults` (see "templates" below).

### The seams

| Seam | Here | Who plugs in |
|---|---|---|
| `IRunHost` | `UtcNow`, `CallAgentAsync(AgentCall)`, `RenderAsync(RenderCall)`, `DelayAsync(TimeSpan, CancellationToken)` | the engine's `DurableRunHost` (activities, durable timers, the replay-safe clock); area 10's byte-equivalence run and area 11's in-package tests bring their own |
| `IRunRecorder` | `RowAsync(RunRow)`, `DecisionAsync(RunDecision)`, awaited inline, so a task must complete synchronously or be a replay-safe one the orchestration gave | area 09 (the record's storage), replacing the engine's `EntityRunRecorder` (D12: counts on the job's entity, telemetry by key, state and hash) |
| `AgentTurn`, `RenderTurn` (in the library, since area 07) | `AgentTurn.Prepare(plan, call, stepLimit, strictRejects)`, `AgentTurn.Finish(prepared, reply)`, `RenderTurn.Render(plan, call)` | the engine's two activities call them; area 07 removed the Api's `IBodyComposer` and `ScribanBodyComposer` (see "templates" below) |
| A rendered deliverable settles `Composed` | the runner stops there | area 08 (outcome, gating, delivery) |
| `V31StartChecks` (in the Api) | `Account`, `Inputs`, `RunFacts` | area 12's Create page, which calls the same checks |
| `SpecVersionAdmission` (in the Api) | `Admits`, `Runs31`, `NotSupported` | area 11 (publish, import, `WorkflowPackages/Current`) |

`RunRow(string Key, string EntryKey, PlanKind Kind, RowState State, string? Reason, string? ValueHash, JsonObject Detail)`
and `RunDecision(string Site, string Member, string Printed, bool? Result, string? Branch, DateTime DecidedAtUtc)`
are what area 09 turns into the record. `RowState` is `Completed`, `Skipped`, `NotRun`, `Failed`, `Cancelled` or
`Composed`. A decision's `Member` is part 08 § 5.2's site (`self`, `pass.<n>`, `until.<n>`, `case`, and since area 07 a placement's
`part.<id>`, `snippet.<id>`, `macro.<id>` or `macro-folder.<id>`), and its time
comes from the host's clock. No row or decision carries a value from an input or a model: only keys, states,
reasons, hashes, counts and package text.

### The time limit (R2)

`public CorvusJsonLogicEvaluator(TimeSpan? matchTimeLimit = null)`, with `MatchTimeLimit`. With `null` the
evaluator does what it did before, and publish-time probing uses it that way. With a limit, the evaluator works
as follows:

- `matches` runs its pattern with the limit as the `Regex` match time-out, from a pattern cache of the instance's
  own.
- `has_word` checks a `Stopwatch` every 4,096 candidate positions.
- Past the limit the rule fails with `JsonLogicFailureKind.MatchTooSlow` (`Detail` `matches` or `has_word`;
  `JsonLogicFailure.Limit` the limit). `ExpressionSentences.RunTime` words it A06-001, `<n>` in seconds (at most
  three decimals).
- A limit of zero, or longer than a regex time-out can be, throws at construction.

The engine passes `ConsultGeneration__RegexMilliseconds` (default 2000). The spec's "reads no clock" was there
so that a replay decides alike. With a limit, an evaluation close to it can decide differently on a Durable
replay, which fails the orchestration; the PR flags this for the owner.

### Sentences

The run says these rows of `sentences.json`:

- S03-040, S03-041 (conditions);
- S04-001 to S04-006, S04-049, S04-077, S04-078 (loops and branches);
- S05-062 (steps);
- S07-006 (a document publish did not write);
- S08-009 (cancelled);
- S09-010, S09-011, S09-012, S09-015 (capacity, service, the job limit, the job's calls).

Area 06 adds these area sentences, in `V31Contract` and the conformance README:

- A06-001 (the time limit);
- A06-002 (publish: a `checkFirst` while reading its own body, R3);
- A06-003 (a computed value that does not fit);
- A06-005 (a reply that does not fit);
- A06-006 (a source that is not a list);
- A06-007 (a `case` value no branch lists, with no `else`);
- A06-008 (a body that fails to render, `RunSentences.CouldNotRender`).

The engine says A06-004 and A06-009 to A06-013 (see the engine guide). A06-003 and A06-005 do not name the
function they are said in.

### What it leaves to later areas

- **Area 07: now built** ("templates" below): the body composer, and with it `parts`, `macro-markers` and
  `run-spots`, which the engine runs. A body reading a local name (`node.draft_summary` inside `refine`) reads its
  rooted key through the entry's `names`.
- **Area 08:** everything after `Composed`.
- **Area 09:** the record. It maps `RunRow.Detail` and the decisions to storage rows, and gives the final form of
  keys. Area 09 also records the tokens, which the agent activity only logs today.
- **Areas 11 and 12:** they call `SpecVersionAdmission` and `V31StartChecks`.
- **Not modelled by the tests' oracle:** a loop over another loop's `.items` chained further, and calls nested
  more than one deep.

### Tests and goldens

The library's tests are in `tests/V31Run/`:

- `RunFixture` builds plans from the compiled goldens through `CompiledPackage.Publish` and `RunPlan.Build`.
- `FakeRunHost` is scripted by key, holds answers until released, and runs on a manual clock.
- `ListRecorder` collects the rows and decisions.
- `DispatchOracle` checks the fixed order independently of the runner's rank.
- `RunGoldenTests` pins three whole runs, `tests/V31Run/Golden/{nested-note,conditional,functions}.run.txt`, from
  replies seeded by the row key (`WorkflowStubOutputs`). Each holds the dispatch sequence, every row, every
  decision and the result. `GOLDEN_UPDATE_V31=1` rewrites them, and `Golden/.gitattributes` keeps them LF.
- `PlanRunnerNestingTests` holds the ceiling and the loop caps under three nested parallel loops of 10.

## specVersion 31: templates (preview)

*Area 07 of the Milestone 39 build (#1504). Built from
[templates and macros](package-format-v31/05-templates-and-macros.md) in full, with
[types and inputs](package-format-v31/02-types-and-inputs.md) § 3.5 for what a model is sent,
[compiled documents](package-format-v31/07-compiled-documents.md) §§ 2.5 and 3.1,
[the record](package-format-v31/08-record.md) §§ 4, 5.2 and 8.2 for what a row carries, and
[the plan](package-format-v31/plans/area-07-prompts-and-macros.md) for the decisions (the owner's rulings, D1 to
D18, and those taken during the build). The dialect itself is the format registry's
`external/consultologist-package-format/v31/scriban-dialect.md`.*

`Consultologist.PackageFormat.V31.Templates` is the template language of 31. It does two jobs, and both are pure
library code, with no Azure in them:

- **At publish** it holds every Scriban body of a package to the dialect, in the scope the resolver gives. It then
  tells the resolver what each body reads, places, renders and fills.
- **At run time** it composes what a model is sent, or what a `rendered` node or a deliverable renders: snippets,
  parts, renderers and run spots, with the run-neutral text. It then checks a reply's macro markers and replaces
  them literally.

A model's reply, an input's value and a profile value are never parsed as a template. Only package text from the
plan reaches `Template.Parse`. Nothing here throws on package content, a document or a reply; a null argument is the
caller's error.

### The step

`SourceCheck.Run(upload, registry, templates = null)` now has six steps, each run only when the ones before refused
nothing: the reader, the type rules, the resolve step, the expressions, **the dialect**
(`SourceTemplates.Check(package, types, resolution, registry)`), and the derived step (D18). The dialect always
runs. The derived step reads bodies through `templates` when the caller passes one, and otherwise through the
dialect's `PackageTemplates`. `SourceCheckResult.Templates` is that `PackageTemplates`. It is set when the dialect
refused nothing, and stays set when only the derived step refuses. `CompiledPackage.Publish` passes `templates` to
the derived step only: the compiler always writes `placements` and `names` from `check.Templates`.

`SourceTemplates.Check` returns a `TemplateCheck(Refusals, Warnings, Templates)`, with `Templates` null exactly when
there is a refusal. Refusals and warnings are said once each and ordered by path. It visits, in order:

1. each agent's prompt and each `rendered` node's body, in `nodes/`, `checks/` and each function, at the node's
   frame;
2. each deliverable, at the top of `nodes/`;
3. each renderer of the package and of each function.

Each snippet, part and macro a template places is checked where it is placed. Then every snippet and macro, placed
or not, is held to the rules that need only its text: its syntax, a snippet's format and header, a snippet cycle,
and a macro's markers, placements and Markdown. Then come the parts on a node that is no agent, the
profile-carrying reads (S05-064), and the routes that carry a folder item's body out of its loop (A07-013). An
unplaced snippet's or macro's reads are not typed.

### The types

| Kind | Types |
|---|---|
| One body read | `TemplateSyntax.Parse(text)` gives a `TemplateSyntax`: `Tags` (`TemplateTag`: line, column, offsets, the `~` and `-` marks, `AloneOnLine`, `TextBefore`, `TextAfter`), `Outputs` (`TemplateOutput`, with `Path`, `Pipes` of `TemplatePipe`, `Paths`, `Depth`), `Fors` (`TemplateFor`), `Ifs` (`TemplateIf`, with each `TemplateTest`), and `Faults` (`DialectFault(Id, Line, Fill)`). It reads with Scriban 7.2.5's own parser and lexer, so a tag is exactly what Scriban reads as code. A comment or a raw block gives nothing. |
| The sites | `TemplateSite`: `Prompt`, `Rendered`, `Renderer`, `SnippetBody`, `PartText`, `MacroInstruction`, `MacroBody`, `Deliverable`. |
| The package check | `SourceTemplates`, `TemplateHost` (a body at its place: site, subject, file, declaration, function, frame, part), `TypedRead` (a read typed, with its head and rooted head), `Placement` (a placement as written), `CheckedBody` (one body held at its place: syntax, reads, placements, renders, run spots, `Units`, `FolderItems`), `PlacedUnit` (one unit placed: `Site`, `Within`, `Body`, `If`, and for a macro `Marker`, `Instruction` and `MacroBody`), `MacroMarker` and `MacroMarkers.Open`, and `TemplateCheck`. |
| The seam | `PackageTemplates : ITemplateReads`, with `Of`, `Units` (D2), `Names` (D1) and `Warnings`. |
| Sentences | `DialectSentences`: one method per publish sentence, each returning a `SourceRefusal`. `ReplySentences`: S05-053 to S05-059. `RunSentences` gains S05-012, S05-014 and A07-006. |
| Composing | `BodyComposer.Compose(plan, ComposeRequest)` gives a `Composition`, made of `ComposedSpot`, `ComposedRender`, `OfferedMacro` (with `ProfileRead`) and `ComposedUnit`. |
| One turn | `AgentTurn.Prepare` / `AgentTurn.Finish`, with `PreparedPrompt`; `RenderTurn.Render`; `EnvelopedFormat`; `MacroReply.Process`, with `ReplyFault` and `MacroReplyResult`; `MarkdownEscape`. |

### What each site reads

Each body's reads are held to part 05 § 3.1's row for its site. The table is `SiteReads`.

| Site | Reads |
|---|---|
| prompt | `node.*`, `input.*`, `check.*`, `item`, `pass`, `param` (in functions), `snippet.*`, `part.*` (its own node's), `macro.*`, the `renderer.` pipe, `run.*` |
| rendered node | the same, without `part.*` and `macro.*` |
| renderer | its piped `value` only |
| snippet body | what the template that places it reads |
| part text | what its node's prompt reads, `macro.*` and `snippet.*` included |
| macro instruction | what the placing prompt reads, `snippet.*` included, except `macro.*` and `part.*` |
| macro body | the prompt's reads, without `part.*` and `macro.*`, plus `profile.*` |
| deliverable | `node.*`, `input.*`, `snippet.*`, `check.*` (not its own), `profile.*`, `run.*`, the `renderer.` pipe |

The rules for one read:

- A path's head is resolved with `ScopeTree.Resolve` at the body's frame, and the rest of the path with
  `FormatReads.Read`. A resolver refusal (S04-039, S04-050, …) is used as it is given.
- A `for` variable is typed by its list. `for.first`, `for.last`, `for.even` and `for.odd` are booleans, and
  `for.index` is a number. A renderer's `value` is typed by its `input`. `run.<fact>` is one of `date`, `id`,
  `package` and `engine`. `profile.<field>` is one of part 06 § 10.1's five, else S06-028.
- `snippet.<id>.content` is text of the snippet's format. A folder item's `snippet.<folder>.<id>.content` is
  `markdown_block`, and its `.value` is typed by the folder's `format`.
- These are refused:
  - a root the site does not read: S05-020, or S05-021 for `profile.*` in a prompt, a part or a `rendered` node;
  - an unknown root or member: S05-011;
  - a bare `node.x`, `input.x`, `check.x` or `param.x`: A07-008;
  - a bare `snippet.x`: S05-037;
  - an object or a list written directly: A07-007;
  - a position in a list: S05-063.
- A local head is read as written, and `names` maps it to its rooted key (D1).

### Statements, built-ins and body `if`s

A body may hold outputs, `if` / `else if` / `else`, a `for` over a whole list, comments, raw blocks and `~`. These
are refused:

- every other statement or operator: S05-001, naming the keyword or operator as written (`=`, `+=`, `capture`,
  `include`, …);
- a built-in outside the seven (`string.upcase`, `string.downcase`, `string.capitalize`, `string.strip`,
  `array.join`, `array.size`, `date.to_string`), or a built-in named outside a pipe: S05-002;
- `truncate`: S05-003. `object.default`: S05-004. `date.to_string` without a literal pattern: S05-005;
- a `for` over anything but a list, a pipe or a call included: S05-006. `limit:`, `offset:`, `reversed`, a `for`'s
  `else`, `for.rindex`, `for.length` and `for.changed`: A07-004;
- nesting of `if` and `for` past 15 in one body: S05-013. `-` on any tag: S05-019. `renderer.x(y)`: S05-030;
- a body Scriban cannot parse: A07-005, never Scriban's message.

A body `if` (part 05 § 2.5) tests only these:

- a loop position: `for.index` only compared with a literal;
- a `.state` compared with `==` or `!=` to a state of the closed set (`completed`, `skipped`, `not_run`, else
  S05-010);
- whether a check passed: `check.<…>.value.pass`, bare or compared with `true` or `false`.

Any other test, a piped one included, is S05-007. A chain that branches on a state with no `else` handles every
state, else S05-009 for each path tested. A renderer's body may test its own `value` in any way, pipes included
(R2, as built).

### Placements and markers

A placement is a tag whose whole expression is `snippet.<…>.content`, `part.<id>` or `macro.<…>` (the forms of part
05 § 2.6):

- A block, `{{~ x }}`, stands alone on its line. An inline placement, `{{ x }}`, stands inside text.
- These are refused: `~}}`, S05-015; `-`, S05-016; a block inside a line, S05-017; text after a block, S05-018.
- A placement inside an `if` or a `for` is A07-009: a unit is placed at the top level of its template, and its own
  `if` decides it. A loop body reads a folder item as `item.content` instead.

A macro's marker is `{{~ macro.<id>.content }}` (a block) or `{{ macro.<id>.content }}` (inline). A folder's macro
is qualified: `macro.<folder>.<id>.content`. `MacroMarkers.Open(marker)` writes that form. Inside its own
instruction the marker is neither a placement nor a read. At run time it is written as that canonical form, and a
block marker keeps the line's indentation before it (D17).

### `placements` and `names`

An agent's, a `rendered` node's and a deliverable's entry in `resolve.json` (a function's included) gains two
derived members:

- **`placements`** (D2) lists every unit its body places, in order of appearance, with placed units expanded in
  place. Each is `{ site, within, if, plainConverts, form }`:
  - `site` is `part.<id>`, `snippet.<id>`, `snippet.<folder>.<id>`, `macro.<id>`, `macro.<folder>.<id>` or
    `macro-folder.<id>`;
  - `within` is the site whose text places it, or null for the body;
  - `if` is the unit's rule with its keys rooted for this entry's place, or null;
  - `plainConverts` is that rule's list, and `[]` with no rule;
  - `form` is `block` or `inline` for a macro, and null otherwise.

  A folder's macro is preceded once per entry by its `macro-folder.<id>`, which carries the folder's
  `index.json` `if`. The snippets a macro's body places come right after the macro's own entry, `within` it, then
  the snippets its instruction places. A snippet's, a macro's and a part's own `if` in `snippets`, `macros` and
  `parts` stay as written.
- **`names`** (D1) maps each `node.<id>` or `check.<id>` head written in the body, its parts and the snippet and
  macro texts it places to the rooted key it names there, only where the two differ
  (`{ "node.draft_summary": "node.refine.draft_summary" }`), and is `{}` otherwise.

The rest of what the seam says (`TemplateReads`):

- A template's `reads` are its own, then each unit's in the `placements` order, then a macro's body and the
  snippets it places. A read only a macro's body makes is `Substituted`: it is never sent, so S03-044 and S04-069
  for a prompt skip it, while the flags still spread along it.
- `places` lists the macros placed.
- `renders` and `runSpots` are numbered for the entry, in order of appearance across the units placed, whether a
  unit ends up placed at run time or not. A macro's body adds neither.
- `PlacesProfileMacro` is set when a macro placed, or a snippet in its body, reads `profile.*`.

As an entry of its own, a snippet, a macro or a renderer reads what its text reads as written.

The derived step also counts the `if` of each macro and snippet a body places as read by that body (S04-035). The
compiler embeds each built-in renderer an entry pipes into in the package's `renderers`, under `renderer.<id>`,
with the registry's header and body (D6). The features follow from what is placed (D15): `macro-markers` when an
agent places a macro, `parts` when an agent declares a non-empty `parts`, and `run-spots` when any entry has a run
spot. `warnings` gains the dialect's S05-060 and S05-061.

### Snippets, parts and macros at publish

- **Snippets** (part 05 § 6):
  - A snippet's body is checked in every place it is placed, at the placer's frame and against the placer's row. A
    read that is not available there is S05-022, naming the placer and the line.
  - A snippet is looked up in its function's own snippets first, then the package's. One no scope defines is
    S05-035.
  - A single snippet's `format` is a text format (A07-010). A folder item's header is held to the folder's
    `format` (S05-036).
  - A cycle is S05-023, said once. A chain deeper than 15 is S05-013, said of its last snippet.
  - A template places at most 1,000 units, counting what its parts and snippets place (A07-011).
  - A snippet placed in a part's text or a macro's instruction, directly or through another snippet, places no part
    or macro (A07-001).
- **A folder item's body** read as `item.content` in a loop over the folder (part 05 § 6.2) is checked where it is
  read, as a snippet's body at the reader's place (S05-022). It only presents: a placement, a pipe into a renderer,
  a run spot or a folder read of its own is A07-012. Its reads join the reader's reads and `names`. A body carried
  out of its loop as text, through a call's `with` or a computed node's `expression`, holds no tag and no raw block
  (A07-013, said at the carrier).
- **Parts** (§ 7): a part is on an agent only (S05-041 where the reader lets one through). Each `parts.<id>` is
  exactly `if` (a rule or null) and `text` (lines). A part member of the wrong kind or missing gets the reader's
  sentences. The text, its lines joined with `\n`, is a template at the node's frame. It has no `if` of its own and
  places no part (A07-002). The prompt places each declared part exactly once: S05-038 for an undeclared part,
  S05-039 for one never placed, S05-040 for one placed twice.
- **Macros** (§ 8):
  - A macro is placed in a prompt, in a part's text, or in a snippet the prompt places. It is looked up in the
    function's own macros first, then the package's. It is checked as its instruction and its body at the placer's
    frame.
  - The instruction holds its own marker exactly once (S05-044), no other macro's marker (S05-045), and no other
    `macro.*` or `part.*` (A07-003).
  - The body places no macro (S05-042) and no part (S05-043), and holds no marker (S05-051). A `plaintext` macro's
    literal text writes no Markdown (S05-050).
  - The placing node:
    - places a macro once, counting its parts' text (S05-048);
    - returns a text format (S05-049), which takes the macro's format (S05-046);
    - is an agent: in a `rendered` node or a deliverable a macro is S05-047.
  - A prompt or a part that reads a node which carries profile text through a macro is S05-064.

### Run spots

At publish (part 05 § 4, D4) a run spot is an output that reads either of these:

- `run.<fact>`;
- a node, check or parameter declared `runDependent` whose value is not generated by an agent.

The output reads nothing else: S05-024 with a run fact, S05-025 with a run-dependent value. A `for` over either is
S05-006, and an `if` on either is S05-007.

Spots are numbered per entry from 1, in order of appearance with the placed snippets, parts and macro instructions
expanded in place. Each `runSpots` entry is `{ site, at, reads, unit, neutral }`:

- `unit` is the site whose text holds the spot, or null for the body;
- `at` is the line and column in that text;
- `neutral` is `marker` for a raw `run.*` fact, a run-dependent `computed` value or a `for`'s value, and `text` for
  a run-dependent `rendered` node or a flow node or call whose `ret` is one.

Each spot warns S05-060. A macro body's `run.*` reads are filled directly (D5): they are no spot, but they still
make the node run-dependent.

At run time each spot's output is recorded where it is written. The text is never searched. The neutral text comes
from the same render:

- a `marker` spot's output is replaced by `{{run:<n>}}`;
- a `text` spot's statement is evaluated again with the read node's run-neutral text, its read form's `neutral`.
  This runs the spot's whole pipeline, so `| string.upcase` gives `{{RUN:1}}`.

`Composition.NeutralText` is null when no spot was filled. A `ComposedSpot` gives:

- its site and its reads;
- `Start` and `Length` in the sent text, in UTF-16 code units;
- the output's hash, and the output itself only for a raw `run.*` read;
- the value's hash.

### Renderers

At publish (part 05 § 5):

- A renderer's `format` is a text format (S05-028). It reads only its `value` (S05-029) and pipes into no renderer
  (S05-033).
- A pipe step into a renderer is typed against the renderer's `input` with `FormatTypes.Same`: S05-026, or S05-034
  when the step before it is a renderer.
- The renderer is looked up as the function's own, then the package's, then a built-in. One none of them has is
  S05-031.
- A `markdown_block` renderer inside a line warns S05-061.

At run time a renderer's body renders with only `value` (the piped value) and the built-ins. Each step gives one
`ComposedRender`: its number, the renderer, its `bodyHash`, the source (a path, or `render:<n>` in a chain), the
input's hash and size, the output's hash and length in characters, and whether it is in a run spot. A step in a
`for` gives one sub-row per iteration with the same number. A macro body's steps are numbered after the entry's own.

### The composer and its limits

```csharp
public static Composition Compose(RunPlan plan, ComposeRequest request)
```

`ComposeRequest(EntryKey, Function, Scope, Placed, StepLimit)`: the entry (local to its function inside one), the
data it reads (rooted keys, as the run holds them), each decided site of the entry's `placements` (a site absent
from it is not placed), and the step budget. `Composition(Text, NeutralText, Failure, TooManySteps, Spots, Renders,
Macros, Units)` is what it gave. `Text` is null exactly when `Failure` (the node's sentence) or `TooManySteps`
(S05-062, which the runner says) is set.

The render:

- **The context.** Scriban 7.2.5 in the invariant culture, with strict variables, only the seven built-ins, and no
  re-indentation of a value's or a unit's later lines.
- **Names.** A local head reads its rooted key through `names`.
- **Units.**
  - A placed snippet, part or macro instruction renders its own text in the same data, spliced where it stands.
  - A unit not placed renders nothing, so the tag's `~` leaves § 2.6's empty line.
  - A snippet is the function's own before the package's.
  - In a loop over a snippet folder, `item.content` renders the item's body from the plan, in the loop's scope.
- **Values** (D13):
  - `null` writes nothing;
  - a boolean writes `true` or `false`, a number its invariant form, and text as it is (a `date` is ISO text);
  - an object or a list written directly fails the node with A06-008;
  - a built-in given `null` gives `null`, and `array.join` over a `null` element is A07-006.
- **Limits** (D12), counted across one composition, renderers and units included:
  - a `for` past `BodyComposer.IterationLimit` (1,000) iterations, each time it runs: S05-012, at the `for`'s line in
    its own text;
  - each iteration of any `for` is a step, held against `StepLimit`: `TooManySteps`;
  - output past `BodyComposer.OutputLimit` (1 MiB of UTF-8): S05-014.
- **A document publish did not write:** S07-006. That is an unknown entry or function, an entry with no body, a
  unit the plan lacks, a text that does not parse or breaks a statement rule (so `include` or an assignment never
  runs, even after an edit), a folder item that cannot be matched, or units nested past 60 deep.
- **Anything else** that fails is A06-008, never Scriban's message.

`Units` lists each snippet and part placed, by the hash of its text. `Macros` lists the macros offered, in prompt
order.

### Placement decisions

The runner decides placements (part 05 § 9, D3). It never parses a template. `RunPlan.Build` compiles each entry's
`placements` into `PlanEntry.Placements` (`PlanPlacement(Site, Within, If, Printed, Form)`), each rule with its
`plainConverts`. A rule's `var`s join the entry's `Reads`, so a classifier a macro's `if` reads runs first. A
malformed placement is S07-006.

When an agent, `rendered` or deliverable instance's gate holds, `PlanRunner` decides its placements in order:

- A placement is decided only when its `within` is placed. For a folder's macro, its `macro-folder.<f>` must hold
  too.
- Each site is decided once.
- A placement with no rule is placed, and no decision is recorded.
- Each rule reads the instance's own frame. Its decision is a `RunDecision` whose `Site` is the row's key and whose
  `Member` is the placement's site.
- A rule that fails fails the instance with part 03 § 11.3's sentence for the unit's `if` (`Macro 'm' if could not
  be decided: …`).

The results go to the host as `AgentCall.Placed` and `RenderCall.Placed`. A macro is offered when its own, its
folder's and its placing units' decisions all hold.

### A turn: the prompt, the reply, the envelope

`AgentTurn.Prepare(plan, call, stepLimit, strictRejects)` gives a `PreparedPrompt`:

- It composes the body.
- For a `via: "custom"` binding it ends the text with `WorkflowCustomPrompt.Compose(text, AgentTurn.CustomTrailer,
  <the canonical JSON of the format sent>)`. A `via: "format"` agent carries its own format, so none is sent.
- For a retry it appends D11's text after two newlines. The engine guide says what each retry sends.
- `Text` is null when the prompt cannot be prepared, and `ErrorKind` says why: `plan` (no agent entry or binding,
  S07-006), `steps`, or `compose`.

**The envelope** (D8, part 02 § 3.5). `EnvelopedFormat.Of(format, formats, strictRejects)` is `SentFormat.Prepare`'s
copy, with the attested agent's `strictRejects` removed (`NotSent`, as pointers). A copy whose root is not an object
is wrapped as `{ "type": "object", "required": ["value"], "additionalProperties": false, "properties": { "value":
<copy> } }`. `Hash` (`sentFormatHash`) is the hash of the canonical JSON sent, envelope included. `Unwrap(reply)`
gives the `value` member's JSON text, or null when the reply is not exactly that object. A reply that is not the
envelope is passed on, and its format refuses it.

**The reply** (part 05 § 8.6). `AgentTurn.Finish(prepared, reply)` unwraps the reply. For a text format it then calls
`MacroReply.Process(reply, offered, packageMacros, ordered)`:

- **The scan.** It scans the reply once, for the two exact shapes `{{ macro.<ids>.content }}` and
  `{{~ macro.<ids>.content }}`: one space inside each brace pair, and one id or a folder's two, each by the id
  pattern and at most 64 characters. Anything else, a near-miss included, is the reply's own text.
- **The faults,** in this order:
  - for each occurrence naming no offered macro: S05-055 for a macro of the package, S05-056 for no macro;
  - for each offered macro, in prompt order: S05-053 (none), S05-054 (more than one), S05-057 (the other form, a
    block with text before it, or the inline form where a block was asked), S05-058 (text after a block);
  - with `ordered`, S05-059 for the first pair out of order.

  Each `ReplyFault` carries part 08's short reason (`macro <id>: missing`, …).
- **The replacement.** With no fault, every marker is replaced in one pass into a new string, so substituted text is
  never scanned again. A block marker's line loses its indentation and the spaces after the marker, and keeps its
  newline. An inline marker is replaced where it stands.
- **The body substituted.** It is the macro's body rendered in the prompt's data (`profile.*` readable, `run.*`
  filled directly), after dropping exactly one file-final line ending. A `plaintext` macro in a `markdown_block` or
  `markdown_inline` node is escaped first with `MarkdownEscape.Escape` (D14, D7: a backslash before each of the 32
  ASCII punctuation characters, nothing else; version `1.0.0`). `contentHash` is of the text substituted. A snippet
  a macro's body places is rendered when its decision holds.

With faults, `Finish` returns the reply as it came, with `AgentOutcome.Invalid` set. The runner never reads that
reply. It applies `onInvalid` as for a reply that does not fit its format: under `retry` it asks once more with
`AgentCall.Faults`; otherwise, or on a second invalid reply, the node fails with `Node '<id>': <the first fault's
sentence>`. The row's `invalid.reason` is the faults' reasons joined by `; `. The substituted text is then read and
validated against the node's format, and `failIfEmpty` is judged after.

`RenderTurn.Render(plan, call)` is the same composition for a `rendered` node or a deliverable. It gives the text,
`RenderOutcome.NeutralText` and the detail. The runner stores the neutral text as the read form's `neutral` (carried
through an `if`, a `case`, a `while`'s last pass and a call's argument), and a reader's scope gets it through
`PlanEntry.NeutralReads`.

### The row's detail

An agent row's `Detail` takes the last attempt's members (part 08 § 4), then `invalid` and `retries`:

- `inputHash`, of the sent text;
- `hashVersion`, 7;
- `runNeutralHash`, null with no spot;
- `runSpots`, each `{ id: <key>/spot:<n>, site, reads, valueHash, output, outputHash }`;
- `renders`, each `{ id: <key>/render:<n>, renderer, templateHash, source, inputHash, inputSize, outputHash,
  outputLength, runDependent }`, a chain's `source` being the step before's id;
- `macros`, one per macro of `placements`, offered or not: `{ id: <key>/macro:<id>, macro, offered, decisions,
  classifier, placed, form, contentHash }`. Its `decisions` are the ids of the non-null rules its offer depended
  on, and its `classifier` is D10's;
- `profileReads`, each `{ field, hash }`;
- `sentFormatHash` and `notSent`;
- `units`, each `{ site, hash }`.

A `rendered` node's or a deliverable's row takes `inputHash`, `runNeutralHash`, `runSpots`, `renders` and `units`.
No member carries a value, a reply or a profile field: only ids, keys, hashes and lengths.

### Sentences

The dialect says part 05's rows S05-001 to S05-064, with these exceptions:

- S05-008 and S05-027 are the worked examples of S05-009 and S05-026;
- S05-032 is the reader's S01-035;
- S05-052 is said by no code of this area.

It also says S02-019, S04-063 and S06-028 where only a template reaches them. A rule with no row has an area
sentence, in `V31Contract` and the conformance README:

| Id | Sentence |
|---|---|
| A07-001 | `Snippet '<id>' places <part.* or macro.*>, and <subject> places it in <a part's text \| a macro's instruction>; a snippet placed there doesn't place parts or macros.` |
| A07-002 | `Part '<name>' of node '<id>' uses <if or part.*>; a part's text only presents.` |
| A07-003 | `Macro '<id>' places <macro.* or part.*> in its instruction; combine instructions by placing each macro in the prompt.` |
| A07-004 | `<Subject> uses <form> at line <n>; a for runs over a whole list in order and reads only for.first, for.last, for.index, for.even and for.odd.` |
| A07-005 | `<Subject> can't be read as a template at line <n>: close every tag and every if and for.` |
| A07-006 | `<Subject> stopped rendering: array.join met a list with an empty element.` (run time) |
| A07-007 | `<Subject> writes <path> at line <n>, which is <an object \| a list>; read a member, loop over it or pipe it into a renderer.` |
| A07-008 | `<Subject> reads <path> at line <n>; read <.value \| .content \| .title> of it.` |
| A07-009 | `<Subject> places <unit> at line <n> inside <an if \| a for>; a part, a snippet or a macro is placed at the top level of its template, and its own if decides it.` |
| A07-010 | `Snippet '<id>' declares format <format>; a snippet's text is plaintext, markdown_block or markdown_inline.` |
| A07-011 | `<Subject> places more than <limit> parts, snippets and macros, counting what its parts and snippets place; place fewer.` |
| A07-012 | `Snippet '<id>' <places <unit> \| pipes into <renderer> \| reads <path>>, and <subject> reads it as <path> at line <n>; a folder item's body read in a loop places no part, macro or snippet, pipes into no renderer and reads no run.* or run-dependent value.` |
| A07-013 | `Snippet '<id>' holds a tag at line <n>, and <subject> <passes it to function '<f>' as param.<p> \| reads it in its expression>, where it isn't rendered; keep a folder item's body plain text, or read it as item.content in its loop.` |
| A07-014 | `Nodes '<a>' and '<b>' bind agent '<name>' at versions <m> and <n>; a package binds one version of an agent.` (the type rules; `requires.agents` is one integer version per agent) |
| A07-015 | `MacroChoices names '<id>', which is not a boolean input of the package.` (job start; see the engine guide) |
| A07-016 | `Input '<id>' is given one value in Inputs and another in MacroChoices; send it once.` (job start) |

The run-time sentences are S05-012, S05-014, S05-053 to S05-059, S05-062 and A07-006. They are never said at
publish, and S05-056 names the id the model wrote, bounded to the id pattern. Area 07 also applied the owner's later
rulings on #1504:

- `requires.agents` holds integer versions;
- a null `title` compiles to the package's name;
- this pass's node is an edge (see "the derived values and the seam");
- `MacroChoices` is read at a 31 job start, which replaces plan decision D16.

### What it leaves to areas 08 to 12

- **Area 08:** a deliverable's strict formats, its escaped reads, its `templateHash`, `sites` and `substitutions`,
  gating and delivery. This area checks a deliverable's body as a dialect template and composes it.
- **Area 09:** storing rows, sub-rows and decisions. This area puts them in `RunRow.Detail` and `RunDecision` under
  part 08's names. The engine's `EntityRunRecorder` passes only the hashes on to telemetry.
- **Area 10:** converting a 30 package (part 05 § 10).
- **Area 11:** in-package test stubs, and the publish path that stores a pair.
- **Area 12:** the Create page's three-state boolean for a per-run choice, and History's sub-rows.
- **For the owner:** the 31 agents are attested against git, not Foundry, until the contracts are released (D9).

### The conformance cases

Each family is exported by a test that does nothing without `CONFORMANCE_EXPORT_V31=<content repo root>`:

- **`refusal-packages`** gains a case for each publish sentence of the dialect that a source reaches
  (`tests/V31Source/RefusalPackages.Templates.cs`). `RefusalPackagesTests.TemplatesNotShipped` lists the ids that
  have no case, and why.
- **`compiled-documents`** is now compiled from the dialect's reads, not stubs.
- **`dialect-goldens`** (40 cases), **`renderer-goldens`** (26) and **`markdown-escape-vectors`** (42 vectors) are
  new (`tests/V31Templates/DialectGoldensExport.cs`):
  - a dialect case is composed by `BodyComposer` over a published plan, and a macro case is also finished by
    `AgentTurn`;
  - a renderer case is a built-in renderer over the concept cases, piped in a published prompt;
  - the escape vectors are `MarkdownEscape.Escape` over the 32 ASCII punctuation characters and ten sentences.

  The same bytes are the in-repo goldens under `tests/V31Templates/Golden/`. `DialectGoldenTests` holds them to the
  code and the pinned families to them, and `GOLDEN_UPDATE_V31=1` rewrites them.
- **`tests/V31Run/Golden/macros.run.txt`** pins a whole run with a classifier, a folder macro placed by a part, and a
  run spot, each row's detail included.

The content repository's `v31/conformance/README.md` describes the families ("The dialect (area 07)", "The
dialect's goldens (area 07)"), and its `scripts/check-v31.py` checks them.

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport|FullyQualifiedName~CompiledDocumentsExport|FullyQualifiedName~DialectGoldensExport"
```

### Not wired in

31 stays outside `AcceptedSpecVersions`. The engine runs templates only where
`WorkflowPackages__PreviewSpecVersions` lists 31: its two activities call `AgentTurn` and `RenderTurn` (see
[the engine at specVersion 31](engine-v31.md)).
