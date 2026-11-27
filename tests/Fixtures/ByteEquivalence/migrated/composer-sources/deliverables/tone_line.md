---
{"label":"A case","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.tone_case.state"},"completed"]},"check":null,"whenEmpty":"release"}
---
{{ node.tone_case.content | string.strip }}