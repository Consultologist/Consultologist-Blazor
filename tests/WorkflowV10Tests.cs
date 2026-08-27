using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v10 step (a) (#492, package-format-v10-design.md § 4, § 7, § 10): the
/// validator accepts 10 and knows its two declaration shapes — a node's kind
/// and values, and fields and element specs that recurse — refusing each
/// below 10 by name. Nothing runs yet.
/// </summary>
public static class V10Fixtures
{
    public static WorkflowPackageManifest Minimal() => V9Fixtures.Structured() with { SpecVersion = 10 };

    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Minimal().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);
        if (index < 0) inputs.Add(input); else inputs[index] = input;
        return Minimal() with { Inputs = inputs };
    }

    /// <summary>A classifier over the draft, before the existing nodes, with its prompt.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) WithClassifier(
        WorkflowNodeSpec? classifier = null,
        WorkflowPackageManifest? from = null)
    {
        var manifest = from ?? Minimal();
        classifier ??= Classifier();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new(classifier.Prompt!, "prompts/classify.md", new List<string> { "referral" })
        };
        var nodes = new List<WorkflowNodeSpec> { classifier };
        nodes.AddRange(manifest.Nodes!);
        manifest = manifest with { Prompts = prompts, Nodes = nodes };
        var files = new Dictionary<string, string>(V6Fixtures.Files(manifest))
        {
            ["prompts/classify.md"] = "Is this in scope? {{ referral }}"
        };
        return (manifest, files);
    }

    public static WorkflowNodeSpec Classifier(
        string id = "scope",
        List<string>? values = null,
        WorkflowNodeOutputSpec? output = null,
        string? forEach = null,
        Dictionary<string, WorkflowBindingValue>? bindings = null) =>
        new(id, "Is the referral in scope?", Prompt: "classify",
            Bindings: bindings ?? new Dictionary<string, WorkflowBindingValue> { ["referral"] = new("input:consult_draft") },
            Output: output, ForEach: forEach, Kind: WorkflowNodeKinds.Classifier,
            Values: values ?? new List<string> { "in_scope", "out_of_scope" });

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV10GateTests
{
    [Fact]
    public void TheValidatorAccepts10_AndTheStoreDoesNot()
    {
        Assert.Contains(10, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(10, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);

        var result = V10Fixtures.Validate(V10Fixtures.Minimal());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ElevenIsRefused_NamingTheSet()
    {
        Assert.Contains(V10Fixtures.Validate(V10Fixtures.Minimal() with { SpecVersion = 11 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7, 8, 9 or 10"));
    }
}

public class WorkflowV10ClassifierTests
{
    private static IEnumerable<string> Errors((WorkflowPackageManifest, IReadOnlyDictionary<string, string>) bundle) => V10Fixtures.Validate(bundle).Errors;

    [Fact]
    public void AClassifier_Publishes()
    {
        var result = V10Fixtures.Validate(V10Fixtures.WithClassifier());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void KindPrompt_IsTheDefaultSpelled()
    {
        var manifest = V10Fixtures.Minimal();
        var nodes = manifest.Nodes!.Select(n => n with { Kind = WorkflowNodeKinds.Prompt }).ToList();
        // Aggregators may not spell a kind; only the prompt nodes get one here.
        nodes = nodes.Select(n => n.Aggregate != null ? n with { Kind = null } : n).ToList();

        var result = V10Fixtures.Validate(manifest with { Nodes = nodes });
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Theory]
    [InlineData("aggregator", "Node 'scope' declares kind 'aggregator'; an aggregator is declared by aggregate, not by kind.")]
    [InlineData("router", "Node 'scope' declares unknown kind 'router' (accepted: prompt, classifier).")]
    public void AnUnknownKind_IsRefusedByName(string kind, string expected)
    {
        var bundle = V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = kind });
        Assert.Contains(expected, Errors(bundle));
    }

    [Fact]
    public void KindAndAggregateTogether_AreRefused()
    {
        var manifest = V10Fixtures.Minimal();
        var nodes = manifest.Nodes!.Select(n => n.Aggregate != null ? n with { Kind = WorkflowNodeKinds.Classifier } : n).ToList();
        Assert.Contains(V10Fixtures.Validate(manifest with { Nodes = nodes }).Errors,
            e => e.EndsWith("declares both kind and aggregate; an aggregator is declared by aggregate alone.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValuesWithoutTheKind_AreRefused()
    {
        var bundle = V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = null });
        Assert.Contains("Node 'scope' declares values but is not a classifier; only kind 'classifier' answers from a value set.", Errors(bundle));
    }

    [Fact]
    public void AClassifierWithoutValues_IsRefused()
    {
        var bundle = V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Values = null });
        Assert.Contains("Classifier 'scope' declares no values; a classifier must declare the values it may answer.", Errors(bundle));
    }

    [Fact]
    public void TheValues_FollowTheEnumRules()
    {
        Assert.Contains("Classifier 'scope' declares one value; a classifier with one value is a constant, not a choice.",
            Errors(V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "only" }))));
        Assert.Contains("Classifier 'scope' value 'In Scope' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).",
            Errors(V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "In Scope", "out" }))));
        Assert.Contains("Classifier 'scope' declares duplicate value 'a'.",
            Errors(V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "a", "a" }))));
    }

    [Fact]
    public void AClassifier_MayNotDeclareOutputOrForEach()
    {
        Assert.Contains("Classifier 'scope' declares output; a classifier's output is the classification contract, implied by its kind.",
            Errors(V10Fixtures.WithClassifier(V10Fixtures.Classifier(output: new WorkflowNodeOutputSpec("concept-list")))));
        Assert.Contains("Classifier 'scope' declares forEach; a classification is one answer, so a classifier is never fanned.",
            Errors(V10Fixtures.WithClassifier(V10Fixtures.Classifier(forEach: "input:prior_notes"))));
    }

    [Fact]
    public void AClassifier_ReadsInputsAndClassifiersOnly()
    {
        var promptNodeId = V10Fixtures.Minimal().Nodes!.First(n => n.Aggregate is null).Id;
        var bundle = V10Fixtures.WithClassifier(V10Fixtures.Classifier(
            bindings: new Dictionary<string, WorkflowBindingValue> { ["referral"] = new($"node:{promptNodeId}") }));

        Assert.Contains($"Classifier 'scope' binds 'referral' to 'node:{promptNodeId}', which is not a classifier; a classifier may read inputs and classifiers only.", Errors(bundle));
    }

    [Fact]
    public void AClassifier_MayReadAnotherClassifier()
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var second = V10Fixtures.Classifier(id: "urgency",
            bindings: new Dictionary<string, WorkflowBindingValue> { ["referral"] = new("node:scope") },
            values: new List<string> { "routine", "urgent" });
        var nodes = new List<WorkflowNodeSpec> { second };
        nodes.AddRange(manifest.Nodes!);

        var result = WorkflowPackageValidator.Validate(manifest with { Nodes = nodes }, files, TestOutputContracts.CatalogSchemas);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void APromptNode_MayBindAClassifier_ButAnAggregatorMayNotAggregateOne()
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var aggregator = manifest.Nodes!.First(n => n.Aggregate != null);
        var nodes = manifest.Nodes!.Select(n => n.Id == aggregator.Id
            ? n with { Aggregate = new List<string>(n.Aggregate!) { "node:scope" } }
            : n).ToList();

        Assert.Contains($"Aggregator node '{aggregator.Id}' aggregates classifier 'scope'; a classifier's value is bindable, never aggregated.",
            WorkflowPackageValidator.Validate(manifest with { Nodes = nodes }, files, TestOutputContracts.CatalogSchemas).Errors);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(8)]
    public void BelowTen_KindAndValuesAreRefusedByName(int specVersion)
    {
        var (manifest, files) = V10Fixtures.WithClassifier(from: V9Fixtures.Structured() with { SpecVersion = specVersion, Tags = specVersion >= 9 ? new List<string>() : null });
        var errors = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors;

        Assert.Contains("Node 'scope' declares kind, which requires specVersion 10.", errors);
        Assert.Contains("Node 'scope' declares values, which requires specVersion 10.", errors);
    }
}

