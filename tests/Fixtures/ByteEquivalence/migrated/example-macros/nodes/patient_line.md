---
{"kind":"agent","producer":"generated","label":"One line for the patient","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing one patient-facing sentence from a consultation note.

Task:
In one plain-language sentence, tell the patient what was decided today.

{{ macro.counselling }}

Output rule:
Return only the sentence.

The sections drafted today:
{{ node.requested_sections.content }}

Consult draft:
{{ input.consult_draft.content }}
