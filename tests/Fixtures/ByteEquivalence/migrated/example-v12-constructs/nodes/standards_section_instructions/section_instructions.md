---
{"kind":"agent","producer":"generated","label":"Applying section instructions","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
patient_section_draft block:
{{ node.patient_section_draft.content }}

section_name block:
{{ item.value.title }}

section_standard block:
{{ item.content }}