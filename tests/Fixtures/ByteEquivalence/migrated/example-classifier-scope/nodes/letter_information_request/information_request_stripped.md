---
{"kind":"rendered","producer":"computed","label":"Drafting the request for information","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.information_request.state"},"completed"]}}
---
{{ node.information_request.content | string.strip }}