# Hand edits: `example-v12-constructs`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/example-v12-constructs/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in nodes/extract_note_terms.md, body: the template reads deliverable.consult.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/extract_note_terms.md`: a prompt reads no deliverable at 31 (part 05 § 3.1). At 30 `deliverable.consult.content` was the deliverable's composed sources without its macros, so it is written as those reads: `{{ node.standards_section_instructions.content }}`, a blank line, `{{ node.patient_header.content }}`.
- Hand edit needed in nodes/extract_letter_terms.md, body: the template reads deliverable.ack_letter.content at line 1, which a 31 prompt doesn't read; rewrite it.
  - Answered in `nodes/extract_letter_terms.md`: likewise, `{{ node.standards_letter_body.content }}`.
- Hand edit needed in deliverables/consult.md, body: macro 'formal_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/formal_note.md`, `deliverables/consult.md`, `snippets/index.json`, `macros/formal_note.md`, `macros/index.json`: the macro's text is the snippet `formal_note`, its `if` the placement's (`node.tone.value.value = "formal"`), read in the template where 30 appended it (part 06 § 6.5, as `test/general`'s `test` macro). 30 left an unplaced macro out with its separator, so the snippet's body starts with the blank line that separated it and is read inline after the sources. No prompt places the macro, so its file is removed (`macros/formal_note.md.deleted`), as the migration removes a macro it makes a snippet of (D4).
- Hand edit needed in deliverables/consult.md, body: macro 'friendly_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/friendly_note.md`, `deliverables/consult.md`, `snippets/index.json`, `macros/friendly_note.md`, `macros/index.json`: as `formal_note`, its `if` `node.tone.value.value = "friendly"`.
- Hand edit needed in deliverables/consult.md, body: macro 'followup' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/followup.md`, `deliverables/consult.md`, `snippets/index.json`: as `formal_note`, its `if` the macro's own, the per-run choice `input.followup.value ≠ false` (default on, part 10 § 1.3). The macro `followup` is kept, offered by no prompt, so that a rerun's `MacroChoices` entry keyed `followup` still sets the input its `if` reads (R7, part 10 § 1.3).
