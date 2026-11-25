---
{"label":"Brief plan","description":null,"if":{"and":[{"or":[{"===":[{"var":"input.note_type.value"},"consult"]},{"===":[{"var":"input.note_type.value"},"follow_up"]}]},{"!==":[{"var":"input.include_counselling.value"},true]}]},"format":"plaintext","instruction":["The brief plan goes as the last line of the plan when no counselling was given.","","If so, write {{ macro.brief_plan.content }} verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward."]}
---
Plan as above; no counselling recorded.
