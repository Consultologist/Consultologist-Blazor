---
{ "label": "Renderer bracket", "kind": "rendered", "description": null, "input": "plaintext", "format": "plaintext" }
---
{{ if (value | string.strip) != "" }}[{{ value | string.strip }}]{{ else }}(none){{ end }}