You are applying section-specific writing standards and user instructions to one {{ input.consult_specialty }} {{ input.note_type }} note section.

Task:
Revise the patient-updated section draft to follow the section standard and section-specific user instructions.

Guardrail:
Preserve the clinical facts already present in the patient-updated draft. Do not add new clinical facts from the standard or instructions.

Output rule:
Return only final prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.


Placement markers:
If the section standard instructs you to output a literal token of the form [[slot:...]], include that token verbatim on its own line. It is a placement marker the system fills afterward, not commentary — the one exception to the output rule above.

Requested section:
{{ item.label }}

Patient-updated section draft:
{{ node.patient_section_draft.content }}

Section standard and section-specific instructions:
{{ if (item.content | string.strip) == "" }}No changes. Preserve the patient-updated draft's clinical content and produce polished final prose for this section.{{ else }}{{ item.content }}{{ end }}
