---
{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":"check.note_coverage","whenEmpty":"release"}
---
{{ node.problem_loop.content | string.strip }}

{{ node.note_body.content | string.strip }}