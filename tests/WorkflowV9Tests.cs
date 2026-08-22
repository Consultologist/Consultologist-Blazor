using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// specVersion-9 fixtures: the v8 typed package at 9 with nothing else changed
/// — the migration § 12 proves — and a structured variant declaring one of
/// each new shape (package-format-v9-design.md § 4).
/// </summary>
public static class V9Fixtures
{
    public static WorkflowPackageManifest Minimal()
        => V8Fixtures.Typed() with { SpecVersion = 9 };

    /// <summary>A number, an object, an array of text and an array of objects.</summary>
    public static WorkflowPackageManifest Structured()
    {
        var inputs = new List<WorkflowInputSpec>(Minimal().Inputs!)
        {
            new("length_of_stay", "Length of stay (days)", Required: false, Type: WorkflowInputTypes.Number),
            new("patient", "Patient record", Required: false, Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
            {
                new("family_name", "Family name"),
                new("age", "Age", Type: WorkflowInputTypes.Number),
                new("sex", "Sex", Required: false, Type: WorkflowInputTypes.Enum, Values: new List<string> { "female", "male" })
            }),
            new("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text),
            new("medications", "Medications", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec>
                {
                    new("name", "Drug"),
                    new("dose", "Dose", Required: false)
                })
        };

        return Minimal() with { Inputs = inputs };
    }

    /// <summary>The structured fixture with one input's declaration replaced, or added.</summary>
    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Structured().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);

        if (index < 0)
        {
            inputs.Add(input);
        }
        else
        {
            inputs[index] = input;
        }

        return Structured() with { Inputs = inputs };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);

    /// <summary>
    /// The structured package with a scalar prompt node whose one variable
    /// `seen` is bound to the named source, and a hand-written template — the
    /// shape V8Fixtures.Reading uses to exercise the probe.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Reading(
        string template,
        string source)
    {
        var manifest = Structured();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("stamp", "prompts/stamp.md", new List<string> { "seen" })
        };

        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!);
        var reader = new WorkflowNodeSpec("stamp", "Stamping the note",
            Prompt: "stamp",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["seen"] = new(source)
            });

        var resultIndex = nodes.FindIndex(node => node.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string> { "node:section-instructions", "node:stamp" }
        };
        nodes.Insert(resultIndex, reader);

        manifest = manifest with { Prompts = prompts, Nodes = nodes };

        var files = V6Fixtures.Files(manifest);
        files["prompts/stamp.md"] = template;

        return (manifest, files);
    }
}

