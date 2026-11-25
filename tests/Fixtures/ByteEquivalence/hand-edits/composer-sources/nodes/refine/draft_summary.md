---
{"kind":"agent","producer":"generated","label":"Drafting the summary","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":{">":[{"var":"pass.number"},1]},"text":[" Revise the previous draft: {{ pass.previous.node.judge.value.feedback }}"]}},"ordered":false}
---
Write a two-sentence summary of this draft.{{ part.when_1 }}
{{ input.consult_draft.content }}
Topics so far: {{ node.topic_loop.content }}
