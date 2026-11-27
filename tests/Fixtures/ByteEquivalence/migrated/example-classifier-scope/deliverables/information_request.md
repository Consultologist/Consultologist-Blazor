---
{"label":"Request for information","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"node.scope.value.value"},"needs_information"]},"check":null,"whenEmpty":"release"}
---
{{ node.letter_information_request.content | string.strip }}