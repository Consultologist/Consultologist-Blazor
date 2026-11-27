---
{"kind":"rendered","producer":"computed","label":"Tailoring a standing section","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.standing_draft.state"},"completed"]}}
---
{{ node.standing_draft.content | string.strip }}