---
{"kind":"computed","producer":"computed","label":"Counting the topics","description":null,"format":"plaintext","reproducible":true,"modelDerived":false,"runDependent":false,"profileCarrying":false,"if":null,"expression":"\"Topic section length: \" | string.append (node.topic_loop.content | string.size)"}
---
