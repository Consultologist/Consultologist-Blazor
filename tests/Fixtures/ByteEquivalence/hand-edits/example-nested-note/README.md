# Hand edits: `example-nested-note` (not made)

This package is in `HandEditsTests.NotEdited`: its one edit is not made, so the engine's publish refuses it (A11-001) and
it keeps its 30 version and its pin (#1508, the owner's B2 and its clarification, R11). Its worst case, two loops over
lists known only at run time (each up to the items limit, 200), opens more instances than one job may at the engine's
default (`RunLimits.JobInstances`, 10,000). The migration says so before the bulk publish refuses it. Its migrated package
still runs offline as written, so it is compared (`HandEditsTests.ComparedAsMigrated`): the instances limit is publish's.
Each notice of `notices.json`, in its order, with why it is not made.

## Notices

- Hand edit needed in nodes/sections/index.json, max: up to 80,614 steps could run in one job through node.sections, and this engine runs at most 10,000 for one job, so publish refuses the package; the migration guesses no max: lower a loop's max or split the workflow.
  - Not made: the owner ruled that no max is guessed (#1508). Where the package's idea is still wanted, a new 31 package that does it within the format is written instead (content work, outside this build).
