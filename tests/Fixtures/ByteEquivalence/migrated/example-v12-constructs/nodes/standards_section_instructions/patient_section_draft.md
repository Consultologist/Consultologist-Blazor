---
{"kind":"agent","producer":"generated","label":"Applying patient information","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
standard_section_draft block:
{{ node.standard_section_draft.content }}

consult_draft block:
{{ input.consult_draft.content }}

section_name block:
{{ item.value.title }}