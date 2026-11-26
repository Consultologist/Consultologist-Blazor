#!/usr/bin/env dotnet
#:project ../src/Consultologist.PackageFormat/Consultologist.PackageFormat.csproj

// The operator's bulk publish of the accounts' packages at specVersion 31 (#1508, Task 36; part 10 § 1.5).
//
// Usage:
//   ACCESS_TOKEN=<an operator account's bearer token> \
//   dotnet run -v q --file scripts/bulk-publish-v31.cs -- <operator folder> --engine <url> [--publish]
//
// It asks the engine at <url> (its base, as https://<host>; nothing else is called) for every account's active account
// package below 31 (GET <url>/api/Admin/MigratePackage), then migrates each one there (POST, one package a call): without
// --publish a dry run, which writes nothing in the registry and moves no pin. The operator folder holds, for a package,
// <owner>/<name>/options.json (its migration choices; absent: the defaults) and <owner>/<name>/hand-edits/ (each file
// replaces or adds its path in the migrated package; <path>.deleted removes <path>). Every folder is read before anything
// is sent.
//
// It writes <operator folder>/bulk-publish-record.json (one row per package: the engine's answer, with the options used,
// and the time) and <operator folder>/owners.json (per owner: the packages published with the notices the owner must see,
// and the packages not migrated with the reason, S10-005 and "This package stops working when specVersion 30 is retired.").
// A package the engine cannot publish keeps its 30 version and its pin.
//
// Exit codes: 0 every package published (or, without --publish, would); 1 a package is not migrated, or an account's
// active package could not be read; 2 usage, a folder that does not read, or an engine fault (the files then hold what was
// done before it). The logic is Consultologist.PackageFormat.BulkPublishScript, tested in tests/V31Publish/ScriptsTests.cs.

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
