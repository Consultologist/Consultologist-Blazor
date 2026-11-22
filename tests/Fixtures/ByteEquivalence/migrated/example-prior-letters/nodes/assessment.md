---
{"kind":"agent","producer":"generated","label":"Assessment","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing the assessment of a consultation note.

Task:
Write the assessment: the referral question, what the prior letters said about it, and the conclusion the consult draft reaches.

Guardrail:
Use only what the consult draft and the letter summaries contain. Do not add clinical facts they do not contain. Where no prior letter was read, say so plainly.

Output rule:
Return only prose. No heading, no bullets, no commentary.

Prior letters, as read:
{{ for letter in node.letters.items }}{{ if letter.state != "skipped" }}{{ letter.content }}

{{ else }}{{ end }}{{ end }}
Consult draft:
{{ input.consult_draft.content }}
