---
{"kind":"rendered","producer":"computed","label":"Paragraphs","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.paragraphs.state"},"completed"]}}
---
{{ node.paragraphs.content | string.strip }}