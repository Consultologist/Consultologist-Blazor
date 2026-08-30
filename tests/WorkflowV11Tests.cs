using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v11 step (a) (#563, package-format-v11-design.md § 4–§ 6, § 8): the
/// validator accepts 11 and knows its three declaration shapes — macros with
/// their placeholder grammar, a deliverable's macro list and signature flag,
/// and reproducible on nodes — refusing each below 11 by name. Nothing runs
/// yet.
/// </summary>
public static class V11Fixtures
{
    public static WorkflowPackageManifest Minimal() => WithResultsList(V10Fixtures.Minimal() with { SpecVersion = 11 });

    /// <summary>
    /// The lineage fixtures use the v6 string-result sugar; a results list is
    /// where macros and the signature live, so materialise the one-entry form.
    /// </summary>
    public static WorkflowPackageManifest WithResultsList(WorkflowPackageManifest manifest) =>
        manifest.Results != null ? manifest : manifest with
        {
            Result = null,
            Results = new List<WorkflowResultSpec> { new("consult", manifest.Result!, "Consultation note") }
        };

    /// <summary>A macro over the fixture's declared inputs, named by the deliverable.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithMacro(
        string template = "This disclaimer is fixed text.",
        WorkflowPackageManifest? from = null,
        string macroId = "disclaimer")
    {
        var manifest = from ?? Minimal();
        manifest = manifest with
        {
            Macros = new List<WorkflowMacroSpec> { new(macroId, "Standing disclaimer", $"macros/{macroId}.md") },
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { macroId } } : r).ToList()
        };
        var files = V6Fixtures.Files(manifest);
        files[$"macros/{macroId}.md"] = template;
        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV11GateTests
{
    [Fact]
    public void TheValidatorAccepts11_ButTheStoreDoesNotRunItYet()
    {
        // (a) #563: publishable before runnable, as v8 and v10 shipped.
        Assert.Contains(11, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(11, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
        Assert.Equal(10, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions.Max());

        var result = V11Fixtures.Validate(V11Fixtures.Minimal());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TwelveIsRefused_NamingTheSet()
    {
        Assert.Contains(V11Fixtures.Validate(V11Fixtures.Minimal() with { SpecVersion = 12 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7, 8, 9, 10 or 11"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    public void BelowEleven_TheThreeKeys_AreRefusedByName(int specVersion)
    {
        var manifest = V11Fixtures.WithResultsList(V10Fixtures.Minimal()) with
        {
            SpecVersion = specVersion,
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") }
        };
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<string> { "closing" }, Signature = true }
                : r).ToList(),
            Nodes = manifest.Nodes!.Select((n, i) => i == 0 ? n with { Reproducible = true } : n).ToList()
        };

        var errors = V11Fixtures.Validate(manifest).Errors;

        Assert.Contains("macros requires specVersion 11.", errors);
        Assert.Contains(errors, e => e.EndsWith("declares macros, which requires specVersion 11."));
        Assert.Contains(errors, e => e.EndsWith("declares signature, which requires specVersion 11."));
        Assert.Contains(errors, e => e.EndsWith("declares reproducible, which requires specVersion 11."));
    }

    [Fact]
    public void AV10Manifest_WritesTheBytesItAlwaysWrote()
    {
        // § 7's control: a package using none of v11 is byte-identical.
        var json = WorkflowV10StructureTests.Write(V10Fixtures.Minimal());

        Assert.DoesNotContain("\"macros\"", json);
        Assert.DoesNotContain("\"signature\"", json);
        Assert.DoesNotContain("\"reproducible\"", json);
        Assert.Equal(json, WorkflowV10StructureTests.Write(
            WorkflowPackageManifestJson.Read(json, "v10", WorkflowPackageValidator.AcceptedSpecVersions)));
    }
}

public class WorkflowV11MacroTests
{
    private static IEnumerable<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => V11Fixtures.Validate(bundle).Errors;

    [Fact]
    public void AMacro_Publishes_AndEachSenseResolves()
    {
        // A fixed phrase, a template over the declaration, and the run facts —
        // the three senses under one grammar (§ 4).
        var (manifest, files) = V11Fixtures.WithMacro(
            "Fixed text. Stay: {{input:length_of_stay}}. Guide: {{data:intro}}. On {{run:date}} by {{profile:name}} ({{run:package}}, {{run:job}}, {{run:host}}).");
        manifest = manifest with { Data = new Dictionary<string, string>(manifest.Data!) { ["intro"] = "data/intro.md" } };
        files["data/intro.md"] = "A scalar the macro reads.";

        var result = V11Fixtures.Validate((manifest, files));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Macro"));
    }

    [Fact]
    public void AClassificationPlaceholder_ResolvesAgainstAClassifier()
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var (withMacro, macroFiles) = V11Fixtures.WithMacro(
            "Scope: {{classification:scope}}.", from: V11Fixtures.WithResultsList(manifest with { SpecVersion = 11 }));
        foreach (var (k, v) in files) macroFiles.TryAdd(k, v);

        Assert.DoesNotContain(Errors((withMacro, macroFiles)), e => e.Contains("Macro"));
    }

    [Theory]
    [InlineData("{{input:nope}}", "input:nope")]
    [InlineData("{{data:missing}}", "data:missing")]
    [InlineData("{{classification:assemble}}", "classification:assemble")]
    [InlineData("{{run:time}}", "run:time")]
    [InlineData("{{profile:signature}}", "profile:signature")]
    [InlineData("{{no_namespace}}", "no_namespace")]
    [InlineData("{{sql:drop}}", "sql:drop")]
    public void APlaceholderThatDoesNotResolve_IsRefusedNamingTheToken(string template, string token)
    {
        Assert.Contains($"Macro 'disclaimer' placeholder '{{{{{token}}}}}' does not resolve.",
            Errors(V11Fixtures.WithMacro($"Text {template} text.")));
    }

    [Fact]
    public void AnOptionalInputPlaceholder_Warns_AndStillPublishes()
    {
        var result = V11Fixtures.Validate(V11Fixtures.WithMacro("Stay: {{input:length_of_stay}}."));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains("Macro 'disclaimer' references optional input 'length_of_stay', which renders as empty when not supplied.", result.Warnings);
    }

    [Fact]
    public void TheFileRules_MissingEmptyIdAndLabel()
    {
        var (manifest, files) = V11Fixtures.WithMacro();
        files.Remove("macros/disclaimer.md");
        Assert.Contains("Macro 'disclaimer' file 'macros/disclaimer.md' is missing from the package.", Errors((manifest, files)));

        var (m2, f2) = V11Fixtures.WithMacro("   ");
        Assert.Contains("Macro 'disclaimer' file 'macros/disclaimer.md' is empty.", Errors((m2, f2)));

        var (m3, f3) = V11Fixtures.WithMacro(macroId: "Bad-Id");
        Assert.Contains("Macro id 'Bad-Id' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).", Errors((m3, f3)));

        var (m4, f4) = V11Fixtures.WithMacro();
        m4 = m4 with { Macros = new List<WorkflowMacroSpec> { m4.Macros![0] with { Label = " " } } };
        Assert.Contains("Macro 'disclaimer' has no label.", Errors((m4, f4)));
    }

    [Fact]
    public void TheReferenceRules_OrphanUndeclaredAndDuplicate()
    {
        // An orphan macro.
        var orphan = V11Fixtures.Minimal() with
        {
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") }
        };
        var files = V6Fixtures.Files(orphan);
        files["macros/closing.md"] = "Closing words.";
        Assert.Contains("Macro 'closing' is not referenced by any result.", Errors((orphan, files)));

        // An undeclared reference.
        var undeclared = V11Fixtures.Minimal();
        undeclared = undeclared with
        {
            Results = undeclared.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { "ghost" } } : r).ToList()
        };
        Assert.Contains("Result 'consult' references undeclared macro 'ghost'.", V11Fixtures.Validate(undeclared).Errors);

        // The same macro twice on one deliverable.
        var (twice, twiceFiles) = V11Fixtures.WithMacro();
        twice = twice with
        {
            Results = twice.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { "disclaimer", "disclaimer" } } : r).ToList()
        };
        Assert.Contains("Result 'consult' lists macro 'disclaimer' more than once.", Errors((twice, twiceFiles)));
    }

    [Fact]
    public void ASignedDeliverable_AndAReproducibleNode_Publish()
    {
        var manifest = V11Fixtures.Minimal();
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Signature = true } : r).ToList(),
            Nodes = manifest.Nodes!.Select((n, i) => i == 0 ? n with { Reproducible = true } : n).ToList()
        };

        var result = V11Fixtures.Validate(manifest);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
