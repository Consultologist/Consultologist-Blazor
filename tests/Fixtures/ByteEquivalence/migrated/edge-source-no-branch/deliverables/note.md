---
{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.letter_case.state"},"completed"]},"check":null,"whenEmpty":"release"}
---
{{ node.summary.content | string.strip }}

{{ node.letter_case.content | string.strip }}