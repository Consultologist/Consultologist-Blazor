Sections:
{{~ for s in node.plan.value.sections ~}}
- {{ s.title }}
{{~ end ~}}

Draft: {{~ input.consult_draft.content ~}}  
End.
