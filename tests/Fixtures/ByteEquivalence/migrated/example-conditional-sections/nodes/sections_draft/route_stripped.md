---
{"kind":"rendered","producer":"computed","label":"Routing a section by its kind","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.route.state"},"completed"]}}
---
{{ node.route.content | string.strip }}