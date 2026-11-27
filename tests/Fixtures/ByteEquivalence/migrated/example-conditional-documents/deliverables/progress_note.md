---
{"label":"Progress note","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"input.encounter_kind.value"},"follow_up"]},"check":null,"whenEmpty":"release"}
---
{{ node.sections_progress_note_section.content | string.strip }}