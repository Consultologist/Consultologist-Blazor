#!/usr/bin/env dotnet
#:project ../src/Consultologist.Api/Consultologist.Api.csproj
// Same reason as validate-workflow-package.cs: this script is neither trimmed
// nor AOT'd, and the manifest model has no source-generated context.
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Checks a formats catalogue version before it is published (package-format-
// v25-design.md § 3.3; B1 #1000). The agents repo's CI runs it on every pull
// request and again before the publish step:
//
//   dotnet run -v q --file scripts/check-formats-catalog.cs -- <agents-dir>
//
// <agents-dir> is the agents repo's agents/ directory: formats.json, the
// formats/ files and the agent yamls, as they would be published.
//
// It is the ENGINE's reader — WorkflowFormatCatalogIndexJson and
// WorkflowFormatCatalog.Build, the same code that loads the catalogue at
// startup — plus the one rule only the registry can check: that each attested
// entry's agent yaml declares the entry's shape as its response format, at the
// version the index names. One definition, so CI and the engine cannot drift.
//
// Exit 0: the catalogue binds, and every binding is printed. Exit 1: at least
// one sentence (printed). Exit 2: usage, or no formats.json in the directory.

using Consultologist.Api.Agents;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --file scripts/check-formats-catalog.cs -- <agents-dir>");
    return 2;
}

var result = FormatsCatalogCheck.Run(args[0]);

if (result.Catalog is null && result.Errors.Count == 1 && result.Errors[0].StartsWith("No formats.json", StringComparison.Ordinal))
{
    Console.Error.WriteLine("error: " + result.Errors[0]);
    return 2;
}

foreach (var error in result.Errors)
{
    Console.Error.WriteLine("error: " + error);
}

if (!result.IsValid)
{
    Console.Error.WriteLine($"error: the formats catalogue does not bind ({result.Errors.Count} sentence(s)); nothing would be published.");
    return 1;
}

Console.Out.WriteLine($"candidate: {result.Catalog!.ResolvedRef} ({result.Catalog.Formats.Count} formats)");

foreach (var line in FormatsCatalogCheck.Describe(result.Catalog))
{
    Console.Out.WriteLine(line);
}

return 0;
