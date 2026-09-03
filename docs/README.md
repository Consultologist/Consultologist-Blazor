# Documentation

Design records and research notes for Consultologist. Grouped by topic;
`research/` holds point-in-time investigations and migration plans.

Everything in this repository is **public by design** — the engine, its
design records, and these notes. What is not here is the operator's material
(the private storage account's recipes, roles, repair), which lives in a
private operations repository and is named, never linked, where a public page
would otherwise have carried it (#397).

## Consult generation & jobs

- [CONSULT_GENERATION_WORKFLOW.md](CONSULT_GENERATION_WORKFLOW.md) — end-to-end consult generation workflow
- [CONSULT_GENERATION_EVENTS.md](CONSULT_GENERATION_EVENTS.md) — consult generation event flow
- [DURABLE_JOBS.md](DURABLE_JOBS.md) — durable jobs, server-sent events, and .NET version notes
- [ASYNC_DELIVERY.md](ASYNC_DELIVERY.md) — design record for the async-delivery arc: scheduled runs, email intake, encrypted document delivery (#157–#159; implemented 2026-07-25)
- [DOCUMENT_INPUT.md](DOCUMENT_INPUT.md) — design record for reading documents into declared input slots: one parser, two sources (#234)
- [PARALLEL_AGENTS.md](PARALLEL_AGENTS.md) — parallel section calls, backend parallelism, async jobs
- [NEW_AGENTS.md](NEW_AGENTS.md) — new Azure AI Foundry agents
- [SNOMED_TOOL_FAILURES.md](SNOMED_TOOL_FAILURES.md) — diagnosis and fix for `search_concepts` tool failures (250-character Snowstorm term limit; resolved 2026-07-09)

## Server-sent events (SSE)

- [SSE_CLIENT_DESIGN_NOTES.md](SSE_CLIENT_DESIGN_NOTES.md) — SSE client design notes
- [SSE_RESUME.md](SSE_RESUME.md) — SSE resume plan
- [WRAPPER.md](WRAPPER.md) — SSE result wrapper notes

## Auth & accounts

- [ACCOUNTS.md](ACCOUNTS.md) — accounts, Entra API access, and LinkedIn login
- [SATELLITE_CALLERS.md](SATELLITE_CALLERS.md) — how a separate app calls this API as the signed-in clinician, and how external identities bind to accounts (design record, #611)

## Storage & infrastructure

- [CONFIGURATION.md](CONFIGURATION.md) — environment variable / app setting reference for the Api and frontend config keys
- [STORAGE.md](STORAGE.md) — Durable Functions storage setup
- [storage separation](customizable-workflow/storage-separation.md) — the classes of
  state (inputs, outputs, record, usage), their stores and accounts, retention,
  residency, and what a purge leaves behind (#545)
- [NETWORK_HARDENING.md](NETWORK_HARDENING.md) — network hardening notes

## Customizable workflow (design notes)

- [customizable-workflow/](customizable-workflow/README.md) — versioned workflow
  packages, specialty bundles, and the per-consult provenance record — the design
  records behind package formats v2–v10, all shipped, and v11, designed; the normative specifications
  and the record contract are published registries (#376, #400): [current state](customizable-workflow/current-state.md),
  [workflow packages](customizable-workflow/workflow-packages.md),
  [provenance](customizable-workflow/provenance.md),
  [DAG improvements](customizable-workflow/dag-improvements.md),
  [product stages](customizable-workflow/product-stages.md),
  [completeness](customizable-workflow/completeness.md),
  [content repos](customizable-workflow/content-repos.md),
  [package format v2](customizable-workflow/package-format-v2.md),
  [package format v3](customizable-workflow/package-format-v3.md),
  [package format v4](customizable-workflow/package-format-v4.md),
  [package format v5](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v5.md),
  [registry operations](customizable-workflow/registry-operations.md),
  [decoupling roadmap](customizable-workflow/decoupling-roadmap.md),
  [DAG-as-data design](customizable-workflow/dag-as-data-design.md),
  [output-contract catalog](customizable-workflow/output-contract-catalog.md),
  [in-app editing](customizable-workflow/in-app-editing.md),
  [package format v5 design](customizable-workflow/package-format-v5-design.md),
  [package format v6](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v6.md),
  [package format v6 design](customizable-workflow/package-format-v6-design.md),
  [package format v7](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v7.md),
  [package format v7 design](customizable-workflow/package-format-v7-design.md),
  [package format v8](https://github.com/Consultologist/consultologist-package-format/blob/main/package-format-v8.md),
  [package format v8 design](customizable-workflow/package-format-v8-design.md)
  (typed inputs and conditional deliverables; also carries the candidates
  considered and not taken, and why the trigger was overridden rather than
  met),
  [package format v9 design](customizable-workflow/package-format-v9-design.md),
  [package format v10 design](customizable-workflow/package-format-v10-design.md)
  (the classifying node, the condition evaluator, unbounded nesting),
  [package format v11 design](customizable-workflow/package-format-v11-design.md)
  (macros as templates with namespaced placeholders, the signature flag,
  reproducible stages),
  [Forms intake spike](customizable-workflow/forms-intake-spike.md)
  (#511: what the documentation and the engine establish about a Microsoft
  Forms door, the staged design, and the operator-run experiments),
  [Forms intake recipe](customizable-workflow/forms-intake.md)
  (#542: the clinician's once-per-form wiring — the flow, the connection,
  licensing and the email bridge),
  [storage separation](customizable-workflow/storage-separation.md)
  (#545: four classes of state, two private accounts per region, the
  migration order)

## Research

- [research/Findings-AI-Endpoint.md](research/Findings-AI-Endpoint.md) — Azure AI Foundry API endpoint investigation
- [research/Migration-Azure-Foundry.md](research/Migration-Azure-Foundry.md) — Azure AI Foundry integration authentication options
- [research/Migration-Azure-Funciton.md](research/Migration-Azure-Funciton.md) — fixing an Azure Static Web Apps deployment failure
- [research/Migration-dotNET-10.md](research/Migration-dotNET-10.md) — .NET 8 → .NET 10 LTS migration plan
- [research/Migration-Plan.md](research/Migration-Plan.md) — Bootstrap 5.1.0 → Microsoft Fluent UI Blazor migration
- [research/Folder-Structure.md](research/Folder-Structure.md) — Blazor folder structure patterns research
