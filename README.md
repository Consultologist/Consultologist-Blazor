# Consultologist

A clinical consult-note generation app. The frontend is a standalone ASP.NET Core Blazor
WebAssembly PWA (Fluent UI, Microsoft Entra ID sign-in via MSAL); the backend is a .NET 10
isolated Azure Functions app that runs durable consult-generation jobs against Azure AI
Foundry agents and streams progress to the browser over server-sent events (SSE).

![The app after sign-in.](./media/app-signedin.png)

## Repository layout

```
/
├── Consultologist.sln           solution covering all three projects
├── src/
│   ├── Consultologist.Web/      Blazor WASM frontend (Pages/, Shared/, Services/, wwwroot/)
│   └── Consultologist.Api/      Azure Functions backend (deployed separately)
├── tests/                       xUnit tests for the API project
├── external/                    vendored content registries (agents, package-format, provenance)
├── docs/                        design records and research notes — see docs/README.md
├── scripts/                     smoke tests, validators, the record-hash verifier
├── build/                       centralized bin/obj output (gitignored)
└── .github/workflows/           frontend deploy, API deploy, tests
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The WebAssembly build tools: `dotnet workload install wasm-tools`
- An Azure subscription with a Microsoft Entra tenant (for sign-in and deployment)

## Setup

### 1. Register the app in Microsoft Entra ID

Follow [Register an application with the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/quickstart-register-app) with:

| Setting | Value |
|---|---|
| **Supported account types** | Accounts in this organizational directory only (single tenant) |
| **Platform type** | Single-page application |
| **Redirect URI** | `http://localhost:5000/authentication/login-callback` |

### 2. Configure the frontend

In `src/Consultologist.Web/wwwroot/appsettings.json`, set the values from your app registration:

```json
"Authority": "https://login.microsoftonline.com/<tenant ID>",
"ClientId": "<client ID>",
```

## Run

```bash
# Frontend (serves on http://localhost:5000)
dotnet run --project src/Consultologist.Web

# Backend (Azure Functions Core Tools; or use the VS Code build/run tasks)
cd src/Consultologist.Api && func start
```

Sign in with an Entra account, then sign out from the header when done.

![The app signed out.](./media/app-signedout.png)

## Tests

```bash
dotnet test
```

## Workflow packages

Consult workflows are versioned, immutable packages (`packages/general/`): a manifest
declaring the node DAG, prompt templates, data collections, and output schemas —
published to an Azure Blob registry with `scripts/publish-workflow-package.sh`. See
[`docs/customizable-workflow/`](./docs/customizable-workflow/README.md).

`packages/general/dag.mmd` is a **generated** view of the manifest's DAG — never edit
it by hand. After changing `manifest.json`, run:

```bash
./scripts/update-dag-diagram.sh
```

and commit the regenerated file with the manifest. Generation is local and
pre-commit; `git push` only *verifies*: CI regenerates in memory and fails the build
if the committed diagram is stale.

## Deployment

Three GitHub Actions workflows under `.github/workflows/`:

- **Azure Static Web Apps** — builds and deploys `src/Consultologist.Web` on pushes to `main` (with PR preview environments).
- **Azure Function App** — builds and deploys `src/Consultologist.Api` on pushes to `main` that touch it.
- **Tests** — runs the xUnit suite on pushes and pull requests.

## Documentation

Design and operations notes live in [`docs/`](./docs/README.md) — consult generation
workflow and events, durable jobs, SSE design, accounts and auth, storage, and research
notes.

## License

[PolyForm Strict License 1.0.0](./LICENSE.md) — noncommercial use only; no distribution
or derivative works. That is the engine. The content registries are licensed
separately (#399): the package format and the provenance contract under
**CC BY-NC-ND 4.0**, the workflow packages and the agent definitions under
**CC BY-NC-SA 4.0** — each registry's README states the scope, and every
published version carries its `LICENSE`. Inside the app the terms of use grant
more than the public licences do: a user may use a package commercially there
and holds the copyright in the fork they author.
