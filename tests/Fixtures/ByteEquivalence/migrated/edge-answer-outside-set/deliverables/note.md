---
{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.triage.value.value"},"routine"]},"check":null,"whenEmpty":"release"}
---
{{ node.problem_loop.content | string.strip }}