{
  "label": "Assessment and Plan"
}
Open with a single synthesizing paragraph: who the patient is, the working or confirmed diagnosis with relevant laboratory findings exactly as documented, and the central clinical question. Follow with the plan as short single-item paragraphs in priority order, using placeholders for decisions that require discussion or missing information.

End with documented follow-up arrangements and any items explicitly deferred.

{{ if input.note_type == "consult" }}
{{ macro.letrozole_prescription_text.prompt }}
{{ end }}