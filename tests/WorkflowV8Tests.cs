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

    /// <summary>
    /// #370: the two declarations that make a package unreachable by email. Both
    /// are checkable from the manifest alone, and both work perfectly in the app
    /// — the author sees it run, publishes, and has produced something one of the
    /// two intake doors can never accept.
    /// </summary>
    private static WorkflowPackageValidator.ValidationResult ValidateWithBoolean(bool required)
        => V8Fixtures.Validate(V8Fixtures.WithInput(
            new WorkflowInputSpec("billable", "Billable encounter", Required: required, Type: WorkflowInputTypes.Boolean)));

    [Fact]
    public void ARequiredBoolean_WarnsThatEmailCannotStartIt()
    {
        // An emailed value is always text and a string in a boolean slot is a
        // 422, so the slot can never be filled through that door.
        var result = ValidateWithBoolean(required: true);

        Assert.Contains(result.Warnings, w => w.Contains("'billable' is a required boolean", StringComparison.Ordinal));
    }

    [Fact]
    public void ARequiredBoolean_IsAWarningAndNotAnError()
    {
        // The property that keeps already-published packages loading. This
        // validator runs at LOAD as well as publish, versions are immutable, and
        // acct-* versions declaring a required boolean are live — an error here
        // would strand them (#357's reasoning, #374's failure mode).
        var result = ValidateWithBoolean(required: true);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnOptionalBoolean_WarnsNothing()
    {
        var result = ValidateWithBoolean(required: false);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("required boolean", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WorkflowInputTypes.Date)]
    [InlineData(WorkflowInputTypes.Text)]
    public void ARequiredInputEmailCanSupply_WarnsNothing(string type)
    {
        // Decided rather than overlooked (#370 asks): a date and a text input are
        // JSON strings on the wire and email fills them — a seen_on.txt holding
        // 2026-08-10 is a verified path. The boolean is the only type the door
        // cannot express at all.
        var result = V8Fixtures.Validate(V8Fixtures.WithInput(
            new WorkflowInputSpec("seen_on", "Date seen", Required: true, Type: type)));

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EveryDeliverableConditionedOnABoolean_WarnsThatNoDocumentCouldApply()
    {
        // The second shape, and the one #369's body asked for: inputs resolve,
        // but absence satisfies no condition, so the fire set is always empty and
        // every emailed job is refused at start.
        var manifest = V8Fixtures.Conditional(when: "billable");
        var results = manifest.Results!.Select(r => r with { When = "billable" }).ToList();

        var result = V8Fixtures.Validate(manifest with { Results = results });

        Assert.Contains(result.Warnings, w => w.Contains("Every deliverable's condition reads a boolean", StringComparison.Ordinal));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void OneUnconditionalDeliverable_IsEnoughToBeReachable()
    {
        // The default fixture: consult_note has no condition, so it always fires
        // however the enum lands. Reachability needs one deliverable, not all.
        var result = V8Fixtures.Validate(V8Fixtures.Conditional(when: "billable"));

        Assert.DoesNotContain(result.Warnings, w => w.Contains("Every deliverable", StringComparison.Ordinal));
    }

    [Fact]
    public void DeliverablesConditionedOnAnEnum_AreReachable()
    {
        // The live shape: acct-7bca2dcc1ed4@v2026.08.13 gates both deliverables
        // on encounter_kind, which an emailed .txt can answer. Verified this
        // session by an emailed consult that reached the fire-set evaluation.
        var manifest = V8Fixtures.Conditional();
        var results = new List<WorkflowResultSpec>
        {
            manifest.Results![0] with { When = "encounter_kind == new_patient" },
            manifest.Results![1] with { When = "encounter_kind == follow_up" }
        };

        var result = V8Fixtures.Validate(manifest with { Results = results });

        Assert.Empty(result.Warnings);
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
    public void AStructuredValue_DoesNotHold_AndIsExplainedWithoutThrowing()
    {
        // #421: the starter refuses structure before conditions run, but these
        // two are public pure functions and must never throw whatever map they
        // are handed. A value with no canonical string has not answered.
        var inputs = Inputs(("billable", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("secret") })));

        Assert.False(WorkflowResultConditions.Holds(Parse("billable"), inputs));
        Assert.False(WorkflowResultConditions.Holds(Parse("billable != true"), inputs));

        var explained = WorkflowResultConditions.Explain(Parse("billable"), inputs);
        Assert.Contains("an array", explained, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", explained, StringComparison.Ordinal);
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

/// <summary>
/// The wire form of one input value, both directions. v8 admitted a string and
/// a boolean; v9 layer 1 (#421) admits a plain-decimal number, an object of
/// scalars and an array of scalars or one-level objects — package-format-v9-
/// design.md § 4 is the table, and each of its rows is a case here.
///
/// A shape the format cannot carry is a 400, thrown as
/// ConsultInputShapeException so the door can say which token and where. A
/// well-formed value that disagrees with the declaration is the 422, and that
/// check lives in the starter where the slot can be named.
/// </summary>
public class ConsultInputValueWireTests
{
    private static ConsultInputValue? Read(string json)
        => JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(json)!["v"];

    private static string Write(ConsultInputValue value)
        => JsonSerializer.Serialize(new Dictionary<string, ConsultInputValue> { ["v"] = value });

    [Fact]
    public void AJsonStringIsText_AndAJsonBooleanIsAFlag()
    {
        Assert.Equal(ConsultInputValue.OfText("2026-08-10"), Read("""{"v":"2026-08-10"}"""));
        Assert.Equal(ConsultInputValue.OfBoolean(true), Read("""{"v":true}"""));
        Assert.Equal(ConsultInputValue.OfBoolean(false), Read("""{"v":false}"""));
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
        Assert.Equal("""{"v":true}""", Write(ConsultInputValue.OfBoolean(true)));
        Assert.Equal("""{"v":"text"}""", Write("text"));
    }

    [Fact]
    public void ANumber_ReadsAsTheDigitsSent()
    {
        // 1.50, not 1.5: trimming would mean provenance records a value nobody
        // sent (v9 § 4). The decimal is there for comparison; the spelling is
        // what travels.
        var value = Read("""{"v":1.50}""")!;

        Assert.True(value.IsNumber);
        Assert.Equal("1.50", value.Number);
        Assert.Equal(1.50m, value.NumberValue);
        Assert.Equal("1.50", value.Canonical);
        Assert.False(value.IsBlank);
        Assert.Equal("""{"v":1.50}""", Write(value));
    }

    [Fact]
    public void AStringOfDigits_IsText()
    {
        // "3" is a JSON string and stays text; whether a text is acceptable in
        // a number slot is the starter's 422, not the converter's business.
        var value = Read("""{"v":"3"}""")!;

        Assert.Equal(ConsultInputKind.Text, value.Kind);
        Assert.Equal(ConsultInputValue.OfText("3"), value);
    }

    [Theory]
    // Exponent form: valid JSON, never a plain decimal.
    [InlineData("""{"v":1e3}""")]
    [InlineData("""{"v":1E3}""")]
    // 2^96: one past decimal's range.
    [InlineData("""{"v":79228162514264337593543950336}""")]
    // Thirty significant digits: decimal would round, and a rounded value is
    // a value nobody sent.
    [InlineData("""{"v":123456789012345678901234567890}""")]
    [InlineData("""{"v":1.00000000000000000000000000001}""")]
    public void ANumberTheFormatCannotCarry_IsAShapeError(string json)
    {
        var exception = Assert.Throws<ConsultInputShapeException>(() => Read(json));

        Assert.Contains("plain decimal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MinusZero_IsRefused_BecauseItDoesNotRoundTrip()
    {
        // Not a row in § 4's table; what the round-trip rule yields, pinned so
        // a change to it is deliberate. decimal keeps no negative zero, so the
        // spelling cannot be reproduced and the rule refuses it.
        Assert.Throws<ConsultInputShapeException>(() => Read("""{"v":-0}"""));
    }

    [Theory]
    [InlineData("""{"v":+3}""")]
    [InlineData("""{"v":007}""")]
    [InlineData("""{"v":.5}""")]
    [InlineData("""{"v":5.}""")]
    public void AGrammarRefusal_IsAPlainJsonException_NotAShapeError(string json)
    {
        // JSON's own grammar refuses these before the converter sees a token,
        // so they answer the door's generic 400 rather than a named one. The
        // boundary is recorded here rather than papered over by parsing bytes
        // by hand.
        var exception = Assert.ThrowsAny<JsonException>(() => Read(json));

        Assert.IsNotType<ConsultInputShapeException>(exception);
    }

    [Fact]
    public void AnObjectOfScalars_ReadsInSuppliedOrder()
    {
        var value = Read("""{"v":{"b":1,"a":"x","c":true}}""")!;

        Assert.True(value.IsObject);
        Assert.Equal(new[] { "b", "a", "c" }, value.Fields!.Select(field => field.Id));
        Assert.Equal(ConsultInputKind.Number, value.Fields[0].Value.Kind);
        Assert.Equal(ConsultInputKind.Text, value.Fields[1].Value.Kind);
        Assert.Equal(ConsultInputKind.Boolean, value.Fields[2].Value.Kind);
        Assert.False(value.IsBlank);
    }

    [Fact]
    public void AnObjectWithARepeatedKey_IsAShapeError()
    {
        // Last-wins would make provenance record one of two values the caller
        // sent; refusing is the only reading with no surprise in it.
        var exception = Assert.Throws<ConsultInputShapeException>(() => Read("""{"v":{"a":1,"a":2}}"""));

        Assert.Contains("repeats the field 'a'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"v":{"a":{"b":1}}}""", "field 'a'")]
    [InlineData("""{"v":{"a":[1]}}""", "field 'a'")]
    [InlineData("""{"v":[[1]]}""", "element 0")]
    [InlineData("""{"v":[{"a":{"b":1}}]}""", "field 'a'")]
    public void StructurePastOneLevel_IsAShapeError(string json, string where)
    {
        // The format bounds depth at one (v9 § 4), so no declaration could ever
        // admit this — which is what makes it a shape error rather than a
        // declaration disagreement.
        var exception = Assert.Throws<ConsultInputShapeException>(() => Read(json));

        Assert.Contains(where, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AShapeError_NamesTheTokenAndPath_NeverTheValue()
    {
        var exception = Assert.Throws<ConsultInputShapeException>(() => Read("""{"v":[["secret"]]}"""));

        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
        Assert.Contains("element 0 is an array", exception.Message, StringComparison.Ordinal);
        // Set by the serializer before it rethrows our exception unchanged.
        Assert.Equal("$.v", exception.Path);
    }

    [Fact]
    public void AnArrayOfOneLevelObjects_Reads()
    {
        var value = Read("""{"v":[{"k":1},{"k":2}]}""")!;

        Assert.True(value.IsArray);
        Assert.Equal(2, value.Elements!.Count);
        Assert.All(value.Elements, element => Assert.True(element.IsObject));
    }

    [Fact]
    public void AnEmptyArray_IsPresentAndNotBlank()
    {
        // Present and empty, not absent (v9 § 4): a required slot holding one
        // is refused by the starter naming the slot, never waved through.
        var value = Read("""{"v":[]}""")!;

        Assert.True(value.IsArray);
        Assert.Empty(value.Elements!);
        Assert.False(value.IsBlank);
    }

    [Fact]
    public void ANullInsideStructure_IsCarried_ForTheStarterToRefuse()
    {
        var array = Read("""{"v":["a",null]}""")!;
        var obj = Read("""{"v":{"k":null}}""")!;

        Assert.True(array.Elements![1].IsNull);
        Assert.True(obj.Fields![0].Value.IsNull);
        Assert.False(array.IsBlank);
        Assert.False(obj.IsBlank);
    }

    [Fact]
    public void StructureRoundTripsByteForByte()
    {
        // Replay reads this back through the same converter, and the hash
        // sees these bytes: supplied order, spelling, nulls and all.
        const string json = """{"v":[1.50,"a",null,{"k":false,"n":-2.5}]}""";

        Assert.Equal(json, Write(Read(json)!));
    }

    [Fact]
    public void CanonicalIsUnrepresentableForStructure()
    {
        // Deliberately a throw, not an empty string: an empty string would let
        // structure reach a string-only renderer silently (v9 § 10).
        foreach (var value in new[]
                 {
                     ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a") }),
                     ConsultInputValue.OfObject(new[] { new ConsultInputEntry("k", ConsultInputValue.OfText("a")) }),
                     ConsultInputValue.NullElement
                 })
        {
            Assert.False(value.HasCanonical);
            Assert.Throws<InvalidOperationException>(() => value.Canonical);
        }

        Assert.True(ConsultInputValue.OfNumber("3").HasCanonical);
        Assert.Equal("an array", ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()).Described);
        Assert.Equal("an object", ConsultInputValue.OfObject(Array.Empty<ConsultInputEntry>()).Described);
    }

    [Fact]
    public void EqualityIsStructural_AndOverTheSpelling()
    {
        const string json = """{"v":[1.50,{"k":"x"},null]}""";

        Assert.Equal(Read(json), Read(json));
        Assert.Equal(
            JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(json),
            JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(json));

        // 1.5 and 1.50 serialise to different bytes and hash differently, so
        // they are different values — decimal equality would say otherwise.
        Assert.NotEqual(Read("""{"v":1.5}"""), Read("""{"v":1.50}"""));
        Assert.NotEqual(Read("""{"v":[1,2]}"""), Read("""{"v":[2,1]}"""));
    }

    [Fact]
    public void TextLength_CountsTheTextInsideStructure()
    {
        // A log line's number, never the cap — the cap is per text scalar.
        Assert.Equal(5, Read("""{"v":["ab",{"k":"cde"},7,true,null]}""")!.TextLength - "true".Length - "7".Length);
    }

    [Fact]
    public void TheIssuesDoneWhenPayload_Deserialises()
    {
        var supplied = JsonSerializer.Deserialize<Dictionary<string, ConsultInputValue>>(
            """{"prior_notes": ["a", "b"], "length_of_stay": 3}""")!;

        Assert.Equal(2, supplied["prior_notes"].Elements!.Count);
        Assert.Equal("3", supplied["length_of_stay"].Number);
    }

    [Fact]
    public void TheFactories_RefuseWhatTheConverterRefuses()
    {
        // In-process callers (the email door, tests) get the same closure as
        // the wire, so a value that could not have arrived cannot be built.
        Assert.Throws<ArgumentException>(() => ConsultInputValue.OfNumber("1e3"));
        Assert.Throws<ArgumentException>(() => ConsultInputValue.OfArray(new[] { ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()) }));
        Assert.Throws<ArgumentException>(() => ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("a", ConsultInputValue.OfText("x")),
            new ConsultInputEntry("a", ConsultInputValue.OfText("y"))
        }));
        Assert.Throws<ArgumentException>(() => ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("a", ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()))
        }));
    }
}
