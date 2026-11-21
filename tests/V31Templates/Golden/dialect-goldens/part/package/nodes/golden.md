---
{"kind":"agent","producer":"generated","label":"Agent golden","description":null,"format":"plaintext","reproducible":false,"modelDerived":true,"runDependent":false,"profileCarrying":false,"if":null,"onInvalid":"retry","failIfEmpty":null,"parts":{"caution":{"if":{"!":[{"blank":[{"var":"node.summary.content"}]}]},"text":["Keep to the summary:","","{{ node.summary.content | string.strip }}"]}},"ordered":false}
---
Draft the plan for Margaret Okafor.
{{~ part.caution }}
Then sign it.
