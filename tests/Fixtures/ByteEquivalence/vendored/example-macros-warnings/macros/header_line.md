{
  "label": "Header line"
}
{{ case input.note_type }}{{ when "consult" }}Consultation note{{ when "follow_up" }}Follow-up note{{ when "triage" }}Triage note{{ else }}Clinical note{{ end }}
