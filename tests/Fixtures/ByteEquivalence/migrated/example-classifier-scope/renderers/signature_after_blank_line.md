---
{"label":"Signature after a blank line","kind":"rendered","description":null,"input":"markdown_block","format":"markdown_block"}
---
{{ if value != null && (value | string.strip) != "" }}

{{ value }}{{ end }}