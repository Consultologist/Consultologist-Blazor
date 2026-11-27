---
{"kind":"rendered","producer":"computed","label":"Indexing a prior note","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.index_note.state"},"completed"]}}
---
{{ node.index_note.content | string.strip }}