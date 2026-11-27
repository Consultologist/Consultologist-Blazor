---
{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"release"}
---
{{ snippet.intake_line.content }}

{{ node.problem_loop.content | string.strip }}