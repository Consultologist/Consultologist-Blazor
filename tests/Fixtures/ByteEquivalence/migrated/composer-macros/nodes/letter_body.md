---
{"kind":"agent","producer":"generated","label":"Drafting the letter body","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write the body of a clinic letter from this draft and its sections.
{{ input.consult_draft.content }}
{{ node.section_loop.content }}
