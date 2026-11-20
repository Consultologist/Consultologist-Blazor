{
  "label": "Brief plan",
  "instruction": "The brief plan goes as the last line of the plan when no counselling was given.",
  "if": "(input.note_type == \"consult\" || input.note_type == \"follow_up\") && input.include_counselling != true"
}
Plan as above; no counselling recorded.
