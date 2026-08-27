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

## With the engine

`WorkflowPackage` and `WorkflowResolvedResult` (the *resolved* package),
the wire responses and publish request, `WorkflowPackageBlocks` (block
expansion over the resolved package), `WorkflowPackageNaming` (account
prefixes and access — #447's territory), `ConceptOutputContract`, the
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