/// <summary>
/// #424: the publish-time probe hands Scriban the type the renderer will
/// (v9 § 4). Before this every variable not bound to a date or a boolean
/// was the string "placeholder", so {{ for note in prior_notes }} would have
/// been validated against a scalar and a correct template could not publish
/// — #357's defect, one version on.
/// </summary>
public class WorkflowV9ProbeTests
{
    private static WorkflowPackageValidator.ValidationResult ValidateReading(string template, string source)
    {
        var (manifest, files) = V9Fixtures.Reading(template, source);
        return WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas);
    }

    [Fact]
    public void ALoopOverAnArrayOfText_Publishes()
    {
        var result = ValidateReading("{{ for note in seen }}- {{ note }}\n{{ end }}", "input:prior_notes");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AFieldOfAnObject_Publishes()
    {
        var result = ValidateReading("{{ seen.family_name }}, aged {{ seen.age | math.format \"0.0\" }}", "input:patient");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AFieldOfEachObjectInAnArray_Publishes()
    {
        var result = ValidateReading("{{ for m in seen }}{{ m.name }} {{ m.dose }}{{ end }}", "input:medications");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ANumberFilter_OnANumberBoundVariable_Publishes()
    {
        var result = ValidateReading("Stayed {{ seen | math.format \"0.0\" }} days", "input:length_of_stay");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ADateFilter_OnAnArrayBoundVariable_StillFails()
    {
        // The type comes from the binding: an array is not a date, and
        // formatting it as one would throw at the job.
        var result = ValidateReading("Seen {{ seen | date.to_string \"%d %B %Y\" }}", "input:prior_notes");

        Assert.Contains(result.Errors, error => error.Contains("failed strict rendering", StringComparison.Ordinal));
    }

    [Fact]
    public void AnArrayProbesAsTwoElements_AndAnObjectCarriesItsFields()
    {
        // Two rather than one, so a template that assumes a singleton fails
        // the probe rather than the job. Pinned on the value itself, because
        // the validator throws away what a template renders.
        var notes = V9Fixtures.Structured().Inputs!.Single(input => input.Id == "prior_notes");
        var medications = V9Fixtures.Structured().Inputs!.Single(input => input.Id == "medications");
        var patient = V9Fixtures.Structured().Inputs!.Single(input => input.Id == "patient");

        var notesProbe = Assert.IsType<Scriban.Runtime.ScriptArray>(WorkflowPackageValidator.ProbeValue(notes));
        Assert.Equal(2, notesProbe.Count);
        Assert.All(notesProbe, element => Assert.Equal("placeholder", element));

        var medicationsProbe = Assert.IsType<Scriban.Runtime.ScriptArray>(WorkflowPackageValidator.ProbeValue(medications));
        Assert.Equal(2, medicationsProbe.Count);
        Assert.All(medicationsProbe, element => Assert.Contains("dose", Assert.IsType<Scriban.Runtime.ScriptObject>(element).Keys));

        var patientProbe = Assert.IsType<Scriban.Runtime.ScriptObject>(WorkflowPackageValidator.ProbeValue(patient));
        Assert.Equal(1.5m, patientProbe["age"]);
        Assert.Equal("placeholder", patientProbe["sex"]);
    }
}

/// <summary>
/// #424: the v9 declaration — number, object and array, with `items` for an
/// array and `fields` for an object — and the closure the validator keeps
/// over it. Each rule with its own sentence, in both directions.
/// </summary>
public class WorkflowV9DeclarationTests
{
    private static IEnumerable<string> Errors(WorkflowPackageManifest manifest) => V9Fixtures.Validate(manifest).Errors;

    [Fact]
    public void MinimalV9_IsValid_WithNothingNewDeclared()
    {
        // The migration story: specVersion 9 and nothing else.
        var result = V9Fixtures.Validate(V9Fixtures.Minimal());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void EveryNewShape_IsAccepted()
    {
        var result = V9Fixtures.Validate(V9Fixtures.Structured());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    // ---- the version gates ------------------------------------------------

    [Theory]
    [InlineData(WorkflowInputTypes.Number)]
    [InlineData(WorkflowInputTypes.Object)]
    [InlineData(WorkflowInputTypes.Array)]
    public void AV9TypeOnAV8Manifest_NamesTheVersionItNeeds(string type)
    {
        // Not "unknown type": the name exists, one version up. Same posture as
        // "declares a type, which requires specVersion 8".
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen", Type: type));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e == $"Input 'seen_on' declares type '{type}', which requires specVersion 9.");
    }

    [Fact]
    public void ItemsOrFieldsOnAV8Manifest_NameTheVersionTheyNeed()
    {
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen",
            Items: WorkflowInputTypes.Text, Fields: new List<WorkflowFieldSpec> { new("a", "A") }));
        var errors = V8Fixtures.Validate(manifest).Errors;

        Assert.Contains("Input 'seen_on' declares items, which requires specVersion 9.", errors);
        Assert.Contains("Input 'seen_on' declares fields, which requires specVersion 9.", errors);
    }

    [Fact]
    public void ItemsOnAV7Manifest_IsRefusedToo()
    {
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft", Items: WorkflowInputTypes.Text) }
        };

        Assert.Contains("Input 'consult_draft' declares items, which requires specVersion 9.", V7Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AV8Manifest_StillListsFourAcceptedTypes()
    {
        // The published conformance suite records this sentence verbatim; the
        // type set is keyed by version so it does not move under v8.
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen", Type: "timestamp"));

        Assert.Contains(
            "Input 'seen_on' declares unknown type 'timestamp' (accepted: text, date, enum, boolean).",
            V8Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AV9Manifest_ListsSevenAcceptedTypes()
    {
        var manifest = V9Fixtures.WithInput(new("seen_on", "Date seen", Type: "timestamp"));

        Assert.Contains(
            "Input 'seen_on' declares unknown type 'timestamp' (accepted: text, date, enum, boolean, number, object, array).",
            Errors(manifest));
    }

    // ---- items ------------------------------------------------------------

    [Fact]
    public void AnArrayWithoutItems_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes", Type: WorkflowInputTypes.Array));

        Assert.Contains("Input 'prior_notes' is type 'array' and must declare items.", Errors(manifest));
    }

    [Fact]
    public void AnArrayOfArrays_IsRefusedByName()
    {
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes",
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Array));

        Assert.Contains(Errors(manifest), e => e.Contains("structure is one level deep, so an array may not hold arrays"));
    }

    [Fact]
    public void AnUnknownItemsType_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes",
            Type: WorkflowInputTypes.Array, Items: "timestamp"));

        Assert.Contains(
            "Input 'prior_notes' declares unknown items type 'timestamp' (accepted: text, date, enum, boolean, number, object).",
            Errors(manifest));
    }

    [Fact]
    public void ItemsOnANonArray_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("length_of_stay", "Length of stay",
            Type: WorkflowInputTypes.Number, Items: WorkflowInputTypes.Text));

        Assert.Contains("Input 'length_of_stay' is type 'number' and may not declare items.", Errors(manifest));
    }

    // ---- fields -----------------------------------------------------------

    [Fact]
    public void AnObjectWithoutFields_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object));

        Assert.Contains("Input 'patient' is type 'object' and must declare fields.", Errors(manifest));
    }

    [Fact]
    public void AnObjectWithNoFieldsAtAll_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record",
            Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>()));

        Assert.Contains("Input 'patient' is type 'object' and must declare fields.", Errors(manifest));
    }

    [Fact]
    public void AnArrayOfObjectsWithoutFields_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("medications", "Medications",
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object));

        Assert.Contains("Input 'medications' is an array of objects and must declare fields.", Errors(manifest));
    }

    [Fact]
    public void FieldsOnANonObject_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes",
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text,
            Fields: new List<WorkflowFieldSpec> { new("a", "A") }));

        Assert.Contains("Input 'prior_notes' is type 'array' and may not declare fields.", Errors(manifest));
    }

    [Theory]
    [InlineData(WorkflowInputTypes.Object)]
    [InlineData(WorkflowInputTypes.Array)]
    public void AFieldHoldingStructure_IsRefused(string type)
    {
        // The one-level bound, from the other side.
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("history", "History", Type: type) }));

        Assert.Contains(Errors(manifest),
            e => e.StartsWith("Input 'patient' field 'history' is type", StringComparison.Ordinal)
                 && e.Contains("structure is one level deep, so a field holds a scalar"));
    }

    [Fact]
    public void AFieldWithAnUnknownType_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("age", "Age", Type: "integer") }));

        Assert.Contains(
            "Input 'patient' field 'age' declares unknown type 'integer' (accepted: text, date, enum, boolean, number).",
            Errors(manifest));
    }

    [Theory]
    [InlineData("Family Name")]
    [InlineData("1st")]
    [InlineData("")]
    public void AFieldIdBreakingTheIdRule_IsRefused(string id)
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new(id, "Label") }));

        Assert.Contains(Errors(manifest), e => e.Contains($"field id '{id}' must be snake_case"));
    }

    [Fact]
    public void DuplicateFieldIds_AreRefused()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("age", "Age"), new("age", "Age again") }));

        Assert.Contains("Input 'patient' declares duplicate field id 'age'.", Errors(manifest));
    }

    [Fact]
    public void AFieldWithoutALabel_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("age", " ") }));

        Assert.Contains("Input 'patient' field 'age' has no label.", Errors(manifest));
    }

    [Fact]
    public void AnEnumField_KeepsTheEnumRules()
    {
        var one = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("sex", "Sex", Type: WorkflowInputTypes.Enum, Values: new List<string> { "only" }) }));
        var none = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("sex", "Sex", Type: WorkflowInputTypes.Enum) }));
        var stray = V9Fixtures.WithInput(new("patient", "Patient record", Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("age", "Age", Type: WorkflowInputTypes.Number, Values: new List<string> { "a", "b" }) }));

        Assert.Contains(Errors(one), e => e.StartsWith("Input 'patient' field 'sex' declares one enum value", StringComparison.Ordinal));
        Assert.Contains("Input 'patient' field 'sex' is type 'enum' and must declare values.", Errors(none));
        Assert.Contains("Input 'patient' field 'age' is type 'number' and may not declare values.", Errors(stray));
    }

    // ---- values -----------------------------------------------------------

    [Fact]
    public void AnArrayOfEnums_CarriesItsValuesOnTheInput()
    {
        var ok = V9Fixtures.WithInput(new("kinds", "Kinds", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Enum, Values: new List<string> { "a", "b" }));
        var missing = V9Fixtures.WithInput(new("kinds", "Kinds", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Enum));

        Assert.True(V9Fixtures.Validate(ok).IsValid, string.Join(" | ", Errors(ok)));
        Assert.Contains("Input 'kinds' is type 'enum' and must declare values.", Errors(missing));
    }

    [Fact]
    public void ValuesOnAnArrayOfText_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text, Values: new List<string> { "a", "b" }));

        Assert.Contains("Input 'prior_notes' is type 'array' and may not declare values.", Errors(manifest));
    }

    [Fact]
    public void ValuesOnANumber_IsRefused()
    {
        var manifest = V9Fixtures.WithInput(new("length_of_stay", "Length of stay",
            Type: WorkflowInputTypes.Number, Values: new List<string> { "a", "b" }));

        Assert.Contains("Input 'length_of_stay' is type 'number' and may not declare values.", Errors(manifest));
    }

    // ---- the email door ---------------------------------------------------

    [Theory]
    [InlineData(WorkflowInputTypes.Number)]
    [InlineData(WorkflowInputTypes.Object)]
    public void ARequiredNumberOrObject_WarnsThatEmailCannotStartIt(string type)
    {
        // #370's shape, extended: the door supplies text, and neither of these
        // is text. A warning — this validator runs at load.
        var fields = type == WorkflowInputTypes.Object ? new List<WorkflowFieldSpec> { new("a", "A") } : null;
        var result = V9Fixtures.Validate(V9Fixtures.WithInput(new("extra", "Extra", Required: true, Type: type, Fields: fields)));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains($"'extra' is a required {type}", StringComparison.Ordinal));
    }

    [Fact]
    public void ARequiredArrayOfText_WarnsNothing()
    {
        // Per the record (§ 4, § 7): attachments fill an array of text, so the
        // email door can reach it — once #428 lets a slot hold several.
        var result = V9Fixtures.Validate(V9Fixtures.WithInput(new("prior_notes", "Prior notes",
            Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text)));

        Assert.DoesNotContain(result.Warnings, w => w.Contains("'prior_notes'", StringComparison.Ordinal));
    }
}

