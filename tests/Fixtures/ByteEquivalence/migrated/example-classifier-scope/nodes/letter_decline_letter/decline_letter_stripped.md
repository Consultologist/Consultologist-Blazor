---
{"kind":"rendered","producer":"computed","label":"Drafting the decline letter","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.decline_letter.state"},"completed"]}}
---
{{ node.decline_letter.content | string.strip }}