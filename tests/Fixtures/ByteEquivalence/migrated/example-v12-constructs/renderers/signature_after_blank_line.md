---
{"label":"Signature after a blank line","kind":"rendered","description":null,"input":"plaintext","format":"plaintext"}
---
{{ if value != null && (value | string.strip) != "" }}

{{ value }}{{ end }}