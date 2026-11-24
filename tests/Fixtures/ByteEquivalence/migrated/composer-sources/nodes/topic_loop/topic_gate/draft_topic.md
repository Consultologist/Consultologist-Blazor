---
{"kind":"agent","producer":"generated","label":"Drafting a topic","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":"A drafted topic came back empty.","parts":null,"ordered":false}
---
Write one paragraph on the topic "{{ item.value.title }}" from this draft.
{{ input.consult_draft.content }}
