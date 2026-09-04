# Epic SMART on FHIR read-only intake — spike record (#190)

**Status: spike record for Milestone 23 (experiments E1–E4 run 2026-09-04
against Epic's public sandbox). This record answers what the sandbox and
the engine establish, confirms and amends the premises of the build
target (#654), states what a build is NOT, and carries the sequencing
facts #190 raised. No engine code changed; the spike's instrument is the
new `consultologist-smart-panel` repo.**

Epic's legitimate third-party path is a SMART on FHIR app that launches
in patient context and **reads** the chart's referral documents —
replacing paste-the-referral with in-context intake. The developer
sandbox (fhir.epic.com) is free and needs no one's permission to build
against; production enablement is customer-initiated per organization
(§ 6). Write-back is out (#190), and stays out.

## Decisions taken with the operator (2026-09-04)

- **Run the sandbox experiments**, not desk research only — prove #654's
  premises the way this project proves things (E1–E4 below).
- **The panel lands in a new separate repo** — `consultologist-smart-panel`
  (the #613/#614 "any build lands in a separate repo/app registration"
  rule, the `snomed-snowstorm-mcp` precedent). The spike's instrument
  seeds the repo that becomes the real satellite when #654 fires.
- **The registration is the official app** ("Consultologist"), not a
  throwaway — an Epic on FHIR app is a freely editable draft until
  deliberately taken to production; the non-production client id serves
  the whole spike.

## 1. What exists (the foundations, all live)

The perimeter and the roads a SMART build would use are already built:
- The satellite pattern (#611, `SATELLITE_CALLERS.md`) — a SMART panel is
  the canonical satellite: its own registration, presenting the
  clinician's delegated token. § 4 there is the external-identity-binding
  design this spike is the "first spike" for.
- App-only tokens refused by name (#610); CORS origins as configuration
  (#612 — a hosted panel's origin is a setting, not a deploy); the OBO
  foundation and the FromLink parser road (#615,
  `Documents/DocumentExtractions.cs`, `Documents/GraphDocumentFetcher.cs`).
- The document parser (#234, `Documents/DocumentExtraction.cs`) — the sole
  format authority; adding a format touches it and nothing else. Owns
  txt / md / pdf / docx.
- Provenance input-origin kinds are registry-normative
  (`external/consultologist-provenance`) — an Epic-borne origin kind is a
  registry-first change, NOT this spike's to make.

## 2. What the documentation establishes

- Epic's authorization server implements **OpenID Connect** as part of
  SMART App Launch; ONC § 170.315(g)(10) certification requires SMART, so
  this is not optional on Epic's side.
- The sandbox is self-serve: registration mints a production and a
  **non-production** client id; app changes sync to the sandbox within
  ~1 hour; synthetic test data (patients, provider logins) is provided.
- Production is **customer-initiated per organization** — a health
  system's Epic team activates the app for their instance; each instance
  is its own issuer. Production also requires an **https** endpoint URI on
  the registration (the sandbox accepts `http://localhost`).
- Registration answers, as chosen for this app: incoming APIs =
  DocumentReference (read + search) and Binary (read), both for
  Clinical Notes and Document Information categories, plus Patient and
  Practitioner read; **no outgoing APIs** (Epic never calls us — the
  anti-webhook posture); **not** a dynamic-client app; **public client**
  (browser + PKCE, no secret); **SMART scope version v2**.

## 3. What the sandbox establishes (E1–E4)

FHIR base: `https://fhir.epic.com/interconnect-fhir-oauth/api/FHIR/R4`.

### E1 — discovery + capability (unauthenticated)
`.well-known/smart-configuration` and `metadata`, verbatim facts:
- capabilities include `launch-standalone`, `launch-ehr`, `client-public`,
  `context-standalone-patient`, `sso-openid-connect`, **`permission-v2`**
  (and `permission-v1`, `permission-offline`); PKCE `S256`;
  `authorization_code`.
- issuer `https://fhir.epic.com/interconnect-fhir-oauth/oauth2`; a JWKS is
  published.
- CapabilityStatement: FHIR **R4 (4.0.1)**, software **"Epic August 2026"**,
  60 resource types, **DocumentReference and Binary both declared**.
- The FHIR API answers with `Access-Control-Allow-Origin: *` — a
  browser satellite reaches it directly; no CORS obstacle on Epic's side.

### E2 — the standalone launch (authorization code + PKCE)
Ran the panel against the sandbox; signed in with a sandbox **provider**
test login; the standalone `launch/patient` request produced Epic's own
patient-picker (the sandbox satisfies `context-standalone-patient` with a
built-in picker rather than refusing standalone context). Token response
SHAPE (no secrets recorded): `token_type: Bearer`, `expires_in: 3600`,
`state` echoed, `scope` granted (below), plus `id_token`. **No
`refresh_token`** — `offline_access` was never requested (the launch-bound
posture, #654). Cancelling the picker yielded a **`user/`-scoped** grant
(no patient compartment); the granted scope came back in **granular v2
form with a category constraint**, e.g.
`user/DocumentReference.r?category=…|clinical-note`, alongside
`user/Binary.r`, `user/Patient.r`, `user/Practitioner.r`, `fhirUser`,
`openid`.

### E3 — the identity leg (#654 leg 1)
The `id_token`, JWKS-verified (RS256) against the discovery `jwks_uri`,
carried exactly the shape #654's link ceremony consumes:
- `iss = https://fhir.epic.com/interconnect-fhir-oauth/oauth2` (the
  per-instance issuer — confirming the "namespace the subject by `iss`"
  rule, since production has one issuer per health system),
- `fhirUser =` a `Practitioner` resource URL (the signed-in clinician —
  provider login, as intended),
- `sub =` the same Practitioner id.
This is the § 4 proof-of-control artifact for an `epic` provider row,
confirmed against the live sandbox.

### E4 — the document leg (#654 leg 2), and the parser finding
With a test patient in context (Camila Lopez,
`erXuFYUfucBZaryVksYEcMg3`), `DocumentReference?patient=…` returned a
bundle of **total = 115**; the first page listed Consults and Progress
Notes, **every attachment `text/html`**, filtered to clinical notes by
the granted category scope (scanned/imported *Document Information* did
not appear under this token). One Binary was fetched — **62,653 bytes,
`text/html; charset=utf-8`** — and its exact bytes run through the
engine's real parser (`DocumentExtraction.Extract`, a scratch invocation,
now deleted):

- **The Epic note → `corrupt`.** The note's HTML wraps a base64/PDF-ish
  payload as visible text; the parser, which sniffs for txt/md/pdf/docx,
  refuses it.
- **A clean HTML control → `extracted` via `text/1`, but as tag soup** —
  the "text" is the raw `<div>…<span>…` markup, which would flow into
  model input verbatim.

**Finding: Epic-borne clinical notes do NOT land on the existing rails as
first-class documents.** Neither outcome is shippable — refusing all HTML
loses the note; ingesting tag soup pollutes the input. The parser must
learn HTML (sniff it, extract visible text, still refuse genuinely
corrupt payloads) before SMART intake is real. Per the #234 criterion
this touches the parser only; it is filed as a follow-on (see § 7), not
assumed here.

### A relaunch that failed — cause not diagnosed (recorded, not a blocker)
The first, cold launch succeeded end to end (E1–E4 above). Every
**re-launch** afterwards returned "Invalid OAuth 2.0 request" from
Epic's login handler (`fhir.epic.com/HSWeb_uscdi/?ticket=…`), which
reflected a scope of only `openid fhirUser launch/patient` — the
resource scopes absent. **The cause was not diagnosed.** The reflected
URL is Epic's own, so it cannot be told from it whether the panel sent a
truncated scope or Epic pared one back; the one datum that would
distinguish them — the panel's own pre-redirect log of the scope it
built — was not captured while the failures were live. A stale browser
tab, a service-worker cache, an Epic session-resume quirk, and a
remaining panel bug are all consistent with the evidence and none is
ruled in or out. One real panel bug *was* found and fixed along the way
(a persisted, truncated scope edit in `localStorage` overrode the
prefill; scopes are no longer persisted) — but it is not established
that it was the cause of these failures. The consequence for the spike:
the `patient/`-scoped token shape beside the `user/`-scoped one is the
one datum not captured — a bonus, not a #654 premise, so #190's
substance is unaffected. Anyone resuming should first capture the
`scope sent:` log line the panel now prints, which settles the
panel-vs-Epic question in one launch.

## 4. The design #654 builds — premises confirmed / amended

- **Leg 1 (identity)** — CONFIRMED. The id_token's `openid`/`fhirUser`
  shape, JWKS verification, and per-instance `iss` are exactly as #654
  assumed. An `epic` provider row keyed on `{iss}|{sub}` (the
  `IdentityLinks` shape) is sound.
- **Leg 2 (document)** — CONFIRMED reachable, AMENDED on format. A SMART
  access token reads `DocumentReference` → `Binary` as the clinician; the
  FromLink road (bytes → the parser → an input) applies. **Amendment**:
  the bytes are `text/html`, which the parser does not yet handle — the
  build depends on the parser-learns-HTML follow-on, and on selecting the
  right document **category** (Document Information for referral packages,
  not only Clinical Notes).
- **Launch-bound posture** — CONFIRMED costless: no `refresh_token`
  appears without `offline_access`; a session-bound token is the natural,
  stores-nothing default.
- **Panel home** — CONFIRMED: `consultologist-smart-panel` is the repo,
  seeded here.

## 5. Not in this design

- **Write-back** to Epic (DocumentReference or otherwise) — #190's own
  out-of-scope line; much later, org-enabled, and write scopes are gated
  far harder than reads.
- **Refresh-token persistence** — the first durable credential the system
  would ever hold; its own future issue if a use case demands it (#654).
- **Epic as a sign-in IdP** — never; only `entra-external-id` signs in
  (#611). The Epic id_token binds an identity, never a bearer credential.
- **Non-Epic SMART servers** — the standard is the same; Epic is the first
  target because of the design-partner segment (#189).
- **An engine-side change to input-origin kinds** — registry-first, when a
  build fires (#577/provenance), not in this spike.

## 6. Positioning and sequencing

- **Anti-ambient, stated in Epic's own segment** (#188/#189): the crisp
  distinction is **referral-package → consult-letter, not ambient visit
  transcription**. Reading a referral document into intake is
  transcription-side, not clinical decision support; the SaMD line
  (`customizable-workflow/completeness.md`, and the v12 § 13 "SaMD
  position, stated") is unmoved by this spike — nothing here analyses a
  patient or advises a clinician.
- **Trust-through-verifiability** is the segment's likely convert: hospital
  AI-governance committees ask exactly what the provenance spine answers.
- **Sequencing to production** (recorded, not solved): per-organization
  activation by the hospital's Epic team; a sponsor-site pilot realistic
  once **SOC 2** lands (privacy review gates on it); an **https** endpoint
  URI required on the registration before production; and the governance/
  procurement questions around a co-founder championing the product
  (#189's structured product research, unchanged and still the co-founder's
  to run).

## 7. The split

| Issue | Builds | Fires when |
| --- | --- | --- |
| #654 | The Epic identity link + session-bound FHIR credential (the two legs above) | Now unblocked — E1–E4 confirmed its premises |
| #655 | The parser learns HTML — sniff, extract visible text, refuse corrupt | Before SMART intake ships a real document; #234 seam |
| #190 | — closed by this record | — |

The instrument: <https://github.com/Consultologist/consultologist-smart-panel>
(standalone SMART launch, read-only; the spike's E1–E4 ran through it).
