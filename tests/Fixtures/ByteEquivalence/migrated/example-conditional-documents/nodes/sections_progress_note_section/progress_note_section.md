---
{"kind":"agent","producer":"generated","label":"Drafting the progress note","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"input.encounter_kind.value"},"follow_up"]},"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":{"===":[{"var":"input.include_billing.value"},true]},"text":["","Billing:","End the section with one further sentence stating that this was a follow-up encounter, and repeat the encounter date in that sentence exactly as it is written above.",""]}},"ordered":false}
---
You are drafting one section of a follow-up progress note.

Encounter date: {{ input.seen_on.value | date.to_string "%d %b %Y" }}

Task:
Write the requested section as an interval update, using only the referral below and following the section standard.

Guardrail:
Cover what has changed since the last encounter. Do not restate the original presentation in full, and do not add clinical facts the referral does not contain. Where the referral is silent, say so plainly.

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.value.title }}

Section standard:
{{ item.content }}
{{ part.when_1 }}
Referral:
{{ input.consult_draft.content }}
