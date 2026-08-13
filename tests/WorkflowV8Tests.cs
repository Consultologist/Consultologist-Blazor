using System.Text.Json;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// specVersion-8 fixtures: the v7 minimal package at 8 with nothing else
/// changed — the migration #317 proves — and a typed variant declaring one of
/// each type (package-format-v8-design.md § 4).
/// </summary>
public static class V8Fixtures
{
    /// <summary>
    /// The whole migration story in one fixture: v7's declaration, specVersion
    /// 8, no types. If this ever fails to validate, `type` has stopped being
    /// optional and minimal-v8 is no longer a one-line change.
    /// </summary>
    public static WorkflowPackageManifest Minimal()
        => V7Fixtures.Minimal() with { SpecVersion = 8 };

    public static WorkflowPackageManifest Typed()
        => Minimal() with
        {
            Inputs = new List<WorkflowInputSpec>
            {
                new("consult_draft", "Consult draft"),
                new("seen_on", "Date seen", Type: WorkflowInputTypes.Date),
                new("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum,
                    Values: new List<string> { "new_patient", "follow_up" }),
                new("billable", "Billable encounter", Required: false, Type: WorkflowInputTypes.Boolean)
            }
        };

    /// <summary>The typed fixture with one input's declaration replaced.</summary>
    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Typed().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);
        inputs[index] = input;
        return Typed() with { Inputs = inputs };
    }

    /// <summary>
    /// The v7 two-deliverable package at 8, with the typed declaration so a
    /// condition has something to read. The letter is conditional; the note is
    /// not.
    /// </summary>
    public static WorkflowPackageManifest Conditional(string? when = "encounter_kind == follow_up")
    {
        var baseline = V7Fixtures.MultiDeliverable();

        return baseline with
        {
            SpecVersion = 8,
            Inputs = Typed().Inputs,
            Results = new List<WorkflowResultSpec>
            {
                new("consult_note", "node:assemble-note", "Consultation note"),
                new("patient_letter", "node:assemble-letter", "Patient letter", When: when)
            }
        };
    }

    /// <summary>
    /// #357: a package whose prompt actually READS a typed input. Typed() only
    /// declares the inputs — nothing binds them — so it cannot exercise the
    /// probe. This adds a scalar prompt node whose one variable is bound to
    /// whatever source the caller names, and a hand-written template, since
    /// every generated fixture template is a bare {{ variable }}.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Reading(
        string template,
        string source = "input:seen_on",
        string? alsoBoundBy = null)
    {
        var manifest = Typed();
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

        // The aggregator must still reach it, or reachability fails for a reason
        // that has nothing to do with the probe.
        var resultIndex = nodes.FindIndex(node => node.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string> { "node:section-instructions", "node:stamp" }
        };
        nodes.Insert(resultIndex, reader);

        if (alsoBoundBy != null)
        {
            // A second node sharing the same prompt, binding the same variable
            // to a different source — legal since v6, and the case a prompt-wide
            // type environment cannot describe.
            nodes.Insert(resultIndex + 1, new WorkflowNodeSpec("stamp-again", "Stamping again",
                Prompt: "stamp",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["seen"] = new(alsoBoundBy)
                },
                ForEach: "data:standards"));

            nodes[nodes.FindIndex(node => node.Id == "assemble-note")] = nodes[nodes.FindIndex(node => node.Id == "assemble-note")] with
            {
                Aggregate = new List<string> { "node:section-instructions", "node:stamp", "node:stamp-again" }
            };
        }

        manifest = manifest with { Prompts = prompts, Nodes = nodes };

        var files = V6Fixtures.Files(manifest);
        files["prompts/stamp.md"] = template;

        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV8ValidationTests
{
    // #357: the validator probes every prompt by rendering it. It used to hand
    // each variable the string "placeholder", so the format's own documented
    // idiom — {{ seen_on | date.to_string "%d %B %Y" }} — could not publish:
    // Scriban refuses string → DateTime. It types from the BINDINGS, which is
    // where a variable's type actually comes from.

    private static WorkflowPackageValidator.ValidationResult ValidateReading(
        string template,
        string source = "input:seen_on",
        string? alsoBoundBy = null)
    {
        var (manifest, files) = V8Fixtures.Reading(template, source, alsoBoundBy);
        return WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas);
    }

    [Fact]
    public void ADateFilter_OnADateBoundVariable_Validates()
    {
        var result = ValidateReading("Seen {{ seen | date.to_string \"%d %B %Y\" }}");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ADateFilter_OnAStringBoundVariable_StillFails()
    {
        // The type comes from the binding, not from the variable's name or from
        // the template's hopes: item:name is a string at runtime, so formatting
        // it as a date would throw there.
        var result = ValidateReading(
            "Seen {{ seen | date.to_string \"%d %B %Y\" }}",
            source: "item:name");

        Assert.Contains(result.Errors, error => error.Contains("failed strict rendering", StringComparison.Ordinal));
    }

    [Fact]
    public void ASharedPromptWithDivergentBindings_KeepsTheVariableAString()
    {
        // A prompt may be shared since v6, and each node binds every variable
        // itself with no rule forcing agreement. So there is no single type for
        // the variable, and the template is genuinely invalid for the node
        // passing a string — refusing it is the right verdict.
        var result = ValidateReading(
            "Seen {{ seen | date.to_string \"%d %B %Y\" }}",
            source: "input:seen_on",
            alsoBoundBy: "item:name");

        Assert.Contains(result.Errors, error => error.Contains("failed strict rendering", StringComparison.Ordinal));
    }

    [Fact]
    public void ABooleanBoundVariable_ProbesAsABoolean()
    {
        // Not a filter but the same principle: the probe hands Scriban the type
        // the runtime will, so a branch is a branch rather than a truthy string.
        var result = ValidateReading(
            "{{ if seen }}Billable.{{ end }}",
            source: "input:billable");

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AVariableNamedForABuiltin_Warns()
    {
        // Shadowing `date` makes EVERY date in the template render as a .NET
        // default — including ones this variable has nothing to do with. A
        // warning, not an error: this validator runs at load too, and a new
        // error would strand an already-published package.
        var (manifest, files) = V8Fixtures.Reading("{{ seen }}");
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!);
        var index = prompts.FindIndex(prompt => prompt.Id == "stamp");
        prompts[index] = prompts[index] with { Variables = new List<string> { "date" } };

        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!);
        var reader = nodes.FindIndex(node => node.Id == "stamp");
        nodes[reader] = nodes[reader] with
        {
            Bindings = new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["date"] = new("input:seen_on")
            }
        };

        files["prompts/stamp.md"] = "{{ date }}";

        var result = WorkflowPackageValidator.Validate(
            manifest with { Prompts = prompts, Nodes = nodes }, files, TestOutputContracts.CatalogSchemas);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, warning => warning.Contains("shadows Scriban's built-in", StringComparison.Ordinal));
    }

    [Fact]
    public void MinimalV8_IsValid_WithNoTypesDeclared()
    {
        // The proving migration (#317): specVersion 8 and nothing else. `type`
        // defaults to text precisely so this holds.
        var result = V8Fixtures.Validate(V8Fixtures.Minimal());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void EveryDeclaredType_IsAccepted()
    {
        var result = V8Fixtures.Validate(V8Fixtures.Typed());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnUnknownType_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen", Type: "datetime"));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("unknown type 'datetime'") && e.Contains("seen_on"));
    }

    [Fact]
    public void AnEnumWithoutValues_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("type 'enum' and must declare values"));
    }

    [Fact]
    public void ValuesOnANonEnum_IsRejected()
    {
        // The mirror: values belong to enum and to nothing else, so a date
        // carrying them is a mistake worth naming rather than ignoring.
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen",
            Type: WorkflowInputTypes.Date, Values: new List<string> { "a", "b" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("is type 'date' and may not declare values"));
    }

    [Fact]
    public void AnEnumWithOneValue_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "only" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("a constant, not a choice"));
    }

    [Fact]
    public void DuplicateEnumValues_AreRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "follow_up", "follow_up" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("duplicate enum value 'follow_up'"));
    }

    [Fact]
    public void AnEnumValueBreakingTheIdRule_IsRejected()
    {
        // Enum values share the declared-id rule, which is what makes them safe
        // wherever result ids are — authored content, never patient data.
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "new_patient", "Follow Up" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("enum value 'Follow Up'") && e.Contains("snake_case"));
    }

    [Fact]
    public void ATypeOnAV7Manifest_IsRejected()
    {
        // Same posture as "inputs requires specVersion 7": a section the
        // version does not have is an error, never a silently ignored field.
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft", Type: WorkflowInputTypes.Date) }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("declares a type, which requires specVersion 8"));
    }

    [Fact]
    public void TheEngineRunsV8()
    {
        // #313 pinned {5,6,7} so the engine could not run a half-built v8 while
        // the validator already accepted it. #317 moves the line and this
        // assertion together, as that comment promised.
        //
        // v5 and v6 stay: an engine that accepts four versions is the claim,
        // and the unmigrated example-two-documents package is the standing
        // evidence that v7 still runs.
        Assert.Equal(new[] { 5, 6, 7, 8 }, WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void SpecVersion8_IsAcceptedByTheValidatorGate()
    {
        // The validator's gate moves before the engine's: v8 publishes here and
        // refuses to run until SupportedSpecVersions catches up.
        Assert.DoesNotContain(V8Fixtures.Validate(V8Fixtures.Minimal()).Errors,
            e => e.Contains("is not supported"));

        Assert.Contains(
            V8Fixtures.Validate(V8Fixtures.Minimal() with { SpecVersion = 9 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7 or 8"));
    }
}

/// <summary>
/// The proving migration (#317). `general` moves to v8 by changing one
/// character, so anything that breaks is the engine and not the manifest.
///
/// #317 asked to verify this by diffing the rendered *document* against a run
/// on the previous version — which cannot work: the document is model output
/// and varies between runs. What that claim is really about is that nothing
/// the engine does differs, and that is provable here without a model.
/// </summary>
public class MinimalV8MigrationTests
{
    private static WorkflowPackage Resolve(WorkflowPackageManifest manifest)
    {
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            ResultNodeId: null,
            Results: new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") });
    }

    [Fact]
    public void MinimalV8_ExpandsTheSameBlocksAsItsV7Self()
    {
        // The block list is what TotalBlockCount is stamped from and what the
        // run rail renders, so identical ids in identical order is the whole
        // migration claim. This is also the test that fails if any of the
        // nineteen dispatch points routes v8 down a different arm —
        // WorkflowPackageBlocks.Resolve's `>= 7` being the one the design
        // flagged as v8's sharp edge.
        var v7 = WorkflowPackageBlocks.Resolve(Resolve(V7Fixtures.Minimal()));
        var v8 = WorkflowPackageBlocks.Resolve(Resolve(V8Fixtures.Minimal()));

        Assert.Equal(
            v7.Select(b => (b.Id, b.Name, b.Content)).ToArray(),
            v8.Select(b => (b.Id, b.Name, b.Content)).ToArray());
    }

    [Fact]
    public void MinimalV8_RendersTheSamePromptBytes()
    {
        // A typed input renders as its canonical string and this package
        // declares none, so every prompt must come out byte-identical. If a
        // future change makes v8 render differently by default, this is what
        // catches it.
        var template = new WorkflowPromptTemplate(
            "draft", "Draft from {{ consult_draft }} for {{ section_name }}.",
            new[] { "consult_draft", "section_name" }, null);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["consult_draft"] = "Referral text.",
            ["section_name"] = "History"
        };

        // v7 carries no variable types; minimal v8 declares none, so it carries
        // none either. Same inputs, same bytes.
        Assert.Equal(
            PromptTemplateRenderer.Render(template, variables),
            PromptTemplateRenderer.Render(template, variables, variableTypes: null));
    }

    [Fact]
    public void MinimalV8_KeepsItsResultSetAndDeclaration()
    {
        // The manifest differs in exactly one field. Stating it as an assertion
        // means a future fixture change cannot quietly make this migration
        // bigger than one character.
        var options = new JsonSerializerOptions { WriteIndented = false };
        var v7 = JsonSerializer.Serialize(V7Fixtures.Minimal(), options);
        var v8 = JsonSerializer.Serialize(V8Fixtures.Minimal(), options);

        Assert.Contains("\"SpecVersion\":7", v7);
        Assert.Contains("\"SpecVersion\":8", v8);

        // Everything else is byte-identical: the migration is one field, and
        // saying so as an assertion means a fixture change cannot quietly make
        // it bigger.
        Assert.Equal(v7, v8.Replace("\"SpecVersion\":8", "\"SpecVersion\":7"));
    }
}

public class WorkflowV8ConditionTests
{
    private static IReadOnlyList<string> Errors(string? when)
        => V8Fixtures.Validate(V8Fixtures.Conditional(when)).Errors;

    [Theory]
    [InlineData("billable")]
    [InlineData("encounter_kind == follow_up")]
    [InlineData("encounter_kind != new_patient")]
    [InlineData("  encounter_kind  ==  follow_up  ")]
    public void TheGrammarAccepts(string when)
    {
        Assert.Empty(Errors(when));
    }

    [Theory]
    // Undeclared id, and the message lists what is declared.
    [InlineData("urgency == high", "undeclared input 'urgency'")]
    // An enum value the input does not declare is an authoring error, not a
    // condition that silently never holds.
    [InlineData("encounter_kind == procedure", "which it does not declare")]
    // The bare form asks "is this true", which only a boolean answers.
    [InlineData("encounter_kind", "tests an enum for truth")]
    [InlineData("billable == yes", "use true or false")]
    [InlineData("== follow_up", "is not an input id")]
    [InlineData("encounter_kind ==", "compares against nothing")]
    public void TheGrammarRejects(string when, string expected)
    {
        Assert.Contains(Errors(when), e => e.Contains(expected));
    }

    [Theory]
    [InlineData("seen_on == 2026-08-10", "which is a date")]
    [InlineData("consult_draft == \"urgent\"", "which is a text")]
    public void OnlyEnumAndBooleanInputsCanBeTested(string when, string expected)
    {
        // The narrowing (#314): a date asks only "was it exactly this day"
        // until ordering exists (#338), and text equality compares a referral
        // byte for byte. The message names the TYPE, so an author learns why
        // rather than hunting a syntax error.
        var errors = Errors(when);

        Assert.Contains(errors, e => e.Contains(expected) && e.Contains("only enum and boolean"));
    }

    [Fact]
    public void WhenOnAV7Manifest_IsRejected()
    {
        var manifest = V8Fixtures.Conditional() with { SpecVersion = 7 };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("declares when, which requires specVersion 8"));
    }

    [Fact]
    public void AConditionalResult_MustStillReachAForEachSource()
    {
        // The rule #314 asked to pin rather than assume. A conditional
        // deliverable is still a deliverable: "a deliverable with no fan has no
        // consult" applies to it unchanged. Point the conditional result at an
        // aggregator over a non-fanned node and the rule must still fire, and
        // must name that result.
        var baseline = V8Fixtures.Conditional();
        var nodes = new List<WorkflowNodeSpec>(baseline.Nodes!);

        // A prompt node reading only the frozen input: no fan, and no path to
        // one. contextualize would not do — it reaches the guidelines fan
        // transitively, which is exactly what the rule is about.
        nodes.Add(new WorkflowNodeSpec("standalone", "Standalone summary",
            Prompt: "contextualize",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["guideline_summaries"] = new("input:consult_draft")
            }));

        var letterIndex = nodes.FindIndex(n => n.Id == "assemble-letter");
        nodes[letterIndex] = nodes[letterIndex] with
        {
            Aggregate = new List<string> { "node:standalone" }
        };

        var errors = V8Fixtures.Validate(baseline with { Nodes = nodes }).Errors;

        Assert.Contains(errors,
            e => e.Contains("patient_letter") && e.Contains("at least one forEach source"));
    }

    [Fact]
    public void EveryDeliverableMayBeConditional()
    {
        // Decided in #314: no publish-time rule forcing an unconditional
        // deliverable. Conditions can be legitimately exhaustive, proving that
        // in general is a satisfiability question, and an empty fire set is
        // refused at start with a named reason (#315).
        var baseline = V8Fixtures.Conditional();
        var manifest = baseline with
        {
            Results = new List<WorkflowResultSpec>
            {
                new("consult_note", "node:assemble-note", "Consultation note",
                    When: "encounter_kind == new_patient"),
                new("patient_letter", "node:assemble-letter", "Patient letter",
                    When: "encounter_kind != new_patient")
            }
        };

        var result = V8Fixtures.Validate(manifest);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

public class WorkflowResultConditionEvaluationTests
{
    private static Dictionary<string, ConsultInputValue> Inputs(params (string Id, ConsultInputValue Value)[] pairs)
        => pairs.ToDictionary(p => p.Id, p => p.Value, StringComparer.Ordinal);

    private static WorkflowResultCondition Parse(string when)
    {
        Assert.True(WorkflowResultConditions.TryParse(when, out var condition, out _));
        return condition!;
    }

    [Fact]
    public void NoCondition_AlwaysFires()
        => Assert.True(WorkflowResultConditions.Holds(null, Inputs()));

    [Theory]
    [InlineData("billable", true, true)]
    [InlineData("billable", false, false)]
    public void TheBareForm_TestsTruth(string when, bool supplied, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(
            Parse(when), Inputs(("billable", ConsultInputValue.OfBoolean(supplied)))));

    [Theory]
    [InlineData("encounter_kind == follow_up", "follow_up", true)]
    [InlineData("encounter_kind == follow_up", "new_patient", false)]
    [InlineData("encounter_kind != follow_up", "new_patient", true)]
    [InlineData("encounter_kind != follow_up", "follow_up", false)]
    public void EqualityAndItsNegation(string when, string supplied, bool expected)
        => Assert.Equal(expected, WorkflowResultConditions.Holds(
            Parse(when), Inputs(("encounter_kind", supplied))));

    [Theory]
    [InlineData("billable")]
    [InlineData("billable == true")]
    // The negated form too: without this it would fire on every job that left
    // the slot blank, which is the opposite of what an author means.
    [InlineData("billable != true")]
    public void AnAbsentOptionalInput_DoesNotSatisfyAnything(string when)
    {
        Assert.False(WorkflowResultConditions.Holds(Parse(when), Inputs()));
        Assert.False(WorkflowResultConditions.Holds(Parse(when), Inputs(("billable", ""))));
    }

    [Fact]
    public void TheReason_NamesTheInputTheWantAndTheValue()
    {
        var reason = WorkflowResultConditions.Explain(
            Parse("encounter_kind == follow_up"), Inputs(("encounter_kind", "new_patient")));

        Assert.Contains("encounter_kind", reason);
        Assert.Contains("'follow_up'", reason);
        Assert.Contains("'new_patient'", reason);
    }

    [Fact]
    public void TheReason_SaysWhenNothingWasSupplied()
        => Assert.Contains("not supplied",
            WorkflowResultConditions.Explain(Parse("billable"), Inputs()));
}

public class ConsultInputValueWireTests
{
    private static ConsultInputValue? Read(string json)
        => JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(json)!["v"];

    [Fact]
    public void AJsonStringIsText_AndAJsonBooleanIsAFlag()
    {
        Assert.Equal(ConsultInputValue.OfText("2026-08-10"), Read("""{"v":"2026-08-10"}"""));
        Assert.Equal(ConsultInputValue.OfBoolean(true), Read("""{"v":true}"""));
        Assert.Equal(ConsultInputValue.OfBoolean(false), Read("""{"v":false}"""));
    }

    [Theory]
    [InlineData("""{"v":20260810}""")]
    [InlineData("""{"v":{"nested":1}}""")]
    [InlineData("""{"v":["a"]}""")]
    public void ATokenJsonShouldNotCarry_IsMalformed(string json)
    {
        // A shape error, so the HTTP door answers 400. A value that is the
        // right SHAPE but disagrees with the declaration is the 422, and that
        // check lives in the starter where the slot can be named.
        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void AJsonNullIsBlankText_NotANull()
    {
        // A null in the map would be dereferenced by every downstream check.
        // Before typing, a null value arrived as a null string and read as
        // blank, so blank text is what preserves that.
        var value = Read("""{"v":null}""");

        Assert.NotNull(value);
        Assert.True(value!.IsBlank);
    }

    [Fact]
    public void ANullValue_IsRejectedAsMissing_NotThrown()
    {
        var supplied = JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(
            """{"consult_draft":null}""")!;

        var resolution = Consultologist.Api.Jobs.ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new Consultologist.Api.Models.ConsultGenerationRequest(null, Inputs: supplied),
            V8Fixtures.Minimal());

        Assert.Contains("missing", resolution.Error);
    }

    [Fact]
    public void ItRoundTripsAsTheSameJson()
    {
        // The durable payload is replayed from this JSON, so the written form
        // has to be the read form.
        Assert.Equal("""{"v":true}""", JsonSerializer.Serialize(
            new Dictionary<string, ConsultInputValue> { ["v"] = ConsultInputValue.OfBoolean(true) }));
        Assert.Equal("""{"v":"text"}""", JsonSerializer.Serialize(
            new Dictionary<string, ConsultInputValue> { ["v"] = "text" }));
    }
}
