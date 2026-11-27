---
{"kind":"rendered","producer":"computed","label":"Acknowledgement body","description":null,"format":"plaintext","reproducible":true,"modelDerived":false,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.letter_body.state"},"completed"]}}
---
{{ node.letter_body.content | string.strip }}