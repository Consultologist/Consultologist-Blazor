# Byte-equivalence fixtures (#1498)

What today's workflow engine (specVersion 30) produces, frozen as in-repo files. Every file here
is read as exact bytes: `.gitattributes` holds `* -text`, so git never normalises a newline.

## `vendored/`

The thirteen public workflow packages, each vendored from the tag that pins it in the
`consultologist-workflows` repo, as git holds the files (`manifest.json`, `dag.mmd`, prompts,
macros, snippets, everything under `packages/<name>/` at the tag). `test/general` sits at
`vendored/test/general/`.

`vendored/index.json` records, per package, the tag, the commit it resolved to, and the SHA-256 of
every file. The tags are the pins (the list lives in the script); the index is what CI checks:
`VendoredPackagesTests` fails if a file's bytes differ from its hash, or a file is on disk that
the index does not name.

Regenerate from a checkout of the content repo that has the tags:

```
dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]
```

The script deletes `vendored/` first, so a file removed upstream cannot linger, and writes nothing
if a tag does not resolve. Move a pin by editing the tag list in the script, then re-run it.

## `scenarios/` and `captures/`

`scenarios/<package>/<scenario>.json` says how to run one package; `captures/<package>/<scenario>.json`
is what today's engine did with it. `PackageCaptureGoldenTests` runs every scenario through
`CaptureRun` (the real starter, entity and orchestrator, the activities' production halves, no
model) and compares the capture document byte for byte. A package name may hold `/`
(`test/general` is `scenarios/test/general/<scenario>.json`).

A scenario has exactly these members, each present:

```json
{
  "description": ["What the run reaches, in a line or a few."],
  "inputs": { "consult_draft": "Fictional text.", "include_billing": null },
  "profile": { "name": "Dr A. Sample", "signature": "Dr A. Sample\nInternal Medicine" },
  "macroChoices": {},
  "outputs": { "judge:1": { "verdict": "revise", "feedback": "…" }, "scope": { "value": "in_scope" } }
}
```

- `inputs` names every declared input; `null` leaves an optional one unset. Values are what the
  starter accepts, and always fictional.
- `profile` is the display name and the chosen signature block's text. Every scenario uses the
  one above.
- `macroChoices` sets optional macros on or off by id; `{}` takes the defaults.
- `outputs` pins a reply by instance key: `node`, or `node:path`, where the path is item ids and
  while passes joined by `/` (`draft_paragraph:2/3`, `judge:2`, `read_letter:0`; a text array's
  items count from 0, a node's list from 1, a folder's by item id). A text format's pin is a JSON
  string, a structured format's the JSON value, a classifier's `{ "value": "<member>" }`. Every
  other reply is a stub made from the node's format, seeded by its key. A pin that never runs,
  a `null` pin, and a stub the engine flags are refused.

Pin only what a branch needs. To learn the keys, run the scenario with `outputs: {}` first and
read the capture's `outputs`.

A capture holds `package`, `tag` (the vendored tag, or null), `scenario`, `status`, `failure`,
`outputs` (every reply served), `prompts` (each agent node's sent text, trailers included),
`renders` (each rendered node's text), and `deliverables`: one row per manifest deliverable,
`composed` with its final text, `skipped` or `failed` with the engine's reason, or `unsettled` in
a failed job. Every text sits beside its `sha256:` hash.

### Adding a scenario

Write the scenario file and run
`dotnet test tests/Consultologist.Api.Tests.csproj --filter FullyQualifiedName~PackageCaptureGoldenTests`.
The missing capture is written and its row fails once with `Wrote <path>; review it and commit
it.` Read the capture in full: the prompts carry the scenario's inputs, the branches the
description names were taken, each deliverable has its headings, macros and signature. If it did
not do what the description says, fix the scenario, delete the capture, and run again.

### Regenerating

Delete the capture and run its test. A capture is never rewritten in place: a run whose bytes
differ fails.

### Coverage

Every vendored package has a `default` scenario. Every deliverable of a package with scenarios is `composed` in at least one of its captures, and
every node with a prompt has a `prompts` or `renders` entry in one. A deliverable or node no
scenario can reach on today's engine goes in the test's exception table with the reason.

A start the engine refuses is captured too, when it is the refusal a well-formed request can get:
`NoApplicableDeliverable` (no deliverable's condition holds, or a fanned input has no entries).
Its capture has `status` `Refused` (the runner's word, not a job status), `failure`
`NoApplicableDeliverable: <the starter's sentence>`, no outputs, prompts or renders, and every
deliverable `unsettled`. Any other start error (inputs that do not fit the manifest, an undeclared
macro choice, a rate limit) throws, so a mistake in a scenario is never frozen as a refusal. A
refused capture composes nothing, so it adds nothing to the coverage rules.

### Known failure: `example-macros` and `example-macros-warnings`

Today's engine fails both packages (v2026.11.3) in every scenario, at node `summary`, with
`Closing summary failed: Node 'summary' rendered macro.notes/red_flags.prompt, which the package's
walk does not reach from its prompt; the walk and the render disagree.` The summary prompt reads
the `notes/` macro folder (`{{ for m in macro.notes }}… {{ m.prompt }}`); the render records those
`.prompt` reads, and `ConsultNodeCompletion.SanctionedBy` — the walk that cross-checks them — does
not reach them. The captures hold status `Failed`, that sentence, and the prompts sent before it
(the three standing sections and the summary); the requested sections, the patient line and both
deliverables are never reached, and the test's exception tables say so. This is the engine as it
is; it is captured, not worked around.
