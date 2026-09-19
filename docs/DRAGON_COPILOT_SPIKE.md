# Dragon Copilot integration — spike record (#614)

**Status: fit gate resolved, 2026-09-10 — the direct extension does NOT fit cleanly;
recommend EHR-mediated.** The prototype-first fit check ran against Microsoft's *actual*
`/v1/process` contract (`github.com/microsoft/dragon-copilot-extension-samples`, fetched
2026-09-10) and found **two blocking mismatches** (§ 6): the payload is **encounter-bound
with no referral channel**, and Dragon presents an **app-only token with no clinician
delegated token** (the #610 bridge is missing — OBO cannot mint one from an app token).
So the direct "AI apps and agents" extension is **deferred** — a narrow
note-in→letter-out fit, blocked on a clinician-identity bridge; the recommended path is
**EHR-mediated** (Epic #654 / Cerner #662, where the clinician is interactively signed in
and holds a delegated token). The **direct extension / satellite is NOT built** (deferred
until a clinician-identity bridge exists). The **`ambient-note` origin kind was since built
and released** — it shipped with the v17 spec bump, mirroring the live `transcript` kind
(#671), so § 8's inventory is now the record of what was done, not pending work; only the
Dragon consumer that would exercise it is deferred. The extensibility-surface map (§ 3) was
recorded 2026-09-09; this pass folds in the gate outcome. Design of record.**

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

## 2. Decision — EHR-mediated; the direct extension deferred (fit gate resolved)

The original decision (2026-09-09) was "GO, extension path, prototype-first." **The
prototype-first gate (§ 6) has now run, and it changes the decision.** The direct Dragon
extension does **not** fit the referral→consult-letter core, and it lacks a
clinician-token bridge, so it is **deferred — not built**. The recommended path is
**EHR-mediated**: documents flow through Epic/Cerner (SMART on FHIR), where the clinician
is interactively signed in and holds a delegated `access_as_user` token — which both
sidesteps the referral-channel gap and supplies the clinician identity the engine (#610)
requires. The direct extension stays a *future* option — for a narrow "Dragon encounter
note → consult letter" flow only — and only once a clinician-identity bridge exists.

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

## 4. Integration shape per leg (as originally proposed — superseded by § 6)

> **Superseded by the fit gate (§ 6).** The shapes below describe how the direct
> extension *would* work; the gate found it doesn't fit (no referral channel, no
> clinician token), so this is deferred in favour of EHR-mediated (§ 2). Kept for the
> record and for a future revisit once the identity bridge is solved.

- **Leg 2 (the spine):** Consultologist is an **extension** — `POST /v1/process`
  returns an Adaptive Card in Dragon's UI. This is the "more access than a separate
  tab" the issue wants.
- **Leg 1A:** take the Dragon note from the `/v1/process` payload (transcript +
  note) — no separate fetch needed in the extension flow; DDE webhooks are the
  out-of-band alternative.
- **Leg 1B:** the card's **update-note** action writes our consult letter back into
  the clinician's Dragon note.

## 5. The auth bridge (the assumption the gate disproved)

The 2026-09-09 assumption was: Dragon → the extension via an **Entra service principal**;
the extension then calls **our engine as the clinician** — a delegated `access_as_user`
bearer (Entra SSO / On-Behalf-Of, "natural since Dragon is Entra-native"). Our engine
still only ever sees the clinician's delegated token; app-only is refused by name (#610).

> **The gate (§ 6) disproved the middle step.** Dragon calls the extension with an
> **app-only token** (`idtyp=app`) identifying *Dragon*, not the clinician — it carries
> practitioner metadata (NPI) but **no clinician delegated token**, and On-Behalf-Of
> cannot mint one from an app token. So the extension has nothing to present the engine
> as the clinician, and #610 refuses the app token by name. There is **no clean
> clinician-token bridge** in the direct-extension path. EHR-mediated (§ 2) is where the
> clinician's delegated token actually exists. Surfacing letters as a **fetch/read** over
> the existing doors, and never pushing into Dragon, still holds for whatever path ships.

## 6. Fit validation — findings (the gate, resolved 2026-09-10)

The open question was: can `/v1/process` carry our *referral* inputs, and can the
extension call the engine as the clinician? Resolved by reading Microsoft's actual
contract — `physician/physician-extensibility-api.yaml` and `doc/AuthenticationDesign.md`
in `github.com/microsoft/dragon-copilot-extension-samples` (MIT, fetched 2026-09-10).
**Two blocking mismatches:**

1. **No referral channel — the payload is encounter-bound.** `POST /v1/process` takes a
   `DragonStandardPayload` = required `sessionData` + one of `Note` / `Transcript` /
   `IterativeTranscript` / `IterativeAudio`, **all nested under a single `encounter`**
   (patient / practitioner / visit). There is **no field for an external referral
   package** — the extension receives only what Dragon captured in the visit. Our core
   input (a referral → consult letter) has nowhere to enter; it could only ride the
   dictated transcript, a degenerate workflow. (The response is a `ProcessResponse` whose
   `VisualizationResource` — an AdaptiveCard, subtype note/timeline — supports
   **accept / reject / copy / updateNote**, so Leg 1B, writing our letter into the note,
   would work; and Leg 1A, the note itself, is in the payload. Only the narrow "Dragon
   encounter note → consult letter" shape fits — not referral→letter.)

2. **No clinician delegated token — the #610 bridge is missing.** Dragon authenticates to
   the extension with an **app-only** Entra token: the extension must validate
   `idtyp=app`, `azp` = Dragon's app id `d9350f5d-71c2-46b9-b41d-3c5d51ffe6e8`, and
   `aud` = the vendor app (whose identifier URI is `api://{tenant}/{endpoint host}`). That
   token identifies *Dragon*, not the clinician; it carries practitioner metadata (NPI)
   but **no clinician `access_as_user` token**, and because it is app-only, **On-Behalf-Of
   cannot exchange it for one**. The extension is invoked server-side by Dragon (no
   interactive clinician session), so it cannot mint a fresh delegated token either. The
   engine (#610) refuses app-only tokens — so there is **no clean way for the extension to
   call the engine as the clinician**.

**Determination:** the direct extension is a *narrow* fit (note-in→letter-out, not the
referral→letter core) **and** blocked on a hard clinician-identity problem. **Recommend
EHR-mediated** (§ 2): via Epic (#654) / Cerner (#662) SMART on FHIR the clinician is
interactively signed in and holds a delegated token, and documents flow through the EHR —
sidestepping both mismatches. The direct extension is **deferred** pending a solved
clinician-identity bridge; the partner-program application is **not** pursued now (the fit
does not justify the US-only, sales-led onboarding — § 7).

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
- **Partner-program application is NOT pursued now** (§ 6): the fit gate found the direct
  extension does not carry referral→letter work and has no clinician-token bridge, so the
  US-only, sales-led Partner Center onboarding is not justified. Revisit only if the
  clinician-identity bridge is solved and the US market is in scope.

## 8. Engine-change inventory (BUILT + released; the Dragon consumer stays deferred)

**One input origin kind, registry-first — a DISTINCT kind, not `transcript`.** Originally
deferred, this was **since built and released** (v17 spec bump), because a note-in consumer
does exist independent of Dragon — the EHR-mediated document road and the ambient-note
content channel — and a Dragon *ambient* note's provenance differs from a Zoom
speaker-labeled transcript (an AI-summarized visit draft vs. speaker turns), so it warrants
its own kind. The name **`ambient-note`** keeps the SaMD/anti-ambient boundary legible in
the record and in `History.razor` (adding `dictation` only if a verbatim-dictation path
appears). It mirrors the live `transcript` kind (#671) exactly, as built:

1. Bump `consultologist-provenance@v…` — add the `ambient-note` sentence to the
   `inputOrigins` narrative in `provenance-record.md` and bump `provenance-versions.json`
   (prose-only; no hash-ladder change, since an ambient note reuses a document's fields —
   `ProvenanceVersionSetTests` does not gate origin kinds).
2. Const in `ConsultInputOriginKinds` — `src/Consultologist.Api/Models/ConsultGenerationRequest.cs`.
3. A caller-declared marker (an `AmbientNoteInputs` field beside `TranscriptInputs`, or a
   generalized slot→kind map) driving the stamp in
   `src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs` (not the default `document`).
4. A `DescribeOrigin` arm in `src/Consultologist.Web/Pages/History.razor`.

No new endpoint, auth, `source`, container, `dragon` provider constant, or webhook. The
note would enter the existing doors as an `InputFilePayload` on a declared slot —
identical to the Zoom transcript.

## 9. Go / no-go (revised after the gate)

- **Direct extension: NO-GO for now (deferred).** The gate (§ 6) showed it neither carries
  the referral→letter core nor gives the extension a clinician token to call the engine.
  It stays a future option for a narrow note-in→letter-out flow, gated on a solved
  clinician-identity bridge.
- **Recommended path: EHR-mediated** — Epic (#654) / Cerner (#662, leg-2 #665) SMART on
  FHIR, where the clinician holds a delegated token and documents flow through the EHR.
- **Partner-program application: NOT pursued** (§ 7).
- **`ambient-note` origin kind: built + released** (§ 8) — shipped with the v17 spec bump,
  mirroring the live `transcript` kind (#671); the Dragon extension that would consume it
  stays deferred.

Build precedent for a delegated-token satellite (if the identity bridge is ever solved):
`consultologist-copilot-agent` (#667) and the Zoom satellite (#613/#671/#675). The
anti-ambient boundary (§ 1) is unchanged: a Dragon note read into a **reviewed,
provenance-recorded** run is transcription-side, not CDS.
