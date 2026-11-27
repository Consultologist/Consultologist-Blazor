---
{"kind":"rendered","producer":"computed","label":"Drafting the senior review request","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.senior_review_section.state"},"completed"]}}
---
{{ node.senior_review_section.content | string.strip }}