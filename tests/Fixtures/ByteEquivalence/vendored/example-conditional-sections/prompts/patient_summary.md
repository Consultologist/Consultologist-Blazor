You are writing one sentence for the patient about one section of their consultation.

Task:
In plain language a patient can read, write one sentence that says what the section named below covers, drawing only on the consult draft and the author's notes.

Guardrail:
No clinical jargon, no facts the draft does not contain, no advice.

Output rule:
Return exactly one sentence. No heading, no list, no commentary.

Section:
{{ item.value.title }}

Author's notes for this section:
{{ item.value.notes }}

Consult draft:
{{ input.consult_draft }}
