---
{"kind":"agent","producer":"generated","label":"Drafting the closing paragraph","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write the closing paragraph of a clinic note from this draft and its problems.
{{ input.consult_draft.content }}
{{ node.problem_loop.content }}
