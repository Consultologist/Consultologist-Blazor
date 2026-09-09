# Dragon Copilot integration — spike record (#614)

**Status: spike complete, 2026-09-09 — GO on the extension path, prototype-first.
Dragon Copilot now has a documented third-party program ("AI apps and agents"
extensions); Consultologist fits as a delegated-token Entra-native satellite over
the existing engine doors, with at most **one input origin kind (`ambient-note`),
registry-first**. Validate the referral→letter fit against the open-source samples
BEFORE the sales-led, US-only partner application; EHR-mediated is the non-US
fallback. Docs only this pass. Design of record for the build follow-up.**

## 1. What this is — and the anti-ambient boundary, in Dragon's own presence

Two legs: (1) documents flow both ways — a Dragon note reaching Consultologist as
an input, and our consult letters reaching the clinician inside Dragon; (2)
Consultologist as a **step inside Dragon's workflow**.

**Dragon IS the ambient scribe #189 positions against — so the boundary must be
stated in Dragon's own presence.** Transcription and ambient capture are **Dragon's
product**; ours is the **verifiable referral-package → consult-letter assembly with
the provenance spine** — a *complement, not a competitor* (`docs/COPILOT_AGENT_SPIKE.md`
§1: Dragon is "Microsoft/Nuance's finished ambient clinical scribe … minding the
anti-ambient boundary (#189)"). A Dragon note read into a **reviewed,
provenance-recorded** run is *transcription-side*; it is not the SaMD-adjacent
clinical-decision-support work that `docs/customizable-workflow/completeness.md:32`
flags ("stage 3's claim verification and stage 4's coding suggestions edge toward
clinical decision support … SaMD territory"). Nothing here analyses a patient or
advises a clinician; the SaMD line is unmoved.

## 2. Decision — GO, extension path, prototype-first

Build Consultologist as a Dragon **"AI apps and agents" extension** — a
delegated-token satellite (the #611 F2 pattern, like the Copilot agent), in its own
repo. But **validate the fit against the open-source samples before** engaging the
sales-led partner program (§ 6). EHR-mediated is the fallback for non-US markets.

## 3. Extensibility-surface map (state as of 2026-09-09; moving fast, pricing gated)

Dragon is **no longer closed** — a real third-party program exists, direct (not
only EHR-mediated).

| Leg / direction | Exists? | Mechanism |
|---|---|---|
| **1A — Dragon note → us** | Yes (direct API) | The extension `POST /v1/process` payload hands over the **transcript + clinical note**; alternatively **Dragon Data Exchange** webhooks (Dragon standard payload: transcript, document, resources, `external_callback_url`). |
| **1B — our letter → Dragon** | Yes (note/encounter-scoped) | The extension returns an **Adaptive Card** with an **accept/copy/update-note** action that injects our consult letter into the clinical note. Not a generic "drop any document" endpoint. |
| **2 — a step inside Dragon** | Yes (flagship) | The **extension** itself: host `POST /v1/process`; Dragon calls with patient context + audio turns + transcript + note; return an Adaptive Card rendered **inside Dragon** (desktop/web/mobile), iterative or end-of-encounter. |
| Embed Dragon → into our app | Yes | Dragon Copilot Developer Kit (SMART-on-FHIR/token launch; DAXKit SDKs). Not our direction. |
| EHR-mediated | Yes (fallback) | Both plug into Epic/Cerner (SMART on FHIR); documents flow through the EHR. The only realistic non-US path today. |

Auth for the extension = **Entra service principal + OAuth2**; official OpenAPI +
MIT samples + CLI at `github.com/microsoft/dragon-copilot-extension-samples`.
Publish = Partner Center offer ("Dragon Copilot Physicians AI Apps and Agents") →
Microsoft Marketplace → deploy via the Dragon admin center (custom apps can deploy
to a single org without Marketplace).

Sources: https://learn.microsoft.com/industry/healthcare/dragon-copilot/extensions/ ·
.../extensions/development-concepts · .../extensions/create-offer ·
.../extensions/adaptive-card-spec · .../sdk/partner-apis/dragon-data-exchange/ ·
https://github.com/microsoft/dragon-copilot-extension-samples

## 4. Integration shape per leg (recommended)

- **Leg 2 (the spine):** Consultologist is an **extension** — `POST /v1/process`
  returns an Adaptive Card in Dragon's UI. This is the "more access than a separate
  tab" the issue wants.
- **Leg 1A:** take the Dragon note from the `/v1/process` payload (transcript +
  note) — no separate fetch needed in the extension flow; DDE webhooks are the
  out-of-band alternative.
- **Leg 1B:** the card's **update-note** action writes our consult letter back into
  the clinician's Dragon note.

## 5. The auth bridge (the #610 perimeter stays intact)

Dragon → the extension is authenticated with an **Entra service principal**; the
extension then calls **our engine as the clinician** — a delegated `access_as_user`
bearer (Entra SSO / On-Behalf-Of, natural since Dragon is Entra-native). Our engine
still only ever sees the **clinician's delegated token**; app-only is refused by
name (#610, `SATELLITE_CALLERS.md`). Surfacing the clinician's letters is a
**fetch/read** over the existing doors (History / `GET ConsultGenerationJobs/{id}`)
— the engine never pushes into Dragon (`SATELLITE_CALLERS.md` §5; push "leans more
ambient," an extra reason against it).

## 6. The fit-risk + prototype-first validation (the gate)

The extension model is **encounter/note-centric** (an ambient visit → a clinical
note). Consultologist is **referral-package → consult-letter**. **Open question:**
can `/v1/process` carry our *referral* inputs, or must the referral ride the
transcript/context/dictation that Dragon hands in? Resolve this **cheaply and
first**: clone the samples repo, read `physician/physician-extensibility-api.yaml`
(the `/v1/process` contract) and `AuthenticationDesign.md`, and prototype an
endpoint against the sample requests — **before** engaging Dragon partner relations
for allow-listing. If the referral→letter flow doesn't fit the encounter trigger,
fall back to EHR-mediated (§ 3) rather than force it.

## 7. Constraints / gatekeeping

- **US-only today** — the extension program is limited to partners selling to US
  customers; the engine is **Canada-based**. Non-US ⇒ EHR-mediated.
- **Sales-led gating:** Partner Center account + the "Dragon Copilot Physicians AI
  Apps and Agents" offer + an allow-list step with a **"Dragon partner relations
  team"**; an Entra tenant + an Azure subscription with `Microsoft.HealthPlatform`
  registered.
- **No BYOL / license-key apps.** **Pricing + clinical-safety-review depth are not
  public** (gated behind Partner Center) — flag to stakeholders.
- Transcription/STT stays **Dragon's paid product** — none on our side.

## 8. Engine-change inventory (for the build follow-up — not built here)

**One input origin kind, registry-first — a DISTINCT kind, not `transcript`.** A
Dragon note's provenance differs from a Zoom speaker-labeled transcript (a Dragon
*ambient* note is an AI-summarized visit draft; a *dictated* note is the clinician's
own words). Recommend **`ambient-note`** — the name keeps the SaMD/anti-ambient
boundary legible in the provenance record and in `History.razor` — adding
`dictation` only if Dragon hands in verbatim dictation. Same surface as the Zoom
spike's `transcript`:

1. New `consultologist-provenance@v…` version defining the kind (normative;
   `tests/ProvenanceVersionSetTests.cs` holds the engine equal to it).
2. Const in `ConsultInputOriginKinds` — `src/Consultologist.Api/Models/ConsultGenerationRequest.cs`.
3. Stamp it at the note slot in `src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs`
   (not the default `document`).
4. A `DescribeOrigin` arm in `src/Consultologist.Web/Pages/History.razor`.

No new endpoint, auth, `source`, container, `dragon` provider constant, or webhook
(fetch). The note enters the existing doors (`DocumentExtractions`,
`ConsultGenerationJobs`) as an `InputFilePayload`/`ConsultInputValue` on a declared
slot — identical to the Zoom transcript. The satellite is greenfield in its **own
repo**.

## 9. Go / no-go

- **GO** to build the extension satellite (own repo) and — **first** — to prototype
  the `/v1/process` fit against the open-source samples (free, no gating,
  verification-independent).
- **Partner-program application = deferred** (a sub-step): engage Dragon partner
  relations for the Marketplace offer + allow-list only once the fit holds and the
  US-market timing is right. Outside the US, ship EHR-mediated instead.
- Engine cost = the one `ambient-note` origin kind, registry-first — nothing else.

Build precedent: `consultologist-copilot-agent` (the delegated-token satellite,
#667) and the Zoom satellite (#613/#671).
