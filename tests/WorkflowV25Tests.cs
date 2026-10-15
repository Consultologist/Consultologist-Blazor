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
    /// A prose node reading an input and a snippet, a concept-list node, a
    /// classification taking its values through with, a terms-subset check,
    /// a node fanning a snippet folder, an aggregator, a macro with an
    /// instruction placed after the prose node, one deliverable naming the
    /// aggregator and the check.
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
            Inputs = new List<WorkflowInputSpec>
            {
                new("notes", "Referral notes"),
                new("kind", "Kind", true, "enum", new List<string> { "new", "follow_up" }),
            },
            Prompts = new List<WorkflowPromptSpec>
            {
                new("assessment", "prompts/assessment.md", new List<string>()),
                new("extract", "prompts/extract.md", new List<string>()),
                new("scope", "prompts/scope.md", new List<string>()),
                new("section", "prompts/section.md", new List<string>()),
            },
            Nodes = new List<WorkflowNodeSpec>
            {
                new("extract", "Extract concepts", Prompt: "extract", Format: "concept-list", OnInvalid: WorkflowOnInvalid.Fail),
                new("scope", "Scope", Prompt: "scope", Format: "classification", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["values"] = new JsonArray("in_scope", "out_of_scope") }),
                new("assessment", "Assessment", Prompt: "assessment", Format: "prose",
                    Macros: new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) }),
                new("section_notes", "Section notes", Prompt: "section", Format: "prose", ForEach: "snippet.sections"),
                new("coverage", "Coverage", Format: "terms-subset", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["of"] = "node.extract", ["in"] = "node.extract", ["failWith"] = "The note names problems the source does not." }),
                new("all", "Everything", Aggregate: new List<string> { "node.assessment", "node.section_notes" }),
            },
            Snippets = new Dictionary<string, string> { ["standards"] = "snippets/standards.md", ["sections"] = "snippets/sections/" },
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md", Instruction: "Add a closing line if the plan changed.") },
            Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "node.all", "Consultation note", Check: "node.coverage") },
        };

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompts/assessment.md"] = "Assess.\n{{ snippet.standards.text }}\n{{ input.notes }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}",
            ["prompts/extract.md"] = "Extract the concepts from:\n{{ input.notes }}",
            ["prompts/scope.md"] = "Is this in scope? {{ input.notes }}",
            ["prompts/section.md"] = "{{ item.name }}: {{ item.text }}",
            ["snippets/standards.md"] = "Follow the local standards.",
            ["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md", "when": "kind == new" } ] }""",
            ["snippets/sections/hpi.md"] = "History: {{ input.notes }}",
            ["snippets/sections/plan.md"] = "Plan.",
            ["macros/closing.md"] = "Closing: {{ profile.name }}.",
        };

        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle, WorkflowFormatCatalog? catalog) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, catalog, Accepting25);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        Validate(bundle, Catalog()).Errors;

    public static List<string> Warnings((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        Validate(bundle, Catalog()).Warnings;

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithPrompt((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle, string promptId, string text)
    {
        var files = new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal) { [$"prompts/{promptId}.md"] = text };
        return (bundle.Manifest, files);
    }

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
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("everything", "Everything", Aggregate: new List<string> { "node.assessment" }, Format: "prose", Prompt: "assessment")).ToList()
        };

        var errors = V25Package.Errors((manifest, files));

        Assert.Contains("Node 'everything' aggregates and must not declare format; an aggregator is structural.", errors);
        Assert.Contains("Node 'everything' aggregates and must not declare prompt; an aggregator is structural.", errors);
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
        manifest = manifest with
        {
            Formats = new List<WorkflowFormatReference> { new(id, file) },
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("meds", "Medications", Prompt: "extract", Format: id, OnInvalid: "fail")).ToList(),
        };
        manifest = V25Package.WithNode(manifest, "all", n => n with { Aggregate = n.Aggregate!.Append("node.meds").ToList() });

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

/// <summary>A4 PR 2: templates walked, edges derived, macros placed and read, the graph, the probe, deliverables, reachability.</summary>
public class WorkflowV25TemplateTests
{
    private static (WorkflowPackageManifest, Dictionary<string, string>) Minimal() => V25Package.Minimal();

    [Fact]
    public void TheWalkersSentences_PassThrough()
    {
        var bundle = V25Package.WithPrompt(Minimal(), "assessment", "{{ node.nope.text }} {{ input.nope }} {{ data.x }}");

        var errors = V25Package.Errors(bundle);

        Assert.Contains("Prompt 'assessment' reads node 'nope', which the package does not declare.", errors);
        Assert.Contains("Prompt 'assessment' reads input 'nope', which the package does not declare.", errors);
        Assert.Contains(errors, e => e.StartsWith("Prompt 'assessment' reads 'data'; at specVersion 25 data is 'snippet'", StringComparison.Ordinal));
    }

    [Fact]
    public void ABareStructuredTextRead_Warns()
    {
        var bundle = V25Package.WithPrompt(Minimal(), "assessment", "{{ snippet.standards.text }} {{ node.extract.text }}");

        Assert.Contains(V25Package.Warnings(bundle), w => w.Contains("Prompt 'assessment' reads node.extract.text of format 'concept-list' (object)"));
    }

    [Fact]
    public void With_IsTypedByTheFormat()
    {
        var (manifest, files) = Minimal();

        var onProse = V25Package.WithNode(manifest, "assessment", n => n with { With = new Dictionary<string, JsonNode?> { ["x"] = "y" } });
        Assert.Contains("Node 'assessment' declares with, but format 'prose' declares no parameters.", V25Package.Errors((onProse, files)));

        var missing = V25Package.WithNode(manifest, "coverage", n => n with { With = new Dictionary<string, JsonNode?> { ["of"] = "node.extract", ["in"] = "node.extract" } });
        Assert.Contains("Node 'coverage' with must supply parameter 'failWith'.", V25Package.Errors((missing, files)));

        var wrongFormat = V25Package.WithNode(manifest, "coverage", n => n with { With = new Dictionary<string, JsonNode?> { ["of"] = "node.assessment", ["in"] = "node.extract", ["failWith"] = "x" } });
        Assert.Contains("Node 'coverage' with 'of' names node 'assessment' of format 'prose', but the parameter requires format 'concept-list'.", V25Package.Errors((wrongFormat, files)));

        var none = V25Package.WithNode(manifest, "coverage", n => n with { With = null });
        Assert.Contains("Node 'coverage' with must supply parameter 'of'.", V25Package.Errors((none, files)));
    }

    [Fact]
    public void ANonFan_ReadingAFan_MustAggregate()
    {
        var bundle = V25Package.WithPrompt(Minimal(), "assessment", "{{ node.section_notes.text }}");

        Assert.Contains("Node 'assessment' reads node 'section_notes', which fans; aggregation is explicit at specVersion 25 — collect it through an aggregator node.", V25Package.Errors(bundle));
    }

    [Fact]
    public void TwoFans_OverDifferentSources_DoNotAlign()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Append(new WorkflowInputSpec("history", "History", true, "array", Items: new WorkflowElementSpec("text"))).ToList(),
            Prompts = manifest.Prompts!.Append(new WorkflowPromptSpec("per_event", "prompts/per_event.md", new List<string>())).ToList(),
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("per_event", "Per event", Prompt: "per_event", Format: "prose", ForEach: "input.history")).ToList(),
        };
        manifest = V25Package.WithNode(manifest, "all", n => n with { Aggregate = n.Aggregate!.Append("node.per_event").ToList() });
        files["prompts/per_event.md"] = "{{ item.value }} {{ node.section_notes.text }}";

        Assert.Contains("Node 'per_event' reads node 'section_notes' across fans ('input.history' vs 'snippet.sections'); two fans align only over one source.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnInputFan_TypesTheItem()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Append(new WorkflowInputSpec("history", "History", true, "array", Items: new WorkflowElementSpec("object"), Fields: new List<WorkflowFieldSpec> { new("event", "Event", true, "text") })).ToList(),
            Prompts = manifest.Prompts!.Append(new WorkflowPromptSpec("per_event", "prompts/per_event.md", new List<string>())).ToList(),
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("per_event", "Per event", Prompt: "per_event", Format: "prose", ForEach: "input.history")).ToList(),
        };
        manifest = V25Package.WithNode(manifest, "all", n => n with { Aggregate = n.Aggregate!.Append("node.per_event").ToList() });
        files["prompts/per_event.md"] = "{{ item.value.event }} {{ item.value.date }}";

        Assert.Contains("Prompt 'per_event' reads field 'date' of 'item.value', which it does not declare (fields: event).", V25Package.Errors((manifest, files)));
    }

    [Theory]
    [InlineData("data:sections", "Node 'section_notes' forEach 'data:sections' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("snippet:sections", "Node 'section_notes' forEach 'snippet:sections' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("input:history", "Node 'section_notes' forEach 'input:history' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("snippet.nope", "Node 'section_notes' forEach fans snippet folder 'nope', which the package does not declare.")]
    [InlineData("snippet.standards", "Node 'section_notes' forEach fans snippet 'standards', which is a file, not a folder.")]
    [InlineData("input.notes", "Node 'section_notes' forEach 'input.notes' fans input 'notes', which is a text; only an array can be fanned.")]
    [InlineData("input.nope", "Node 'section_notes' forEach 'input.nope' fans undeclared input 'nope'.")]
    [InlineData("sections", "Node 'section_notes' forEach 'sections' must be snippet.<folder> or input.<id>.")]
    [InlineData("snippet.sections.text", "Node 'section_notes' forEach 'snippet.sections.text' must be snippet.<folder> or input.<id>.")]
    public void ForEach_NamesASnippetFolderOrAnArrayInput(string forEach, string sentence)
    {
        var (manifest, files) = Minimal();
        manifest = V25Package.WithNode(manifest, "section_notes", n => n with { ForEach = forEach });

        Assert.Contains(sentence, V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AFolderItemWhen_ReadsInputsOnly()
    {
        var (manifest, files) = Minimal();
        files["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md", "when": "node:scope == in_scope" } ] }""";

        Assert.Contains("Snippet folder 'sections' item 'hpi' condition reads 'node:scope'; a folder item's when reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnOutputMacro_NeedsAFormatThatTakesOne()
    {
        var (manifest, files) = Minimal();
        var placed = V25Package.WithNode(manifest, "extract", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after") } });
        Assert.Contains("Node 'extract' macro 'closing' is placed after the output, but format 'concept-list' takes no output macros; only a text format with outputMacros may.", V25Package.Errors((placed, files)));

        var inline = V25Package.WithPrompt((manifest, files), "extract", "{{ input.notes }} {{ macro.closing.prompt }}");
        Assert.Contains("Node 'extract' reads macro.closing.prompt (a slot), but format 'concept-list' takes no output macros; only a text format with outputMacros may.", V25Package.Errors(inline));
    }

    [Fact]
    public void AnInlineSlot_ThroughASnippet_IsFine_OnProse()
    {
        var (manifest, files) = Minimal();
        files["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}";

        var result = V25Package.Validate((manifest, files), V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AMacro_NeverPlacedOrRead_IsRefused()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with { Macros = manifest.Macros!.Append(new WorkflowMacroSpec("unused", "Unused", "macros/unused.md", Instruction: "x")).ToList() };
        files["macros/unused.md"] = "Nothing.";

        Assert.Contains("Macro 'unused' is declared but never placed or read; place it on a node or a deliverable, or read macro.unused.text / macro.unused.prompt.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnInstance_HasOneIdentity()
    {
        var (manifest, files) = Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after"), new("closing", At: "after", When: "kind == new") } });

        Assert.Contains("Node 'assessment' macro 'closing' is placed twice (after); one identity (id, at, forItem) once — two gates to one text is one instance with when: a or b.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void ForItem_NamesAnItemOfTheFannedFolder()
    {
        var (manifest, files) = Minimal();
        var ok = V25Package.WithNode(manifest, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", ForItem: "plan") } });
        Assert.True(V25Package.Validate((ok, files), V25Package.Catalog()).IsValid);

        var unknown = V25Package.WithNode(manifest, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", ForItem: "ros") } });
        Assert.Contains("Node 'section_notes' macro 'closing' anchors to fan item 'ros', which the folder fanned by 'section_notes' does not contain (items: hpi, plan).", V25Package.Errors((unknown, files)));

        var noFan = V25Package.WithNode(manifest, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", ForItem: "hpi") } });
        Assert.Contains("Node 'assessment' macro 'closing' anchors to fan item 'hpi', but 'assessment' does not fan a snippet folder.", V25Package.Errors((noFan, files)));
    }

    [Fact]
    public void FolderItems_LackingTheSlotRead_AreNamed()
    {
        var (manifest, files) = Minimal();
        files["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}";

        Assert.Contains("Node 'section_notes' fans snippet folder 'sections'; items plan carry no macro.closing.prompt read, so no marker is emitted for them.", V25Package.Warnings((manifest, files)));
    }

    [Fact]
    public void ASnippetCycle_IsNamed()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with { Snippets = new Dictionary<string, string>(manifest.Snippets!) { ["intro"] = "snippets/intro.md" } };
        files["snippets/standards.md"] = "{{ snippet.intro.text }}";
        files["snippets/intro.md"] = "{{ snippet.standards.text }}";

        Assert.Contains("Templates contain a cycle: snippet 'standards' → snippet 'intro' → snippet 'standards'.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AMacroText_ReachingAPromptRead_IsRefused()
    {
        var (manifest, files) = Minimal();
        files["macros/closing.md"] = "{{ snippet.standards.text }}";
        files["snippets/standards.md"] = "{{ macro.closing.prompt }}";

        Assert.Contains("Macro 'closing' text reaches macro.closing.prompt through snippet 'standards'; text placed after a reply cannot carry a slot marker.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void AnObjectFormSlot_IsAccepted_At25_WhenItDeclaresFields()
    {
        // B6 #1005 (design § 9): the v13 refusal lifts at 25 for an object
        // with declared fields; an object with none, and an array of
        // objects, are still refused by name.
        var (manifest, files) = Minimal();
        WorkflowPackageManifest WithIntake(WorkflowInputSpec intake) => manifest with { Inputs = manifest.Inputs!.Append(intake).ToList() };

        var ok = WithIntake(new WorkflowInputSpec("intake", "Intake", Required: false, Type: new WorkflowTypeSet(new[] { WorkflowInputTypes.Object }),
            Fields: new List<WorkflowFieldSpec> { new("allergies", "Allergies", Type: "array", Items: new WorkflowElementSpec("text")) },
            ExpectedContent: WorkflowExpectedContent.Form));
        Assert.True(V25Package.Validate((ok, files), V25Package.Catalog()).IsValid, string.Join(" | ", V25Package.Errors((ok, files))));

        var noFields = WithIntake(new WorkflowInputSpec("intake", "Intake", Required: false, Type: new WorkflowTypeSet(new[] { WorkflowInputTypes.Object }), ExpectedContent: WorkflowExpectedContent.Form));
        Assert.Contains("Input 'intake' declares expectedContent 'form' on an object with no fields; declare the fields a response fills.", V25Package.Errors((noFields, files)));

        var arrayOfObjects = WithIntake(new WorkflowInputSpec("visits", "Visits", Required: false, Type: new WorkflowTypeSet(new[] { WorkflowInputTypes.Array }),
            Items: new WorkflowElementSpec("object"), Fields: new List<WorkflowFieldSpec> { new("on", "On", Type: "date") },
            ExpectedContent: WorkflowExpectedContent.Form));
        Assert.Contains("Input 'visits' declares expectedContent 'form', which a slot of type 'array' cannot be filled from.", V25Package.Errors((arrayOfObjects, files)));
    }

    [Fact]
    public void ADeliverableMacro_ReadingAFannedNode_IsRefused()
    {
        // B8 #1027 (design § 2): a deliverable instance's reads are the
        // deliverable's edges; a fanned node has no item at a deliverable.
        var (manifest, files) = Minimal();
        manifest = manifest with
        {
            Deliverables = new List<WorkflowDeliverableSpec>
            {
                new("consult", "node.all", "Consultation note", Check: "node.coverage",
                    Macros: new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) }),
            },
        };
        files["macros/closing.md"] = "Closing: {{ node.section_notes.text }}.";

        Assert.Contains("Deliverable 'consult' macro 'closing' reads node 'section_notes', which fans; a deliverable macro reads scalar nodes and aggregators — place the macro on the node to read its items.", V25Package.Errors((manifest, files)));

        files["macros/closing.md"] = "Closing: {{ node.scope.text }}.";
        Assert.True(V25Package.Validate((manifest, files), V25Package.Catalog()).IsValid, string.Join(" | ", V25Package.Errors((manifest, files))));
    }

    [Fact]
    public void FailIfEmpty_LivesOnTheNode_At25()
    {
        // B5 #1004 (design § 3.6): output is refused at 25, so failIfEmpty moves
        // up to the node — a sentence, judged on the reply; a producer with no
        // reply takes none.
        var (manifest, files) = Minimal();

        var onProse = V25Package.WithNode(manifest, "assessment", n => n with { FailIfEmpty = "The assessment is empty." });
        Assert.True(V25Package.Validate((onProse, files), V25Package.Catalog()).IsValid);

        var onConceptList = V25Package.WithNode(manifest, "extract", n => n with { FailIfEmpty = "No concepts were found." });
        Assert.True(V25Package.Validate((onConceptList, files), V25Package.Catalog()).IsValid);

        var blank = V25Package.WithNode(manifest, "assessment", n => n with { FailIfEmpty = " " });
        Assert.Contains("Node 'assessment' failIfEmpty must not be blank.", V25Package.Errors((blank, files)));

        var onComputed = V25Package.WithNode(manifest, "coverage", n => n with { FailIfEmpty = "Empty." });
        Assert.Contains("Node 'coverage' declares failIfEmpty, but format 'terms-subset' is computed and has no reply to judge.", V25Package.Errors((onComputed, files)));
    }

    [Fact]
    public void TheStrictRender_NamesTheTemplate()
    {
        var bundle = V25Package.WithPrompt(Minimal(), "assessment", """{{ input.notes | date.to_string "%Y" }}""");

        Assert.Contains(V25Package.Errors(bundle), e => e.StartsWith("Prompt 'assessment' failed strict rendering:", StringComparison.Ordinal));
    }

    [Fact]
    public void Deliverables_AreRequired_AndNameAnAggregator()
    {
        var (manifest, files) = Minimal();

        Assert.Contains("deliverables must declare at least one deliverable.", V25Package.Errors((manifest with { Deliverables = new List<WorkflowDeliverableSpec>() }, files)));
        Assert.Contains("Deliverable 'consult' must reference an aggregator node ('assessment' is not one).", V25Package.Errors((manifest with { Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "node.assessment", "C", Check: "node.coverage") } }, files)));
        Assert.Contains("Deliverable 'consult' references unknown node 'nope'.", V25Package.Errors((manifest with { Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "node.nope", "C", Check: "node.coverage") } }, files)));
        // #1037: the object spelling; the ≤24 one is refused by name.
        Assert.Contains("Deliverable 'consult' node 'node:all' is the ≤24 spelling; at specVersion 25 a deliverable names node.<id>.", V25Package.Errors((manifest with { Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "node:all", "C", Check: "node.coverage") } }, files)));
        Assert.Contains("Deliverable 'consult' node 'all' must be node.<id>.", V25Package.Errors((manifest with { Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "all", "C", Check: "node.coverage") } }, files)));
    }

    [Theory]
    [InlineData("node:assessment", "Aggregator node 'all' source 'node:assessment' is the ≤24 spelling; at specVersion 25 a source is node.<id>.")]
    [InlineData("assessment", "Aggregator node 'all' source 'assessment' must be node.<id>.")]
    [InlineData("node.assessment.text", "Aggregator node 'all' source 'node.assessment.text' must be node.<id>.")]
    [InlineData("node.nope", "Aggregator node 'all' references unknown node 'nope'.")]
    [InlineData("node.all", "Aggregator node 'all' aggregates itself.")]
    public void AnAggregateSource_IsANodeReference_InTheObjectSpelling(string source, string expected)
    {
        var (manifest, files) = Minimal();
        manifest = V25Package.WithNode(manifest, "all", n => n with { Aggregate = new List<string> { "node.section_notes", source } });

        Assert.Contains(expected, V25Package.Errors((manifest, files)));
    }

    [Theory]
    [InlineData("node:coverage", "Deliverable 'consult' check 'node:coverage' must be node.<id>, naming a node of a computed format.")]
    [InlineData("node.assessment", "Deliverable 'consult' check names node 'assessment', whose format is not computed; a check is a node of a computed format (terms-subset).")]
    [InlineData("node.nope", "Deliverable 'consult' check names node 'nope', which the package does not declare.")]
    public void ACheck_NamesAComputedNode(string check, string sentence)
    {
        var (manifest, files) = Minimal();
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Check = check }).ToList() };

        Assert.Contains(sentence, V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void ACheckNobodyNames_IsDeadWeight()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Check = null }).ToList() };

        Assert.Contains("Check 'coverage' is not named by any deliverable; a check gates a deliverable, or it is dead weight.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void ADeliverableCondition_ReadsEnumFormatNodes()
    {
        var (manifest, files) = Minimal();

        var ok = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = "node:scope == in_scope and kind == new" }).ToList() };
        Assert.True(V25Package.Validate((ok, files), V25Package.Catalog()).IsValid, string.Join(" | ", V25Package.Errors((ok, files))));

        var badValue = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = "node:scope == maybe" }).ToList() };
        Assert.Contains("Deliverable 'consult' condition compares 'node:scope' to 'maybe', which it does not declare (values: in_scope, out_of_scope).", V25Package.Errors((badValue, files)));

        var notEnum = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = "node:assessment == x" }).ToList() };
        Assert.Contains("Deliverable 'consult' condition reads 'node:assessment', which is not a classifier (classifiers: scope).", V25Package.Errors((notEnum, files)));
    }

    [Fact]
    public void ADeliverableInstance_DeclaresAt()
    {
        var (manifest, files) = Minimal();
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { "closing", new("closing", At: "before") } }).ToList() };

        var errors = V25Package.Errors((manifest, files));

        Assert.Contains("Deliverable 'consult' macro 'closing' must declare at (prepend | append).", errors);
        Assert.Contains("Deliverable 'consult' macro 'closing' declares at 'before'; a deliverable instance is placed 'prepend' or 'append'.", errors);
    }

    [Fact]
    public void ADeliverable_IsSignedOnce()
    {
        var (manifest, files) = Minimal();
        files["macros/closing.md"] = "{{ profile.signature }}";
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Signature = true, Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append") } }).ToList() };

        Assert.Contains("Deliverable 'consult' declares signature and places a macro that reads profile.signature; a deliverable is signed once.", V25Package.Errors((manifest, files)));
    }

    [Fact]
    public void EveryNode_FeedsADeliverable_AndADeliverableHasAFan()
    {
        var (manifest, files) = Minimal();
        manifest = V25Package.WithNode(manifest, "all", n => n with { Aggregate = new List<string> { "node.assessment" } });

        var errors = V25Package.Errors((manifest, files));

        Assert.Contains(errors, e => e.StartsWith("Deliverable 'consult' must transitively include at least one forEach source: a deliverable with no fan has no consult.", StringComparison.Ordinal));
        Assert.Contains("Node 'section_notes' does not feed the deliverable: every node must transitively reach 'all'. Read it from a node that does, or add it to the deliverable's aggregator.", errors);
    }

    [Fact]
    public void AnEnumNode_ReadOnlyByConditions_IsNotOrphaned()
    {
        // scope is read by nothing but a condition; it feeds the boundary.
        var result = V25Package.Validate(Minimal(), V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ANodeCycle_IsRefused()
    {
        var bundle = V25Package.WithPrompt(Minimal(), "extract", "{{ node.assessment.text }}");

        Assert.Contains(V25Package.Errors(bundle), e => e.StartsWith("nodes contain a cycle involving:", StringComparison.Ordinal));
    }
}
