{{ for e in node.problem_loop }}- {{ e.item.value }}: {{ e.content }}
{{ end }}