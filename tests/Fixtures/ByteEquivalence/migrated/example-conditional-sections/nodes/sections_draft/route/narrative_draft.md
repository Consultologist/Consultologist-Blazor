---
{"kind":"agent","producer":"generated","label":"Drafting a narrative section","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are drafting one section of a consultation note as prose.

Task:
Write the section named below, using only the consult draft and the author's notes for this section.

Guardrail:
Do not add clinical facts the draft does not contain. Where the draft is silent, say so plainly.

Output rule:
Return only prose for this section. No heading, no bullets, no table, no commentary.

Section:
{{ item.value.title }}

Author's notes for this section:
{{ item.value.notes }}

Consult draft:
{{ input.consult_draft.content }}
