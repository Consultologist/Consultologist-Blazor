---
{"kind":"rendered","producer":"computed","label":"Narrative sections only","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.narrative_only.state"},"completed"]}}
---
{{ node.narrative_only.content | string.strip }}