---
{"kind":"rendered","producer":"computed","label":"Summarising the letter","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.letter_summary.state"},"completed"]}}
---
{{ node.letter_summary.content | string.strip }}