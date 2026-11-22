{{ for s in node.plan.value.sections }}{{ if for.odd }}(odd) {{ end }}{{ s.title }}
{{ end }}