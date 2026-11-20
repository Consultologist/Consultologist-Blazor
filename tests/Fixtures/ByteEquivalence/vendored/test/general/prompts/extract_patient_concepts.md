{{ snippet.snomed_tool_guidance.content }}

Extract patient-specific clinical concepts from the draft consult note.

Output only SNOMED concept bullets in these exact forms:
- term (type) - id number
- term [not SNOMED concept]
- term (type) - id number [not active SNOMED concept]

Include inactive SNOMED concepts when relevant. Include clinically important findings that are not SNOMED concepts using [not SNOMED concept].
Do not include commentary, headings, JSON, or non-bullet lines.

Draft consult note:
{{ input.consult_draft | array.join "\n\n---\n\n" }}
