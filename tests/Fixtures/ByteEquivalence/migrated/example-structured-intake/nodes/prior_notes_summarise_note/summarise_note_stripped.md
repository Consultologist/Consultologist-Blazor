---
{"kind":"rendered","producer":"computed","label":"Summarising a prior note","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.summarise_note.state"},"completed"]}}
---
{{ node.summarise_note.content | string.strip }}