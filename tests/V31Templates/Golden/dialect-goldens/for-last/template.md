{{ for s in node.plan.value.sections }}{{ s.title }}{{ if !for.last }}; {{ end }}{{ end }}.
