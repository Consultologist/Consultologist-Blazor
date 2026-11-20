You are writing one patient-facing sentence from a consultation note.

Task:
In one plain-language sentence, tell the patient what was decided today.

{{~ if input.note_type == "follow_up" ~}}
{{ macro.counselling.prompt }}
{{~ end ~}}

Output rule:
Return only the sentence.

The sections drafted today:
{{ node.requested_sections.content }}

Consult draft:
{{ input.consult_draft }}
