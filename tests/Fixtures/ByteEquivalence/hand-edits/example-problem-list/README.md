# Hand edits: `example-problem-list`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/example-problem-list/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in snippets/sections/follow_up_plan.md, if: a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.
  - Answered in `snippets/sections/follow_up_plan.md`, `nodes/sections/applies/index.json`, `nodes/sections/applies/section_draft.md`, `nodes/sections/section_draft.md`, `nodes/sections/index.json`, `keys.json`: the folder item's header loses 30's string `if` and keeps its data (`title`). The loop's body is the `if` node `applies` (part 04 § 5.4's `standing_sections/applies`), its `if` `item.id ≠ "follow_up_plan" or node.follow_up.value.value = "yes"`, read per item, its `then` the section draft, moved into `applies/` (the old file `nodes/sections/section_draft.md.deleted`); the loop's `nodes` and `ret` name `applies`. An item whose `if` does not hold is `skipped` with the printed condition as its reason, and has no block in the loop's `.content`, as 30's skipped item had none. The move changes the draft's row key to `node.sections.applies.section_draft:<item>`: `keys.json`, not a package file, is the operator's record of that move, laid over the migration's keys so the comparison maps 30's `section_draft:<item>` to it.
