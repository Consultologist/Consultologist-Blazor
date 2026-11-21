{{ for s in node.plan.value.sections }}{{ for.index }}: {{ s.title }}
{{ end }}