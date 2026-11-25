---
{"kind":"agent","producer":"generated","label":"Writing the cover letter","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":{"===":[{"var":"input.audience.value"},"patient"]},"text":["Patient copy: {{ deliverable.patient_copy.content }}"]}},"ordered":false}
---
Write a two-line cover letter for this note.
{{ deliverable.note.content }}
{{ part.when_1 }}
