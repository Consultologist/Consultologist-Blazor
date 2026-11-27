---
{"label":"A signed deliverable with no macro","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"release"}
---
{{ node.letter_body.content | string.strip }}{{ profile.signature | renderer.signature_after_blank_line }}