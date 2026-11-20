# Area 04: JSON Logic (specVersion 31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Consultologist.PackageFormat` the expression language of specVersion 31: the written form of a JSON Logic rule and its checker, the printer, every operation with its null rules, the evaluator behind `IJsonLogicEvaluator`, the typing of every expression site of a package source, and the three contagious flags as an algorithm, with nothing in the engine calling any of it yet.

**Architecture:** New files beside areas 02 and 03's in `src/Consultologist.PackageFormat/V31/`. A rule is a `JsonNode`. `JsonLogicShape` judges the written form with no types; `JsonLogicPrinter` turns a rule into text and back; `JsonLogicFunctions` holds what each operation computes, as plain C# on `JsonNode` values; `CorvusJsonLogicEvaluator` runs a rule on `Corvus.Text.Json.JsonLogic` with those functions registered over Corvus's own. `ExpressionChecker` types one rule at one site against an `IExpressionScope` (who can be read there) and a `FormatSet`; `SourceExpressions.Check` walks a parsed package and is the third step of `SourceCheck.Run`. Reads through `node`, `check`, `item`, `pass` and `param` are resolved by a scope that area 05 supplies (D2); until then they are typed as unknown.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonNode`), `Corvus.Text.Json.JsonLogic` 5.8.12 (behind `IJsonLogicEvaluator`), Markdig 0.41.3 (`plain`), `System.Text.RegularExpressions` (`NonBacktracking`), xUnit in `tests/Consultologist.Api.Tests.csproj`.

**Spec:** `docs/customizable-workflow/package-format-v31/` — [`03-expressions.md`](../03-expressions.md), all of it; [`06-checks-deliverables-profile.md`](../06-checks-deliverables-profile.md) § 2 (`check_result` and the three check operations); [`04-loops-and-functions.md`](../04-loops-and-functions.md) § 2.6 (the `.items` element `all_pass` reads); [`10-migration-and-tools.md`](../10-migration-and-tools.md) § 5 (the conformance suite); [`refusal-sentences.md`](../refusal-sentences.md). The published contract is `external/consultologist-package-format/v31/` (`constants.json`, `sentences.json`, `formats/check_result.json`, `conformance/`) and `external/consultologist-agents/agents/v31/formats/concept_list.json`. Area issue #1501, parent #1496, runbook [`BUILD-RUNBOOK.md`](../BUILD-RUNBOOK.md). Area 03's plan, [`area-03-types.md`](area-03-types.md), says what the type services do (its D-numbers are cited as "03-D9").

## Global Constraints

- **Worktree only:** `~/Projects/worktrees/m39-area-04` on `m39-area-04`. The content repo is worked in `~/Projects/consultologist-package-format` on `m39-build`, pushed directly; the pin under `external/` moves only to a pushed commit.
- **Never edit** `.github/`, `.claude/`, `scripts/m39-build/`, `AGENTS.md`, `CLAUDE.md` or `BUILD-RUNBOOK.md`, in any repo.
- **31 stays off:** no file under `src/Consultologist.Api`, `src/Consultologist.Web`, `src/Consultologist.Admin` or `src/Consultologist.UI` changes; no accepted-version set changes. Everything is reachable only from tests.
- **Nothing the engine at 30 reads changes:** outside `V31/`, the only edits are `Directory.Packages.props` (one pin), `Consultologist.PackageFormat.csproj` (one reference and its comment) and the library guide.
- **Sentences are used as written.** A sentence with a row in `v31/sentences.json` is that row character for character with its `<…>` filled in, under that row's id. A rule with no row gets an area sentence `A04-NNN` (table below), each listed in the PR under *Decisions made during build*.
- **No sentence, printed text or failure carries a value** from an input or a model: only ids, paths, operation names, type strings, counts, limits and package text (a literal, a pattern, a `failWith`).
- **Never throw on package content or run data.** Every public method that takes a rule, a pattern, a value or data returns for any `JsonNode`; `ArgumentNullException` for a null `site`, `scope`, `formats` or `package` argument is the only exception. A rule or value that is C# `null` is the JSON value `null`.
- **Treat model JSON as read-only:** clone (`DeepClone`) before building a new tree from part of another.
- **Invariant culture, ordinal comparison,** everywhere. Characters are Unicode scalar values (`Rune`), never UTF-16 units.
- **Fictional only:** invented people and clinical text (Margaret Okafor, Dr A. Sample, letrozole for a made-up patient).
- **Goldens come from code,** are read, and are listed in the PR.
- **Constants** (from `v31/constants.json`; tests hold the code to the file): evaluator `1.0.0`; printer `1.0.0`; every custom operation `1.0.0`; `plain_conversion` `1.0.0`; limits `matchesPatternCodePoints` 200, `matchesRepetition` 100, `matchesTextBytes` 1048576; run facts `date`, `id`, `package`, `engine`; states `completed`, `skipped`, `not_run`.
- **Commits:** small, by concern: `PackageFormat (#1501): …`, `Tests (#1501): …`, `Docs (#1501): …`; in the content repo `Format spec (Consultologist-Blazor#1501): …`. Every message ends with `Co-Authored-By: Claude <noreply@anthropic.com>`. Push after each task.
- **Tests:** `tests/V31Logic/`, namespace `Consultologist.Api.Tests.V31Logic`. Command: `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"` (runs areas 02 and 03's too, which must stay green).

## Decisions this plan takes where the spec is silent

Each goes in the PR body under *Decisions made during build*.

| # | Where | Choice |
|---|---|---|
| D1 | § 9 "pins the version current when it starts" | `Corvus.Text.Json.JsonLogic` is pinned exactly at 5.8.12, the version current on 2026-11-20 and the one area 03's validator already shares `Corvus.Text.Json` with. |
| D2 | § 4.1, § 7 "Reads must exist"; issues #1501 and #1502 | Area 05 owns part 01 § 9 and part 04's publish-time rules, so which `node`, `check`, `item`, `pass` and `param` a site can see, and their types, come from an `IExpressionScope` area 05 writes. This area's own scope (`PackageScope`) resolves `input.<id>` and `run.<fact>` and answers **deferred** for the other five roots; inside a function it defers `input`, `run` and `check` too (S04-050 and S04-051 are area 05's). A deferred read has no type: it fits any operand, and a result with no type is not held to S03-025 or S03-026. Part 03's own root and site rules (S03-013, S03-015, S03-018 to S03-022) are judged before the scope is asked. |
| D3 | S03-015 to S03-017 against S02-018 to S02-020; S03-036 against S01-029 and S02-011 | Rows that state one rule twice: a JSON Logic site says the S03 row (part 03 owns the site); the S02 rows are left to templates (area 07). The flags' disagreement is worded as S03-036. |
| D4 | § 11.1 `<path> (<format>)`, `<site>` | Where a row shows `<path> (<format>)` and the operand is not a `var`, the operand's printed text stands for `<path>`. `<format>` is `TypeStrings.Print`, with ` \| null` added when the read is nullable and the format is not. `<site>` is `Node`, `Loop`, `While`, `If`, `Case`, `Check`, `Deliverable`, `Macro`, `Snippet` with `'<id>'`, `Part '<p>' of node '<id>'`, and `Computed node '<id>'` for an `expression`; a macro folder is `Macro '<folder>'` and its item `Macro '<folder>.<item>'`; inside a function ` in function '<f>'` follows. |
| D5 | § 3 | A single snippet's `if` (part 05 § 6.1) is not in § 3's table. It is a condition site, `Snippet '<id>'`, read as a macro's `if` is, without the `param` rule. |
| D6 | § 2.2 paths | A path is non-empty segments joined by dots. A segment of digits only is a position (S03-013). Any other segment is accepted by the written form (a catalogue format declares `isSnomedConcept`), and existence is the scope's and the format's to say. |
| D7 | § 2.1, § 5.1 | A JSON object with no member or more than one is not a rule (A04-004). An array's elements must have one type (A04-003). |
| D8 | § 5.1 arity words | S03-010's `<m>`: `1`, `2`, `3`, `1 or 2`, `1 or more`, `2 or more`, and for `if` `3 or more`. An `if` with an even count of 2 or more is S03-034; with 1 operand S03-010. `missing` with no path is S03-010 (`1 or more`). S03-006's `<the allowed list>` is every allowed name, the standard ones then the custom ones, in `constants.json`'s order, joined by `, `. |
| D9 | § 5.3, § 5.4 literals | A `pattern`, `word`, `failWith`, `min` or `missing` path that is an operation or an array is S03-029 (`<operand>`: `pattern`, `word`, `failWith`, `min`, `path`). A literal of the wrong JSON kind is A04-010, except `min`, which is S03-030 with `<n>` its JSON text. |
| D10 | § 5.3 "checked against RFC 9485's grammar" | The grammar is RFC 9485 § 4's ABNF, transcribed by hand in `IRegexp`. S03-031's `<reason>` is one of the fixed phrases of Task 1. A counted repetition is over the limit when either of its numbers is over 100. Checks run in this order and stop at the first: length, grammar, repetition, compile. |
| D11 | § 5.3 "translated to .NET per RFC 9485 § 5", "counts code points" | Translation: every literal is escaped; `^` and `$` are literals in I-Regexp, so they are escaped too; `.` is any code point but `\n` and `\r`; a character outside the Basic Multilingual Plane is one atom (its surrogate pair, grouped); a class is rebuilt from code-point ranges so that it, and its negation, match whole code points. **Unicode categories are judged on Basic Multilingual Plane code points only:** .NET tests categories per UTF-16 unit, so a code point outside the plane is in no `\p{…}` and in every `\P{…}`. `\p{C}` is written as its four members (`Cc`, `Cf`, `Cn`, `Co`), since .NET's `C` holds the surrogates. |
| D12 | § 5.3 "doesn't compile", "1 MiB" | S03-043's `<reason>` is `it needs too large an automaton` when .NET refuses the size, else `.NET does not accept it`; never .NET's own message (it is localised). The text limit is 1,048,576 bytes of UTF-8. S03-040's `<n>` is the size in MiB to one decimal, rounded up (`1.1`). |
| D13 | § 5.2 `plain` "the same conversion as plain-text output" | No part defines the conversion's steps. `PlainConversion.ToPlain` is Markdig's `Markdown.ToPlainText` with the `UseAdvancedExtensions` pipeline (part 02 § 5.2), `\r\n` and `\r` turned to `\n`, and line breaks at the very end removed. It is `plain_conversion` 1.0.0, pinned by goldens, and the one conversion areas 07 and 08 use for plain-text output. |
| D14 | § 5.2 `plain` "a plain-text operand is returned unchanged" | A value at run time is a string with no format, so the evaluator cannot tell. `ExpressionCheck.PlainConverts` lists, for each `plain` in the rule in depth-first order, whether its operand is Markdown; `IJsonLogicEvaluator.Compile` takes that list and converts only those. Without the list every `plain` converts. Where the list is kept (in the compiled package, or worked out again at load) is areas 05 and 06's. |
| D15 | § 9 "by registering our own compilers for the affected built-ins" | Corvus's defaults differ from § 5 in more places than § 9 lists (probed on 5.8.12): its `var` gives *undefined* for an absent path, so `=== null` is false, and it indexes arrays; its `missing` does not count `""`; its `and` and `or` return an operand, so `and [null, true]` is `null`; `+` reads `null` as 0; `cat` writes `null`; an overflow throws. Our compilers are registered for every operation of § 5 except `if`, `!`, `===` and `!==`, which are Corvus's. `and` and `or` give `true` or `false`. |
| D16 | § 5.4 `object` | Corvus compiles an operand that is an object as a rule and throws on its field names. Before a rule goes to Corvus, `{ "object": { "a": r1, "b": r2 } }` is rewritten to `{ "object": [ "a", r1, "b", r2 ] }`; the rule a package holds is unchanged. |
| D17 | § 9 "declines a rule naming any operation outside § 5" | The evaluator declines a rule with any fault of the written form (`JsonLogicShape.Check`), which includes every operation outside § 5, and a rule nested more than 64 deep. |
| D18 | § 6.2 "evaluation never throws"; data that escaped validation | At run time an operand of a kind its operation does not take is read as `null` by that operation: `"3" + 1` is `null`, `lower(5)` is `null`, `5 < "a"` is `false`. A date that is not `YYYY-MM-DD` in years 0001 to 9999 is `null`. `<`, `>`, `<=`, `>=` compare two numbers, or two strings that are both dates; anything else is `false`. Nothing is coerced. |
| D19 | § 5.1 `cat` "numbers in the invariant culture"; § 7 numbers | `cat` writes a number in its RFC 8785 form, as the printer does. A result that is not finite is `null`. `%` is .NET's remainder (the sign of the dividend). |
| D20 | § 5.2 Unicode | Whitespace is `Rune.IsWhiteSpace` (Unicode `White_Space`); `lower` is `ToLowerInvariant`; `size` counts `Rune`s; a word character is a `Rune` whose category is a letter, a mark, a decimal digit or connector punctuation. The text vectors carry no runtime or Unicode version of their own: `lock.json` records them (area 05). |
| D21 | § 5.4 check operations | `terms_subset`'s operands are `concept_list` values (`{ "concepts": [...] }`); `null`, or anything else, is an empty list. `uncovered` and `untested` are `null` when empty, as at 30. For `all_pass` and `count_pass` an element with a `state` member is judged when its `state` is `completed`, on its `value`; an element without one is judged as it is; an element that is not an object, or whose judged value has no boolean `pass`, is not judged. |
| D22 | § 9 "A condition that yields no boolean" | `Decide` tests truthiness (§ 6.4): `true`, `false`, `null` and any list decide; a number, text or an object fails with S03-041, `<a value kind>` being `a number`, `text` or `an object`. A declined rule or a fault inside Corvus fails with A04-011. |
| D23 | § 8 the printer | The printer is total. For a rule with no fault of the written form its text parses back to the same tree; `JsonLogicPrinter.TryParse` is built here so that the round trip can be tested, and is the converter's to reuse (area 10). To keep the tree: one-operand `-` on a number literal prints `-(1)` (a negative literal prints `-1`); a child inside a function form, an array or an `object` is never parenthesised; a `var` path that is not dot-joined identifiers, or whose first segment is a printer word (`true`, `false`, `null`, `not`, `and`, `or`, `in`, `if`, `then`, `else`), prints as `var("<path>")`; an `object` field that is not an identifier prints as a JSON string; an empty `object` prints `{}`. An operation whose operand count its form cannot show, an operation outside § 5, or one with a bare operand, prints as `name(a, b)`; a non-rule object prints as its canonical JSON. |
| D24 | § 7 typing, where no row words the refusal | Operand rules are Task 7's table. A string literal is `string`, and also fits an enum it is a value of, a `date` it is a valid date of, and a text format (package text is shape only). A number literal is `integer` when whole, else `number`. `size`, `words`, `lines`, `days_between` and `years_between` type as `integer` (which fits `number`); arithmetic, `min` and `max` as `number`. `lower` and `trim` of an enum give `string`. `cat` gives `string` only when every operand is a string or an enum. |
| D25 | § 6.2 "nullable operands are not refused at publish" | The checker tracks **declared** nullability only: a `null` literal, a read whose format admits `null` (or an input that is not required), an `if` with such a branch. An operation's result is never nullable for typing, though it can be `null` at run time. A computed node whose result is nullable declares a nullable format (S03-026 otherwise); an `object` field that is nullable needs a nullable property (A04-009). |
| D26 | § 3 "or as the list `missing` returns" | A condition types as `boolean`, or is a `missing` operation itself. A read of a `list<string>` is not a condition. |
| D27 | § 5.4 `object` "checked against the computed node's format" | `object` is accepted only where a format is expected: a computed expression's result, through `if` branches, array elements against a list's `items`, and its own fields against their properties. Anywhere else it is A04-008. |
| D28 | §§ 4.4–4.6, § 11.2; issues #1501 and #1502 | The flags are worked out from the compiled package's edges, which area 05 builds, and from what templates read, which area 07 finds. This area builds the algorithm (`LineageFlags`) over a graph its caller supplies, and the sentences S03-036, S03-038, S03-039 and S03-044. `SourceExpressions` gives S03-037 and S03-038 only for a site that reads `run.<fact>` itself. No package reaches S03-036 or S03-044 yet, so the suite has no case for them. |
| D29 | part 10 § 5 "what a case looks like is fixed by the area that exports its family" | The six families' files are Task 11's: one JSON file per group, `{ "about", "cases": [...] }`, a case holding `rule`, `data` and `result` (vectors), `output` (an operation golden: the result's canonical JSON, as a string) or `text` (a printer golden). |

**Area sentences** (ids not in `sentences.json`):

| Id | Sentence |
|---|---|
| A04-001 | `<site> gives <operation> <operand> (<format>); <operation> takes <expected>.` (`<expected>`: `numbers`, `booleans`, `a condition`, `text`, `text or a list`, `a list of its type, or text`, `a string to find`, `strings, numbers and plain text`, `dates`, `a date and a number`, `two concept_list values`, `a list of check results`) |
| A04-002 | `<site> compares <a> (<format>) with <b> (<format>); = and ≠ compare two values of one type.` |
| A04-003 | `<site> mixes <format> and <format> in <the branches of an if \| a list>; they have one type.` |
| A04-004 | `<site> writes an object with <n> members where a rule is expected; an operation is an object with one member, and object builds an object.` |
| A04-005 | `<site> writes var with <what it is>; var takes one path, written as text.` (the words of A02-001: `a number`, `true or false`, `an object`, `null`) |
| A04-006 | `<site> reads '<path>', which is not a path; a path is a root, then names joined by dots.` |
| A04-007 | `<site> writes object with <what it is>; object takes one object, from field names to rules.` |
| A04-008 | `<site> builds an object where no format gives its fields; object builds the value of a computed node.` |
| A04-009 | `Computed node '<id>' builds <format> with <field> as <format>; the format declares it <format>.` |
| A04-010 | `<site> gives <operation> a <operand> that is <what it is>; <operand> is written as text.` |
| A04-011 | `<site> <member> could not be evaluated: <detail>.` (run time; `<detail>`: `it uses <operation>, which is not an operation of this format`, `it is not a rule of this format`, `the evaluator failed`) |

## Files

```
Directory.Packages.props                         Corvus.Text.Json.JsonLogic 5.8.12                     Task 5
src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj   + the reference                 Task 5
src/Consultologist.PackageFormat/V31/
  JsonLogicOperations.cs   the operations of § 5: names, operand counts, versions                      Task 1
  IRegexp.cs               RFC 9485: grammar, bounds, translation to .NET, compile                     Task 1
  JsonLogicShape.cs        the written form (§ 2, § 5 arity, literals), no types                       Task 2
  JsonLogicPrinter.cs      rule → text, text → rule, reason sentences                                  Task 3
  PlainConversion.cs       Markdown → plain text                                                       Task 4
  JsonLogicFunctions.cs    what each operation computes, null rules included                           Task 4
  JsonLogicEvaluator.cs    IJsonLogicEvaluator, IJsonLogicRule, results, CorvusJsonLogicEvaluator      Task 5
  ExpressionSentences.cs   one method per S03 / A04 sentence                                           Tasks 6–9
  ExpressionScope.cs       ExpressionSite, IExpressionScope, ScopeHead, ExpressionType                 Task 6
  ExpressionChecker.cs     Check: the written form, reads, site rules, the result                      Task 6
  ExpressionChecker.Operations.cs   operand and result types of every operation                        Task 7
  SourceExpressions.cs     every site of a package; PackageScope; the run-fact warnings                Task 8
  SourceCheck.cs           + the third step                                                            Task 8
  LineageFlags.cs          the three contagious flags over a graph                                     Task 9
tests/V31Logic/
  *Tests.cs                one per source file
  LogicFixture.cs          rule and data builders, a dictionary-backed IExpressionScope                Task 6
  LogicVectors.cs          the suite's inputs (rules and data), by family                              Task 11
  LogicVectorsExport.cs    the generator (CONFORMANCE_EXPORT_V31)                                      Task 11
tests/V31Source/           V31Contract (A04 rows), RefusalPackages.Logic.cs (S03 cases)                Tasks 6, 10
docs/customizable-workflow/package-format-library.md   + "specVersion 31: JSON Logic"                  Task 12
consultologist-package-format (m39-build)   v31/conformance/** (six families, refusal packages),
                                            README.md, index.json, scripts/check-v31.py               Tasks 10, 11
```

## Review Focus

1. **Garbage where a rule is expected:** `{}`, `{ "a": 1, "b": 2 }`, `{ "var": null }`, `{ "if": {} }`, `{ "": [] }`, a bare `true` operand, an operation nested 80 deep, a hand-edited compiled rule naming `map`. Expected: refusals at publish, *declined* at run time, readable text from the printer; never an exception, never a stack overflow, never Corvus's default behaviour. (Task 2 `AnyMutationOfAValidRule_NeverThrows`; Task 3 `Print_IsTotal`; Task 5 `ARuleOutsideTheFormat_IsDeclined_NeverRun`; Task 7 `AnyMutation_IsRefusedOrTyped_NeverThrows`.)
2. **Run data that escaped validation:** a number where text is expected, text where a number is, an object given to `size`, `2026-02-30`, `2026-2-3`, `1e308 * 10`, a `check_result` without `pass`. Expected: `null` or `false` by the operation's row, no coercion, no exception. (Task 4 `OperandsOfTheWrongKind_ReadAsNull`; Task 5 `Evaluate_NeverThrows_OnAnyData`.)
3. **Characters outside the Basic Multilingual Plane, combining marks and odd whitespace:** `size` of an emoji with a joiner, `has_word` beside an accented or astral letter, `matches` with `.`, a class and a quantifier over an emoji, `trim` and `blank` on U+00A0 and U+2003, `lower` of `İ`. Expected: counts in code points, boundaries by Unicode category, the same bytes on every machine. (Task 1 `Translation_CountsCodePoints`; Task 4 `Text_IsCountedInCodePoints`.)
4. **Text at the edges:** `""`, only whitespace, `\r\n` against `\n` against a lone `\r`, text of exactly 1 MiB and one byte more given to `matches`. Expected: the table's values; the over-long text fails the rule with S03-040, and the sentence holds a size, never the text. (Task 4 `Lines_CountsEveryLineBreak`; Task 5 `Matches_FailsOverOneMebibyte_AndNeverQuotesTheText`.)
5. **Two trees that could print alike:** `-1` and `{ "-": [1] }`; `and` in `and`; an `if` in an `else`; `a - (b - c)`; a string literal holding `" and "`, a quote, a newline or `≠`; a path named `true`. Expected: different trees print differently, and every printed rule parses back to itself. (Task 3 `DifferentTrees_PrintDifferently`, `EveryGeneratedRule_RoundTrips`.)

---

### Task 1: The operation table and I-Regexp

**Files:**
- Create: `V31/JsonLogicOperations.cs`, `V31/IRegexp.cs`, `tests/V31Logic/JsonLogicOperationsTests.cs`, `tests/V31Logic/IRegexpTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record JsonLogicOperation(string Name, int Min, int? Max, bool Custom)` — `Max` null is "or more". `var` and `object` are `(1, 1)`; `if` is `(3, null)`.
  - `public static class JsonLogicOperations { const string EvaluatorVersion = "1.0.0"; const string PrinterVersion = "1.0.0"; const string OperationVersion = "1.0.0"; IReadOnlyList<JsonLogicOperation> All; JsonLogicOperation? Find(string name); string AllowedList; string Takes(JsonLogicOperation operation); IReadOnlyList<string> RunFacts; }` — `All` is the 21 standard names then the 18 custom ones, in `constants.json`'s order. Operand counts: `missing` 1+; `and`, `or`, `+`, `*`, `cat` 2+; `min`, `max` 1+; `!`, `size`, `blank`, `words`, `lines`, `lower`, `trim`, `plain` 1; `-` 1–2; `===`, `!==`, `<`, `<=`, `>`, `>=`, `/`, `%`, `in`, `starts_with`, `ends_with`, `matches`, `has_word`, `all_pass`, `days_between`, `years_between`, `add_days` 2; `terms_subset`, `count_pass` 3. `Takes` gives D8's words.
  - `public enum IRegexpFault { None, TooLong, NotIRegexp, Repeats, DoesNotCompile }`
  - `public sealed record IRegexpCheck(IRegexpFault Fault, string Reason, int Count, Regex? Regex)` — `Count` is the code points for `TooLong`, the offending number for `Repeats`; `Regex` is set exactly when `Fault` is `None`.
  - `public static class IRegexp { const int MaxCodePoints = 200; const int MaxRepetition = 100; const int MaxTextBytes = 1048576; IRegexpCheck Check(string pattern); string? ToDotNet(string pattern); bool TooLong(string text); bool IsMatch(Regex regex, string text); }` — `Check` caches its result by pattern (a `ConcurrentDictionary`); `ToDotNet` is null when the pattern is not I-Regexp; `IsMatch` is search (anywhere).

The grammar (RFC 9485 § 4), over code points:

```
i-regexp = branch *( "|" branch )          branch = *piece          piece = atom [ quantifier ]
quantifier = "*" / "+" / "?" / "{" QuantExact [ "," [ QuantExact ] ] "}"      QuantExact = 1*DIGIT
atom = NormalChar / charClass / ( "(" i-regexp ")" )
NormalChar = %x00-27 / "," / "-" / %x2F-3E / %x40-5A / %x5E-7A / %x7E-D7FF / %xE000-10FFFF
charClass = "." / SingleCharEsc / charClassEsc / charClassExpr
SingleCharEsc = "\" ( %x28-2B / "-" / "." / "?" / %x5B-5E / "n" / "r" / "t" / %x7B-7D )
charClassEsc = "\p{" charProp "}" / "\P{" charProp "}"
charProp = "L" ["l"/"m"/"o"/"t"/"u"] / "M" ["c"/"e"/"n"] / "N" ["d"/"l"/"o"] / "P" ["c"/"d"/"e"/"f"/"i"/"o"/"s"]
         / "Z" ["l"/"p"/"s"] / "S" ["c"/"k"/"m"/"o"] / "C" ["c"/"f"/"n"/"o"]
charClassExpr = "[" [ "^" ] ( "-" / CCE1 ) *CCE1 [ "-" ] "]"
CCE1 = ( CCchar [ "-" CCchar ] ) / charClassEsc
CCchar = %x00-2C / %x2E-5A / %x5E-D7FF / %xE000-10FFFF / SingleCharEsc
```

S03-031's reasons, `<n>` a 1-based code-point position: `'<c>' at character <n> is not allowed there`; `the pattern ends inside a group`, `… a class`, `… an escape`, `… a repetition`; `'\<c>' at character <n> is not an escape of I-Regexp`; `'<name>' at character <n> is not a Unicode category of I-Regexp`; `the range at character <n> is backwards`; `the repetition at character <n> has its numbers backwards`.

- [ ] **Step 1: Write the failing tests**

`JsonLogicOperationsTests`:
- `Names_AreTheContractsNames`: `All.Where(!Custom).Select(Name)` equals `constants.json`'s `standardOperations` in order; the custom names equal the keys of its `operations` in order, each with value `OperationVersion`; `EvaluatorVersion` equals its `evaluator`, `PrinterVersion` its `builtins.printer`, `RunFacts` its `runFacts`; `IRegexp`'s three limits equal `limits.matchesPatternCodePoints`, `matchesRepetition`, `matchesTextBytes`. Read the file as `V31Contract` does (fail, never skip, when the submodule is missing).
- `Takes_SaysTheCount`: `Takes(Find("!"))` is `1`; `-` → `1 or 2`; `+` → `2 or more`; `min` → `1 or more`; `if` → `3 or more`; `count_pass` → `3`.

`IRegexpTests`:
- `ValidPatterns_Compile` (theory): `letrozole|femara`, `a{2,3}`, `[a-c]+`, `[^0-9]`, `\p{Lu}\P{Nd}`, `a\.b`, `(ab)*c?`, `\[x\]`, `^` and `$` (literals), `` (empty) → `Fault` `None`.
- `Search_FindsAnywhere` (theory: pattern, text, expected): (`letrozole|femara`, `started Femara then letrozole`, true); (`femara`, `Femara`, false); (`a.c`, `a\nc`, false); (`a.c`, `abc`, true); (`^a`, `a`, false); (`^a`, `x^a`, true); (`a$`, `a$`, true); (`\p{Lu}{2}`, `xABy`, true); (`[^a]`, `a`, false).
- `Translation_CountsCodePoints` (theory): (`a.b`, `a😀b`, true); (`a..b`, `a😀b`, false); (`😀{2}`, `😀😀`, true); (`😀{2}`, `😀`, false); (`[😀🙂]`, `🙂`, true); (`[^a]b`, `😀b`, true); (`a[^😀]b`, `a😀b`, false); (`a[^😀]b`, `a🙂b`, true); (`[😀-🙂]`, `😁`, true); (`a\P{L}b`, `a😀b`, true).
- `NotIRegexp_SaysWhy` (theory: pattern, reason): (`\d`, `'\d' at character 1 is not an escape of I-Regexp`); (`a\b`, `'\b' at character 2 is not an escape of I-Regexp`); (`(a`, `the pattern ends inside a group`); (`[a`, `the pattern ends inside a class`); (`a\`, `the pattern ends inside an escape`); (`a{2`, `the pattern ends inside a repetition`); (`a)`, `')' at character 2 is not allowed there`); (`*a`, `'*' at character 1 is not allowed there`); (`\p{Greek}`, `'Greek' at character 1 is not a Unicode category of I-Regexp`); (`[b-a]`, `the range at character 2 is backwards`); (`a{3,2}`, `the repetition at character 2 has its numbers backwards`); (`a(?=b)`, `'?' at character 3 is not allowed there`).
- `Bounds`: 200 `a`s → `None`; 201 → `TooLong`, `Count` 201; 200 `😀` → `None`; `a{100}` → `None`; `a{101}` → `Repeats`, `Count` 101; `a{2,101}` → `Repeats`, 101; `a{101,}` → `Repeats`, 101.
- `APatternTooLargeForLinearMatching_DoesNotCompile`: find, by trying nested counted repetitions (`((a{100}){100}){100}` and wider), a pattern within the other bounds that .NET's `NonBacktracking` refuses; assert `DoesNotCompile` and the reason `it needs too large an automaton`. If no pattern within the bounds is refused, assert that fact instead (`Assert.All` over the candidates gives `None`) and say so in the test's summary: Task 10 then ships no S03-043 case.
- `Compiled_WithTheEnginesOptions`: `Check("a").Regex!.Options` is `NonBacktracking | CultureInvariant`.
- `TooLong_CountsUtf8Bytes`: a string of 1,048,576 `a`s is not too long; one more is; 349,526 `€` (3 bytes each) is.
- `AnyText_NeverThrows`: for 2,000 strings built by a seeded `Random(31)` from the alphabet `a|()[]{}*+?.\^$-,0123456789pP😀` (length 0 to 12), `Check` returns, and when `Fault` is `None`, `IsMatch(regex, "a😀b")` returns.

- [ ] **Step 2: Run them; they fail to compile.** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31Logic"` → build error naming `JsonLogicOperations`.

- [ ] **Step 3: Implement `JsonLogicOperations` and `IRegexp`.** `IRegexp` parses to a small tree (alternation, sequence, repeat, literal code point, any, category, class of code-point ranges and categories, negated or not) with a recursive-descent parser over `Rune`s, bounded by the 200 code points; then writes .NET by D11: a literal as `\uXXXX` units (an astral one as `(?:😀)`); `.` as `(?:[^\n\r\uD800-\uDFFF]|[\uD800-\uDBFF][\uDC00-\uDFFF])`; a class as `(?:[<BMP ranges and categories>]|<astral ranges as surrogate-pair alternatives>)`, a negated one as `(?:[^<BMP items>\uD800-\uDFFF]|<the complement of its astral ranges>)`, with BMP ranges split around U+D800–U+DFFF; `\P{X}` alone as `(?:[^\p{X}\uD800-\uDFFF]|[\uD800-\uDBFF][\uDC00-\uDFFF])`. Compile with `RegexOptions.NonBacktracking | RegexOptions.CultureInvariant` and no time-out; catch `NotSupportedException` (D12's first reason) and any other exception (its second).

- [ ] **Step 4: Run the tests; they pass.**

- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): the operations of JSON Logic at 31, and I-Regexp checked, bounded and translated to .NET`

### Task 2: The written form

**Files:**
- Create: `V31/JsonLogicShape.cs`, `tests/V31Logic/JsonLogicShapeTests.cs`

**Interfaces:**
- Consumes: `JsonLogicOperations`, `IRegexp.Check`.
- Produces:
  - `public enum ShapeFaultKind { TooDeep, NotARule, LooseEquality, LooseInequality, Substr, UnknownOperation, BareOperand, Arity, IfWithoutElse, VarDefault, VarNotText, PathNotAPath, PathPosition, ObjectOperand, ComputedLiteral, LiteralNotText, CountPassMin, BlankWord, PatternNotIRegexp, PatternTooLong, PatternRepeats, PatternDoesNotCompile }`
  - `public sealed record ShapeFault(ShapeFaultKind Kind, string Operation, string Text, int Count, string Detail)` — by kind: `NotARule` (`Count` members); `UnknownOperation`, `BareOperand`, `LooseEquality`, `LooseInequality`, `Substr`, `IfWithoutElse` (`Operation`); `Arity` (`Operation`, `Count` given, `Text` = `Takes`); `VarNotText`, `ObjectOperand` (`Text` what it is, A02-001's words); `PathNotAPath`, `PathPosition` (`Text` the path); `ComputedLiteral`, `LiteralNotText` (`Operation`, `Text` the operand's name, `Detail` what it is); `CountPassMin` (`Text` the literal's JSON); `BlankWord`; the four pattern kinds (`Text` the pattern, `Count`, `Detail` the reason). Unused members are `""` and 0.
  - `public static class JsonLogicShape { const int MaxDepth = 64; IReadOnlyList<ShapeFault> Check(JsonNode? rule); bool IsPath(string path); bool IsPosition(string path); IReadOnlyList<string> Paths(JsonNode? rule); }` — `Check` gives every fault in tree order (an operation's own faults before its operands'); it descends into the operands of an unknown or mis-counted operation, and not into a `var`, a `missing` or a literal operand. `Paths` is every `var` path and `missing` path that is text, distinct, in first-written order.

Rules, each with its kind: a literal or an array of rules is a rule; an object needs exactly one member (`NotARule`); depth past 64 is `TooDeep`, said once. `==` → `LooseEquality`; `!=` → `LooseInequality`; `substr` → `Substr`; any other name not in `JsonLogicOperations` → `UnknownOperation`. `var`: an array → `VarDefault`; not text → `VarNotText`; text → `IsPath` (`PathNotAPath`) then `IsPosition` (`PathPosition`). `object`: not an object → `ObjectOperand`; its fields' values are rules. Every other operation: operands not an array → `BareOperand`; count outside `Min`–`Max` → `Arity`, except `if` with an even count of 2 or more → `IfWithoutElse`. `missing`: each operand a text literal (an operation or array → `ComputedLiteral` `path`; another literal → `LiteralNotText` `path`), then held to the path rules. Literal operands (D9): `matches` operand 2 (`pattern`): text, then `IRegexp.Check`'s fault; `has_word` operand 2 (`word`): text, not blank (`BlankWord`); `terms_subset` operand 3, `all_pass` operand 2, `count_pass` operand 3 (`failWith`): text; `count_pass` operand 2 (`min`): a number literal that is whole and 0 or more, at most 2,147,483,647 (`CountPassMin`; an operation or array → `ComputedLiteral`).

- [ ] **Step 1: Write the failing tests** (`JsonLogicShapeTests`; rules as JSON text, parsed by a local `Rule(string)` helper)

- `CanonicalRules_HaveNoFault` (theory): part 03's own examples, each verbatim: § 2.3's `or`; § 4.5's `in`/`lower`; § 5.4's `terms_subset`; § 5.5's `years_between`; § 6.2's two pitfall rules; and `{"missing":["input.age.value"]}`, `{"if":[true,1,2]}`, `{"if":[true,1,false,2,3]}`, `{"-":[1]}`, `{"object":{"title":{"var":"item.id"},"n":1}}`, `[1,{"var":"item.id"}]`, `null`, `"text"`, `{"count_pass":[{"var":"check.c.items"},0,"Too few."]}`.
- `EachFault` (theory: rule, kind, operation, text, count): (`{"==":[1,1]}`, `LooseEquality`); (`{"!=":[1,1]}`, `LooseInequality`); (`{"substr":["a",1]}`, `Substr`); (`{"map":[[1],1]}`, `UnknownOperation`, `map`); (`{"!!":[true]}`, `UnknownOperation`, `!!`); (`{"asDouble":[1]}`, `UnknownOperation`); (`{"!":true}`, `BareOperand`, `!`); (`{"<":[1,2,3]}`, `Arity`, `<`, `2`, 3); (`{"!":[]}`, `Arity`, `!`, `1`, 0); (`{"if":[true,1]}`, `IfWithoutElse`); (`{"if":[true]}`, `Arity`, `if`, `3 or more`, 1); (`{"missing":[]}`, `Arity`, `missing`, `1 or more`, 0); (`{"var":["input.a.value",0]}`, `VarDefault`); (`{"var":5}`, `VarNotText`, text `a number`); (`{"var":""}`, `PathNotAPath`); (`{"var":"input..value"}`, `PathNotAPath`); (`{"var":"node.rows.value.0"}`, `PathPosition`); (`{"missing":["node.rows.value.0"]}`, `PathPosition`); (`{"missing":[{"var":"a.b"}]}`, `ComputedLiteral`, `missing`, `path`); (`{"object":[1]}`, `ObjectOperand`, text `a list`); (`{}`, `NotARule`, count 0); (`{"a":1,"b":2}`, `NotARule`, count 2); (`{"matches":[{"var":"a.b"},{"var":"c.d"}]}`, `ComputedLiteral`, `matches`, `pattern`); (`{"matches":[{"var":"a.b"},5]}`, `LiteralNotText`, `matches`, `pattern`); (`{"matches":[{"var":"a.b"},"\\d"]}`, `PatternNotIRegexp`); (201 `a`s, `PatternTooLong`, count 201); (`a{101}`, `PatternRepeats`, count 101); (`{"has_word":[{"var":"a.b"},"  "]}`, `BlankWord`); (`{"has_word":[{"var":"a.b"},{"var":"c.d"}]}`, `ComputedLiteral`, `has_word`, `word`); (`{"all_pass":[{"var":"a.b"},{"cat":["a","b"]}]}`, `ComputedLiteral`, `all_pass`, `failWith`); (`{"count_pass":[{"var":"a.b"},{"var":"c.d"},"x"]}`, `ComputedLiteral`, `count_pass`, `min`); (`{"count_pass":[{"var":"a.b"},-1,"x"]}`, `CountPassMin`, text `-1`); (`…,1.5,"x"`, `CountPassMin`, `1.5`); (`…,"3","x"`, `CountPassMin`, `"3"`).
- `Faults_ComeInTreeOrder_AndReachInsideARefusedOperation`: `{"and":[{"==":[1,1]},{"map":[{"!=":[1,2]}]}]}` gives `LooseEquality`, `UnknownOperation`, `LooseInequality`, in that order.
- `NestedTooDeep_IsOneFault`: `{"!":[` 80 times around `true` → exactly one fault, `TooDeep`; 60 times → none.
- `Paths_ListsEveryRead`: `{"and":[{"===":[{"var":"input.a.value"},1]},{"missing":["run.date","input.a.value"]}]}` → `["input.a.value", "run.date"]`.
- `AnyMutationOfAValidRule_NeverThrows`: take each rule of `CanonicalRules_HaveNoFault`; for every node of its tree, replace that node in turn by each of `null`, `true`, `5`, `"x"`, `[]`, `{}`, `{"a":1,"b":2}`, `{"var":null}`, `{"":[]}`; `Check` and `Paths` return for every mutant.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement `JsonLogicShape`** as one recursive walk carrying its depth.
- [ ] **Step 4: Run the tests; they pass.**
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): the written form of a JSON Logic rule, judged without types`

### Task 3: The printer

**Files:**
- Create: `V31/JsonLogicPrinter.cs`, `tests/V31Logic/JsonLogicPrinterTests.cs`

**Interfaces:**
- Consumes: `CanonicalJson.Write` (a string's and a number's text), `JsonLogicOperations`.
- Produces: `public static class JsonLogicPrinter { const string Version = "1.0.0"; string Print(JsonNode? rule); bool TryParse(string text, out JsonNode? rule); string Reason(string state, JsonNode? rule, bool result); }` — `Reason("skipped", rule, false)` is `skipped: <printed> is false` (S03-001).

Forms (§ 8's table, with D23): precedence, tightest first: 7 function forms, literals, paths, arrays, `object`; 6 `not`, one-operand `-`; 5 `*`, `/`, `%`; 4 `+`, two-operand `-`; 3 `=`, `≠`, `<`, `>`, `≤`, `≥`, `in`; 2 `and`; 1 `or`; 0 `if`. A child operation is parenthesised when its level is lower than or equal to its parent's; operands of a function form, elements of an array and fields of an `object` are printed bare. `and`, `or`, `+`, `*` join all their operands; `missing` prints `missing(["a.b", "c.d"])`. `TryParse` reads exactly this text: a chain of one infix operator is one operation; mixed operators of one level without parentheses, or anything else the printer does not write, is `false`.

- [ ] **Step 1: Write the failing tests** (`JsonLogicPrinterTests`)

- `Prints` (theory: rule, text), every row of § 8's table and part 03's printed examples:
  - § 2.3's rule → `item.id ≠ "billing" or input.include_billing.value = true`
  - § 5.5's → `years_between(input.date_of_birth.value, run.date) ≥ 65`
  - § 5.2's → `"letrozole" in lower(node.draft.content)`
  - `{"===":[{"var":"input.specialty.title"},"Medical oncology"]}` → `input.specialty.title = "Medical oncology"`
  - `65` → `65`; `0.5` → `0.5`; `1e21` → `1e+21`; `-1` → `-1`; `true`, `false`, `null`; `"table"` → `"table"`; `"a\"b\n≠"` → `"a\"b\n≠"`
  - `[1,{"var":"a.b"},"c"]` → `[1, a.b, "c"]`; `[]` → `[]`
  - `{"<":[{"var":"a.b"},2]}` → `a.b < 2`; `<=` → `≤`; `>=` → `≥`; `>` → `>`
  - `{"and":[a,b,c]}` (three `var`s `x.a`, `x.b`, `x.c`) → `x.a and x.b and x.c`
  - `{"and":[{"and":[a,b]},c]}` → `(x.a and x.b) and x.c`
  - `{"or":[{"and":[a,b]},c]}` → `x.a and x.b or x.c`; `{"and":[{"or":[a,b]},c]}` → `(x.a or x.b) and x.c`
  - `{"!":[a]}` → `not x.a`; `{"!":[{"and":[a,b]}]}` → `not (x.a and x.b)`; `{"!":[{"!":[a]}]}` → `not (not x.a)`
  - `{"+":[1,2,3]}` → `1 + 2 + 3`; `{"*":[{"+":[1,2]},3]}` → `(1 + 2) * 3`; `{"-":[a,{"-":[b,c]}]}` → `x.a - (x.b - x.c)`; `{"-":[{"-":[a,b]},c]}` → `(x.a - x.b) - x.c`; `{"-":[a]}` → `-x.a`; `{"-":[1]}` → `-(1)`; `{"-":[a,-1]}` → `x.a - -1`; `{"/":[a,2]}` → `x.a / 2`; `{"%":[a,2]}` → `x.a % 2`
  - `{"in":["a",["a","b"]]}` → `"a" in ["a", "b"]`
  - `{"if":[a,1,2]}` → `if x.a then 1 else 2`; five operands → `if x.a then 1 else if x.b then 2 else 3`; `{"if":[a,1,{"if":[b,2,3]}]}` → `if x.a then 1 else (if x.b then 2 else 3)`; `{"+":[1,{"if":[a,1,2]}]}` → `1 + (if x.a then 1 else 2)`
  - `{"object":{"title":a,"content":b}}` → `{ title: x.a, content: x.b }`; `{"object":{}}` → `{}`; a field `my field` → `{ "my field": 1 }`
  - `{"size":[{"var":"input.prior_notes.value"}]}` → `size(input.prior_notes.value)`; `{"missing":["input.age.value"]}` → `missing(["input.age.value"])`; `{"min":[1,{"if":[a,1,2]}]}` → `min(1, if x.a then 1 else 2)`; `{"terms_subset":[a,b,"The note does not cover every term."]}` → `terms_subset(x.a, x.b, "The note does not cover every term.")`
  - `{"var":"true"}` → `var("true")`; `{"var":"a b"}` → `var("a b")`
- `Print_IsTotal` (theory): `{}` → `{}`; `{"a":1,"b":2}` → `{"a":1,"b":2}`; `{"!":true}` → `!(true)`; `{"<":[1,2,3]}` → `<(1, 2, 3)`; `{"map":[[1],2]}` → `map([1], 2)`; `{"var":5}` → `var(5)`; `{"==":[1,2]}` → `==(1, 2)`; an operation nested 200 deep returns a string (past `JsonLogicShape.MaxDepth` the rest prints as `…`).
- `Reason_IsTheRowOfTheSentences`: `Reason("skipped", {"===":[{"var":"input.kind.value"},"table"]}, false)` equals `sentences.json`'s S03-001 sentence, character for character.
- `DifferentTrees_PrintDifferently`: the pairs of Review Focus 5 (`-1` / `{"-":[1]}`; flat and nested `and`; flat five-operand `if` / nested `if`; `{"-":[a,{"-":[b,c]}]}` / `{"-":[{"-":[a,b]},c]}`; `"x.a and x.b"` the string / the `and`; `{"var":"true"}` / `true`) each print two different texts.
- `EveryGeneratedRule_RoundTrips`: build 3,000 rules with a seeded `Random(31)`: depth up to 4, drawing from every operation of `JsonLogicOperations.All` with a valid operand count (at most 4), `var` paths from a fixed list, literals (`null`, booleans, `0`, `-1`, `65`, `0.5`, `1e21`, `""`, `"table"`, `"a\"b"`, `" and "`, `"≠"`), arrays and `object`s; literal operands (`pattern`, `word`, `failWith`, `min`, paths) valid. For each: `JsonLogicShape.Check` is empty, `TryParse(Print(rule))` is true, and the parsed rule's `ToJsonString()` equals the rule's (member order included).
- `TryParse_RefusesWhatThePrinterNeverWrites` (theory): `x.a + x.b - x.c`, `x.a and`, `(x.a`, `x.a = = 1`, `"unterminated`, `` → false.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement `JsonLogicPrinter`:** `Print` as a recursive writer passing the parent's level; `TryParse` as a tokenizer and a precedence-climbing parser over the same levels, bounded by `JsonLogicShape.MaxDepth`, returning false on anything unexpected, never throwing.
- [ ] **Step 4: Run the tests; they pass.**
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): the printer of JSON Logic, and the parser of its text`

### Task 4: What each operation computes

**Files:**
- Create: `V31/PlainConversion.cs`, `V31/JsonLogicFunctions.cs`, `tests/V31Logic/PlainConversionTests.cs`, `tests/V31Logic/JsonLogicFunctionsTests.cs`

**Interfaces:**
- Consumes: `IRegexp`, `CanonicalJson.Write`.
- Produces:
  - `public static class PlainConversion { const string Version = "1.0.0"; string ToPlain(string markdown); }` (D13)
  - `public enum JsonLogicFailureKind { Declined, MatchesTextTooLong, NotABoolean, Fault }`
  - `public sealed record JsonLogicFailure(JsonLogicFailureKind Kind, string Detail)`
  - `public sealed class JsonLogicFailureException(JsonLogicFailure failure) : Exception { JsonLogicFailure Failure }`
  - `public static class JsonLogicFunctions { JsonNode? Apply(string operation, IReadOnlyList<JsonNode?> operands, bool plainConverts = true); bool Truthy(JsonNode? value); JsonArray Missing(IReadOnlyList<string> paths, Func<string, JsonNode?> read); JsonNode? Read(JsonNode? data, string path); }` — `Apply` is every operation of § 5 but `var`, `missing`, `if`, `and`, `or`, `!`, `===`, `!==` and `object` (which take rules or data, not values); it returns a new node, never one of its operands. It throws only `JsonLogicFailureException` (`MatchesTextTooLong`, `Detail` D12's `<n>`), and `ArgumentException` for a name it does not compute. `Read` walks `data` by the path's segments through objects only; anything absent, or a step into a non-object, is `null`. `Missing` gives the paths whose read is `null` or `""`, in written order. `Truthy` is § 6.4, with `0` and `""` false and any other value true.

Each operation is its row of §§ 5.1–5.5 with D18–D21. Stated here where the rows leave a choice: a number result is a `double`, written as `JsonValue.Create(double)`, and `null` when not finite; `<`-family: two numbers numerically, two valid dates chronologically, else `false`; `in`: an array second operand is membership (numbers equal numerically, strings ordinally, `null` equals `null`, booleans equal), a string second operand is an ordinal substring search with a string needle, anything else `false`; `cat`: a string as it is, a number in RFC 8785 form, anything else `""`; `size`: a string's `Rune` count, an array's count, else `null`; `lines`: `0` for `""`, else 1 plus the line breaks (`\r\n` is one) not counting one at the very end; `has_word`: any occurrence (overlapping ones too) with no word character on either side; `years_between(a, b)` for `b ≥ a`: `b.Year − a.Year`, less one when `b` is before `a`'s anniversary in `b`'s year (1 March when `a` is 29 February and the year has none); for `b < a`, `−years_between(b, a)`; `add_days`: `n` whole, the result within years 0001–9999, else `null`; `plain`: a string through `PlainConversion.ToPlain` when `plainConverts`, else unchanged; `terms_subset`, `all_pass`, `count_pass`: D21, the result an object with the six members of `check_result` in the format's order (`pass`, `message`, `uncovered`, `untested`, `passed`, `total`).

- [ ] **Step 1: Write the failing tests**

`PlainConversionTests` (theory: Markdown, plain; the expected texts are read from what the code gives on the first run, checked by eye, then frozen in the test): `**Letrozole** 2.5 mg daily` → `Letrozole 2.5 mg daily`; `# Plan\n\nContinue [letrozole](https://example.org).` (two blocks: the heading's text, a line break, the sentence without the link's address); a two-item list; a pipe table of two columns; `` (empty) → ``; `plain text, no marks` → itself; `a\r\nb` has no `\r`; no result ends with `\n`.

`JsonLogicFunctionsTests` (each a theory: operation, operands as JSON, expected as JSON):
- `Text`: `size ["héllo"]` 5; `size ["👩‍⚕️"]` 3; `size [[1,2,3]]` 3; `size [""]` 0; `blank [""]` true; `blank ["  \t  "]` true; `blank ["a"]` false; `words ["  chest   pain\nworse "]` 3; `words [""]` 0; `lines [""]` 0; `lines ["a"]` 1; `lines ["a\n"]` 1; `lines ["a\nb"]` 2; `lines ["a\r\nb\rc"]` 3; `lines ["\n"]` 1; `lines ["\n\n"]` 2; `lower ["LETROZOLE Ω İ"]` (`"LETROZOLE Ω İ".ToLowerInvariant()`, computed in the test); `trim ["  pain  \n"]` `"pain"`; `starts_with ["Dear Dr Sample","Dear"]` true; `starts_with ["dear","Dear"]` false; `ends_with ["note.","."]` true; `in ["pain","chest pain"]` true; `in ["Pain","chest pain"]` false; `in ["","abc"]` true.
- `Text_IsCountedInCodePoints`: `size ["a😀b"]` 3; `has_word ["𝒳pain","pain"]` false (U+1D4B3 is a letter); `has_word ["😀pain😀","pain"]` true; `trim [" 😀 "]` `"😀"`.
- `HasWord`: (`"painful"`, `pain`) false; (`"Spain"`, `pain`) false; (`"pain."`, `pain`) true; (`"chest pain today"`, `chest pain`) true; (`"pain_scale"`, `pain`) false; (`"pain2"`, `pain`) false; (`"paín pain"`, `pain`) true; (`"painpain pain"`, `pain`) true; (`"Pain"`, `pain`) false.
- `Matches`: (`"started Femara"`, `letrozole|femara`) false; (`"started femara"`, same) true.
- `Numbers`: `+ [1,2,3]` 6; `+ [0.1,0.2]` 0.30000000000000004; `- [5]` -5; `- [5,3]` 2; `* [2,3,4]` 24; `/ [1,4]` 0.25; `/ [1,0]` null; `% [7,3]` 1; `% [-7,3]` -1; `% [1,0]` null; `min [3,1,2]` 1; `max [3]` 3; `* [1e308,10]` null; `< [1,2]` true; `<= [2,2]` true; `> [1,2]` false; `in [2,[1,2.0]]` true; `in ["a",["b"]]` false; `cat ["a",1.50,"b"]` `"a1.5b"`; `cat ["n=",1e21]` `"n=1e+21"`.
- `Dates`: `days_between ["2026-01-01","2026-01-31"]` 30; reversed -30; across `2024-02-28`→`2024-03-01` 2; `years_between ["2008-02-29","2009-02-28"]` 0; `…,"2009-03-01"` 1; `["2008-02-29","2012-02-28"]` 3; `["2008-02-29","2012-02-29"]` 4; `["1960-06-15","2026-06-14"]` 65; `…"2026-06-15"` 66; `["2026-06-15","1960-06-15"]` -66; `["2026-06-14","1960-06-15"]` -65; `add_days ["2026-12-31",1]` `"2027-01-01"`; `["2024-02-28",2]` `"2024-03-01"`; `["2026-01-01",-1]` `"2025-12-31"`; `["2026-01-01",1.5]` null; `["9999-12-31",1]` null; `["0001-01-01",-1]` null; `< ["2026-01-02","2026-01-10"]` true; `>= ["2026-01-02","2026-01-02"]` true.
- `NullOperands_FollowEachRow` (one row per operation of §§ 5.1–5.5): `< [null,1]` false (and `>`, `<=`, `>=`, and `[1,null]`); `+ [1,null]`, `* [null,2]`, `- [null]`, `- [1,null]`, `/ [null,1]`, `% [1,null]`, `min [1,null]`, `max [null]` null; `in [null,[1,null]]` true; `in [null,[1]]` false; `in [1,null]` false; `in [null,"abc"]` false; `in ["a",null]` false; `cat ["a",null,"b"]` `"ab"`; `size`, `words`, `lines`, `lower`, `trim`, `plain` of `[null]` null; `blank [null]` true; `starts_with [null,"a"]`, `["a",null]`, `ends_with` likewise, `matches [null,"a"]`, `has_word [null,"a"]` false; `days_between [null,"2026-01-01"]`, `years_between ["2026-01-01",null]`, `add_days [null,1]`, `add_days ["2026-01-01",null]` null.
- `OperandsOfTheWrongKind_ReadAsNull`: `+ ["3",1]` null; `lower [5]` null; `size [{"a":1}]` null; `size [5]` null; `< [5,"a"]` false; `< ["a","b"]` false; `< ["2026-02-30","2026-03-01"]` false; `days_between ["2026-2-3","2026-02-04"]` null; `years_between ["2026-02-30","2027-01-01"]` null; `cat ["a",true,{"x":1}]` `"a"`; `blank [5]` true; `starts_with [5,"5"]` false; `add_days ["2026-01-01","1"]` null.
- `TermsSubset`: with fictional concepts (`hot flushes` id `1`, `joint pain` id `2`, an inactive one, one with a null id): every codable `of` id in `in` → `{"pass":true,"message":null,"uncovered":null,"untested":null,"passed":null,"total":null}`; one missing → `pass` false, `message` the `failWith`, `uncovered` `["joint pain"]`; uncodable terms from both lists, distinct, in `untested`; a term repeated in `of` is listed once; `of` null → passes; `in` null → every codable `of` term uncovered; `of` a number → passes. Member order is asserted on `ToJsonString()`.
- `AllPass_AndCountPass`: a list of two passing `check_result`s → `pass` true, `passed` 2, `total` 2, `message` null, `uncovered` and `untested` null; one failing → `pass` false, `message` the `failWith`, `passed` 1, `total` 2; `[]` and `null` → `all_pass` fails with `total` 0; `.items` elements (`{"state":"completed","value":{…pass true…}}`, `{"state":"skipped","value":null}`, `{"state":"not_run","value":null}`) → `passed` 1, `total` 1, passes; an element `5`, and one whose value has no `pass` → not judged; `count_pass` with `min` 0 on `null` passes; `min` 2 with one pass fails; `min` 1 with one pass passes.
- `Matches_TextOverTheLimit_Fails`: `Apply("matches", [<1,048,577 bytes>, "a"])` throws `JsonLogicFailureException` with `MatchesTextTooLong` and `Detail` `1.1`; exactly 1,048,576 bytes does not throw.
- `Plain_ConvertsOnlyWhenTold`: `Apply("plain", ["**a**"])` is `"a"`; with `plainConverts: false` it is `"**a**"`.
- `Read_AndMissing`: on `{"input":{"age":{"value":null},"name":{"value":""},"kind":{"value":"table"}},"list":[1]}`: `Read` of `input.kind.value` is `"table"`; of `input.nope.value`, `input.kind.value.x` and `list.0` it is `null`; of `input` it is the object; `Missing(["input.age.value","input.name.value","input.kind.value","input.nope.value"])` is the first, second and fourth.
- `Truthy`: `true`, `[1]` true; `false`, `null`, `[]`, `0`, `""` false.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement `PlainConversion` and `JsonLogicFunctions`.** `PlainConversion` holds its own pipeline, `new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`.
- [ ] **Step 4: Run the tests; they pass.** Read each `PlainConversionTests` expectation before freezing it.
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): what each JSON Logic operation computes, with its null rules, and plain`

### Task 5: The evaluator

**Files:**
- Modify: `Directory.Packages.props` (`<PackageVersion Include="Corvus.Text.Json.JsonLogic" Version="5.8.12" />`, with a comment as the validator's has), `src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj` (the reference; its comment gains a sentence)
- Create: `V31/JsonLogicEvaluator.cs`, `tests/V31Logic/JsonLogicEvaluatorTests.cs`

**Interfaces:**
- Consumes: `JsonLogicShape.Check`, `JsonLogicFunctions`, `IRegexp`.
- Produces:
  - `public sealed record JsonLogicValue(JsonNode? Value, JsonLogicFailure? Failure)` — `Failure` null means it ran.
  - `public sealed record JsonLogicDecision(bool Result, JsonLogicFailure? Failure)`
  - `public interface IJsonLogicRule { JsonLogicFailure? Declined { get; } JsonLogicValue Evaluate(JsonNode? data); JsonLogicDecision Decide(JsonNode? data); }`
  - `public interface IJsonLogicEvaluator { string Version { get; } IJsonLogicRule Compile(JsonNode? rule, IReadOnlyList<bool>? plainConverts = null); }`
  - `public sealed class CorvusJsonLogicEvaluator : IJsonLogicEvaluator` — `Version` is `JsonLogicOperations.EvaluatorVersion`. `Compile` checks the written form (D17: a fault gives a rule whose `Declined` is `Declined` with A04-011's detail: `it uses <operation>, which is not an operation of this format` for `UnknownOperation`, `LooseEquality`, `LooseInequality` and `Substr`, else `it is not a rule of this format`), rewrites `object` (D16), and caches the compiled rule by the rule's JSON text as written (`ToJsonString()`, which keeps an `object`'s field order; canonical JSON would sort it away) plus the `plainConverts` list. Safe to share between threads. `data` is one object whose members are the roots.

What was learned of Corvus 5.8.12 by probe, so the implementer need not rediscover it: the public surface is `JsonLogicEvaluator(IReadOnlyDictionary<string, IOperatorCompiler>)` with `Evaluate(in JsonLogicRule, in JsonElement)` and `EvaluateToString(ruleJson, dataJson)` (null for *undefined*), `IOperatorCompiler.Compile(RuleEvaluator[] operands)`, `delegate EvalResult RuleEvaluator(in JsonElement data, JsonWorkspace workspace)`, `EvalResult.FromDouble`, `FromElement`, `AsElement(workspace)`, `IsTruthy()`, and `JsonLogicHelpers.StringToElement`, `BooleanElement`, `NullElement`, `DoubleToElement`. A registered compiler is consulted before the built-in for every name, `var`, `and` and `<` included. Operands arrive as delegates, so one that is not invoked is not evaluated (`and`, `or`). An operand that is an object with several members is compiled as a rule and throws `Unknown JsonLogic operator`. An unknown operator throws `InvalidOperationException` at first evaluation; an overflow throws from inside. `JsonElement.ParseValue` is marked obsolete: parse through `ParsedJsonDocument<JsonElement>` and clone. `var` should walk the Corvus `JsonElement` itself (no conversion of the data per read); other operations may cross to `JsonNode` through JSON text.

- [ ] **Step 1: Write the failing tests** (`JsonLogicEvaluatorTests`; a helper `Eval(ruleJson, dataJson)` returns the value's `ToJsonString()`, `"null"` for null)

- `Pins`: `Directory.Packages.props` pins `Corvus.Text.Json.JsonLogic` at exactly `5.8.12` (no `*`, no range), and the loaded assembly's informational version starts with it.
- `TheSpecsExamples` (theory): § 2.3's rule on `{"item":{"id":"billing"},"input":{"include_billing":{"value":true}}}` → true; with `value` false → false; with `item.id` `"hpi"` → true. § 4.5's on a draft holding `Letrozole` → true. § 5.5's on `date_of_birth` `1960-06-15`, `run.date` `2026-06-15` → true; `2026-06-14`… 65 still → true; born `1962-01-01` → false. § 6.2's pitfall: `{"!":[{">":[{"var":"input.age.value"},65]}]}` with age missing → true; the guarded form → false.
- `Standard` (theory): `var` of a present, a null and an absent path (the last two `null`); `{"===":[{"var":"input.nope.value"},null]}` true; `{"===":[1,1.0]}` true; `{"===":["a","a"]}` true; `{"!==":[null,null]}` false; `{"!":[null]}` true; `{"and":[true,null]}` false; `{"and":[true,true]}` true; `{"or":[null,false]}` false; `{"or":[false,true]}` true; `{"if":[null,1,2]}` 2; `{"if":[false,1,true,2,3]}` 2; `{"missing":["input.age.value","input.name.value"]}` on a null age and `""` name → both paths; `[1,{"var":"item.id"},"s"]` → the list with the read's value; `{"in":["consult",["consult","follow_up"]]}` true.
- `Lazy`: `{"and":[false,{"matches":[{"var":"t"},"a"]}]}` with `t` over 1 MiB → false, no failure; `{"or":[true,…same…]}` → true; `{"if":[true,1,…same…]}` → 1.
- `Object`: `{"object":{"title":{"var":"item.id"},"n":{"+":[1,2]},"inner":{"object":{"ok":true}}}}` on `{"item":{"id":"hpi"}}` → `{"title":"hpi","n":3,"inner":{"ok":true}}`, members in written order; `{"object":{}}` → `{}`.
- `EveryOperationRuns`: for each name in `JsonLogicOperations.All`, a rule using it with literal operands evaluates without failure (one row each; `terms_subset` with two `{"concepts":[]}` reads).
- `Plain_FollowsTheList`: `{"cat":[{"plain":[{"var":"a"}]},"|",{"plain":[{"var":"b"}]}]}` on `a` and `b` both `**x**`: with no list → `x|x`; with `[true, false]` → `x|**x**`.
- `ARuleOutsideTheFormat_IsDeclined_NeverRun` (theory): `{"map":[[1,2],{"var":""}]}`, `{"==":[1,"1"]}`, `{"!!":[1]}`, `{"var":["a",1]}`, `{"<":[1,2,3]}`, `{"if":[true,1]}`, `{}`, `{"a":1,"b":2}`, 80 nested `!` → `Declined` is not null with kind `Declined`; `Evaluate` and `Decide` return that failure. The first three details name the operation.
- `Decide`: `true` → (true, none); `false`, `null`, `[]` → false; `["input.a.value"]` (from `missing`) → true; a rule yielding `5` → failure `NotABoolean`, detail `a number`; `"x"` → `text`; an `object` → `an object`.
- `Matches_FailsOverOneMebibyte_AndNeverQuotesTheText`: `{"matches":[{"var":"t"},"letrozole"]}` with `t` 1,048,577 bytes → `Evaluate` and `Decide` fail with `MatchesTextTooLong`, detail `1.1`; the detail holds no character of `t` but digits and a dot.
- `Evaluate_NeverThrows_OnAnyData`: every rule of `EveryOperationRuns`, rewritten to read its operands from `d.a`, `d.b`, `d.c`, evaluated on each of the data `null`, `5`, `"x"`, `[]`, `{}`, `{"d":null}`, `{"d":{"a":{"x":1},"b":[1],"c":"2026-02-30"}}`, `{"d":{"a":1e308,"b":1e308,"c":true}}` → returns a `JsonLogicValue`, never throws.
- `CompiledOnce`: `Compile` of the same rule text twice returns the same instance; two `object` rules whose fields are written in a different order are two rules, each giving its own member order.
- `SharedBetweenThreads`: one evaluator, 8 threads, each 2,000 evaluations of four rules on its own data; every result is right.
- `NoCorvusDefaultIsReachable`: for every rule of `NullOperands_FollowEachRow` (Task 4) written as a rule on literals, the evaluator's result equals `JsonLogicFunctions.Apply`'s.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Add the pin and the reference; implement the evaluator.** One Corvus `JsonLogicEvaluator` per `CorvusJsonLogicEvaluator`, built with a compiler for every operation of § 5 except `if`, `!`, `===`, `!==`: `var` (the operand is the path literal; walk the data's members; absent → null element), `missing` (the operands are path literals), `and` and `or` (lazy, by `IsTruthy`, giving booleans), `object` (operands alternate name, value), `plain` (its index in depth-first order chooses whether to convert: number the `plain`s while rewriting, as an extra literal operand), and one generic compiler that evaluates its operands, calls `JsonLogicFunctions.Apply` and converts back. `Evaluate` catches `JsonLogicFailureException` (its failure) and every other exception (`Fault`, detail `the evaluator failed`). If `SharedBetweenThreads` fails on Corvus's own cache, serialise first evaluation of a rule with a lock.
- [ ] **Step 4: Run the tests; they pass.** Then `dotnet build Consultologist.sln` (the reference reaches every project that references the library) → 0 errors, no new warnings.
- [ ] **Step 5: Commit and push.** Two commits: `PackageFormat (#1501): Corvus.Text.Json.JsonLogic pinned at 5.8.12`; `PackageFormat (#1501): the evaluator behind IJsonLogicEvaluator, our operations over Corvus's`

### Task 6: The checker: sites, reads and results

**Files:**
- Create: `V31/ExpressionSentences.cs`, `V31/ExpressionScope.cs`, `V31/ExpressionChecker.cs`, `tests/V31Logic/LogicFixture.cs`, `tests/V31Logic/ExpressionSentencesTests.cs`, `tests/V31Logic/ExpressionCheckerReadsTests.cs`
- Modify: `tests/V31Source/V31Contract.cs` (the A04 rows, as the A03 ones are held)

**Interfaces:**
- Consumes: `JsonLogicShape`, `JsonLogicPrinter.Print`, `FormatReads.Read`, `FormatTypes`, `TypeStrings.Print`, `FormatSet`, `SourceRefusal`.
- Produces:
  - `public enum ExpressionSiteKind { NodeIf, FlowIf, While, Until, CheckIf, DeliverableIf, PartIf, MacroIf, SnippetIf, Expression }`
  - `public sealed record ExpressionSite(ExpressionSiteKind Kind, string Subject, string Member, string Path, string? ComputedId, JsonNode? Declared)` — `Subject` is D4's `<site>`; `Member` is `if`, `while`, `until` or `expression`; `Path` the file a refusal names; `ComputedId` and `Declared` (the node's format) are set for `Expression` only.
  - `public enum ScopeHeadKind { Format, Value, Deferred, Unknown, Refused }`
  - `public sealed record ScopeHead(ScopeHeadKind Kind, int Length, JsonNode? Format, bool Nullable, SourceRefusal? Refusal)` — `Format`: the first `Length` segments name a thing read through `.value`, `.content`, `.title` (the rest go to `FormatReads.Read`); `Value`: they are already a value of `Format`, and the rest are its fields; `Deferred`: no type yet (D2); `Unknown`: nothing there (S03-014); `Refused`: the scope's own sentence.
  - `public interface IExpressionScope { ScopeHead Head(IReadOnlyList<string> segments); }`
  - `public sealed record ExpressionType(JsonNode? Format, bool Nullable, IReadOnlyList<string>? Titles = null)` — `Format` null is "not known".
  - `public sealed record ExpressionCheck(IReadOnlyList<SourceRefusal> Refusals, ExpressionType? Type, IReadOnlyList<string> Reads, string Printed, IReadOnlyList<bool> PlainConverts)` — `Type` is null when there is a refusal; `Reads` is `JsonLogicShape.Paths`; `PlainConverts` is D14's list.
  - `public static class ExpressionChecker { ExpressionCheck Check(JsonNode? rule, ExpressionSite site, IExpressionScope scope, FormatSet formats); }` — a `null` rule (a site that is `null`: always) gives no refusal and `Type` `boolean`.
  - `internal static class ExpressionSentences`: one `public static SourceRefusal` method per S03-002 to S03-035, S03-042, S03-043 and A04-001 to A04-010, taking the site and the row's fields; `Shape(ExpressionSite, ShapeFault)` (the fault's sentence: S03-004 to S03-007, S03-010 to S03-013, S03-029 to S03-034, S03-042, S03-043, A04-004 to A04-007, A04-010); and, public, `string RunTime(string subject, string member, JsonLogicFailure failure)` (S03-040, S03-041, A04-011). Tasks 8 and 9 add S03-036 to S03-039 and S03-044.
  - `tests/V31Logic/LogicFixture`: `Rule(string json)`; `Formats()` (the published registry's `FormatSet`, plus package formats a test passes as JSON); `Site(kind, declared: null)` (subject `Node 'gate'`, member by kind, path `nodes/gate.md`; `Expression` gives `Computed node 'total'`); `MapScope` — an `IExpressionScope` over a dictionary from a head (`input.kind`, `node.draft`, `run.date`, `item`) to a `ScopeHead`, `Unknown` for anything else.

This task's part of `Check`, in order: (1) the written form: each `ShapeFault` is its sentence; with any, stop. (2) every `var` and `missing` path, in tree order, typed by `Read` below; a refused read's type is not known. (3) operations: Task 7; until then every operation's type is not known. (4) the result: a condition site (every kind but `Expression`) whose type is known and is not `boolean`, and whose rule is not a `missing` operation (D26) → S03-025 with the type string; `Expression` → Task 7.

`Read(path)`: segments by `.`. Root rules first: `profile` → S03-018 (the path as written); `snippet`, `part`, `macro`, `renderer`, `deliverable` → S03-019; any root but `input`, `node`, `check`, `item`, `pass`, `param`, `run` → S03-014. Site rules: `DeliverableIf` and root `check` → S03-021 (`check.<second segment>`); `DeliverableIf`, root `node` and a segment `content` → S03-020; `MacroIf` and root `param` → S03-022 (`param.<second segment>`). `input` with fewer than three segments → S03-015. Then `scope.Head`: `Refused` → its refusal; `Unknown` → S03-014; `Deferred` → not known; `Format` → `FormatReads.Read(Format, rest)`; `Value` → `FormatReads.Read(Format, ["value", ..rest])`. A `ReadFault`: `TitleOfPlainEnum` → S03-016 (`<path>` without `.title`, `<format>` the enum's type string); `NotEveryMember` → S03-024 (`<path>` up to `.value`, `<field>` the rest, `<format>` the union's type string); every other fault → S03-014. The type is the read's format, nullable when the read or the head is, with its titles.

- [ ] **Step 1: Write the failing tests**

`ExpressionSentencesTests`:
- `EverySentence_IsItsRow`: call each method with placeholder-free arguments and hold the result to `V31Contract.AssertSentence` (the row read as a pattern); each A04 method to this plan's table, added to `V31Contract` as the A03 rows were.
- `Exact` (the sentences whose words a caller sees most, character for character): S03-004 `Node 'gate' uses ==; equality is written === (shown as =).`; S03-010 `Node 'gate' gives < 3 operands; it takes 2.`; S03-006 `Node 'gate' uses map, which is not a JSON Logic operation of this format (var, missing, if, ===, !==, <, <=, >, >=, and, or, !, in, cat, +, -, *, /, %, min, max, size, blank, words, lines, lower, trim, starts_with, ends_with, plain, object, matches, has_word, terms_subset, all_pass, count_pass, days_between, years_between, add_days).`; S03-031 `Node 'gate' pattern "\d" is not I-Regexp (RFC 9485): '\d' at character 1 is not an escape of I-Regexp.`; S03-029 `Node 'gate' gives matches a computed pattern; pattern must be written as a literal.`
- `RunTime`: `RunTime("Node 'gate'", "if", MatchesTextTooLong "1.1")` → `Node 'gate' if could not be decided: matches was given 1.1 MiB of text; the most is 1 MiB.`; `NotABoolean "a number"` → `Node 'gate' if yields a number; a condition yields a boolean.`; `Declined "it uses map, which is not an operation of this format"` → `Node 'gate' if could not be evaluated: it uses map, which is not an operation of this format.`

`ExpressionCheckerReadsTests` (scope: `input.kind` an enum format `{"type":"string","enum":["narrative","table"]}`; `input.specialty` a labelled `oneOf` (`med_onc` / `Medical oncology`, `rad_onc` / `Radiation oncology`); `input.consult_draft` `plaintext`; `input.age` `number`; `input.choice` an object whose property `pick` is `anyOf` of two objects, only one declaring `dose`; `node.draft` `Deferred`; `run.date` a `Value` of `date`):
- `AShapeFault_IsItsSentence_AndStopsTheCheck`: `{"==":[{"var":"input.nope.value"},1]}` → one refusal, S03-004; `Printed` is `==(input.nope.value, 1)`.
- `Reads` (theory: rule, id, sentence): `{"var":"input.nope.value"}` at `Expression` → S03-014 `Computed node 'total' reads input.nope.value, which doesn't exist here.`; `{"var":"input.kind"}` → S03-015 `… reads input.kind; an input is read as input.kind.value, .content or .title.`; `{"var":"input.kind.title"}` → S03-016 `… reads input.kind.title, but string {narrative, table} is a plain enum with no titles; read .value, or give the format oneOf with const and title.`; `{"var":"input.choice.value.pick.dose"}` → S03-024; `{"var":"profile.name"}` → S03-018 `… reads profile.name; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions.`; `{"var":"snippet.standards"}` → S03-019; `{"var":"wibble.x"}` → S03-014; `{"var":"input.age.value.x"}` → S03-014; `{"var":"input.age.content"}` → S03-014; `{"var":"run.weather"}` → S03-014; `{"missing":["input.nope.value"]}` → S03-014.
- `SiteRules` (theory): `DeliverableIf` with `{"===":[{"var":"node.draft.content"},"x"]}` → exactly S03-020 `Deliverable 'note' if reads node.draft.content; a deliverable's if reads data (.value), not text.`; `DeliverableIf` with `{"var":"check.coverage.value.pass"}` → S03-021 `Deliverable 'note' if reads check.coverage; a deliverable's existence can't depend on a check.`; `MacroIf` with `{"var":"param.standard.value"}` → S03-022; the same three rules at `NodeIf` give none of the three.
- `TypedReads`: `{"var":"input.age.value"}` at `Expression` (declared not judged yet) → `Type.Format` prints `number`; `{"var":"run.date"}` → `date`; `{"var":"input.specialty.title"}` → `string` with the two titles; `{"var":"node.draft.value"}` → `Type.Format` null, no refusal; a `Refused` head's refusal is passed through as it is.
- `AConditionYieldsABoolean`: `{"var":"input.age.value"}` at `NodeIf` → S03-025 `Node 'gate' if yields number; a condition yields a boolean.`; at `While` the member is `while`; `{"missing":["input.age.value"]}` → none; `{"var":"node.draft.value"}` (deferred) → none; `null` → none.
- `Reads_AndPrinted_AreReported`: for § 2.3's rule, `Reads` is `["item.id", "input.include_billing.value"]` and `Printed` its printed text.
- `AnyRule_NeverThrows`: Task 2's mutants, each checked at `NodeIf` and at `Expression`, return.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement** the three files and the fixture.
- [ ] **Step 4: Run the tests; they pass.**
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): expression sites, the scope a rule is read in, and its reads checked`

### Task 7: The checker: every operation's types

**Files:**
- Create: `V31/ExpressionChecker.Operations.cs`, `tests/V31Logic/ExpressionCheckerTypesTests.cs`
- Modify: `V31/ExpressionChecker.cs` (make it `partial`; step 3 and the `Expression` result)

**Interfaces:**
- Consumes: Task 6; `FormatTypes.Shape`, `Fits`, `Same`, `ListOf`, `TextKind`, `IsNullable`; `FormatSet.Resolve`.
- Produces: nothing new in signature. `ExpressionCheck.Type` is now known for every operation; `PlainConverts` is filled.

An operand's **family** is its `FormatShape` with `null` set aside: *text* (`Text`), *string* (`String`, `Enum`), *number* (`Number`, `Integer`), *boolean*, *date*, *list*, *object*, *union*. *Text-like* is text or string. A string literal is a string, and counts as a date when it is a valid one. A type that is not known, and a `null` literal, satisfy every operand rule. A refused operand's type is not known, so one mistake gives one sentence. `<x>` below is the operand as D4 shows it.

| Operation | Operands | Refusal | Result |
|---|---|---|---|
| a literal | | | string → `string`; whole number → `integer`; other number → `number`; boolean → `boolean`; `null` → not known, nullable |
| an array | one type, by `if`'s rule | A04-003 (`a list`) | `list<T>`; empty → not known |
| `!` | boolean | S03-009 | `boolean` |
| `and`, `or` | booleans | A04-001 (`booleans`) | `boolean` |
| `if` | conditions: boolean, or a `missing` | A04-001 (`a condition`) | the branches' one type: all `Same`; whole and other numbers → `number`; a string literal beside an enum → the enum (S03-023 when it is no value), beside a date → `date`, beside text → the text format; otherwise A04-003 (`the branches of an if`). Nullable when a branch is |
| `===`, `!==` | two of one family among string, number, boolean, date | text on either side → S03-002 (the other a literal) or S03-003; a list, object or union, or two families → A04-002; a string literal against an enum that is no value of it → S03-023 (`<values>` joined `, `); against a `.title` read that is no title of it → S03-017 | `boolean` |
| `<`, `<=`, `>`, `>=` | two numbers, or two dates | S03-008, on the first operand that is neither (or, for a number against a date, the second) | `boolean` |
| `+`, `-`, `*`, `/`, `%`, `min`, `max` | numbers | A04-001 (`numbers`) | `number` |
| `in` | second a list: the needle of its element's family (each string literal of a literal list, and a literal needle, held to an enum's values: S03-023); second text-like: a string needle | A04-001: a wrong second → `a list of its type, or text`; a wrong needle → `a list of its type, or text` for a list, `a string to find` for text | `boolean` |
| `cat` | string, number or `plaintext` | Markdown → S03-035; else A04-001 (`strings, numbers and plain text`) | `string` when all are string; else `plaintext` |
| `size` | text-like or a list | A04-001 (`text or a list`) | `integer` |
| `blank` | text-like | A04-001 (`text`) | `boolean` |
| `words`, `lines` | text-like | A04-001 (`text`) | `integer` |
| `lower`, `trim` | text-like | A04-001 (`text`) | the operand's format as written (an enum → `string`); nullable as the operand |
| `starts_with`, `ends_with` | both text-like | A04-001 (`text`) | `boolean` |
| `plain` | text-like | A04-001 (`text`) | `plaintext`; `PlainConverts` gets `true` for Markdown, `true` when not known, else `false` |
| `matches`, `has_word` | first text-like | A04-001 (`text`) | `boolean` |
| `terms_subset` | both `Fits` `concept_list` | A04-001 (`two concept_list values`) | `check_result` |
| `all_pass`, `count_pass` | a list whose element `Fits` `check_result`, or is an object with properties `state` and `value` where `value`, `null` set aside, `Fits` `check_result` | A04-001 (`a list of check results`) | `check_result` |
| `days_between`, `years_between` | two dates | A04-001 (`dates`) | `integer` |
| `add_days` | a date, a number | A04-001 (`a date and a number`) | `date` |
| `missing` | paths that exist (Task 6) | | `list<string>` |
| `object` | where a format is expected (D27): fields against the target's properties | not an object target, or none → A04-008 when there is no target, S03-026 at the top of an `Expression` whose format is no object; a property with no field → S03-027 (each); a field with no property → S03-028 (each); a field whose type does not fit, or is nullable where the property is not → A04-009 | the target format |

The `Expression` result: the type is known and does not fit `site.Declared` (`FormatTypes.Fits`, with a string literal's extra fits, and D25's nullability) → S03-026, `yields` the result's type string, `declares` the declared one. The target format flows down from `site.Declared` through `if` branches, an array's elements (the target's `items`), and `object` fields (each property).

- [ ] **Step 1: Write the failing tests** (`ExpressionCheckerTypesTests`; Task 6's scope, plus `input.note_type` an enum `consult` / `follow_up`, `input.include_billing` `boolean`, `input.prior_notes` a list of `plaintext`, `input.date_of_birth` `date`, `input.summary` `markdown_block`, `input.flags` a list of `string`, `node.extract_input_terms` and `node.extract_note_terms` as `Format` heads of `concept_list`, `check.coverage` a `Value` head whose format is an object with `items` a list of the `.items` element and `value` a list of `check_result`)

- `TheSpecsExamples_TypeCheck`: § 2.3's (with `item` deferred), § 4.5's (`node.draft` deferred), § 5.4's at `Expression` declared `check_result`, § 5.5's, § 6.2's two → no refusal; types `boolean`, `boolean`, `check_result`, `boolean`, `boolean`.
- `Refusals` (theory: site kind, rule, id, sentence; all at subject `Node 'gate'` unless `Expression`):
  - `{"===":[{"var":"input.consult_draft.content"},"x"]}` → S03-002 `Node 'gate' compares input.consult_draft.content (plaintext) with a literal; text is tested by whether it is blank, its size, or in.`
  - `{"===":[{"var":"input.consult_draft.content"},{"var":"input.summary.content"}]}` → S03-003 `… compares input.consult_draft.content (plaintext) with input.summary.content (markdown_block); text is tested …`
  - `{"<":[{"var":"input.note_type.value"},3]}` → S03-008 `Node 'gate' orders input.note_type.value (string {consult, follow_up}); <, >, ≤ and ≥ compare numbers or dates.`
  - `{"!":[{"var":"input.consult_draft.content"}]}` → S03-009 `Node 'gate' tests input.consult_draft.content (plaintext) with not; not takes a boolean — test text with blank.`
  - `{"===":[{"var":"input.note_type.value"},"consultation"]}` → S03-023 `Node 'gate' compares input.note_type.value with "consultation", which is not one of its values (consult, follow_up).`
  - `{"in":[{"var":"input.note_type.value"},["consult","followup"]]}` → S03-023 naming `"followup"`
  - `{"===":[{"var":"input.specialty.title"},"Oncology"]}` → S03-017 `Node 'gate' compares input.specialty.title with "Oncology", which is not one of its titles (Medical oncology, Radiation oncology).`
  - `{"cat":["Summary: ",{"var":"input.summary.content"}]}` at `Expression` declared `plaintext` → S03-035 `Computed node 'total' cats input.summary.content (markdown_block); convert Markdown with plain first.`
  - `{"+":[{"var":"input.note_type.value"},1]}` → A04-001 `Node 'gate' gives + input.note_type.value (string {consult, follow_up}); + takes numbers.`
  - `{"===":[{"var":"input.age.value"},"65"]}` → A04-002 `Node 'gate' compares input.age.value (number) with "65" (string); = and ≠ compare two values of one type.`
  - `{"===":[{"var":"input.prior_notes.value"},null]}` → none (a `null` test); `{"===":[{"var":"input.prior_notes.value"},{"var":"input.flags.value"}]}` → A04-002
  - `{"if":[{"var":"input.include_billing.value"},1,"one"]}` at `Expression` → A04-003 `Computed node 'total' mixes integer and string in the branches of an if; they have one type.`
  - `[1,"a"]` at `Expression` → A04-003 `… in a list; …`
  - `{"===":[{"object":{"a":1}},null]}` → A04-008
  - `{"and":[{"var":"input.age.value"},true]}` → A04-001 (`booleans`); `{"size":[{"var":"input.age.value"}]}` → A04-001 (`text or a list`); `{"days_between":[{"var":"input.age.value"},{"var":"run.date"}]}` → A04-001 (`dates`); `{"all_pass":[{"var":"input.flags.value"},"Not all passed."]}` → A04-001 (`a list of check results`); `{"terms_subset":[{"var":"input.flags.value"},{"var":"node.extract_note_terms.value"},"x"]}` → A04-001; `{"in":[{"var":"input.age.value"},{"var":"input.consult_draft.content"}]}` → A04-001 (`a string to find`); `{"in":["a",{"var":"input.age.value"}]}` → A04-001 (`a list of its type, or text`).
- `Accepted` (theory; no refusal, the type): `{"===":[{"var":"input.note_type.value"},"consult"]}` boolean; `{"in":[{"var":"input.note_type.value"},["consult","follow_up"]]}`; `{"===":[{"var":"input.date_of_birth.value"},"1960-06-15"]}`; `{"<":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]}`; `{"<":[{"var":"input.age.value"},null]}`; `{"blank":[{"var":"input.consult_draft.content"}]}`; `{"in":["letrozole",{"lower":[{"var":"input.consult_draft.content"}]}]}`; `{"in":["letrozole",{"plain":[{"var":"input.summary.content"}]}]}`; `{"all_pass":[{"var":"check.coverage.items"},"x"]}` and `…coverage.value…` at `Expression` declared `check_result`; `{"size":[{"var":"input.prior_notes.value"}]}` declared `number` (an `integer` fits); `{"trim":[{"var":"input.summary.content"}]}` declared `markdown_block`; `{"cat":["a","b"]}` declared `string`; `{"cat":["a",1]}` declared `plaintext`; `"Yes"` declared `plaintext`; `{"if":[{"var":"input.include_billing.value"},"consult","follow_up"]}` declared the `note_type` enum's schema; `{"add_days":[{"var":"run.date"},7]}` declared `date`; an operation over deferred reads (`{"+":[{"var":"node.count.value"},1]}`) declared `number`.
- `AComputedNodeYieldsWhatItDeclares`: `{"size":[{"var":"input.prior_notes.value"}]}` declared `boolean` → S03-026 `Computed node 'total' yields integer; it declares boolean.`; `{"/":[1,2]}` declared `{"type":"integer"}` → S03-026 (`number`, `integer`); `{"if":[{"var":"input.include_billing.value"},1,null]}` declared `number` → S03-026 (`integer | null`, `number`), declared `{"type":["number","null"]}` → none; `{"var":"input.note_type.value"}` declared `string` → whatever `FormatTypes.Fits(enum, "string")` gives (the test computes it and holds the checker to it).
- `Object` (declared a package format `summary_row` = `{ title: string, count: number, note: plaintext | null }`): the full `object` → none; without `count` → S03-027 `Computed node 'total' builds summary_row without count; object must give every property of the format.`; with `extra` → S03-028 `… builds summary_row with extra, which the format doesn't declare.`; `count` given `"three"` → A04-009 `Computed node 'total' builds summary_row with count as string; the format declares it number.`; `note` given `null` → none; `title` given `null` → A04-009 (`with title as null; the format declares it string`); an `object` declared `number` → S03-026 (`yields object`); a list of `object`s declared `{"type":"array","items":{"$ref":"summary_row"}}` → none, and a bad field in the second element → its sentence.
- `PlainConverts_FollowsTheOperands`: `{"cat":[{"plain":[{"var":"input.summary.content"}]},{"plain":[{"var":"input.consult_draft.content"}]},{"plain":[{"var":"node.draft.content"}]}]}` → `[true, false, true]`.
- `OneMistake_OneSentence`: `{"and":[{"+":[{"var":"input.nope.value"},1]},true]}` → exactly S03-014; `{"!":[{"+":[{"var":"input.note_type.value"},1]}]}` → exactly A04-001.
- `AnyMutation_IsRefusedOrTyped_NeverThrows`: Task 2's mutants of the `Accepted` rules, at `NodeIf` and at `Expression` with each of the declared formats above: `Check` returns; `Type` is null exactly when `Refusals` is not empty.

- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement the table** as one method per operation group over a private operand record (the type, the rule, whether it is a `var`, a literal, a `missing`).
- [ ] **Step 4: Run the tests; they pass.** Where a test computes its expectation from `FormatTypes.Fits`, its summary says so.
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): every JSON Logic operation typed against the formats, and a computed node held to its format`

### Task 8: Every site of a package

**Files:**
- Create: `V31/SourceExpressions.cs`, `tests/V31Logic/SourceExpressionsTests.cs`
- Modify: `V31/SourceCheck.cs`, `V31/ExpressionSentences.cs` (S03-037, S03-038), the tests of `SourceCheckResult` that construct it

**Interfaces:**
- Consumes: `ExpressionChecker.Check`, `SourcePackage` and its nodes, `PackageTypes` (`Formats`, `FunctionFormats`, `NodeFormats`).
- Produces:
  - `public sealed record SiteContext(ExpressionSite Site, object Declaration, SourceFunction? Function, string? PartId)` — `Declaration` is the `SourceNode`, `SourceText` (a macro, a snippet, a macro folder's item), `SourceTextFolder` or `SourceDeliverable` the site is on.
  - `public interface IExpressionScopes { IExpressionScope For(SiteContext context); }`
  - `public sealed class PackageScope(SourcePackage package, bool inFunction) : IExpressionScope` — D2: outside a function, `input.<id>` is a `Format` head of length 2 with the input's `format` (nullable when its `required` is not `true`; an unknown id is `Unknown`), `run.<fact>` a `Value` head of length 2 (`date` → `date`, the other three → `string`; another fact, or `run` alone, `Unknown`); every other root, and every root inside a function, `Deferred`.
  - `public sealed record SiteExpression(SiteContext Context, JsonNode Rule, ExpressionCheck Check)`
  - `public sealed record PackageExpressions(IReadOnlyList<SiteExpression> Sites, IReadOnlyList<SourceRefusal> Warnings)`
  - `public sealed record ExpressionCheckResult(IReadOnlyList<SourceRefusal> Refusals, PackageExpressions? Expressions)` — `Expressions` null exactly when there is a refusal; refusals ordered by path (ordinal), then as found, said once each.
  - `public static class SourceExpressions { ExpressionCheckResult Check(SourcePackage package, PackageTypes types, IExpressionScopes? scopes = null); }`
  - `SourceCheckResult` gains a fourth member, `PackageExpressions? Expressions`; `SourceCheck.Run` calls `SourceExpressions.Check` when the type rules gave no refusal, and its `Refusals` are then the expressions'. `Types` stays set when only an expression is refused.

The sites, in the order `SourceScopes.CheckNodeExpressions` visits nodes (`nodes/`, `checks/`, each function; a flow node before its children): a leaf's `if` (`NodeIf`, subject `Node`; under `checks/` `CheckIf`, subject `Check`); a computed leaf's `expression` (`Expression`, `Computed node`, `Declared` its `format`); each of an agent's `parts.<id>.if` (`PartIf`, `Part '<p>' of node '<id>'`), in written order; a `SourceIf`'s `if` (`FlowIf`, `If`); a `SourceWhile`'s `while` (`While`) and `until` (`Until`), subject `While`; a `SourceFor`'s `until` (`Until`, `Loop`). Then the definitions of the package and of each function: a single snippet's `if` (`SnippetIf`), a single macro's, a macro folder's and each of its items' (`MacroIf`). Then every deliverable's `if` (`DeliverableIf`). A site whose member is `null` or absent is skipped. A site in a function has ` in function '<f>'` after its subject, is checked against that function's `FormatSet`, and is asked for through `PackageScope(package, inFunction: true)`.

Warnings (§ 11.2), for a site with no refusal that reads `run.<fact>` itself: a condition → S03-037 `<site> <member> reads run.<fact>; its decision can differ between runs with the same inputs.`, once per fact; an `Expression` → S03-038 `Computed node '<id>' reads run.<fact>, which is run-dependent; its value can differ between runs with the same inputs.`

- [ ] **Step 1: Write the failing tests** (`SourceExpressionsTests`, on `SourceFixture.Minimal()` and `Full()` edited with `WithJson`)

- `TheFullFixture_HasNoRefusal_AndEverySiteIsListed`: `SourceCheck.Run(Full())` has no refusal; `Expressions.Sites` holds, among others, `Computed node 'section_count'` (`expression`), `While 'refine'` (`while`), `If 'billing_gate'` (`if`); the `billing_gate` site's `Check.Printed` is `input.include_billing.value = true`.
- `EachKindOfSite_IsChecked` (theory: the file edited, the member set to `{"==":[1,1]}`, the expected subject): `nodes/plan.md` `if` → `Node 'plan'`; a part's `if` on `plan` → `Part 'tone' of node 'plan'`; `nodes/section_count.md` `expression` → `Computed node 'section_count'`; `nodes/billing_gate/index.json` `if` → `If 'billing_gate'`; `nodes/refine/index.json` `while` and `until` → `While 'refine'`; a sequential loop's `until` → `Loop '<id>'`; a check's `if` → `Check '<id>'`; a macro's, a macro folder's and its item's `if` → `Macro '…'`; a snippet's `if` → `Snippet '…'`; a deliverable's `if` → `Deliverable 'note'`; a node in a function → `Node '<id>' in function 'draft_section'`. Each gives exactly one refusal, S03-004, on that file's path.
- `InputsAndRunFacts_AreTyped`: `plan`'s `if` set to `{"===":[{"var":"input.consult_draft.content"},"x"]}` → S03-002; to `{"var":"input.nope.value"}` → S03-014; to `{"===":[{"var":"run.date"},"2026-11-20"]}` → none; to `{"var":"run.weather"}` → S03-014.
- `OtherRoots_AreDeferred`: `plan`'s `if` set to `{"===":[{"var":"node.anything.value.x"},{"var":"item.value.kind"}]}` → none.
- `InsideAFunction_InputsAreDeferred`: a function node's `if` set to `{"var":"input.nope.value"}` → none (area 05 says S04-050); set to `{"var":"profile.name"}` → S03-018.
- `AComputedNode_IsHeldToItsFormat`: `section_count`'s `expression` set to `{"blank":[{"var":"input.consult_draft.content"}]}` → S03-026 `Computed node 'section_count' yields boolean; it declares number.`
- `RunFactReads_Warn`: `plan`'s `if` set to `{">=":[{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]},65]}` (with a `date` input added) → no refusal; `Warnings` is exactly S03-037 `Node 'plan' if reads run.date; its decision can differ between runs with the same inputs.`; a computed node reading `run.id` → S03-038 `Computed node 'stamp' reads run.id, which is run-dependent; its value can differ between runs with the same inputs.`
- `AScopeFromOutside_IsUsed`: an `IExpressionScopes` whose scope answers `Unknown` for `node.judge` → `refine`'s `while` gives S03-014.
- `TypesRefused_ExpressionsNotRun`: a package with a format refusal and a bad expression gives only the format's refusal.
- `EveryCaseOfAreas02And03_StillSaysWhatItClaims`: run `RefusalPackagesTests.EveryCase_SaysWhatItClaims` (it already runs through `SourceCheck.Run`); it passes unchanged. If a valid package of an earlier area is refused by an expression rule, correct that package in `RefusalPackages*.cs` so its expression is valid, and list it in the PR.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement `SourceExpressions`, `PackageScope` and the third step of `SourceCheck.Run`.**
- [ ] **Step 4: Run every V31 test; they pass.** `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"`.
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): every expression site of a package source checked, as the third step of SourceCheck`

### Task 9: The contagious flags

**Files:**
- Create: `V31/LineageFlags.cs`, `tests/V31Logic/LineageFlagsTests.cs`
- Modify: `V31/ExpressionSentences.cs` (S03-036, S03-039, S03-044)

**Interfaces:**
- Produces:
  - `public enum LineageFlag { ModelDerived, RunDependent, ProfileCarrying }`
  - `public sealed record LineageRead(string Path, string? Key)` — `Path` as written (`run.date`, `node.draft.content`); `Key` the node read, null when the read is of no node.
  - `public sealed record LineageNode(string Key, bool StartsModelDerived, bool StartsProfileCarrying, IReadOnlyList<LineageRead> ContentReads)` — `ContentReads` are what the value is made from (§ 4.4 "How far it spreads"): never a condition's reads, never a read of an earlier pass.
  - `public sealed record LineageValue(bool Value, string? Because)` — `Because`: the first content read, in written order, that makes it true; null when the node starts it itself, or when it is false.
  - `public static class LineageFlags { IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> Derive(IReadOnlyList<LineageNode> nodes); IReadOnlyDictionary<LineageFlag, LineageValue> OfReads(IReadOnlyList<LineageRead> reads, IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> derived); SourceRefusal? Disagreement(string nodeId, LineageFlag flag, bool declared, LineageValue derived, string path); string Member(LineageFlag flag); }` — a node is run-dependent when a content read's path has the root `run`, or reads a run-dependent node; model-derived when it starts so, or reads a model-derived node; profile-carrying likewise. `Derive` iterates to a fixed point, so a cycle ends. `OfReads` is a decision's flags (its condition's reads). `Member` is `modelDerived`, `runDependent`, `profileCarrying`.
  - `ExpressionSentences`: `FlagDisagrees` (S03-036: `Node '<id>' declares <member> <value>, but its reads make it <derived>: <the read, or none>.`), `ModelDerivedNote(string nodeId)` (S03-039, a string: `computed from model text: node.<id>`), `ReadsProfileText(site, readPath)` (S03-044).

- [ ] **Step 1: Write the failing tests** (`LineageFlagsTests`)

- `TheSpecsExample`: `draft` (starts model-derived, no reads) and `names_drug` (reads `node.draft.content` → `draft`): `names_drug` is model-derived because `node.draft.content`, not run-dependent, not profile-carrying; `draft` is model-derived with `Because` null.
- `RunDependence_Spreads`: `age` reads `run.date` and `input.date_of_birth.value`; `banner` reads `node.age.value`; `plain` reads only an input → `age` true because `run.date`; `banner` true because `node.age.value`; `plain` false.
- `ADecision_DoesNotPassItOn`: a node with no content reads, whose `if` reads a run-dependent node, is not run-dependent; `OfReads` of that `if`'s reads is run-dependent.
- `ProfileCarrying_StartsInText`: a node that starts profile-carrying, and one that reads it.
- `ACycle_Ends`: `a` reads `b`, `b` reads `a`, `b` also reads `run.id` → both run-dependent; `Derive` returns.
- `AReadOfAnUnknownKey_IsIgnored`: a read whose `Key` names no node changes nothing.
- `Disagreement`: `Disagreement("names_drug", ModelDerived, declared: false, derived (true, "node.draft.content"), "nodes/names_drug.md")` → S03-036 `Node 'names_drug' declares modelDerived false, but its reads make it true: node.draft.content.`; declared `true` against derived false → `… declares runDependent true, but its reads make it false: none.`; agreeing → null. Both are held to `sentences.json`'s row.
- `Notes`: `ModelDerivedNote("draft")` is `computed from model text: node.draft`; `ReadsProfileText` is S03-044's row filled.

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests; they pass.**
- [ ] **Step 5: Commit and push.** `PackageFormat (#1501): the three contagious flags worked out over a graph of reads`

### Task 10: The refusal packages of chapter 3

**Files:**
- Create: `tests/V31Source/RefusalPackages.Logic.cs`
- Modify: `tests/V31Source/RefusalPackages.cs` (`All()` adds `Logic()`), `tests/V31Source/RefusalPackagesTests.cs` (the not-shipped list and its reasons; the coverage test reads `ExpressionSentences` too), `tests/V31Source/RefusalPackagesExport.cs` (its summary names the third step)
- Content repo: `v31/conformance/refusal-packages/**`, `v31/conformance/index.json`, `v31/conformance/README.md`

**Interfaces:**
- Consumes: `SourceCheck.Run`, `RefusalCase`, `SourceFixture`.
- Produces: `private static IEnumerable<RefusalCase> Logic()` in `RefusalPackages`.

One case for every sentence a package reaches through `SourceCheck.Run`: S03-002 to S03-035, S03-042, S03-043 (only if Task 1 found a pattern), and A04-001 to A04-010. As areas 02 and 03's cases are, each side is `Minimal()` plus only what its rule needs (a second input of the needed format, a package format, a computed node, a deliverable's or a macro's `if`), and the invalid side is the valid side with one text replacement, giving exactly its one sentence. The `about` says the rule with its chapter 3 section, what the invalid package does and what the valid one does. Not shipped, each with its reason in `RefusalPackagesTests`: S03-001 (a reason sentence, not a refusal), S03-036 and S03-044 (the flags need the edges, areas 05 and 07; D28), S03-037 to S03-039 (warnings and a note), S03-040, S03-041 and A04-011 (run time).

- [ ] **Step 1: Write the cases, and extend the tests.** `EveryCase_SaysWhatItClaims` already holds each case to its claim; extend the "every sentence has a case" test so that every `SourceRefusal`-returning method of `ExpressionSentences` has a case or is in the not-shipped list.
- [ ] **Step 2: Run `RefusalPackagesTests`; the pinned-suite tests fail** (the suite under `external/` does not hold the new cases), the others pass.
- [ ] **Step 3: Export and publish.** In `~/Projects/consultologist-package-format` on `m39-build` (pull first): `CONFORMANCE_EXPORT_V31=$HOME/Projects/consultologist-package-format dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~RefusalPackagesExport"`. Read the diff: areas 02 and 03's cases unchanged (or changed only as Task 8 corrected them), each new `expect.json` holding its one sentence. Add to `v31/conformance/README.md` a section *The refusal packages of the expressions (area 04)* (which sentences have cases, which have none and why, that reads through `node`, `check`, `item`, `pass` and `param` are not judged until area 05) and the table of this implementation's `A04-…` sentences with their rules, as the A02 and A03 tables are written. Run `python3 scripts/check-v31.py` → every group passes. Commit (`Format spec (Consultologist-Blazor#1501): refusal packages for chapter 3, the expressions`), push, and move `external/consultologist-package-format` in the worktree to that commit.
- [ ] **Step 4: Run every V31 test; they pass.**
- [ ] **Step 5: Commit and push.** `Tests (#1501): a refusal package for every expression sentence a package reaches, pinned`

### Task 11: Goldens and vectors

**Files:**
- Create: `tests/V31Logic/LogicVectors.cs`, `tests/V31Logic/LogicVectorsExport.cs`, `tests/V31Logic/LogicVectorsTests.cs`
- Content repo: `v31/conformance/{operation-goldens,printer-goldens,null-vectors,text-vectors,number-vectors,date-vectors}/*.json`, `v31/conformance/index.json`, `v31/conformance/README.md`, `scripts/check-v31.py`

**Interfaces:**
- Consumes: `CorvusJsonLogicEvaluator`, `JsonLogicPrinter`, `CanonicalJson.Write`.
- Produces:
  - `internal sealed record LogicCase(string About, string Rule, string Data)` (JSON texts; `Data` `"{}"` when the rule reads nothing)
  - `internal sealed record LogicFile(string Family, string Name, string About, IReadOnlyList<LogicCase> Cases)`
  - `internal static class LogicVectors { IReadOnlyList<LogicFile> All(); string Render(LogicFile file); }` — `Render` runs each case and writes the file's bytes.

A file is `{ "about": "<what the group pins>", "cases": [ … ] }`, 2-space indentation, `\n` line ends, a final newline, non-ASCII written as itself (the conventions of `expect.json`). A case is `{ "about", "rule", "data", "result" }` in the four vector families; `{ "about", "rule", "data", "output" }` in `operation-goldens`, `output` being the result's canonical JSON as a string (the bytes); `{ "about", "rule", "text" }` in `printer-goldens`. The index lists a family's cases as its file names. The families and files:

| Family | Files | Holds |
|---|---|---|
| `operation-goldens` | one per custom operation, `<name>.json` (18) | at least four cases each, covering every branch of the operation's meaning in §§ 5.2–5.5; `plain.json` holds Task 4's Markdown inputs |
| `printer-goldens` | `operations.json`, `precedence.json`, `literals.json` | every operation of § 5 printed once; Task 3's precedence and parenthesis rows; every literal form and D23's forms |
| `null-vectors` | `standard.json`, `text.json`, `structural.json`, `dates.json`, `absence.json` | one case for every row of §§ 5.1–5.5 with a `null` operand in each operand position; `absence.json` holds every row of § 6.1's table (an unsupplied input, a node that did not complete, `.state`, a nullable field, `pass.previous` on the first pass) and § 6.4's truthiness |
| `text-vectors` | `lower.json`, `trim.json`, `words.json`, `has_word.json` | Task 4's text rows, with every `White_Space` character for `trim` and `words`, case pairs outside ASCII (`Ω`, `İ`, `ẞ`, `Ǆ`, Cherokee, an astral Deseret letter) for `lower`, and each word-character category beside the word for `has_word` |
| `number-vectors` | `arithmetic.json`, `non-finite.json`, `integers.json` | IEEE results (`0.1 + 0.2`), overflow and division by zero giving `null`, a whole result written without a fraction, `1.0 === 1` |
| `date-vectors` | `days_between.json`, `years_between.json`, `add_days.json`, `comparisons.json` | Task 4's date rows, every 29 February case of § 5.5, the bounds of years 0001 and 9999, malformed dates |

- [ ] **Step 1: Write `LogicVectors.All()`** (the inputs only) and the tests:
  - `EveryOperation_HasAGolden_APrinterRow_AndItsNullVectors`: every custom operation has its `operation-goldens` file; every name of `JsonLogicOperations.All` appears as an operation in `printer-goldens/operations.json` and in some `null-vectors` file.
  - `EveryCase_Runs`: every case's rule has no fault of the written form (`JsonLogicShape.Check`), compiles and evaluates without failure; every printer case round-trips through `TryParse`.
  - `ThePinnedSuite_IsWhatTheCodeGives`: for each file, the bytes under `external/consultologist-package-format/v31/conformance/<family>/<name>.json` equal `Render(file)`; the index's `cases` for each of the six families are exactly the file names, sorted. Fails, never skips, when the folder is missing.
- [ ] **Step 2: Run them; `ThePinnedSuite_IsWhatTheCodeGives` fails** (the suite has no such files), the others pass.
- [ ] **Step 3: Write the exporter and publish.** `LogicVectorsExport.Export` (a no-op without `CONFORMANCE_EXPORT_V31`, as `RefusalPackagesExport` is) deletes and rewrites the six family folders and sets each family's `cases` in `index.json`, leaving every other byte of that file (generalise `RefusalPackagesExport.WithCases` to take the family id). Run it. **Read every exported file**: each `result`, `output` and `text` against the spec's row it pins; a wrong one is a bug in Tasks 3 to 5, fixed there with a test. In the content repo: add to `README.md` a section *The goldens and vectors of JSON Logic (area 04)* (the file and case shapes above, D13, D18 to D21 as what an outside implementation must match, and that `matches`' 1 MiB failure and the run-time sentences have no vector); add to `scripts/check-v31.py` a group `vectors` that holds each listed file of the six families to its shape (`about`, `cases`, and each case's members by family) and each family's folder to the index. Run `python3 scripts/check-v31.py` → every group passes. Commit (`Format spec (Consultologist-Blazor#1501): the goldens and vectors of JSON Logic`), push, and move the pin.
- [ ] **Step 4: Run every V31 test; they pass.**
- [ ] **Step 5: Commit and push.** `Tests (#1501): the operation and printer goldens and the null, text, number and date vectors, generated and pinned`

### Task 12: The guide, and the plan ticked

**Files:**
- Modify: `docs/customizable-workflow/package-format-library.md` (a section *specVersion 31: JSON Logic* after area 03's: each new type in one line, how a caller checks a rule, prints it, compiles and evaluates it, what a scope is and that area 05 supplies it, D14's list, and that the engine does not call any of it yet), this plan (checkboxes; a decision taken during the build added to the table, marked *taken during the build*)

- [ ] **Step 1: Write the section and tick the plan.**
- [ ] **Step 2: Verify.** `dotnet build Consultologist.sln` → 0 errors; `dotnet test tests/Consultologist.Api.Tests.csproj --filter "FullyQualifiedName~V31"` → all pass; `git diff origin/m39-build --stat -- src/Consultologist.Api src/Consultologist.Web .github .claude scripts/m39-build AGENTS.md CLAUDE.md` → empty.
- [ ] **Step 3: Commit and push.** `Docs (#1501): JSON Logic at 31 in the library's guide, and the plan ticked`

## Spec coverage

| Part 03 | Task |
|---|---|
| § 1 the rule; § 2 the written form | 2 |
| § 3 where expressions appear | 6 (sites), 8 (the walk) |
| § 4.1–4.2 roots and per-site rules | 6; visibility of `node`, `check`, `item`, `pass`, `param` is area 05's (D2) |
| § 4.3 run facts | 8 (`PackageScope`); recording them is area 09's |
| § 4.4–4.6 the three flags | 9 (the algorithm); the edges and the header check are area 05's (D28) |
| § 5.1 standard operations | 1 (table), 2 (arity), 4 (meaning), 5 (evaluated), 7 (typed) |
| § 5.2 text operations; § 5.3 `matches`, `has_word` | 1 (`IRegexp`), 2, 4, 5, 7 |
| § 5.4 `object` and the check operations; § 5.5 dates | 2, 4, 5, 7 |
| § 6 values, absence and null; § 6.3 vectors | 4, 5, 11 |
| § 7 typing | 6, 7 |
| § 8 the printer | 3, 11 |
| § 9 the evaluator | 5 |
| § 10 what the record holds | the printed condition, reads and failures are produced here (3, 5, 6); the record's rows are area 09's |
| § 11.1 refusals | 2, 6, 7, 10 |
| § 11.2 warnings | 8 (direct reads), 9 (sentences) |
| § 11.3 run-time failures | 5, 6 (`RunTime`) |
| part 10 § 5 goldens and vectors | 11 |
