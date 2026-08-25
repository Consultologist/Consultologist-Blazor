# Registry Operations: Browsing and Inspecting Workflow Packages

How to look at the package registry — the Azure Blob container that holds published
workflow packages. Written 2026-07-12, when the registry held `general` versions
v2026.07.1 (seed), v2026.07.2 (first real standards), and v2026.07.3 (first
specVersion-2 package with prompts).

## Where the registry lives

**Ownership split (Milestone 6, #92)** — two storage accounts, one home per
artifact:

- **Public**: `consultologistpublic` (resource group `consultologist_group`),
  container `workflow-packages`, **container-level anonymous read** (anonymous
  listing included — a public registry is browsable by design; blob-level
  access alone breaks enumeration, as #95's verification found) — the one and
  only home of repo-owned packages (`general`, future specialty bundles).
  Anyone can fetch
  `https://consultologistpublic.blob.core.windows.net/workflow-packages/general/v2026.07.6/manifest.json`
  with no token. The clinical account never enables public blob access —
  isolation by construction. (This account also hosts the `output-contracts`
  and `agent-definitions` registries, #93/#94.)
- **Private**: `consultologistjobqueue` (the account backing Durable
  Functions), container `workflow-packages` — the one and only home of
  `acct-*` forks, written exclusively by the app's registry writer and
  readable only by their owning account.

The engine's store routes by name (`WorkflowPackageNaming.IsAccountPackage`):
repo-owned refs resolve from the public container, `acct-*` from the private
one. When `WorkflowPackages__PublicBlobServiceUri` is unset (local dev),
everything routes private, as before the split.

Since #10, `consultologistjobqueue` is **RBAC-only** — shared-key access is
disabled, so every path (the app's identity, `az` with `--auth-mode login`,
tooling) authenticates through Entra ID data-plane roles. Account keys no
longer work there. (`consultologistpublic` keeps shared-key until #16 moves
publishing to CI OIDC.)

- **Layout** (one package shown; identical in both containers):

```
workflow-packages/
└── general/
    ├── latest.json                     # mutable pointer: {"version": "vYYYY.MM.N"}
    ├── v2026.07.2/
    │   ├── manifest.json
    │   └── standards.md
    ├── v2026.07.3/                     # specVersion 2: prompts join the bundle
    │   ├── manifest.json
    │   ├── standards.md
    │   └── prompts/
    │       ├── _snomed-tool-guidance.md
    │       └── <seven prompt templates>.md
    └── v2026.07.6/                     # specVersion 5: data collections replace standards.md
        ├── manifest.json
        ├── publish.json                # the publication stamp (#433): catalogRef + schema → contract, written at publish
        ├── dag.mmd
        ├── prompts/…
        ├── schemas/concept-list.json
        └── data/standards/
            ├── index.json
            └── <nine per-section .md files>
```

A package version is **one artifact**: prompts, schemas, and data collections travel
together under a single CalVer version. `latest.json` files are the only mutable
blobs; published versions are immutable by convention. **Versions ≤ v2026.07.5 are
pre-v5 archives** — immutable historical artifacts the current engine will not
execute (the v5-only rebase, #77).

## Via `az` CLI

`--auth-mode login` uses your Entra identity (requires a Storage Blob Data role on the
account); omitting it makes az fall back to account keys with a warning.

```bash
# List everything in the registry
az storage blob list --account-name consultologistjobqueue --auth-mode login \
  --container-name workflow-packages --query "[].name" -o tsv

# Read any single file straight to the terminal
az storage blob download --account-name consultologistjobqueue --auth-mode login \
  --container-name workflow-packages \
  --name general/v2026.07.3/prompts/extract-patient-concepts.md --file /dev/stdout

# Check where 'latest' points
az storage blob download --account-name consultologistjobqueue --auth-mode login \
  --container-name workflow-packages --name general/latest.json --file /dev/stdout
```

## Via the Azure portal

portal.azure.com → search **consultologistjobqueue** → left menu **Containers** (under
"Data storage") → **workflow-packages** → browse into `general/` and the version
folders. Clicking a blob offers an **Edit** tab that renders markdown/JSON inline —
convenient for reading a prompt.

> **Caution**: the portal will happily let you edit and save a blob in place, but
> published versions are immutable **by convention only**. Never edit a published
> version — changes go through a new CalVer version via
> `scripts/publish-workflow-package.sh` (which refuses to overwrite existing versions).
> The [content-repos design](content-repos.md) eventually makes this enforcement rather
> than convention: CI becomes the only writer and humans get read-only access.

## Via Azure Storage Explorer

The desktop app (or the portal's "Storage browser" blade) shows the same content with
tree navigation, and makes bulk-downloading a whole version folder trivial — useful for
diffing two published versions locally.

## Publishing (for completeness)

Publishing is not a browse operation: author changes in the repo's `packages/` sources,
bump the manifest's CalVer version, and run
publishing from the [consultologist-workflows](https://github.com/Consultologist/consultologist-workflows)
repo: tag `{name}-vYYYY.MM.N` and CI publishes via OIDC (#16). Human registry
writes are retired.
The script uploads the version folder, updates the `latest` pointer, and refuses to
republish an existing version.

**What the content repo's CI does and does not check.** Its `validate.yml` is
*structural* — manifest parse, name and CalVer shape, and that every referenced
prompt, prelude, schema and data file exists. It never reads `specVersion`, never
traverses `nodes`, and never checks that bindings, `result`/`results` and declared
`inputs` resolve. A manifest with a broken binding or an unreachable deliverable
passes there and fails at engine load. **Run a manifest through this repo's
`WorkflowPackageValidator` before tagging** — the engine is the only authority, and
nothing upstream of it enforces the format. The content repo's CI runs that
validator itself (#185), against the engine commit the deployed app reports at
`GET /api/Public/Engine` (#449) — `main` only when the endpoint cannot say, and
the run says which.

The checked-in `packages/<name>/dag.mmd` is the **derived** DAG diagram (Mermaid;
GitHub renders it inline) generated by `WorkflowDagDiagram` from `nodes` — never
edit it by hand. Regenerate it whenever nodes or bindings change (a declaration-only
change such as adding `inputs` leaves it byte-identical). Since content left this
repo (#16) nothing regenerates or diffs it automatically: `WorkflowDagDiagramTests`
pins the generator against fixtures, not against any package's checked-in file, and
the content repo's CI ignores `dag.mmd` entirely. The publish script uploads the
diagram with the version folder when present.

## The output-contracts registry — implemented 2026-07-16 (#93)

The catalog is a versioned artifact in the public account, container
`output-contracts`: `vYYYY.MM.N/output-contracts.json` + `vYYYY.MM.N/schemas/…`,
with the usual mutable `latest.json` pointer and refuse-overwrite immutability
(`scripts/publish-output-contracts.sh`). The engine loads the pinned version at
startup (`OutputContracts__Pin`, default `@latest`) — **the registry is the
runtime source**; changing the catalog is publish + restart, no redeploy. Every
job record stamps the resolved concrete ref (`catalogRef`), and startup
attestation fails loud if the registry version differs from the bundled
git-tracked `agents/output-contracts.json` (publish/pin/deploy drift). Local dev
(public URI unset) loads the bundled file directly.

## The agent-definitions registry — implemented 2026-07-16 (#94)

Public account, container `agent-definitions`:
`<name>/<foundry-version>/definition.yaml` + `<name>/latest.json` — keyed by
**Foundry's version sequence**, the same numbers job records store in
`agentVersions`, so a record resolves with zero translation. Published
definitions are **redacted**: instructions, model, schema, and tool types are
public; `tools[].server_url` and `project_connection_id` are stripped. The
transform lives **once**, in the agents repo's
`scripts/publish-agent-definition.sh`, and the script greps its own output and
fails if either field survives — so plumbing cannot reach the registry. A
second implementation in this repo (`AgentDefinitionRedaction`) was deleted in
#259: it had no production caller, so it proved only that two implementations
agreed and nothing consumed the agreement.

**No check compares the published artifact against `redact(git manifest)`** —
this said a runtime one did until 2026-07-31. It cannot be done honestly from
the app: the runtime fetches the version the catalog pins, while a bundled
manifest tracks whatever the submodule points at, and the two legitimately
differ (measured 2026-08-05: `test-json.yaml` declared 48, the catalog pinned
47). The property belongs in the agents repo, where the publish script reads
the version out of the manifest it is publishing so the two align by
construction. Tracked there.

Versions are refuse-overwrite immutable.

## Account packages (`acct-*`) — implemented 2026-07-16 (#57)

The in-app editor publishes per-account forks under `acct-<12 hex of the
AppUserId>` — same container, same layout, same validator as repo packages. The
server assigns name, version, and `derivedFrom` (the fork's concrete source ref);
the manifest is uploaded last under a conditional create, so a version is
invisible until it commits and can never be overwritten. `acct-*` names are
usable only by their owning account (enforced in the pin resolver and at job
start); account versions are never deleted. First fork in the registry:
`acct-7bca2dcc1ed4/v2026.07.1` (derived from `general/v2026.07.6`).

Publishing requires Storage Blob Data **Contributor** (reads need only Reader) —
granted to the identity the app *actually authenticates as*: the
**user-assigned** managed identity selected by the `AZURE_CLIENT_ID` app
setting, not the Function App's system-assigned identity (the first production
publish 403'd on exactly this distinction).

Since #134 the pin (`consult.workflowPackage`) is user-settable: the package
selector on the **Consults** page writes it through the generic account-settings
PUT — publish (pins the new fork version) and revert-to-default (deletes the
setting) are no longer the only writers. The Workflow page's selector wrote it
too until #411, which separated *what you are editing* from *what your consults
run*: it now chooses which package the editor loads (`?ref=` on
`WorkflowPackages/Current/Content` and `/Diagram`, gated by the same
`WorkflowPackageNaming.CanAccess` the publisher applies to its source) and
writes nothing. Publishing still activates. The selector lists
repo-owned packages from the anonymous chain view and the caller's own fork from
`GET /api/WorkflowPackages/Mine` (authorized; private-registry listing of the
account's `acct-*` versions). The pin resolver remains the sole authority on
what a pin may reference.

**Since #447 (2026-08-24) ownership is a record, not the name.** An account
holds many packages — its first is still `acct-<12 hex>`, its others are
`acct-<12 hex>-<slug>` — and the `PackageOwners` table on the account-storage
account (`PartitionKey` = AppUserId, `RowKey` = package name) says which
account may read, pin, publish to, or walk the lineage of an `acct-*` package.
The publisher writes the row before its first upload; nothing else writes
one. `GET /api/WorkflowPackages/MinePackages` lists the set (`Mine` still
serves the first package to older clients). #462 retired the derived-name
fallback and the startup backfill once every existing package was recorded.
To check the two agree — the registry's names against the table's rows —
read-only, with identity auth:

```
az storage blob list --account-name consultologistjobqueue --container-name workflow-packages \
  --auth-mode login --query "[].name" -o tsv | awk -F/ 'NF==3 && $3=="manifest.json" {print $1}' | sort -u
az storage entity query --account-name consultologistjobqueue --table-name PackageOwners \
  --auth-mode login --query "items[].[PartitionKey, RowKey]" -o tsv
```

Every name from the first must appear once in the second under the account
whose id starts with the name's 12 hex. A name with no row is unreachable by
anyone; the remedy is one `az storage entity insert` under the right account.

## Lineage (#89)

`GET /api/WorkflowPackages/Lineage?ref=name@vYYYY.MM.N` (authorized; owner-only
per hop) walks `derivedFrom` manifests to the root and returns the ordered
chain — e.g. `["acct-…@v2026.07.1", "general@v2026.07.6"]`. Manifest-only
reads: lineage displays even for versions the engine would not execute.

## Blob CORS (added 2026-07-17, #105)

The public account allows cross-origin `GET` from any origin (blob service CORS
rule) so browsers fetch registry documents directly — the History page resolves
each job's `catalogRef` document this way, and the marketing site will read the
same blobs.

## The anonymous chain view — implemented 2026-07-16 (#95)

`GET /api/Public/Chain` (no auth, open CORS, 60s cache): one JSON document
naming the whole public chain — repo-owned packages (versions + latest), the
output-contract catalog (versions, latest, contract → agent@version table),
and the redacted agent definitions (Foundry versions + latest). Backed
exclusively by `PublicRegistryReader`, which holds no credential and no
private-container client — `acct-*` content is unreachable from this endpoint
by construction, not by filtering. This is the surface the app's logged-out
view and the marketing site consume.

## Publication metadata (decided 2026-07-16; the sidecar exists since #433 — `publishedBy`/`publishedAt` still pending)

Author and publication time are **publish-event data, not package content**, so
they will never appear in the manifest (rationale in package-format-v5.md, "What
the manifest deliberately excludes"). When a package-picker UI or the editor's
"Editing `general@v2026.07.6`" banner wants display metadata, the home is the
registry layer, stamped at publish: blob metadata on the manifest blob (or a
sidecar record) carrying `publishedBy`/`publishedAt` — set by the publish script
for repo packages and by the registry writer (#57) for `acct-*` ones. Same trust
posture as the server-stamped `derivedFrom`, zero format change, and the manifest
stays byte-round-trippable through the editor. Until then, the blob's creation
time and CalVer already answer "when", and git / the `acct-*` name already answer
"who".

> **Amended 2026-08-23 (#433).** The sidecar is `publish.json`, beside the
> manifest. Today it carries the **publication stamp**: `catalogRef` — the
> output-contract catalog the version was validated under — and `contracts`,
> schema id → the contract id each declared schema resolved to. The app's
> writer uploads it after the files and before the manifest (the commit
> marker), so an `acct-*` version is never reachable without it; the content
> repo's CI does not run the publisher and writes it with
> `scripts/stamp-workflow-package.cs` — wired in
> [consultologist-workflows#19](https://github.com/Consultologist/consultologist-workflows/issues/19);
> until then public versions are unstamped and load as they always did. The
> stamp is never part of the editor's files: a request carrying one is
> refused by path. `publishedBy`/`publishedAt` join the same file when they
> land.
