---
{"label":"Consultation note","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"input.encounter_kind.value"},"new_patient"]},"check":null,"whenEmpty":"release"}
---
{{ node.sections_consult_note_section.content | string.strip }}