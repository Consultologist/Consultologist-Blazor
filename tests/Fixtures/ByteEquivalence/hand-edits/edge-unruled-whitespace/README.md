# Hand edits: `edge-unruled-whitespace`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/edge-unruled-whitespace/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in macros/problem_close.md, instruction: macro 'problem_close' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Answered in `macros/problem_close.md`: the instruction asks for the block marker alone on the last line of the reply, after the paragraph, as `composer-macros`' `section_close` does. The body, with the white space after its last word, is as the migration wrote it.
- Hand edit needed in nodes/follow_up_applies.md, body: node 'follow_up_applies' decides whether macro 'follow_up' is offered, which 30's model chose; write its yes-or-no question on the prompt's last line, and remove the "otherwise" clause from the macro's instruction.
  - Answered in `nodes/follow_up_applies.md`, `macros/follow_up.md`: the classifier's prompt ends with the question *"Does the draft book a follow-up visit?"*, and the instruction loses "; otherwise nothing" (part 10 § 1.2, R13). The macro's body, which ends with a line ending, is as the migration wrote it.
