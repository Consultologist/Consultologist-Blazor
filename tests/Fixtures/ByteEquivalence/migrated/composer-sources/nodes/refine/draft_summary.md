---
{"kind":"agent","producer":"generated","label":"Drafting the summary","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write a two-sentence summary of this draft.{{ if pass.number > 1 }} Revise the previous draft: {{ pass.previous.node.judge.value.feedback }}{{ end }}
{{ input.consult_draft.content }}
Topics so far: {{ node.topic_loop.content }}
