---
{"kind":"rendered","producer":"computed","label":"Drafting the progress note","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.progress_note_section.state"},"completed"]}}
---
{{ node.progress_note_section.content | string.strip }}