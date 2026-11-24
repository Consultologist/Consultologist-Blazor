---
{"kind":"agent","producer":"generated","label":"Drafting the consultation note","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":{"===":[{"var":"input.include_billing.value"},true]},"text":["","Billing:","End the section with one further sentence stating that this was a new-patient encounter, and repeat the encounter date in that sentence exactly as it is written above.",""]}},"ordered":false}
---
You are drafting one section of a new-patient consultation note.

Encounter date: {{ input.seen_on.value }}

Task:
Write the requested section using only the referral below, following the section standard.

Guardrail:
Do not add clinical facts the referral does not contain. Where the referral is silent, say so plainly.

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.value.title }}

Section standard:
{{ item.content }}
{{ part.when_1 }}
Referral:
{{ input.consult_draft.content }}
