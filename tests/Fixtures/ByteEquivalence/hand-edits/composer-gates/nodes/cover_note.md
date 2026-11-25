---
{"kind":"agent","producer":"generated","label":"Writing the cover letter","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":{"when_1":{"if":{"===":[{"var":"input.audience.value"},"patient"]},"text":["Patient copy: {{ node.patient_line.content }}"]}},"ordered":false}
---
Write a two-line cover letter for this note.
{{ node.problem_loop.content }}

{{ node.note_body.content }}
{{ part.when_1 }}
