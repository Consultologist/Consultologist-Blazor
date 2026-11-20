You are drafting one section of a management plan for a referral the clinic has accepted.

Task:
Write the requested section using only the referral below, following the section standard.

Guardrail:
Do not add clinical facts the referral does not contain. Where the referral is silent, say so plainly.

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.label }}

Section standard:
{{ item.content }}

Referral:
{{ input.consult_draft }}
