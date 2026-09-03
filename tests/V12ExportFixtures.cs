using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v12 (#623): the bundles the conformance export publishes — each valid
/// shape once, each rule broken once against an otherwise-valid baseline,
/// and the six-construct demo that is ALSO the rung (g) live demo's own
/// manifest (one source of truth).
/// </summary>
public static class V12ExportFixtures
{
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) AtEleven(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle) =>
        (bundle.Manifest with { SpecVersion = 11 }, bundle.Files);

    /// <summary>§ 3: the optional pair on the standing macro fixture.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) OptionalMacro(
        bool optional = true, bool withDefault = true, bool optionalOnly = false)
    {
        var (manifest, files) = V11Fixtures.WithMacro(from: V12Fixtures.Minimal());
        return (manifest with
        {
            Macros = manifest.Macros!.Select(m => m with
            {
                Optional = optional ? true : null,
                Default = withDefault && !optionalOnly ? true : null
            }).ToList()
        }, files);
    }

    /// <summary>§ 4: a placed entry anchored on the deliverable's own aggregated section.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) PlacedMacro(
        string anchor = "node:section-instructions", bool alsoAfter = false)
    {
        var (manifest, files) = V11Fixtures.WithMacro(from: V12Fixtures.Minimal());
        return (manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with
                {
                    Macros = new List<WorkflowResultMacroSpec>
                    {
                        new("disclaimer", Before: anchor, After: alsoAfter ? anchor : null)
                    }
                }
                : r).ToList()
        }, files);
    }

    /// <summary>§ 5: a macro carrying the signature token, and its refusals.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) SignatureTokenMacro(
        bool optional = false, bool signedFlag = false, bool twice = false)
    {
        var (manifest, files) = V11Fixtures.WithMacro(
            "Sincerely,\n\n{{profile:signature}}", from: V12Fixtures.Minimal());

        if (optional)
        {
            manifest = manifest with
            {
                Macros = manifest.Macros!.Select(m => m with { Optional = true, Default = true }).ToList()
            };
        }

        if (signedFlag)
        {
            manifest = manifest with
            {
                Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Signature = true } : r).ToList()
            };
        }

        if (twice)
        {
            var mutable = new Dictionary<string, string>(files, StringComparer.Ordinal)
            {
                ["macros/second.md"] = "Also signed: {{profile:signature}}"
            };
            manifest = manifest with
            {
                Macros = new List<WorkflowMacroSpec>(manifest.Macros!)
                {
                    new("second", "Second signature", "macros/second.md")
                },
                Results = manifest.Results!.Select((r, i) => i == 0
                    ? r with { Macros = new List<WorkflowResultMacroSpec> { "disclaimer", "second" } }
                    : r).ToList()
            };
            return (manifest, mutable);
        }

        return (manifest, files);
    }

    /// <summary>§ 14: a classifier and a when-gated macro entry (the match/case's one arm).</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) ConditionalMacro(
        string when = "node:scope == in_scope",
        string template = "This paragraph is gated.")
    {
        var (manifest, files) = V10Fixtures.WithClassifier(from: V12Fixtures.Minimal());
        manifest = manifest with
        {
            Macros = new List<WorkflowMacroSpec>
            {
                new("gated", "Gated paragraph", "macros/gated.md"),
                new("other", "Other arm", "macros/other.md")
            },
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with
                {
                    Macros = new List<WorkflowResultMacroSpec>
                    {
                        new("gated", When: when),
                        new("other", When: "node:scope == out_of_scope")
                    }
                }
                : r).ToList()
        };
        var all = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["macros/gated.md"] = template,
            ["macros/other.md"] = "The other arm's paragraph."
        };
        return (manifest, all);
    }

    /// <summary>§ 15: a template node feeding the deliverable's aggregator.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) TemplateNode(
        bool reproducible = false, bool classificationOutput = false, string? kind = null)
    {
        var manifest = V12Fixtures.Minimal();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("header", "prompts/header.md", new List<string> { "seen_on" })
        };
        var template = new WorkflowNodeSpec("patient-header", "Patient header",
            Prompt: "header",
            Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
            Kind: kind ?? WorkflowNodeKinds.Template,
            Reproducible: reproducible ? true : null,
            Output: classificationOutput ? new WorkflowNodeOutputSpec("verdict") : null);
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!) { template };
        nodes = nodes.Select(n => n.Aggregate != null
            ? n with { Aggregate = new List<string>(n.Aggregate) { "node:patient-header" } }
            : n).ToList();
        manifest = manifest with { Prompts = prompts, Nodes = nodes };

        if (classificationOutput)
        {
            manifest = manifest with
            {
                Schemas = new Dictionary<string, string>(manifest.Schemas ?? new Dictionary<string, string>())
                {
                    ["verdict"] = "schemas/verdict.json"
                }
            };
        }

        var files = V6Fixtures.Files(manifest);
        files["prompts/header.md"] = classificationOutput ? "{\"value\": \"yes\"}" : "Seen on {{ seen_on }}.";
        if (classificationOutput)
        {
            files["schemas/verdict.json"] = TestOutputContracts.CatalogSchemas["classification"];
        }

        return (manifest, files);
    }

    /// <summary>§ 13: the valid check baseline with one node rule broken.</summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) BrokenCheck(
        Func<WorkflowNodeSpec, WorkflowNodeSpec> mutate) =>
        CheckPackage(manifest => manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "coverage" ? mutate(n) : n).ToList()
        });

    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) CheckPackage(
        Func<WorkflowPackageManifest, WorkflowPackageManifest> mutate)
    {
        var manifest = mutate(V12Fixtures.WithCheck());
        return (manifest, V6Fixtures.Files(manifest));
    }

    /// <summary>
    /// The six constructs in one package — and the live demo's manifest. Two
    /// deliverables: the consult note (a placed macro, two conditional arms
    /// on a tone classifier, a chosen optional macro, a signature-carrying
    /// closing, a template header, and a coverage check that passes) and the
    /// acknowledgement letter (template-rendered fixed text whose coverage
    /// check fails deterministically — the input's clinical terms cannot be
    /// covered by text that never mentions them).
    /// </summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) AllConstructsDemo()
    {
        var baseline = V12Fixtures.Minimal();
        var manifest = baseline with
        {
            Schemas = new Dictionary<string, string>(baseline.Schemas ?? new Dictionary<string, string>())
            {
                ["concept-list"] = "schemas/concept-list.json"
            },
            Prompts = new List<WorkflowPromptSpec>(baseline.Prompts!)
            {
                new("classify-tone", "prompts/classify-tone.md", new List<string> { "referral" }),
                new("extract-terms", "prompts/extract-terms.md", new List<string> { "text" }),
                new("header", "prompts/header.md", new List<string> { "seen_on" }),
                new("letter-body", "prompts/letter-body.md", new List<string>())
            },
            Macros = new List<WorkflowMacroSpec>
            {
                new("disclaimer", "Standing disclaimer", "macros/disclaimer.md"),
                new("formal_note", "Formal closing note", "macros/formal_note.md"),
                new("friendly_note", "Friendly closing note", "macros/friendly_note.md"),
                new("followup", "Follow-up offer", "macros/followup.md", Optional: true, Default: true),
                new("closing", "Signature closing", "macros/closing.md")
            },
            Nodes = new List<WorkflowNodeSpec>(baseline.Nodes!)
            {
                new("tone", "Reading the referral's tone", Prompt: "classify-tone",
                    Kind: WorkflowNodeKinds.Classifier,
                    Values: new List<string> { "formal", "friendly" },
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["referral"] = new("input:consult_draft") }),
                new("patient-header", "Patient header", Prompt: "header",
                    Kind: WorkflowNodeKinds.Template,
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") }),
                new("extract-input-terms", "Extracting the referral's terms", Prompt: "extract-terms",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["text"] = new("input:consult_draft") },
                    Output: new WorkflowNodeOutputSpec("concept-list")),
                new("extract-note-terms", "Extracting the note's terms", Prompt: "extract-terms",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["text"] = new("node:assemble-note") },
                    Output: new WorkflowNodeOutputSpec("concept-list")),
                new("note-coverage", "Note coverage check", Kind: WorkflowNodeKinds.Check,
                    Op: WorkflowCheckOps.TermsSubset,
                    Of: "node:extract-input-terms", In: "node:extract-note-terms",
                    FailWith: "The note does not cover every clinical term found in the referral."),
                // A FANNED template (rung (j): forEach from day one) — the
                // deliverable rule wants a fan, and a template fan keeps the
                // letter deterministic: fixed text, once per section.
                new("letter-body", "Acknowledgement body", Prompt: "letter-body",
                    Kind: WorkflowNodeKinds.Template,
                    ForEach: "data:standards"),
                new("assemble-letter", "Assembling the acknowledgement",
                    Aggregate: new List<string> { "node:letter-body" }),
                new("extract-letter-terms", "Extracting the letter's terms", Prompt: "extract-terms",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["text"] = new("node:assemble-letter") },
                    Output: new WorkflowNodeOutputSpec("concept-list")),
                new("letter-coverage", "Letter coverage check", Kind: WorkflowNodeKinds.Check,
                    Op: WorkflowCheckOps.TermsSubset,
                    Of: "node:extract-input-terms", In: "node:extract-letter-terms",
                    FailWith: "The acknowledgement does not cover the referral's clinical terms.")
            }
            .Select(n => n.Aggregate != null && n.Id == "assemble-note"
                ? n with { Aggregate = new List<string>(n.Aggregate) { "node:patient-header" } }
                : n).ToList(),
            Results = new List<WorkflowResultSpec>
            {
                baseline.Results![0] with
                {
                    Macros = new List<WorkflowResultMacroSpec>
                    {
                        new("disclaimer", Before: "node:patient-header"),
                        new("formal_note", When: "node:tone == formal"),
                        new("friendly_note", When: "node:tone == friendly"),
                        "followup",
                        "closing"
                    },
                    Check = "node:note-coverage"
                },
                new("ack_letter", "node:assemble-letter", "Acknowledgement letter",
                    Check: "node:letter-coverage")
            }
        };

        var files = V6Fixtures.Files(manifest);
        files["prompts/classify-tone.md"] = "Is this referral's tone formal or friendly? {{ referral }}";
        files["prompts/extract-terms.md"] = "Extract the clinical terms as concept-list JSON: {{ text }}";
        files["prompts/header.md"] = "Prepared regarding the consultation of {{ seen_on }}.";
        files["prompts/letter-body.md"] = "We have received this referral and will be in touch with an appointment.";
        files["macros/disclaimer.md"] = "This document was generated with decision support; the signing clinician reviewed it.";
        files["macros/formal_note.md"] = "We remain at your disposal for any further questions.";
        files["macros/friendly_note.md"] = "Please reach out any time if anything changes.";
        files["macros/followup.md"] = "A follow-up appointment will be arranged within three months.";
        files["macros/closing.md"] = "Sincerely,\n\n{{profile:signature}}";
        files["schemas/concept-list.json"] = TestOutputContracts.ConceptListSchema;
        return (manifest, files);
    }
}
