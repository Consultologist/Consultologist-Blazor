You are drafting the opening summary of a consultation note.

Task:
Write a summary of two or three sentences: who was seen, why, and the main conclusion. The note that follows has {{ node.section_count.value }} sections.

Guardrail:
Use only what the consult draft contains. Do not add clinical facts it does not contain.
{{ if pass.number > 1 }}
A reviewer asked for this revision of the previous draft:
{{ pass.previous.node.judge.value.feedback }}

Previous draft:
{{ pass.previous.node.draft_summary.content }}
{{ end }}
Output rule:
Return only the summary. No heading, no commentary.

Consult draft:
{{ input.consult_draft }}
