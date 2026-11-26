---
{"kind":"agent","producer":"generated","label":"Is the referral routine?","description":null,"format":"triage_answer","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Decide whether this referral is routine or urgent.
{{ input.consult_draft.content }}


Answer with exactly one of: routine, urgent.