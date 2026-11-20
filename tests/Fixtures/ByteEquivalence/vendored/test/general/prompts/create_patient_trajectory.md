{{ snippet.snomed_tool_guidance.content }}

Reconcile a patient-specific trajectory from the validated patient concepts and typical trajectory relevant {{ input.consult_specialty }}.

Output only SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Include only patient-specific trajectory details supported by validated patient concepts. Do not add typical trajectory details unless supported by patient concepts.
Include a concise support phrase after the accepted bullet only when needed by appending " -- support: ...".
Do not include commentary, headings, JSON, or non-bullet lines.

Disease/problem concept:
{{ node.identify_problem | concept_bullets }}

Validated patient concepts:
{{ node.extract_patient_concepts | concept_bullets }}

Typical trajectory concepts:
{{ node.create_typical_trajectory | concept_bullets }}
