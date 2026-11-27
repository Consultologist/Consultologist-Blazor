---
{"kind":"rendered","producer":"computed","label":"Drafting a point","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.draft_point.state"},"completed"]}}
---
{{ node.draft_point.content | string.strip }}