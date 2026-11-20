You are drafting one section of a consultation note as a table.

Task:
Lay the section named below out as one markdown table, using only the consult draft and the author's notes for this section. Choose the columns the section calls for (for a medication list: name, dose, frequency, indication).

Guardrail:
Do not add clinical facts the draft does not contain. Leave a cell blank rather than inferring it.

Output rule:
Return only the markdown table, with a header row. No heading above it, no prose, no commentary.

Section:
{{ item.value.title }}

Author's notes for this section:
{{ item.value.notes }}

Consult draft:
{{ input.consult_draft }}
