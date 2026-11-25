---
{"kind":"agent","producer":"generated","label":"Outlining the section","description":null,"format":"outline","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are outlining one section of a consultation note.

Task:
List the points this section makes, in order, one paragraph each — between one and three. State each point in a sentence.

Guardrail:
Use only what the consult draft contains for this section. Do not add clinical facts it does not contain.

Section:
{{ item.value.title }} — {{ item.value.purpose }}

Consult draft:
{{ input.consult_draft.content }}
