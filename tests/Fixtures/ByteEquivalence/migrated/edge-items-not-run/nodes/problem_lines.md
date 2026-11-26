---
{"kind":"rendered","producer":"computed","label":"Listing the problems","description":null,"format":"plaintext","reproducible":true,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null}
---
{{ for e in node.problem_loop.items }}{{ if e.state == "completed" }}- {{ e.item.value }}: {{ e.content }}
{{ else }}{{ end }}{{ end }}