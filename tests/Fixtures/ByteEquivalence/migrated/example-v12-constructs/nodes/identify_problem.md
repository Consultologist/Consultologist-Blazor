---
{"kind":"agent","producer":"generated","label":"Identifying primary problem","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"No valid disease or problem concept was identified.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

patient_concepts block:
{{ node.extract_patient_concepts.value | renderer.concept_bullets }}