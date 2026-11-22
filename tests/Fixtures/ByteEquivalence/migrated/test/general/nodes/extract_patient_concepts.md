---
{"kind":"agent","producer":"generated","label":"Extracting clinical concepts","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"The consult could not be processed because clinical concepts could not be extracted from the draft.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

Extract patient-specific clinical concepts from the draft consult note.

Output only SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Include inactive SNOMED concepts when relevant. Include clinically important findings that are not SNOMED concepts using [not SNOMED concept].
Do not include commentary, headings, JSON, or non-bullet lines.

Draft consult note:
{{ input.consult_draft.value | array.join "\n\n---\n\n" }}
