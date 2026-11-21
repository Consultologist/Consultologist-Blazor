---
{ "label": "Follow-up", "description": null, "if": null, "format": "plaintext", "instruction": ["Place the follow-up line alone, after the plan:", "  {{~ macro.follow_up.content }}"] }
---
Follow up with {{ snippet.clinician.content }} on {{ input.visit_date.value | date.to_string "%d %B %Y" }}.
