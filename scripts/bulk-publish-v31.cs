#!/usr/bin/env dotnet
#:project ../src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj

// The operator's bulk publish of the accounts' packages at specVersion 31 (#1508, Task 36; part 10 § 1.5).
//
// Usage:
//   ACCESS_TOKEN=<an operator account's bearer token> \
//   dotnet run -v q --file scripts/bulk-publish-v31.cs -- <operator folder> --engine <url> [--publish]
//
// It asks the engine at <url> (its base, as https://<host>, or http:// only for localhost, 127.0.0.1 or [::1], since the
// token is sent to it; nothing else is called) for every account's active account package below 31
// (GET <url>/api/Admin/MigratePackage), then migrates each one there (POST, one package a call): without --publish a dry
// run, which writes nothing in the registry and moves no pin. The operator folder holds, for a package,
// <owner>/<name>/options.json (its migration choices; absent: the defaults) and <owner>/<name>/hand-edits/ (each file
// replaces or adds its path in the migrated package; <path>.deleted removes <path>). Every folder is read before anything
// is sent, and a folder holding options.json or hand-edits/ that names no listed package and no row the record carries
// (a misspelt one would leave its package at the defaults) stops the run (exit 2), each printed, nothing sent.
//
// After every row it writes <operator folder>/bulk-publish-record.json (one row per package: the engine's answer, with the
// options used, and the time) and <operator folder>/owners.json (per owner: the packages published with the notices the
// owner must see; the packages not migrated with the reason, S10-005 and "This package stops working when specVersion 30
// is retired."; the packages unknown, with "The engine did not answer for this package; check whether it was published
// before sending this list."), each through a temporary file moved over it, so an interruption loses at most the package
// in flight. A package the engine did not answer for (a fault, a timeout, a lost answer) is recorded
// "unknown: <fault>" and the run stops (exit 2): in a --publish run it may have been published (a dry run's unknown
// cannot be). Check its version on the engine, then rerun: a package still listed is sent again and its answer replaces
// the row; one at 31 was published, and its row stays unknown (and the run exits 2) until the operator writes "published"
// as its outcome in the record. A package the engine cannot publish keeps its 30 version and its pin. A rerun carries
// forward every row of the existing record whose outcome is "published" or "unknown: …" and whose package it did not
// answer (the latest row of each package), and builds both files from the merged rows. A record it cannot read, or one
// written for another --engine, stops it (exit 2) before anything is sent, neither file written.
// A hand edit is made only when the file its notice names is in hand-edits/ (edited, unchanged, or <path>.deleted).
//
// Exit codes: 0 every package published (or, without --publish, would); 1 a package is not migrated, or an account's
// active package could not be read; 2 usage, a folder that does not read or names no package, a record of another
// engine, an engine fault, or an unknown row this run did not answer (the files then hold every row answered before it).
// The logic is Consultologist.PackageFormat.BulkPublishScript, tested in tests/V31Publish/ScriptsTests.cs.

using Consultologist.PackageFormat;

var (arguments, usage) = BulkPublishScript.Parse(args);
if (arguments is null)
{
    Console.Error.WriteLine($"error: {usage}");
    Console.Error.WriteLine(BulkPublishScript.Usage);
    return BulkPublishScript.Fault;
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
return await BulkPublishScript.RunAsync(
    arguments, http, Environment.GetEnvironmentVariable(BulkPublishScript.TokenVariable), TimeProvider.System, Console.Out, Console.Error);
