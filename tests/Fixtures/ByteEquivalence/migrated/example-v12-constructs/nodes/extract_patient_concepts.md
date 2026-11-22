---
{"kind":"agent","producer":"generated","label":"Extracting clinical concepts","description":null,"format":"concept_list","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"The consult could not be processed because clinical concepts could not be extracted from the draft.","parts":null,"ordered":false}
---
{{ snippet.snomed_tool_guidance.content }}

consult_draft block:
{{ input.consult_draft.content }}