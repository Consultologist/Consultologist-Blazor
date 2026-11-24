# Hand edits: `example-problem-list`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/example-problem-list/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in snippets/sections/follow_up_plan.md, if: a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.
  - Answered in `snippets/sections/follow_up_plan.md`, `nodes/sections/section_draft.md`: the folder item's header loses 30's string `if` and keeps its data (`title`); the loop's one body node, `section_draft`, takes the gate as its own `if`, read per item, `item.id ≠ "follow_up_plan" or node.follow_up.value.value = "yes"` (part 04 § 5.4's `applies`, written on the node so the item's keys stay `node.sections.section_draft:<item>`). An item whose `ret` did not run is `skipped` and has no block in the loop's `.content`, as 30's skipped item had none.
