---
{"kind":"agent","producer":"generated","label":"Drafting the long-stay summary","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are drafting one section of a summary for an admission of {{ input.length_of_stay.value }} days.

Task:
Write the requested section focusing on what a stay of {{ input.length_of_stay.value }} days implies for follow-up, using only the referral below and following the section standard.

Guardrail:
Do not add clinical facts the referral does not contain.

Output rule:
Return only prose for the requested section. No heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.value.title }}

Section standard:
{{ item.content }}

Referral:
{{ input.consult_draft.content }}
