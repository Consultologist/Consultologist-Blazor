---
{"kind":"agent","producer":"generated","label":"Drafting section","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
section_name block:
{{ item.value.title }}

patient_trajectory_concepts block:
{{ node.create_patient_trajectory.value | renderer.concept_context }}