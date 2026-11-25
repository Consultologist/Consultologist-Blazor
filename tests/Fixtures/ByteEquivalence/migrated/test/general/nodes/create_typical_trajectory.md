---
{"kind":"agent","producer":"generated","label":"Building reference trajectory","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"No valid typical trajectory concepts were generated.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

Build a typical clinical trajectory for the disease/problem concept relevant to {{ input.consult_specialty.value }}.

Output only SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Include a concise support phrase after the accepted bullet only when needed by appending " -- support: ...".
Do not include commentary, headings, JSON, or non-bullet lines.

Disease/problem concept:
{{ node.identify_problem.value | renderer.concept_bullets }}
