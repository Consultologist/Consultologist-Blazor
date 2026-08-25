#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// The Functions host publishes trimmed, which turns reflection-based
// System.Text.Json off; this script is neither trimmed nor AOT'd, and the
// record model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property NoWarn=IL2026;IL3050

// Recomputes a job record's hashes and says whether they match (#402).
//
// Usage:
//   dotnet run -v q --file scripts/verify-job-hashes.cs -- <job.json>
//   dotnet run -v q --file scripts/verify-job-hashes.cs -- <job.json> --inputs <inputs.json>
//   dotnet run -v q --file scripts/verify-job-hashes.cs -- <job.json> --draft <draft.txt>
//
// <job.json> is the record as the API serves it:
//   curl -sS -H "Authorization: Bearer $TOKEN" \
//     https://<function-host>/api/ConsultGenerationJobs/<job-id> > job.json
// <inputs.json> is the inputs as they were submitted — {"id": value, ...} in
// the request's own form (text, true/false, numbers, one-level objects and
// arrays) — for a record whose effectiveInputHashVersion is 3, 4 or 5;
// <draft.txt> is the consult draft for version 2.
//
// WHY THIS EXISTS. The other registries let somebody re-run a consult from
// published artifacts. A hash is what lets them check that the re-run
// matches the record — and a verifier who guesses the recipe wrong gets a
// mismatch and cannot tell whether the run differed or their recipe did,
// which is the worst failure a provenance system can have. This runs the
// engine's own functions (ConsultGenerationProvenance), whose agreement with
// the published definitions and worked examples (hash-definitions.md,
// provenance@vYYYY.MM.N) is pinned by tests/ProvenanceVersionSetTests.cs.
//
// What it can and cannot recompute: the deliverable hashes come from the
// record alone (the texts are on it); the input hash needs the inputs, which
// a record does not carry; the per-node hashes cover rendered prompts the
// record does not carry either, and are not checked here.
//
// Read-only: it reads two files and prints. Exit 0 when every recomputed hash
// matches, 1 when any does not, 2 on usage.

using System.Text.Json;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var jobPath = positional.FirstOrDefault();
var inputsPath = Option("--inputs");
var draftPath = Option("--draft");
if (jobPath is null || !File.Exists(jobPath))
{
    Console.Error.WriteLine("usage: verify-job-hashes.cs <job.json> [--inputs <inputs.json>] [--draft <draft.txt>]");
    return 2;
}

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
ConsultGenerationJobResponse record;
try
{
    record = JsonSerializer.Deserialize<ConsultGenerationJobResponse>(File.ReadAllText(jobPath), options)
        ?? throw new JsonException("empty record");
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"error: {jobPath} is not a job record: {ex.Message}");
    return 2;
}

IReadOnlyDictionary<string, ConsultInputValue>? inputs = null;
if (inputsPath != null)
{
    if (!File.Exists(inputsPath)) { Console.Error.WriteLine($"error: {inputsPath} not found"); return 2; }
    try
    {
        inputs = JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(File.ReadAllText(inputsPath), options);
    }
    catch (Exception ex) when (ex is JsonException or ConsultInputShapeException)
    {
        Console.Error.WriteLine($"error: {inputsPath} is not an inputs map: {ex.Message}");
        return 2;
    }
}

string? draft = draftPath != null ? File.ReadAllText(draftPath) : null;
if (draftPath != null && !File.Exists(draftPath)) { Console.Error.WriteLine($"error: {draftPath} not found"); return 2; }

Console.Out.WriteLine($"{record.JobId}: {record.WorkflowPackage ?? "(no package)"}, status {record.Status}, storage version {record.SchemaVersion?.ToString() ?? "—"}");

var checks = ProvenanceRecordCheck.Check(record, inputs, draft);
var mismatches = 0;
foreach (var check in checks)
{
    var definition = check.Definition is int d ? $" (definition {d})" : string.Empty;
    switch (check.Matches)
    {
        case true:
            Console.Out.WriteLine($"  {check.Name}{definition}: matches");
            break;
        case false:
            mismatches++;
            Console.Out.WriteLine($"  {check.Name}{definition}: MISMATCH — recorded {check.Recorded}, recomputed {check.Recomputed}");
            break;
        default:
            Console.Out.WriteLine($"  {check.Name}{definition}: skipped — {check.Note}");
            break;
    }
}

var checkedCount = checks.Count(c => c.Matches != null);
Console.Out.WriteLine($"{checkedCount} hash(es) recomputed, {mismatches} mismatch(es), {checks.Count - checkedCount} skipped");
return mismatches == 0 ? 0 : 1;
