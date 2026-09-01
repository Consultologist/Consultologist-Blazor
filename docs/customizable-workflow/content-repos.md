# Content Repos: Workflows and Agents as GitOps-Published Artifacts

Design recorded 2026-07-10; **implemented 2026-07-23 (#16)**. Both repos exist
and publish through CI OIDC: [consultologist-workflows](https://github.com/Consultologist/consultologist-workflows)
(tag `{name}-vYYYY.MM.N` → registry) and
[consultologist-agents](https://github.com/Consultologist/consultologist-agents)
(merge → Foundry version + redacted registry mirror + catalog). Implementation
notes vs the original text: the agent mirror layout is
`agent-definitions/{name}/{version}/definition.yaml` (redacted); the agents repo
also carries `concept-extraction.yaml` and the separately-versioned
`output-contracts.json` catalog; in production the attestation baseline is the
registry manifest; the local-dev fallback is the canonical repo pinned as a git
submodule at `external/consultologist-agents` (bump the submodule to advance
the local baseline — no silent drift), and the catalog pin
(`OutputContracts__Pin`) is set explicitly. Goal achieved: the app repo is
**engine-only** — content cadence fully decouples from app deploys.

## Topology: two repos, then five

> The layout every registry obeys — paths, CalVer, immutability by refusal,
> `latest.json` as a pointer, anonymous read including listing — is published
> as `registry-layout.md` in
> [consultologist-provenance](https://github.com/Consultologist/consultologist-provenance)
> (#401). The table below is the map; that document is the contract.

| Repo | Contents | Versioning / tags | Publishes | Licence |
|---|---|---|---|---|
| `consultologist-workflows` | Workflow package sources (`packages/general/`, `packages/example-two-documents/`) | CalVer `vYYYY.MM.N` per package | `workflow-packages` blob container (`{name}/{version}/…` + `latest.json` pointers) | CC BY-NC-SA 4.0 (#399) |
| `consultologist-provenance` | The provenance record contract and the hash definitions (`provenance-record.md`, `hash-definitions.md`, `provenance-versions.json`) | CalVer `vYYYY.MM.N` in the index; merging publishes | `provenance` container; vendored by the engine as a submodule and pinned by test (#400) | CC BY-NC-ND 4.0 |
| `consultologist-agents` | Agent manifests (`agents/*.yaml`) and the output-contract catalog (`agents/output-contracts.json`) | Agent versions are the Foundry integers the platform assigns and the yaml must match (`version: "48"`); the catalog is CalVer `vYYYY.MM.N`; merging publishes | **Both** the Foundry agent version (via the agents REST API) and the redacted mirror in blob (`agent-definitions/{name}/{version}/definition.yaml` + `{name}/latest.json`); the catalog to `output-contracts` (`{version}/output-contracts.json` + schemas, root `latest.json`) | CC BY-NC-SA 4.0 (#399) |
| `consultologist-package-format` | The normative package formats, schemas and conformance suite (`spec-versions.json`) | CalVer `vYYYY.MM.N` in the index; merging publishes | `package-format` container; vendored by the engine as a submodule and pinned by test (#376) | CC BY-NC-ND 4.0 |

A catalog change is gated twice: the agents repo's CI refuses a pull request
whose catalog would strand a published public package
(`scripts/check-catalog-strands-packages.cs`), and the operator sweeps both
registries against a published candidate before bumping the pin
(`GET /api/Operator/CatalogStrands`, #452).

Both content repos validate against the engine before publishing; the workflows
repo checks the app out at the commit the deployed engine reports
(`GET /api/Public/Engine`, #449), so "passed engine validation" is a statement
about what will run, not about `main`.

Two repos rather than one content repo: clean per-artifact tagging (CalVer vs Foundry
integers), separate future access control (clinicians author workflows, not agents), and
consistency with the per-artifact repo pattern (`snomed-snowstorm-mcp`).

## Full GitOps for agents

Merging to the agents repo's main **is** the publish event: CI creates the new Foundry
agent version (`az rest POST …/agents/{name}/versions`) and mirrors the manifest to
blob. The portal is never used to publish, so git↔Foundry drift becomes impossible by
construction; the app's attestation check remains as the backstop that *proves* it
(catching out-of-band portal edits). The pin bump (`AzureAI__AgentVersion`) stays a
deliberate, separate act — publishing a version and activating it are different
decisions.

## Trust model (the load-bearing part)

The registry is only a git-controlled channel if **CI is the only writer**:

- CI authenticates via **GitHub→Azure OIDC federated credentials** (no stored secrets);
  its identity holds Storage Blob Data Contributor scoped to the registry containers,
  and (agents repo) the Foundry role needed to publish agent versions.
- Humans and the app hold **Storage Blob Data Reader** only. The current
  laptop-publishing flow (user's Contributor role on `consultologistjobrecords`) is
  retired at migration.
- Shared-key access on the storage account is disabled once nothing depends on it
  (overlaps with the identity-based-connections migration, issue #10).

With that in place, startup attestation compares the Foundry plane (portal-editable)
against the registry plane (git-only) — a meaningful cross-channel check. Serving the
attestation manifest from a registry that humans could write would make the check
tautological; the write restriction is what preserves its value. Rationale history: the
manifest was first bundled into the app package precisely to keep it in a
harder-to-change channel than the thing it verifies.

## App changes at migration

- `AgentAttestationService` fetches the manifest from the registry
  (`agent-definitions/{name}/{pinned-version}/definition.yaml`) instead of the bundled file; the bundled
  file remains only as a local-dev fallback. Enforce/warn semantics unchanged.
- The `specVersion` gate, trust policy (registry location, enforce mode), and the engine
  itself stay in the app repo — expectations about *how to interpret* content ride with
  the code.
- `scripts/publish-workflow-package.sh` migrates to the workflows repo (CI becomes its
  main caller); the app repo's `packages/` and `agents/` folders are removed.

## Migration checklist (for the implementing session)

1. Create `consultologist-workflows` and `consultologist-agents` (gh), seed from the app
   repo's `packages/` and `agents/` (short history — plain copy with a pointer commit is
   acceptable; `git filter-repo` if history preservation is wanted).
2. Entra: one app registration or user-assigned MI per repo CI with federated credentials
   for `repo:Consultologist@<org-id>/<repo>@<repo-id>:environment:registry` — GitHub
   emits the immutable subject form for these repos, confirmed after the org
   transfer (#268); role assignments as
   above — **Storage Blob Data Contributor scoped to the repo's container(s)**,
   not the account (re-scoped for the two oldest identities 2026-08-25, #401).
3. CI workflows: validate (schema, CalVer format, immutability check) → publish on tag
   (workflows) / on merge (agents, plus Foundry publish).
4. App: attestation registry fetch + remove bundled content; update
   `docs/CONFIGURATION.md` for any new settings (e.g. manifest registry URI).
5. Lock down: revoke laptop Contributor, disable shared-key access (with #10).
6. Update this doc's status line and `workflow-packages.md`'s authoring section.
