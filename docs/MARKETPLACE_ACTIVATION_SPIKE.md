# Marketplace SaaS offer as the activation door — spike record (#554)

**Status: spike complete, 2026-09-09 — GO, as an *additional, org-only* activation
signal beside LinkedIn. The engine needs new code (inventory in § 8); this pass is
docs only. The live proof is gated behind the operator's Partner Center account
verification (currently being redone) — see the Phase-A/Phase-B split (§ 3). This
document is the design of record for the build follow-up.**

## 1. What this is (and what it is not)

Today an account activates (`Pending` → `Active`) on a **LinkedIn** link
(#133/#197/#195), with a manual operator `az` write as the fallback (#191). The
question #554 poses: can a **Microsoft commercial-marketplace SaaS offer** be
*another* door — a clinic buys or trials the offer and the account activates from
the purchase?

**Answer: GO**, but as an **additional** door, not a replacement. A paid
marketplace purchase requires a work/school Entra account (§ 6), so LinkedIn stays
the universal signal and marketplace covers organisations only.

Not to be confused with **agent distribution** (listing the Copilot agent on
AppSource / Teams Store) — that is `consultologist-copilot-agent#6`. This spike is
the **engine account-activation** half. The two meet at the same Partner Center
publisher account and should be sequenced together (#6 depends on this door for an
external clinician to have an activatable account).

## 2. Two "verifications" — keep them separate

- **Partner Center account (publisher/identity) verification** — the business/
  legal/identity/domain vetting of the *seller account*. **This is the one that
  errored and is being redone.** It gates *publishing* (below).
- **Microsoft Entra publisher verification** (the blue "verified publisher" badge
  on the consent prompt) — a separate, free, few-minute process tied to an app
  registration. Not the same thing and **not** what is blocking. The agent already
  shows this badge in Copilot.

Everything below means **Partner Center account verification** unless stated.
- https://learn.microsoft.com/partner-center/enroll/understand-the-verification-process
- https://learn.microsoft.com/entra/identity-platform/publisher-verification-overview

## 3. The gate, and the Phase-A / Phase-B split

**The gate is at *publish* — and publishing to *preview* counts.** Drafting and
configuring an offer is not gated; going to Publisher sign-off / generating preview
links is. Two compounding facts:

1. "Before publishing any listing in the Commercial Marketplace, the information in
   your Partner Center account must be verified."
2. The **resolve → activate → webhook lifecycle exists only on a *transactable***
   offer ("sell through Microsoft"). A *Get it now (Free)* / list-only offer has no
   marketplace token and no fulfillment lifecycle. Even a **$0 / free-trial** plan
   must be a *transactable* offer — which additionally requires **Tax Profile +
   Payout Account** before publish.

So the door we want (buy/trial → resolve → activate → account flips) is a
transactable offer, gated by verification **and** Tax + Payout.

| # | Action | Phase | Why |
|---|--------|-------|-----|
| 1 | Register the Entra apps (landing SSO + S2S) + the SaaS API service principal `20e940b3-4c77-4b0b-9a53-9e16a1b010a7` | **A — now** | Own tenant; independent of Partner Center verification |
| 2 | Build + unit-test landing page, webhook, and all Fulfillment API client code against **dummy payloads** | **A — now** | Own infrastructure; docs recommend dummy responses to start |
| 3 | Create + fully configure a transactable **$0/free-trial** SaaS offer in **draft** (Offer setup, Properties, Listing, Plans, Technical Config = landing/webhook URLs + Entra app/tenant ids, Preview audience) → **Save draft** | **A — now** | Drafting/saving is not publishing |
| 4 | Submit → validation → **Preview / Publisher sign-off** (generate preview links) | **B — blocked** | Publishing to preview needs account verification |
| 5 | Run the **real preview purchase → resolve → activate → webhook** loop | **B — blocked** | Needs a published preview + Tax + Payout; preview purchases really bill |
| 6 | Publish **live**; take real transactions / payout | **B — blocked** | Verification + Tax + Payout |

- https://learn.microsoft.com/legal/marketplace/mpa-faq#are-there-other-prerequisites-before-i-can-publish-a-listing-in-the-commercial-marketplace
- https://learn.microsoft.com/partner-center/marketplace-offers/create-new-saas-offer
- https://learn.microsoft.com/partner-center/marketplace-offers/test-saas-preview-offer

## 4. SaaS Fulfillment API v2 — the flow to build (Phase A)

Manual-activation flow (keeps the interactive account-linking step; preferred over
auto-activation, which skips the landing page):

1. Marketplace redirects the buyer to the **landing page** with `?token=<blob>`
   (URL-encoded; decode). Token life **24 h**.
2. **Sign the user in** (Entra SSO; accept work/school **and** MSA — cert policy
   1000.3), then **Resolve** the token (`x-ms-marketplace-token` header) → `subscriptionId`,
   `offerId`, `planId`, quantity, purchaser/beneficiary.
3. Tie the subscription to the signed-in engine account, then **Activate** (billing
   starts only at Activate). Resolve/activate **within 30 days** of
   `PendingFulfillmentStart` or the asset voids.

Endpoints — base `https://marketplaceapi.microsoft.com/api/saas`, `api-version=2018-08-31`,
**backend-only**: `POST /subscriptions/resolve`, `POST /subscriptions/{id}/activate`,
`GET /subscriptions/{id}`, `GET /subscriptions`, `GET /subscriptions/{id}/listAvailablePlans`,
`PATCH /subscriptions/{id}` (plan/quantity), `DELETE /subscriptions/{id}`,
`GET /subscriptions/{id}/operations[/{opId}]`, `PATCH …/operations/{opId}` (ACK).

- https://learn.microsoft.com/partner-center/marketplace-offers/azure-ad-transactable-saas-landing-page
- https://learn.microsoft.com/partner-center/marketplace-offers/pc-saas-fulfillment-subscription-api
- https://learn.microsoft.com/partner-center/marketplace-offers/pc-saas-fulfillment-operations-api

## 5. Webhook — subscription lifecycle → `Status`

A publisher-hosted HTTPS endpoint, up 24×7 (Microsoft retries ~500× over ~8 h; do
not strict-deserialize). **Authenticate every call: validate the Entra JWT bearer
AND independently call the Get Operation API to confirm the payload before acting
— never trust the POST body alone.**

| Event | ACK | Engine `Status` |
|-------|-----|-----------------|
| `Subscribe` / Activate | 200 | → `Active` |
| `ChangePlan` / `ChangeQuantity` | 200, then **PATCH op success/failure within 10 s** | (plan/quantity only) |
| `Renew` | 200 (notify) | — |
| `Suspend` | 200 (notify; 30-day grace) | suspended (keep recoverable) |
| `Unsubscribe` | 200 (notify) | cancelled |
| `Reinstate` | 200 (or Delete if refusing) | → `Active` |

`isFreeTrial` in the payload distinguishes trial from paid.
- https://learn.microsoft.com/partner-center/marketplace-offers/pc-saas-fulfillment-webhook

## 6. Sign-in kinds — the coverage gap (real)

- **Purchasing** a paid/transactable SaaS offer requires a **work/school Entra
  account**. **Personal Microsoft accounts cannot buy.**
- The **landing-page SSO** must still accept both (cert policy 1000.3), and Preview
  audiences accept MSA emails — but that only matters for free/list-only offers.

**Consequence:** marketplace-as-activation covers **organisation accounts only**.
Personal-account users of the `/common` engine (auth topology: org + MSA, consumers
tenant `9188040d-6c67-4c5b-b112-36a304b66dad`) **keep LinkedIn as their door**. This
is why marketplace is an *additional* signal, not a replacement.
- https://learn.microsoft.com/marketplace/purchase-software-appsource
- https://learn.microsoft.com/legal/marketplace/certification-policies#1000-software-as-a-service-saas

## 7. Auth for the server-to-server Fulfillment call (identity-only posture)

The fulfillment call is a standard Entra **client-credentials** token
(`scope=20e940b3-4c77-4b0b-9a53-9e16a1b010a7/.default`), and **the offer's Technical
Config pins a specific app-registration client id + tenant** — the presented token's
`appid`/`tid` must match exactly or the API 401/403s. So a *plain* managed-identity
token cannot be used (its appid is the MI, not the registered app).

The secretless pattern that satisfies both "no secret" (§ engine identity-only,
`docs/CONFIGURATION.md`) **and** "token appid == the registered app" is **workload
identity federation where the app registration trusts the user-assigned managed
identity** (a federated credential), acquiring the fulfillment token via a client
assertion — mirroring `src/Consultologist.Api/Auth/OnBehalfOfTokenClient.cs`
mechanics with the raw-REST shape of `src/Consultologist.Api/Email/GraphMailClient.cs`.

**Caveat to verify empirically in Phase A:** Marketplace docs only document the
`client_secret` variant of this call; secretless is supported at the Entra layer but
is **not documented as a Marketplace-supported path**. If WIF is rejected, the
`LinkedIn__ClientSecret` exception is the precedent fallback (a single documented
secret). Recommendation: two Entra apps — a *multitenant* app for landing SSO and a
*single-tenant* app for the S2S call; never enable public-client flows; never call
fulfillment from the browser.
- https://learn.microsoft.com/partner-center/marketplace-offers/pc-saas-registration
- https://learn.microsoft.com/entra/workload-id/workload-identity-federation

## 8. Engine-change inventory (for the build follow-up — not built here)

- **Activation signal.** Add `IdentityProviders.MicrosoftMarketplace` +
  `ActivatesAccount == true` (`Auth/AccountModels.cs`), and a subscription-confirmed
  handler that flips status through the existing writer `AccountStore.ApplyStatusAsync`
  (via `LinkIdentityAsync`/`StatusAfterLink`, `Auth/AccountStore.cs`). **Reconsider
  `StatusAfterUnlink`** so *either* signal independently keeps an account `Active`
  (today unlinking an activating provider unconditionally demotes `Active` →
  `Unverified`; with two independent doors it must check whether another activating
  link remains).
- **Endpoints** (anonymous Functions, the satellite pattern): a **landing** GET that
  SSO-signs-in and 302s into the SPA (model on `AccountLinkedIn.CallbackAsync`), and a
  **webhook** POST that validates the Entra JWT + Get-Operation (model on
  `AccountEpic.LinkAsync`'s ungated-for-`Pending` posture). Fulfillment client mirrors
  `GraphMailClient` + the WIF of § 7.
- **Profile UI.** A **Subscription** `content-card` beside the LinkedIn/Epic/Cerner
  cards in `src/Consultologist.Web/Pages/Profile.razor`; status via
  `Shared/AccountStatusBanner.razor`.
- Greenfield — no marketplace code exists today.

## 9. Cost / gatekeeping

- **No listing fee.** Transactable sales carry Microsoft's **3% marketplace service
  fee** (Microsoft bills the customer, pays 97%). A **$0 / free-trial** plan
  (1–180 days) drives the full lifecycle at no charge — the recommended first-offer
  shape, scoped to a **Preview audience** (1–20 whitelisted Entra/MSA emails).
- **Private plan** (Azure-portal-only, whitelisted by subscription id) is an option
  for negotiated pricing; note previews are unsupported for CSP-configured offers.

## 10. The operator's Partner Center experiment protocol

1. **(Phase A)** Register the two Entra apps + `az ad sp create` the SaaS API SP
   `20e940b3-…`. Stand up the landing + webhook endpoints (dummy-payload tested).
2. **(Phase A)** Create a **transactable** SaaS offer; add a **$0/free-trial** plan;
   Technical Config = landing URL, webhook URL, the S2S app's tenant + client ids;
   add a **Preview audience** (the operator's + a test email). **Save draft.**
3. **(Phase-B gate)** Complete Partner Center **account verification** (the redo) +
   **Tax Profile** + **Payout Account**.
4. **(Phase B)** Submit → **Preview**; from a preview-audience work/school account,
   purchase the $0 plan → land → resolve → activate → confirm the account flips to
   `Active` and the webhook events map correctly; cancel; then **Go live**.

## 11. Decision — GO

Standard supported flow, no new secret if WIF holds, low cost, and it reuses the
existing per-signal activation policy. The one boundary to hold: marketplace is the
**org-only** door; LinkedIn remains universal. Build it as a follow-up when the
Partner Center verification clears enough to reach Preview.
