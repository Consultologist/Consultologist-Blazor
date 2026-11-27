# Registry Operations: Browsing and Inspecting Workflow Packages

How to look at the package registry — the Azure Blob container that holds published
workflow packages. Written 2026-07-12, when the registry held `general` versions
v2026.07.1 (seed), v2026.07.2 (first real standards), and v2026.07.3 (first
specVersion-2 package with prompts).

## Where the registry lives

> The contract a reader relies on — one container per registry, the two
> layout families, CalVer, immutability, `latest.json`, anonymous read
> including listing — is published as `registry-layout.md` in
> [consultologist-provenance](https://github.com/Consultologist/consultologist-provenance)
> (#401). This page is what an author or a verifier needs to read the
> public registries; the operator's recipes — the private account, roles,
> repair — live in the operations repository (private, by design; #397).

**Ownership split (Milestone 6, #92)** — two storage accounts, one home per
artifact:

- **Public**: `consultpubcaeast` (resource group `consultologist_group`),
  container `workflow-packages`, **container-level anonymous read** (anonymous
  listing included — a public registry is browsable by design; blob-level
  access alone breaks enumeration, as #95's verification found) — the one and
  only home of repo-owned packages (`general`, future specialty bundles).
  Anyone can fetch
  `https://consultpubcaeast.blob.core.windows.net/workflow-packages/general/v2026.07.6/manifest.json`
  with no token. The clinical account never enables public blob access —
  isolation by construction. (This account also hosts the `output-contracts`
  and `agent-definitions` registries, #93/#94, the `package-format` registry,
  #376, and the `provenance` registry, #400 — the record contract a verifier
  reads and the hash definitions they recompute with.)
- **Private**: the account registry, containers `org-account-packages` /
  `personal-account-packages` on the app's private storage account (#602 —
  the account's `AccountKind` chooses; null falls to personal, text's own
  rule) — the one and only home of `acct-*` packages, written exclusively
  by the app's registry writer and readable only by their owning account.
  Never publicly readable.

The engine's store routes by name (`WorkflowPackageNaming.IsAccountPackage`):
repo-owned refs resolve from the public container, `acct-*` from the private
pair — a fork derives from one account and so lives in exactly one of the
two, which is what makes a context-free try-both read safe. When
`WorkflowPackages__PublicBlobServiceUri` is unset (local dev), repo-owned
names also fall to the private pair, as before the split.

Both accounts are **RBAC-only** — shared-key access is disabled (#10 on the
private account, #16 on the public one once publishing moved to CI OIDC), so
every path — the app's identity, CI, `az` with `--auth-mode login` —
authenticates through Entra ID data-plane roles. Account keys work nowhere.

- **Layout** (one package shown; identical in all three containers):

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

## Reading the public registry

No credential is needed for any public registry: fetch or list by URL.

```bash
# List everything in a registry (container-level anonymous listing)
curl -s "https://consultpubcaeast.blob.core.windows.net/workflow-packages?restype=container&comp=list"

# Read any single file
curl -s https://consultpubcaeast.blob.core.windows.net/workflow-packages/general/v2026.07.3/prompts/extract-patient-concepts.md

# Check where 'latest' points
curl -s https://consultpubcaeast.blob.core.windows.net/workflow-packages/general/latest.json
```

Every published version carries its `LICENSE` beside its index from the
first publish after 2026-08-25 (#399); the registry's README states the
terms and their scope. `az storage blob …` against `consultpubcaeast` works too (`--auth-mode
login` with a Storage Blob Data Reader role). Published versions are
immutable by refusal (registry-layout.md § 4): a published version is never
edited in place; changes are a new CalVer version.

## Publishing (for completeness)

Publishing is not a browse operation: author changes in the repo's `packages/` sources,
bump the manifest's CalVer version, and publish from the
[consultologist-workflows](https://github.com/Consultologist/consultologist-workflows)
repo: tag `{name}-vYYYY.MM.N` and CI publishes via OIDC (#16). Human registry
writes are retired. That repo's `scripts/publish-workflow-package.sh` uploads
the version folder (the manifest last), updates the `latest` pointer, and
refuses to republish an existing version.

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

> **2026-08-28 (#500).** `package-format@v2026.08.8`: specVersion 10 defined,
> its schema and its conformance suite (148 cases) published; the engine runs
> ten from the same deploy, and the demo `example-classifier-scope` follows in
> the workflows repo.
>
> **2026-08-27 (#495).** A third contract, `classification` (v10 § 4), published
> as `output-contracts@v2026.08.1` with its agent `classification@1`. As
> every catalog release: publish, strand check, then the app's submodule
> pin and `OutputContracts__Pin` — nothing activates on publish.

The catalog is a versioned artifact in the public account, container
`output-contracts`: `vYYYY.MM.N/output-contracts.json` + `vYYYY.MM.N/schemas/…`,
with the usual mutable `latest.json` pointer and refuse-overwrite immutability
(the agents repo's `scripts/publish-output-contracts.sh`). The engine loads the pinned version at
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
construction. Tracked there as
[consultologist-agents#6](https://github.com/Consultologist/consultologist-agents/issues/6).

Versions are refuse-overwrite immutable.

## Account packages (`acct-*`) — implemented 2026-07-16 (#57)

The in-app editor publishes per-account forks under `acct-<12 hex of the
AppUserId>` — same container, same layout, same validator as repo packages. The
server assigns name, version, and `derivedFrom` (the fork's concrete source ref);
the manifest is uploaded last under a conditional create, so a version is
invisible until it commits and can never be overwritten. `acct-*` names are
usable only by their owning account (enforced in the pin resolver and at job
start); account versions are never deleted while the account exists (account closure, #559, removes them with the account). The first fork was published on
2026-07-16, derived from `general/v2026.07.6`.

Publishing is the app's registry writer acting as the app's own identity; no
person holds a write role on the account registry.

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
The operations repository carries the recipe that checks the two agree.

## Operator checks (#384, #452)

Every account's pin is resolved against the loaded catalog at startup
(`[PinHealth] …` in the log stream) and on demand at `GET /api/Operator/PinHealth`;
before a catalog pin bump, `GET /api/Operator/CatalogStrands?candidate=…`
resolves every published version in both registries against the candidate.
Both are operator surfaces (`Operators__AppUserIds`, CONFIGURATION.md); the
recipes and how to read them are in the operations repository. The public
half of the pre-bump check runs on the agents repo's pull request through
`scripts/check-catalog-strands-packages.cs`; both call
`WorkflowPackageStore.ResolveContracts`, so they cannot drift.

## Verifying a record (#402)

A job record's hashes are defined byte for byte in the provenance registry
(`hash-definitions.md`, `provenance@vYYYY.MM.N`); History links each number
to that document at the version the deployed engine attests, and the format
chip to its specification. To check a record:

- **In History**, *Verify* (Provenance section, completed jobs) recomputes
  the deliverable hashes from the texts the record carries — the output hash
  by its definition and every document digest — and marks each *recomputed —
  matches* or *does not match*. It cannot check the input hash (the inputs
  are not on the record) or the per-node hashes (the rendered prompts are
  not either).
- **From a terminal**, with the record and, for the input hash, the inputs:

  ```
  curl -sS -H "Authorization: Bearer $TOKEN" \
    https://<function-host>/api/ConsultGenerationJobs/<job-id> > job.json
  dotnet run -v q --file scripts/verify-job-hashes.cs -- job.json
  dotnet run -v q --file scripts/verify-job-hashes.cs -- job.json --inputs inputs.json   # effectiveInputHashVersion 3, 4, 5
  dotnet run -v q --file scripts/verify-job-hashes.cs -- job.json --draft draft.txt      # version 2
  ```

  It runs the engine's own functions (`ConsultGenerationProvenance`), whose
  agreement with the published definitions and worked examples is pinned by
  `tests/ProvenanceVersionSetTests.cs`; a mismatch is therefore about the
  bytes, never about the recipe. Exit 0 all match, 1 any mismatch, 2 usage.
  `inputs.json` is the request's own form — `{"id": value, …}`.

## Lineage (#89)

`GET /api/WorkflowPackages/Lineage?ref=name@vYYYY.MM.N` (authorized; owner-only
per hop) walks `derivedFrom` manifests to the root and returns the ordered
chain — e.g. `["acct-…@v2026.07.1", "general@v2026.07.6"]`. Manifest-only
reads: lineage displays even for versions the engine would not execute.

## The formats catalogue read (#1005, 2026-10-14)

`GET /api/WorkflowFormats` (authorized; CORS applied; never cached) serves the
formats catalogue the editor reads at specVersion 25 (design § 3.8): every
format's id, description, type, producer, `outputMacros`, values and
parameters; the format file including its schema in the editor's write form,
with the hash a stamp records; and the detail line's facts — the agent's name
and version where one runs, the operation where the engine computes, and
whether the output is attested — plus the custom producer's agent. The engine's
pinned catalogue (`Formats__Pin`) by default; `?ref=formats@vYYYY.MM.N` reads
the version a package's stamp names. The agent yaml and the custom trailer are
never served: no authoring decision depends on them.

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
> land. *(2026-10-17: both `validate-workflow-package.cs` and
> `stamp-workflow-package.cs` load the formats catalogue beside the output
> contracts, so a specVersion 25 package validates and stamps offline; a
> package below 25 is migrated on disk by `migrate-workflow-package.cs`.)*

## specVersion 31 (#1508, preview)

*Part 07 §§ 4.4, 5 and 6 and part 09 § 2 of
[the 31 design](package-format-v31/index.md). How the engine publishes and serves a 31 version is in
[the engine at specVersion 31](engine-v31.md), "Publish, `Current`, `Content` and import"; the library's pipeline in
[the package format as a library](package-format-library.md), "specVersion 31: publish".* Nothing here applies
unless `WorkflowPackages__PreviewSpecVersions` admits 31, which only dev does.

### A 31 version's folder

```
<container>/
└── acct-<root>/<slug>/
    ├── latest.json                     # the pointer, set after the stamp, as at 5–30
    ├── v2026.12.1.lease                # the writer's lock blob (empty), beside the folder, never in it
    └── v2026.12.1/
        ├── manifest.json               # name and version written by the registry (D2)
        ├── nodes/…, formats/…, checks/…, deliverables/…, tests/…   # the source, as normalised, every file sourceHash covers
        ├── lock.json
        ├── publish-report.json         # the publish report (not evidence: records never pin it)
        ├── migration-report.json       # only for a version the operator's migration published
        ├── resolve.json                # last of the files
        └── publish.json                # the stamp, written last of all
```

The engine writes a 31 version only into an account's private container (`org-account-packages` or
`personal-account-packages`), under an `acct-…` name: `WorkflowPackages/Publish` publishes only an account's packages.
`dag.mmd` is written only once area 13 draws it. The order matters: **a folder without `publish.json` does not exist
for any reader**, so a publish that dies part-way leaves nothing anyone loads, and the next attempt clears it and
writes it again. A folder that has a `publish.json` of any shape (a 5–30 stamp has the same name) is never written to
again.

**The stamp** is `publish.json`, canonical JSON:
`{ "lockHash": "sha256:…", "resolveHash": "sha256:…", "specVersion": 31 }`, with `"migratedFrom": "<old version>"`
for a version the operator's migration published. Its schema is `publish-stamp.schema.json` in the format spec. The
engine reads it first: a stamp with `specVersion` 31 takes the 31 load path (`resolve.json` and `lock.json`, held to
the stamp's hashes), and anything else takes the 5–30 path.

**Evidence is never taken from an upload.** `resolve.json`, `lock.json`, `publish-report.json` and `publish.json` in an
upload or a zip are dropped unread; publish writes its own.

### Dev's registry (part 09 § 2.2)

31 is proven on `canada-east-ai-function-dev` with its own registry, and production's registry never sees a 31 package
before the bump. On dev, `WorkflowPackages__BlobServiceUri` (the private side) and `WorkflowPackages__PublicBlobServiceUri`
(the public side) point at containers in `consultdevcaeast` ([CONFIGURATION.md](../CONFIGURATION.md)). Setting them,
and the roles the app's identity needs there, are the user's. It is filled in two ways, with fictional content only:

- **The catalogues are copied, not published.** The public-side containers the engine reads (`workflow-packages`,
  `output-contracts`, `formats`, `agent-definitions`, `package-format`, `provenance`) get copies of the versions
  production uses, taken from the same release tags. The 31 type registry and the 31 agents catalogue are bundled in
  the app (`package-format/v31/`, `agents/v31/`) and are not read from the registry.
- **The example packages are published through the engine.** The example packages at 31 come from the migration
  branch and are published to dev through `WorkflowPackages/Publish` by an operator's account, not by the public
  repository's tag CI (which publishes to production). Each lands as that account's package (`acct-…/<slug>`):

  ```
  POST <dev app>/api/WorkflowPackages/Publish
  Authorization: Bearer <an account's token>
  { "newPackageSlug": "example-nested-note", "files": { "manifest.json": "<its text>", "nodes/index.json": "<its text>", … } }
  ```

  `manifest` is left out (null) and every file, `manifest.json` included, is text in `files`. The answer is the
  version and the publish report in full, or a 400 whose `report` says why. A migrated version is published the same
  way with the trailing `migrationReport` (the operator's `migration-report.json`), which only an account in
  `Operators__AppUserIds` may send.

### The two scripts

Both reference the Api project, as `scripts/migrate-workflow-package.cs` does, and build the engine's own inputs from
the pinned content repositories: the type registry from `external/consultologist-package-format/v31` and
`external/consultologist-agents/agents/v31`, the toolchain as the engine attests it (the package-format release in
`spec-versions.json`, the formats catalogue's ref, `agents@<the 31 catalogue's version>`), and the limits the engine has
with no `ConsultGeneration__*` settings (the spec's starting values), so a run is the same anywhere. The clock is the
system's, and no diagram is drawn.

**`scripts/check-workflow-package-v31.cs`** checks a package folder, or publishes it for CI:

```
dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder>
dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder> --write <out folder> [--version <tag>] [--published-by "ci <tag>"]
```

- Without `--write` it runs steps 0 to 3 (`PackagePublish.CheckAsync`): nothing is written.
- With `--write` it runs steps 0 to 4 and writes the version's files in the pipeline's order into `<out folder>`, then
  `publish.json` last. The out folder must be new or empty (a stamped or non-empty one is refused, exit 2), so no stale
  file ever sits beside the stamp. These are the files CI is to attach as Release assets.
- `--version <tag>` replaces `manifest.json`'s `version` in place before step 0 (the name is kept); without it the
  manifest's own name and version are used. `--published-by` is the report's `publishedBy`; it defaults to `ci <tag>`
  with `--version` and `ci` without. `--version` and `--published-by` need `--write`.
- stdout is the report's canonical JSON alone. stderr has S07-015 for each compiled copy or stamp found in the folder
  (dropped unread), then each refusal, each warning, and a summary line.
- Exit 0: published, or would publish; 1: refused; 2: usage or environment (an unreadable folder or registry, or an
  out folder that is not empty or holds a stamp). A `.git` folder is skipped, and so is a symbolic link (any reparse
  point), file or folder: a link is never followed, so `--write` never copies a link's target into the Release assets.

**`scripts/rerender-workflow-packages-v31.cs`** is the corpus re-render (part 07 § 4.4, D11):

```
dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <folder of name/version folders>
```

It walks the folder to any depth for the folders that hold a `publish.json`. Each is a version: its name is the
relative path of its parent, with `/` (so an account package published with a slug, `acct-<12hex>/<slug>/<version>`,
and a nested public name such as `test/general/<version>` are found, as `<name>/<version>` is), and its version is the
folder's own name. The walk does not go into a version's folder, and skips a symbolic link (any reparse point), file or
folder, both in the walk and when it reads a version's files. A folder with no `publish.json` is not a version; a
`publish.json` that is readable JSON with a `specVersion` other than 31 is skipped (a 5–30 version has a stamp of the
same name); a `publish.json` that cannot be read or is not valid JSON is a difference.

For each 31 version it reads the stored source as an upload (leaving out `dag.mmd` and `migration-report.json`; the
evidence is dropped by intake), runs steps 0 to 3 in memory with this build's library, and compares the result with
the stored `publish-report.json`: the `resolveHash`, and each test's `outcomeHash`. A test either side lacks, a version
that is now refused, and a stamped folder with no readable report are each a difference. stdout has one line per
difference and a summary. Exit 0: no difference; 1: a difference; 2: usage or environment, or no stamped 31 version
found (a walk that checked nothing never reports "no difference").

**The corpus re-render before a library upgrade.** Before a build moves a locked library (Scriban, the evaluator, an
operation), copy every stamped 31 version from the registry into a folder laid out as the registry is (`<name>/<version>/`, the
name nested where it is nested), run the
re-render on the current build, then on the candidate build. Each run must print no difference. A difference under the
candidate is the upgrade changing what a published version compiles to or does: the upgrade needs a break point (added to
`EngineBreakPoints`, compiled in, so a code change; area 06) or is not made.

**What only a manual run has shown.** The scripts' wiring is pinned by `tests/V31Publish/ScriptsTests.cs`, which does
not run them as processes. The folder walks, the out-folder refusals firing, the `--version` edit, the stamp written
last, a trailing separator on the out folder and the re-render's difference lines were shown by these runs (#1508,
Task 20), on a copy of the `compiled-documents/macros/source` case outside the repository given a snapshot test with
`dotnet run -v q --file scripts/migrate-workflow-package.cs -- <source> --to 31 --snapshot --write` (`<source>`, `<out>`
and `<corpus>` stand for folders outside the repository):

1. `dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <source>`: exit 0, stderr
   `ok: example-macros@v2026.12.1 would publish (1 test(s) passed); nothing written.` (after two S04 warnings, for
   `draft` and `stamp`).
2. `dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <source> --write <out> --version v2026.12.7 --published-by "ci v2026.12.7"`:
   exit 0, `published: example-macros@v2026.12.7 written to <out> (27 files, publish.json last).`
   The stamp was `{"lockHash":"sha256:935b2a36...","resolveHash":"sha256:1193334f...","specVersion":31}`.
3. What was written, copied to `corpus/example-macros/v2026.12.7`:
   `dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <corpus>`: exit 0,
   `no difference: 1 stamped 31 version(s) re-rendered.`
4. The written copy's `draft` stub edited ("Stub reply for node.draft (c82d06f2)." to "Edited draft stub."), then the
   same re-render: exit 1,
   `example-macros/v2026.12.7: refused now (1 refusal(s)): Test 'migration_snapshot' failed: deliverables for deliverable.note expected sha256:8d7ef30f..., got sha256:2527b96f.... Publish stopped.`
   then `1 difference(s) in 1 stamped 31 version(s) re-rendered.` The test pins the deliverable's hash, so the edit
   shows as "refused now". An edit that keeps every test passing would show as
   `test '<id>' outcomeHash was …, now …`; no run has shown that line.
5. The check on the stamped folder: exit 0, with four S07-015 lines (`lock.json`, `publish-report.json`, `publish.json`,
   `resolve.json`). The check on the edited copy: exit 1, with S07-007. `--write` into the stamped folder: exit 2,
   "holds a stamp".
6. A trailing separator on the out folder:
   `dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <source> --write <out2>/ --version v2026.12.7`:
   exit 0, `published: example-macros@v2026.12.7 written to <out2> (27 files, publish.json last).`
   (`<out3>//` also exit 0). A re-render of a copy of `<out2>`: `no difference: 1 stamped 31 version(s) re-rendered.`

These runs (#1508, the final review's fixes) showed the nested walk, the stamp cases, the new exit and the links, on the
same case (its own `close_line` test) written with `--write <out> --version v2026.12.7`:

7. Copies of `<out>` at `<corpus>/acct-0123456789ab/breast/v2026.12.7`, `<corpus>/test/general/v2026.12.7` and
   `<corpus>/plain/v2026.12.7`: `dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <corpus>`: exit 0,
   `no difference: 3 stamped 31 version(s) re-rendered.` (Before the fix, the two nested ones were skipped.)
8. Added: a folder with a 5–30 stamp (`{"specVersion":9,…}`), one with no `publish.json`, and
   `acct-0123456789ab/broken/v2026.12.8` whose `publish.json` is `{"specVersion":`: exit 1,
   `acct-0123456789ab/broken/v2026.12.8: publish.json is not readable or not valid JSON` then
   `1 difference(s) in 4 stamped 31 version(s) re-rendered.`
9. A folder holding only a 5–30 stamp and a folder with no stamp, and an empty folder: exit 2,
   `error: no stamped 31 version found under <corpus> (a version is a folder holding publish.json).`
10. Links: a name folder that is a link to a stamped copy outside the corpus, a version folder that is a link, and
    `plain/v2026.12.7/nodes/extra.md` a link to a file outside: exit 0, `no difference: 3 stamped 31 version(s)
    re-rendered.` (The control, the same `extra.md` as a real file: exit 1, `refused now … File 'nodes/extra.md' is
    not listed …`.) The check script on a copy of `<source>` with a linked `nodes/extra.md` and a linked `assets`
    folder, `--write <out>`: exit 0, no link and nothing of either target in `<out>`, and the report's `sourceHash`
    the same as the run without links.

### The bulk publish

The operator migrates every account's package to 31 and publishes it as a new version of that account's package
(part 10 § 1.5 of [the 31 design](package-format-v31/10-migration-and-tools.md#15-account-packages-by-the-operator)).
The engine does one package a call (`Admin/MigratePackage`, [the engine at specVersion 31](engine-v31.md), "The bulk
publish"); `scripts/bulk-publish-v31.cs` drives it over every account. Running it against production, sending the
owners' notices and moving production pins stay the user's (R21). The route counts and runs the migration's snapshot at the
deployment's configured limits (`ConsultGeneration__*`); the two CI scripts and `scripts/migrate-workflow-package.cs` keep the
spec's starting values, as they run with no deployment.

```
ACCESS_TOKEN=<an operator account's bearer token> \
dotnet run -v q --file scripts/bulk-publish-v31.cs -- <operator folder> --engine <engine url> [--publish]
```

**The folder.** The operator's folder holds what the operator chose for each package, by account and package name:

```
<operator folder>/
├── <owner>/<name>/options.json        # the package's migration choices (part 10 § 1.4); absent: the defaults
├── <owner>/<name>/hand-edits/…        # each file replaces or adds its path in the migrated package;
│                                      # <path>.deleted removes <path>
├── bulk-publish-record.json           # written by the run
└── owners.json                        # written by the run
```

`<owner>` is the account's app user id and `<name>` its package (`acct-<root>-<slug>` or `acct-<root>/<path>`, nested
as it is nested). A package with no folder is migrated with the defaults and no edits. **A folder that names no
package stops the run:** a folder holding `options.json` or `hand-edits/` whose `<owner>/<name>` is neither a package
the engine listed nor one the record carries (below) is printed, by its path, and the run exits 2 before anything is
sent, neither file written; a misspelt folder would otherwise leave its package at the defaults. The engine counts the
packages it could not read but does not name them, so a folder of such a package is refused too: set it aside for
that run.

**A hand edit is made only when the file its notice names is among the edits.** Each S10-002 notice names a file
(*"Hand edit needed in <file>, <member>: …"*); unless `hand-edits/` holds that file (edited, or `<file>.deleted`), the
hand edit is left, and the package is `not migrated` with every left notice in `handEditsLeft`. An operator who answers a
notice by editing another file still puts the named file in `hand-edits/`, unchanged if need be, as the
acknowledgement that it is answered. The script asks the engine for
the list (`GET`), reads every package's folder before it sends anything (an `options.json` that does not read, A10-019's
sentence, stops the run with exit 2 and nothing sent), then sends each package in the engine's order, one at a time. It
talks only to the engine URL it is given, with the token in `ACCESS_TOKEN`; the URL is `https://`, or `http://` only for
`localhost`, `127.0.0.1` or `[::1]` (any other is a usage error, exit 2, nothing sent).

**The dry run.** Without `--publish` each package is migrated and its publish run in memory by the engine: nothing is
written in the registry, no pin moves, and no alert is raised. Its outcome is `would publish`, with the version it
would take. Run it first, make the hand edits it reports (`handEditsLeft`) and the choices it asks for, and run it
again until every package would publish or is to stay at 30; then run it with `--publish`.

**The record.** `bulk-publish-record.json` is `{ "engine", "publish", "notRead", "rows" }`: one row per package, the
engine's answer (`owner`, `name`, `oldVersion`, `newVersion`, `outcome`, `handEditsLeft`, `notices`, `report`, the
`options` used and `defaults`, A10-083 when the package had no `options.json`) and the `time` it was answered (UTC).
`notRead` counts the accounts' active packages the engine could not read; they are not in the record. **Both files are
written after every row**, each to a temporary file beside it that is then moved over it, so an interruption loses at
most the package in flight and never leaves half a file. **A package the engine did not answer for** (an error status,
a timeout, a lost answer, an answer with no outcome) gets the row `{ owner, name, oldVersion, outcome, time }` with the
outcome `unknown: <fault>` (the status, or the exception's type and message; never the answer's text), and the run
stops there with exit 2. In a `--publish` run an `unknown` package may have been published (the engine can fail after
the stamp, or the answer be lost); in a dry run nothing is published, so its `unknown` means only that it was not
answered. **A run carries forward** every row of the existing record whose outcome is `published` or `unknown: …` and
whose package it did not answer (a package published by an earlier run is at 31, so the engine no longer lists it),
the latest row for each package, and builds both files from the merged rows: the earlier rows first, then this run's.
An existing record the script cannot read, or one written for another engine (its `engine` is not this `--engine`,
compared after trimming a trailing `/`), stops it with exit 2 before anything is sent, and neither file is written:
use one operator folder for each engine. So a rerun after a fault or an interruption, or a dry run after a real run,
keeps every package published or perhaps published, and its owner, in the notification. `owners.json` is the list for
the before-and-after notification (#1447): per owner, the packages `published` (in a dry run, those that would be;
each with its versions and the notices the owner must see, `flag` becoming `retry` among them), the packages
`notMigrated` (each with its version, the `reason`, S10-005 as the owner's `notice`, and the `deadline`, A11-015:
*"This package stops working when specVersion 30 is retired."*), and the packages `unknown` (each with its `name` and,
as its `notice`, A11-017 for the operator: *"The engine did not answer for this package; check whether it was
published before sending this list."*).

**An `unknown` row.** Check the package's version on the engine (its account's pin, and whether a 31 version of it is
stamped), then rerun. A package the engine still lists is sent again, and its answer replaces the `unknown` row. One the
engine no longer lists is at 31: it was published, and its row stays `unknown`, and every run exits 2 for it, until the
operator writes `"published"` as that row's `outcome` in the record (its notices are not known: tell its owner from
the 31 version's report). Send `owners.json` only when it lists no `unknown` package.

**Exit codes:** 0, every package published (or would); 1, a package is not migrated, or an account's active package
could not be read; 2, usage, a folder or an earlier record that does not read, a folder that names no package, a
record of another engine, an engine that does not answer as the route does, or an `unknown` row this run did not
answer. The run stops at the first engine fault, and the two files then hold every row answered before it, the
`unknown` row of the package in flight, and the earlier record's carried rows. Rerun it until it exits 0 or 1: what
was published is carried forward, and a package the engine still lists is tried again. (A package at 31 that the
engine is asked for anyway answers `not migrated: The package is already at specVersion 31.`, and its earlier
published row is replaced by that answer; keep the earlier record when that happens.)

**A package it cannot publish.** The owner's clarification of B2 on #1508
([2026-11-25](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7179842619)), word for word:

> This covers a package the migration cannot publish at 31: a publish refusal such as A11-001, or a migration refusal.
> - **It keeps its 30 version and its pin.** Nothing is published at 31 for it, and no `max` or other value is guessed to force it through.
> - **It keeps running at 30** until 30 is retired (#1450). Old spec versions keep their own load path; there is no backfill.
> - **It is recorded "not migrated: <reason>"** in the bulk publish's record, with the refusal's sentence. The weekly summary already counts it ("<q> packages do not migrate", D22 of [PR #1532](https://github.com/Consultologist/Consultologist-Blazor/pull/1532)).
> - **Its owner is named in the before-and-after notification** (#1447), with the reason, and with the deadline: the package stops working when 30 is retired.
> - **"Pins move always" (#1447) applies to packages that publish at 31.** A package with no 31 version keeps its pin.
> - **Where its core idea is still wanted, a new 31 package** that does it within the format is written. That is content work, outside this area.

The engine records such a package `not migrated: <sentence>`, the first sentence of what stopped it (a hand edit left,
S10-002; a migration refusal; a publish refusal, A11-001 among them), and writes nothing for it.

### Before 31 leaves preview

31 is admitted outside preview (joins `AcceptedSpecVersions`, and the preview setting is removed) only once these
hold. They are the owner's conditions on #1508, word for word.

From [the third comment](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7073314794)
(item 3):

> 3. **Conditions before 31 is admitted outside preview,** recorded in this area's publish notes and the release plan:
>    - the v31 agents (`judgement` 1) are attested in Foundry; today they are attested against git only (D9);
>    - `consultologist-agents` catalogue `v2026.12.2` (`concept_list` with `source`, `judgement`'s newline schema) and the content-repo changes are released from their `m39-build` branches.
>
>    Until then, a 5–30 host does not resolve the 31 catalogue (#1505, ruling 7).

From [the fourth comment](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7082079188):

> **Added to the conditions before 31 is admitted outside preview** (beside [the earlier list](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7073314794)):
> - a dev-deployment run of a 31 job, including a forced replay: the Azure table and blob adapters (insert-if-absent, `row1…` splitting, the 1 MiB check) have only run over in-memory seams;
> - the owner sets `Operators__AlertAddress` and `OperatorWeeklySummarySchedule`, and drains 31 orchestrations before deploying (their history gains `job-slow`, `evaluate-rule-v31` and the delivery step).

From [the further rulings of 2026-11-25](https://github.com/Consultologist/Consultologist-Blazor/issues/1508#issuecomment-7171840295)
(item 3):

> **Before 31 leaves preview,** these same packages also run **live on the dev deployment** with fictional inputs. This joins the release conditions above (the dev run with a forced replay). The full live-model test suite stays Milestone 40 (#1449).

From [the third fix-first group](https://github.com/Consultologist/Consultologist-Blazor/issues/1509#issuecomment-7209167166)
(item 5):

> Add to "Before 31 leaves preview": the live dev run of the edge-case packages checks that replies written line per line render as intended.


**The edge-case packages** (#1508, Task 35). "These same packages" are the six small packages built for the cases the
review of area 10 found, one each: `edge-missing-marker` (a reply that leaves a macro out), `edge-no-branch` (a flow that
takes no branch), `edge-items-not-run` (loop items not run), `edge-unset-object` (an unset optional object input),
`edge-answer-outside-set` (a classifier answer outside its set) and `edge-flag` (a 30 `onInvalid: flag` node); a seventh,
`edge-unruled-whitespace`, shows two white-space differences no ruling covers. Each is
a 30 package under `tests/Fixtures/ByteEquivalence/fixture-packages/edge-*`, with its scenario and capture; its 31
package, as the migration writes it, is `tests/Fixtures/ByteEquivalence/migrated/edge-*/`, and its operator's edits are
`hand-edits/edge-*/` (`edge-missing-marker` and `edge-unruled-whitespace` have them: the question of the classifier `follow_up_applies`,
and `edge-unruled-whitespace`'s `problem_close` instruction). CI runs
each offline with the capture's replies pinned (`tests/Fixtures/ByteEquivalence/edge-tests/`,
`EdgeCasePackagesTests`); the live run asks the model instead.

To publish one on the dev deployment, make its 31 folder and check it first:

1. Copy `migrated/edge-<case>/` to a new folder, and delete its `notices.json` (the migration's notices, not a package
   file). For `edge-missing-marker` and `edge-unruled-whitespace`, lay `hand-edits/edge-<case>/` over it: copy each of its files
   but `README.md` and `options.json` (not package files) in place of the migrated one.
2. Check it offline, as CI would publish it:
   `dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder>`. It must end `would publish`; the
   warnings it prints are part 04 § 10.2's notes. To run the pinned test too, copy `edge-tests/edge-<case>.json` into the
   folder as `tests/edge.json` and add `"edge"` to `tests/index.json`'s `items`: five of them pass;
   `edge-answer-outside-set`'s and `edge-flag`'s pin the reply 31 refuses, so publish stops with S07-009, which is the case.
3. Publish the folder to the dev deployment (the Editor's publish, or `WorkflowPackages/Publish` with the files), start a
   job of it with fictional inputs (Margaret Okafor's draft and problem list, as the scenarios give them; for
   `edge-no-branch`, leave `include_plan` unset; for `edge-unset-object`, leave `intake` unset; for
   `edge-items-not-run`, give three problems; for `edge-unruled-whitespace`, leave `clinic` unset), and read the job's record: the deliverables, the skipped and not-run
   rows, and for `edge-missing-marker` the classifier's answer and the letter's macro row.
