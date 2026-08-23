using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
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

    /// <summary>The v8 conditional package, at v9, over the structured inputs.</summary>
    public static WorkflowPackageManifest Conditional(string? when)
        => V8Fixtures.Conditional(when) with { SpecVersion = 9, Inputs = Structured().Inputs };

    /// <summary>
    /// #426: the structured package with a node fanning over the caller's
    /// prior notes — `forEach: input:prior_notes`, `note: item:value` —
    /// aggregated into the result beside the standards chain. The fanned
    /// input is required, as a fanned input should be.
    /// </summary>
    public static WorkflowPackageManifest Fanned(string forEach = "input:prior_notes", string binding = "item:value")
    {
        // The fanned input is required, as a fanned input should be; the
        // others keep Structured()'s optional declarations.
        var fannedId = WorkflowInputFans.IsInputFan(forEach) ? WorkflowInputFans.InputIdOf(forEach) : null;
        var manifest = Structured() with
        {
            Inputs = Structured().Inputs!
                .Select(input => input.Id == fannedId ? input with { Required = true } : input)
                .ToList()
        };
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("summarise-note", "prompts/summarise-note.md", new List<string> { "note" })
        };

        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!);
        var fan = new WorkflowNodeSpec("summarise-note", "Summarising a prior note",
            Prompt: "summarise-note",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["note"] = new(binding)
            },
            ForEach: forEach);

        var resultIndex = nodes.FindIndex(node => node.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string> { "node:section-instructions", "node:summarise-note" }
        };
        nodes.Insert(resultIndex, fan);

        return manifest with { Prompts = prompts, Nodes = nodes };
    }

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

/// <summary>
/// #426: a node may fan over a caller-supplied array. The validator admits
/// `forEach: input:&lt;id&gt;` for an array input from specVersion 9, and an
/// input fan's items carry one shape — id, name, value.
/// </summary>
public class WorkflowV9FanTests
{
    private static IEnumerable<string> Errors(WorkflowPackageManifest manifest) => V9Fixtures.Validate(manifest).Errors;

