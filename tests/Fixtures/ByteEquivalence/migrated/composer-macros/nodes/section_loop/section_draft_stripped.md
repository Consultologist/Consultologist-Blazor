---
{"kind":"rendered","producer":"computed","label":"Drafting a section","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.section_draft.state"},"completed"]}}
---
{{ node.section_draft.content | string.strip }}