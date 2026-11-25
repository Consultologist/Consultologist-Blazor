---
{"kind":"agent","producer":"generated","label":"Naming the sections left out","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
You are closing a consultation note with one line about what it leaves out.

The sections the author asked for, and whether each was produced:
{{ for e in node.sections_draft.items }}
- {{ e.item.value.title }}: {{ if e.state == "skipped" }}left out at the author's request{{ else }}produced{{ end }}
{{ end }}

Output rule:
Return one sentence naming the sections left out at the author's request, or, if none were, one sentence saying every requested section was produced. No heading, no list, no commentary.
