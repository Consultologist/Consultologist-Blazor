using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A 25-shaped package (package-format-v25-design.md) and the fixture
/// catalogue of A1's five formats. Validation goes through the A4 seam that
/// accepts 25 for the call; the public entry still refuses 25 until A5.
/// </summary>
public static class V25Package
{
    public static readonly IReadOnlyCollection<int> Accepting25 =
        WorkflowPackageValidator.AcceptedSpecVersions.Append(25).ToList();

    public static WorkflowFormatCatalog Catalog()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formats/prose.json"] = FormatFixtures.Prose,
            ["formats/classification.json"] = FormatFixtures.Classification,
            ["formats/concept-list.json"] = FormatFixtures.ConceptList,
            ["formats/rendered.json"] = FormatFixtures.Rendered,
            ["formats/terms-subset.json"] = FormatFixtures.TermsSubset,
        };
        var index = WorkflowFormatCatalogIndexTests.Read(WorkflowFormatCatalogIndexTests.Example);
        var errors = new List<string>();
        var catalog = WorkflowFormatCatalog.Build(index, path => files.GetValueOrDefault(path), errors);
        Assert.Empty(errors);
        return catalog!;
    }

    /// <summary>
    /// A prose node reading an input, a concept-list node, a classification
    /// taking its values through with, a terms-subset check, a snippet, a
    /// macro with an instruction, one deliverable.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (v24, _) = V24Fixtures.Base();

        var manifest = v24 with
        {
            SpecVersion = 25,
            Preludes = null,
            Data = null,
            Schemas = null,
            SchemaRefs = null,
            CustomSchemas = null,
            Result = null,
            Results = null,
            Inputs = new List<WorkflowInputSpec> { new("notes", "Referral notes") },
            Prompts = new List<WorkflowPromptSpec>
            {
                new("assessment", "prompts/assessment.md", new List<string>()),
                new("extract", "prompts/extract.md", new List<string>()),
                new("scope", "prompts/scope.md", new List<string>()),
            },
            Nodes = new List<WorkflowNodeSpec>
            {
                new("extract", "Extract concepts", Prompt: "extract", Format: "concept-list", OnInvalid: WorkflowOnInvalid.Fail),
                new("scope", "Scope", Prompt: "scope", Format: "classification", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["values"] = new JsonArray("in_scope", "out_of_scope") }),
                new("assessment", "Assessment", Prompt: "assessment", Format: "prose",
                    Macros: new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) }),
                new("coverage", "Coverage", Format: "terms-subset", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["of"] = "node.extract", ["in"] = "node.extract", ["failWith"] = "The note names problems the source does not." }),
            },
            Snippets = new Dictionary<string, string> { ["standards"] = "snippets/standards.md" },
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md", Instruction: "Add a closing line if the plan changed.") },
            Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "assessment", "Consultation note", Check: "node.coverage") },
        };

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompts/assessment.md"] = "Assess.\n{{ snippet.standards.text }}\n{{ input.notes }}\n{{ for c in node.extract.value.concepts }}- {{ c.term }}\n{{ end }}",
            ["prompts/extract.md"] = "Extract the concepts from:\n{{ input.notes }}",
            ["prompts/scope.md"] = "Is this in scope? {{ input.notes }}",
            ["snippets/standards.md"] = "Follow the local standards.",
            ["macros/closing.md"] = "Closing: {{ profile.name }}.",
        };

        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle, WorkflowFormatCatalog? catalog) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, catalog, Accepting25);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        Validate(bundle, Catalog()).Errors;

    public static WorkflowNodeSpec Node(WorkflowPackageManifest manifest, string id) => manifest.Nodes!.First(n => n.Id == id);

    public static WorkflowPackageManifest WithNode(WorkflowPackageManifest manifest, string id, Func<WorkflowNodeSpec, WorkflowNodeSpec> change) =>
        manifest with { Nodes = manifest.Nodes!.Select(n => n.Id == id ? change(n) : n).ToList() };
}

