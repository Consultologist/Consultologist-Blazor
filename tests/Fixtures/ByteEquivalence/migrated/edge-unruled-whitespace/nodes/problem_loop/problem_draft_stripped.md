---
{"kind":"rendered","producer":"computed","label":"Drafting a problem","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.problem_draft.state"},"completed"]}}
---
{{ node.problem_draft.content | string.strip }}