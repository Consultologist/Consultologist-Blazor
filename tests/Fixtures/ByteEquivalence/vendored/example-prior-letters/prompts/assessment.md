You are writing the assessment of a consultation note.

Task:
Write the assessment: the referral question, what the prior letters said about it, and the conclusion the consult draft reaches.

Guardrail:
Use only what the consult draft and the letter summaries contain. Do not add clinical facts they do not contain. Where no prior letter was read, say so plainly.

Output rule:
Return only prose. No heading, no bullets, no commentary.

Prior letters, as read:
{{ for letter in node.letters }}{{ if !letter.skipped }}{{ letter.content }}

{{ end }}{{ end }}
Consult draft:
{{ input.consult_draft }}
