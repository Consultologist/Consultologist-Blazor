{{ snippet.snomed_tool_guidance.content }}

Identify the primary disease or problem concept from the validated patient concepts.

Output only one or more SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Prefer the disease/problem driving the oncology consult. Do not include commentary, headings, JSON, or non-bullet lines.

Validated patient concepts:
{{ node.extract_patient_concepts | concept_bullets }}