    [Fact]
    public void AFanOverAnArrayInput_IsValid()
    {
        var result = V9Fixtures.Validate(V9Fixtures.Fanned());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AFanOverAnArrayOfObjects_IsValid()
    {
        var result = V9Fixtures.Validate(V9Fixtures.Fanned(forEach: "input:medications"));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AFanOverANonArrayInput_IsRefused()
    {
        Assert.Contains(
            "Node 'summarise-note' forEach 'input:length_of_stay' fans input 'length_of_stay', which is a number; only an array can be fanned.",
            Errors(V9Fixtures.Fanned(forEach: "input:length_of_stay")));
    }

    [Fact]
    public void AFanOverAnUndeclaredInput_IsRefused()
    {
        Assert.Contains(
            "Node 'summarise-note' forEach 'input:nothing' fans undeclared input 'nothing'.",
            Errors(V9Fixtures.Fanned(forEach: "input:nothing")));
    }

    [Fact]
    public void AnInputFanOnAV8Manifest_ReadsAsItAlwaysDid()
    {
        // Below 9 the sentence is unchanged: an input is not a source that
        // version can fan, and the conformance suite pins the wording.
        var manifest = V9Fixtures.Fanned() with { SpecVersion = 8 };

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("forEach 'input:prior_notes' must be a data: collection reference."));
    }

    [Fact]
    public void AnItemFieldAnInputFanDoesNotCarry_IsRefused()
    {
        // One shape for every caller element: the element is item:value.
        Assert.Contains(
            "Node 'summarise-note' binds 'note' to unknown item field 'text' (an input fan's items carry: id, name, value).",
            Errors(V9Fixtures.Fanned(binding: "item:text")));
    }

    [Theory]
    [InlineData("item:id")]
    [InlineData("item:name")]
    public void TheMintedItemFields_AreBindable(string binding)
    {
        var result = V9Fixtures.Validate(V9Fixtures.Fanned(binding: binding));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AFannedOptionalInput_WarnsAtPublish()
    {
        // Absent or empty, a fanned input refuses the job at start by name.
        // Correct, and worth hearing at publish rather than on every consult.
        var manifest = V9Fixtures.Fanned();
        var inputs = manifest.Inputs!.Select(i => i.Id == "prior_notes" ? i with { Required = false } : i).ToList();

        var result = V9Fixtures.Validate(manifest with { Inputs = inputs });

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, w => w.StartsWith("Input 'prior_notes' is optional but node 'summarise-note' fans it", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInputFansItems_CarryOneShape()
    {
        // id is the index, name is the label and ordinal — never the element,
        // which is patient data and reaches history events and the rail —
        // and value is the element: a scalar's text, an object's carrier.
        var notes = new WorkflowInputSpec("prior_notes", "Prior notes", Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text);
        var items = WorkflowInputFans.Items(notes, ConsultInputValue.OfArray(new[]
        {
            ConsultInputValue.OfText("Seen in clinic; BP 150/95."),
            ConsultInputValue.OfText("Follow-up; BP 130/85.")
        }));

        Assert.Equal(new[] { "0", "1" }, items.Select(item => item["id"]));
        Assert.Equal(new[] { "Prior notes 1", "Prior notes 2" }, items.Select(item => item["name"]));
        Assert.Equal("Follow-up; BP 130/85.", items[1]["value"]);
        Assert.All(items, item => Assert.DoesNotContain("BP", item["name"], StringComparison.Ordinal));

        var meds = new WorkflowInputSpec("medications", "Medications", Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("name", "Drug"), new("dose", "Dose") });
        var element = ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("name", ConsultInputValue.OfText("metformin")),
            new ConsultInputEntry("dose", ConsultInputValue.OfText("500 mg"))
        });
        var medItems = WorkflowInputFans.Items(meds, ConsultInputValue.OfArray(new[] { element }));

        Assert.Equal(element, ConsultInputValue.FromJson(medItems[0]["value"]));
        Assert.Equal("Medications 1", medItems[0]["name"]);
        Assert.Empty(WorkflowInputFans.Items(notes, null));
        Assert.Empty(WorkflowInputFans.Items(notes, ConsultInputValue.OfText("not an array")));
    }
}

/// <summary>
/// #426: what an input fan's item carries, typed. The element reaches its
/// node as item:value — a string in the item map, untagged — and the
/// array's declaration, one level down, says what it is.
/// </summary>
public class WorkflowV9FanRenderingTests
{
    [Fact]
    public void AnElementOfAnArrayOfObjects_RendersItsFields()
    {
        var manifest = V9Fixtures.Fanned(forEach: "input:medications");
        var element = ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("name", ConsultInputValue.OfText("metformin")),
            new ConsultInputEntry("dose", ConsultInputValue.OfText("500 mg"))
        });
        var item = WorkflowInputFans.Items(manifest.Inputs!.Single(i => i.Id == "medications"), ConsultInputValue.OfArray(new[] { element }))[0];

        // The resolver hands the node the carrier, as a string, as it does
        // every item field.
        var node = ConsultGenerationJobStarter.DescribeNode(manifest.Nodes!.Single(n => n.Id == "summarise-note"), null);
        var variables = ConsultNodeVariableResolver.Resolve(node, new Dictionary<string, string>(), item, null,
            new Dictionary<string, ConsultNodeDescriptor>(), new Dictionary<string, NodeRunResult>());
        Assert.Equal(element.AsJson(), variables["note"]);

