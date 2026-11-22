---
{"kind":"agent","producer":"generated","label":"Building patient trajectory","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"No valid patient trajectory concepts were generated.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

problem_concepts block:
{{ node.identify_problem.value | renderer.concept_bullets }}

patient_concepts block:
{{ node.extract_patient_concepts.value | renderer.concept_bullets }}

typical_trajectory_concepts block:
{{ node.create_typical_trajectory.value | renderer.concept_bullets }}