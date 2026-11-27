---
{"kind":"agent","producer":"generated","label":"Indexing a prior note","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"and":[{"!==":[{"var":"input.prior_notes.value"},null]},{">":[{"size":[{"var":"input.prior_notes.value"}]},2]}]},"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing one line of an index of prior clinical notes.

Task:
Write a single line of at most fifteen words naming the note's date and its main subject, from the note below.

Guardrail:
Do not add facts the note does not contain.

Output rule:
Return only that one line. No heading, JSON, markdown, bullets, or commentary.

Prior note:
{{ item.value }}
