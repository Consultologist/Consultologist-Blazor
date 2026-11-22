---
{ "label": "Consultation note", "kind": "rendered", "description": null, "format": "markdown_block", "if": null, "check": null, "whenEmpty": "hold" }
---
## Assessment
{{ node.assessment.content }}

## Summary
{{ node.summary.content }}
{{ node.summary.value | renderer.bullets }}
## Lines
{{ for t in input.lines.value }}- {{ t }}
{{ end }}
{{~ snippet.sign_off.content }}
