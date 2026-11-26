# Hand edits: `composer-macros`

The edits an operator makes after the migration (part 10 §§ 1.4, 1.5), laid over `migrated/composer-macros/` by
`MigrationFixture.Edited`. Each hand-edit notice of `notices.json`, in its order, with the files that answer it.

A deliverable macro with a condition becomes a snippet with an `if`, read in the template where 30 placed the macro (part 06
§ 6.5; part 05 § 10). 30 left a macro that was not placed (its own `if`, its placement's, or an optional one chosen off)
out of the deliverable with its separator, and kept the separators of one that was placed but rendered empty; so a snippet
for a macro 30 might not place starts with the blank line that separated it and is read inline after the piece before it,
and one 30 always placed keeps its place between blank lines. A macro no prompt places is removed, as the migration removes
one it makes a snippet of (D4), except one whose `if` is a per-run choice: it is kept, offered by no prompt, so that a
rerun's `MacroChoices` entry keyed by its id still sets the input its `if` reads (R7, part 10 § 1.3).

## Notices

- Hand edit needed in snippets/review_leaflet.md, body: the body's if at line 1 decides what a macro body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.
  - Answered in `snippets/review_leaflet.md`: the body's `if` is the snippet's own `if`, `input.visit.value = "review"`, and the body its text. 30 placed the macro always and kept its empty piece, so its place in `deliverables/gated.md` keeps its blank lines.
- Hand edit needed in snippets/visit_echo.md, body: macro 'new_visit_note' is read as fixed text at line 1, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.
  - Answered in `snippets/visit_echo.md`, `snippets/new_visit_text.md`, `snippets/index.json`: `visit_echo` reads the snippet `new_visit_text`, the macro's text with its `if` (`input.visit.value = "new"`) and its file's final line end, as 30's `.content` read it (an inert macro read empty at 30, an absent snippet reads nothing).
- Hand edit needed in macros/quoted_review.md, body: the template reads deliverable.review_summary.content at line 1, which a 31 macro body doesn't read; rewrite it.
  - Answered in `macros/quoted_review.md`, `snippets/review_summary_text.md`, `snippets/index.json`: deliverables are terminal (part 06 § 6.7). At 30 the read was `review_summary`'s composed sources, trimmed, without its macros; the snippet `review_summary_text` is that read, `{{ node.letter_body.content | string.strip }}`, under the deliverable's own `if` (`input.visit.value = "review"`), so where 30 failed reading a skipped deliverable it reads nothing, as R4 item 1 rules a skipped deliverable's `.content` reads null. The kept macro's body reads the same snippet.
- Hand edit needed in macros/section_intro.md, instruction: macro 'section_intro' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Answered in `macros/section_intro.md`: the instruction asks for the block marker alone on the first line of the reply, before the section's text (part 05 § 8.4), where 30's engine put the body.
- Hand edit needed in macros/section_close.md, instruction: macro 'section_close' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Answered in `macros/section_close.md`: the instruction asks for the block marker alone on the last line of the reply, after the section's text.
- Hand edit needed in deliverables/gated.md, body: macro 'new_visit_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/new_visit_note.md`, `deliverables/gated.md`, `snippets/index.json`, `macros/new_visit_note.md`, `macros/index.json`: the snippet `new_visit_note`, its `if` the macro's own (`input.visit.value = "new"`), read inline after the letter body; the macro is removed.
- Hand edit needed in deliverables/gated.md, body: macro 'review_note' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/review_note.md`, `deliverables/gated.md`, `snippets/index.json`, `macros/review_note.md`, `macros/index.json`: the snippet `review_note`, its `if` the placement's (`input.visit.value = "review"`), read inline after `new_visit_note`; the macro is removed.
- Hand edit needed in deliverables/optional.md, body: macro 'followup_offer' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/followup_offer.md`, `deliverables/optional.md`, `snippets/index.json`: the snippet `followup_offer`, its `if` the per-run choice (`input.followup_offer.value ≠ false`, default on), read inline after the letter body; the macro is kept for the choice.
- Hand edit needed in deliverables/optional.md, body: macro 'leaflet_offer' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/leaflet_offer.md`, `deliverables/optional.md`, `snippets/index.json`: the snippet `leaflet_offer`, its `if` the per-run choice (`input.leaflet_offer.value = true`, default off), read inline after `followup_offer`; the macro is kept for the choice.
- Hand edit needed in deliverables/echoes.md, body: macro 'quoted_review' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Answered in `snippets/quoted_review.md`, `deliverables/echoes.md`, `snippets/index.json`: the snippet `quoted_review`, its `if` the per-run choice (`input.quoted_review.value = true`, default off), its body the macro's rewritten one, read inline after `visit_echo`; the macro is kept for the choice.
