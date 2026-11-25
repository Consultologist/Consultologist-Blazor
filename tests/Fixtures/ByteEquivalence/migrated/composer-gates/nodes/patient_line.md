---
{"kind":"agent","producer":"generated","label":"Writing the patient line","description":null,"format":"markdown_block","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"fail","failIfEmpty":null,"parts":null,"ordered":false}
---
Write one line for the patient about these problems.
{{ node.problem_loop.content }}
