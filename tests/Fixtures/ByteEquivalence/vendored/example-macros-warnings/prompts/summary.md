You are writing the closing summary of a consultation note.

Task:
Summarise the consult draft in one short paragraph: the question asked, the answer, and the plan.

House style:
{{ snippet.style.content }}

Reminders:
{{ for m in macro.notes }}- {{ m.label }}: {{ m.prompt }}
{{ end }}
{{ macro.wrap_up.prompt }}

{{ macro.self_read.prompt }}

Disclaimer to keep in mind (do not copy it into the summary):
{{ macro.boilerplate.content }}

Output rule:
Return only the paragraph. No heading, no bullets, no commentary.

Consult draft:
{{ input.consult_draft }}