public class WorkflowV10StructureTests
{
    private static IEnumerable<string> Errors(WorkflowPackageManifest manifest) => V10Fixtures.Validate(manifest).Errors;

    // The publisher's wire form: camelCase, nulls omitted.
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string Write(WorkflowPackageManifest manifest) => JsonSerializer.Serialize(manifest, Wire);

    private static WorkflowInputSpec FamilyHistory() =>
        new("family_history", "Family history", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec>
            {
                new("relative", "Relative"),
                new("conditions", "Conditions", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text),
                new("contact", "Contact", Required: false, Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
                {
                    new("phone", "Phone"),
                    new("preferred", "Preferred", Required: false, Type: WorkflowInputTypes.Enum, Values: new List<string> { "phone", "email" })
                })
            });

    private static WorkflowInputSpec Grid() =>
        new("grid", "Grid", Required: false, Type: WorkflowInputTypes.Array,
            Items: new WorkflowElementSpec(WorkflowInputTypes.Array, Items: WorkflowInputTypes.Number));

    [Fact]
    public void AFieldMayHoldStructure_AtTen()
    {
        var result = V10Fixtures.Validate(V10Fixtures.WithInput(FamilyHistory()));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnArrayOfArrays_IsDeclaredByAnElementSpec_AtTen()
    {
        var result = V10Fixtures.Validate(V10Fixtures.WithInput(Grid()));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheRulesRecurse_WithADottedSubject()
    {
        var bad = FamilyHistory() with
        {
            Fields = new List<WorkflowFieldSpec>
            {
                new("contact", "Contact", Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
                {
                    new("kind", "Kind", Type: WorkflowInputTypes.Enum, Values: new List<string> { "only" })
                }),
                new("conditions", "Conditions", Type: WorkflowInputTypes.Array)
            }
        };

        var errors = Errors(V10Fixtures.WithInput(bad)).ToList();
        Assert.Contains("Input 'family_history' field 'contact' field 'kind' declares one enum value; an enum with one value is a constant, not a choice.", errors);
        Assert.Contains("Input 'family_history' field 'conditions' is type 'array' and must declare items.", errors);

        var deep = Grid() with { Items = new WorkflowElementSpec(WorkflowInputTypes.Array, Items: new WorkflowElementSpec(WorkflowInputTypes.Enum)) };
        // The inner array is 'grid items'; an array of enums declares the values (v9's rule, one level down).
        Assert.Contains("Input 'grid' items is type 'enum' and must declare values.", Errors(V10Fixtures.WithInput(deep)));

        var both = Grid() with { Values = new List<string> { "a", "b" } };
        Assert.Contains("Input 'grid' declares items as a shape and also fields or values; an element spec carries its own.", Errors(V10Fixtures.WithInput(both)));
    }

    [Fact]
    public void AnElementSpec_RoundTripsOnTheWire_AndABareOneWritesTheString()
    {
        var manifest = V10Fixtures.WithInput(Grid());
        var json = Write(manifest);

        Assert.Contains("\"items\":{\"type\":\"array\",\"items\":\"number\"}", json);
        Assert.Contains("\"items\":\"text\"", json); // prior_notes, the v9 way

        var back = WorkflowPackageManifestJson.Read(json, "grid", WorkflowPackageValidator.AcceptedSpecVersions);
        var grid = back.Inputs!.Single(i => i.Id == "grid");
        Assert.Equal(WorkflowInputTypes.Array, grid.Items!.Type);
        Assert.Equal(WorkflowInputTypes.Number, grid.Items.Items!.Type);
        Assert.True(grid.Items.Items.IsBare);
    }

    [Fact]
    public void AV9Manifest_WritesTheBytesItAlwaysWrote()
    {
        var v9 = V9Fixtures.Structured();
        var json = Write(v9);

        Assert.DoesNotContain("\"kind\"", json);
        Assert.DoesNotContain("{\"type\":\"object\"", json.Replace("\"items\":\"object\"", string.Empty));
        Assert.Equal(json, Write(WorkflowPackageManifestJson.Read(json, "v9", WorkflowPackageValidator.AcceptedSpecVersions)));
    }

    [Fact]
    public void BelowTen_AnElementSpec_IsRefusedByName()
    {
        var manifest = V9Fixtures.WithInput(Grid());
        Assert.Contains("Input 'grid' declares items as a shape, which requires specVersion 10.", V9Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void BelowTen_AFieldsItemsOrFields_AreRefusedByName()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
        {
            new("history", "History", Items: WorkflowInputTypes.Text),
            new("contact", "Contact", Fields: new List<WorkflowFieldSpec> { new("phone", "Phone") })
        }));
        var errors = V9Fixtures.Validate(manifest).Errors;

        Assert.Contains("Input 'patient' field 'history' declares items, which requires specVersion 10.", errors);
        Assert.Contains("Input 'patient' field 'contact' declares fields, which requires specVersion 10.", errors);
    }

    [Fact]
    public void BelowTen_TheV9Sentences_AreUnchanged()
    {
        // The published conformance suite pins these on a v9 manifest.
        Assert.Contains(V9Fixtures.Validate(V9Fixtures.WithInput(new("prior_notes", "Prior notes", Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Array))).Errors,
            e => e.Contains("structure is one level deep, so an array may not hold arrays"));
        Assert.Contains(V9Fixtures.Validate(V9Fixtures.WithInput(new("patient", "Patient", Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("h", "H", Type: WorkflowInputTypes.Object) }))).Errors,
            e => e.Contains("structure is one level deep, so a field holds a scalar"));
    }

    [Fact]
    public void TheTypeLists_AreKeyedByVersion()
    {
        Assert.Equal(WorkflowInputTypes.Scalars, WorkflowInputTypes.ScalarsFor(9));
        Assert.Equal(WorkflowInputTypes.ElementTypes, WorkflowInputTypes.ElementTypesFor(9));
        Assert.Equal(WorkflowInputTypes.All, WorkflowInputTypes.ScalarsFor(10));
        Assert.Equal(WorkflowInputTypes.All, WorkflowInputTypes.ElementTypesFor(10));
    }
}
