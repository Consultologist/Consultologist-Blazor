# Milestone 40 planning: session log

Where the planning stands at the end of each session, newest first. Read `README.md` first, then this
file, then re-read the milestone on GitHub: issues are added to it while M39 is built.

## Session 1 (2026-11-21): the milestone re-read

Read `README.md` and `AGENTS.md`, then re-read Milestone 40's description and open issues. Nothing was
planned or decided.

**The milestone on 2026-11-21:** 64 open issues, none closed. The description and the issue list
matched `README.md` § Scope, and no issue had been added since the README was written. The newest
issues, #1522, #1523, #1524 and #1527, were filed from the M39 build. (#1525 and #1526 are pull
requests.)

**Comments that change an issue's scope:**

- **#1439, the package author's ruling of 2026-11-18 (v31 design revision 4).** It replaces the
  `x-producer`/`genr8_*` ruling of the same day. Formats describe shape only, with no producer
  families and no prefixes, and every node header writes `producer`. So the names in the titles of
  #1438 (`ocrxt_*`) and #1439 (`atten`, `adata`, `agent`, `genr8`) are out of date. Plan with the
  newer ruling, and check it against what M39 built.
- **#1478.** The `modelDerived` and `runDependent` flags moved into M39 (every node header writes
  them). M40 keeps only letting a read require a flag's value, for example
  `"requires": { "modelDerived": false }`.
- **#1479.** `"producer"` is reserved in `via` at specVersion 31, and refused until this issue lands.

**Rulings already written into the issue bodies:**

- **#1522.** For M39, descriptions in the format sent to the model are joined with a newline (ruled on
  #1504). M40 decides between a space and a newline, based on what models actually do with each.
- **#1523.** For M39, the checker exempts registry and catalogue formats from the id pattern (ruled on
  #1504). M40 removes the exemption by renaming the fields (`isSnomedConcept`, `isActive`). That
  cascades into new hashes, new versions of the attested agents, and changes to the renderers.
- **#1524.** For M39, a refused deliverable is withheld, not erased (ruled on #1507). M40 lets a
  clinician release it after review, as a recorded override.
- **#1527.** For M39, a function's inner edges are missing from `resolve.json`'s `graph`, and `.items`
  loops get both a `for` and an `aligned` edge (area 05, PR #1525, decisions D7 and D40). Changing
  either is a contract change.

**Related issues in no milestone:** #1458 (moving the JSON Logic evaluator from Corvus to
json-everything) and #1459 (moving value validation from Corvus.JsonSchema to JsonSchema.Net). Both fit
the move towards standard components in #1477. Ask the owner whether to add them to M40.

**Next session:**

1. Re-read the milestone and its open issues. Note any new ones here.
2. `git fetch origin m39-build` and check which areas have been built since 2026-11-21 (area 07 was in
   progress then). Read each new area PR's "Decisions made during build" section.
3. Grill the owner on the first group: the inputs chain (#1442 and its sub-issues #1430–#1437, then
   #1429, #1451, #1452), unless they choose another.
