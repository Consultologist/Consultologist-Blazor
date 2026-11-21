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
"the resolver and the lock" below); prompt members and macros (07); checks, deliverables and the
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
  found and typed; templates' reads are area 07's. A bare `input.<id>` as a loop's source, a
  `case` subject or a call's argument is S02-018, which now has a case. No package reaches S02-019
  or S02-020 yet.
- **A check's format** (D15): area 08's (S06-009). A rendered check's inline format is still
  A02-001.
- **The envelope** (D22): `SentFormat` stops before it. The envelope, and so the exact bytes of
  `sentFormatHash` if it is part of them, belongs to the area that sends (06 or 07).
- **`uses.formats` listing a built-in or catalogue name** (D36): part 04 § 7.3's rule. Area 05
  allows it, and warns with S04-074, as for any other name, when the function never names it
  (05-D12).
- **The other kinds' formats** (part 02 § 2.2): a renderer's, a deliverable's and a macro's (07,
  08), a `computed` node's (04), a `call`'s (05: S04-070, the call's against its function's).

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
`if`, which is read where it is placed (area 07's). A read of `run.<fact>` at a site gives S03-037
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
now said, and each has a case. What templates read is area 07's (D28): until a provider is given,
a flag that depends on a body is not judged.

### Sentences

The sentences are `ExpressionSentences`' (internal), one method each. A sentence with a row in
`v31/sentences.json` is that row, character for character; the rules with no row are `A04-001` to
`A04-011`, listed in the conformance README. Where part 03 and part 02 say one rule, the sentence is
the part 03 row at a rule's site (D3). A run-time failure (S03-040, S03-041, A04-011) is never said
at publish.

### What it leaves to later areas

- **The scope of `node`, `check`, `item`, `pass` and `param`, and the rules of what a site may see**
  (D2): built by area 05 (`ResolvedScopes`). Only a macro's and a snippet's `if` stay `Deferred`.
- **The edges the flags spread along, and the header check** (D28): built by area 05
  (`SourceDerived`). The reads of templates are area 07's.
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
  through which area 07 says what a Scriban body reads.
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
  sites stay in `PackageScope`, deferred; with the seam, the derived step judges them where each
  body places them (below).

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
   own `.items` is one; a while reading its own passes is not.
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
- `RunSpots`: one `TemplateRunSpot(Site, At, Reads)` per run spot.
- `PlacesProfileMacro`.

An empty or white-space body is known and reads nothing (`TemplateReads.None`).

**Without the seam (this area's state), the derived values are incomplete:**

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
(see the plan's contract corrections). `title` is the manifest's, or `<name>@<version>` when that is
null. There is no time anywhere: the same source and toolchain give the same bytes, whatever the
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
  `features` follow `constants.json`'s order. `agents` is each bound agent, name to version as text.
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
  - S07-003 for another locked library (Markdig, the two Corvus libraries: any break point that is
    not Scriban's, the evaluator's or an operation's) only when all of these hold:
    - the engine has a break point for it;
    - the locked version is before that break point (a missing locked version counts as before);
    - the engine's own version of it (`EngineCapabilities.Libraries`) is at or past the break
      point, or is missing or not a version.

    A missing or invalid version is said as `unknown`. With no break point for a library, nothing is
    said of it.
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

- S04-063: it needs the seam.
- S04-072, S04-073, S04-079, S04-080 and S04-074: notes and warnings.
- Part 07 § 10's: job start, load, import, CI.

### What it leaves to later areas

- **Area 07, the seam.** An `ITemplateReads` built on the dialect, passed to `SourceCheck.Run` and
  `CompiledPackage.Publish`. Until then, everything in "Without the seam" above holds. The goldens
  are compiled with stub reads (`ResolveFixture.*Reads()`), so area 07 regenerates them and reads the
  diff. Also area 07's:
  - a renderer's, a macro's and a deliverable's formats;
  - where a macro or a snippet is placed;
  - the envelope.
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
  compiled with a fixed toolchain and stub reads. `ThePinnedFamily_IsWhatPublishGives` holds the pin
  to what `Publish` gives.

  The same bytes are the goldens in `tests/V31Resolve/Golden/`: canonical, one line, no final
  newline. `GOLDEN_UPDATE_V31=1` rewrites them. The content repository's `scripts/check-v31.py` has
  a `compiled-documents` group. It validates each pair against `resolve.schema.json` and
  `lock.schema.json`, and recomputes `lock.resolve.hash` from the resolve's bytes.

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport|FullyQualifiedName~CompiledDocumentsExport"
```

### Not wired in

Nothing in the engine calls any of it yet. No publish path compiles a 31 package, and no engine
loads one. 31 stays outside `AcceptedSpecVersions`.