public class WorkflowV25Tests
{
    [Fact]
    public void TheMinimalPackage_IsValid_AtTheSeam()
    {
        var result = V25Package.Validate(V25Package.Minimal(), V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ThePublicEntry_StillRefuses25_UntilA5()
    {
        var (manifest, files) = V25Package.Minimal();

        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());

        Assert.Contains(result.Errors, e => e.Contains("specVersion 25 is not supported"));
        Assert.DoesNotContain(25, WorkflowPackageValidator.AcceptedSpecVersions);
    }

    [Fact]
    public void WithoutACatalogue_CatalogueFormats_AreNamed_Once()
    {
        var result = V25Package.Validate(V25Package.Minimal(), null);

        Assert.Contains("The format catalogue is not available on this engine; catalogue formats (classification, concept-list, prose, terms-subset) cannot be resolved.", result.Errors);
        Assert.Single(result.Errors);
    }

    // ----- § 10: retired by name, with the replacement ------------------------------

    [Theory]
    [InlineData("preludes", "preludes is retired at specVersion 25; read the text inline ({{ snippet.<id>.text }}) — the editor migrated preludes at 23.")]
    [InlineData("data", "data is retired at specVersion 25; declare snippets (read as {{ snippet.<id>.text }}).")]
    [InlineData("schemas", "schemas is retired at specVersion 25; declare formats and name one on each node (nodes[].format).")]
    [InlineData("schemaRefs", "schemaRefs is retired at specVersion 25; declare formats and name one on each node (nodes[].format).")]
    [InlineData("customSchemas", "customSchemas is retired at specVersion 25; declare formats and name one on each node (nodes[].format).")]
    [InlineData("results", "results is retired at specVersion 25; declare deliverables.")]
    [InlineData("result", "result is retired at specVersion 25; declare deliverables.")]
    public void AManifestKey_RetiredAt25_IsRefusedWithItsReplacement(string key, string sentence)
    {
        var (manifest, files) = V25Package.Minimal();
        var map = new Dictionary<string, string> { ["x"] = "x.md" };
        manifest = key switch
        {
            "preludes" => manifest with { Preludes = map },
            "data" => manifest with { Data = map },
            "schemas" => manifest with { Schemas = map },
            "schemaRefs" => manifest with { SchemaRefs = map },
            "customSchemas" => manifest with { CustomSchemas = map },
            "results" => manifest with { Results = manifest.Deliverables },
            _ => manifest with { Result = "node:assessment" },
        };

        Assert.Contains(sentence, V25Package.Errors((manifest, files)));
    }

    [Theory]
    [InlineData("prelude", "Prompt 'assessment' declares prelude, which is retired at specVersion 25; read the text inline")]
    [InlineData("variables", "Prompt 'assessment' declares variables, which are retired at specVersion 25; a template reads objects (input.<id>, node.<id>.text, snippet.<id>.text, item.<member>, macro.<id>.text) and its reads are derived.")]
    [InlineData("raw", "Prompt 'assessment' declares raw, which is retired at specVersion 25; escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}.")]
    public void APromptKey_RetiredAt25_IsRefused(string key, string sentence)
    {
        var (manifest, files) = V25Package.Minimal();
        var prompt = manifest.Prompts!.First();
        var changed = key switch
        {
            "prelude" => prompt with { Prelude = "p" },
            "variables" => prompt with { Variables = new List<string> { "notes" } },
            _ => prompt with { Raw = false },
        };
        manifest = manifest with { Prompts = manifest.Prompts!.Select(p => p.Id == prompt.Id ? changed : p).ToList() };

        Assert.Contains(V25Package.Errors((manifest, files)), e => e.StartsWith(sentence, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bindings", "Node 'assessment' declares bindings, which are retired at specVersion 25; a template reads objects")]
    [InlineData("output", "Node 'assessment' declares output, which is retired at specVersion 25; name the node's format (nodes[].format).")]
    [InlineData("kind", "Node 'assessment' declares kind, which is retired at specVersion 25; the format says what the node is (classification, rendered, terms-subset).")]
    [InlineData("values", "Node 'assessment' declares values, which is retired at specVersion 25; a classification takes its answer set through with: { values: [...] }.")]
    [InlineData("op", "Node 'assessment' declares op, which is retired at specVersion 25; a check is a node of format terms-subset with with: { of, in, failWith }.")]
    [InlineData("of", "Node 'assessment' declares of, which is retired at specVersion 25;")]
    [InlineData("in", "Node 'assessment' declares in, which is retired at specVersion 25;")]
    [InlineData("failWith", "Node 'assessment' declares failWith, which is retired at specVersion 25;")]
    public void ANodeKey_RetiredAt25_IsRefused(string key, string sentence)
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => key switch
        {
            "bindings" => n with { Bindings = new Dictionary<string, WorkflowBindingValue> { ["notes"] = new WorkflowBindingValue("input:notes") } },
            "output" => n with { Output = new WorkflowNodeOutputSpec("s") },
            "kind" => n with { Kind = "template" },
            "values" => n with { Values = new List<string> { "a", "b" } },
            "op" => n with { Op = "terms-subset" },
            "of" => n with { Of = "node:extract" },
            "in" => n with { In = "node:extract" },
            _ => n with { FailWith = "x" },
        });

        Assert.Contains(V25Package.Errors((manifest, files)), e => e.StartsWith(sentence, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("before", "Deliverable 'consult' macro 'closing' declares before, which is retired at specVersion 25; place it on the node as a node instance ({ id, at: before|after }).")]
    [InlineData("after", "Deliverable 'consult' macro 'closing' declares after, which is retired at specVersion 25;")]
    [InlineData("forItem", "Deliverable 'consult' macro 'closing' declares forItem, which is retired at specVersion 25;")]
    [InlineData("slot", "Deliverable 'consult' macro 'closing' declares slot, which is retired at specVersion 25; read {{ macro.<id>.prompt }} in the template where the slot goes.")]
    public void ADeliverableMacroKey_RetiredAt25_IsRefused(string key, string sentence)
    {
        var (manifest, files) = V25Package.Minimal();
        var entry = key switch
        {
            "before" => new WorkflowDeliverableMacroSpec("closing", Before: "node:assessment"),
            "after" => new WorkflowDeliverableMacroSpec("closing", After: "node:assessment"),
            "forItem" => new WorkflowDeliverableMacroSpec("closing", ForItem: "a"),
            _ => new WorkflowDeliverableMacroSpec("closing", Slot: true),
        };
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { entry } }).ToList() };

        Assert.Contains(V25Package.Errors((manifest, files)), e => e.StartsWith(sentence, StringComparison.Ordinal));
    }

    // ----- § 3: formats on nodes ------------------------------------------------------

    [Fact]
    public void ANode_MustDeclareAFormat()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Format = null });

        Assert.Contains("Node 'assessment' must declare a format at specVersion 25.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnUnknownFormat_IsRefused()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Format = "essay" });

        Assert.Contains("Node 'assessment' declares format 'essay', which is neither a package format nor a catalogue format.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnAggregator_IsStructural()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("all", "All", Aggregate: new List<string> { "node:assessment" }, Format: "prose", Prompt: "assessment")).ToList()
        };

        var errors = V25Package.Errors((manifest, files));

        Assert.Contains("Node 'all' aggregates and must not declare format; an aggregator is structural.", errors);
        Assert.Contains("Node 'all' aggregates and must not declare prompt; an aggregator is structural.", errors);
    }

    [Fact]
    public void AComputedNode_TakesNoPrompt_AndOthersNeedOne()
    {
        var (manifest, files) = V25Package.Minimal();
        var withPrompt = V25Package.WithNode(manifest, "coverage", n => n with { Prompt = "assessment" });
        Assert.Contains("Node 'coverage' declares prompt, but format 'terms-subset' is computed and takes none.", V25Package.Errors((withPrompt, files)));

        var without = V25Package.WithNode(manifest, "assessment", n => n with { Prompt = null });
        Assert.Contains("Node 'assessment' must declare a prompt; format 'prose' is attested.", V25Package.Errors((without, files)));

        var unknown = V25Package.WithNode(manifest, "assessment", n => n with { Prompt = "nope" });
        Assert.Contains("Node 'assessment' names prompt 'nope', which the package does not declare.", V25Package.Errors((unknown, files)));
    }

    [Fact]
    public void OnInvalid_IsRequiredIffTheFormatValidates()
    {
        var (manifest, files) = V25Package.Minimal();

        var onText = V25Package.WithNode(manifest, "assessment", n => n with { OnInvalid = "fail" });
        Assert.Contains("Node 'assessment' declares onInvalid, but format 'prose' is text and validates nothing.", V25Package.Errors((onText, files)));

        var missing = V25Package.WithNode(manifest, "extract", n => n with { OnInvalid = null });
        Assert.Contains("Node 'extract' must declare onInvalid (fail | retry | flag); format 'concept-list' is validated.", V25Package.Errors((missing, files)));

        var unknown = V25Package.WithNode(manifest, "extract", n => n with { OnInvalid = "ignore" });
        Assert.Contains("Node 'extract' onInvalid 'ignore' is not one of fail, retry, flag.", V25Package.Errors((unknown, files)));
    }

    [Fact]
    public void AHyphenatedNodeId_IsRefused_WithTheRename()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "extract" ? n with { Id = "identify-problem" } : n).ToList()
        };

        Assert.Contains("Node id 'identify-problem' must be snake_case at specVersion 25 (identify_problem): a template reads it as a Scriban member.", V25Package.Errors((manifest, files)));
    }

    // ----- § 3.6: package-local formats ---------------------------------------------

    private static (WorkflowPackageManifest, Dictionary<string, string>) WithLocalFormat(string id, string file, string? json)
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with { Formats = new List<WorkflowFormatReference> { new(id, file) } };
        manifest = V25Package.WithNode(manifest, "extract", n => n with { Format = id, OnInvalid = "fail" });

        if (json is not null)
        {
            files[file] = json;
        }

        return (manifest, files);
    }

    [Fact]
    public void ALocalCustomFormat_ResolvesOnANode()
    {
        var bundle = WithLocalFormat("medication_list", "formats/medication_list.json", FormatFixtures.LocalCustom);

        var result = V25Package.Validate(bundle, V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Theory]
    [InlineData("Medication-List", "formats/m.json", FormatFixtures.LocalCustom, "Format id 'Medication-List' must be snake_case")]
    [InlineData("prose", "formats/prose.json", FormatFixtures.LocalCustom, "Format 'prose' is a catalogue id and cannot be declared locally; catalogue ids are reserved.")]
    [InlineData("meds", "formats/meds.json", null, "Format 'meds' file 'formats/meds.json' is missing from the package.")]
    [InlineData("meds", "formats/meds.json", "{ nope", "Format 'meds' file is not valid JSON.")]
    [InlineData("meds", "formats/meds.json", FormatFixtures.Prose, "Format 'meds' is package-local and must declare producer custom; attested, rendered and computed formats come from the catalogue.")]
    public void ALocalFormatDefect_IsRefusedByName(string id, string file, string? json, string sentence)
    {
        var bundle = WithLocalFormat(id, file, json);

        Assert.Contains(V25Package.Errors(bundle), e => e.Contains(sentence));
    }

    [Fact]
    public void ALocalFormat_DeclaredTwice_IsRefused()
    {
        var (manifest, files) = WithLocalFormat("meds", "formats/meds.json", FormatFixtures.LocalCustom);
        manifest = manifest with { Formats = manifest.Formats!.Concat(manifest.Formats!).ToList() };

        Assert.Contains("Format 'meds' is declared twice.", V25Package.Errors((manifest, files)));
    }

    // ----- § 5: the macro library ----------------------------------------------------

    [Fact]
    public void AMacro_MustDeclareAnInstruction()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with { Macros = manifest.Macros!.Select(m => m with { Instruction = " " }).ToList() };

        Assert.Contains("Macro 'closing' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AMacroFile_MustExist()
    {
        var (manifest, files) = V25Package.Minimal();
        files.Remove("macros/closing.md");

        Assert.Contains("Macro 'closing' file 'macros/closing.md' is missing from the package.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void TheV24Suite_IsUntouched_ByThe25Path()
    {
        // The v24 fixture validates exactly as before through the public entry.
        var (manifest, files) = V24Fixtures.Base();

        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
