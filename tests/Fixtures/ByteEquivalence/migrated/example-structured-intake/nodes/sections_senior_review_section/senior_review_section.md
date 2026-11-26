---
{"kind":"agent","producer":"generated","label":"Drafting the senior review request","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are drafting one section of a senior review request for a patient aged {{ if input.patient.value }}{{ input.patient.value.age }}{{ end }}{{ if input.patient.value.sex }} ({{ if input.patient.value }}{{ input.patient.value.sex }}{{ end }}){{ end }}.

Task:
Write the requested section as a concise request for senior clinician review, using only the referral below and following the section standard.

Guardrail:
Do not add clinical facts the referral does not contain.

Output rule:
Return only prose for the requested section. No heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.value.title }}

Section standard:
{{ item.content }}

Referral:
{{ input.consult_draft.content }}
