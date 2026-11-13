# Codebase-Wide Read — Consultologist.sln

**Date:** 2026-11-13
**Method:** `dotnet build Consultologist.sln -nologo -v:minimal` — the Roslyn compiler, the same engine OmniSharp drives for diagnostics. Authoritative and complete in one pass.

## Result

**9/9 projects build — 0 errors, 187 warnings.**

| Project | Warnings |
|---|---|
| tests/Consultologist.Api.Tests | ~119 |
| tests/Consultologist.Web.Tests | ~22 |
| src/Consultologist.PackageFormat | ~18 |
| src/Consultologist.Api | ~11 |
| src/Consultologist.Web | ~5 |
| src/Consultologist.UI | 0 |
| src/Consultologist.Admin | 0 |
| tests/Consultologist.Web.E2E.Tests | 0 |
| tests/Consultologist.Admin.Tests | 0 |

## Warning profile (187 total)

| Category | ≈ count | Meaning |
|---|---|---|
| CS8604 | ~84 | Possible-null argument (nullable enumerables into LINQ/APIs) |
| xUnit1031 | ~32 | Blocking task op (`.Result`/`.Wait()`) in tests instead of async/await |
| CS8602 | ~21 | Dereference of possibly-null reference |
| CS8625 / CS8620 | ~20 | `null` literal to non-nullable param; unused nullable annotation |
| CS0618 | ~8 | Obsolete API use |
| xUnit style (2000/2029/2013/2031/1013/2012/1026) | ~30 | Assert-arg order, `Assert.Empty` vs `DoesNotContain`, `Where`-before-`Single`, etc. |
| CS8425 / CS4014 | ~8 | Async-iterator `CancellationToken` unconsumed; fire-and-forget call |
| NU1902 / NU1903 | 6 | NuGet vulnerability audit (below) |
| CS8619 / CS8603 / CS8321 / CS0169 | ~8 | Nullability mismatch; unused local function; unused field |

Production code (`Api`, `Web`, `PackageFormat`) is comparatively clean; the bulk of warnings are in the test projects. The nullability warnings are annotation strictness, not runtime bugs.

## Security finding (actionable)

**`System.Security.Cryptography.Pkcs` 8.0.1** — HIGH severity `NU1903` (GHSA-846f-f764-76mx) and moderate `NU1902` (GHSA-2fmv-rv2c-59x7).

- Not a direct dependency — transitive via **PDFsharp-MigraDoc 6.2.4** (Api PDF generation, `ConsultDocumentPdf.cs`).
- Fix: bump PDFsharp to a release pulling Pkcs ≥ 8.0.2, or add a direct `System.Security.Cryptography.Pkcs` reference at 8.0.2+.

## Environment notes

- **`wasm-tools` workload not installed** — blocks `dotnet run`/`publish` of `Consultologist.Web` (Blazor WASM), not `build`. Install with `dotnet workload install wasm-tools`.
- `global.json` pins SDK 10.0.102 (`rollForward: latestFeature`); active SDK is 10.0.404.
