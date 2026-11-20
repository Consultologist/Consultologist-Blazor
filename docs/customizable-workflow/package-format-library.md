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
`SourcePackage` or a list of refusals, never both, and `Normalised`, the files whose `\r\n`
intake turned into `\n` (the publish report notes them). It throws only for a null argument;
nothing an upload holds throws.

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

### The model

| `SourcePackage` holds | |
|---|---|
| `Manifest` | name, version, `derivedFrom`, title, description, tags, inputs; the whole file as `Json` |
| `Nodes`, `Checks` | bodies of `SourceNode`s in listed order: `SourceLeaf`, `SourceFor`, `SourceWhile`, `SourceIf`, `SourceCase`, each with its rooted key |
| `Functions` | `SourceFunction`: index, parameters, body and its own definitions |
| `Definitions` | renderers, snippets, macros, formats |
| `Deliverables`, `Tests` | deliverables (one level of folders) and tests |
| `Readmes` | every `README.md`, path to text |

Kept as raw JSON for later areas: every header (`JsonObject`), a manifest input, a format's
schema, a test, and an expression (`until`, `while`, `if`) as a `JsonNode`. Bodies are kept
byte for byte after line-ending normalisation.

### Refusals

A `SourceRefusal(Id, Sentence, Path)` carries the id of a row of the format registry's
`v31/sentences.json`, the sentence, and the file it is about (`""` for the package as a whole).
The `A02-NNN` sentences exist for rules the design gives no sentence of its own; the list is in
the conformance README,
`external/consultologist-package-format/v31/conformance/README.md`. One fault gives one
sentence, said once. Refusals are ordered by path (ordinal), then in the order found.

### What it leaves to later areas

Types and formats (03); JSON Logic (04); the resolver and the derived values — `reproducible`,
`modelDerived`, `runDependent`, `profileCarrying`, a flow node's format, call depth (05); prompt
members and macros (07); checks, deliverables and the profile (08); names and versions (11).
The reader types the members that carry these and holds their values; it does not compare them.

### The conformance cases

`tests/V31Source/RefusalPackages*.cs` generates the `refusal-packages` family: for each rule, a
valid and an invalid package with the parser's own result as `expect.json`. They are pinned
under `external/consultologist-package-format/v31/conformance/refusal-packages/`. After a
sentence changes, re-export into the content repository's checkout and commit it there:

```
CONFORMANCE_EXPORT_V31=<content repo root> dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport"
```

The export deletes and rewrites the family and the `cases` of `v31/conformance/index.json`;
without the variable the test does nothing.

### Not wired in

Nothing in the engine calls the reader yet. 31 stays outside `AcceptedSpecVersions`.
