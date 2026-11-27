---
{"kind":"rendered","producer":"computed","label":"Drafting the plan","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.plan_section.state"},"completed"]}}
---
{{ node.plan_section.content | string.strip }}