---
{"kind":"agent","producer":"generated","label":"Building reference trajectory","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"No valid typical trajectory concepts were generated.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

problem_concepts block:
{{ node.identify_problem.value | renderer.concept_bullets }}