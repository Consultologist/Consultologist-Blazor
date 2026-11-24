---
{"kind":"agent","producer":"generated","label":"Drafting a table section","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are drafting one requested section of a consultation note as a table.

Task:
Lay the section titled below out as a Markdown pipe table with a header row, one row per item the draft documents for it.

Guardrail:
Do not add clinical facts the draft does not contain; where the draft documents nothing for this section, return one row saying so.

Output rule:
Return only the table. No heading, no commentary.

Section title:
{{ item.value.title }}

Consult draft:
{{ input.consult_draft.content }}
{{ macro.table_note }}
