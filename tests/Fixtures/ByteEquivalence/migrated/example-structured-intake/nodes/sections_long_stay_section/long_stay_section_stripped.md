---
{"kind":"rendered","producer":"computed","label":"Drafting the long-stay summary","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.long_stay_section.state"},"completed"]}}
---
{{ node.long_stay_section.content | string.strip }}