# Hand edits: `example-macros` (not made)

This package is in `HandEditsTests.NotEdited`: its edits are not made, so it stays refused at publish and is not compared.
Some of its instructions are 30 templates whose 31 form needs a choice the notices do not settle (the plan's Task 16,
Step 3: an edit that needs a design choice is not made). Each notice of `notices.json`, in its order, with why it is not
made, or what its edit would be once the choices are taken.

## Notices

- Hand edit needed in macros/header_line.md, body: the template uses case at line 1, which the 31 dialect doesn't have; rewrite it.
  - Not made: with the rest of the package; the case is two snippets with an `if` each (`consult`, `follow_up`), read where 30 prepended the macro (its placement `if` already excludes `discharge_summary`).
- Hand edit needed in macros/counselling.md, if: macro 'counselling' is placed under different conditions in nodes/standing_sections/standing_draft.md and nodes/patient_line.md; a macro has one if, so place it under one condition, or split it into one macro per condition.
  - Not made: with the rest of the package; 30's two mechanisms would be split: one macro per condition in `standing_draft` (the engine's after placement for `assessment_plan`, and the prompt's offer when `include_counselling`); the `patient_line` placement never held at 30 (`consult` and `follow_up` exclude each other).
- Hand edit needed in macros/counselling.md, instruction: macro 'counselling' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Not made: with the rest of the package; the instruction would ask for the block marker alone on the first or last line of the reply, where 30's engine put the body.
- Hand edit needed in macros/section_caveat.md, instruction: macro 'section_caveat' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Not made: with the rest of the package; the instruction would ask for the block marker alone on the first or last line of the reply, where 30's engine put the body.
- Hand edit needed in macros/table_note.md, instruction: macro 'table_note' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Not made: with the rest of the package; the instruction would ask for the block marker alone on the first or last line of the reply, where 30's engine put the body.
- Hand edit needed in macros/preamble.md, instruction: macro 'preamble' was placed before the reply by the engine; say in its instruction where the model writes its marker.
  - Not made: with the rest of the package; the instruction would ask for the block marker alone on the first or last line of the reply, where 30's engine put the body.
- Hand edit needed in macros/sign_off.md, instruction: macro 'sign_off' was placed after the reply by the engine; say in its instruction where the model writes its marker.
  - Not made: with the rest of the package; the instruction would ask for the block marker alone on the first or last line of the reply, where 30's engine put the body.
- Hand edit needed in macros/silent_reminder.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Not made: 30's instruction is `{{ "" }}`, which rendered as nothing, so 30 never told the model where, or whether, to write the marker; a 31 instruction must say where the marker goes and the model must then write it (part 05 §§ 8.3, 8.6), so any place it names is a choice 30's package does not make. Choosing one (or dropping the placement) is the owner's.
- Hand edit needed in macros/wrap_up.md, instruction: the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.
  - Not made: 30's instruction read `{{ macro.sign_off.content }}`, which rendered the sign-off's body, the profile's name, into the prompt; 31 sends no profile to a model (part 05 § 3.1) and an instruction holds no other macro's marker (§ 8.4), and `sign_off`, placed after the reply at 30, is itself a marker at 31. Where `wrap_up` goes against `sign_off` is a choice the notice does not settle.
- Hand edit needed in deliverables/consult_note.md, body: macro 'header_line' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Not made: with the rest of the package; the macro's text would be a snippet with the placement's or the macro's `if`, read inline where 30 placed it.
- Hand edit needed in deliverables/consult_note.md, body: macro 'followup_only' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Not made: with the rest of the package; the macro's text would be a snippet with the placement's or the macro's `if`, read inline where 30 placed it.
- Hand edit needed in deliverables/consult_note.md, body: macro 'brief_plan' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Not made: with the rest of the package; the macro's text would be a snippet with the placement's or the macro's `if`, read inline where 30 placed it.
- Hand edit needed in deliverables/consult_note.md, body: macro 'follow_up_reminder' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.
  - Not made: with the rest of the package; the macro's text would be a snippet with the placement's or the macro's `if`, read inline where 30 placed it.
