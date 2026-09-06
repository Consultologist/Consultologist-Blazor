# The satellite caller pattern

**Status: design record for #611 (M23 integration foundation F2, settled
2026-09-03). Part (a) — the pre-authorized-client pattern — is built,
documented and proven live (§ 6). Part (b) — external identity binding —
is designed here and built when the first spike (#190, #613, #614) demands
it.** The perimeter this pattern lives inside is #610's: only delegated
user tokens enter the API.

## 1. What a satellite is

A satellite is a separate application — a SMART on FHIR panel, a Zoom app,
a Dragon workflow step, a Microsoft 365 Copilot agent — that calls this API
**as the signed-in clinician**. It has its own app registration and presents a delegated
`access_as_user` bearer for that clinician; it never presents its own
identity. Machine callers get a designed path or none: an app-only
(client-credentials) token is refused by name before the scope check
(#610, `Auth/BearerTokenValidator.cs`). One API registration serves every
satellite, as it serves every location (#515) — a satellite is admitted to
the existing audience, never given its own.

The first satellite was the Power Automate *HTTP with Microsoft Entra ID*
connector (#542, the forms door): the pattern below is that setup,
generalized.

The family's outward half is #615: where a satellite carries a delegated
token INTO this API, On-Behalf-Of carries the clinician's delegation OUT
of it — the engine exchanging the incoming bearer for a downstream Graph
token (CONFIGURATION.md, On-Behalf-Of). Inward and outward are the same
posture: always the signed-in clinician, never a machine identity.

A **Microsoft 365 Copilot agent** (#663 spike, GO) is a designed satellite of
this exact shape and needs **no new engine surface**: an M365 Agents-SDK agent
signs the clinician in (Teams/M365 SSO) and, via On-Behalf-Of, exchanges that
token for a delegated `access_as_user` bearer to call the existing doors
(`DocumentExtractions`, `ConsultGenerationJobs`); results are fetched, not
pushed. It is admitted identically to the SPA and the EHR panels (#610 still
refuses app-only). Its own build lands in a separate repo; its publishing —
an M365 admin approving it and consenting to `access_as_user` — is #554's
activation door. Full record: `docs/COPILOT_AGENT_SPIKE.md`.

## 2. The pattern, normative (part a)

Four operator steps, once per satellite per tenant. The satellite's own
registration is its business — public client (device code, auth code +
PKCE) or confidential in its own right; the API cares only about the
token it presents.

1. **The satellite's app registration exists** — created by the
   satellite's vendor (multi-tenant) or by the operator (in-tenant).
2. **Its service principal exists in the tenant** — `az ad sp create
   --id $SATELLITE_APP_ID`; without it, sign-in fails `AADSTS650057`
   (multi-tenant apps have no SP in a consuming tenant until one is
   created).
3. **Tenant-wide delegated grant of `access_as_user`** — an
   `oauth2PermissionGrant` with `consentType: AllPrincipals`, so no
   clinician is ever prompted. The narrower alternative — per-user
   consent, granted on first sign-in — is legitimate where the operator
   wants each clinician to see and accept what the satellite reaches.
4. **Preauthorization on the API registration** — an entry in
   `api.preAuthorizedApplications`, **merged with the existing entries
   (the SPA, the connector, every prior satellite), never replacing
   them**: a PATCH that overwrites the array would silently break every
   SPA sign-in. Always read-modify-write.

The generic session (parameterized; the scope id is `access_as_user`'s,
the API app is `b3866040-8bae-4c01-88ba-ecff646df451`):

```bash
SATELLITE_APP_ID=<the satellite's appId>
SPID=$(az ad sp create --id $SATELLITE_APP_ID --query id -o tsv)
APISP=$(az ad sp show --id b3866040-8bae-4c01-88ba-ecff646df451 --query id -o tsv)
az rest --method POST --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" \
  --body "{\"clientId\":\"$SPID\",\"consentType\":\"AllPrincipals\",\"resourceId\":\"$APISP\",\"scope\":\"access_as_user\"}"

# Preauthorize: READ the current array, APPEND, PATCH — never replace.
APPOBJ=$(az ad app show --id b3866040-8bae-4c01-88ba-ecff646df451 --query id -o tsv)
az ad app show --id b3866040-8bae-4c01-88ba-ecff646df451 \
  --query "api.preAuthorizedApplications" -o json > preauth.json
python3 - <<PY > patch.json
import json
base = json.load(open("preauth.json"))
base.append({"appId": "$SATELLITE_APP_ID",
             "delegatedPermissionIds": ["56062e5c-58b4-4282-b497-27350d152114"]})
print(json.dumps({"api": {"preAuthorizedApplications": base}}))
PY
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$APPOBJ" --body @patch.json
```

Verify: `az ad app show … --query api.preAuthorizedApplications[].appId`
lists every prior entry **plus** the satellite. Reverse: delete the grant
and the SP (`az ad sp delete` drops the grant with it), PATCH the array
back without the entry.

A **browser-based** satellite has one more admission: its page's origin
joins the API's CORS allow-list via the `Cors__AllowedOrigins` app
setting (CONFIGURATION.md, #612) — a setting change, not a deploy. CSP is
not involved unless the SPA itself fetches from that origin.

## 3. What the engine checks, and what it deliberately does not

The engine checks the token: issuer (against the authority's metadata),
audience, lifetime, signature, the delegated shape (#610: `idtyp=app`
refused, no delegated scopes refused), and `Auth__RequiredScope`. It does
**not** allowlist client applications — the `azp` claim is unread. That
absence is chosen: preauthorization and the permission grant are
Entra-side admission, and the tenant admin's grant is the gate; an
engine-side copy of that list would be a second registry to drift. The
revisit trigger, named: a satellite that must be *distinguished* — given
more, less, or different behaviour than the SPA — would need `azp` read
and a designed policy behind it. No current satellite needs it; that
issue does not exist yet and should be filed when one does.

## 4. External identity binding — designed (part b); `epic` + `cerner` built (#654, #662)

**Built for `epic` (#654) and `cerner` (#662):** the clinician links their
EHR identity from the SMART panel — the panel holds the SMART id_token and
POSTs it to `Account/{provider}/Link` under the clinician's Entra bearer;
the engine validates it (a per-installation issuer allowlist — the shared
`SmartIdTokenValidator`, one config prefix per EHR) and binds the provider
via `LinkIdentityAsync`, keyed `{iss}|{sub}`. Both are proof/display only:
never a bearer credential, and — the one refinement to the design below —
**not an activation signal** (`IdentityProviders.ActivatesAccount` gates
status to LinkedIn alone). No intake door consumes the `epic`/`cerner` row
today: the panel presents the clinician's Entra token, so the account
resolves from that; the row is proof and readiness for a future
launch-context resolution. Cerner (#662) was the Epic pattern generalized
to a second EHR — the validator became shared, the store/`Account/Me` were
already provider-parameterized, so it was mostly a new prefix + a thin
`AccountCerner` endpoint + a Profile card. The `zoom` provider remains
designed, not built.


A satellite-borne payload — a Zoom transcript webhook, an Epic launch
context — arrives bearing an **external subject** (a Zoom user id, an
Epic user), not an Entra token. Resolution follows the LinkedIn precedent
(#133/#197), generalized; nothing below is built until a spike demands it.

- **The rows**: a provider-keyed pair in the existing tables — the lookup
  row (`IdentityLinks`: PK provider, RK subjectHash) and the reverse row
  (`UserIdentityLinks`: PK appUserId, RK `{provider}-{subjectHash}`),
  written together as today (`AccountStore.LinkIdentityAsync`). The
  provider name (`zoom`, `epic`) joins `IdentityProviders` with LinkedIn's
  own sentence: linked for proof of account control, **never accepted as a
  bearer credential** — only `entra-external-id` signs in.
- **Who holds the pen**: the clinician, from inside the app — the #133
  flow shape (start from the profile → prove control at the external
  service → callback → `LinkIdentityAsync`). The external service never
  creates the mapping; a satellite that could write its own binding would
  hold the account book.
- **Conflicts**: `DecideLinkOutcome` as today — no row links, the same
  account is idempotent, another account's row refuses.
- **Resolution**: the satellite payload's subject hashes to the lookup
  row. Absence refuses; ambiguity cannot arise by construction (the
  lookup key is unique), but any resolution that becomes a scan inherits
  the `EmailSenderResolver` discipline verbatim: ambiguity is an explicit
  rejection, never a guess.
- **Unlink**: allowed, as for LinkedIn — non-credential rows only.

What the first spike must build: the provider constant, the link flow's
external leg (each provider's own proof-of-control ceremony), the
resolution call at its intake door, and the named refusals for the unbound
and the conflicting subject.

## 5. Candidates not taken

- **App-only access for satellites** — refused by name, #610; a machine
  identity is never a clinician.
- **Outbound push/webhooks to satellites** — satellites poll or hold SSE
  with the delegated token; a callback design waits for a spike that
  demands one.
- **Satellite-created bindings** — see § 4; the clinician links from
  inside the app, always.
- **An engine-side client allowlist** — see § 3; Entra-side admission is
  the design until a satellite must be distinguished, not merely admitted.

## 6. The proof (2026-09-03)

The generalized recipe was run once with a scratch satellite and fully
reversed, live against the production API (engine commit `991d3bf`):

- `scratch-611-satellite` created as a public client
  (`8f8302ff-42b0-4d8e-9071-a2907a7cce45`), SP created, tenant-wide
  `access_as_user` grant written, entry APPENDED to
  `api.preAuthorizedApplications` — the read-modify-write left the SPA and
  connector entries intact (three entries after, the prior two verbatim).
- Device-code sign-in through the scratch client prompted **no consent**
  (the tenant-wide grant) and minted a delegated token of exactly the
  satellite shape: `aud` = the API, `azp` = the scratch app,
  `scp=access_as_user`, no `idtyp`.
- `GET /api/Account/Me` with it answered **200 as the signed-in
  clinician** — the operator's existing account, resolved by subject; no
  new account row appeared.
- Reversal: grant deleted, array PATCHed back to the two baseline entries,
  registration deleted (listing count 0), token discarded. The tenant
  ended unchanged.
