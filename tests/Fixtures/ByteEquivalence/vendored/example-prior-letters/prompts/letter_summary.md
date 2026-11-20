You are summarising one prior letter for a consultation note.

Task:
Summarise in two or three sentences what this letter says that bears on the referral question.

Guardrail:
Use only what the letter contains. Do not add clinical facts it does not contain.

Output rule:
Return only the summary. No heading, no commentary.

What bears on the question:
{{ node.read_letter.value.note }}

Prior letter {{ item.id }}:
{{ item.value }}
