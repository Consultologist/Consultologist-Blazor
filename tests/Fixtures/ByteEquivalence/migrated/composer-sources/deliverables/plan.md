---
{"label":"An if","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.plan_gate.state"},"completed"]},"check":null,"whenEmpty":"release"}
---
{{ node.plan_gate.content | string.strip }}