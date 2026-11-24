---
{"kind":"agent","producer":"generated","label":"Summarising a prior note","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are summarising one prior clinical note.

Task:
Summarise the note below in at most four sentences of prose, keeping dates and clinical decisions.

Guardrail:
Do not add facts the note does not contain.

Output rule:
Return only the summary prose. No heading, JSON, markdown, bullets, or commentary.

Prior note:
{{ item.value }}
