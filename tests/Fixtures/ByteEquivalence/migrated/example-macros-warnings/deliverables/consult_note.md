---
{"label":"Consult note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"release"}
---
{{ macro.header_line.fixed_text }}

{{ node.requested_sections.content | string.strip }}

{{ node.standing_sections.content | string.strip }}

{{ node.summary.content | string.strip }}

{{ snippet.closing.content }}

{{ snippet.kept_note.content }}

{{ macro.followup_only.fixed_text }}

{{ macro.brief_plan.fixed_text }}

{{ macro.follow_up_reminder.fixed_text }}