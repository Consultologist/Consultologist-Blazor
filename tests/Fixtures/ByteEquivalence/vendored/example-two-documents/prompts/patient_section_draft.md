You are updating one {{ snippet.specialty.content }} {{ snippet.note_type.content }} section with patient information.

Source of truth:
Use only the clinical facts contained in the original draft consult note below.

Task:
Rewrite the standard section draft so it reflects the patient information in the original draft consult note.

Missing information rule:
Do not invent missing pathology, dates, staging, receptor status, genomic scores, medications, allergies, physical exam findings, or treatment decisions.
If a detail is not present in the original draft, omit it unless the section would be misleading without it, in which case write "not documented."

Output rule:
Return only prose for the requested section. Do not include a heading, JSON, markdown, bullets, or commentary.

Requested section:
{{ item.label }}

Standard section draft:
{{ node.standard_section_draft.content }}

Original draft consult note:
{{ input.consult_draft }}
