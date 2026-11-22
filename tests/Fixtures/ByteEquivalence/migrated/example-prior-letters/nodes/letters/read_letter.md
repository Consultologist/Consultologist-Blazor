---
{"kind":"agent","producer":"generated","label":"Reading the letter","description":null,"format":"letter_check","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are reading one prior letter against a consult draft's referral question.

Task:
Decide whether this letter answers the question the consult draft was referred with: true when the letter states the answer or the decision the referral asks for, false otherwise. Then say in one or two sentences what in the letter bears on the question.

Guardrail:
Decide from the letter and the draft alone. A letter that only mentions the same condition does not answer the question.

Prior letter {{ item.id }}:
{{ item.value }}

Consult draft:
{{ input.consult_draft.content }}
