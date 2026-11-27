---
{"label":"Patient letter","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"release"}
---
{{ node.patient_line.content | string.strip }}