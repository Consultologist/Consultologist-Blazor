# AGENTS.md

This file provides guidance to AI coding agents working with code in this repository.

## Commands

```bash
dotnet build Consultologist.sln          # build every project
dotnet test Consultologist.sln           # run both suites (API + client render)
dotnet test --filter FunctionCorsTests   # run a single test class
dotnet run --project src/Consultologist.Web   # frontend on http://localhost:5000
cd src/Consultologist.Api && func start       # backend (Azure Functions Core Tools)
```

Prerequisites: .NET 10 SDK (pinned in `global.json`) and the WASM workload (`dotnet workload install wasm-tools`).

All `bin`/`obj` output is centralized to `build/bin|obj/<ProjectName>/` via `Directory.Build.props` — project folders never contain build artifacts. NuGet versions are centralized in `Directory.Packages.props` (central package management; `Version=` on a `PackageReference` will not build).

## Architecture

Two independently deployed applications in one solution, a shared format library, plus two test projects:

- **`src/Consultologist.Web`** — standalone Blazor WebAssembly PWA (Fluent UI). Auth is Microsoft Entra ID via MSAL (`Microsoft.Authentication.WebAssembly.Msal`); config lives in `wwwroot/appsettings.json`. Deployed to Azure Static Web Apps.
- **`src/Consultologist.PackageFormat`** — the workflow package format as a class library (#450): manifest records, the validator, the wire value, conditions, fans, rendering, the stamp. Referenced by the Api; no Azure or engine dependency (`docs/customizable-workflow/package-format-library.md`).
- **`src/Consultologist.Api`** — .NET 10 isolated Azure Functions. Deployed to a separate Azure Function App (the SWA's `api_location` is intentionally empty); the frontend calls it cross-origin, so every HTTP function applies `FunctionCors` manually and new endpoints must too.
- **`tests/Consultologist.Api.Tests.csproj`** — xUnit + NSubstitute against the Api project. `Consultologist.Api.csproj` grants `InternalsVisibleTo` to it.
- **`tests/Consultologist.Web.Tests/`** — bUnit render tests for the client (#224). `dotnet build` type-checks Razor markup but never renders it, so bind expressions and component wiring fail only at runtime; these cover the pages with a demonstrated failure history (Consults setup and result, History provenance).

### Consult generation flow (the core feature)

`Jobs/ConsultGenerationJobs.cs` (~2,700 lines) holds the Durable Functions pipeline: an HTTP starter, an orchestrator, per-section activities, and a durable entity tracking job state. Activities call `Agents/AgentSectionGenerator`, which drives Azure AI Foundry agents. Progress events are persisted to Azure Table Storage (`Jobs/ConsultGenerationJobEventStore`, `...JobIndexStore`) so the browser can stream them via SSE **with resume support** (`Last-Event-ID`) — see `docs/SSE_RESUME.md` and `docs/CONSULT_GENERATION_EVENTS.md`. `Agents/ConsultGeneration.cs` is the older direct (non-durable) SSE endpoint over the same generator. The frontend consumes streams through `Services/AI/AIEndpointService`.

### Auth chain (Api)

`Auth/BearerTokenValidator` (Entra JWT validation) → `Auth/AccountAuthorizer` → `Auth/AccountStore` (Azure Tables: app users + provider identity links). HTTP functions resolve the account per request; `AccountAuthorizer.IsActive` gates disabled accounts.

### Conventions and constraints

- Namespaces mirror folders (`Consultologist.Api.Jobs`, `Consultologist.Web.Services.AI`, `Consultologist.PackageFormat`, …).
- `[Function("Name")]` strings and durable orchestrator/entity/activity class names are the deployed contract — renaming a function string or an activity class affects the live Function App and any in-flight durable orchestrations; namespaces are safe to change.
- The scoped-CSS bundle is named after the assembly: `wwwroot/index.html` links `Consultologist.Web.styles.css`. Renaming the Web project breaks this link unless index.html is updated.
- CI is path-filtered: the Function App deploys only on `src/Consultologist.Api/**` changes; the SWA deploy ignores api/tests/docs/markdown changes but builds previews for every PR. Tests run in their own workflow.
- Design docs live in `docs/` (indexed by `docs/README.md`); `docs/research/` is historical point-in-time material — don't update it to match later refactors.

## Traps

Each of these has cost real debugging time. They are listed by **symptom**,
because the symptom never points at the cause.

### NSubstitute returns `""` for unstubbed strings, not `null`

*Symptom: a null-check branch is never taken, and the code proceeds with an
empty id.* Auto-values give `string.Empty` for `string` and
`Task<string?>` — so `if (folderId == null)` is false on a substitute that
was never told what to return. Check `string.IsNullOrWhiteSpace` instead of
`== null` at any boundary a test can reach.

### The last matching NSubstitute stub wins

*Symptom: a `NullReferenceException` deep inside the code under test, nowhere
near the stub.* A helper that ends with
`.TryAcquireAsync(Arg.Any<string>(), …).Returns(allowed)` silently overrides
a specific `.TryAcquireAsync("user-1", …).Returns(refused)` set **before** it,
so the test runs the wrong path and dies on the first unstubbed thing it
meets. Construct first, override after — and put the override in a named
helper so the ordering requirement is visible.

### `gh pr edit` and `gh issue close` fail on this repo

*Symptom: `GraphQL: Projects (classic) is being deprecated … (repository.pullRequest.projectCards)`.*
Nothing is wrong with the PR or the token. Use REST:

```bash
gh api -X PATCH repos/OWNER/REPO/pulls/N -F body=@body.md
gh api -X PATCH repos/OWNER/REPO/issues/N -f state=closed
```

### JSON response casing is not uniform

*Symptom: a client reads a field and gets an empty value with no error.*
Records serialize **PascalCase** (`DocumentExtractionResponse` → `"Text"`),
while anonymous objects keep their declared casing (`new { error = … }` →
`"error"`). The same endpoint therefore answers `Text` on success and `error`
on refusal. Scripts should read both (`d.get("Text") or d.get("text")`);
`scripts/show-extraction.sh` and `scripts/verify-rate-limit.sh` do.

### A test file may hold more than one class

*Symptom: `CS0103: The name '_field' does not exist in the current context`
on a test that looks correctly placed.* `ConsultGenerationJobStarterTests.cs`
holds `ConsultGenerationJobStarterTests`, `ResolveEffectiveInputsTests` and a
`file static` helper. Anchoring an edit on "the last test in the file" lands
in the wrong class. Check the enclosing class before adding a test.

### An App Insights query returns nothing and the data is there

*Symptom: a Kusto query with an explicit `timestamp > todatetime(…)` filter
returns zero rows for a window you know had traffic.* Two independent causes,
and neither errors — both just answer "none", which reads exactly like
"it never happened".

**`--offset` clamps the range regardless of the query text.** It defaults to
`PT1H`, so any filter reaching further back is silently intersected to the
last hour. Pass an offset at least as wide as the window (`--offset P3D`).

**There is more than one component.** The function app's is
`e3f91af5-af22-43b6-a9d8-6cc0aa0cfde7` (`canada-east-ai-function`, in
`consultologist_group` — *not* the hostname prefix). Querying
`0b51e6cf-8720-48f6-9c84-6ed68c4e9914` instead produced a whole session of
false "no telemetry" conclusions.

Sanity-check any new query against a time range known to have data **before**
trusting an empty result. An absence is evidence only once the read path is
proven — the recurring wrong turn of Milestone 16, three times over.

### `az functionapp show --query state` is null on Flex Consumption

*Symptom: `state`, `defaultHostName` and `availabilityState` all return
`None`.* The Flex Consumption shape does not populate them through those
JMESPath paths. Probe the app over HTTP instead — `GET /api/HttpProbe`
returns 204 — which is better evidence anyway.
