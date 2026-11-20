section_name block:
{{ item.label }}

patient_trajectory_concepts block:
{{ node.create_patient_trajectory | concept_context }}