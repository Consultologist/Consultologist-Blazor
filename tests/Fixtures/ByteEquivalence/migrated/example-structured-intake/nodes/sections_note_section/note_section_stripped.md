---
{"kind":"rendered","producer":"computed","label":"Drafting the note","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.note_section.state"},"completed"]}}
---
{{ node.note_section.content | string.strip }}