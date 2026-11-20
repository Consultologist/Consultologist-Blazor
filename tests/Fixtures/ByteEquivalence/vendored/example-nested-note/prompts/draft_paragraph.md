You are drafting one paragraph of a consultation note.

Task:
Write the paragraph that makes the point below, within the section it belongs to.

Guardrail:
Use only what the consult draft contains. Do not add clinical facts it does not contain.

Output rule:
Return only the paragraph. No heading, no bullets, no commentary.

Section:
{{ item.parent.value.title }}

Point {{ item.id }}:
{{ item.value.point }}

Consult draft:
{{ input.consult_draft }}
