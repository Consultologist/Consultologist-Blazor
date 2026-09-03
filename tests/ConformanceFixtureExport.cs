using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
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

        // #448: a name is a path; a nested derivedFrom is a well-formed ref on
        // every version. Recorded at 9 beside the other v9 cases.
        var v9Nested = v9 with { DerivedFrom = "oncology/breast@v2026.08.1" };
        cases.Add(new Case("v9-nested-derived-from", 9,
            "A derivedFrom naming a nested package (oncology/breast). Package names are paths of up to four segments; a flat name is a one-segment path.",
            v9Nested, V6Fixtures.Files(v9Nested)));

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

        // ----- v10 (#500): the deferred grammar — package-format-v10-design.md § 4–§ 7, § 9. -----
        // Generated with the gate flipped; published with the v10 prose.
        void Bundle(string id, int spec, string description, (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, spec, description, bundle.Manifest, bundle.Files));

        // § 4 — the classifying node: accepted, and each of its rules refused by name.
        Bundle("v10-classifier", 10,
            "A classifier over the draft: kind classifier, the values it may answer, bound to an input.",
            V10Fixtures.WithClassifier());
        Bundle("invalid-classifier-kind-aggregator", 10,
            "kind 'aggregator' spelled on a node. An aggregator is declared by aggregate, never by kind. A node of no known kind references no prompt, so the second error follows from the first.",
            Consumed(V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = "aggregator" })));
        Bundle("invalid-classifier-kind-unknown", 10,
            "A kind the format does not define. A node of no known kind references no prompt, so the second error follows from the first.",
            Consumed(V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = "router" })));
        Bundle("invalid-classifier-values-without-kind", 10,
            "values on a node that is not a classifier. Only kind 'classifier' answers from a value set; the node is not read as a prompt node either, so its prompt goes unreferenced — the second error follows from the first.",
            Consumed(V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = null })));
        Bundle("invalid-classifier-without-values", 10,
            "A classifier with no values to answer.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Values = null }));
        Bundle("invalid-classifier-one-value", 10,
            "A classifier with one value is a constant, not a choice.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "only" })));
        Bundle("invalid-classifier-value-not-snake-case", 10,
            "A classifier value that is not snake_case.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "In Scope", "out" })));
        Bundle("invalid-classifier-value-repeated", 10,
            "A classifier declaring the same value twice.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier(values: new List<string> { "a", "a" })));
        Bundle("invalid-classifier-declares-output", 10,
            "A classifier declaring output. Its output is the classification contract, implied by its kind.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier(output: new WorkflowNodeOutputSpec("concept-list"))));
        Bundle("invalid-classifier-declares-foreach", 10,
            "A classifier declaring forEach. A classification is one answer; a classifier is never fanned.",
            V10Fixtures.WithClassifier(V10Fixtures.Classifier(forEach: "input:prior_notes")));
        {
            var promptNodeId = V10Fixtures.Minimal().Nodes!.First(n => n.Aggregate is null).Id;
            Bundle("invalid-classifier-binds-a-prompt-node", 10,
                "A classifier binding a prompt node's output. A classifier may read inputs and classifiers only.",
                V10Fixtures.WithClassifier(V10Fixtures.Classifier(
                    bindings: new Dictionary<string, WorkflowBindingValue> { ["referral"] = new($"node:{promptNodeId}") })));
        }
        {
            var (manifest, files) = V10Fixtures.WithClassifier();
            var aggregator = manifest.Nodes!.First(n => n.Aggregate != null);
            var nodes = manifest.Nodes!.Select(n => n.Id == aggregator.Id
                ? n with { Aggregate = new List<string>(n.Aggregate!) { "node:scope" } }
                : n).ToList();
            Bundle("invalid-aggregator-aggregates-a-classifier", 10,
                "An aggregator listing a classifier as a source. A classifier's value is bindable, never aggregated.",
                (manifest with { Nodes = nodes }, files));
        }
        {
            var (manifest, files) = V10Fixtures.WithClassifier();
            var second = V10Fixtures.Classifier(id: "urgency",
                bindings: new Dictionary<string, WorkflowBindingValue> { ["referral"] = new("node:scope") },
                values: new List<string> { "routine", "urgent" });
            var nodes = new List<WorkflowNodeSpec> { second };
            nodes.AddRange(manifest.Nodes!);
            Bundle("v10-classifier-reads-a-classifier", 10,
                "A classifier bound to another classifier's answer, and a deliverable conditioned on both.",
                (manifest with { Nodes = nodes }, files));
        }

        // § 4, below 10 — kind and values refused by name on a v9 manifest.
        Bundle("invalid-classifier-kind-at-v9", 9,
            "kind on a v9 manifest. The classifying node arrives at 10; below it the node references no prompt, so the second error follows from the first.",
            Consumed(V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Values = null }, from: V9Fixtures.Structured())));
        Bundle("invalid-classifier-values-at-v9", 9,
            "values on a v9 manifest. Below 10 the node references no prompt, so the second error follows from the first.",
            Consumed(V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { Kind = null }, from: V9Fixtures.Structured())));

        // § 6 — conditions at 10: the expression forms accepted, and each refusal by name.
        var v10Conditions = new (string Id, string When, string Description)[]
        {
            ("and", "length_of_stay > 7 and billable", "Two clauses joined by and."),
            ("or-and-not-grouped", "(encounter_kind == follow_up or billable) and not length_of_stay > 30", "or, and, not and explicit grouping."),
            ("arithmetic", "length_of_stay - 2 > 5", "Arithmetic on the left of a comparison."),
            ("date-plus-days", "seen_on + 30 >= 2026-01-01", "A date plus whole days compared with a date."),
            ("count-times-two", "count(prior_notes) * 2 > length_of_stay", "A count in arithmetic compared with a number."),
            ("node-value", "node:scope == in_scope", "A classifier's answer compared with one of its values."),
            ("node-value-and-boolean", "node:scope != out_of_scope and billable", "A classifier's answer beside a boolean."),
        };
        foreach (var form in v10Conditions)
        {
            Bundle($"v10-condition-{form.Id}", 10, form.Description, WithClassifierAt10(form.When));
        }

        var v10Refusals = new (string Id, string When, string Description)[]
        {
            ("node-not-a-classifier", "node:nope == x", "A node: operand naming a node that is not a classifier."),
            ("bare-node", "node:scope", "A classifier tested for truth; compare it to one of its values."),
            ("node-ordered", "node:scope > in_scope", "An ordering operator on a classifier's value; only == and != compare it."),
            ("node-value-undeclared", "node:scope == elsewhere", "A value the classifier does not declare."),
            ("node-in-arithmetic", "node:scope + 1 > 0", "A classifier's value in arithmetic; a symbol, not a number."),
            ("arithmetic-on-enum", "encounter_kind + 1 > 0", "Arithmetic on an enum; arithmetic applies to a number, a count or a date."),
            ("arithmetic-without-comparison", "length_of_stay + 1", "Arithmetic with no comparison."),
            ("divides-by-zero", "length_of_stay / 0 > 1", "Division by a literal zero."),
            ("date-times-number", "seen_on * 2 > 2026-01-01", "A date multiplied; a date admits only ± whole days."),
            ("date-compared-with-number", "seen_on + 1 > 7", "A date compared with a number; both sides are numbers, or both dates."),
            ("arithmetic-undeclared-input", "length_of_stay + abc > 1", "An undeclared input inside arithmetic."),
            ("arithmetic-literal-exponent", "length_of_stay + 1e3 > 1", "An exponent literal in arithmetic."),
            ("path-into-a-number", "patient.age.years > 1", "A deeper path into a number."),
            ("path-to-undeclared-nested-field", "patient.contact.phone == x", "A deeper path to a field the object does not declare."),
            ("and-undeclared-input", "length_of_stay > 7 and urgency == high", "An undeclared input in the second clause."),
            ("and-bare-enum", "billable and encounter_kind", "A bare enum in the second clause."),
            ("trailing-and", "a and", "An and with nothing after it."),
            ("unbalanced-parenthesis", "(a or b", "A parenthesis never closed."),
        };
        foreach (var refusal in v10Refusals)
        {
            Bundle($"invalid-condition-{refusal.Id}", 10, refusal.Description, WithClassifierAt10(refusal.When));
        }

        // § 6, below 10 — each expression form refused by name on a v9 manifest.
        var v9Gates = new (string Id, string When, string Description)[]
        {
            ("and", "length_of_stay > 7 and billable", "and on a v9 manifest. Expressions arrive at 10."),
            ("or", "billable or length_of_stay > 7", "or on a v9 manifest."),
            ("not", "not billable", "not on a v9 manifest."),
            ("arithmetic", "length_of_stay - 2 > 5", "Arithmetic on a v9 manifest."),
            ("deep-path", "patient.age.x >= 1", "A path of three segments on a v9 manifest."),
        };
        foreach (var gate in v9Gates)
        {
            Invalid($"invalid-condition-{gate.Id}-at-v9", 9, gate.Description, V9Fixtures.Conditional(gate.When));
        }
        Invalid("invalid-condition-node-at-v9", 9,
            "node: on a v9 manifest. A classifier's answer is a v10 operand.",
            V9Fixtures.Conditional("node:assemble-note == x"));

        // § 7 — nested structure: accepted, refused by name, and the email warning.
        var familyHistory = new WorkflowInputSpec("family_history", "Family history", Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object,
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
        var grid = new WorkflowInputSpec("grid", "Grid", Required: false, Type: WorkflowInputTypes.Array,
            Items: new WorkflowElementSpec(WorkflowInputTypes.Array, Items: WorkflowInputTypes.Number));
        Invalid("v10-nested-structure", 10,
            "Structure below one level: an array of objects whose fields are an array and an object, and an array of arrays as an element spec. Valid; a required array deeper than one level warns about the email door.",
            V10Fixtures.WithInput(familyHistory) with { Inputs = new List<WorkflowInputSpec>(V10Fixtures.WithInput(familyHistory).Inputs!) { grid } });
        Invalid("invalid-nested-field-enum-one-value", 10,
            "A nested enum field with one value.",
            V10Fixtures.WithInput(familyHistory with
            {
                Fields = familyHistory.Fields!.Select(f => f.Id == "contact"
                    ? f with { Fields = new List<WorkflowFieldSpec> { new("kind", "Kind", Type: WorkflowInputTypes.Enum, Values: new List<string> { "only" }) } }
                    : f).ToList()
            }));
        Invalid("invalid-nested-array-field-without-items", 10,
            "A nested array field declaring no items.",
            V10Fixtures.WithInput(familyHistory with
            {
                Fields = familyHistory.Fields!.Select(f => f.Id == "conditions" ? f with { Items = null } : f).ToList()
            }));
        Invalid("invalid-element-enum-without-values", 10,
            "An element spec typed enum with no values.",
            V10Fixtures.WithInput(grid with { Items = new WorkflowElementSpec(WorkflowInputTypes.Enum) }));
        Invalid("invalid-element-spec-and-fields-together", 10,
            "An element spec beside fields on the array. A spec carries its own fields and values.",
            V10Fixtures.WithInput(grid with
            {
                Items = new WorkflowElementSpec(WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec> { new("a", "A") }),
                Fields = new List<WorkflowFieldSpec> { new("b", "B") }
            }));

        // § 7, below 10 — the shapes refused by name on a v9 manifest.
        Invalid("invalid-items-spec-at-v9", 9,
            "items as an element spec on a v9 manifest. A spec requires 10.",
            V9Fixtures.WithInput(grid));
        Invalid("invalid-field-array-at-v9", 9,
            "A field typed array on a v9 manifest, where a field holds a scalar.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
            {
                new("history", "History", Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text)
            })));
        Invalid("invalid-field-object-at-v9", 9,
            "A field typed object on a v9 manifest.",
            V9Fixtures.WithInput(new WorkflowInputSpec("patient", "Patient", Required: false, Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec>
            {
                new("contact", "Contact", Type: WorkflowInputTypes.Object, Fields: new List<WorkflowFieldSpec> { new("phone", "Phone") })
            })));

        // § 12 — the accepted showcase: the demo package's shape (scope → plan, decline letter or a request for information).
        Bundle("v10-classifier-scope-demo", 10,
            "The v10 width in one valid manifest: a three-way classifier, three deliverables each conditioned on its answer, a prompt node reading the classifier.",
            ScopeDemo());

        // ----- v11 (#566): macros, the signed flag, reproducible —
        // package-format-v11-design.md § 4–§ 6, § 8. Generated with the gate
        // flipped; published with the v11 prose. -----

        var v10Minimal = V10Fixtures.Minimal();

        // The § 7 control: one edit, nothing of v11 used.
        Bundle("v11-minimal-is-v10-plus-a-line", 11,
            "The migration v11 promises: a valid v10 manifest with specVersion 11 and nothing else changed.",
            (v10Minimal with { SpecVersion = 11 }, V6Fixtures.Files(v10Minimal)));

        {
            // The v11 width in one valid manifest — the demo's shape.
            var (classifier, classifierFiles) = V10Fixtures.WithClassifier();
            var (manifest, files) = V11Fixtures.WithMacro(
                "This determination ({{classification:scope}}) was made on {{run:date}} by {{profile:name}}, "
                + "from {{input:consult_draft}} under {{data:intro}} ({{run:package}} at {{run:host}}, job {{run:job}}).",
                from: V11Fixtures.WithResultsList(classifier with { SpecVersion = 11 }),
                macroId: "closing");
            foreach (var pair in classifierFiles)
            {
                files.TryAdd(pair.Key, pair.Value);
            }

            manifest = manifest with
            {
                Data = new Dictionary<string, string>(manifest.Data ?? new Dictionary<string, string>()) { ["intro"] = "data/intro.md" },
                Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Signature = true } : r).ToList(),
                Nodes = manifest.Nodes!.Select(n => WorkflowNodeKinds.IsClassifier(n) ? n with { Reproducible = true } : n).ToList()
            };
            files["data/intro.md"] = "A scalar the macro reads.";
            Bundle("v11-macro-signed-reproducible", 11,
                "The v11 width in one valid manifest: a macro over every namespace appended to a signed deliverable, and a reproducible classifier.",
                (manifest, files));
        }

        Bundle("v11-macro-optional-input-warns", 11,
            "A macro reading an optional input: valid, with the warning recorded — absent renders as empty at assembly.",
            V11Fixtures.WithMacro("Stay: {{input:length_of_stay}}."));

        // § 4's rules, each refused by name against an otherwise-valid baseline.
        {
            var manifest = V11Fixtures.Minimal() with
            {
                Macros = new List<WorkflowMacroSpec> { new("closing", "Closing paragraph", "macros/closing.md") }
            };
            var files = new Dictionary<string, string>(V6Fixtures.Files(manifest), StringComparer.Ordinal)
            {
                ["macros/closing.md"] = "Thank you for this referral."
            };
            Bundle("invalid-macro-orphaned", 11,
                "A declared macro no result references. The orphan rule, as prompts have.",
                (manifest, files));
        }
        {
            var manifest = V11Fixtures.Minimal();
            Invalid("invalid-macro-reference-undeclared", 11,
                "A result naming a macro the manifest does not declare.",
                manifest with { Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<WorkflowResultMacroSpec> { "ghost" } } : r).ToList() });
        }
        {
            var (manifest, files) = V11Fixtures.WithMacro();
            Bundle("invalid-macro-listed-twice", 11,
                "The same macro twice on one deliverable.",
                (manifest with { Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<WorkflowResultMacroSpec> { "disclaimer", "disclaimer" } } : r).ToList() }, files));
        }
        {
            var (manifest, files) = V11Fixtures.WithMacro();
            var trimmed = new Dictionary<string, string>(files, StringComparer.Ordinal);
            trimmed.Remove("macros/disclaimer.md");
            Bundle("invalid-macro-file-missing", 11, "A macro whose file is not in the package.", (manifest, trimmed));
        }
        Bundle("invalid-macro-file-empty", 11,
            "A macro whose file is blank. Prompts check presence only; a macro's file must carry text.",
            V11Fixtures.WithMacro("   "));
        Bundle("invalid-macro-id-not-snake-case", 11,
            "A macro id with a dash. Macro ids are snake_case, the declared-id grammar; the malformed id is undeclarable, so the reference to it fails too — a second error that follows from the first.",
            V11Fixtures.WithMacro(macroId: "Bad-Id"));
        {
            var (manifest, files) = V11Fixtures.WithMacro();
            Bundle("invalid-macro-without-label", 11, "A macro with a blank label.",
                (manifest with { Macros = new List<WorkflowMacroSpec> { manifest.Macros![0] with { Label = " " } } }, files));
        }

        // The placeholder scanner: closed namespaces, every miss named.
        Bundle("invalid-macro-token-unknown-input", 11,
            "A macro placeholder naming an input the manifest does not declare.",
            V11Fixtures.WithMacro("Stay: {{input:nope}}."));
        Bundle("invalid-macro-token-unknown-run-word", 11,
            "A run: word outside the closed set (date, job, package, host).",
            V11Fixtures.WithMacro("At {{run:time}}."));
        Bundle("invalid-macro-token-profile-signature", 11,
            "profile:signature arrives at 12 (the v12 fold) — on an 11-manifest it is a version requirement, never an unknown word; the signature there is the results[].signature flag.",
            V11Fixtures.WithMacro("Signed {{profile:signature}}."));
        Bundle("invalid-macro-token-unknown-namespace", 11,
            "A namespace outside the closed set.",
            V11Fixtures.WithMacro("Do {{sql:drop}}."));
        Bundle("invalid-macro-token-without-namespace", 11,
            "A token with no namespace at all.",
            V11Fixtures.WithMacro("Just {{closing}}."));

        // The gates at v10 (§ 8): each new form refused below 11 by name.
        var v10WithResults = V11Fixtures.WithResultsList(v10Minimal);
        Invalid("invalid-macros-at-v10", 10,
            "A macros section on a v10 manifest. The section arrives at 11.",
            v10Minimal with { Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") } });
        Invalid("invalid-result-macros-at-v10", 10,
            "A deliverable's macro list on a v10 manifest.",
            v10WithResults with { Results = v10WithResults.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<WorkflowResultMacroSpec> { "closing" } } : r).ToList() });
        Invalid("invalid-result-signature-at-v10", 10,
            "A deliverable's signature flag on a v10 manifest — presence, not truth, is the error.",
            v10WithResults with { Results = v10WithResults.Results!.Select((r, i) => i == 0 ? r with { Signature = false } : r).ToList() });
        Invalid("invalid-reproducible-at-v10", 10,
            "A node's reproducible claim on a v10 manifest.",
            v10Minimal with { Nodes = v10Minimal.Nodes!.Select((n, i) => i == 0 ? n with { Reproducible = true } : n).ToList() });

        // ----- v12 (#623): optional macros, placement, the signature token,
        // the check node, conditional macros, the template node —
        // package-format-v12-design.md §§ 3-5, 8, 13-15. Generated with the
        // gate flipped; published with the v12 prose. -----

        var v11Minimal = V11Fixtures.Minimal();

        // The § 7 control: one edit, nothing of v12 used.
        Bundle("v12-minimal-is-v11-plus-a-line", 12,
            "The migration v12 promises: a valid v11 manifest with specVersion 12 and nothing else changed.",
            (v11Minimal with { SpecVersion = 12 }, V6Fixtures.Files(v11Minimal)));

        Bundle("v12-optional-macro-with-default", 12,
            "An optional macro with its declared default — the per-run choice the setup form offers (§ 3).",
            V12ExportFixtures.OptionalMacro());

        Bundle("v12-macro-placed-before-section", 12,
            "A macro placed before one of its deliverable's aggregated sections (§ 4).",
            V12ExportFixtures.PlacedMacro());

        Bundle("v12-macro-signature-token", 12,
            "A macro embedding {{profile:signature}} — the signature where its macro sits, signed once (§ 5).",
            V12ExportFixtures.SignatureTokenMacro());

        Bundle("v12-check-node", 12,
            "A terms-subset check over two package-declared concept-list extractions, gating one deliverable (§ 13).",
            (V12Fixtures.WithCheck(), V6Fixtures.Files(V12Fixtures.WithCheck())));

        Bundle("v12-conditional-macro", 12,
            "Two when-gated macros on one deliverable — the match/case, decided by a classifier (§ 14).",
            V12ExportFixtures.ConditionalMacro());

        Bundle("v12-template-node", 12,
            "A template node: its prompt renders deterministically and the render IS the output (§ 15).",
            V12ExportFixtures.TemplateNode());

        Bundle("v12-all-constructs-demo", 12,
            "The six constructs in one package: a placed macro, a chosen optional macro, an embedded signature, "
            + "conditional arms on a classifier, a template-rendered letter, and checks on both deliverables — "
            + "the letter's failing deterministically. The rung (g) live demo's own manifest.",
            V12ExportFixtures.AllConstructsDemo());

        // § 3 rules.
        Bundle("invalid-macro-optional-without-default", 12,
            "An optional macro with no default: the package must say what a run that makes no choice does.",
            V12ExportFixtures.OptionalMacro(withDefault: false));
        Bundle("invalid-macro-default-without-optional", 12,
            "A default on a macro that is not optional; only optional: true takes a per-run choice.",
            V12ExportFixtures.OptionalMacro(optional: false));

        // § 4 rules.
        Bundle("invalid-macro-placement-both-anchors", 12,
            "A placed entry naming both before and after; a placement names exactly one.",
            V12ExportFixtures.PlacedMacro(alsoAfter: true));
        Bundle("invalid-macro-placement-anchor-not-aggregated", 12,
            "A placement anchoring on a node its deliverable's aggregator does not aggregate.",
            V12ExportFixtures.PlacedMacro(anchor: "node:scope"));

        // § 5 rules.
        Bundle("invalid-signature-token-in-optional-macro", 12,
            "The signature token inside an optional macro — a per-run signature choice was rejected (#516) and stays rejected.",
            V12ExportFixtures.SignatureTokenMacro(optional: true));
        Bundle("invalid-signature-flag-beside-token", 12,
            "The signed flag on a result whose macro carries the token — a deliverable is signed once.",
            V12ExportFixtures.SignatureTokenMacro(signedFlag: true));
        Bundle("invalid-signature-token-twice", 12,
            "The token in two of one result's macros — a deliverable is signed once.",
            V12ExportFixtures.SignatureTokenMacro(twice: true));

        // § 13 rules, each against the valid check baseline.
        Bundle("invalid-check-without-op", 12, "A check with no op.",
            V12ExportFixtures.BrokenCheck(node => node with { Op = null }));
        Bundle("invalid-check-unknown-op", 12, "A check with an op outside the closed set.",
            V12ExportFixtures.BrokenCheck(node => node with { Op = "terms-equal" }));
        Bundle("invalid-check-without-of", 12, "A check with no of operand.",
            V12ExportFixtures.BrokenCheck(node => node with { Of = null }));
        Bundle("invalid-check-without-in", 12, "A check with no in operand; unreached, its of-extraction also trips the reach rule — a second error that follows from the first.",
            V12ExportFixtures.BrokenCheck(node => node with { In = null }));
        Bundle("invalid-check-without-fail-with", 12, "A check with a blank failWith — a failed check must speak the package's own sentence.",
            V12ExportFixtures.BrokenCheck(node => node with { FailWith = " " }));
        Bundle("invalid-check-of-not-concept-list", 12, "A check operand naming a node without the concept-list contract.",
            V12ExportFixtures.BrokenCheck(node => node with { Of = "node:section-instructions" }));
        Bundle("invalid-check-declares-prompt-fields", 12, "A check carrying the prompt family — the property is the behaviour.",
            V12ExportFixtures.BrokenCheck(node => node with { Prompt = "extract-patient-concepts" }));
        Bundle("invalid-check-reproducible", 12, "reproducible on a check — deterministic by construction, and the claim is not its to make.",
            V12ExportFixtures.BrokenCheck(node => node with { Reproducible = true }));
        Bundle("invalid-check-members-on-a-prompt", 12, "op on a prompt node; only kind check declares it.",
            V12ExportFixtures.CheckPackage(manifest => manifest with
            {
                Nodes = manifest.Nodes!.Select(n => n.Id == "extract-document-terms" ? n with { Op = WorkflowCheckOps.TermsSubset } : n).ToList()
            }));
        Bundle("invalid-result-check-not-a-node-ref", 12, "results[].check must be a node:<id> reference; the unnamed check is then an orphan too — a second error that follows from the first.",
            V12ExportFixtures.CheckPackage(manifest => manifest with
            {
                Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Check = "coverage" } : r).ToList()
            }));
        Bundle("invalid-result-check-not-a-check-node", 12, "results[].check naming a node that is not a check; the real check is then an orphan too — a second error that follows from the first.",
            V12ExportFixtures.CheckPackage(manifest => manifest with
            {
                Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Check = "node:scope" } : r).ToList()
            }));
        Bundle("invalid-check-orphaned", 12, "A check no result names — it gates a deliverable, or it is dead weight.",
            V12ExportFixtures.CheckPackage(manifest => manifest with
            {
                Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Check = null } : r).ToList()
            }));

        // § 14 rules.
        Bundle("invalid-macro-condition-blank", 12, "A macro entry gated by a blank when.",
            V12ExportFixtures.ConditionalMacro(when: "   "));
        Bundle("invalid-macro-condition-value-undeclared", 12, "A macro when comparing a classifier to a value it does not declare.",
            V12ExportFixtures.ConditionalMacro(when: "node:scope == maybe"));
        Bundle("invalid-conditional-signature", 12, "A when-gated macro carrying the token — a conditional signature was rejected (#516) and stays rejected.",
            V12ExportFixtures.ConditionalMacro(template: "Sincerely, {{profile:signature}}"));

        // § 15 rules.
        Bundle("invalid-template-reproducible", 12, "reproducible on a template — deterministic by construction, and the claim is not its to make.",
            V12ExportFixtures.TemplateNode(reproducible: true));
        Bundle("invalid-template-classification-output", 12, "A template whose output schema resolves to the classification contract — a template renders, it does not answer.",
            V12ExportFixtures.TemplateNode(classificationOutput: true));
        Bundle("invalid-node-kind-unknown-at-v12", 12, "An unknown node kind at 12 — the sentence names the four kinds this version may spell; a node of no known kind references no prompt, the second error that follows.",
            V12ExportFixtures.TemplateNode(kind: "router"));

        // The gates at v11 (§ 8): each new form refused below 12 by name.
        Bundle("invalid-macro-optional-at-v11", 11, "optional on a v11 macro. The pair arrives at 12.",
            V12ExportFixtures.AtEleven(V12ExportFixtures.OptionalMacro(withDefault: false, optionalOnly: true)));
        Bundle("invalid-macro-default-at-v11", 11, "default on a v11 macro.",
            V12ExportFixtures.AtEleven(V12ExportFixtures.OptionalMacro(optional: false)));
        Bundle("invalid-result-macro-placement-at-v11", 11, "A placed macro entry on a v11 manifest.",
            V12ExportFixtures.AtEleven(V12ExportFixtures.PlacedMacro()));
        Bundle("invalid-result-macro-when-at-v11", 11, "A when-gated macro entry on a v11 manifest — the object entry form and the when inside it, each refused by version, two sentences.",
            V12ExportFixtures.AtEleven(V12ExportFixtures.ConditionalMacro()));
        Bundle("invalid-result-check-at-v11", 11, "results[].check on a v11 manifest, on a bundle carrying the whole construct: the check kind, each of its four members, the aggregate gap and the reference — every v12 word refused by version at once, seven sentences.",
            V12ExportFixtures.AtEleven((V12Fixtures.WithCheck(), V6Fixtures.Files(V12Fixtures.WithCheck()))));
        Bundle("invalid-template-kind-at-v11", 11, "kind template on a v11 manifest — refused by version before the unknown-kind sentence can fire, and the v11 kind sentence follows, two sentences.",
            V12ExportFixtures.AtEleven(V12ExportFixtures.TemplateNode()));

        return cases;
    }

    /// <summary>The classifier's answer bound into the first prompt node, so a case about the node's own rules is refused for that rule alone.</summary>
    private static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) Consumed((WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle)
    {
        var (manifest, files) = bundle;
        var nodes = manifest.Nodes!.Select(n => n.Aggregate is null && n.Id != "scope" && n.Prompt != null && n.Bindings != null && !n.Bindings.ContainsKey("scope")
            ? n with { Bindings = new Dictionary<string, WorkflowBindingValue>(n.Bindings) { ["scope"] = new("node:scope") } }
            : n).ToList();
        var prompts = manifest.Prompts!.Select(p => nodes.Any(n => n.Prompt == p.Id && n.Bindings?.ContainsKey("scope") == true) && !p.Variables.Contains("scope")
            ? p with { Variables = new List<string>(p.Variables) { "scope" } }
            : p).ToList();
        return (manifest with { Nodes = nodes, Prompts = prompts }, files);
    }

    private static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) WithClassifierAt10(string when) =>
        V10Fixtures.WithClassifier(from: V9Fixtures.Conditional(when) with { SpecVersion = 10 });

    /// <summary>
    /// The demo package's shape (package-format-v10-design.md § 12): one
    /// input, a classifier answering three values, three aggregated
    /// deliverables each firing on one answer, the plan fanned over a data
    /// collection and the letters reading the classifier's answer.
    /// </summary>
    private static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) ScopeDemo()
    {
        var baseline = V10Fixtures.Minimal();
        var manifest = baseline with
        {
            Result = null,
            Title = "Scope classifier demo",
            Description = "A classifier decides whether the referral is in scope; the package produces a plan, a decline letter or a request for information. Not intended for clinical use.",
            Tags = new List<string> { "demo", "classifier" },
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft") },
            Prompts = new List<WorkflowPromptSpec>
            {
                new("classify-scope", "prompts/classify-scope.md", new List<string> { "referral" }),
                new("draft-plan-section", "prompts/draft-plan-section.md", new List<string> { "section_name", "section_content", "consult_draft" }),
                new("draft-decline-letter", "prompts/draft-decline-letter.md", new List<string> { "consult_draft", "scope" }),
                new("draft-information-request", "prompts/draft-information-request.md", new List<string> { "consult_draft" })
            },
            Results = new List<WorkflowResultSpec>
            {
                new("plan", "node:assemble-plan", "Plan", When: "node:scope == in_scope"),
                new("decline_letter", "node:assemble-decline", "Decline letter", When: "node:scope == out_of_scope"),
                new("information_request", "node:assemble-request", "Request for information", When: "node:scope == needs_information")
            },
            Nodes = new List<WorkflowNodeSpec>
            {
                V10Fixtures.Classifier(values: new List<string> { "in_scope", "out_of_scope", "needs_information" }) with { Prompt = "classify-scope" },
                new("draft-plan", "Drafting the plan", Prompt: "draft-plan-section", ForEach: "data:standards",
                    Bindings: new Dictionary<string, WorkflowBindingValue>
                    {
                        ["section_name"] = new("item:name"), ["section_content"] = new("item:content"), ["consult_draft"] = new("input:consult_draft")
                    }),
                new("assemble-plan", "Assembling the plan", Aggregate: new List<string> { "node:draft-plan" }),
                // Every deliverable fans (a deliverable with no fan has no consult):
                // the letters fan over the same collection, one section each in the demo.
                new("draft-decline", "Drafting the decline letter", Prompt: "draft-decline-letter", ForEach: "data:standards",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("input:consult_draft"), ["scope"] = new("node:scope") }),
                new("assemble-decline", "Assembling the decline letter", Aggregate: new List<string> { "node:draft-decline" }),
                new("draft-request", "Drafting the request for information", Prompt: "draft-information-request", ForEach: "data:standards",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("input:consult_draft") }),
                new("assemble-request", "Assembling the request", Aggregate: new List<string> { "node:draft-request" })
            }
        };
        var files = new Dictionary<string, string>(V6Fixtures.Files(baseline), StringComparer.Ordinal)
        {
            ["prompts/classify-scope.md"] = "Is this referral within the clinic's scope? {{ referral }}",
            ["prompts/draft-plan-section.md"] = "Draft the {{ section_name }} section of the plan from {{ section_content }} and {{ consult_draft }}.",
            ["prompts/draft-decline-letter.md"] = "Write a decline letter for {{ consult_draft }}; the referral was classified {{ scope }}.",
            ["prompts/draft-information-request.md"] = "Write a request for the information missing from {{ consult_draft }}."
        };
        return (manifest, files);
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
