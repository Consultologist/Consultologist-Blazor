# Configuration Reference

Environment variables (Function App app settings) read by `Consultologist.Api`, and the
frontend's configuration keys. Inventoried from the code on 2026-07-10.

Note on naming: `__` (double underscore) is .NET's hierarchy separator, so
`Auth__Authority` surfaces as `Auth:Authority` to `IConfiguration`. Settings read
through `IConfiguration` accept either form; settings read directly via
`Environment.GetEnvironmentVariable` (the `AzureAI__*`, `AgentAttestation__*`, and
`WorkflowPackages__Default` groups) must use the exact `__` name.

## Authentication (`Auth/BearerTokenValidator.cs`)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `Auth__Authority` | Entra authority URL. Production uses `https://login.microsoftonline.com/common/v2.0` (since 2026-07-23, #132 — includes personal Microsoft accounts); `…/organizations/v2.0` restricts to work/school tenants; a tenanted `…/<tenant-id>/v2.0` locks sign-in to one tenant. Issuer validation adapts automatically: the validator accepts whatever the authority's OIDC metadata declares, including the `{tenantid}` template the `common`/`organizations` endpoints publish | — | yes (startup throws without it) |
| `Auth__Audience` | Expected token audience, e.g. `api://<client-id>` | — | yes |
| `Auth__RequiredScope` | Scope name callers must carry, e.g. `access_as_user` | none (scope check skipped) | no |

### Multi-tenant sign-in (2026-07-18; personal accounts 2026-07-23)

Sign-in accepts any Microsoft Entra organizational tenant and, since #132,
personal Microsoft accounts. Three settings make that true, and only one of
them lives in this repo's deployment surface:

