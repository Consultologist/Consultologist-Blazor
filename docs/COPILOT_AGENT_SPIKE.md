# Microsoft 365 Copilot agent — spike record (#663)

**Status: spike complete, 2026-09-06 — GO. The engine needs no changes; the
agent is a satellite (`docs/SATELLITE_CALLERS.md`) over the existing doors.
The build lands in a separate repo and is tracked as a follow-up. This
document is the design of record for that build.**

## 1. What this is (and what it is not)

Build Consultologist's **own** agent on the **Microsoft 365 Agents SDK**,
surfaced in Microsoft Teams and Microsoft 365 Copilot, where a clinician hands
in a referral (or points at a document) and gets a consult draft in the flow
they already work in.

**Not #614 (Dragon Copilot).** Same "Copilot" word, different thing. Dragon is
Microsoft/Nuance's finished *ambient clinical scribe* — #614 integrates
Consultologist as a guest *into* that product, via the healthcare partner
program, minding the anti-ambient boundary (#189). **This** is us building and
owning an agent in the *general* M365 Copilot surface, Entra-native, standard
publishing. Separate targets; scope them apart.

## 2. Decision — GO

Strong fit, standard supported auth, and — the load-bearing finding — **no
engine changes**. The agent is one more delegated-token satellite over the doors
the SPA and the Epic/Cerner panels already use. Nothing in the research gates a
GO; the cost floor is low and no end-user Copilot license is required.

## 3. SDK — Agents SDK code-first (.NET), not Copilot Studio

The M365 Agents SDK is a **code-first** framework (the evolution of the archived
Bot Framework SDK) for building a **custom engine agent** — you own the model,
orchestration, and hosting; the SDK brokers messages to/from the M365 channels.
C#/.NET is first-class (richest packages, full Adaptive Card routing, the
`auto-signin` and `obo-authorization` samples), which also matches our stack.

Copilot Studio (low-code) was considered and rejected for this: we need
custom-API delegated auth, control of the **On-Behalf-Of** exchange, and a
structured/validated input flow — exactly where pro-code wins; Copilot Studio
would cede the auth path and caps Adaptive Cards at 1.5/1.6. Build **net-new**
on the Agents SDK (the Bot Framework SDK is archived; support ends 2025-12-31).

- Overview: https://learn.microsoft.com/microsoft-365/agents-sdk/agents-sdk-overview
- .NET reference: https://learn.microsoft.com/dotnet/api/copilot-sdk-docs-dotnet/overview?view=m365-agents-sdk
- Studio vs pro-code: https://learn.microsoft.com/microsoft-365/copilot/extensibility/overview-custom-engine-agent
- BF migration/archival: https://learn.microsoft.com/microsoft-365/agents-sdk/bf-migration-guidance

## 4. Auth — SSO + On-Behalf-Of (our existing model)

The standard, first-class path, and it is **our satellite/OBO model exactly**:

1. **User SSO** in Teams/M365 Copilot — the channel obtains a signed-in-user
   token for the agent silently (manifest `webApplicationInfo` with the agent's
   Entra client id and an `api://botid-<clientId>` identifier URI exposing
   `access_as_user`, Teams client ids pre-authorized).
2. **OBO to our API** — `Microsoft.Identity.Web` exchanges that token for a
   delegated token to `api://<consultologist-api>/access_as_user`
   (`OBOConnectionName` + `OBOScopes`; the underlying Entra OAuth 2.0 OBO flow).
