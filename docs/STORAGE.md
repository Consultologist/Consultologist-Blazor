# Durable Functions Storage Setup

## Portal Setup

1. In Azure Portal, create or choose a Storage account.
   - Use a standard general-purpose storage account.
   - Prefer the same region as the Function App.
   - It must support Blob, Queue, and Table storage.

2. Open the storage account:
   - **Security + networking** -> **Access keys**
   - Copy a **Connection string**.

3. Open your Function App:
   - **Settings** -> **Environment variables**
   - Go to **App settings**
   - Add or update:

```text
Name: AzureWebJobsStorage
Value: <storage account connection string>
```

> **Production note (#10):** the deployed app uses the *identity-based* form
> instead of a connection string — `AzureWebJobsStorage__accountName`,
> `__blobServiceUri`, `__queueServiceUri`, `__tableServiceUri`,
> `__credential=managedidentity`, and `__clientId` (the user-assigned
> identity). The identity needs Storage Blob Data Owner, Storage Queue Data
> Contributor, and Storage Table Data Contributor on the account; shared-key
> access is disabled on `consultjobrecscaeast`. The connection-string form
> below remains for local development (Azurite).

4. For non-Flex Consumption Function Apps, also confirm this setting exists:

```text
Name: FUNCTIONS_WORKER_RUNTIME
Value: dotnet-isolated
```

For Flex Consumption Function Apps, do not add `FUNCTIONS_WORKER_RUNTIME`.
Flex stores the runtime in `properties.functionAppConfig.runtime.name` instead.

5. Click **Apply** / **Save**. The Function App will restart.

6. Retry:

```text
POST /api/ConsultGenerationJobs
```

## Optional Dedicated Durable Storage

For a production Durable setup, you can use a dedicated storage account instead
of reusing the host storage account. Add this to `host.json`:

```json
{
  "extensions": {
    "durableTask": {
      "storageProvider": {
        "connectionStringName": "DurableStorage"
      }
    }
  }
}
```

Then add this Function App setting:

```text
Name: DurableStorage
Value: <dedicated storage account connection string>
```

For now, using `AzureWebJobsStorage` is the fastest fix and is supported by
default.

## Where each class of state lives

This file is the Durable Functions plumbing. Which account and container or
table holds a job's inputs, its outputs, the permanent record and the usage
counts — and what deletes each, when — is the design record
[storage separation](customizable-workflow/storage-separation.md) (#545): a
*text* account for what is PHI and short-lived, a *records* account (this
one, `consultjobrecscaeast`) for what is permanent and PHI-free.

## The text account (PHI) — consulttextcaeast (#556)

The [storage separation](customizable-workflow/storage-separation.md) record's
§ 3 text account for Canada East, named by the naming rule
(`consult<role><region>` since the 2026-09-01 rename). It holds the six org/personal text containers
(`org-`/`personal-` × `job-inputs`, `job-outputs`, `form-responses`) and, from
M2, the `ConsultGenerationJobEvents` table. Its axes differ from this records
account on purpose:

- **Shared key off** — identity-only, as everywhere since #10.
- **Blob soft delete, container soft delete and versioning OFF** — a deleted
  blob must be gone; text is the class that is deleted on schedule.
- **Lifecycle policy**: delete blobs 30 days after creation, every container —
  the ceiling of #548, never the rule (the app's sweeps delete earlier).
- **RBAC**: the user-assigned identity gets Storage Blob Data Contributor and
  Storage Table Data Contributor on this account only.
- **Network**: public access as today; this account is first in line for a
  private endpoint (NETWORK_HARDENING.md owns that path).

Driving it by hand (operator steps, on the operator's go — each mutation
confirmed before it runs):

```azurecli
# The user-assigned identity the function app runs as:
CLIENT_ID=$(az functionapp config appsettings list --name canada-east-ai-function --resource-group consultologist_group --query "[?name=='AZURE_CLIENT_ID'].value | [0]" -o tsv)
PRINCIPAL_ID=$(az identity list --resource-group consultologist_group --query "[?clientId=='$CLIENT_ID'].principalId | [0]" -o tsv)

# 1. The account. CLI-created accounts start with soft delete and versioning
#    disabled; the show command below is the proof, not an assumption.
az storage account create --name consulttextcaeast --resource-group consultologist_group   --location canadaeast --sku Standard_LRS --kind StorageV2   --allow-shared-key-access false --min-tls-version TLS1_2 --allow-blob-public-access false

az storage account blob-service-properties show --account-name consulttextcaeast   --resource-group consultologist_group   --query "{softDelete:deleteRetentionPolicy.enabled, containerSoftDelete:containerDeleteRetentionPolicy.enabled, versioning:isVersioningEnabled}"

# 2. The six containers — control-plane creates, so shared-key-off never bites.
for c in org-job-inputs personal-job-inputs org-job-outputs personal-job-outputs org-form-responses personal-form-responses; do
  az storage container-rm create --storage-account consulttextcaeast --resource-group consultologist_group --name "$c"
done

# 3. The 30-day ceiling, every container (the sweeps delete earlier; this
#    deletes what a bug or an outage left behind).
az storage account management-policy create --account-name consulttextcaeast --resource-group consultologist_group --policy '{
  "rules": [ { "enabled": true, "name": "text-30-day-ceiling", "type": "Lifecycle",
    "definition": { "actions": { "baseBlob": { "delete": { "daysAfterCreationGreaterThan": 30 } } },
                    "filters": { "blobTypes": [ "blockBlob" ] } } } ] }'

# 4. The identity's two roles, scoped to this account only.
SCOPE=$(az storage account show --name consulttextcaeast --resource-group consultologist_group --query id -o tsv)
az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal --role "Storage Blob Data Contributor" --scope "$SCOPE"
az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal --role "Storage Table Data Contributor" --scope "$SCOPE"

# 5. The settings (CONFIGURATION.md). Since #596 the two URIs are optional:
#    Storage__Region=caeast derives them; set them only as overrides.
az functionapp config appsettings set --name canada-east-ai-function --resource-group consultologist_group --settings   TextStorage__BlobServiceUri=https://consulttextcaeast.blob.core.windows.net   TextStorage__TableServiceUri=https://consulttextcaeast.table.core.windows.net   TextStorage__credential=managedidentity   "TextStorage__clientId=$CLIENT_ID"
```

The one-shot `AccountKind` back-fill (#556's code half stamps new accounts and
lazily back-fills at sign-in; this recipe covers accounts that have not signed
in since). Read the linked identities, decide the kind — an issuer carrying
the consumers tenant `9188040d-6c67-4c5b-b112-36a304b66dad` is `personal`,
any other is `organisation` — and merge it, idempotently (a stamped kind is
never changed; the account cannot change tenant):

```azurecli
az storage entity query --account-name consultjobrecscaeast --table-name UserIdentityLinks   --auth-mode login --query "items[].{account:PartitionKey, issuer:Issuer}" -o table

# Per account, once:
az storage entity merge --account-name consultjobrecscaeast --table-name AppUsers --auth-mode login   --entity PartitionKey=app-user RowKey=<appUserId> AccountKind=<organisation|personal>
```

Verification, read-only: `az storage container-rm list`, `az storage account
management-policy show`, `az role assignment list --scope "$SCOPE"`,
`az storage account show --query allowSharedKeyAccess`, and `Account/Me`
returning `accountKind` after the back-fill.

## The fork-container move (#602, one-time)

Account forks moved from the records account's single private
`workflow-packages` container into the `org-account-packages` /
`personal-account-packages` pair, chosen by `AccountKind` (the same pairing
as text). Operator steps, on the operator's go:

```azurecli
# 1. The pair — control-plane creates, so shared-key-off never bites. No new
#    RBAC: the identity holds Blob Data Owner at account scope.
for c in org-account-packages personal-account-packages; do
  az storage container-rm create --storage-account consultjobrecscaeast --resource-group consultologist_group --name "$c"
done
```

2. The routing table: `PackageOwners` rows are the authoritative fork set
   (#462 — the name prefix is never trusted alone), joined to
   `AppUsers.AccountKind` (absent → `personal`, the code's own rule).
3. Copy each owned name's `{name}/` blobs into the kind's container —
   download/upload with `--auth-mode login` (shared key is off, so
   server-side copy-batch has no source to authenticate). A blob matching no
   ownership row is reported, not copied. Idempotent: versions are immutable,
   so a re-copy is byte-identical.
4. Deploy the #602 code, delta-check the old container, run the live smoke
   (a fork resolve, a publish, `Operator/PinHealth`), then delete the old
   container: `az storage container-rm delete --storage-account
   consultjobrecscaeast --resource-group consultologist_group --name
   workflow-packages`.

(Executed 2026-09: pre-release, a quiet window; no code-side fallback to the
old container was kept.)

### What lives there since M2 (#557) and M3 (#547)

At completion the engine writes one JSON blob per job — the deliverables
with their text, the block texts, the node concepts — to
`<kind>-job-outputs/{appUserId}/{jobId}.json`, and the record carries the
pointer (`outputsBlob`: container + name, never a URL). The entity then
sheds its four text fields; reads hydrate from the blob, and records from
before the migration keep serving their entity fields. The
`ConsultGenerationJobEvents` table lives here too; rows written before the
move sit on the records account until their jobs purge — the retention
sweep deletes from both tables during the transition (`LegacyJobEventDelete`,
removed once the old table is empty; #558 checked on 2026-08-31 and the
table still held pre-#557 rows for jobs not yet past retention, so the
removal is deferred to a small follow-up once they purge). `DropText` deletes the blob
first, then stamps the record; the 30-day lifecycle policy above is the
backstop for anything a failure leaves behind. `DropText` deletes produced text, concepts and
held inputs; it keeps names and History labels. At specVersion 30 an item named by its
`labelField` carries that field's text (capped at 120 characters) in its name, so that text stays
for the life of the record by design (v30 design § 5, revision 3).

Since M3 (#547) the **held inputs** live here too: the starter writes the
effective input map — exactly what ran, plus each supplied value's typed
wire form for a rerun (#549) — to `<kind>-job-inputs/{appUserId}/{jobId}.json`
before the orchestration is scheduled (a storage failure never refuses a
start; the run proceeds unheld). The record carries `inputsBlob` and,
after the drop, `inputsDroppedAtUtc`; History shows the inputs while held.
Since M4 (#548) the clocks are the account's: `retention.outputDays` for
produced text and a never-longer `retention.inputDays` for held inputs
(both 1..30, default `TextRetention__Days`, clamped on read). A shorter
inputs clock drops the held inputs alone — `DropInputs` deletes the blob
and stamps `inputsDroppedAtUtc`, no instance purge, no events delete;
those run when the outputs clock arrives. v5/v6 jobs are not held.
Since #549 the blob is what the rerun door reads: `POST
ConsultGenerationJobs/{jobId}/Rerun` rebuilds the request from the
Supplied half under the record's exact package ref and starts a new job
whose every effective input carries a `rerun` origin naming the source.
Since #582 the door also captures the source's hashes as a baseline,
and the rerun's completion stamps `rerunVerdict` — pass/fail over the
package's own reproducible claims, judged from two records and nothing
else. Since #546 every start also inverts its origins into the
`ConsultGenerationLinks` table on the records account (PK the source
job, ids only, never deleted while the account exists) — the "used by"
side of History's lineage; account closure (#559) is its one deleter.

### The record of a specVersion 31 job (#1506, record storage 12)

A 31 job's record is not in the entity (design: [part 08](customizable-workflow/package-format-v31/08-record.md) § 1.1).
It is in two places, and nothing in it holds a value from an input, a model or a profile except the retained text,
which is only ever in blobs:

- **The table `ConsultGenerationRecordRows`**, on the records account (the `ConsultGenerationRecordStorage` section if
  set, else `AccountStorage`, as the other job tables; created on a 31 job's first write). One row per record row:
  partition key the job id, row key the record key escaped for Azure Tables (an escaped key past 512 UTF-16 characters is `~`
  and its SHA-256 hex), and the properties `key` (the true key), `section` (`node`, `check`, `deliverable`, `delivery` or
  `decision`), `row` (the row's canonical JSON, continued in `row1`, `row2`… past 30,000 characters) and `hash`. A row is
  written once (insert-if-absent) and never rewritten. Holds hashes, ids, counts, reasons and package text only. A row
  larger than a table row holds (1 MiB, in UTF-16) is not written, and its job fails with A09-002 naming its key. The
  job index row of a storage-12 job carries `RecordStorage` (12), which the sweep and closure read instead of the
  job's entity; a 5–30 job's index row has no such property.
- **The blobs**, in `<kind>-job-outputs` beside the job's output blob, under `{appUserId}/{jobId}/`:
  - `text/{sha256 hex of the row key}.produced.json` and `….settled.json`: the retained text (replies, prompts as sent,
    renderer inputs and outputs, composed deliverables, item names from inputs or models, a `check_result`'s lists,
    run-spot and substitution text);
  - `record.json`: the canonical record document, sealed at the job's end with `recordHash` stamped on the entity.

**What deletes what.** The 7-day text sweep seals the job if it is not yet sealed, then deletes the `text/` blobs
(`DropText` follows), and rewrites no row, so `recordHash` holds after the drop; a job it cannot seal keeps its text for
the next pass. It never deletes `record.json` or a row. The 30-day lifecycle policy above is a blunt ceiling over every
blob of the container, `record.json` included, so it removes the sealed document at 30 days. The document is a
materialisation (decided in the build, #1506): the next read (`GET …/Record`) assembles it again from the rows and the
header, checks it against the stamped `recordHash`, and writes it back only if it matches (a mismatch is an engine
fault, logged with both hashes, and nothing is written). What must outlive the policy is the table's rows and the
entity's stamp, and both do: neither is in the text account. Account closure (#559)
deletes every row of every job of the account (`DeleteJobAsync`, after the job's events) and, with the account's
`{appUserId}/` prefix, every blob. A 5–30 job has none of this.

## After Setup

In the storage account, Durable Functions will create runtime artifacts such as
queues, tables, and blobs for orchestration history, control messages, entities,
and leases. You usually do not create these manually.