/// <summary>
/// #425: the variable → declaration map the probe and the activity share.
/// </summary>
public class WorkflowVariableDeclarationsTests
{
    [Fact]
    public void AVariableBoundToAConvertedInput_CarriesItsDeclaration()
    {
        var (manifest, _) = V9Fixtures.Reading("{{ seen }}", "input:patient");

        var declarations = WorkflowVariableDeclarations.For(manifest);

        Assert.Equal("patient", declarations["seen"].Id);
        Assert.Equal(3, declarations["seen"].Fields!.Count);
    }

    [Theory]
    [InlineData("input:consult_draft")]
    [InlineData("input:encounter_kind")]
    [InlineData("item:name")]
    public void AVariableBoundToAStringSource_IsNotTyped(string source)
    {
        // Text and enum are strings at runtime; so is anything not an input.
        var (manifest, _) = V9Fixtures.Reading("{{ seen }}", source);

        Assert.DoesNotContain("seen", WorkflowVariableDeclarations.For(manifest).Keys);
    }

    [Fact]
    public void AVariableTwoNodesBindDifferently_StaysAString()
    {
        // The v8 rule, kept: a shared prompt's variable is typed only when
        // every binding that reaches it agrees.
        var (manifest, _) = V8Fixtures.Reading("{{ seen }}", "input:seen_on", alsoBoundBy: "item:name");

        Assert.DoesNotContain("seen", WorkflowVariableDeclarations.For(manifest).Keys);
    }

