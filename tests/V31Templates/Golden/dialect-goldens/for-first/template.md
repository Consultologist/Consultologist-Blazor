{{ for s in node.plan.value.sections }}{{ if for.first }}First: {{ else }}, then {{ end }}{{ s.title }}{{ end }}.
