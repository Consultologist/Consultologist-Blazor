You are drafting one section of a follow-up progress note.

Encounter date: {{ input.seen_on }}

Task:
Write the requested section as an interval update, using only the referral below and following the section standard.

Guardrail:
Cover what has changed since the last encounter. Do not restate the original presentation in full, and do not add clinical facts the referral does not contain. Where the referral is silent, say so plainly.

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.label }}

Section standard:
{{ item.content }}
{{ if input.include_billing == true }}
Billing:
End the section with one further sentence stating that this was a follow-up encounter, and repeat the encounter date in that sentence exactly as it is written above.
{{ end }}
Referral:
{{ input.consult_draft }}
