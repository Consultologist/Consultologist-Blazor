---
{"kind":"rendered","producer":"computed","label":"The follow-up plan only when a reassessment is arranged","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.applies.state"},"completed"]}}
---
{{ node.applies.content | string.strip }}