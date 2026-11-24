---
{"kind":"agent","producer":"generated","label":"Closing line","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write one closing line for a clinic note from this draft.
{{ input.consult_draft.content }}
Topics so far: {{ node.topic_loop.content }}
