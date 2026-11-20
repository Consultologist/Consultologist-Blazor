You are tailoring a standing section of a consultation note to this consult.

Task:
Write the section named below by following its standard against the consult draft.

Guardrail:
Do not add clinical facts the draft does not contain. Where the standard asks for something the draft does not document, say that it is not documented.

Output rule:
Return only prose for this section. No heading, no bullets, no commentary.

Section:
{{ item.label }}

Section standard:
{{ item.content }}

Consult draft:
{{ input.consult_draft }}
