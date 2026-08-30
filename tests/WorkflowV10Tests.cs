using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.PackageFormat;
using Consultologist.Api.Workflow;

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
    public void TheValidatorAccepts10_AndTheStoreRunsIt()
    {
        // (a) #492 accepted ten at publish; (i) #500 runs it — the gate flipped
        // together with the registry's v10 publication and the pin.
        Assert.Contains(10, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(10, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
        Assert.Equal(10, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions.Max());

        var result = V10Fixtures.Validate(V10Fixtures.Minimal());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ElevenIsAccepted_ButDoesNotRunYet()
    {
        // (a) #563: the validator's gate moved first, the engine's follows at
        // rung (g). Twelve is what the gate refuses now — WorkflowV11Tests.
        Assert.Contains(11, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(11, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
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

/// <summary>v10 step (b) (#493): nested values against nested declarations, hash 6, the probe, the renderer.</summary>
public class WorkflowV10ValueTests
{
    private const string Sentinel = "SENTINEL-CLINICAL-CONTENT-0f1e2d";

    private static WorkflowInputSpec FamilyHistory() =>
        new("family_history", "Family history", Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
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

    private static ConsultInputValue Obj(params (string Id, ConsultInputValue Value)[] fields) =>
        ConsultInputValue.OfObject(fields.Select(f => new ConsultInputEntry(f.Id, f.Value)));

    private static ConsultInputValue Arr(params ConsultInputValue[] elements) => ConsultInputValue.OfArray(elements);

    private static string? Complaint(WorkflowInputSpec input, ConsultInputValue value) =>
        Consultologist.Api.Jobs.ConsultGenerationJobStarter.CanonicalFormComplaint(input, value);

    [Fact]
    public void ThePackageResponse_CarriesTheElementToAnyDepth()
    {
        // v10 step (f) (#497): the intake form reads the element as the
        // declaration node resolves it — a bare v9 items with the array's own
        // fields becomes an element that carries them; a spec carries its own.
        var manifest = V10Fixtures.WithInput(FamilyHistory()) with
        {
            Inputs = new List<WorkflowInputSpec>(V10Fixtures.WithInput(FamilyHistory()).Inputs!) { Grid() }
        };
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);
        var response = WorkflowPackages.Describe(new WorkflowPackage(manifest, Nodes: manifest.Nodes, Data: data, Results: new List<WorkflowResolvedResult>()));

        var family = response.Inputs!.Single(i => i.Id == "family_history");
        Assert.Equal(WorkflowInputTypes.Object, family.Items!.Type);
        Assert.Equal(new[] { "relative", "conditions", "contact" }, family.Items.Fields!.Select(f => f.Id));
        Assert.Equal(new[] { "relative", "conditions", "contact" }, family.Fields!.Select(f => f.Id));
        var conditions = family.Items.Fields[1];
        Assert.Equal(WorkflowInputTypes.Text, conditions.Items!.Type);
        var preferred = family.Items.Fields[2].Fields![1];
        Assert.Equal(new[] { "phone", "email" }, preferred.Values);

        var grid = response.Inputs.Single(i => i.Id == "grid");
        Assert.Equal(WorkflowInputTypes.Array, grid.Items!.Type);
        Assert.Equal(WorkflowInputTypes.Number, grid.Items.Items!.Type);
        Assert.Null(grid.Items.Fields);

        // The bytes: the element is an object even for the flat v9 form.
        var json = JsonSerializer.Serialize(grid, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"items\":{\"type\":\"array\",\"items\":{\"type\":\"number\"", json);
    }

    [Fact]
    public void AConditionOverAHyphenatedNode_ReadsAsANodeValue_AndTheValidatorNamesIt()
    {
        // #498: node ids carry hyphens (draft-section); the operand parser
        // took the declared-id rule and read the text as arithmetic instead.
        Assert.True(WorkflowResultConditions.TryParseExpression("node:draft-section == in_scope", out var expression, out var error), error);
        var clause = Assert.Single(expression!.Leaves);
        Assert.True(clause.IsNodeValue);
        Assert.Equal("draft-section", clause.NodeId);
        Assert.False(clause.IsArithmetic);

        var (manifest, files) = V10Fixtures.WithClassifier(from: V9Fixtures.Conditional("node:draft-section == in_scope") with { SpecVersion = 10 });
        var errors = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors;
        Assert.Contains(errors, e => e.Contains("reads 'node:draft-section', which is not a classifier"));
    }

    [Fact]
    public void ANestedValue_ThatMatchesItsDeclaration_HasNoComplaint()
    {
        var value = Arr(Obj(("relative", "mother"), ("conditions", Arr("asthma", "migraine")),
            ("contact", Obj(("phone", "555"), ("preferred", "email")))));

        Assert.Null(Complaint(FamilyHistory(), value));
        Assert.Null(Complaint(Grid(), Arr(Arr(ConsultInputValue.OfNumber("1.5"), ConsultInputValue.OfNumber("2")), Arr())));
    }

    [Fact]
    public void TheComplaint_SpellsThePath_AndNeverTheValue()
    {
        var wrongEnum = Arr(Obj(("relative", "mother"), ("contact", Obj(("phone", "555"), ("preferred", Sentinel)))));
        var complaint = Complaint(FamilyHistory(), wrongEnum)!;
        Assert.StartsWith("element 0 field 'contact' field 'preferred' accepts 'phone', 'email'; got '", complaint, StringComparison.Ordinal);

        var notAnArray = Arr(Obj(("relative", "mother"), ("conditions", "asthma")));
        Assert.Equal("element 0 field 'conditions' is an array and must be sent as a JSON array; got text.", Complaint(FamilyHistory(), notAnArray));

        var textInGrid = Arr(Arr(ConsultInputValue.OfNumber("1"), Sentinel));
        Assert.Equal("element 0 element 1 is a number and must be sent as a JSON number; got text.", Complaint(Grid(), textInGrid));

        var undeclared = Arr(Obj(("relative", "mother"), ("contact", Obj(("phone", "555"), ("fax", "1")))));
        Assert.Equal("element 0 field 'contact' has a field 'fax' it does not declare (fields: phone, preferred).", Complaint(FamilyHistory(), undeclared));
    }

    [Fact]
    public void TheV9Sentences_AreUnchanged_ForAOneLevelDeclaration()
    {
        var patient = V9Fixtures.Structured().Inputs!.Single(i => i.Id == "patient");
        var medications = V9Fixtures.Structured().Inputs!.Single(i => i.Id == "medications");

        Assert.Equal("has a field 'nickname' it does not declare (fields: family_name, age, sex).",
            Complaint(patient, Obj(("family_name", "x"), ("age", ConsultInputValue.OfNumber("1")), ("nickname", "y"))));
        Assert.Equal("is missing required field 'family_name'.", Complaint(patient, Obj(("age", ConsultInputValue.OfNumber("1")))));
        Assert.Equal("element 1 is missing required field 'name'.", Complaint(medications, Arr(Obj(("name", "a")), Obj(("dose", "b")))));
        Assert.Equal("element 0 is an object and must be sent as a JSON object; got text.", Complaint(medications, Arr("x")));
    }

    [Fact]
    public void DefinitionSix_IsDefinitionFiveRecursed()
    {
        var flat = new Dictionary<string, ConsultInputValue>
        {
            ["consult_draft"] = "Draft text.",
            ["patient"] = Obj(("z", ConsultInputValue.OfNumber("1.50")), ("a", "x"))
        };
        Assert.Equal(ConsultGenerationProvenance.ComputeStructuredInputsHash(flat), ConsultGenerationProvenance.ComputeNestedInputsHash(flat));
        Assert.Equal(6, ConsultGenerationProvenance.NestedInputsHashVersion);

        var nested = new Dictionary<string, ConsultInputValue>
        {
            ["family_history"] = Arr(Obj(("relative", "mother"), ("contact", Obj(("preferred", "email"), ("phone", "555"))), ("conditions", Arr("b", "a"))))
        };
        var bytes = """{"family_history":[{"conditions":["b","a"],"contact":{"phone":"555","preferred":"email"},"relative":"mother"}]}""";
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(bytes), ConsultGenerationProvenance.ComputeNestedInputsHash(nested));
    }

    [Fact]
    public void TheHashLadder_IsFiveWay()
    {
        var supplied = new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft text." };
        var resolution = new Consultologist.Api.Jobs.EffectiveInputsResolution(null, supplied, null);
        var request = new Consultologist.Api.Models.ConsultGenerationRequest("Draft text.");

        var (hash10, version10) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.EffectiveInputHashOf(10, request, resolution);
        var (hash9, version9) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.EffectiveInputHashOf(9, request, resolution);

        Assert.Equal(6, version10);
        Assert.Equal(5, version9);
        Assert.Equal(hash9, hash10);
    }

    [Fact]
    public void TheProbe_Recurses_TwoElementsAtEveryLevel()
    {
        var probe = (Scriban.Runtime.ScriptArray)WorkflowPackageValidator.ProbeValue(FamilyHistory());
        Assert.Equal(2, probe.Count);
        var entry = (Scriban.Runtime.ScriptObject)probe[0]!;
        Assert.Equal("placeholder", entry["relative"]);
        var conditions = (Scriban.Runtime.ScriptArray)entry["conditions"]!;
        Assert.Equal(new object[] { "placeholder", "placeholder" }, conditions.ToArray());
        var contact = (Scriban.Runtime.ScriptObject)entry["contact"]!;
        Assert.Equal("placeholder", contact["phone"]);

        var grid = (Scriban.Runtime.ScriptArray)WorkflowPackageValidator.ProbeValue(Grid());
        var inner = (Scriban.Runtime.ScriptArray)grid[1]!;
        Assert.Equal(2, inner.Count);
        Assert.IsType<decimal>(inner[0]);
    }

    [Fact]
    public void ANestedValue_RendersAsItself()
    {
        var manifest = V10Fixtures.WithInput(FamilyHistory());
        var declarations = new Dictionary<string, WorkflowInputSpec> { ["family_history"] = FamilyHistory() };
        var value = Arr(Obj(("relative", "mother"), ("conditions", Arr("asthma", "migraine")), ("contact", Obj(("phone", "555")))));

        var rendered = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("t", "{{ for r in family_history }}{{ r.relative }}: {{ r.conditions | array.join \", \" }} ({{ r.contact.phone }}){{ end }}", new[] { "family_history" }, null),
            new Dictionary<string, string> { ["family_history"] = value.AsJson() },
            new Dictionary<string, string> { ["family_history"] = WorkflowInputTypes.Array },
            declarations);

        Assert.Equal("mother: asthma, migraine (555)", rendered);
        _ = manifest;
    }

    [Fact]
    public void ARequiredArrayDeeperThanOneLevel_WarnsAboutTheEmailDoor()
    {
        var result = V10Fixtures.Validate(V10Fixtures.WithInput(FamilyHistory()));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, w => w.StartsWith("Input 'family_history' is a required array with structure deeper than one level.", StringComparison.Ordinal));

        var flat = V10Fixtures.Validate(V10Fixtures.WithInput(V9Fixtures.Structured().Inputs!.Single(i => i.Id == "medications") with { Required = true }));
        Assert.DoesNotContain(flat.Warnings, w => w.Contains("deeper than one level"));
    }

    [Fact]
    public void TheDoor_BoundsNestedStructure_AtEveryLevel()
    {
        var deepText = Arr(Obj(("a", Arr(new string('x', Consultologist.Api.Jobs.ConsultGenerationJobs.MaxInputLength + 1)))));
        Assert.Equal("Input 'v' element 0 field 'a' element 0 exceeds 256 KB.",
            Consultologist.Api.Jobs.ConsultGenerationJobs.ValidateRequest(new Consultologist.Api.Models.ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue> { ["v"] = deepText })));

        var wide = Arr(Enumerable.Range(0, 64).Select(_ => Arr(Enumerable.Range(0, 64).Select(_ => (ConsultInputValue)"x").ToArray())).ToArray());
        // 1 array + 63 × (1 + 64) = 4,096 values before the 64th inner array, which is the one over.
        Assert.Equal("Input 'v' element 63 is part of a structure with more than 4096 values.",
            Consultologist.Api.Jobs.ConsultGenerationJobs.ValidateRequest(new Consultologist.Api.Models.ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue> { ["v"] = wide })));
    }
}

/// <summary>v10 step (c) (#494): the expression grammar, its evaluator, its sentences, and the gate below 10.</summary>
public class WorkflowV10ConditionTests
{
    private static Dictionary<string, ConsultInputValue> Inputs(params (string Id, ConsultInputValue Value)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.Value, StringComparer.Ordinal);

    private static WorkflowConditionExpression Parse(string when)
    {
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var expression, out var error), error);
        return expression!;
    }

    private static bool Holds(string when, Dictionary<string, ConsultInputValue> inputs, IReadOnlyDictionary<string, string>? classifications = null) =>
        WorkflowResultConditions.Holds(Parse(when), inputs, classifications);

    private static ConsultInputValue Obj(params (string Id, ConsultInputValue Value)[] fields) =>
        ConsultInputValue.OfObject(fields.Select(f => new ConsultInputEntry(f.Id, f.Value)));

    // --- parsing

    [Theory]
    [InlineData("length_of_stay > 7 and billable", "length_of_stay > 7 and billable")]
    [InlineData("a or b and c", "a or b and c")]
    [InlineData("(a or b) and c", "(a or b) and c")]
    [InlineData("not a and b", "not a and b")]
    [InlineData("not (a and b)", "not (a and b)")]
    [InlineData("length_of_stay - 2 > 5", "length_of_stay - 2 > 5")]
    [InlineData("length_of_stay * 2 + 1 >= 7", "length_of_stay * 2 + 1 >= 7")]
    [InlineData("(length_of_stay + 1) * 2 >= 7", "(length_of_stay + 1) * 2 >= 7")]
    [InlineData("seen_on + 30 >= 2026-01-01", "seen_on + 30 >= 2026-01-01")]
    [InlineData("node:scope == in_scope", "node:scope == in_scope")]
    [InlineData("node:draft-section == in_scope", "node:draft-section == in_scope")]
    [InlineData("patient.contact.phone == x", "patient.contact.phone == x")]
    [InlineData("count(patient.notes) > 1", "count(patient.notes) > 1")]
    public void TheParserReadsEveryForm_AndWritesItBack(string when, string text)
    {
        Assert.Equal(text, Parse(when).Text);
    }

    [Fact]
    public void Precedence_IsAsStated()
    {
        // and binds tighter than or; not tighter than and; * before +.
        Assert.IsType<WorkflowOrExpression>(Parse("a or b and c"));
        Assert.IsType<WorkflowAndExpression>(((WorkflowOrExpression)Parse("a or b and c")).Right);
        Assert.IsType<WorkflowAndExpression>(Parse("not a and b"));
        Assert.IsType<WorkflowNotExpression>(((WorkflowAndExpression)Parse("not a and b")).Left);

        var clause = Parse("x + 2 * 3 > 1").SingleClause!;
        var left = (WorkflowBinaryTerm)clause.Left!;
        Assert.Equal('+', left.Op);
        Assert.Equal('*', ((WorkflowBinaryTerm)left.Right).Op);
    }

    [Theory]
    [InlineData("count(prior_notes) > -1", "-1")]
    [InlineData("seen_on > 2026-1-1", "2026-1-1")]
    public void TheWhitespaceRule_KeepsAnUnspacedMinusInTheLiteral(string when, string literal)
    {
        // The v9 corpus pins the sentences about these; they stay one clause.
        var clause = Parse(when).SingleClause;
        Assert.NotNull(clause);
        Assert.False(clause!.IsArithmetic);
        Assert.Equal(literal, clause.Literal);
    }

    [Fact]
    public void AV9Text_ParsesToTheV9Record()
    {
        var clause = Parse("patient.age >= 65").SingleClause!;
        Assert.Equal(("patient", "65", false, "age", false, ">=", 1, false, false),
            (clause.InputId, clause.Literal, clause.Negated, clause.Field, clause.IsCount, clause.Ordering, clause.PathDepth, clause.IsArithmetic, clause.IsNodeValue));
        Assert.Equal("patient.age", clause.Operand);
        // And a v9 text with no v10 token never enters the new parser: a bare
        // word on the right is a literal, as it always was.
        Assert.Equal("follow_up", Parse("encounter_kind == follow_up").SingleClause!.Literal);
        Assert.Equal("follow_up", Parse("encounter_kind == follow_up and billable").Leaves.First().Literal);
    }

    [Theory]
    [InlineData("a and", "ends where a clause was expected.")]
    [InlineData("(a or b", "is missing a ')'.")]
    [InlineData("a > ", "compares against nothing; write a value after the operator.")]
    [InlineData("a b", "'a b' is not an input id. Write 'when: <input>' or 'when: <input> == <value>'.")]
    [InlineData("a + > 1", "has '>' where a value was expected.")]
    [InlineData("""a == "x and b""", "literal '\"x' has a stray quote.")]
    public void ASyntaxError_IsNamed(string when, string expected)
    {
        Assert.False(WorkflowResultConditions.TryParseExpression(when, out _, out var error));
        Assert.Equal(expected, error);
    }

    // --- evaluation

    [Theory]
    [InlineData("length_of_stay > 7 and billable", 10, true, true)]
    [InlineData("length_of_stay > 7 and billable", 10, false, false)]
    [InlineData("length_of_stay > 7 or billable", 3, true, true)]
    [InlineData("length_of_stay > 7 or billable", 3, false, false)]
    [InlineData("not billable", 3, false, true)]
    [InlineData("not (length_of_stay > 7 and billable)", 10, true, false)]
    public void AndOrNot_Combine(string when, int stay, bool billable, bool expected)
    {
        Assert.Equal(expected, Holds(when, Inputs(("length_of_stay", ConsultInputValue.OfNumber(stay.ToString())), ("billable", ConsultInputValue.OfBoolean(billable)))));
    }

    [Fact]
    public void Absence_IsNeverHeld_AndStaysAbsentUnderNot()
    {
        var none = Inputs();
        Assert.False(Holds("billable", none));
        Assert.False(Holds("not billable", none));
        Assert.False(Holds("billable != true", none));
        Assert.False(Holds("not (billable != true)", none));
        Assert.False(Holds("billable and length_of_stay > 1", Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")))));
        // or: one held side is enough even beside an absent one.
        Assert.True(Holds("billable or length_of_stay > 1", Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")))));
        // or: an absent side beside a not-held side is not held — absence never rescues a clause.
        Assert.False(Holds("billable or length_of_stay > 9", Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")))));
        Assert.False(Holds("length_of_stay > 9 or billable", Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")))));
        // and: absent beside not-held is not held; not over that holds.
        Assert.True(Holds("not (billable and length_of_stay > 9)", Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")))));
    }

    [Theory]
    [InlineData("length_of_stay - 2 > 5", "8", true)]
    [InlineData("length_of_stay - 2 > 5", "7", false)]
    [InlineData("length_of_stay * 2 + 1 >= 7", "3", true)]
    [InlineData("(length_of_stay + 1) * 2 >= 7", "2.5", true)]
    [InlineData("length_of_stay / 2 > 3", "6.5", true)]
    [InlineData("- length_of_stay < 0", "1", true)]
    [InlineData("length_of_stay > count(prior_notes) + 1", "3", true)]
    public void Arithmetic_Computes(string when, string stay, bool expected)
    {
        Assert.Equal(expected, Holds(when, Inputs(("length_of_stay", ConsultInputValue.OfNumber(stay)), ("prior_notes", ConsultInputValue.OfArray(new[] { (ConsultInputValue)"a" })))));
    }

    [Fact]
    public void ADate_PlusOrMinusDays()
    {
        var seen = Inputs(("seen_on", "2026-01-10"));
        Assert.True(Holds("seen_on + 30 >= 2026-02-01", seen));
        Assert.False(Holds("seen_on - 30 >= 2026-01-01", seen));
        Assert.True(Holds("seen_on - 9 == 2026-01-01", seen));
    }

    [Fact]
    public void DivisionByZero_AnswersNothing()
    {
        // Absent, not false: `not` over it must not become a held clause.
        var inputs = Inputs(("length_of_stay", ConsultInputValue.OfNumber("5")), ("zero", ConsultInputValue.OfNumber("0")));
        Assert.False(Holds("length_of_stay / zero > 1", inputs));
        Assert.False(Holds("not (length_of_stay / zero > 1)", inputs));
    }

    [Fact]
    public void APath_ReadsThroughNestedStructure_AndCountsANestedArray()
    {
        var patient = Obj(("contact", Obj(("phone", "555"), ("preferred", "email"))), ("notes", ConsultInputValue.OfArray(new[] { (ConsultInputValue)"a", "b" })));
        var inputs = Inputs(("patient", patient));

        Assert.True(Holds("patient.contact.preferred == email", inputs));
        Assert.False(Holds("patient.contact.preferred != email", inputs));
        Assert.True(Holds("count(patient.notes) == 2", inputs));
        Assert.True(Holds("count(patient.missing) == 0", inputs)); // a path that stops short counts zero
        Assert.False(Holds("patient.contact.fax == x", inputs));   // absent
    }

    [Fact]
    public void AClassifiersValue_IsReadFromTheClassifications()
    {
        var decided = new Dictionary<string, string> { ["scope"] = "out_of_scope" };
        Assert.True(Holds("node:scope == out_of_scope", Inputs(), decided));
        Assert.False(Holds("node:scope == in_scope", Inputs(), decided));
        Assert.True(Holds("node:scope != in_scope", Inputs(), decided));
        // Absent until the boundary: never held, even negated.
        Assert.False(Holds("node:scope == in_scope", Inputs(), null));
        Assert.False(Holds("node:scope != in_scope", Inputs(), null));
        Assert.False(Holds("not (node:scope == in_scope)", Inputs(), null));
    }

    // --- Explain

    [Fact]
    public void OneClause_ReadsExactlyAsV9()
    {
        var inputs = Inputs(("billable", ConsultInputValue.OfBoolean(false)), ("patient", Obj(("age", ConsultInputValue.OfNumber("40")))));
        Assert.Equal("needs billable to be 'true'; it is 'false'", WorkflowResultConditions.Explain(Parse("billable == true"), inputs));
        Assert.Equal("needs patient.age to be >= 65; it is not", WorkflowResultConditions.Explain(Parse("patient.age >= 65"), inputs));
    }

    [Fact]
    public void ACompoundSentence_NamesEachClause_AndNeverThePatientsValue()
    {
        var inputs = Inputs(("length_of_stay", ConsultInputValue.OfNumber("3")), ("prior_notes", ConsultInputValue.OfArray(new[] { (ConsultInputValue)"a", "b" })));
        var sentence = WorkflowResultConditions.Explain(Parse("length_of_stay > 7 and count(prior_notes) > 0"), inputs);

        Assert.Equal("needs (length_of_stay to be > 7 and count(prior_notes) to be > 0); length_of_stay is not, count(prior_notes) is 2", sentence);
        Assert.DoesNotContain("3", sentence.Replace("count(prior_notes) is 2", string.Empty));
    }

    [Fact]
    public void AClassifiersValue_IsPrinted_ItIsDeclared()
    {
        var decided = new Dictionary<string, string> { ["scope"] = "out_of_scope" };
        Assert.Equal("needs node:scope to be 'in_scope'; it is 'out_of_scope'", WorkflowResultConditions.Explain(Parse("node:scope == in_scope"), Inputs(), decided));
        Assert.Equal("needs node:scope to be 'in_scope'; it is not decided", WorkflowResultConditions.Explain(Parse("node:scope == in_scope"), Inputs(), null));
    }

    [Fact]
    public void AnArithmeticClause_PrintsTheTermsByName_NeverAValue()
    {
        var sentence = WorkflowResultConditions.Explain(Parse("length_of_stay - 2 > 5"), Inputs(("length_of_stay", ConsultInputValue.OfNumber("4"))));
        Assert.Equal("needs length_of_stay - 2 to be > 5; it is not", sentence);
        Assert.Equal("needs length_of_stay - 2 to be > 5; it is not supplied", WorkflowResultConditions.Explain(Parse("length_of_stay - 2 > 5"), Inputs()));
    }

    // --- validation

    private static WorkflowPackageManifest At10(string when) => V9Fixtures.Conditional(when) with { SpecVersion = 10 };

    private static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) WithClassifierAt10(string when)
    {
        var (manifest, files) = V10Fixtures.WithClassifier(from: At10(when));
        return (manifest, files);
    }

    private static IEnumerable<string> ErrorsAt10(string when) => V10Fixtures.Validate(WithClassifierAt10(when)).Errors;

    [Theory]
    [InlineData("length_of_stay > 7 and billable")]
    [InlineData("(encounter_kind == follow_up or billable) and not length_of_stay > 30")]
    [InlineData("length_of_stay - 2 > 5")]
    [InlineData("seen_on + 30 >= 2026-01-01")]
    [InlineData("count(prior_notes) * 2 > length_of_stay")]
    [InlineData("node:scope == in_scope")]
    [InlineData("node:scope != out_of_scope and billable")]
    [InlineData("patient.age >= 65")]
    public void TheGrammarAccepts_At10(string when)
    {
        Assert.Empty(ErrorsAt10(when));
    }

    [Theory]
    [InlineData("node:nope == x", "reads 'node:nope', which is not a classifier (classifiers: scope).")]
    [InlineData("node:scope", "'node:scope' tests a classifier for truth; compare it to one of its values instead.")]
    [InlineData("node:scope > in_scope", "compares 'node:scope' with >; a classifier's value is compared with == or != only.")]
    [InlineData("node:scope == elsewhere", "compares 'node:scope' to 'elsewhere', which it does not declare (values: in_scope, out_of_scope).")]
    [InlineData("node:scope + 1 > 0", "uses 'node:scope' in arithmetic; a classifier's value is a symbol, not a number.")]
    [InlineData("encounter_kind + 1 > 0", "uses 'encounter_kind' in arithmetic, which is an enum; arithmetic applies to a number, a count or a date.")]
    [InlineData("length_of_stay + 1", "'length_of_stay + 1' is arithmetic with no comparison; write length_of_stay + 1 > 0.")]
    [InlineData("length_of_stay / 0 > 1", "divides by zero in 'length_of_stay / 0'.")]
    [InlineData("seen_on * 2 > 2026-01-01", "computes 'seen_on * 2', which is a date * a number; a date admits only ± whole days, and everything else is numbers.")]
    [InlineData("seen_on + 1 > 7", "compares a date 'seen_on + 1' with a number '7'; both sides of a comparison must be numbers, or both dates.")]
    [InlineData("length_of_stay + abc > 1", "reads undeclared input 'abc'")]
    [InlineData("length_of_stay + 1e3 > 1", "uses '1e3' in arithmetic, which is neither a plain decimal nor a date written YYYY-MM-DD.")]
    [InlineData("patient.age.years > 1", "reads field 'years' of 'patient.age', which is a number, not an object.")]
    [InlineData("patient.contact.phone == x", "reads field 'contact' of 'patient', which it does not declare (fields: family_name, age, sex).")]
    [InlineData("length_of_stay > 7 and urgency == high", "reads undeclared input 'urgency'")]
    [InlineData("billable and encounter_kind", "'encounter_kind' tests an enum for truth; compare it to one of its values instead.")]
    public void TheGrammarRejects_At10(string when, string expected)
    {
        var errors = ErrorsAt10(when).ToList();
        Assert.Contains(errors, e => e.Contains(expected));
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("length_of_stay > 7 and billable", "condition uses 'and', which requires specVersion 10.")]
    [InlineData("billable or length_of_stay > 7", "condition uses 'or', which requires specVersion 10.")]
    [InlineData("not billable", "condition uses 'not', which requires specVersion 10.")]
    [InlineData("length_of_stay - 2 > 5", "condition uses arithmetic, which requires specVersion 10.")]
    [InlineData("node:scope == in_scope", "condition reads 'node:scope', which requires specVersion 10.")]
    [InlineData("patient.age.x >= 1", "condition reads a path of 3 segments, which requires specVersion 10.")]
    public void BelowTen_EveryFormIsRefusedByName(string when, string expected)
    {
        Assert.Contains(V9Fixtures.Validate(V9Fixtures.Conditional(when)).Errors, e => e.Contains(expected));
    }
}
