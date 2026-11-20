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
`reproducible`, `modelDerived`, `runDependent`, `profileCarrying`, call depth (05); prompt
members and macros (07); checks, deliverables and the profile (08); names and versions (11).
The reader types the members that carry these and holds their values; it does not compare them.

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
  they follow from what a node reads, so areas 04 and 05.
- **Finding the reads** (D6): JSON Logic `var`, templates and reference strings are areas 04, 05
  and 07's. `FormatReads` types a read when asked, so no package reaches S02-018, S02-019 or
  S02-020 yet.
- **A check's format** (D15): area 08's (S06-009). A rendered check's inline format is still
  A02-001.
- **The envelope** (D22): `SentFormat` stops before it. The envelope, and so the exact bytes of
  `sentFormatHash` if it is part of them, belongs to the area that sends (06 or 07).
- **`uses.formats` listing a built-in or catalogue name** (D36): part 04 § 7.3's rule, area 05's.
- **The other kinds' formats** (part 02 § 2.2): a renderer's, a deliverable's and a macro's (07,
  08), a `computed` node's (04), a `call`'s (05).

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
  at most 100 (`Check` gives an `IRegexpCheck`: the `IRegexpFault`, a fixed-phrase `Reason`, a
  `Count`, and the .NET `Regex` when it is sound), and translated to .NET (`ToDotNet`), compiled
  `NonBacktracking` with no time-out. `IsMatch` searches; `TooLong` says a text is over 1 MiB.
- **`JsonLogicShape`**: the written form with no types. `Check` gives every `ShapeFault`
  (`ShapeFaultKind`: an object that is not a rule, a loose `==`, an unknown operation, a wrong
  operand count, a bad `var` default, a pattern that is not I-Regexp, and so on). `Paths` lists the
  paths a rule reads (`var` and `missing`), `IsPath` and `IsPosition` judge one. `MaxDepth` is 64.
- **`JsonLogicPrinter`**: a rule as people see it. `Print` is total. `TryParse` reads back only text
  `Print` itself writes: it parses, prints again and compares, and gives `false` for anything else.
  `Reason` words a decision's reason for the record. `Version` is 1.0.0.
- **`PlainConversion.ToPlain(markdown)`**: the conversion `plain` applies (Markdig's plain text with
  the advanced extensions, `\n` line breaks, no spaces, tabs or line breaks at the very end).
  `Version` is 1.0.0.
- **`JsonLogicFunctions`**: the meaning of each operation on values (`Apply`), truthiness (`Truthy`),
  reading a path (`Read`), `Missing`. A `JsonLogicFailure(Kind, Detail)` is a run-time failure
  (`JsonLogicFailureKind`: `Declined`, `MatchesTextTooLong`, `NotABoolean`, `Fault`).
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
a read through it has no type and fits any operand), or `Unknown` (nothing there: S03-014). The root
and site rules of part 03 (S03-013, S03-015, S03-018 to S03-022) are judged before a scope is asked.

`PackageScope` is the scope of this area: `input.<id>` and `run.<fact>` are typed; every other root
(`node`, `check`, `item`, `pass`, `param`), and every root inside a function, is `Deferred`. Area 05
supplies the real scope, through `IExpressionScopes` (`SourceExpressions.Check(package, types,
scopes)`), and until then a read through those roots is not judged. A read of `run.<fact>` at a site
gives S03-037 (a condition) or S03-038 (a computed node) as a warning, in
`PackageExpressions.Warnings`; a function's site that reads `run.*` warns the same way.

### Where a rule is written

`SourceExpressions.Check` visits, in the order `SourceScopes` does, the `if` of a production leaf, of
an `if` flow node, of a check, of a deliverable, of a part, of a macro and of a snippet; the `while`
and `until` of a flow node; and every `computed` node's `expression` (always checked). A condition
site whose member is null or absent is skipped; at an `expression` a `null` rule is the `null`
literal, held to the node's declared format. Each site is checked against its own function's
`FormatSet` when it is in one. A `SiteExpression` holds the site, its rule (null for the `null`
literal) and its `ExpressionCheck`. Refusals are said once and ordered by path.

### The flags

`LineageFlags.Derive(nodes)` works out `modelDerived`, `runDependent` and `profileCarrying` for a
graph of `LineageNode`s the caller supplies, in rounds against a snapshot of the one before; a cycle
ends. A flag's cause (`LineageValue.Because`) is the read that made it true first. `OfReads` gives a
decision's flags, and `Disagreement` gives S03-036 when a node's declared flag is not what its
reads make it. No caller gives it a graph yet: the compiled package's edges are area 05's and what
templates read is area 07's (D28), so no package reaches S03-036 or S03-044.

### Sentences

The sentences are `ExpressionSentences`' (internal), one method each. A sentence with a row in
`v31/sentences.json` is that row, character for character; the rules with no row are `A04-001` to
`A04-011`, listed in the conformance README. Where part 03 and part 02 say one rule, the sentence is
the part 03 row at a rule's site (D3). A run-time failure (S03-040, S03-041, A04-011) is never said
at publish.

### What it leaves to later areas

- **The scope of `node`, `check`, `item`, `pass` and `param`, and the rules of what a site may see**
  (D2): area 05's. Until then those reads are `Deferred`.
- **The edges the flags spread along, and the header check** (D28): area 05's, and the reads of
  templates, area 07's.
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
