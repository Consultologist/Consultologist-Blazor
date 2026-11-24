# Hand edits: `test/general`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/test/general/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in snippets/standards/assessment_plan.md, body: macro 'letrozole_prescription_text' is placed at line 5, but a snippet folder's item read in a loop places no macro at 31; place it in the prompt that loops over the folder.
  - Answered in `snippets/standards/assessment_plan.md`, `nodes/standards/section_instructions.md`, `macros/letrozole_prescription_text.md`: the placement leaves the folder item's body (with the line end that 30's `{{ if }}` tag carried into its branch), and the loop's prompt, `section_instructions`, places `{{ macro.letrozole_prescription_text }}` inline right after the part that writes `item.content`, where the item's text placed it. The macro's `if` gains `item.id = "assessment_plan"` (read per item, part 05 § 8.2), so it is offered for that item alone, as 30 offered it; its instruction gains an empty first and last line, the line ends 30's placement wrote around it, so the prompt keeps 30's bytes. Its `format` is now `markdown_block`, the format of the agent that places it (part 05 § 8.3, as the migration's own `MacroFormat` chooses), so its body is inserted as 30 inserted it, not escaped.
