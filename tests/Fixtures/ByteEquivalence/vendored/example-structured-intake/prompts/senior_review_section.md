You are drafting one section of a senior review request for a patient aged {{ input.patient.age }}{{ if input.patient.sex }} ({{ input.patient.sex }}){{ end }}.

Task:
Write the requested section as a concise request for senior clinician review, using only the referral below and following the section standard.

Guardrail:
Do not add clinical facts the referral does not contain.

Output rule:
Return only prose for the requested section. No heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.label }}

Section standard:
{{ item.content }}

Referral:
{{ input.consult_draft }}
