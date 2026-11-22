---
{"kind":"agent","producer":"generated","label":"Identifying primary problem","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"No valid disease or problem concept was identified.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

Identify the primary disease or problem concept relevant to {{ input.consult_specialty.value }} from the validated patient concepts.

Output only one or more SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Prefer the disease/problem driving the {{ input.consult_specialty.value }} consult. Do not include commentary, headings, JSON, or non-bullet lines.

Validated patient concepts:
{{ node.extract_patient_concepts.value | renderer.concept_bullets }}
