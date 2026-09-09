# The Zoom satellite — spike record (#613)

**Status: spike complete, 2026-09-09 — GO. Build a Zoom satellite as a
delegated-token client (the #611 F2 pattern, exactly like the Copilot agent):
a Zoom App that signs the clinician into Microsoft Entra and calls the existing
engine doors *as the clinician*. Engine-change inventory is **one input origin
kind (`transcript`), registry-first — nothing else**. The satellite lands in its
own repo; this pass is docs only. Design of record for the build follow-up.**

## 1. What this is (and the anti-ambient boundary)

Two legs, both wanted:

- **Leg 1 — the meeting's documents in the meeting.** A Zoom App panel, in the
  meeting, that shows the clinician the consult documents for that meeting.
- **Leg 2 — the transcript as an input.** Zoom's speaker-labeled cloud transcript
  becomes a consult input: **Zoom does the transcription** (a paid cloud-recording
  feature the clinic enables — we never run STT), and the labeled transcript enters
  a run like other inputs do.

**Not #614 (Dragon), and on the right side of #189.** We do **not** build an
ambient scribe — that is the licensed/partnered product (#614), and #188/#189
record the stance. Ours stays the **verifiable referral/transcript → consult-letter
assembly**: a transcript is an *input* the clinician pulls into a **reviewed** run
with recorded provenance, not ambient clinical decision support. The SaMD line
(`docs/EPIC_SMART_INTAKE.md` §6) is unmoved — reading a transcript into intake is
transcription-side, and the run remains the clinician's own.

## 2. Decision — GO, as a delegated-token satellite (fetch)

The satellite is one more **delegated-token caller** over the doors the SPA, the
Epic/Cerner panels, and the Copilot agent already use (`docs/SATELLITE_CALLERS.md`,
#611). It presents the clinician's `access_as_user` bearer; **#610 still refuses
app-only tokens**. This choice (fetch, below) means the engine needs **no new
endpoint and no new auth** — only one input origin kind. The satellite itself is a
new hosted app in its **own repo** (the `consultologist-copilot-agent` / snomed /
content-repos precedent).

## 3. Leg 1 — documents in the meeting (Zoom App)

- **App surface.** A Zoom **General app** (Marketplace build type: user-managed
  OAuth) whose front end is an **embedded webview of our own web page**, served
  from the app's Home URL. There is no separate "sidebar" build — the Apps panel is
  a *running context* of the same webview (`inMainClient` from the client's Apps
  panel; `inMeeting` during a meeting).
- **Context from the Zoom Apps SDK** (`@zoom/appssdk`): `config()` declares
  capabilities; `getRunningContext()` distinguishes in-meeting vs main-client;
  `getMeetingContext()`/`getMeetingUUID()` give the meeting id/topic;
  `getUserContext()` the Zoom participant. On launch Zoom also sends the encrypted
  **`X-Zoom-App-Context`** header (`uid`, `mid`) to the Home URL — the server-side
  signal for which meeting.
- **Auth = Microsoft Entra, in the panel.** The webview runs our page and does
  **Entra SSO** (independent of Zoom's OAuth), then calls the engine as the
  clinician — the delegated-token satellite. This is preferred over a Zoom-identity
  link (which the fetch model does not need; see § 6).
- **What it shows.** The clinician's consult documents via the existing read doors
  (History / `GET ConsultGenerationJobs/{id}`), scoped to the meeting per § 5.
- **Constraints.** The Home URL response must carry a **`Content-Security-Policy`**
  header, and every external origin the UI loads must be on the app's **Domain
  Allow List** (our web origin, the engine API origin
  `east.ca.api.consultologist.ai`, and `login.microsoftonline.com`).

Docs: https://developers.zoom.us/docs/zoom-apps/architecture/ ·
https://developers.zoom.us/docs/zoom-apps/zoom-app-context/ ·
https://appssdk.zoom.us/classes/ZoomSdk.ZoomSdk.html ·
https://developers.zoom.us/docs/zoom-apps/security/owasp/

## 4. Leg 2 — the transcript as an input (fetch)

**The satellite fetches, the engine stays unchanged (bar one origin kind).**

1. The satellite holds the clinician's **Zoom user-OAuth** token (scope
   `cloud_recording:read`).
2. It obtains the **speaker-labeled VTT** transcript — either the `TRANSCRIPT`
   entry (`file_extension: VTT`) in a `recording.completed`/`recording.transcript_completed`
   event via its 24-hour `download_token`, or the *Get meeting recordings* REST API
   with the OAuth token.
3. It submits the transcript to **`POST ConsultGenerationJobs` as the clinician** —
   the transcript rides an existing declared slot as an `InputFilePayload`
   (VTT bytes) or `ConsultInputValue.OfText` (extracted text), exactly like a
   document.

**It is a new input *origin kind*, not a new source** — the Forms-intake precedent
(`docs/customizable-workflow/forms-intake-spike.md:206-210`): `source` stays the
opaque two-value allowlist; the run is the clinician's, from the app; what the
record needs is a new *origin*. Origin is recorded **beside** the effective-input
hash, never inside it (`Workflow/ConsultGenerationProvenance.cs`).

**Not a new container.** The transcript's *value* rides the existing `Inputs`/
`InputFiles` slot, like a document — there is **no "Transcripts" container**. Forms
added no value container either (a form answer's value also rides `Inputs`); Forms'
`InputFormRefs` is only a provenance *reference* to a **held, reusable** form
response (#540, pointed at by id). A fetched transcript is transient bytes pulled
from Zoom, not an engine-held artifact, so it needs **no `InputTranscriptRefs`
reference** — just the origin kind. (A held/reusable transcript would justify a ref;
the fetch model doesn't hold one.) That is what keeps the inventory at *one origin
kind, nothing else*.

Docs: https://developers.zoom.us/docs/api/meetings/events/ ·
https://developers.zoom.us/docs/build/cloud-recording/ ·
https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0064927

## 5. Meeting → consult association (the open design question)

The engine models **no visit/appointment/encounter** today (confirmed: nothing on
the account or the job hangs a meeting id). So "the documents for *this* meeting"
needs an association we design. Recommended **minimal, engine-unchanged** stance:
the **satellite** maps `meetingUUID` → the consult job(s) the clinician started
from that meeting (satellite-held state), and filters the clinician's existing job
History to them. A first cut can simply show the clinician's recent jobs and let
them pin the meeting's. A richer **engine-side "visit"/tag** on jobs is a larger,
separately-deferred option — deliberately out of this satellite's first scope so
the "one origin kind, nothing else" inventory holds.

## 6. Push vs fetch — why fetch

**Fetch (chosen):** the satellite pulls the transcript with the clinician's Zoom
OAuth and submits it as the clinician — **no new engine auth, no Zoom identity
link, no inbound webhook**; the only engine change is the `transcript` origin kind.

**Push (not taken):** Zoom posts a recording/transcript webhook automatically;
the engine would resolve the Zoom subject → an account via the **F2 external-identity
binding** (a new `zoom` provider link + resolution + named refusals, in the
`EmailSenderResolver` discipline — ambiguity is an explicit rejection, never a
guess) **and** stand up a net-new inbound **Zoom-signed webhook** (CRC handshake +
`x-zm-signature`). `docs/SATELLITE_CALLERS.md` §5 explicitly deferred such a
callback surface. Push buys hands-free capture of every meeting without a clinician
signed in, but at a materially larger engine inventory and a new inbound-auth
surface — and it leans more ambient. Fetch keeps the clinician in the loop
(review-first) and the perimeter clean. **Recommendation: fetch.**

## 7. Cost / gatekeeping

- **Transcription is Zoom's paid product.** Cloud recording + audio transcription
  require a paid Zoom plan (Pro/Business/Education/Enterprise) with the feature
  enabled; the **clinic pays Zoom**. No STT and no transcription cost on our side.
- The satellite is a small hosted web app (its own repo) + a Zoom Marketplace
  General app (public listing or unlisted/account install). No engine hosting
  change.

## 8. Engine-change inventory

**One input origin kind (`transcript`), registry-first — and nothing else:**

1. A new **`consultologist-provenance@v…`** version defining the `transcript` kind
   (normative; `tests/ProvenanceVersionSetTests.cs` holds the engine equal to it).
2. `ConsultInputOriginKinds.Transcript = "transcript"` —
   `src/Consultologist.Api/Models/ConsultGenerationRequest.cs` (the kind vocabulary).
3. Stamp it at the transcript slot in `src/Consultologist.Api/Jobs/ConsultGenerationJobStarter.cs`
   (the document-origin path — a transcript must stamp the new kind, not default to
   `document`).
4. A new arm in `DescribeOrigin` — `src/Consultologist.Web/Pages/History.razor`
   (else a transcript renders as "read from a document …").

No new endpoint, no new auth, no new `source`, no `zoom` provider constant, no
external-identity binding (`SATELLITE_CALLERS.md` §4/§5 stay unbuilt under fetch).

## 9. Build outline (the follow-up)

- **Satellite app** (new repo): a Zoom **General app** — the Entra-SSO webview
  panel (Leg 1, meeting context + document view) and the Zoom user-OAuth transcript
  **fetch → submit to `ConsultGenerationJobs`** (Leg 2). CSP + Domain Allow List;
  the satellite-held meeting→job map (§ 5).
- **Engine** (this repo): the single registry-first `transcript` origin kind (§ 8).
- **Zoom Marketplace**: the General app (OAuth, scopes `cloud_recording:read`), the
  Home URL/redirect on the deployed satellite, listing/enablement.

Ready to build when the satellite repo is created; no engine prerequisite beyond
the one origin-kind change.
