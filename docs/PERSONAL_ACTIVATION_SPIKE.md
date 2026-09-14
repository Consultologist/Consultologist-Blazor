# Personal-account paid activation via an external processor — spike record (#694)

**Status: spike complete, 2026-09-14 — GO, as a paid *personal-account* door beside
LinkedIn (free) and the Microsoft marketplace (org). Recommendation: a
**merchant-of-record (MoR)** processor, not Stripe-direct. The engine side is a
near-exact mirror of the built marketplace door (#669) and needs new code
(inventory in § 5); this pass is docs only. It is **verification-independent** — it
needs no Partner Center account, so it does not wait on the #669 Phase-B gate. This
document is the design of record for the build follow-up.**

## 1. What this is (and what it is not)

Today a personal Microsoft account activates (`Pending` → `Active`) only on a
**LinkedIn** link (#133/#197) — a free eligibility signal, no payment. A paid
**Microsoft commercial-marketplace** purchase (#669) activates too, but **only
work/school Entra accounts can transact a marketplace offer** (the #554 finding, §6
there): personal accounts cannot buy. So there is no *paid* door for a solo
clinician on a personal MSA.

#694 asks: can an **external subscription** — Stripe, or a MoR like Paddle /
Lemon Squeezy — be tied to the signed-in Entra account and activate it? **Answer:
GO**, as an **additional** door. It **adds** a paid personal path; LinkedIn stays
the free eligibility signal; the marketplace stays the org door (§ 4). It also
sidesteps the Partner Center verification bottleneck entirely — nothing here touches
Partner Center.

Not in scope: a "paid browser extension" is not a billing rail — the Edge/Chrome
stores have no in-app-purchase commerce, so any such extension would still route to
an external processor. This spike is that processor.

## 2. Two models — payment processor vs merchant of record

The whole decision turns on **who is the legal seller (merchant of record)** and
therefore who owes indirect tax:

- **Payment processor (Stripe standard + Stripe Tax).** Stripe is *not* an MoR: in a
  standard account **you remain the legal seller** and own tax registration, filing,
  and remittance in every jurisdiction where you have an obligation. Stripe Tax
  *calculates* tax; it does not remit it or take the liability.[^stripe-mor]
  Fees ≈ **2.9% + 30¢**.
- **Merchant of record (Paddle, Lemon Squeezy).** The MoR *is* the legal seller of
  your software. It collects payment, and **calculates, collects, files, and remits
  sales tax / VAT / GST worldwide on your behalf**, then pays you your share. It also
  owns chargebacks and dunning. Fees ≈ **5% + 50¢** all-in.[^paddle-ls][^mor-compare]
- **New (Feb 2026): Stripe Managed Payments** — Stripe's *own* MoR product (grown out
  of its 2024 Lemon Squeezy acquisition), now GA: with Managed Payments Stripe
  becomes the MoR and handles global indirect tax.[^stripe-managed] This means "use
  Stripe" no longer implies "you are the MoR"; there is now a Stripe-native MoR
  option too.

The issue framed this as "Stripe vs a MoR." The accurate 2026 framing is
**processor-you-are-MoR vs someone-else-is-MoR**, and Stripe now sits on both sides.

## 3. Recommendation — a merchant of record

**Recommend a MoR for the personal tier.** For a **solo Canadian vendor**, the ~2%
premium buys out a real, compounding operational burden:

- **Canada:** sales to non-Canadian customers are exports at **0% GST/HST**; under
  **CA$30k** revenue you are a small supplier and need not register for GST/HST at
  all (PST rules vary).[^ca-tax] So a *Canada-only, early-stage* seller's
  Stripe-direct tax burden is genuinely light — this is the honest case *against* the
  premium at the very start.
- **The moment it crosses borders or scales, the burden compounds:** US **economic
  nexus** is tracked *per state* (~$100k / 200 transactions each; about half of states
  tax SaaS, and which ones changes), and EU/UK/Australia/etc. levy **VAT/GST on digital
  services** with B2C/B2B-reverse-charge rules.[^ca-tax][^us-nexus] A one-person
  operation should not become a multi-jurisdiction tax filer to sell a subscription.
- A MoR removes **all** of that, plus **chargebacks and dunning**, for one fee. For a
  clinician-facing product sold by one person, minimal billing ops is worth ~2%.

**Which MoR:** **Paddle** is the recommended default — independent, the most mature
tax coverage (US states, EU VAT incl. B2B reverse charge, UK/AU/…), compliant
invoicing.[^paddle-ls] **Stripe Managed Payments** is the strong alternative if we
want the Stripe ecosystem (Checkout, Billing, dashboards) with MoR tax handling — and
would be the pick if we later also want Stripe's processor features. **Lemon Squeezy**
(now Stripe-owned) is viable but converging into Managed Payments. Pick Paddle unless
we already want to standardize on Stripe tooling; both integrate the same way (§ 4).

The Stripe-direct (you-are-MoR) path is **not** recommended for a solo vendor — it
trades ~2% for becoming a tax registrant/filer across jurisdictions and owning
chargebacks. Revisit only if volume makes ~2% material *and* there is billing-ops
capacity.

## 4. Coexistence — complement, not replace

**Topology: complement.**

| Account kind | Free eligibility | Paid activation |
|---|---|---|
| Personal MSA | LinkedIn | **external processor (this door)** |
| Organisation | LinkedIn | Microsoft marketplace (#669) |

LinkedIn stays the universal free signal for both. The marketplace stays the **org**
door — it keeps Microsoft's org-procurement, AppSource discoverability, and co-sell
reach, which an external processor cannot replicate. The external processor is the
**paid personal** door. This mirrors today's model (personal↔LinkedIn,
org↔marketplace) and adds the missing paid-personal quadrant. The activation model
already supports N independent activating doors (§ 5), so a third one is additive.

(A "universal self-serve processor door for personal *and* org" was considered and
set aside: it would forgo the marketplace's org-procurement/AppSource/co-sell reach
for orgs. Keep marketplace for orgs.)

## 5. Engine-change inventory (for the build follow-up — not built here)

This is **not greenfield** — the marketplace door (#669) built the pattern, and the
per-signal activation policy already generalizes. Reuses, by file:

- **Activation signal.** Add one `IdentityProviders.<processor>` string constant and
  include it in `ActivatesAccount` (`src/Consultologist.Api/Auth/AccountModels.cs`,
  which today lists exactly `LinkedIn` + `MicrosoftMarketplace`). The two-door status
  logic in `src/Consultologist.Api/Auth/AccountStore.cs` — `StatusAfterLink`,
  `StatusAfterUnlink(current, anotherActivatingLinkRemains)`, and
  `AnyActivatingLinkAsync(appUserId, exceptProvider)` — already handles an **Nth**
  activating door **verbatim** (unlinking one door leaves the account `Active` if
  another activating link stands). No change to that logic.
- **Webhook** (anonymous Function, modelled on `AccountMarketplace.cs`'s
  `AccountMarketplaceWebhook`): a lifecycle→effect switch like `EffectFor`, resolving
  the account via `FindAppUserByLinkAsync(<processor>, <issuer>, <subscriptionId>)`,
  every path returning 200. **Two-step trust, exactly as the marketplace webhook does
  it** (there: validate the Entra JWT **and** independently confirm via
  `GetOperationAsync`, never trust the POST body): here **(1)** verify the processor
  signature — `Stripe-Signature` / `Paddle-Signature` **HMAC-SHA256** over the *raw*
  body[^stripe-cri][^paddle-sig] — then **(2)** treat the entitlement as authoritative
  only from **server-verified subscription state** (fetch the subscription from the
  processor by id), not from client-supplied metadata.[^paddle-sig] Map
  `checkout.session.completed` / `customer.subscription.updated|deleted` (or Paddle's
  `subscription.activated|past_due|canceled`) → `ApplyStatusAsync` / link / unlink:
  **active → `Active`**, **past_due → `Unverified`**, **canceled → unlink** (which,
  via the two-door logic, only demotes if no other activating link remains).
- **Checkout + landing.** A checkout kicked off from the signed-in SPA (Stripe
  Checkout / Payment Link, or the Paddle equivalent) carrying `appUserId` as
  **`client_reference_id`** (Stripe) or **`custom_data`** (Paddle) — the value the
  webhook reads back to make the tie.[^stripe-cri] A resolve/return page mirrors
  `AccountMarketplaceResolve` + `Subscription.razor`; the tie itself is created by
  `AccountStore.LinkIdentityAsync(<processor>, <issuer>, <subscriptionId>, …)`, which —
  because the provider is activating — flips `Pending`/`Unverified` → `Active`.
- **Profile UI.** A **Subscription** card (display-only, status→copy) beside the
  marketplace/LinkedIn/Epic/Cerner cards in `src/Consultologist.Web/Pages/Profile.razor`;
  `Shared/AccountStatusBanner.razor` unchanged.
- **Secret handling.** The processor's **webhook signing secret** and **API key** are
  genuine secrets with no managed-identity equivalent, so they follow the single
  documented-secret precedent — `LinkedIn__ClientSecret` (and the
  `Marketplace__ClientSecret` fallback) — read from `IConfiguration` via a
  throw-if-missing helper, status-only logging, never persisted to the identity
  tables. Document the new keys in `docs/CONFIGURATION.md`.

## 6. Perimeter, PCI, and PHI (the boundaries to hold)

- **#610 sign-in perimeter intact.** Entra sign-in is unchanged; the processor is a
  **billing signal, never a bearer credential**. A subscription links an activating
  provider row; it never authenticates a request. The only credential remains the
  Entra token.
- **PCI.** Hosted Checkout (Stripe/Paddle-hosted) means **no card data touches us** —
  we never see or store a PAN; the processor is the SAQ-A boundary.
- **No PHI to the processor.** The processor receives **billing identity only** — the
  clinician's name/email and the subscription/customer id. No referral content, no
  patient data, no consult record ever crosses to the processor. (A MoR being the
  legal *seller* of the software does not entail any access to clinical data.)
- **Refunds / chargebacks / dunning.** With a MoR these are the MoR's to own; our side
  only reacts to the resulting lifecycle webhook (`past_due` → `Unverified`,
  `canceled` → unlink). Clinician-facing billing support (receipts, cancellation) is
  largely the MoR's hosted customer portal.

## 7. Decision — GO

GO, as the paid personal-account door, with a **merchant of record** (Paddle default;
Stripe Managed Payments if we want the Stripe ecosystem). It is verification-independent
(no Partner Center dependency), reuses the marketplace door's activation-provider +
webhook pattern almost verbatim, holds the #610 perimeter, and keeps PHI away from the
processor. LinkedIn stays free-for-all; marketplace stays org-only.

**Build follow-up:** a separate implementation issue (the way #669 followed #554),
scoped to § 5 — provider constant + webhook + checkout/landing + Subscription card +
config, against the chosen MoR. That build needs a processor account (free to set up;
no Partner Center), but no verification gate.

---

[^stripe-mor]: Stripe is a payment processor, not a merchant of record; with a standard account the business remains the legal seller and owns tax compliance (Stripe Tax calculates, it does not remit or assume liability). https://stripe.com/tax and https://dodopayments.com/blogs/is-stripe-a-merchant-of-record
[^paddle-ls]: Paddle and Lemon Squeezy are merchants of record (~5% + 50¢) that collect, file, and remit sales tax/VAT/GST worldwide and own chargebacks; Paddle has the most mature jurisdiction coverage incl. EU B2B reverse charge. https://www.buildmvpfast.com/blog/lemon-squeezy-vs-polar-paddle-merchant-of-record-2026 and https://contracollective.com/blog/paddle-vs-lemon-squeezy-merchant-of-record-digital-commerce-2026
[^mor-compare]: MoR vs Stripe trade — "Stripe is a payment processor: cheap fees, best API, but you own global tax and compliance. Paddle is a merchant of record: it eats the VAT, sales tax, and fraud headache for one all-in fee." https://fintechspecs.com/blog/stripe-vs-paddle-vs-lemon-squeezy-vs-polar-merchant-of-record-b2b-saas/
[^stripe-managed]: Stripe Managed Payments (GA Feb 2026) makes Stripe the merchant of record and handles global indirect tax; it grew out of Stripe's 2024 acquisition of Lemon Squeezy. https://stripe.com/managed-payments
[^ca-tax]: Canadian sellers charge no GST/HST on exports (non-Canadian customers = 0%); under CA$30k revenue you are a small supplier and need not register for GST/HST. https://quaderno.io/blog/gst/sales-tax-101-canadian-startups/
[^us-nexus]: US state sales tax applies only with nexus; economic nexus is typically ~$100k or 200 transactions per state, and about half of states tax SaaS. https://www.getsphere.com/blog/sales-tax-compliance-for-saas
[^stripe-cri]: Stripe `client_reference_id` on a Checkout Session (commonly the authenticated user's id) is echoed back in the `checkout.session.completed` webhook to reconcile the session with your system. https://docs.stripe.com/api/checkout/sessions
[^paddle-sig]: Paddle webhooks are signed with an HMAC-SHA256 `Paddle-Signature` over the raw body; verify the signature, and grant entitlement from server-verified paid line items, not client-supplied `custom_data`. https://developer.paddle.com/webhooks/about/signature-verification/
