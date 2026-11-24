# Hand edits: `composer-sources`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/composer-sources/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

## Notices

- Hand edit needed in nodes/topic_tally.md, expression: The condition '"Topic section length: " | string.append (node.topic_loop.content | string.size)' uses string.append, which has no JSON Logic form at 31: rewrite it by hand. The condition '"Topic section length: " | string.append (node.topic_loop.content | string.size)' uses string.size, which has no JSON Logic form at 31: rewrite it by hand.
  - Answered in `nodes/topic_tally.md`: the `let` is `cat("Topic section length: ", size(node.topic_loop.content))` (part 03 §§ 5.1, 5.2: `size` counts a text's code points, `cat` writes the number in the invariant culture). It reads a model's text, so the checker's S03-036 names `modelDerived`, now `true`.
- Hand edit needed in nodes/refine/draft_summary.md, body: the body's if at line 1 does more than choose text to show; write it as parts with their own if.
  - Answered in `nodes/refine/draft_summary.md`: 30's inline `{{ if pass.number > 1 }} Revise the previous draft: …{{ end }}` is the part `when_1`, its `if` `pass.number > 1`, its text 30's branch, placed inline where the `if` was (part 05 § 7; part 04 § 3.3's `pass.previous`).
