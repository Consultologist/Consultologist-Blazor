---
{"kind":"agent","producer":"generated","label":"Drafting a narrative section","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are drafting one requested section of a consultation note as prose.

Task:
Write the section titled below from the consult draft, in two to five sentences.

Guardrail:
Do not add clinical facts the draft does not contain; where the draft is silent, say so.

Output rule:
Return only the prose of this section. No heading, no bullets, no commentary.

Section title:
{{ item.value.title }}

Consult draft:
{{ input.consult_draft.content }}
{{ macro.section_caveat }}
