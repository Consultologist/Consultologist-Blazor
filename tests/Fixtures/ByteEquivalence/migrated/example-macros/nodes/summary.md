---
{"kind":"agent","producer":"generated","label":"Closing summary","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":true,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing the closing summary of a consultation note.

Task:
Summarise the consult draft in one short paragraph: the question asked, the answer, and the plan.

House style:
{{ snippet.style.content }}

Reminders:
- Red flags: {{ macro.notes.red_flags }}
- Safety net: {{ macro.notes.safety_net }}

{{ macro.wrap_up }}

Disclaimer to keep in mind (do not copy it into the summary):
{{ snippet.boilerplate.content }}

Output rule:
Return only the paragraph. No heading, no bullets, no commentary.

Consult draft:
{{ input.consult_draft.content }}
{{ macro.preamble }}
{{ macro.sign_off }}
