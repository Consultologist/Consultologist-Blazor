---
{"kind":"agent","producer":"generated","label":"Drafting the summary","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write the summary of a clinic note from these problem paragraphs.
{{ node.problem_loop.content }}
