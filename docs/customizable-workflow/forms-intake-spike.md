# Microsoft Forms as an intake source — spike record (#511)

**Status: spike record for Milestone 21 (research settled 2026-08-28;
experiments E1–E4 run 2026-08-29, § 6; split filed, § 7).** #511 asked whether a Microsoft Forms
questionnaire response can fill a package's declared inputs the way an
email (#157–#159) or a document (#235–#240) does, and said *exploration
first*: which integration path is reachable for a personal Microsoft
account and an organisation tenant alike, whether a pre-filled hidden
question survives submission, and what the response payload looks like
per input type. This record answers what can be answered from the
documentation and the engine, states the two decisions taken with the
operator, proposes the design the split issues will build, and carries
the experiment protocol whose results complete it.

Decisions taken with the operator: responses are **staged, not
automatic** — the clinician's flow pushes each response into a held list,
and on the setup form a *Load from a form response…* picker (the #510
pattern) fills the declared inputs for review before a run; and the flow
authenticates with **the clinician's own token, no secrets** — a
delegated `access_as_user` bearer, so the account is the token's and
nothing is stored.

## 1. What exists

Two doors start a consult today, and the record says which
(`source`: `app` | `email`, #158). The app door takes typed values
(v8 § 4), documents (#238; the server extracts and records the origin)
and, since #510, a previous run's deliverable (the server copies and
records the origin). The email door
(`src/Consultologist.Api/Email/EmailIntakeProcessor.cs`) claims each
message exactly once before reading it, matches the authenticated sender
to an account, fills declared inputs from the body and attachments
(`EmailAttachmentInputs.Resolve`), and starts the job with
`ConsultGenerationJobOrigin(Source: "email", ReplyToAddress)`. Every
emailed value is text: *a v8 package with a required boolean input is
unreachable by email* (`EmailIntakeProcessor.cs:325-327`). A
questionnaire is the first source that can carry a boolean, an enum, a
number or an array as such.

## 2. What the documentation establishes

### 2.1 There is no supported API for Forms responses

Microsoft Graph exposes Forms only as beta administrator settings
(`GET /admin/forms`, [adminForms][graph-admin]); no Graph resource returns
a form's responses, and Microsoft's answer to the request is *use Power
Automate, or the Excel workbook* ([Q&A, 2026-03][qa-api]). The
`forms.office.com/formapi/api/…/light/forms('…')/responses` endpoints the
Forms web client uses are undocumented, have moved host
(`forms.cloud.microsoft`), and are being shut out of the connectors that
used to reach them; they are not a foundation. The supported paths are:

- the **Microsoft Forms connector** in Power Automate ([reference][connector]):
  one trigger, *When a new response is submitted* (a webhook carrying only
  `responseId`), and one action, *Get response details*
  (`form_id`, `response_id` → the answers); the trigger's poll is once a
  day, the webhook is immediate;
- the **Excel workbook** a form syncs to in OneDrive/SharePoint (since
  November 2024 it syncs only while the workbook is open — a poor sink),
  or a SharePoint list fed by a flow.

A door that *pulls* from either sink would need a delegated Graph consent
per clinician — the first in the system, which today calls Graph only as
its own managed identity (`GraphMailClient`, application permissions
restricted to one mailbox by an Exchange access policy). A door that is
*pushed to* by the clinician's flow keeps the identity model intact. The
push is the design.

### 2.2 Personal Microsoft accounts are out — twice

The Forms connector "only works with organizational accounts"
([known issues][connector]), and Power Automate cloud flows have required
a work or school sign-in since 27 July 2025 ([FAQ][pa-faq]). A personal
Microsoft account can *fill in* a form but cannot own the flow that moves
its responses. The Forms door is therefore an **organisation-sign-in
feature**, the carve-out #517 already draws for the delivery address
(`DeliveryAddress.IsOrganisation`, the consumers tenant refused by name);
a personal account keeps the app and email doors.

### 2.3 Pre-filled links exist; hidden questions do not

Since May 2024 a form's *… → Get Pre-filled URL* produces a link of the
shape `…/ResponsePage.aspx?id=<form>&r<questionId>=<value>&…`
([Neowin][prefill-news]; practitioner write-ups agree on the URL shape).
Two properties matter here: the pre-filled value is **visible and
editable** by the respondent — Forms has no hidden question type — and it
is **not applied when the form restores a respondent's earlier session**
(a form not open to *anyone* remembers the last answers). A per-consult
token carried this way would be a visible field the patient could alter
or that could silently not appear. The staged design (§ 4) does not need
one: a response is matched to a clinician by the *flow owner's token*,
and to a consult by the clinician choosing it.

### 2.4 The payload, per question type

*Get response details* returns one flat JSON object
([write-ups][payload-1], [payload-2]):

```json
{
  "responder": "someone@clinic.example",
  "submitDate": "7/28/2021 10:41:26 AM",
  "r917f27f11e4b433c93bfad308c133b94": "[\"IT\",\"Finance\"]",
  "rbce4c992fc4f4453b01567fe21e17099": "2021-07-28",
  "re2c5f1cc0fc14413ad38347f3d5ef9c4": "4",
  "r9d6a6441c0244a269fcafc4fdb879c16": ""
}
```

- **Every answer is a string**, keyed by the question's id
  (`r` + 32 hex), never its title. Single choice → the option's text;
  multiple choice → a JSON array *as a string*; date → `yyyy-MM-dd`;
  number → the digits typed; rating → digits; an unanswered question → `""`.
- `responder` is the respondent's UPN only when the form is restricted to
  the organisation; otherwise `"anonymous"`. `submitDate` is a US-format
  local string, not ISO — the flow carries `utcNow()` instead.
- **File upload** questions are org-only; the answer is a JSON array of
  `{ name, link, id, size, driveId, … }` pointing at SharePoint. The link
  is to the *clinician's* tenant; the API cannot follow it without a
  delegated consent — out of scope (a document still comes by the app or
  email door).
- A single-line text question truncates at 255 characters; a referral
  needs a **long-answer** question.

The flow's dynamic-content panel offers the answers *by question title*,
so the mapping *question → declared input id* is authored once, in the
flow, as a JSON body keyed by the package's input ids (§ 4.4). The API
never learns question ids or titles unless the flow sends them.

### 2.5 The flow can authenticate as the clinician, without a secret

Power Automate's plain *HTTP* action authenticates to Entra only as an
application (client secret or certificate in the flow). The **HTTP with
Microsoft Entra ID** connector ([reference][webcontentsv2]; premium) sends
a **delegated** token for the connection's user to a resource of the
maker's choosing — exactly the `access_as_user` bearer the SPA sends, so
`AccountAuthorizer` resolves the same account and nothing is stored
anywhere. What it needs, once per tenant:

- the connector's client app, `d2ebd3a9-1ada-4480-8b2d-eac162716601`
  (*PowerPlatform-webcontentsv2-Connector*), has **no service principal
  in a tenant until one is created**; without it the connection fails
  `AADSTS650057 Invalid resource` ([write-up][blimped]);
- an `oauth2PermissionGrant` giving that principal the API's delegated
  `access_as_user` scope (Microsoft's [PowerShell script][pa-consent], or
  two CLI-for-Microsoft-365 commands);
- the API registration lists it under *Authorized client applications*,
  so the clinician sees no consent prompt beyond the connection.

**Tenant setup, done 2026-08-28** (the operator's `az` session, tenant
`consultologist.ai`; each step reversible):

```sh
C=d2ebd3a9-1ada-4480-8b2d-eac162716601          # PowerPlatform-webcontentsv2-Connector
API=b3866040-8bae-4c01-88ba-ecff646df451        # the API registration
SCOPE=56062e5c-58b4-4282-b497-27350d152114      # its access_as_user scope id

# 1. The connector's service principal (absent until created).
az ad sp create --id $C
# 2. Tenant-wide delegated consent: the connector may call the API as the signed-in user.
az ad app permission grant --id $C --api $API --scope access_as_user
# 3. Preauthorize the connector on the API registration — MERGED with the
#    existing entries (the SPA), never replacing them.
cur=$(az ad app show --id $API --query api.preAuthorizedApplications -o json)
new=$(python3 -c "import json,sys; a=json.loads(sys.argv[1]); a=[x for x in a if x['appId']!='$C']; \
  a.append({'appId':'$C','delegatedPermissionIds':['$SCOPE']}); print(json.dumps(a))" "$cur")
objid=$(az ad app show --id $API --query id -o tsv)
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$objid" \
  --headers "Content-Type=application/json" --body "{\"api\":{\"preAuthorizedApplications\":$new}}"
```

Verify: `az ad sp show --id $C`; the grant via
`oauth2PermissionGrants?$filter=clientId eq '<sp object id>'` shows
`access_as_user` / `AllPrincipals`; `az ad app show --id $API --query
api.preAuthorizedApplications[].appId` lists both apps. Reverse with
`az ad sp delete --id $C` (drops the grant too) and a PATCH that omits the
entry. A second region's API is a second registration only if it has one;
today one registration serves every location (#515), so this is once.

The older *HTTP with Microsoft Entra ID (preauthorized)* connector uses a
first-party app preauthorized only for Microsoft services and is being
retired in favour of discrete consent; it is not the path. The fallback,
should E1 fail, is a **custom connector** with managed-identity
authentication (federated credential, preview) — still no secret.
Either HTTP connector needs a Power Automate **premium** licence for the
flow's owner.

## 3. What the engine establishes

- **Nothing today authenticates on anything but an Entra bearer.** Every
  HTTP function is `AuthorizationLevel.Anonymous` with in-process
  validation (`BearerTokenValidator`, `AccountAuthorizer`); no function
  key, shared key or webhook secret exists in code or configuration. The
  identity-only rule the docs state is about *outbound* storage access;
  the de-facto inbound posture is stronger, and § 2.5 keeps it.
- **Typed values already exist on the wire.** `ConsultInputValue.OfText /
  OfBoolean / OfNumber(spelling) / OfArray` and the starter's
  `InputsMismatch` (422) validate a value against the declaration; a
  Forms answer coerces from its string by the declared type (§ 4.2).
- **`source` is opaque end to end** (`ConsultGenerationJobSources`,
  `GateWaitFor`, History's two-value allowlist, `provenance-record.md`
  row 40). The staged design does not add a source (§ 4.3): the run is
  the clinician's, from the app; what the record needs is an *input
  origin*.
- **The no-PHI rules apply unchanged** (#245, #217): a response is PHI
  throughout; logs carry form id, response id, input ids, counts and
  lengths — never an answer, never a question title (a title such as
  *"Patient name"* is harmless, but the rule is simpler without the
  exception).

## 4. The design the split issues build

### 4.1 Held responses (API)

`POST Intake/Forms/Responses` — bearer; **organisation sign-in required**
(`DeliveryAddress.IsOrganisation`; a personal token → 403
`personal-account`, the #517 rule). Body:

```json
{ "formId": "…", "responseId": 17, "submittedAtUtc": "2026-09-01T14:02:11Z",
  "inputs": { "consult_draft": "…", "urgent": "Yes", "prior_notes": "[\"…\",\"…\"]" } }
```

Stored in a `FormResponses` table (PK `appUserId`, RK `formId:responseId`)
with the strings as sent; a second push of the same response is 204 (the
flow may retry). The row carries the text-retention clock (#368): the
sweep deletes the values `TextRetention__Days` after `submittedAtUtc`
and stamps `deletedAtUtc`, a named state; the row stays so the list can
say *deleted*. `GET Intake/Forms/Responses` lists the account's held
responses (form id, response id, submitted, the input ids present, held
or deleted on which day); `DELETE …/{formId}/{responseId}` discards one.
Values never appear in logs.

### 4.2 The picker and the coercion (Web + starter)

On the setup form, *Load from a form response…* beside *Load from a
previous run…*: held responses newest first, a deleted one listed and
not choosable (*values deleted Sep 8, 2026*). Choosing one fills every
declared input whose id the response carries, **coerced by the
declaration**: `text`/`date` as the string; `enum` only when the string
is one of the declared `Values` (else named, not filled); `boolean` from
`Yes`/`No`/`true`/`false` (case-insensitive; else named); `number` by
`ConsultInputValue.TryParseNumber` (spelling kept; else named); an
`array` of text from a JSON-array string, or from a single string as one
element. Inputs the response does not carry stay as they are. The form
then behaves as a typed form: the clinician reviews and may edit before
running — a questionnaire answer is the patient's words, and the
clinician is the author of the consult.

The request carries, per filled input, a reference
`{ formId, responseId }` (`InputRefs`' sibling, `InputFormRefs`) beside the
typed values. At start the server verifies the held response still holds
those values and records the origin per input:
`{ kind: "form-response", sourceFormId, sourceResponseId, textSha256 }` —
the #510 shape, observed by the server (the value the form ran on equals
the held value; an edited value is typed text and carries no origin).
`source` stays `app`: the clinician chose, reviewed and ran.

### 4.3 The record (registry)

`inputOrigins` gains the kind `form-response` with `sourceFormId` and
`sourceResponseId` (a version bump of the provenance registry, as #510's
`previous-run` was). A form id is not a name; a response id is an
integer.

### 4.4 The flow recipe and the tenant setup (docs + one operator step)

Documented for the clinician: one automated cloud flow per form —
*When a new response is submitted* → *Get response details* → *HTTP with
Microsoft Entra ID: Invoke an HTTP request*, `POST
/api/Intake/Forms/Responses`, body a JSON object keyed by the package's
declared input ids, each value the matching question's dynamic content,
plus `formId`, `responseId` and `utcNow()`. Multiple-choice answers are
passed as they arrive (a JSON array string). For the operator, once:
create the connector's service principal, grant it `access_as_user`,
authorize it on the API registration (§ 2.5).

## 5. What is not in this design

- A per-consult link or token (§ 2.3) — the staged picker replaces it.
- Automatic runs from a response — the operator chose review first; an
  *auto-run* switch could be a later profile option with #518's shape.
- Following file-upload links into the clinician's tenant (§ 2.4).
- Personal Microsoft accounts (§ 2.2).
- A reply to the respondent — the respondent is the patient; the consult
  is delivered to the clinician as every app run is (#486).

## 6. Experiments (operator-run; results recorded here)

The engine cannot run these: a form and a flow are artefacts of the
operator's tenant. Each has a result slot.

**E1 — auth.** Tenant setup once (§ 2.5), then a flow with *HTTP with
Microsoft Entra ID → Invoke an HTTP request*, resource
`api://b3866040-8bae-4c01-88ba-ecff646df451`, base URL
`https://east.ca.api.consultologist.ai`, connected with the organisation
account: `GET /api/Account/Me`. Expected 200 with the account. Then the
same connection attempted from a personal Microsoft account: expected to
fail at the connector.
*Result (2026-08-28, partial):* the tenant setup above took effect within
minutes — a connection for `api://b3866040-…` with the organisation account
completed with **no consent prompt** (the token side of E1 holds). Two
findings on the way there, both for the flow recipe (issue D):
- *Making the connection is convoluted in the new designer.* Its
  in-editor dialog asks only for the Entra Resource URI, and a connection
  made there never completes (*Invalid connection, please update your
  connection to load complete details*). The fields are easy to swap on
  the Connections page too — the base URL in the resource field gives
  `AADSTS500011: The resource principal named https://east.ca.api.… was
  not found`. What worked: create the connection from the action, then
  **edit it on the Connections page** to add Base Resource URL
  `https://east.ca.api.consultologist.ai` beside the resource URI, then
  re-enter the action's method and URL. A solution flow also binds to a
  *connection reference*, not the connection — a plain flow (*My flows →
  New*) is simpler for this.
- *The run is gated by licensing.* The flow checker: *This flow's owner
  needs a Power Automate Premium license.* Both HTTP connectors and custom
  connectors are premium, so **every clinician running a Forms flow into
  the API needs a premium licence** (a 90-day self-service trial exists).
  The one licence-free way out of a Forms flow is a standard connector —
  *Office 365 Outlook → Send an email* — i.e. a **Forms → email-door
  bridge**: the flow mails the answers from the clinician's own mailbox to
  the intake mailbox, matched by sender as today (§ 1), at email's cost
  (text only, no staged review). To be weighed in the split.
*Result (2026-08-29, organisation half closed):* with a Premium licence
assigned, the flow ran green and `GET /api/Account/Me` answered **200**
with the operator's own account — the same `AppUserId` the app resolves,
`SignInKind: "organisation"`, `DeliveryAddressVerifiedBy: "tenant"`. The
gateway added its own `x-ms-*` headers and ~3.9 s upstream time; the API
saw an ordinary bearer request. One fact that made it the *same* account:
the validator keys identity on the token's `oid` claim
(`BearerTokenValidator.cs:80`), not the pairwise `sub` — `sub` differs per
client application, and the flow's client is not the SPA, so an account
keyed on `sub` would have split in two. The personal-account half (the
connector refusing a personal sign-in) is still to run.

**E2 — payload.** A test form with one question of each type: text
(short), text (long), choice (single), choice (multiple), date, number,
rating, yes/no (a two-option choice), file upload. One response; the
*Get response details* raw output pasted here with any name replaced.
*Result:* _pending_.

**E3 — pre-fill.** *Get Pre-filled URL* with the short text question
pre-filled `token-123`; opened in a private window and submitted — does
`token-123` arrive in E2's payload? Opened again in a normal window after
a prior response — is the pre-fill applied?
*Result:* _pending_.

**E4 — push shape.** The flow POSTs
`{ formId, responseId, submittedAtUtc, inputs: { consult_draft: <long text> } }`
to `/api/Intake/Forms/Responses` (which does not exist yet). Expected: a
404 from the API *after* the bearer was accepted (a 401 would mean the
token was refused at the edge).
*Result:* _pending_.

## 7. The split

| Issue | Builds | Order |
|---|---|---|
| A | § 4.1 held responses: endpoint, table, retention sweep, list/discard | first |
| B | § 4.2 picker, coercion, `InputFormRefs`, origin at start | after A |
| C | § 4.3 provenance `form-response` kind (registry, version bump) | before B lands |
| D | § 4.4 flow recipe, tenant setup, CONFIGURATION/ACCOUNTS docs | after E1 |

#511 closes when this record carries E1–E4's results and A–D are filed.

[graph-admin]: https://learn.microsoft.com/graph/api/resources/adminforms?view=graph-rest-beta
[qa-api]: https://learn.microsoft.com/en-us/answers/questions/5810347/extract-microsoft-forms-responses-data-using-api
[connector]: https://learn.microsoft.com/en-us/connectors/microsoftforms/
[pa-faq]: https://learn.microsoft.com/power-automate/frequently-asked-questions#which-email-addresses-are-supported
[prefill-news]: https://www.neowin.net/news/microsoft-forms-has-added-pre-filled-links-support-for-responses/
[payload-1]: https://manueltgomes.com/microsoft/power-platform/powerautomate/power-automate-action-reference/forms-get-response-details-action/
[payload-2]: https://stackoverflow.com/questions/68264344/how-to-get-file-upload-field-properties-from-microsoft-form-in-power-automate
[webcontentsv2]: https://learn.microsoft.com/en-us/connectors/webcontentsv2/
[blimped]: https://www.blimped.nl/calling-entra-id-secured-azure-function-from-power-automate/
[pa-consent]: https://learn.microsoft.com/en-us/connectors/webcontentsv2/#authorize-the-connector-to-act-on-behalf-of-a-signed-in-user
