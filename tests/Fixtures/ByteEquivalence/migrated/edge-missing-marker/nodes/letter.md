---
{"kind":"agent","producer":"generated","label":"Drafting the letter","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write a short clinic letter from this draft and these problem paragraphs.
{{ input.consult_draft.content }}
{{ node.problem_loop.content }}

{{ macro.follow_up }}
