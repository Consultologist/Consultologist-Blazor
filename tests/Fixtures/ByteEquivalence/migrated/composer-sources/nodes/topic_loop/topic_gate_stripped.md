---
{"kind":"rendered","producer":"computed","label":"Drafted topics only","description":null,"format":"markdown_block","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.topic_gate.state"},"completed"]}}
---
{{ node.topic_gate.content | string.strip }}