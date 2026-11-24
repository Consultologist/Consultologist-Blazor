---
{"kind":"agent","producer":"generated","label":"Tailoring a standing section","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are tailoring a standing section of a consultation note to this consult.

Task:
Write the section named below by following its standard against the consult draft.

Guardrail:
Do not add clinical facts the draft does not contain. Where the standard asks for something the draft does not document, say that it is not documented.

Output rule:
Return only prose for this section. No heading, no bullets, no commentary.

Section:
{{ item.value.title }}

Section standard:
{{ item.content }}

{{ macro.counselling }}
{{ macro.silent_reminder }}

Consult draft:
{{ input.consult_draft.content }}
