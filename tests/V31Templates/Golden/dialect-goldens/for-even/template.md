{{ for s in node.plan.value.sections }}{{ if for.even }}* {{ else }}- {{ end }}{{ s.title }}
{{ end }}