---
{"kind":"agent","producer":"generated","label":"Drafting section","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing one standard section of an {{ input.consult_specialty.value }} {{ input.note_type.value }} note.

Task:
Write a standard draft for the requested section using the validated patient trajectory as organizing context.

Guardrail:
Do not add typical trajectory details unless they are present in the validated patient trajectory. This is a draft to be reconciled with the original consult note in the next step. Include detail relevant to {{ input.consult_specialty.value }} to the level of detail found in {{ input.note_type.value }}.

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.value.title }}

Validated patient trajectory:
{{ node.create_patient_trajectory.value | renderer.concept_context }}
