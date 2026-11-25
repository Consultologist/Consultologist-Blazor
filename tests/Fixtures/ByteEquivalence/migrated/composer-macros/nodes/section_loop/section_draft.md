---
{"kind":"agent","producer":"generated","label":"Drafting a section","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write the section "{{ item.value.title }}" of a clinic letter from this draft.
{{ input.consult_draft.content }}
{{ macro.section_intro }}
{{ macro.section_close }}
