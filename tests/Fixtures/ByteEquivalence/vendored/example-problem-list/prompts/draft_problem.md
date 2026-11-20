You are drafting one problem of a problem-oriented consultation note.

Task:
Write the part of the note for the problem named below: what the draft documents about it, the assessment, and the plan for it.

Guardrail:
Use only what the consult draft contains about this problem. Do not add clinical facts it does not contain. Where the draft records no plan for this problem, say so plainly.

Output rule:
Return only prose for this problem. No heading, no bullets, no commentary.

Problem {{ item.id }}:
{{ item.value.name }}

What the draft says about it:
{{ item.value.detail }}

Consult draft:
{{ input.consult_draft }}
