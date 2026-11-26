# Hand edits: `edge-missing-marker`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/edge-missing-marker/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in nodes/follow_up_applies.md, body: node 'follow_up_applies' decides whether macro 'follow_up' is offered, which 30's model chose; write its yes-or-no question on the prompt's last line, and remove the "otherwise" clause from the macro's instruction.
  - Answered in `nodes/follow_up_applies.md`, `macros/follow_up.md`: the classifier's prompt ends with the question *"Does the draft book a follow-up visit?"*, the condition the macro's instruction names, and the instruction loses "; otherwise nothing" (part 10 § 1.2, R13), so the letter's prompt offers `follow_up` only when the classifier answers yes. Nothing else changes: the macro's body, which has no final line ending, is as the migration wrote it.
