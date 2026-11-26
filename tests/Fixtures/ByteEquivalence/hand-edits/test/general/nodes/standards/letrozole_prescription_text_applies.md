---
{"kind":"agent","producer":"generated","label":"Whether Letrozole prescription text applies","description":null,"format":"judgement","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":{"and":[{"===":[{"var":"input.note_type.value"},"consult"]},{"===":[{"var":"item.id"},"assessment_plan"]}]},"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
{{ input.consult_specialty.value }}

{{ input.note_type.value }}

{{ item.value.title }}

{{ node.patient_section_draft.content }}

Does the plan prescribe letrozole?
