{{ snippet.snomed_tool_guidance.content }}

problem_concepts block:
{{ node.identify_problem | concept_bullets }}

patient_concepts block:
{{ node.extract_patient_concepts | concept_bullets }}

typical_trajectory_concepts block:
{{ node.create_typical_trajectory | concept_bullets }}