# Forms intake — the flow recipe

**Status: the clinician's wiring guide for the forms door (#542,
2026-09-01).** The door itself is built and live: held responses
(#539), the setup form's picker with coercion by declaration (#540),
the `form-response` provenance origin (#541, provenance@v2026.09.1) and
the account's *run at once* option (#543, `source: forms`,
provenance@v2026.09.2). The design record and the experiments behind
every rule here are in
[the spike record](forms-intake-spike.md) (§ 2.4, § 2.5, § 4.4, § 6);
this document is the recipe a clinician follows **once per form**.

## The flow, step by step (§ 4.4)

One **automated cloud flow** per form — a *plain* flow (*My flows →
New*), not a solution flow: a solution flow binds to a *connection
reference* rather than the connection, which complicates the one fiddly
step below for nothing.

1. Trigger: **When a new response is submitted** (Microsoft Forms),
   pointed at the form.
2. **Get response details** for the trigger's response id.
3. **HTTP with Microsoft Entra ID → Invoke an HTTP request**:
   - Method `POST`, URL of the request
     `https://east.ca.api.consultologist.ai/api/Intake/Forms/Responses`
   - Header `Content-Type: application/json`
   - Body — a JSON object keyed by the **package's declared input
     ids**, each value the matching question's dynamic content, plus
     the form id, the response id and the flow's own clock:

```json
{
  "formId": "triage-intake",
  "responseId": "@{triggerOutputs()?['body/resourceData/responseId']}",
  "submittedAtUtc": "@{utcNow()}",
  "inputs": {
    "consult_draft": "<dynamic content: the referral question>",
    "urgent": "<dynamic content: the urgency question>"
  }
}
```

The mapping *question → declared input id* is authored once, here, in
the flow. The API never learns question ids or titles unless the flow
sends them.

**The wire rules** (§ 2.4, from the experiments):

- Use a **long-answer** question for the referral — a single-line text
  question truncates at 255 characters.
- Multiple-choice answers pass **as they arrive** (a JSON array as a
  string, e.g. `"[\"A\",\"C\"]"`) — the app understands that form.
- An unanswered question arrives as `""` and is simply not supplied to
  any run.
- An *Other* choice arrives as its free text, indistinguishable from a
  declared option; an answer that matches none of the input's declared
  values is **named, not filled** — on the picker, and as a refusal on
  a run-at-once start.
- Do not send Forms' `submitDate` (a US-format local string):
  `utcNow()` is the timestamp the door expects.
- File-upload questions are out of scope: their answer is a link into
  the clinician's own tenant that the API cannot follow. A document
  still comes by the app or the email door.

Every status answers JSON (E4): the connector reports any non-JSON body
as an error, so a refusal reads as its reason in the flow's run
history, never as a formatting failure.

## The connection (E1 — the fiddly step, learned the hard way)

The *HTTP with Microsoft Entra ID* connection carries two fields, and
the new designer makes them easy to get wrong:

1. **Create the connection from the action** — the in-editor dialog
   asks for one field: the Entra Resource URI,
   `api://b3866040-8bae-4c01-88ba-ecff646df451`. A connection created
   there will not complete on its own (*Invalid connection, please
   update your connection to load complete details*).
2. **Edit the connection on the Connections page** and add the **Base
   Resource URL**: `https://east.ca.api.consultologist.ai`.
3. **Re-enter the action** (method and URL again) so it binds the
   completed connection.

If sign-in fails with **`AADSTS500011` — "The resource principal named
https://east.ca.api… was not found"** — the two fields are swapped: the
host has been put in the resource field. The resource is always the
`api://…` URI; the host is always the Base Resource URL.

There is no consent prompt: the connector is preauthorized on the API
registration (the tenant setup below). The connection's token is the
same delegated `access_as_user` bearer the app sends, so the API
resolves the **flow owner's own account** — the body names no account,
and organisation sign-in is required (a personal-account token is
refused by its word).

## What the account sees

- **Held for review** (the default, and whenever the profile option is
  unset): each pushed response appears under *Load from a form
  response…* on the setup form; choosing one fills the declared inputs,
  coerced by each declaration, for review and editing before a run.
- **Run at once** (the profile's *Form responses* option, #543): the
  push also starts a consult on the account's pinned package. The
  `201` body then carries `run.jobId` — or `run.error` naming the
  refusal (a misfit answer, a rate limit, anything) in the flow's run
  history. **The response is held for the picker either way**; a start
  failure never loses it. The record says `source: forms`, each slot
  carries its `form-response` origin, delivery follows the account's
  own email choice, and the respondent — the patient — is never
  replied to.
- A retried push of a response the door already holds answers `204` and
  starts nothing: the held row is the exactly-once claim.

## Licensing, plainly (§ 6.1)

Both HTTP connectors (and custom connectors) are **premium**: every
clinician whose flow calls the API needs a **Power Automate Premium
licence**. A 90-day self-service trial exists; pay-as-you-go per
environment is the alternative at scale.

**The licence-free fallback — the email bridge.** A standard connector
avoids the licence entirely: *Office 365 Outlook → Send an email* mails
the form's answers from the clinician's own mailbox to the intake
mailbox, and the email door does the rest (sender-matched, as
[ASYNC_DELIVERY.md](../ASYNC_DELIVERY.md) § 2 describes). Its costs,
stated: every value is text (no booleans, enums, numbers or arrays),
there is no staged review and no per-input origin, and there is no
run-at-once option — the email door always starts. It is a fallback for
a clinic without premium licences, not the design.

## A second location

When a second API location exists (the #515 host rule —
[CONFIGURATION.md](../CONFIGURATION.md), "The host name of a region"):

- The clinician's connection needs the new host as its **Base Resource
  URL** (a new connection, or the existing one edited); the **resource
  URI does not change** — one API registration serves every location.
- The **tenant setup is not repeated** — it is once per tenant, never
  per location.
- The host itself must already be in the client's `Locations` and the
  CSP `connect-src`, which is the location's own checklist, not this
  recipe's.

## The tenant setup (operator, once — done 2026-08-28)

Before any flow in a tenant can connect: the connector's service
principal, the delegated `access_as_user` grant, and the
preauthorization on the API registration — **merged with the SPA's
entry, never replacing it**. The three `az` steps, with verification
and reversal, are the fenced block in
[the spike record § 2.5](forms-intake-spike.md); the operator paragraph
in [CONFIGURATION.md](../CONFIGURATION.md) says what each step does and
why. Done 2026-08-28 for `consultologist.ai`.
