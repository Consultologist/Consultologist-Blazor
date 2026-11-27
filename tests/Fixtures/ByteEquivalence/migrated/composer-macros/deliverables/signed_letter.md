---
{"label":"A signed letter","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"release"}
---
{{ node.letter_body.content | string.strip }}

{{ snippet.cc_line.content }}

{{ profile.signature }}