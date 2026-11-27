---
{"kind":"rendered","producer":"computed","label":"Drafting the consultation note","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.consult_note_section.state"},"completed"]}}
---
{{ node.consult_note_section.content | string.strip }}