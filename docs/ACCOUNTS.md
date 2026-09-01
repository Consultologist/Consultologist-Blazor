# Accounts, Entra API Access, and LinkedIn Linking

## Summary

This app uses Microsoft Entra ID login through MSAL in the Blazor WebAssembly client. Protected Azure Functions validate an Entra access token for the Consultologist API and resolve the caller to an internal app account stored in Azure Tables.

LinkedIn is a **verification signal**, not a login provider (#133): a Connect LinkedIn flow on the Profile page proves the signed-in user controls a real LinkedIn identity, and the stored link feeds the operator's manual activation decision. Microsoft remains the only credential authority. The flow is enforced server-side in the Azure Functions project — browser-only checks are never authorization.

## Current Entra Setup

The production setup uses two app registrations:

- **Consultologist SPA**: `7aea065e-4632-43d3-adb7-9cd315f2b8da`
- **Consultologist API**: `b3866040-8bae-4c01-88ba-ecff646df451`

The SPA signs users in and requests the delegated API scope only when calling protected Functions:

```text
api://b3866040-8bae-4c01-88ba-ecff646df451/access_as_user
```

The API app registration must expose:

```text
Application ID URI: api://b3866040-8bae-4c01-88ba-ecff646df451
Scope: access_as_user
requestedAccessTokenVersion: 2
```

The SPA app registration must have delegated permission to the API scope.
Since #542 the Power Automate *HTTP with Microsoft Entra ID* connector
(`d2ebd3a9-1ada-4480-8b2d-eac162716601`) is likewise granted
`access_as_user` and preauthorized on the API registration — the forms
door's flows authenticate as their owner with no consent prompt
(CONFIGURATION.md has the operator paragraph; the spike record § 2.5 the
runbook).

Since 2026-07-18 sign-in is **multi-tenant**, and since 2026-07-23 (#132) it
also accepts **personal Microsoft accounts**: both registrations use
`signInAudience: AzureADandPersonalMicrosoftAccount`, and the API
registration lists the SPA client id in `api.knownClientApplications` so a
foreign tenant grants one combined consent covering both apps (see the
"Multi-tenant sign-in" section in `CONFIGURATION.md` for where each setting
lives, the MSA-specific registration constraints, and what a foreign tenant
still needs).

The Function App settings should be:

```text
Auth__Authority=https://login.microsoftonline.com/common/v2.0
Auth__Audience=b3866040-8bae-4c01-88ba-ecff646df451
Auth__RequiredScope=access_as_user
AccountStorage__TableServiceUri=https://consultjobrecscaeast.table.core.windows.net
```

Expected access-token claims (the issuer varies per signing tenant; the
validator accepts any tenant's issuer under the `common` authority — for
personal accounts that tenant is Microsoft's consumer tenant
`9188040d-6c67-4c5b-b112-36a304b66dad`):

```text
ver=2.0
iss=https://login.microsoftonline.com/<tenant-id>/v2.0
aud=b3866040-8bae-4c01-88ba-ecff646df451
scp=access_as_user
```

Accounts are tenant-agnostic: a first sign-in from any organizational tenant
resolves-or-creates an app account exactly like a home-tenant sign-in.

Bearer tokens must not be pasted into logs, issues, or chat. Use decoded claim summaries when debugging.

## Account Statuses and Activation (#191)

New accounts land **`Pending`** (since #191; before that they were created
`Active`, despite docs claiming otherwise). The activation flip in the
`AppUsers` table is the sole admission control for self-provisioned sign-ups.

| Status | Meaning |
|---|---|
| `Pending` | Created on first sign-in; every protected endpoint returns 403 |
| `Active` | Activated by the operator; full access |
| `Unverified` | #195: was Active, unlinked its LinkedIn — may use the app, may not start consults |
| `Disabled` | Deliberately turned off by the operator; 403 everywhere — and the precondition for closure (#559) |

Comparison is ordinal and case-sensitive (`AccountAuthorizer.CanUseApp`), so
the exact strings above are the vocabulary. The one exception to the 403
gate is `GET /api/Account/Me`, which returns the caller's own profile
(including `Status`) for any authenticated account so the client can show an
"awaiting activation" banner instead of a broken app.

Activation is an operator's table write on the account store (`Status`
becomes `Active`; `Disabled` turns an account off); the recipe lives in the
operations repository (private, #397). Existing rows
created before #191 keep their `Active` status — no migration.

## Operators (#384)

There is no admin role. An **operator** is an ordinary account whose AppUserId
is listed in `Operators__AppUserIds` (see CONFIGURATION.md); the list is empty
by default, so no surface is reachable until it is set. Operator surfaces
were read-only reports until #559 — `GET /api/Operator/PinHealth`,
`GET /api/Operator/Usage`, `GET /api/Operator/CatalogStrands` — gated like
every other function (bearer token, `CanUseApp`) plus the list. Status
changes are still done with `az`, by an operator, from the operations
repository's recipes.

**Account closure (#559)** is the one mutating operator surface:
`POST /api/Operator/Accounts/{appUserId}/Close` removes everything the
account owns — text, package forks, records and links, the index, settings,
identities, the `AppUsers` row — in the order storage-separation § 2.6
records, and writes one `ClosedAccounts` audit row (ids and counts only).
Two steps by design: the account must already be `Disabled` (the az write
above), or the call answers 409 naming its status — a slip is never final.
Idempotent: closing a closed account answers 200 with `alreadyClosed` and
deletes nothing. What stays: usage counts, rate-limit rows, the email claim
ledger. The sign-in that created the account starts a new, Pending account
if it returns.

## Account Settings

Authenticated user preferences are stored server-side in Azure Table Storage so they follow the app account across browsers and devices.

Current tables:

- `AppUsers`: internal app user records.
- `IdentityLinks`: provider identity to app user lookup.
- `UserIdentityLinks`: app user to linked identities lookup.
- `AccountSettings`: per-user settings keyed by app user ID and setting key.

The generic settings endpoint shape is:

```text
GET    /api/Account/Settings/{key}
PUT    /api/Account/Settings/{key}
DELETE /api/Account/Settings/{key}
```

`GET /api/Account/Usage?from=YYYY-MM-DD&to=YYYY-MM-DD` (#552) serves the
account's own usage days from the derived `AccountUsage` store — consults
completed and token counts per UTC day, numbers only, nothing naming a
consult's content. Written once at each run's completion from the numbers
#551 stamps on the record and never re-derived; empty until the first
completed run after 2026-08-31. Defaults to the last 30 days; an
oversized window clamps to 92 days; unparseable dates and an inverted
range refuse by name.

`GET /api/Operator/Usage?from&to` (#553) is the same window over every
account, for callers on `Operators__AppUserIds` only (the bare-403 gate
every operator surface uses): one row per account with usage in the
window — its id, the display name the account already carries, its kind,
and its issuer tenant parsed from the stored Entra identity (personal
accounts carry the consumers tenant there). Reads only the derived
store, never job records; the /operators page groups rows by tenant.
`Account/Me` reports the caller's own `IsOperator` so the nav can show
the link — a fact about the caller, never the gate.

The forms-intake door (#539): `POST /api/Intake/Forms/Responses` holds a
form response for the token's account — organisation sign-in required
(a personal token is 403 `personal-account`, the #517 rule), every
status answering JSON (the Power Automate connector treats any non-JSON
body as an error). The values live in one blob on the text account
(`org-`/`personal-form-responses`); the `FormResponses` row on the
records account carries form id, response id, submitted, the input ids
present, the blob pointer and the deleted stamp — never a value. A
second push of the same response is 204 and rewrites nothing, including
one the sweep already deleted. `GET .../Responses` lists; `DELETE
.../{formId}/{responseId}` discards (blob deleted, row stamped — the
list says deleted). Logs carry ids, counts, lengths and outcomes only.

The current setting keys are:

```text
consult.workflowPackage
consult.scheduleTime
consult.location            (the record of the location a device chose, #515 — the device decides)
delivery.documentPassword   (write-only — see below)
delivery.address            (reserved — set only by confirmation, see below)
delivery.addressPending     (reserved — the code out to an address)
delivery.addressVerifiedBy  (reserved — "code" | "tenant", how it was verified)
delivery.emailPdf           ("true" | "false" — whether app-initiated runs are emailed; absent = not chosen = sent)
profile.signatures          (JSON — the profile's signature blocks and the chosen one, #516)
retention.outputDays        (whole days produced text is kept after completion, #548 — 1..30; absent = TextRetention__Days)
retention.inputDays         (whole days held inputs are kept, #548 — 1..30, never past outputs; absent = TextRetention__Days)
```

`delivery.emailPdf` (#518) is a preference, not an identity, so it rides
the generic routes like `consult.scheduleTime`. Only a stored `false`
turns the completion email off; anything else — absent, blank,
unreadable — is *not chosen*, which sends, as before the option existed.
The starter reads it when an app job starts or is rescheduled, and a run
started under `false` records `deliveryOutcome: not-requested`
(`docs/ASYNC_DELIVERY.md` §4). Email-door jobs ignore it.

`profile.signatures` (#516) holds the profile's named signature blocks and
which one is chosen, as one JSON row (PascalCase:
`{ "Blocks": [{ "Id", "Name", "Text", "UpdatedAtUtc" }], "ChosenId" }`).
A preference-shaped identity artifact — it rides the generic routes; the
Web writes it, and the job starter reads it at start when a package marks
a deliverable signed (the chosen block is snapshotted onto the job, the
EmailRequested principle). Explicit initialisation: absent, blank, or
unreadable is an empty set, and with no chosen block a signed deliverable
is produced unsigned, said by name on the record and in History.

`retention.outputDays` / `retention.inputDays` (#548) are the account's
retention clocks — how many whole days a completed run's produced text
and held inputs are kept before the daily sweep deletes them. Preferences
on the generic routes, but the one pair with a cross-key rule: the PUT
validates `1 ≤ inputDays ≤ outputDays ≤ 30` against the stored sibling
(`Auth/RetentionSettings.cs`) and refuses by name — inputs never outlive
outputs, and 30 days is the storage lifecycle ceiling. Absent means the
deployment default (`TextRetention__Days`). The sweep reads tolerantly:
a value that is missing, unreadable, or out of shape falls back and is
clamped, so nothing is ever kept past the ceiling. A shortened setting
applies to runs already held at the next sweep; the record's hashes and
history are never subject to retention.

`delivery.address` (#486) is the account's **verified delivery address**:
the only address app-submitted consults are ever emailed to. It is written
by one of two paths: `POST /api/Account/DeliveryAddress` (sends a six-digit
code to the address) followed by `POST /api/Account/DeliveryAddress/Confirm`
(15 minutes, five attempts) — every account; or, since #517,
`POST /api/Account/DeliveryAddress/UseSignedIn` — **an organisation's
sign-in only**: the token's `tid` must not be the consumers tenant
(`9188040d-…`, a personal Microsoft account, which is refused by name and
keeps the code), the address is the token's own email claim and no body is
accepted, so a client cannot name a different one. The organisation has
already verified that mailbox is the signed-in person's; the app records
which trust the address rests on in `delivery.addressVerifiedBy` (`code` |
`tenant`, shown on the card as *Verified by your organisation*).
`DELETE /api/Account/DeliveryAddress` removes all three. The generic
settings routes refuse the three keys in both directions, so nothing can
plant an address nobody confirmed, or claim a trust nobody extended.
`Account/Me` reports `deliveryAddress`, `deliveryAddressPending`,
`deliveryAddressVerifiedBy`, and — this token's, never stored —
`signInEmail` and `signInKind` (`organisation` | `personal`). The address
is a delivery target, never an identity — see "Do Not Do".

`delivery.documentPassword` (#159) is a **secret**: it never travels the
generic settings routes (they refuse the key in both directions). It is
managed only through `PUT`/`DELETE /api/Account/DeliveryPassword`
(16-character minimum) and its existence surfaces only as `Account/Me`'s
`documentPasswordSet` flag. When set, completion emails attach the consult
as an AES-256 password-protected PDF; the trust boundary is stated in
`docs/ASYNC_DELIVERY.md` §3 — it protects the document in the mailbox, not
from the server, which produced the plaintext.

It holds the account's workflow-package pin (`name` or `name@version`), resolved server-side at job start; when unset, the `WorkflowPackages__Default` app setting (`general@latest`) applies. `GET` returns `404` when the setting has not been saved. `PUT` accepts a JSON body with `value` and `contentType`. `DELETE` removes the pin and restores the default resolution.

Historical: the original key, `consult.sectionStandardsMarkdown` (a per-account section-standards override), retired 2026-07-15 with the specVersion-5 input model (#71) — section standards are now package data, and account customization is package forking (the in-app editor, #57; see docs/customizable-workflow/in-app-editing.md).

## LinkedIn Linking (#133, implemented)

LinkedIn is an account-**verification** signal, explicitly not a login
provider — that would mean re-platforming auth, rejected in favor of
Microsoft remaining the only credential authority. The Connect LinkedIn
button on the Profile page runs LinkedIn's Sign-In-with-LinkedIn OpenID
Connect code flow through two server-side Functions endpoints
(`AccountLinkedIn.cs`); the client secret lives in Function App settings
(`docs/CONFIGURATION.md`, "LinkedIn identity linking") and never reaches the
browser.

Flow:

1. `GET /api/Account/LinkedIn/Start` (bearer-authed; **no IsActive gate** —
   linking is an input to activating a `Pending` account): validates the
   browser `Origin` against the CORS allow-list, writes a single-use state
   row (`LinkedInLinkStates` table: CSPRNG state + nonce, the app user id,
   the return origin, 10-minute expiry) and returns the LinkedIn
   authorization URL as JSON; the client navigates top-level.
2. The user consents on linkedin.com (scopes `openid profile email`;
   personal LinkedIn credentials never touch this app).
3. `GET /api/Account/LinkedIn/Callback` (anonymous — the browser arrives by
   redirect): consumes the state (ETag-conditioned delete; a replayed or
   expired callback gets a 400, not a redirect), exchanges the code
   server-side, validates the id_token (LinkedIn issuer, our client id
   audience; the nonce is sent in the authorize request but validated
   opportunistically — LinkedIn does not echo it into id_tokens, so the
   single-use state is the replay defense), and stores the link. Only the id_token is consumed —
   the access token is discarded and LinkedIn APIs are never called on the
   user's behalf. The browser is then 302'd to
   `{origin}/profile?linkedin=connected` (or `…?linkedin=error&reason=…`).

What is stored (`IdentityLinks` PK `linkedin` / `UserIdentityLinks`): the
stable OIDC `sub` (pairwise per LinkedIn app), issuer
`https://www.linkedin.com/oauth`, and the token's name/email/picture
claims. One LinkedIn identity vouches for at most one app account —
linking an identity already attached to a different account is refused
(`already-linked`). Re-linking the same account is idempotent and
refreshes the claims. `Account/Me` exposes the link in `LinkedIdentities`
so both the user and the operator (reviewing a `Pending` account) can see
it. Since #195 a user can disconnect it themselves
(`DELETE Account/LinkedIn`), which deletes both rows; the sign-in identity
(`entra-external-id`) is refused, since removing it would orphan the
account from its own credentials.

Meaning of "verified", two layers:

1. **Proof of account control** (always): a real, demonstrably
   user-controlled LinkedIn identity — an OAuth proof, never a typed-in
   profile URL.
2. **Verified on LinkedIn categories** (best-effort): the callback also
   calls `GET https://api.linkedin.com/rest/verificationReport` (scope
   `r_verify`, `LinkedIn-Version` per `LinkedIn__ApiVersion`) with the
   just-issued access token and stores the member's verified categories
   (`IDENTITY` = government-ID, `WORKPLACE` = work email/Entra) on the
   link. The app's LinkedIn registration has the self-serve **Verified on
   LinkedIn** product attached, currently the **Development tier**: the
   report is only available for members who are admins of the developer
   app (everyone else gets 403 → categories stay empty, linking still
   succeeds). Apply for the Lite tier before external users.

**Changed by #195.** Linking now activates directly: a successful link moves
`Pending` or `Unverified` to `Active`, and disconnecting moves `Active` to
`Unverified`. The manual runbook above is the fallback rather than the path.

`Unverified` is the third status — activated once, on evidence since
withdrawn. It reads everything the account already made and cannot start new
consults, enforced at the two creation doors (`ConsultGenerationJobs` start
and `EmailSenderResolver`) via `AccountAuthorizer.CanStartConsults`, while
everything else takes `CanUseApp`.

> **Open question this raises (#388).** What activates now is the *link* — proof of
> control over a LinkedIn account — not the Verified on LinkedIn categories,
> which remain display-only. That distinction matters, because the Verified
> API is licensed for trust enhancement, **not for eligibility decisions,
> employment screening, or KYC**, and this document previously read "never an
> automated gate" partly for that reason. Automating on the link rather than
> on the verified categories is intended to stay the right side of that line,
> but it should be confirmed against the current LinkedIn terms before
> external users arrive.

## Do Not Do

- Do not store LinkedIn client secrets in Blazor WebAssembly.
- Do not trust browser-side checks as authorization.
- Do not merge accounts based only on matching email addresses.
- Do not treat a linked LinkedIn identity as proof of the profile's claims —
  it proves control of the account, nothing more.
- Do not use mutable profile fields, display names, or email alone as
  permanent account keys (links key on provider + issuer + `sub`).
- Do not send anything to `AppUsers.Email` (a token claim — unverified, not
  unique) and do not treat the verified delivery address as an identity:
  it is where consults go, not who the account is (#486). #517 does not
  bend this: the signed-in email becomes a target only when the user
  chooses it on the card, on an organisation's token, and it is written as
  the verified address like any other — the stored `AppUsers.Email` is
  still never a target by itself.
- Do not accept a LinkedIn identity as a bearer credential — only
  `entra-external-id` identities sign in.
