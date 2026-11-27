---
{"kind":"rendered","producer":"computed","label":"Applying section instructions","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.section_instructions.state"},"completed"]}}
---
{{ node.section_instructions.content | string.strip }}