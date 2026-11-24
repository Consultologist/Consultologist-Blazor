# Hand edits: `example-structured-intake`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/example-structured-intake/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in nodes/sections_senior_review_section/senior_review_section.md, body: the body's if at line 1 does more than choose text to show; write it as parts with their own if.
  - Answered in `nodes/sections_senior_review_section/senior_review_section.md`: 30's inline `{{ if input.patient.value.sex }} ({{ input.patient.value.sex }}){{ end }}` is the part `when_1`, its `if` `input.patient.value.sex ≠ null` (30's truthiness of an enum that is a value or null), its text ` ({{ input.patient.value.sex }})`, placed inline where the `if` was (part 05 § 7).