        // And the renderer, given the declarations the activity has, makes it an object.
        var rendered = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("summarise-note", "{{ note.name }} at {{ note.dose }}", new[] { "note" }, null),
            variables,
            variableTypes: null,
            WorkflowVariableDeclarations.For(manifest));

        Assert.Equal("metformin at 500 mg", rendered);
    }

    [Fact]
    public void AnElementOfAnArrayOfDates_FormatsAsADate()
    {
        var manifest = V9Fixtures.Fanned();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Select(i => i.Id == "prior_notes" ? i with { Items = WorkflowInputTypes.Date } : i).ToList()
        };

        var rendered = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("summarise-note", "Seen {{ note | date.to_string \"%d %B %Y\" }}", new[] { "note" }, null),
            new Dictionary<string, string> { ["note"] = "2026-08-10" },
            variableTypes: null,
            WorkflowVariableDeclarations.For(manifest));

        Assert.Equal("Seen 10 August 2026", rendered);
    }

    [Fact]
    public void AnElementOfAnArrayOfText_NeedsNoDeclaration()
    {
        var declarations = WorkflowVariableDeclarations.For(V9Fixtures.Fanned());

        Assert.DoesNotContain("note", declarations.Keys);
    }

    [Fact]
    public void TheProbe_AgreesWithTheFan()
    {
        // Publish-time: a template reaching into an object element validates,
        // because the probe types item:value from the same declaration.
        var manifest = V9Fixtures.Fanned(forEach: "input:medications");
        var files = V6Fixtures.Files(manifest);
        files["prompts/summarise-note.md"] = "{{ note.name }} at {{ note.dose }}";

        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

/// <summary>
/// #427: the widened grammar, evaluated. Six operators, a path into an
/// object, count() of an array, array truthiness — and a sentence that
/// never prints the patient's number, date or field.
/// </summary>
public class WorkflowV9ConditionEvaluationTests
{
    private static Dictionary<string, ConsultInputValue> Inputs(params (string Id, ConsultInputValue Value)[] pairs)
        => pairs.ToDictionary(p => p.Id, p => p.Value, StringComparer.Ordinal);

    private static WorkflowResultCondition Parse(string when)
    {
        Assert.True(WorkflowResultConditions.TryParse(when, out var condition, out var error), error);
        return condition!;
    }

    private static ConsultInputValue Patient(int age, string? sex = "female") =>
        ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("family_name", ConsultInputValue.OfText("Smith")),
            new ConsultInputEntry("age", ConsultInputValue.OfNumber(age.ToString())),
            new ConsultInputEntry("sex", sex is null ? ConsultInputValue.NullElement : ConsultInputValue.OfText(sex))
        });

    private static ConsultInputValue Notes(params string[] notes) =>
        ConsultInputValue.OfArray(notes.Select(ConsultInputValue.OfText));

    // ---- the parser ----

    [Theory]
    [InlineData("length_of_stay > 7", "length_of_stay", null, false, ">", "7")]
    [InlineData("length_of_stay >= 7", "length_of_stay", null, false, ">=", "7")]
    [InlineData("seen_on <= 2026-01-01", "seen_on", null, false, "<=", "2026-01-01")]
    [InlineData("patient.age < 65", "patient", "age", false, "<", "65")]
    [InlineData("patient.sex == female", "patient", "sex", false, null, "female")]
    [InlineData("count(prior_notes) > 1", "prior_notes", null, true, ">", "1")]
    [InlineData("count( prior_notes ) == 0", "prior_notes", null, true, null, "0")]
    [InlineData("prior_notes", "prior_notes", null, false, null, null)]
    public void TheParserReadsEveryForm(string when, string input, string? field, bool isCount, string? ordering, string? literal)
    {
        var condition = Parse(when);

        Assert.Equal(input, condition.InputId);
        Assert.Equal(field, condition.Field);
        Assert.Equal(isCount, condition.IsCount);
        Assert.Equal(ordering, condition.Ordering);
        Assert.Equal(literal, condition.Literal);
    }

    [Theory]
    [InlineData("patient.age.x >= 1")]
    [InlineData("count(prior_notes")]
    [InlineData("count(Prior Notes) > 1")]
    [InlineData("patient..age == 1")]
    public void AMalformedOperand_IsNotAnInputId(string when)
    {
        Assert.False(WorkflowResultConditions.TryParse(when, out _, out var error));
        Assert.Contains("is not an input id", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCharacterOperators_AreReadBeforeOneCharacterOnes()
    {
        // ">=" is never ">" followed by a literal beginning with "=".
        var condition = Parse("length_of_stay >= 7");

        Assert.Equal(">=", condition.Ordering);
        Assert.Equal("7", condition.Literal);
    }

    // ---- Holds ----

    [Theory]
    [InlineData("length_of_stay > 7", "10", true)]
    [InlineData("length_of_stay > 7", "7", false)]
    [InlineData("length_of_stay >= 7", "7", true)]
    [InlineData("length_of_stay < 7", "6.5", true)]
    [InlineData("length_of_stay <= 7", "7.01", false)]
    [InlineData("length_of_stay == 7", "7.0", true)]
    [InlineData("length_of_stay != 7", "7.0", false)]
    public void ANumber_Compares(string when, string supplied, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(Parse(when), Inputs(("length_of_stay", ConsultInputValue.OfNumber(supplied)))));

    [Theory]
    [InlineData("seen_on >= 2026-01-01", "2026-08-10", true)]
    [InlineData("seen_on < 2026-01-01", "2026-08-10", false)]
    [InlineData("seen_on == 2026-08-10", "2026-08-10", true)]
    [InlineData("seen_on != 2026-08-10", "2026-08-10", false)]
    public void ADate_Compares(string when, string supplied, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(Parse(when), Inputs(("seen_on", supplied))));

    [Theory]
    [InlineData("patient.age >= 65", 70, true)]
    [InlineData("patient.age >= 65", 40, false)]
    [InlineData("patient.sex == female", 40, true)]
    [InlineData("patient.sex != female", 40, false)]
    public void APath_ReadsOneField(string when, int age, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(Parse(when), Inputs(("patient", Patient(age)))));

    [Fact]
    public void APathIntoAnAbsentObject_OrAMissingOrNullField_DoesNotHold()
    {
        Assert.False(WorkflowResultConditions.Holds(Parse("patient.age >= 65"), Inputs()));
        Assert.False(WorkflowResultConditions.Holds(Parse("patient.nickname == x"), Inputs(("patient", Patient(70)))));
        Assert.False(WorkflowResultConditions.Holds(Parse("patient.sex == female"), Inputs(("patient", Patient(70, sex: null)))));
        Assert.False(WorkflowResultConditions.Holds(Parse("patient.sex != female"), Inputs(("patient", Patient(70, sex: null)))));
        // A path into something that is not an object answers nothing.
        Assert.False(WorkflowResultConditions.Holds(Parse("patient.age >= 65"), Inputs(("patient", "text"))));
    }

    [Theory]
    [InlineData("count(prior_notes) > 1", 2, true)]
    [InlineData("count(prior_notes) > 1", 1, false)]
    [InlineData("count(prior_notes) == 0", 0, true)]
    [InlineData("count(prior_notes) <= 3", 3, true)]
    public void Count_IsTheNumberOfEntries(string when, int entries, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(
            Parse(when), Inputs(("prior_notes", Notes(Enumerable.Repeat("n", entries).ToArray())))));

    [Fact]
    public void CountOfAnAbsentArray_IsZero_TheOneExceptionToAbsence()
    {
        // "No entries supplied" and "an empty list supplied" answer the same
        // clinical question (v9 § 6).
        Assert.True(WorkflowResultConditions.Holds(Parse("count(prior_notes) == 0"), Inputs()));
        Assert.False(WorkflowResultConditions.Holds(Parse("count(prior_notes) > 0"), Inputs()));
        Assert.True(WorkflowResultConditions.Holds(Parse("count(prior_notes) < 1"), Inputs()));
        // count() of something that is not a list answers nothing.
        Assert.False(WorkflowResultConditions.Holds(Parse("count(prior_notes) == 0"), Inputs(("prior_notes", "text"))));
    }

    [Fact]
    public void TheBareForm_OnAnArray_IsNonEmpty()
    {
        Assert.True(WorkflowResultConditions.Holds(Parse("prior_notes"), Inputs(("prior_notes", Notes("a")))));
        Assert.False(WorkflowResultConditions.Holds(Parse("prior_notes"), Inputs(("prior_notes", Notes()))));
        Assert.False(WorkflowResultConditions.Holds(Parse("prior_notes"), Inputs()));
    }

    // ---- Explain ----

    [Theory]
    [InlineData("length_of_stay > 7", "needs length_of_stay to be > 7; it is not")]
    [InlineData("seen_on >= 2026-01-01", "needs seen_on to be >= 2026-01-01; it is not")]
    [InlineData("seen_on == 2026-01-01", "needs seen_on to be '2026-01-01'; it is not")]
    [InlineData("patient.age >= 65", "needs patient.age to be >= 65; it is not")]
    [InlineData("patient.sex == male", "needs patient.sex to be 'male'; it is not")]
    public void TheSentence_NeverPrintsThePatientsValue(string when, string expected)
    {
        // A number, a date or a field's value is the patient's, and this sentence
        // reaches History and the email reply. It says what was needed and that
        // it was not met — never what was found.
        var inputs = Inputs(
            ("length_of_stay", ConsultInputValue.OfNumber("3")),
            ("seen_on", "2025-12-31"),
            ("patient", Patient(40)));

        var explained = WorkflowResultConditions.Explain(Parse(when), inputs);

        Assert.Equal(expected, explained);
        Assert.DoesNotContain("3", explained.Replace("65", "").Replace("2026-01-01", ""), StringComparison.Ordinal);
        Assert.DoesNotContain("2025-12-31", explained, StringComparison.Ordinal);
        Assert.DoesNotContain("40", explained, StringComparison.Ordinal);
        Assert.DoesNotContain("female", explained, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentence_PrintsACount_AndAnArraysEmptiness()
    {
        // A count of entries is not content.
        Assert.Equal("needs count(prior_notes) to be > 1; it is 1",
            WorkflowResultConditions.Explain(Parse("count(prior_notes) > 1"), Inputs(("prior_notes", Notes("secret")))));
        Assert.Equal("needs count(prior_notes) to be > 1; it is 0",
            WorkflowResultConditions.Explain(Parse("count(prior_notes) > 1"), Inputs()));
        Assert.Equal("needs prior_notes to be non-empty; it is empty",
            WorkflowResultConditions.Explain(Parse("prior_notes"), Inputs(("prior_notes", Notes()))));
        Assert.Equal("needs prior_notes to be non-empty; it is 2 entries",
            WorkflowResultConditions.Explain(Parse("prior_notes"), Inputs(("prior_notes", Notes("a", "b")))));
        Assert.Equal("needs prior_notes to be true; it is not supplied",
            WorkflowResultConditions.Explain(Parse("prior_notes"), Inputs()));
    }

    [Fact]
    public void TheV8Sentences_AreUnchanged()
    {
        // The email door quotes these back; two client tests pin them verbatim.
        Assert.Equal("needs billable to be 'true'; it is 'false'",
            WorkflowResultConditions.Explain(Parse("billable == true"), Inputs(("billable", ConsultInputValue.OfBoolean(false)))));
        Assert.Equal("needs billable to be true; it is 'false'",
            WorkflowResultConditions.Explain(Parse("billable"), Inputs(("billable", ConsultInputValue.OfBoolean(false)))));
        Assert.Equal("needs encounter_kind to be not 'follow_up'; it is 'follow_up'",
            WorkflowResultConditions.Explain(Parse("encounter_kind != follow_up"), Inputs(("encounter_kind", "follow_up"))));
        Assert.Equal("needs billable to be true; it is not supplied",
            WorkflowResultConditions.Explain(Parse("billable"), Inputs()));
    }
}

/// <summary>
/// #427, v9 § 6: the narrowing, where the declaration is known. The
/// operand is resolved to a type — the input's, one field's, or a count —
/// and the operator and literal are held to it.
/// </summary>
public class WorkflowV9ConditionValidationTests
{
    private static IEnumerable<string> Errors(string when) => V9Fixtures.Validate(V9Fixtures.Conditional(when)).Errors;

    [Theory]
    // v8's forms still hold.
    [InlineData("billable")]
    [InlineData("encounter_kind == follow_up")]
    [InlineData("encounter_kind != new_patient")]
    [InlineData("billable == false")]
    // Ordering for a number and a date.
    [InlineData("length_of_stay > 7")]
    [InlineData("length_of_stay <= 7.5")]
    [InlineData("length_of_stay == 0")]
    [InlineData("seen_on >= 2026-01-01")]
    [InlineData("seen_on != 2026-01-01")]
    // A path into one field of an object.
    [InlineData("patient.age >= 65")]
    [InlineData("patient.sex == female")]
    // A count, and the bare form on an array.
    [InlineData("count(prior_notes) > 1")]
    [InlineData("count(medications) == 0")]
    [InlineData("prior_notes")]
    public void TheGrammarAccepts(string when)
    {
        Assert.Empty(Errors(when));
    }

    [Theory]
    // The operator is held to the operand's type.
    [InlineData("encounter_kind > follow_up", "compares 'encounter_kind' with >, which is a enum; ordering operators apply to a number or a date.")]
    [InlineData("billable >= true", "compares 'billable' with >=, which is a boolean; ordering operators apply to a number or a date.")]
    [InlineData("patient.sex < female", "compares 'patient.sex' with <, which is a enum")]
    // Text stays incomparable, on the input and on a field.
    [InlineData("consult_draft == urgent", "reads 'consult_draft', which is a text: a text input cannot be tested.")]
    [InlineData("patient.family_name == Smith", "reads 'patient.family_name', which is a text: a text input cannot be tested.")]
    // Only a boolean or an array is tested bare.
    [InlineData("encounter_kind", "'encounter_kind' tests an enum for truth; compare it to one of its values instead.")]
    [InlineData("length_of_stay", "'length_of_stay' tests a number for truth; only a boolean or an array can be tested bare.")]
    [InlineData("patient", "'patient' tests a object for truth; only a boolean or an array can be tested bare.")]
    [InlineData("patient.age", "'patient.age' tests a number for truth")]
    // A whole object or array is not compared; its parts are.
    [InlineData("patient == x", "compares 'patient', which is an object; compare one of its fields, or its count, instead.")]
    [InlineData("prior_notes == x", "compares 'prior_notes', which is an array; compare one of its fields, or its count, instead.")]
    // A path needs an object and a declared field.
    [InlineData("seen_on.year == 2026", "reads field 'year' of 'seen_on', which is a date, not an object.")]
    [InlineData("medications.name == x", "reads field 'name' of 'medications', which is a array, not an object.")]
    [InlineData("patient.weight > 90", "reads field 'weight' of 'patient', which it does not declare (fields: family_name, age, sex).")]
    // A count needs an array, and a comparison.
    [InlineData("count(patient) > 0", "counts 'patient', which is a object; only an array has a count.")]
    [InlineData("count(prior_notes)", "'count(prior_notes)' needs a comparison; write count(prior_notes) > 0.")]
    // The literal is held to the type.
    [InlineData("length_of_stay > abc", "compares 'length_of_stay' to 'abc', which is not a plain decimal.")]
    [InlineData("length_of_stay > 1e3", "which is not a plain decimal.")]
    [InlineData("seen_on > 2026-1-1", "compares 'seen_on' to '2026-1-1', which is not a date written YYYY-MM-DD.")]
    [InlineData("patient.sex == other", "compares 'patient.sex' to 'other', which it does not declare (values: female, male).")]
    [InlineData("count(prior_notes) > -1", "compares 'count(prior_notes)' to '-1', which is not a whole number.")]
    [InlineData("count(prior_notes) > 1.5", "which is not a whole number.")]
    [InlineData("billable == yes", "compares boolean 'billable' to 'yes'; use true or false.")]
    // The syntax and the undeclared-input refusals read as before.
    [InlineData("urgency == high", "reads undeclared input 'urgency'")]
    [InlineData("count(urgency) > 0", "reads undeclared input 'urgency'")]
    [InlineData("patient.age >=", "compares against nothing")]
    public void TheGrammarRejects(string when, string expected)
    {
        var errors = Errors(when).ToList();

        Assert.Contains(errors, e => e.Contains(expected));
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("patient.age >= 65", "condition reads a field of 'patient', which requires specVersion 9.")]
    [InlineData("count(prior_notes) > 1", "condition counts 'prior_notes', which requires specVersion 9.")]
    [InlineData("length_of_stay > 7", "condition compares 'length_of_stay' with >, which requires specVersion 9.")]
    public void TheNewFormsRequireV9(string when, string expected)
    {
        // A v8 manifest that happens to declare the structured inputs (it
        // cannot, but the gate is on the condition, not the declaration).
        var manifest = V9Fixtures.Conditional(when) with { SpecVersion = 8 };

        Assert.Contains(V9Fixtures.Validate(manifest).Errors, e => e.Contains(expected));
    }

    [Theory]
    [InlineData("seen_on == 2026-08-10", "which is a date")]
    [InlineData("consult_draft == \"urgent\"", "which is a text")]
    [InlineData("encounter_kind", "tests an enum for truth")]
    [InlineData("billable == yes", "use true or false")]
    public void TheV8Refusals_AreUnchangedOnAV8Manifest(string when, string expected)
    {
        Assert.Contains(V8Fixtures.Validate(V8Fixtures.Conditional(when)).Errors, e => e.Contains(expected));
    }
}

/// <summary>#432, v9 § 4: a title and a description — both optional, both arriving at 9.</summary>
public class WorkflowV9MetadataTests
{
    private static IEnumerable<string> Errors(WorkflowPackageManifest manifest) => V9Fixtures.Validate(manifest).Errors;

    [Fact]
    public void ATitleAndADescription_AreAcceptedAtNine()
    {
        var manifest = V9Fixtures.Minimal() with
        {
            Title = "Breast oncology consults",
            Description = "Referral triage and consult notes for the breast clinic."
        };

        Assert.True(V9Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void BelowNine_EachIsRefusedByName(int specVersion)
    {
        // Not "unknown property": the reader knows them on every version. The
        // gate is < 9, not == 8 — the same posture as items and fields.
        var manifest = (specVersion == 7 ? V7Fixtures.Minimal() : V8Fixtures.Typed()) with
        {
            Title = "Breast oncology consults",
            Description = "Referral triage."
        };

        var errors = (specVersion == 7 ? V7Fixtures.Validate(manifest) : V8Fixtures.Validate(manifest)).Errors;

        Assert.Contains("title requires specVersion 9.", errors);
        Assert.Contains("description requires specVersion 9.", errors);
    }

    [Theory]
    [InlineData("", "title must not be empty.")]
    [InlineData("   ", "title must not be empty.")]
    [InlineData("\n", "title must not be empty.")]
    [InlineData("Breast\nclinic", "title must be a single line.")]
    [InlineData("Breast\r\nclinic", "title must be a single line.")]
    public void ATitle_IsHeldToItsRules(string title, string expected)
    {
        var errors = Errors(V9Fixtures.Minimal() with { Title = title }).ToList();

        Assert.Contains(expected, errors);
        Assert.Single(errors);
    }

    [Fact]
    public void ATitle_IsAtMostEightyCharacters()
    {
        Assert.True(V9Fixtures.Validate(V9Fixtures.Minimal() with { Title = new string('x', 80) }).IsValid);
        Assert.Contains(
            "title must be at most 80 characters.",
            Errors(V9Fixtures.Minimal() with { Title = new string('x', 81) }));
    }

    [Fact]
    public void ATwoLineTitleThatIsAlsoTooLong_IsToldBoth()
    {
        var errors = Errors(V9Fixtures.Minimal() with { Title = new string('x', 50) + "\n" + new string('y', 50) }).ToList();

        Assert.Equal(new[] { "title must be a single line.", "title must be at most 80 characters." }, errors);
    }

    [Theory]
    [InlineData("", "description must not be empty.")]
    [InlineData("  ", "description must not be empty.")]
    public void ADescription_MustNotBeEmpty(string description, string expected)
    {
        var errors = Errors(V9Fixtures.Minimal() with { Description = description }).ToList();

        Assert.Contains(expected, errors);
        Assert.Single(errors);
    }

    [Fact]
    public void ADescription_IsAtMostFiveHundredCharacters_AndMaySpanLines()
    {
        Assert.True(V9Fixtures.Validate(V9Fixtures.Minimal() with { Description = new string('x', 500) }).IsValid);
        Assert.True(V9Fixtures.Validate(V9Fixtures.Minimal() with { Description = "Two\nlines." }).IsValid);
        Assert.Contains(
            "description must be at most 500 characters.",
            Errors(V9Fixtures.Minimal() with { Description = new string('x', 501) }));
    }
}
