---
{"kind":"rendered","producer":"computed","label":"Drafting the paragraph","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.draft_paragraph.state"},"completed"]}}
---
{{ node.draft_paragraph.content | string.strip }}