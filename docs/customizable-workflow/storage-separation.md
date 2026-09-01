# Storage separation — the classes of state, their stores, and what deletes them

**Status: design record for Milestone 22 (settled 2026-08-30), implementation
tracked by the migration order in § 7.** Five open issues add server-side
state — held form responses (#539), stored inputs (#547), per-account
retention (#548), tokens (#551), a usage table (#552), a links index (#546)
— and today every class of state the app keeps sits wherever it first
landed: produced text inside Durable entity state and orchestration
history, ten application tables on one account, generated prose in the
SSE resume table, and no document naming any table's key scheme or its
deleter. This record names the classes, chooses a store for each, fixes
retention and residency per class, and orders the moves so that the
issues above land on the chosen stores rather than on entity state first.

Decisions taken with the operator: **a job's text lives in private blobs,
one class per container**, and the entity keeps hashes and pointers —
deletion is a blob delete, a storage lifecycle policy backs the sweep, and
PHI leaves Durable state; **two private storage accounts per region**, a
*text* account for what is PHI and short-lived and a *records* account for
what is permanent and PHI-free; **enterprise and personal accounts kept
apart** — every text container comes as a pair, one for organisation
sign-ins and one for personal Microsoft accounts (§ 2.5); one store set
per location (#515), the public registries shared; identity-only access
everywhere, as since M11.

## 1. What exists

### 1.1 The accounts (as listed on 2026-08-30)

| Account | Region | Shared key | Holds |
|---|---|---|---|
| `consultologistjobqueue` | canadaeast | off | the ten application tables (§ 1.2), the private `workflow-packages` container — **and** a stale copy of the `consultologistjobs` hub tables, frozen at the 2026-07-22 identity move (§ 7, M6; the #558 recon proved it dead: its Instances stop that day) |
| `consultologistgroup8cbf` | canadaeast | off | the function host's own account (`azure-webjobs-*`, the deployed package), the **live** Durable task hub `consultologistjobs` — it rides `AzureWebJobsStorage`, which points here (`AzureWebJobsStorage__accountName`), Instances current through the present — and the stale `canadaeastaifunction` hub tables (empty; § 7, M6) |
| `consultologistpublic` | canadaeast | off | the public registries only: `workflow-packages`, `output-contracts`, `agent-definitions`, `package-format`, `provenance` — anonymous read by design |
| six others (`train…`, `tutorial…`, `consultologist0733…`/`9579…`, `canadaeastlangstorage`) | eastus2 / canadacentral | three at the default (on) | Azure ML, training and tutorial residue — **not app stores**; named here so "identity-only" is read as a claim about the app's accounts, not the resource group (§ 7, M6) |

No storage account name appears in `src/` — `tests/PublicByDesignTests.cs`
enforces it; every store is a `…ServiceUri` setting chaining to
`AccountStorage`, built by one `DefaultAzureCredential` with the
user-assigned identity (`src/Consultologist.Api/Program.cs:30-64`), one
table factory (`src/Consultologist.Api/StorageTables.cs`) and one blob
factory (`src/Consultologist.Api/Workflow/WorkflowPackageBlobContainerFactory.cs`).
The identity's roles and the shared-key posture are in
`docs/STORAGE.md` (24–31) and `docs/CONFIGURATION.md` (394–416).

### 1.2 The tables

| Table | Key (PK / RK) | Contents | PHI | Deleter today | Source |
|---|---|---|---|---|---|
| `AppUsers` | `app-user` / appUserId | display name, email, status, timestamps | PII | none | `Auth/AccountStore.cs:48` |
| `IdentityLinks` | provider / subject hash | issuer, subject, claims | PII | the user's unlink (#195) | `Auth/AccountStore.cs:49` |
| `UserIdentityLinks` | appUserId / provider-hash | the reverse index | PII | the user's unlink | `Auth/AccountStore.cs:50` |
| `AccountSettings` | appUserId / key | preferences; `delivery.documentPassword` (secret, write-only); the address keys | PII + one secret | the user's DELETE | `Auth/AccountSettingsStore.cs:17` |
| `AccountRateLimits` | appUserId / UTC hour | a count | no | the sweep, windows past 92 days — the read clamp (#558, closing #266's deferral) | `RateLimiting/AccountRateLimiter.cs:80` |
| `LinkedInLinkStates` | `linkedin-state` / token | nonce, return origin, expiry (10 min) | no | single-use take; the sweep deletes expired rows once per run (#558) | `Auth/LinkedInLinkStateStore.cs:25` |
| `PackageOwners` | appUserId / package name | a timestamp | no | none | `Workflow/WorkflowPackageOwnership.cs:53` |
| `ConsultGenerationJobIndex` | appUserId / reverse-ticks_jobId | status, counts, timestamps, `Source`, `TextDroppedAtUtc`, delivery | no | **none — the permanent pointer** | `Jobs/ConsultGenerationJobIndexStore.cs:29` |
| `ConsultGenerationJobEvents` | jobId / sequence (+ dedupe, counter rows) | `PayloadJson`: streamed snapshots **with the generated prose** | **yes** | the retention sweep, whole partition | `Jobs/ConsultGenerationJobEventStore.cs:32` |
| `EmailIntakeProcessed` | `email-intake` / hash of message id | message id, from address, outcome, job id — never subject or body | PII | only a `queued` claim's release | `Email/EmailIntakeClaimStore.cs:78` |

### 1.3 Where text is today

- **The orchestration payload** — `Request.Inputs`, the effective input
  map, every rendered prompt and raw model response in the history table,
  spilling to the hub's `-largemessages` blobs past the inline limit.
  Purged whole by `PurgeInstanceAsync` in the sweep
  (`Jobs/TextRetentionSweep.cs:34-40`). Uploaded bytes never get here:
  `InputFiles` is cleared before the payload is built
  (`Jobs/ConsultGenerationJobStarter.cs:1188-1199`, `docs/DOCUMENT_INPUT.md` § 5).
- **The entity** `ConsultGenerationJobState` — four text fields nulled in
  place by `DropText` (`Jobs/ConsultGenerationState.cs:422-452`):
  `AssembledDocument`, `AssembledDocuments[].Text`, `Blocks[].GeneratedText`,
  `NodeOutputs[].Concepts`. Everything else on the entity is the
  permanent record: hashes, nodes, rosters, refs, labels, `ApiHost`,
  `EngineCommit`, the origins with their digests, the history events,
  delivery, `TextDroppedAtUtc`.
- **The events table** — `PayloadJson`, deleted with the sweep; a job
  nobody streamed has no rows (`docs/CONFIGURATION.md` 325–327).

The sweep (`TextRetentionSweep.cs`, #368) is one clock, `TextRetention__Days`
(7), applied to every terminal job: signal `DropText`, purge the
orchestration, delete the events partition — entity first, so an
interrupted sweep leaves a record that already says the text is gone.
`docs/ASYNC_DELIVERY.md` § retention states the policy; this record
changes where the text is, not the policy.

### 1.4 What never reaches the server

The per-tab memento of inputs as typed (`Services/AI/ConsultJobSession.cs`),
an attached document's bytes and extracted preview (`Consults.razor`,
`AttachedDocument`), a loaded deliverable's preview (`LoadedDeliverable`),
the device's location choice (`api-location`) and the editor's drafts in
localStorage. None is a store this record governs; each is named so that
"the server holds it" is never assumed of them.

## 2. Four classes

The record keeps to four classes, and says of each: what it holds, which
account and container or table, the key, who deletes it and when, what a
purge leaves behind, and its residency.

| Class | Holds | Account | Store | Key | Retention | Deleter |
|---|---|---|---|---|---|---|
| **Inputs** | the effective input map (documents already extracted, references already copied, form values already coerced); held form responses | text | `org-job-inputs` / `personal-job-inputs`; `org-form-responses` / `personal-form-responses` (§ 2.5) | `{appUserId}/{jobId}.json`; `{appUserId}/{formId}-{responseId}.json` | the account's `retention.inputDays` (#548), default `TextRetention__Days`, ≤ outputs, ≤ 30 | the sweep (blob delete + `inputsDroppedAtUtc`); lifecycle policy at 30 days as the backstop |
| **Outputs** | assembled deliverables, node outputs and concepts, the SSE snapshots | text | `org-job-outputs` / `personal-job-outputs`; `ConsultGenerationJobEvents` table (moved) | `{appUserId}/{jobId}.json`; jobId partition | `retention.outputDays`, default `TextRetention__Days`, ≤ 30 | the sweep (blob delete + `textDroppedAtUtc`, events partition delete); same backstop |
| **History / record** | the entity minus its text; the index row; the links index (#546); the origins with digests | records | Durable entity state; `ConsultGenerationJobIndex`; `ConsultGenerationLinks` table | jobId; appUserId / reverse-ticks_jobId; sourceJobId / consumerJobId | **never** while the account exists | account closure only (§ 2.6) — hashes are stamped at completion and outlive everything else |
| **Packages** (account forks) | `acct-*` versions: manifest and files, immutable | records | `org-account-packages` / `personal-account-packages` (§ 2.5); `PackageOwners` | `{name}/{version}/…`, `{name}/latest.json`; appUserId / name | **never** while the account exists | account closure only (§ 2.6) |
| **Usage** | per-account, per-day counts (#552): consults completed, input and output tokens; per-run numbers on the record (#551) | records | `AccountUsage` table | appUserId / `yyyy-MM-dd` | the read window — days past 92 go (#558); still the store that survives every text purge | the sweep drops days past the read clamp; numbers only, derived at completion, **never re-derived from records** |

The account, identity, settings, claim and ownership tables of § 1.2 stay
on the records account, and their deleters above are now the written
rule: a user's unlink and DELETE, a claim's release, and — built by #558 —
the sweep's cleanup for `AccountRateLimits` windows, `AccountUsage` days
and abandoned `LinkedInLinkStates`.

### 2.1 Inputs

What #547 stores is the **effective** map — exactly what ran — as one JSON
blob per job, written by the starter after extraction and resolution,
before the orchestration is scheduled. The orchestration payload still
carries the map (the engine reads it there), but the payload is purged
with the instance as today; the blob is the copy that History shows and
the rerun door (#549, built) reads — the Supplied half back through its
typed wire form, reproducing the source's `effectiveInputHash` by
construction. Held form responses (#539, **built**) are inputs before any job:
one blob per response, the same container family, the same clock from
`submittedAtUtc`, and a row in the records account's `FormResponses`
table for the list (form id, response id, submitted, input ids present,
deleted-on) — the row never holds a value.

### 2.2 Outputs

At completion the engine writes one JSON blob per job — the deliverables
with their text, the node outputs with their concepts — and the entity
keeps what it keeps today minus the four text fields. The events table
moves to the text account unchanged in shape; its partitions are deleted
with the job as now. `DropText` becomes: delete the two blobs, delete the
events partition, stamp `textDroppedAtUtc` (and `inputsDroppedAtUtc` when
the input clock is the one that fired), append the `retention` history
event, upsert the index row — the order unchanged: record first.

### 2.3 History / record

Nothing new is stored; two things are added to what is: the blob pointers
(`inputsBlob`, `outputsBlob` — a container and a name, not a URL, so a
store can move without rewriting records) and the second dropped-at
state. The provenance registry's "three kinds of field"
(`provenance.md` 34–54) is untouched: the pointers are operational
snapshots. The links index (#546, built) is a records-account table keyed
by the source job, ids only, never deleted. Its row key refines the
consumer id with the slot and element for a previous-run copy
(`{consumerJobId}_{inputId}_{i}` — one consumer may copy twice from one
source) and is the bare consumer id for a rerun — one row per replay,
never one per slot origin. Written best-effort at start (a storage blip
never refuses the run; the consumer's own origins remain the truth this
index projects), which also means edges from runs started before #546
are absent, not back-filled.

### 2.4 Usage

A fourth thing, deliberately outside the provenance record: a **derived
store**. Written once at completion from the numbers the record carries
(#551, built), read by the profile (#552, **built**: the `AccountUsage`
table incremented under the ETag at FinalizeJob with an on-state stamp
guarding the reply-leg re-finalize, and `GET Account/Usage` serving the
day rows) and the admin page (#553, **built**: `GET Operator/Usage` behind the allowlist, per-user totals grouped by issuer tenant on /operators), never
recomputed from job records — which the sweep may purge and which a
region may one day retire. `AccountRateLimits` is its older cousin: one
row per account per hour, the same shape of store, and the cleanup rule
(§ 7 M6, built by #558) applies to both — rows older than the longest
window anyone reads go. That window is 92 days
(`Account.MaxUsageWindowDays`, the #552 read clamp): the cleanup horizon
derives from the constant the reads are clamped to, so the two can never
drift.

### 2.5 Enterprise and personal accounts, apart

An account signs in either with an **organisation** tenant or with a
**personal Microsoft account** (the consumers tenant
`9188040d-6c67-4c5b-b112-36a304b66dad`; `docs/ACCOUNTS.md`, and the rule
#517 draws in `DeliveryAddress.IsOrganisation`). The two populations
differ in who is accountable for the data, what an organisation may ask
for (its own retention, its own hardening, one day its own store set),
and what a personal user expects. Their text is therefore never in one
container: **every text container is a pair**, `org-…` and `personal-…`,
same layout, same clocks, separate lifecycle rules and separate access
policies if ever needed — `org-job-inputs`, `personal-job-inputs`,
`org-job-outputs`, `personal-job-outputs`, `org-form-responses`,
`personal-form-responses`. The events table stays one table (rows are
per job, deleted per job); the records account's tables stay shared —
every row is already partitioned by the account.

The kind is a property of the **account**, decided once: `AppUsers`
gains `AccountKind` (`organisation` | `personal`), stamped at creation
from the first Entra identity's issuer tenant, and for existing accounts
derived once from their linked Entra identity (an account with none —
LinkedIn-only is impossible; Entra is the sign-in — cannot exist). The
kind is on `Account/Me` as `signInKind` already is for the *token*; the
two agree by construction for a single-identity account, and the account's
kind wins for the store. A blob pointer on the record names the container,
so the kind is readable from any record without a lookup. Moving an
account between kinds is not a feature: it would be a migration of every
held blob, and there is no path by which an account changes tenant.

Why containers, not accounts: the two populations need different
*policies* on the same *class* of data, and a container is the unit those
are set on within one region and one class; a third and fourth account per
region would double the operator surface for no separation the container
does not already give. An organisation that later needs its own store set
is a new location in #515's sense, not a new container.

### 2.6 Account closure — the one deleter of what is never deleted

Three classes above say *never*: the record, the links, and — added here —
the account's package forks. Each is never deleted **while the account
exists**. The one thing that deletes them is **account closure** (#559):
an operator action, on the existing allowlist, that removes everything an
account owns in an order that leaves no orphan still resolving — text,
then packages (every version of every `acct-*` fork, and the ownership
rows), then records and links, then the index, settings, identities, and
the `AppUsers` row. What stays: the usage counts (numbers), the rate-limit
rows, the email claim ledger (message ids and outcomes), and one closure
row as the audit of the act. The provenance registry's promise that
*account versions are never deleted — provenance refs stay resolvable
forever* is thereby narrowed to *for as long as the account exists*; a
record that named a closed account's package is gone with the account,
so no ref dangles. Public packages are never closed: they belong to the
repository, not to an account.

Account forks therefore get the same org/personal pairing as text
(§ 2.5): `org-account-packages` and `personal-account-packages` on the
records account, the writer and the reader choosing by `AccountKind`; the
public container on `consultologistpublic` is untouched. Nothing in the
package format changes — a container name in the writer and the reader.

## 3. Two accounts per region

Named from the host rule (`docs/CONFIGURATION.md` 142–152): the region
part of the host, then the class — `consultologist<region>text` and
`consultologist<region>records`, e.g. `consultologisteastcatext`. For
Canada East the **records** account is the existing
`consultologistjobqueue`, renamed only in the documents (a storage
account cannot be renamed; the name is a setting value, and the tests
already forbid it in code). The **text** account is new (§ 7 M1).

| | text account | records account |
|---|---|---|
| Holds | the six text containers of § 2.5 (`org-`/`personal-` × `job-inputs`, `job-outputs`, `form-responses`); `ConsultGenerationJobEvents` | the Durable hub; the account, settings, claim, ownership, index, links, usage and closure tables; the account forks in `org-account-packages` / `personal-account-packages` (§ 2.6; today one `workflow-packages`) |
| Shared key | off | off |
| Identity's roles | Storage Blob Data Contributor, Storage Table Data Contributor | as today: Blob Data Owner, Queue Data Contributor, Table Data Contributor |
| Lifecycle policy | delete blobs 30 days after creation, every container — the ceiling of #548, never the rule | none |
| Soft delete / versioning | **off** — a deleted blob must be gone | default |
| Network | first in line for a private endpoint (`docs/NETWORK_HARDENING.md`); until then, public access as today | as today |
| Settings | `TextStorage__BlobServiceUri`, `TextStorage__TableServiceUri`, `__credential=managedidentity`, `__clientId` | `AccountStorage__TableServiceUri`, `WorkflowPackages__BlobServiceUri`, `AzureWebJobsStorage__*` as today |

Why two rather than one with containers: the two classes differ in every
axis an account controls — retention, lifecycle, soft delete, network
hardening, blast radius of a credential — and an account is the unit those
are set on. Why not three or more: the records account's contents are all
permanent and PHI-free; splitting them buys nothing.

## 4. Residency

One store set per location. `Public__ApiHost` names the region on every
record (#514); the two accounts' URIs are that deployment's settings; a
second region (`east.us.api…`) gets `consultologisteastustext` and
`…records`, its own Durable hub, and its own function app. Nothing moves
data between regions: `consult.location` is the device's choice of *which*
API to call (#515), and an account that calls two regions has two accounts
of state, each where it was made. Shared across regions: the public
registries (`consultologistpublic`) and the terminology server — neither
holds patient data.

## 5. What a purge leaves behind

| Class | Before the sweep | After |
|---|---|---|
| Inputs | `<kind>-job-inputs/{a}/{j}.json`; the orchestration payload | blob gone; payload purged; `inputsDroppedAtUtc` on the record; the effective-input hash and the origins' digests stay |
| Outputs | `<kind>-job-outputs/{a}/{j}.json`; the events partition; the entity's text fields (until M2 lands) | blob and partition gone; `textDroppedAtUtc`; `documentHash`, `workflowOutputHash`, every node's `outputHash` stay |
| Record | the entity, the index row, the links | untouched, plus the `retention` history event |
| Usage | the day's row | untouched |

The rule of #368 stands: hashes attest what was produced and can no longer
be checked against it once the text is gone; the record says when.

## 6. What this record does not do

- Private endpoints themselves — `docs/NETWORK_HARDENING.md` owns them;
  this record only says which account goes first.
- A second region's creation — the naming and the settings are here; the
  deployment is #515's follow-up.
- Moving the Durable hub — the live hub follows `AzureWebJobsStorage` to
  the host account `consultologistgroup8cbf` (the #558 recon corrected an
  earlier version of this record that placed it on
  `consultologistjobqueue`); the hub holds no text once M2 and M3 land
  (the payload still carries inputs until purged; `docs/STORAGE.md` 51–76
  has the dedicated-account form if ever wanted).
- Encryption keys — platform-managed keys stay.

## 7. Migration order

| Step | Builds | Issue |
|---|---|---|
| M1 | The Canada East text account: create, shared key off, soft delete off, the six containers with their lifecycle policy, RBAC on the user-assigned identity, the `TextStorage__*` settings — operator steps, on the operator's go; and `AccountKind` on `AppUsers`, stamped at creation and back-filled once | to file (M22) |
| M2 | Outputs to blobs: write `job-outputs` at completion beside the entity fields; read from the blob; then stop writing text on the entity; `DropText` as § 2.2; the events table on the text account | to file (M22) |
| M3 | Inputs on `job-inputs`; held form responses on `form-responses` | #547 built; #539 built |
| M4 | Per-account retention drives the sweep over both classes | #548 |
| M5 | `AccountUsage` on the records account, after the numbers exist — **built** | #551 → #552, both built |
| M6 | Cleanup: the stale hub artifacts (`canadaeastaifunction` on the host account; the dead `consultologistjobs` copy on `consultologistjobqueue` — § 1.1); the AML/training accounts (shared key off, or deleted); the `AccountRateLimits` / `AccountUsage` / `LinkedInLinkStates` cleanup rule — **code half built** (the sweep's legs); the deletions are operator steps on the operator's go | #558 |
| M7 | Account forks into `org-`/`personal-account-packages` (writer and reader by `AccountKind`; the existing `workflow-packages` forks moved once); account closure (§ 2.6) | #559 (after #556) |

M2 before M3 so the read path, the pointer fields and the new `DropText`
exist before inputs — the class with the shorter clock — arrive. #546's
links table and #539's list table go on the records account as designed;
their bodies are amended to say so.
