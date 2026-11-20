{
  "label": "Header line"
}
{{ case input.note_type }}{{ when "consult" }}Consultation note{{ when "follow_up" }}Follow-up note{{ else }}Clinical note{{ end }}
