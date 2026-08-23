using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// #407: exports the conformance suite that consultologist-package-format
/// publishes — the artifact that lets somebody outside this repo check a
/// manifest without building the engine.
///
/// Expectations are not hand-written. Every case is run through the real
/// validator and whatever it says is what gets recorded, so a fixture can
/// never claim an error message the engine does not produce.
///
/// Skipped unless CONFORMANCE_EXPORT names a directory to write into: this is
/// a generator, and regenerating on every CI run would be a slow way to write
/// files nobody reads.
/// </summary>
public class ConformanceFixtureExport
{
    private sealed record Case(
        string Id,
        int SpecVersion,
        string Description,
        WorkflowPackageManifest Manifest,
        IReadOnlyDictionary<string, string> Files);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static List<Case> Cases()
    {
        var v5 = V5Fixtures.Manifest();
        var v6 = V6Fixtures.SingleCollection();
        var v7 = V7Fixtures.Minimal();
        var v7Multi = V7Fixtures.MultiDeliverable();
        var v8 = V8Fixtures.Minimal();
        var v8Typed = V8Fixtures.Typed();
        var v8Conditional = V8Fixtures.Conditional();

        var cases = new List<Case>
        {
            new("v5-minimal", 5, "The canonical v5 package: one collection, forEach sections, a single result.", v5, V5Fixtures.Files(v5)),
            new("v6-single-collection", 6, "v5 with an aggregator node as the deliverable — the v6 closure.", v6, V6Fixtures.Files(v6)),
            new("v7-minimal", 7, "One declared input, one deliverable. The smallest v7 package.", v7, V6Fixtures.Files(v7)),
            new("v7-multi-deliverable", 7, "Two independent chains, two declared deliverables, one optional input.", v7Multi, V6Fixtures.Files(v7Multi)),
            new("v8-minimal-is-v7-plus-a-line", 8, "The migration v8 promises: a valid v7 manifest with specVersion 8 and nothing else changed.", v8, V6Fixtures.Files(v8)),
            new("v8-typed-inputs", 8, "One input of each declared type: text, date, enum with values, optional boolean.", v8Typed, V6Fixtures.Files(v8Typed)),
            new("v8-conditional-deliverable", 8, "A deliverable that fires only when an enum input takes a given value.", v8Conditional, V6Fixtures.Files(v8Conditional)),
        };

        // Invalid cases. Each breaks exactly one rule against a baseline that is
        // otherwise valid, so the recorded error is attributable.
        void Invalid(string id, int spec, string description, WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string>? files = null)
            => cases.Add(new Case(id, spec, description, manifest, files ?? V6Fixtures.Files(manifest)));

        Invalid("invalid-spec-version-unsupported", 3,
            "A specVersion the engine does not accept. Pre-v5 packages are archived, not executable.",
            v8 with { SpecVersion = 3 });

        Invalid("invalid-derived-from-latest", 8,
            "derivedFrom naming a moving pointer. Lineage must always be a concrete version.",
            v8 with { DerivedFrom = "general@latest" });

        Invalid("invalid-inputs-before-v7", 6,
            "Declared inputs on a v6 manifest. A section the version does not have is an error, never a silently ignored field.",
            v6 with { Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft") } });

        Invalid("invalid-results-before-v7", 6,
            "A declared result set on a v6 manifest, which knows only a single result.",
            v6 with { Results = new List<WorkflowResultSpec> { new("note", "node:assemble-note", "Note") } });

        // From the multi-deliverable baseline, which declares results and no
        // result: adding result to v7-minimal changes nothing, because minimal
        // already carries the singular form inherited from v6.
        Invalid("invalid-result-and-results-together", 7,
            "Both the v5/v6 single result and the v7 result set. One package, one answer.",
            v7Multi with { Result = "node:assemble-note" });

        Invalid("invalid-typed-input-before-v8", 7,
            "An input declaring a type on a v7 manifest. Types arrive with v8.",
            v7 with { Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft", Type: WorkflowInputTypes.Date) } });

        Invalid("invalid-condition-before-v8", 7,
            "A deliverable condition on a v7 manifest. Conditions arrive with v8.",
            v7Multi with { Results = v7Multi.Results!.Select((r, i) => i == 1 ? r with { When = "encounter_kind == follow_up" } : r).ToList() });

        Invalid("invalid-values-without-enum", 8,
            "values declared on an input that is not an enum. values belongs to enum and to nothing else.",
            V8Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Date seen", Type: WorkflowInputTypes.Date,
                Values: new List<string> { "yes", "no" })));

        Invalid("invalid-enum-without-values", 8,
            "An enum input with no values to choose from.",
            V8Fixtures.WithInput(new WorkflowInputSpec("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum)));

        Invalid("invalid-unknown-input-type", 8,
            "An input type the format does not define.",
            V8Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Date seen", Type: "timestamp")));

        Invalid("invalid-missing-prompt-file", 8,
            "A prompt the manifest references and the bundle does not carry.",
            v8, V6Fixtures.Files(v8).Where(f => !f.Key.EndsWith("section-instructions.md", StringComparison.Ordinal))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));

        // Same reason: emptied on the baseline that uses the result SET, or the
        // singular result survives and the package fails the "not both" rule
        // instead — a different error wearing this case's name.
        Invalid("invalid-empty-results", 8,
            "A result set declaring no deliverables. A package that can produce nothing is not a package.",
            v7Multi with { SpecVersion = 8, Results = new List<WorkflowResultSpec>() });

        // v9 (#453). Generated here ahead of the registry release; published
        // with the v9 prose by #430. Tags are required at 9, so the minimal v9
        // manifest is v8's plus the version line AND an empty tags array.
        var v9 = V9Fixtures.Minimal();
        var v9Tagged = v9 with { Tags = new List<string> { "oncology", "Breast", "new-patient" } };
        cases.Add(new Case("v9-minimal-is-v8-plus-two-lines", 9,
            "The migration v9 promises: a valid v8 manifest with specVersion 9 and an empty tags array, nothing else changed.",
            v9, V6Fixtures.Files(v9)));
        cases.Add(new Case("v9-tagged", 9,
            "Three tags in authored order; case is kept as written.",
            v9Tagged, V6Fixtures.Files(v9Tagged)));

        Invalid("invalid-tags-omitted-at-v9", 9,
            "A v9 manifest without tags. Every v9 manifest states its tags; an empty array is how it says none.",
            v9 with { Tags = null });

        Invalid("invalid-tags-before-v9", 8,
            "A tags array on a v8 manifest, even an empty one. A section the version does not have is an error.",
            v8 with { Tags = new List<string>() });

        Invalid("invalid-tag-repeated-ignoring-case", 9,
            "Two tags that differ only in case. A filter treats them as one, so the manifest may not declare two.",
            v9 with { Tags = new List<string> { "oncology", "Oncology" } });

        // ----- #430: the v9 release's coverage — a rejection case for every
        // publish-time refusal the design record names (§§ 4–6). § 7's rules
        // (document multiplicity, the aggregate cap, the content floor) and
        // the supplied-value rules of § 4's wire table are START-TIME: they
        // guard a request, not a manifest, so the validator this suite replays
        // never says them and no case can carry them. The same holds for the
        // empty fan (v9 § 5, #434): an empty array is a request.

        // § 4 metadata — title and description (#432).
        Invalid("invalid-title-before-v9", 8,
            "A title on a v8 manifest. The section arrives at 9; before it, a known field is an error, never ignored.",
            v8Typed with { Title = "Breast oncology consults" });
        Invalid("invalid-description-before-v9", 8,
            "A description on a v8 manifest, for the same rule.",
            v8Typed with { Description = "Referral triage." });
        Invalid("invalid-title-empty", 9,
            "A whitespace-only title. Clearing is done by omitting the field, not by blanking it.",
            v9 with { Title = "   " });
        Invalid("invalid-title-multiline", 9,
            "A title spanning lines. It is shown beside the ref on one line everywhere.",
            v9 with { Title = "Breast\nclinic" });
        Invalid("invalid-title-too-long", 9,
            "A title over 80 UTF-16 code units.",
            v9 with { Title = new string('a', 81) });
        Invalid("invalid-description-empty", 9,
            "A whitespace-only description.",
            v9 with { Description = " " });
        Invalid("invalid-description-too-long", 9,
            "A description over 500 UTF-16 code units.",
            v9 with { Description = new string('a', 501) });

        // § 4 metadata — tags (#453), beyond the three cases above.
        Invalid("invalid-tag-empty", 9,
            "A blank tag, named by position.",
            v9 with { Tags = new List<string> { "oncology", "   " } });
        Invalid("invalid-tag-multiline", 9,
            "A tag spanning lines.",
            v9 with { Tags = new List<string> { "breast\nclinic" } });
        Invalid("invalid-tag-untrimmed", 9,
            "A tag with leading or trailing whitespace. The stored spelling is the label.",
            v9 with { Tags = new List<string> { " oncology" } });
        Invalid("invalid-tag-too-long", 9,
            "A tag over 32 UTF-16 code units.",
            v9 with { Tags = new List<string> { new string('a', 33) } });
        Invalid("invalid-too-many-tags", 9,
            "Twenty-one tags. Twenty is the ceiling.",
            v9 with { Tags = Enumerable.Range(0, 21).Select(i => $"tag-{i}").ToList() });

        // § 4 structured inputs — version gates at 8.
        Invalid("invalid-structured-type-at-v8", 8,
            "A number input on a v8 manifest. The scalar set widens at 9.",
            V8Fixtures.Typed() with { Inputs = V8Fixtures.Typed().Inputs!
                .Append(new WorkflowInputSpec("length_of_stay", "Length of stay", Required: false, Type: WorkflowInputTypes.Number)).ToList() });
        Invalid("invalid-items-at-v8", 8,
            "An items declaration on a v8 manifest. Arrays arrive at 9.",
            V8Fixtures.Typed() with { Inputs = V8Fixtures.Typed().Inputs!
                .Append(new WorkflowInputSpec("prior_notes", "Prior notes", Required: false, Items: WorkflowInputTypes.Text)).ToList() });
        Invalid("invalid-fields-at-v8", 8,
            "A fields declaration on a v8 manifest. Objects arrive at 9.",
            V8Fixtures.Typed() with { Inputs = V8Fixtures.Typed().Inputs!
                .Append(new WorkflowInputSpec("patient", "Patient", Required: false,
                    Fields: new List<WorkflowFieldSpec> { new("age", "Age") })).ToList() });

        // § 4 structured inputs — shape rules at 9.
        Invalid("invalid-unknown-input-type", 9,
            "A type name outside the v9 vocabulary.",
            V9Fixtures.WithInput(new WorkflowInputSpec("stamp", "Timestamp", Required: false, Type: "timestamp")));
        Invalid("invalid-array-without-items", 9,
            "An array that does not say what its elements are.",
            V9Fixtures.WithInput(new WorkflowInputSpec("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array)));
        Invalid("invalid-array-of-arrays", 9,
            "items: array. Structure is one level deep by design.",
            V9Fixtures.WithInput(new WorkflowInputSpec("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Array)));
        Invalid("invalid-unknown-items-type", 9,
            "An items name outside the element vocabulary.",
            V9Fixtures.WithInput(new WorkflowInputSpec("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array, Items: "attachment")));
        Invalid("invalid-items-on-a-scalar", 9,
            "items on a non-array input.",
            V9Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Seen on", Type: WorkflowInputTypes.Date, Items: WorkflowInputTypes.Text)));
        Invalid("invalid-array-of-objects-without-fields", 9,
            "An array of objects that does not declare the object's fields.",
            V9Fixtures.WithInput(new WorkflowInputSpec("medications", "Medications", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object)));
        Invalid("invalid-object-without-fields", 9,
            "An object with no fields is a shape that admits nothing.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object)));
        Invalid("invalid-fields-on-a-scalar", 9,
            "fields on a non-object input.",
            V9Fixtures.WithInput(new WorkflowInputSpec("seen_on", "Seen on", Type: WorkflowInputTypes.Date,
                Fields: new List<WorkflowFieldSpec> { new("year", "Year") })));
        Invalid("invalid-object-field-bad-id", 9,
            "A field id that is not snake_case. Ids are read in conditions and bindings.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("Family Name", "Family name") })));
        Invalid("invalid-object-duplicate-field", 9,
            "The same field id twice.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("age", "Age"), new("age", "Age again") })));
        Invalid("invalid-object-field-without-label", 9,
            "A field with no label. The intake renders the label.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("age", " ") })));
        Invalid("invalid-object-field-structured", 9,
            "A field typed object. Structure is one level deep: a field holds a scalar.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("history", "History", Type: WorkflowInputTypes.Object) })));
        Invalid("invalid-object-field-unknown-type", 9,
            "A field type outside the scalar vocabulary.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("age", "Age", Type: "years") })));
        Invalid("invalid-object-field-values-on-non-enum", 9,
            "values on a non-enum field.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("age", "Age", Type: WorkflowInputTypes.Number, Values: new List<string> { "old" }) })));
        Invalid("invalid-field-enum-single-value", 9,
            "A field enum with one value is a constant, not a choice — the enum rules hold inside an object too.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
                Fields: new List<WorkflowFieldSpec> { new("sex", "Sex", Type: WorkflowInputTypes.Enum, Values: new List<string> { "female" }) })));

        // § 5 — fanning over a caller-supplied array.
        cases.Add(new Case("v9-fanned-array", 9,
            "A required array of text fanned by forEach, its elements bound as item:value.",
            V9Fixtures.Fanned(), V6Fixtures.Files(V9Fixtures.Fanned())));
        var fannedOptional = V9Fixtures.Fanned() with
        {
            Inputs = V9Fixtures.Fanned().Inputs!
                .Select(input => input.Id == "prior_notes" ? input with { Required = false } : input)
                .ToList()
        };
        cases.Add(new Case("v9-fanned-optional-input-warns", 9,
            "An optional array fanned by forEach: valid, with the warning that an empty consult is refused at start. The demonstration package carries exactly this shape on purpose.",
            fannedOptional, V6Fixtures.Files(fannedOptional)));
        Invalid("invalid-fan-of-undeclared-input", 9,
            "forEach naming an input the manifest does not declare.",
            V9Fixtures.Fanned(forEach: "input:missing_notes"));
        Invalid("invalid-fan-of-a-non-array", 9,
            "forEach over a text input. Only an array can be fanned.",
            V9Fixtures.Fanned(forEach: "input:consult_draft"));
        Invalid("invalid-fan-item-field", 9,
            "A binding reading an item field an input fan does not carry (they carry: id, name, value).",
            V9Fixtures.Fanned(binding: "item:text"));
        Invalid("invalid-input-fan-before-v9", 8,
            "forEach over an input on a v8 manifest, where forEach must be a data: collection reference.",
            V8Fixtures.Typed() with { Nodes = V9Fixtures.Fanned().Nodes, Prompts = V9Fixtures.Fanned().Prompts,
                Inputs = V8Fixtures.Typed().Inputs!.Append(new WorkflowInputSpec("prior_notes", "Prior notes")).ToList() });

        // § 6 — the condition grammar, each refusal by name. Ids and triggers
        // mirror WorkflowV9Tests.TheGrammarRejects; the recorded sentence is
        // whatever the validator says, as everywhere in this suite.
        var conditionRefusals = new (string Id, string When, string Description)[]
        {
            ("orders-an-enum", "encounter_kind > follow_up", "An ordering operator on an enum. Ordering applies to a number or a date."),
            ("orders-a-boolean", "billable >= true", "An ordering operator on a boolean."),
            ("orders-an-enum-field", "patient.sex < female", "An ordering operator on an enum field."),
            ("tests-a-text", "consult_draft == urgent", "A text input in a condition. Free text is never compared."),
            ("tests-a-text-field", "patient.family_name == Smith", "A text field, for the same rule."),
            ("bare-enum", "encounter_kind", "A bare enum tests nothing; compare it to one of its values."),
            ("bare-number", "length_of_stay", "A bare number tested for truth."),
            ("bare-object", "patient", "A bare object tested for truth."),
            ("bare-number-field", "patient.age", "A bare number field tested for truth."),
            ("compares-an-object", "patient == x", "A whole object compared. Its fields or its count are what compare."),
            ("compares-an-array", "prior_notes == x", "A whole array compared."),
            ("path-into-a-date", "seen_on.year == 2026", "A field read of a non-object."),
            ("path-into-an-array", "medications.name == x", "A field read of an array."),
            ("path-to-undeclared-field", "patient.weight > 90", "A field the object does not declare."),
            ("counts-an-object", "count(patient) > 0", "count() of a non-array."),
            ("bare-count", "count(prior_notes)", "count() without a comparison."),
            ("count-not-whole", "count(prior_notes) > 1.5", "A count compared to a non-whole number."),
            ("count-negative", "count(prior_notes) > -1", "A count compared to a negative literal."),
            ("number-literal-not-decimal", "length_of_stay > abc", "A number compared to a non-decimal literal."),
            ("number-literal-exponent", "length_of_stay > 1e3", "Exponent form. A literal is a plain decimal, as a supplied value is."),
            ("date-literal-not-iso", "seen_on > 2026-1-1", "A date literal not written YYYY-MM-DD."),
            ("enum-literal-undeclared", "patient.sex == other", "A value the enum does not declare."),
            ("boolean-literal-not-true-false", "billable == yes", "A boolean compared to a word."),
            ("reads-undeclared-input", "urgency == high", "An input the manifest does not declare."),
            ("counts-undeclared-input", "count(urgency) > 0", "count() of an undeclared input."),
            ("trailing-operator", "patient.age >=", "An operator with nothing after it."),
            ("blank", "   ", "A blank condition."),
            ("stray-quote", "encounter_kind == \"new_patient", "An unbalanced quote in the literal."),
        };

        foreach (var refusal in conditionRefusals)
        {
            Invalid($"invalid-condition-{refusal.Id}", 9, refusal.Description, V9Fixtures.Conditional(refusal.When));
        }

        // § 6 — the v9 forms are refused by name on a v8 manifest.
        Invalid("invalid-condition-path-at-v8", 8,
            "A field read on a v8 manifest. Paths arrive at 9.",
            V8Fixtures.Conditional("seen_on.year == 2026"));
        Invalid("invalid-condition-count-at-v8", 8,
            "count() on a v8 manifest.",
            V8Fixtures.Conditional("count(consult_draft) > 0"));
        Invalid("invalid-condition-ordering-at-v8", 8,
            "An ordering operator on a v8 manifest, where only equality and bare booleans compare.",
            V8Fixtures.Conditional("seen_on > 2026-01-01"));

        // § 4/§ 6 together — the accepted showcase: the demo package's shape.
        var v9Structured = V9Fixtures.Conditional("patient.age >= 65") with
        {
            Title = "Structured intake demo",
            Description = "An array fanned by forEach, an object read by path, a number ordered, a count() gate.",
            Tags = new List<string> { "demo", "structured-intake" }
        };
        cases.Add(new Case("v9-structured-consult", 9,
            "The v9 width in one valid manifest: structured inputs, a path condition, title, description and tags.",
            v9Structured, V6Fixtures.Files(v9Structured)));

        return cases;
    }

    [Fact]
    public void Export()
    {
        // A no-op without the variable rather than a skip: adding a package
        // just to render one line of CI output differently is a poor trade.
        var outDir = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        var index = new List<object>();

        foreach (var testCase in Cases())
        {
            var result = WorkflowPackageValidator.Validate(
                testCase.Manifest, testCase.Files, TestOutputContracts.CatalogSchemas);

            // Cases about one format live under that format; a case about the
            // ACCEPTED SET itself belongs to no version — filing it under v3
            // would read as "the rules for specVersion 3", which do not exist
            // here at all.
            var group = WorkflowPackageStore.SupportedSpecVersions.Contains(testCase.SpecVersion)
                ? $"v{testCase.SpecVersion}"
                : "any";
            var dir = Path.Combine(outDir!, group);
            Directory.CreateDirectory(dir);

            var payload = new
            {
                id = testCase.Id,
                specVersion = testCase.SpecVersion,
                description = testCase.Description,
                expect = new
                {
                    valid = result.IsValid,
                    errors = result.Errors,
                    warnings = result.Warnings
                },
                manifest = testCase.Manifest,
                files = testCase.Files,
            };

            File.WriteAllText(Path.Combine(dir, $"{testCase.Id}.json"), JsonSerializer.Serialize(payload, Json));
            index.Add(new { id = testCase.Id, specVersion = testCase.SpecVersion, valid = result.IsValid, path = $"{group}/{testCase.Id}.json" });
        }

        File.WriteAllText(Path.Combine(outDir!, "catalog-schemas.json"),
            JsonSerializer.Serialize(TestOutputContracts.CatalogSchemas, Json));
        File.WriteAllText(Path.Combine(outDir!, "index.json"),
            JsonSerializer.Serialize(new { cases = index }, Json));
    }
}
