---
{"label":"A review summary","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"input.visit.value"},"review"]},"check":null,"whenEmpty":"release"}
---
{{ snippet.letterhead.content }}

{{ node.letter_body.content | string.strip }}