---
{"kind":"agent","producer":"generated","label":"Drafting the request for information","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.scope.value.value"},"needs_information"]},"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
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
{{ input.consult_draft.content }}
