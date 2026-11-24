# Hand edits: `composer-gates`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/composer-gates/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

A prompt reads no deliverable at 31 (part 05 § 3.1; part 06 § 6.7). At 30 a prompt's `deliverable.<id>.content` was that
deliverable's composed sources, whether its check passed or not (the fixture README's `note-check-fails`), without its
macros; each read below is written as the reads of the deliverable's own template, so the prompt has the same text.

## Notices

- Hand edit needed in nodes/extract_note_terms.md, body: the template reads deliverable.note.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/extract_note_terms.md`: `deliverable.note.content` is `deliverables/note.md`'s reads, `{{ node.problem_loop.content }}`, a blank line, `{{ node.note_body.content }}`.
- Hand edit needed in nodes/extract_copy_terms.md, body: the template reads deliverable.record_copy.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/extract_copy_terms.md`: `deliverable.record_copy.content` is `deliverables/record_copy.md`'s reads, the same two nodes.
- Hand edit needed in nodes/cover_note.md, parts.when_1.text: the template reads deliverable.patient_copy.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/cover_note.md`: `deliverable.patient_copy.content` is `{{ node.patient_line.content }}`, under the part's `if`, which is the deliverable's own (`input.audience.value = "patient"`).
- Hand edit needed in nodes/cover_note.md, body: the template reads deliverable.note.content at line 2, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/cover_note.md`: as `extract_note_terms`, the note's two reads.
