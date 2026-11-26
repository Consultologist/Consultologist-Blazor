---
{"kind":"agent","producer":"generated","label":"Drafting a problem","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write one paragraph on the problem "{{ item.value }}" from this draft.
Reason for referral: {{ if input.intake.value }}{{ input.intake.value.reason }}{{ end }}
{{ input.consult_draft.content }}
