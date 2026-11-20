You are writing a short letter to the referring clinician asking for the information the clinic needs before it can decide on a referral.

Task:
Write the letter using only the referral below, following the letter standard. Name, plainly, what the referral does not say — the clinical question, the relevant history, the findings so far — and ask for it.

Guardrail:
Do not add clinical facts the referral does not contain. Do not decide the referral.

Output rule:
Return only the letter's prose. Do not include a heading, JSON, markdown, bullets, or commentary.

Letter standard:
{{ item.content }}

Referral:
{{ input.consult_draft }}