    [Fact]
    public void TheProbeAndTheActivity_UseTheSameMap()
    {
        // What publishes is what runs: a field typed as a date by the probe is
        // a date at the job. Rendered here exactly as RunPromptNodeActivity
        // does, with the declarations the package carries.
        var (manifest, files) = V9Fixtures.Reading("Seen {{ seen.seen_on | date.to_string \"%d %B %Y\" }}", "input:patient");
        var fields = new List<WorkflowFieldSpec>(manifest.Inputs!.Single(i => i.Id == "patient").Fields!)
        {
            new("seen_on", "Seen on", Required: false, Type: WorkflowInputTypes.Date)
        };
        var inputs = manifest.Inputs!.Select(i => i.Id == "patient" ? i with { Fields = fields } : i).ToList();
        manifest = manifest with { Inputs = inputs };

        var validation = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas);
        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));

        var rendered = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("stamp", files["prompts/stamp.md"], new[] { "seen" }, null),
            new Dictionary<string, string> { ["seen"] = """{"family_name":"Smith","age":40,"seen_on":"2026-08-10"}""" },
            new Dictionary<string, string> { ["seen"] = WorkflowInputTypes.Object },
            WorkflowVariableDeclarations.For(manifest));

        Assert.Equal("Seen 10 August 2026", rendered);
    }
}
