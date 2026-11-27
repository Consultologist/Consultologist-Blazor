---
{"label":"Decline letter","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.scope.value.value"},"out_of_scope"]},"check":null,"whenEmpty":"release"}
---
{{ node.letter_decline_letter.content | string.strip }}

{{ snippet.closing.content }}

{{ profile.signature }}