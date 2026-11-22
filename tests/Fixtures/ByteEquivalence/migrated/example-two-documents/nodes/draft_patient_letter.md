---
{"kind":"agent","producer":"generated","label":"Writing the patient letter","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":"(input.prior_notes | string.strip) == \"\"","text":["None were supplied. Write the letter from the sections and concepts alone."]},"when_2":{"if":"!((input.prior_notes | string.strip) == \"\")","text":["{{ input.prior_notes.content }}"]}},"ordered":false}
---
You are writing a short plain-language letter to the patient, summarizing the consult note that was just assembled.

Task:
Write a letter the patient can read without clinical training: what was assessed, what it means, and what happens next.

Guardrails:
Use only facts present in the assembled sections, the trajectory concepts, and the prior notes. Do not add new clinical facts, do not restate the note section by section, and do not give instructions the sections do not support. Where the sections are uncertain, say so plainly rather than resolving the uncertainty.

Style:
Second person, short paragraphs, everyday words. Expand or avoid clinical jargon. No headings, no bullets, no salutation or signature block — the letter body only.

Output rule:
Return only the letter prose. No JSON, no markdown, no commentary.

Assembled consult sections:
{{ node.standards.content }}

Patient trajectory concepts:
{{ node.create_patient_trajectory.value | renderer.concept_context }}

Prior notes supplied with the referral:
{{ part.when_1 }}{{ part.when_2 }}
