---
{"kind":"rendered","producer":"computed","label":"Holding the billing section back unless asked","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.applies.state"},"completed"]}}
---
{{ node.applies.content | string.strip }}