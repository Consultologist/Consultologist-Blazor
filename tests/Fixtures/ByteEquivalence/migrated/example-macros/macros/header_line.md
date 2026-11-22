---
{"label":"Header line","description":null,"if":null,"format":"plaintext","instruction":["If so, write {{ macro.header_line.content }} verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward."]}
---
{{ case input.note_type.value }}{{ when "consult" }}Consultation note{{ when "follow_up" }}Follow-up note{{ else }}Clinical note{{ end }}
