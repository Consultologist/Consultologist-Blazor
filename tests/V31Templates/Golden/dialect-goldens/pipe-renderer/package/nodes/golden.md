---
{ "kind": "agent", "producer": "generated", "label": "Agent golden", "description": null, "format": "plaintext", "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false, "if": null, "onInvalid": "retry", "failIfEmpty": null, "parts": null, "ordered": false }
---
Terms:
{{ node.terms.value | renderer.concept_bullets }}
Quoted:
{{ node.terms.value | renderer.concept_bullets | renderer.quote }}
