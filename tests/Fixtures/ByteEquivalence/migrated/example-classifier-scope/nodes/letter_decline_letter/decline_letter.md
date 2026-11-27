---
{"kind":"agent","producer":"generated","label":"Drafting the decline letter","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"===":[{"var":"node.scope.value.value"},"out_of_scope"]},"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are writing a short letter to the referring clinician declining a referral the clinic cannot take.

Task:
Write the letter using only the referral below, following the letter standard. The referral was classified {{ node.scope.value.value }}: say, in one sentence, why the request falls outside what the clinic provides, and suggest where it belongs if the referral itself makes that clear.

Guardrail:
Do not add clinical facts the referral does not contain. Do not offer advice on managing the patient.

Output rule:
Return only the letter's prose. Do not include a heading, JSON, markdown, bullets, or commentary.

Letter standard:
{{ item.content }}

Referral:
{{ input.consult_draft.content }}