3. **The engine admits it identically to the SPA/panels** — `BearerTokenValidator`
   requires a delegated Entra token for `Auth:Audience` carrying
   `Auth:RequiredScope` = `access_as_user`; **app-only tokens are refused by name
   (#610)**. There is no per-client branching.

**No new engine endpoint.** OBO#615 (the engine reaching Graph as the user) is
only relevant if the agent asks the engine to fetch a OneDrive/SharePoint
document as the user; the upload-bytes and job-start paths need none.

- Configure agent OAuth/OBO: https://learn.microsoft.com/microsoft-365/agents-sdk/agent-oauth-configuration
- Teams SSO setup: https://learn.microsoft.com/microsoftteams/platform/teams-sdk/teams/user-authentication/sso-setup
- OBO flow: https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow

## 5. The doors the agent reuses (satellite)

All existing, all delegated-token, results **fetched not pushed**:

- `POST DocumentExtractions` — preview a document's extracted text (raw bytes;
  returns `{ Text, Extractor, PageCount }`). `…/FromLink` for a OneDrive/
  SharePoint share link (uses OBO#615).
- `POST ConsultGenerationJobs` — start the run. Body `ConsultGenerationRequest`
  (`Inputs`: typed `ConsultInputValue` map; `WorkflowPackage`: the pinned ref;
  `InputFiles`: `InputFilePayload` bytes). Returns `202 { JobId, StatusUrl }`.
- `GET ConsultGenerationJobs/{jobId}` (poll) and `…/{jobId}/events` (SSE) — the
  deliverable + `EffectiveInputHash` + provenance. Optional `Cancel`/`Reschedule`/
  `Rerun`. The only outbound is the opt-in completion **email**; no webhook.

## 6. Surface — Teams personal chat first, then M365 Copilot

One Agents-SDK agent serves multiple channels off one Azure Bot registration.
**Target Teams personal (1:1) chat first** — the most mature surface for SSO and
file attachments — then enable the M365 Copilot channel on the same registration
at low incremental cost.

## 7. Structured input + the conversation→consult mapping

The agent presents an **Adaptive Card** that mirrors the setup form: each
declared `WorkflowInputSpec` (of the pinned package) → a typed card field (text/
date/enum/boolean/number) → a `ConsultInputValue` of the matching `Kind`;
documents → `InputFilePayload` entries. `Action.Execute` submits the whole set in
one step; the agent refuses to start until the input set is valid. This keeps the
chat surface from becoming free-form ambiguity.

**File-attachment caveat (real):** Adaptive Cards **cannot** accept file uploads.
A referral document arrives via a Teams **personal-chat message attachment**
(download before the URL expires; manifest `supportsFiles: true`, `personal`
scope), or via a SharePoint/OneDrive link + Graph. File support is
**personal-chat-only**, and M365 Copilot Chat file behaviour differs from Teams —
validate early. (Adaptive Card routing is .NET/JS only, not Python — another
reason for .NET.)

- Adaptive Cards in agents: https://learn.microsoft.com/microsoft-365/agents-sdk/adaptive-cards
- Handle files in Teams: https://learn.microsoft.com/microsoft-365/agents-sdk/teams/teams-files

## 8. The verifiability boundary (the guard)

The agent is a delivery/interaction **surface**. It only assembles a candidate
input set and submits bytes/values. **Extraction, canonicalisation, the
effective-input hash, and per-input provenance are computed by the engine at job
start** (`ConsultGenerationProvenance`, over what the server itself observed).
The agent never supplies the hash, never asserts extracted text as authoritative,
and never bypasses the setup discipline. Review-first stays the engine's.

## 9. Publishing = #554's activation door

A tenant gets the agent only after an **M365 admin approves it and grants
consent** to the requested delegated permissions (including `access_as_user`):
M365 admin center → **Agents → Requests** → review capabilities/permissions →
publish to the org store, scoping the audience. Optionally list on **AppSource /
the Commercial Marketplace** (Teams Store validation via Partner Center).

**This is exactly #554's "marketplace as the activation door"** — a per-tenant
activation and commercial chokepoint, layered on top of per-clinician delegated
identity at runtime. #663 and #554 should be planned together at build time.

- Manage agent requests/consent: https://learn.microsoft.com/microsoft-365/admin/manage/agent-requests
- Publish to Teams Store/AppSource: https://learn.microsoft.com/microsoftteams/platform/concepts/deploy-and-publish/appsource/publish

## 10. Cost / gatekeeping (for the go/no-go)

- **Compute (ours):** the agent is a web app on Azure App Service (or container);
  add whatever AI/orchestration it uses. Azure Bot Service channel registration
  is separate (Teams/Web/Direct Line effectively free).
- **End users:** **no Microsoft 365 Copilot license required** for a custom
  engine agent that calls its own external API (usage-based Copilot Credits apply
  only to unlicensed users when an agent touches shared tenant data — ours does
  not).
- **Program:** org-catalog publishing is free (admin approval only); Commercial
  Marketplace listing needs Partner Center. No per-seat Microsoft fee on a
  self-hosted custom engine agent beyond Azure costs.

## 11. Engine-change inventory

**None.** The agent is a satellite over the existing doors, authenticated by the
existing `access_as_user` delegated path. The build is entirely a new repo
(`consultologist-copilot-agent`) plus Azure/M365 setup:

- **Agent app** (.NET Agents SDK custom engine agent): the Adaptive-Card intake
  mapped to `ConsultGenerationRequest`, the OBO API client (preview + start +
  poll), Teams personal-chat file handling.
- **Operator/Azure setup** (needs the owner's Azure/M365 admin): an Entra app
  registration for the agent (identifier URI, `access_as_user`, Teams SSO
  pre-auth), an Azure Bot resource with the Teams + M365 Copilot channels, the
  API registration's `preAuthorizedApplications`/OBO consent for the agent, and
  the Teams app package + admin approval to publish.

The build is a substantial new hosted app — hence its own follow-up issue,
built when the Azure/M365-admin setup is in hand.
