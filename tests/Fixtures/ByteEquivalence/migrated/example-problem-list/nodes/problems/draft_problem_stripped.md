---
{"kind":"rendered","producer":"computed","label":"Drafting the problem","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.draft_problem.state"},"completed"]}}
---
{{ node.draft_problem.content | string.strip }}