1. **`signInAudience` = `AzureADandPersonalMicrosoftAccount`** on **both**
   app registrations (the SPA and the API); `AzureADMultipleOrgs` is the
   work/school-only variant used 2026-07-18 → 2026-07-23. Portal: App
   registrations → *Authentication* → "Supported account types" (also
   visible in the registration's Manifest). Two MSA-specific constraints:
   Entra refuses the widening unless the registration's
   `api.requestedAccessTokenVersion` is `2` (the SPA registration had it
   unset and needed a Graph PATCH first), and identifier URIs must be the
   `api://<client-id>` form (already the case). The API being single-tenant
   is the classic failure: a foreign tenant has no service principal for it
   and sign-in dies with **AADSTS500011** ("resource principal not found") —
   a consent/provisioning problem, never a credential one.
2. **`api.knownClientApplications` = `[<spa-client-id>]`** on the **API**
   registration, so a foreign tenant consents to the SPA and the API in one
   combined prompt. Manifest-only — there is no portal blade for it. Do not
   confuse it with the *Expose an API* blade's "Authorized client
   applications" (`preAuthorizedApplications`), which *skips* consent rather
   than bundling it.
3. **`Auth__Authority`** on the Function App set to the `common` authority
   (table above), and the client's `AzureAd:Authority`
   (`src/Consultologist.Web/wwwroot/appsettings.json`) set to
   `https://login.microsoftonline.com/common` — MSAL takes the authority
   without `/v2.0`; the API's OIDC-metadata URL takes it with. Locally the
   Function value comes from `local.settings.json` (gitignored).

What a foreign tenant still needs: consent. Where user consent is allowed,
the first sign-in shows the combined prompt and provisions both service
principals; tenants that gate consent need their admin to visit
`https://login.microsoftonline.com/<their-tenant-id>/adminconsent?client_id=<spa-client-id>`.
Personal Microsoft accounts have no tenant admin — the user always consents
for themselves, which is exactly why this path exists as the fallback for
users whose IT departments block consent (#132).

CSP note: MSA sign-in can bounce through `login.live.com`, but only via
top-level navigation (redirect login mode), which CSP does not restrict —
`staticwebapp.config.json` needs no new origins. Token endpoints and silent
renewal stay on `login.microsoftonline.com`, already in `connect-src`.

Credential posture is unchanged by any of this: the SPA is a public client
(authorization code + PKCE — client secrets/certificates are never used and
none exist), and the API only validates incoming tokens, reaching storage via
the user-assigned managed identity. Neither registration carries a
credential, so there is nothing to expire or rotate.

A first sign-in from a foreign tenant creates an app account like any other,
and it lands **`Pending`** (since #191) — the activation flip in the
`AppUsers` table is the admission control for self-provisioned sign-ups (see
"Account Statuses and Activation" in `docs/ACCOUNTS.md` for the runbook).

## LinkedIn identity linking (`Auth/LinkedInLink*`, #133)

LinkedIn is a **verification signal**, never a credential: the Connect
LinkedIn flow on the Profile page proves the signed-in user controls a real
LinkedIn identity and stores it (with name/email/picture claims) on the app
account as an input to the manual activation decision. Only the id_token is
consumed — the flow never calls LinkedIn APIs on the user's behalf.

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `LinkedIn__ClientId` | Client ID of the LinkedIn Developer app (Sign In with LinkedIn using OpenID Connect product) | — | yes (Start endpoint and validator throw without it) |
| `LinkedIn__ClientSecret` | The LinkedIn app's client secret — a genuine secret app setting (no managed-identity equivalent exists) | — | yes (token exchange throws without it) |
| `LinkedIn__RedirectUri` | The callback URL, byte-for-byte equal to one registered in the LinkedIn app. Production: `https://east.ca.api.consultologist.ai/api/Account/LinkedIn/Callback`; local: `http://localhost:7071/api/Account/LinkedIn/Callback` | — | yes |
| `LinkedIn__StateTtlMinutes` | Minutes an OAuth state stays valid | `10` | no |
| `LinkedIn__ApiVersion` | `LinkedIn-Version` header for the Verified on LinkedIn `verificationReport` call (see LinkedIn's release notes for the current value) | `202510` | no |
| `LinkedInStateStorage__TableServiceUri` | Table service URI for the single-use `LinkedInLinkStates` table; chains to `AccountStorage` when unset (the usual case) | falls through to `AccountStorage` | no |

The authorize request asks for `openid profile email r_verify` — the last
scope comes from the **Verified on LinkedIn** product attached to the
LinkedIn app and lets the callback fetch the member's verification
categories (IDENTITY/WORKPLACE), stored on the link. The fetch is
best-effort: on the **Development tier** LinkedIn returns 403 for members
who are not admins of the developer app, so linking still succeeds and the
categories simply stay empty. Apply for the **Lite tier** before external
users if their categories should populate.

State rows are single-use (ETag-conditioned delete — a replayed callback
gets a 400) and expire after the TTL; Azure Tables has no native TTL, so
abandoned rows are deleted opportunistically when touched and are otherwise
harmless. The callback's redirect-back origin is captured at Start from the
browser's `Origin` header validated against the CORS allow-list — never
from a client-supplied value.

## Email consult intake (`Email/*`, #158)

Submit consults by email: a timer polls the dedicated shared mailbox via
Microsoft Graph (the managed identity's `Mail.ReadWrite`/`Mail.Send` roles
are restricted to that one mailbox by an Exchange application access policy
— see `docs/ACCOUNTS.md`), matches the sender to a registered account,
starts a normal consult job from the email body and any `.txt`/`.md`
attachments (`source: email`), and replies on completion with a no-PHI
`/history/{jobId}` deep link.

Attachments fill the pinned package's declared input slots (#210): a
filename stem claims the slot it names (outranking the body for it),
the body takes `consult_draft` if still free, and one leftover file
fills one leftover slot. Caps are code constants, not settings — 256 KB
per body, and since #237 10 MB per attachment with 20 MB across all of
them, matching the app door.

Email no longer reads attachments (#237): their bytes go to the job
start and the parser reads them there, so there is no list of accepted
types here — whatever the parser reads, email accepts. A file the parser
cannot read, one over the cap, an assignment too ambiguous to make
without guessing, or an attachment sent to a package that declares no
inputs, all reject the message with the `rejected-attachments` claim
outcome, and the reply names which of those it was.

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `EmailIntake__MailboxAddress` | The intake mailbox (`consults@consultologist.ai`). **Unset = intake off**: the poller no-ops quietly (local dev, CI) | — | prod only |
| `EmailIntakePollSchedule` | NCRONTAB expression for the poll timer (`0 */2 * * * *`). **Deliberately flat-named**: `%…%` timer binding expressions resolve literal config keys, and the environment provider normalizes `__` names to `:` form, so a grouped name can never resolve there. When unset, only `EmailIntakePoll` fails indexing and is disabled — the rest of the host runs. Locally in `local.settings.json` (gitignored) | — | yes wherever the poller should run |
| `EmailIntake__AppBaseUrl` | SPA origin for reply deep links (`https://app.consultologist.ai`) — the server cannot derive it | — | prod only (replies skipped with a warning when unset) |
| `EmailIntake__MaxMessagesPerPoll` | Per-tick message cap; excess waits for the next tick | `25` | no |
| `EmailIntakeStorage__TableServiceUri` | Table endpoint for the `EmailIntakeProcessed` claim table; chains to `AccountStorage` when unset (the usual case) | falls through to `AccountStorage` | no |

Posture, in brief:

- **Authentication floor**: authenticated intra-tenant submission
  (`X-MS-Exchange-Organization-AuthAs: Internal` — unforgeable from outside;
  EOP strips that header family from external mail, and intra-tenant mail
  carries no SPF/DKIM/DMARC stamps at all), OR the first
  `Authentication-Results` header (our Exchange hop's) showing `dmarc=pass`
  or `spf=pass` **and** `dkim=pass`.
- **Sender gate**: the From address must match **exactly one** account and
  it must be `Active` — emails come from token claims and are not unique,
  so ambiguity is a rejection, never a guess. (A partition scan today; an
  `EmailIndex` table is the follow-up if account counts grow.)
- **Silent rejection**: unmatched, inactive, unauthenticated, or empty
  messages are moved to `Inbox/Rejected` and logged — no bounce, no
  backscatter. Accepted messages move to `Inbox/Processed`.
- **PHI at rest**: the mailbox holds referral emails, so an Exchange
  retention policy is applied (#201, 2026-07-25): the "Consults Intake
  Retention" policy with a default 30-day **permanently-delete** tag,
  covering every folder (Inbox, Processed, Rejected, Sent). The job
  record is the canonical copy of accepted drafts; mailbox copies exist
  only for a debugging window. Managed via ExchangeOnlineManagement
  PowerShell (`Get-Mailbox consults@… | fl RetentionPolicy` to verify).
- **Exactly-once**: a claim row (`EmailIntakeProcessed`, keyed by
  internetMessageId) is written atomically BEFORE the job starts — a
  message can never start two jobs; a crash between claim and start drops
  that message with a warning (at-most-once by design for PHI jobs).
- **Replies carry no PHI**: fixed subjects, boilerplate + deep link only;
  the inbound subject is never echoed.

## Azure AI Foundry agent (`Agents/AgentSectionGenerator.cs`)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `AzureAI__Endpoint` | Foundry project endpoint, e.g. `https://<resource>.services.ai.azure.com/api/projects/<project>` | — | yes (agent calls throw without it) |
| `AzureAI__ApiVersion` | Foundry agents API version | `v1` | no |
| `AzureAI__NetworkTimeoutSeconds` | Non-negative integer (seconds) | `270` | no |
| `AzureAI__MaxRetries` | Non-negative integer (SDK-internal retries per call; durable retries stack on top) | `0` | no |
| `AZURE_CLIENT_ID` | Client id (GUID) of the user-assigned managed identity | — | yes when running in Azure (detected via `WEBSITE_INSTANCE_ID`) |

Agent name/version pins are **not** app settings: they live in the bundled,
git-tracked output-contract catalog (`agents/output-contracts.json`), keyed by output
contract id (`text` = prose default, `concept-list` = structured concepts, schema at
`agents/schemas/concept-list.json`). Selection is catalog-driven per node; every
entry's agent version is recorded in per-job provenance (`agentVersions` map). The
former `AzureAI__AgentName`/`AgentVersion`/`ConceptAgentName`/`ConceptAgentVersion`
settings are retired — delete them from the Function App.

## Agent attestation (`Agents/AgentAttestationService.cs`)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `AgentAttestation__Enforce` | `true` (case-insensitive) = drift fails host startup; any other value = drift logs an error only. Since #16 the production baseline is the registry's published definition (CI-only channel); the submodule-pinned copy (`external/consultologist-agents/agents`, bundled into the build output) is the baseline only in local dev | warn-only | no |
| `TextRetentionSweepSchedule` | NCRONTAB for the retention sweep (#368), e.g. `0 0 3 * * *` (daily 03:00 UTC). Flat name — a `%…%` binding cannot resolve a `__` key. Unset: the sweep function is disabled and **no text is ever deleted** | unset — set it in every environment that holds real jobs | yes, in production |
| `TextRetention__Days` | Days after a job completes (or fails, or is cancelled) before its produced text — the assembled documents, sections and extracted concepts — is deleted from the record, the Durable orchestration history and the streamed-event rows. Provenance stays. The policy: PHI at rest for this long and no longer | 7 | no |
| `Terminology__InfoUrl` | The terminology server's attestation endpoint (`https://mcp.snomed.consultologist.ai/api/Public/Terminology`, #403). Read once per cache window; the edition and the server build it reports are stamped on every job record. Unset: nothing is recorded | unset (nothing recorded) | no |
| `Terminology__CacheMinutes` | How long one answer serves; an outage keeps the last answer | 60 | no |
| `Operators__AppUserIds` | Comma/space-separated AppUserIds allowed to call operator surfaces (`GET /api/Operator/PinHealth`, #384). Not a role: an ordinary account, listed here. Matched exactly | nobody | no |
| `AgentAttestation__ManifestDirectory` | Directory holding the attested agent YAMLs and `output-contracts.json` (replaces the former `AgentAttestation__ManifestPath`) | `agents/` under the app base directory (populated at build from the `external/consultologist-agents` submodule) | no |

Every output-contract catalog entry is attested: the deployed agent against its git
manifest (`{agent-name}.yaml`, including the `text.format` block — type/name/strict
and canonical-JSON schema comparison), plus the catalog↔manifest schema cross-check
(a catalog entry whose declared schema differs from its agent manifest's is a
startup failure under enforce).

Transient check failures (Foundry unreachable) only warn, even in enforce mode —
only proven disagreement is fatal. A catalog entry with no git manifest is proven
disagreement, not transient.

## Workflow packages (`Workflow/WorkflowPackageStore.cs`, `Workflow/WorkflowPackages.cs`)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `WorkflowPackages__BlobServiceUri` | Blob service URI of the **private** registry (acct-* forks), e.g. `https://<account>.blob.core.windows.net` — enables Entra ID auth via the managed identity (reading needs Storage Blob Data Reader; the in-app editor's publish endpoint needs Storage Blob Data **Contributor**) | none (falls back to connection string) | recommended in Azure |
| `WorkflowPackages__PublicBlobServiceUri` | Blob service URI of the **public** registry (repo-owned packages; anonymous read, no credential) — e.g. `https://consultologistpublic.blob.core.windows.net`. Unset → repo-owned packages resolve from the private container AND the output-contract catalog loads from the bundled `agents/` directory (local dev) | none | yes in Azure since #92 |
| `OutputContracts__Pin` | Catalog registry ref: `output-contracts@latest` or `output-contracts@vYYYY.MM.N` — the version loaded at startup and stamped into job records as `catalogRef` (#93). Activating a new catalog = publish + bump the concrete pin + restart; production pins explicitly (set 2026-07-23) so catalog releases never activate implicitly | `output-contracts@latest` | no |

`GET /api/Public/Chain` (#95) is anonymous with open CORS and requires only `WorkflowPackages__PublicBlobServiceUri`; it 503s when the public registry is unconfigured.
| `WorkflowPackages__ConnectionStringName` | *Name of another setting* holding a storage connection string (local-dev fallback path) | `AzureWebJobsStorage` | no |
| `WorkflowPackages__Default` | Package ref: `name@vYYYY.MM.N` or `name@latest` | `general@latest` | no |

## Document extraction (`Documents/DocumentExtraction.cs`, #241)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `DocumentExtraction__MaxConcurrentParses` | A positive integer: how many documents this worker parses at once. Anything unparseable or ≤ 0 falls back to the default | `4` | no |

Four because the Function App runs on Linux Flex Consumption at **2048 MB,
which is one CPU core**. Past a handful of concurrent parses there is no
throughput to gain and only memory to lose, and four still finish inside the
20-second parse timeout where the platform's default of 16 concurrent HTTP
requests would not. A parse retains 10–15 MB for documents we accept
(measured against PdfPig 0.1.15; cost tracks pages and extracted text, not
file size).

**Set it to `1` to watch the gate fire.** With one slot, two simultaneous
uploads race for it and the loser gets `503` with *"We are reading several
documents right now. Nothing is wrong with this one — try again in a
moment."* At the default of 4 the gate is invisible to a single operator,
which is intended — so `1` is the only practical way to see the refusal path
in production. No deploy is needed either way; remove the setting afterwards.

```azurecli
az functionapp config appsettings set --name <APP_NAME> --resource-group <RESOURCE_GROUP> \
    --settings DocumentExtraction__MaxConcurrentParses=1
# and to restore the default:
az functionapp config appsettings delete --name <APP_NAME> --resource-group <RESOURCE_GROUP> \
    --setting-names DocumentExtraction__MaxConcurrentParses
```

Note what this does **not** bound: the request body is buffered before the
gate, so upload memory is bounded by platform concurrency rather than by this
number, and one crafted file can still allocate its own worst case. See
`docs/DOCUMENT_INPUT.md` § 9.

## Rate limiting (`RateLimiting/AccountRateLimiter.cs`, #266)

| Variable | Accepted values | Default | Required |
|---|---|---|---|
| `RateLimits__SubmissionsPerHour` | A positive integer: submissions one account may make per clock hour. **`0` or negative disables the limit entirely** — the kill switch, and what local dev and CI run on | `60` | no |
| `RateLimits__MaxEmailDeferralHours` | How long an emailed consult may sit in the `Queued` folder before it is given up on and answered with a rejection reply | `2` | no |

**One submission is one preview call or one job start**, whatever it carries.
A submission with three attachments costs exactly what a 20 KB text file
costs — this bounds how often an account can ask, not what each ask costs.
Parse cost is bounded by `DocumentExtraction__MaxConcurrentParses` above.

60/hour is roughly 20 consults an hour at two attachments each: far beyond a
single operator, and a 12× cut from the 750/hour the email door can otherwise
sustain (25 messages per poll × 30 polls). The window is fixed and aligned to
the UTC hour, so a burst straddling a boundary can reach twice the limit.

**The limiter fails open.** If its table is unreachable the submission
proceeds and the fault is logged: losing the limit during an outage costs
CPU, while refusing during one costs a clinician their referral.

Rows are one per account per hour in the `AccountRateLimits` table and are
never deleted; at that volume a cleanup is worth having eventually rather
than now.

### Where to look when investigating a job

Tables answer *what happened to a message*; they do not answer *what a
consult was built from*. Knowing which is which saves a wrong turn — during
#294 the events table was the reachable one, was empty, and looked like an
answer.

| question | where |
| --- | --- |
| what became of an emailed message | `EmailIntakeProcessed` — one row per message, `Outcome` is the disposition |
| status, timing, block counts of a job | `ConsultGenerationJobIndex` — one row per job |
| how often an account has submitted | `AccountRateLimits` |
| **which documents a consult actually read** | `/history/{jobId}` in the app, or `scripts/show-job-provenance.sh <job-id>` from a terminal |

**Provenance is not in any table.** `InputOrigins` lives on the job's durable
entity and is served by `GET /api/ConsultGenerationJobs/{jobId}`; History
renders it as the Provenance panel and the script prints the same thing.

**`ConsultGenerationJobEvents` is not an audit trail.** It is written only by
the SSE endpoint, for browser resume. A job nobody streamed — which is every
emailed one — correctly has zero rows, and that says nothing about the job.


### Driving it by hand (verified in production 2026-08-01)

Both settings exist to be turned down: at their defaults the limit is
invisible to a single operator, which is intended and is why exercising
either refusal takes deliberate setup. Both apply on the app setting write —
no deploy.

```azurecli
# the interactive refusal: third preview in an hour returns 429
az functionapp config appsettings set --name canada-east-ai-function \
    --resource-group consultologist_group --settings RateLimits__SubmissionsPerHour=2

# the email expiry: anything in Queued is given up on at the next poll
az functionapp config appsettings set --name canada-east-ai-function \
    --resource-group consultologist_group --settings RateLimits__MaxEmailDeferralHours=0

# restore BOTH afterwards — neither has a sane production value here
az functionapp config appsettings delete --name canada-east-ai-function \
    --resource-group consultologist_group \
    --setting-names RateLimits__SubmissionsPerHour RateLimits__MaxEmailDeferralHours
```

`scripts/verify-rate-limit.sh` drives the interactive door and asserts the
refusal; the email door has no script, because the authentication floor needs
a real mail path and the sender must be the address matched in `AppUsers`.

**Seed the counter rather than racing the clock.** The window is the UTC
hour, so budget spent by hand evaporates at the top of it and a test set up
at :55 tests nothing at :01. Writing the counter directly is deterministic,
and seeding the *next* window too removes the boundary from the picture
entirely. `Count` must be `Edm.Int32` — as a string the entity fails to
deserialize, the limiter fails open, and the submission sails through
looking like a bug in the limiter:

```azurecli
az storage entity insert --account-name consultologistjobqueue --auth-mode login \
    --table-name AccountRateLimits --if-exists replace \
    --entity PartitionKey=<APP_USER_ID> RowKey=2026-08-01T23 \
             Count=2 Count@odata.type=Edm.Int32 \
             UpdatedAtUtc=2026-08-01T23:47:00Z UpdatedAtUtc@odata.type=Edm.DateTime
```

**What to watch, and how long it takes.** The claim table is the audit
surface; expect `queued` → (row released) → re-claimed, about two polls per
retry cycle at the 2-minute cadence:

```azurecli
az storage entity query --account-name consultologistjobqueue --auth-mode login \
    --table-name EmailIntakeProcessed --query "items[].{c:ClaimedAtUtc,o:Outcome,j:JobId}"
```

A queued message has `Outcome=queued` and **no** `JobId`, and the counter
does **not** move while it is refused — a refusal spends nothing, which is
what stops a rate-limited referral starving its own account on every retry.

**Delete the seeded rows when finished.** They are indistinguishable from
real ones and will refuse that account's genuine traffic for the rest of the
hour:

```azurecli
az storage entity delete --account-name consultologistjobqueue --auth-mode login \
    --table-name AccountRateLimits --partition-key <APP_USER_ID> --row-key <WINDOW>
```

## Storage stores (Azure Tables)

Entra ID first (#10, mirroring the workflow-package registry): when a
`…__TableServiceUri` setting is present the store authenticates as the app's
managed identity (the `AZURE_CLIENT_ID` user-assigned identity needs
**Storage Table Data Contributor** on the account). The named connection
string remains only as the local-dev (Azurite) fallback.

| Variable | Read in |
|---|---|
| `AccountStorage__TableServiceUri` | `Auth/AccountStore.cs`, `Auth/AccountSettingsStore.cs` (also the fallback URI for the two below). Production: `https://consultologistjobqueue.table.core.windows.net` |
| `ConsultGenerationJobEventStorage__TableServiceUri` | `Jobs/ConsultGenerationJobEventStore.cs` (optional override) |
| `ConsultGenerationJobIndexStorage__TableServiceUri` | `Jobs/ConsultGenerationJobIndexStore.cs` (optional override) |
| `AccountStorage__ConnectionStringName` | Local-dev fallback name (default `AzureWebJobsStorage`); same chain as before for the two job stores |

## Platform / runtime (not set by application code)

| Variable | Notes |
|---|---|
| `AzureWebJobsStorage` | Local-dev only (`UseDevelopmentStorage=true`, Azurite). In production the host and Durable Functions use the identity-based form instead: `AzureWebJobsStorage__accountName` + `__blobServiceUri`/`__queueServiceUri`/`__tableServiceUri` + `__credential=managedidentity` + `__clientId` (the user-assigned identity, which needs Storage Blob Data Owner, Queue Data Contributor, and Table Data Contributor on the account). Shared-key access is disabled on `consultologistjobqueue` (#10). |
| `FUNCTIONS_WORKER_RUNTIME` | Must be `dotnet-isolated`. |
| `WEBSITE_INSTANCE_ID` | Provided by Azure; the code only reads it to detect "running in Azure". Never set manually. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry destination. |

### Flex Consumption scale settings (not app settings)

Not in `host.json` and not environment variables — they live on the plan and
are read with `az functionapp scale config show`. Recorded because #241's
concurrency gate is sized against them.

| Setting | Current | Notes |
|---|---|---|
| `instanceMemoryMB` | `2048` | Flex offers 512 / 2048 / 4096, giving 0.25 / 1 / 2 CPU cores. Plus 272 MB of platform buffer that is not billed |
| HTTP concurrency | unset ⇒ **16** | The default comes from instance size: 512 → 4, 2048 → 16, 4096 → 32. Set explicitly with `az functionapp scale config set --trigger-type http --trigger-settings perInstanceConcurrency=N`, after which it stops tracking instance size |
| `maximumInstanceCount` | `100` | Dropping below 40 for HTTP apps is documented as causing throttling under load |

**HTTP concurrency cannot be set in `host.json` on Flex Consumption.**
`maxConcurrentRequests` there is accepted into the file and silently ignored,
which is worth knowing before anyone tries it.

## Legacy settings

None. The assistants-era leftovers (`AzureAI__AgentId__old`, `AzureAI__Endpoint__old`,
`AzureAI__ApiVersion__old`) and the retired agent pin settings
(`AzureAI__AgentName`/`AgentVersion`/`ConceptAgentName`/`ConceptAgentVersion`,
replaced by the output-contract catalog) were deleted from the Function App
2026-07-15 — and from the **`consultologist-blazor` Static Web App**
2026-08-01, which that first pass missed. Worth remembering that these are two
resources with separate configuration: a setting deleted from one survives on
the other, and this section said "None" for two weeks while three of them were
still sitting on the site.

## Frontend (`src/Consultologist.Web/wwwroot/appsettings.json`)

The Blazor WASM app is configured by this JSON file (bundled, per-environment overrides
via `appsettings.Development.json`), not by environment variables:

- `AzureAd:Authority`, `AzureAd:ClientId`, `AzureAd:ValidateAuthority` — MSAL sign-in.
- `AzureFunction:ApiScope` — scope requested for API tokens.
- `AzureFunction:*Url` — endpoint URLs: `AccountMeUrl`, `ConsultGenerationJobsUrl`,
  `DiagnosticsSseExitUrl`, `WorkflowPackageCurrentUrl`, and the editor pair
  `WorkflowPackageContentUrl` / `WorkflowPackagePublishUrl` (#57),
  `WorkflowPackageLineageUrl` (#89), `WorkflowPackageDiagramUrl` (#114),
  `WorkflowPackageMineUrl` (#134 — the package selector's fork listing), and
  `WorkflowPackageDiagramPreviewUrl` (#144 — POST a manifest, get its diagram;
  the editor's pending-edits graph preview). (`AgentProxyUrl`
  and `ConsultGenerationUrl` were removed with their legacy endpoints in milestone 3.)
- `AzureFunction:TimeoutSeconds` — HTTP client timeout for AI calls (default 240 when
  absent; shipped value 300).

## Static Web App staging environments (#156)

Every PR gets a staging environment on the SWA (`consultologist-blazor`);
the workflow's close job removes it when the PR closes. The historical
leak was a race, not a close failure: a still-in-flight PR build could
finish *after* the close job and re-create the environment. The workflow's
`concurrency` group (one run per branch, newest wins, superseded builds
canceled) makes that impossible — the close-event run is always the
newest in its branch group.

A weekly scheduled sweep (`.github/workflows/swa-staging-sweep.yml`,
Mondays 09:00 UTC, also runnable via workflow_dispatch) deletes any
staging environment whose PR is no longer open — it authenticates via
GitHub OIDC as `consultologist-blazor-swa-sweep` (Contributor scoped to
the SWA resource only) and never touches `default` (#182).

Manual sweep, only if the scheduled one is unavailable (also mind the
10-environment cap):

```bash
az staticwebapp environment list -n consultologist-blazor \
  --query "[].{name:name, source:sourceBranch}" -o table
az staticwebapp environment delete -n consultologist-blazor \
  --environment-name <name> --yes
```

An automated no-open-PR sweep becomes a trivial scheduled job once #16
establishes GitHub→Azure OIDC for CI.
