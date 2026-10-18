using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A5 (#1018): the specVersion 25 conformance vectors — the object-template
/// format (package-format-v25.md). The valid baselines are the design's
/// minimal package, the v24 minimal run through the 24→25 rewrite (the
/// "minimal is the previous version plus a line" convention cannot hold at 25:
/// every v24 key the rewrite retires is refused by name), and a rich case.
/// Each invalid case breaks one rule against one of three otherwise-valid
/// bases; the recorded outcome is whatever the validator says. Cross-field
/// rules are SHAPE_BLIND in the registry's validate.yml.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static readonly JsonSerializerOptions MigrationJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string Prelude = "snomed-tool-guidance";
    private const string PreludeMacro = "snomed_tool_guidance";

    /// <summary>
    /// The v24 minimal with its prelude already a node macro and nothing else
    /// changed — what the 23 editor step leaves behind (#963) and what the
    /// rewrite takes in. Valid at 24 (asserted by a fact).
    /// </summary>
    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) V24MinimalWithoutPreludes()
    {
        var v24 = V18Fixtures.Minimal() with { SpecVersion = 24 };
        var preludePrompts = v24.Prompts!.Where(p => p.Prelude == Prelude).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        var manifest = v24 with
        {
            Preludes = null,
            Prompts = v24.Prompts!.Select(p => p with { Prelude = null }).ToList(),
            Macros = new List<WorkflowMacroSpec> { new(PreludeMacro, "SNOMED tool guidance", $"macros/{PreludeMacro}.md") },
            Nodes = v24.Nodes!.Select(n => n.Prompt is not null && preludePrompts.Contains(n.Prompt)
                ? n with { Macros = new List<WorkflowNodeMacroSpec> { new(PreludeMacro) } }
                : n).ToList(),
        };

        var files = V6Fixtures.Files(v24);
        files.Remove($"prompts/_{Prelude}.md");
        files[$"macros/{PreludeMacro}.md"] = "Use short SNOMED search terms.";
        return (manifest, files);
    }

    /// <summary>The v24 minimal through WorkflowPackageMigration24To25 — the migration's promise, as a vector.</summary>
    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) MigratedV24Minimal()
    {
        var (manifest, files) = V24MinimalWithoutPreludes();
        var result = WorkflowPackageMigration24To25.Migrate(JsonSerializer.Serialize(manifest, MigrationJson), files, V24Package.ResolveContract);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The v24 minimal does not migrate: " + string.Join(" | ", result.Refusals));
        }

        return (V24Package.Manifest(result.ManifestJson), new Dictionary<string, string>(result.Files, StringComparer.Ordinal));
    }

    private static void AddV25Cases(List<Case> cases)
    {
        void Invalid(string id, string description, (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle, int spec = 25)
            => cases.Add(new Case(id, spec, description, bundle.Manifest, bundle.Files));
        void Valid(string id, string description, (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 25, description, bundle.Manifest, bundle.Files));

        // ----- bases -----
        var m = V25Package.Minimal();
        // the fanned prompt reading no item, so a broken forEach yields its one sentence
        var f = V25Package.WithPrompt(m, "section", "Section notes for {{ input.notes }}.");
        // an unused package-local format, so each file defect yields its one sentence
        var l = Files(Manifest(m, x => x with { Formats = new List<WorkflowFormatReference> { new("meds", "formats/meds.json") } }),
            fs => fs["formats/meds.json"] = FormatFixtures.LocalCustom);

        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Manifest((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Func<WorkflowPackageManifest, WorkflowPackageManifest> change)
            => (change(b.Manifest), b.Files);
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Files((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Action<Dictionary<string, string>> change)
        {
            var files = new Dictionary<string, string>(b.Files, StringComparer.Ordinal);
            change(files);
            return (b.Manifest, files);
        }
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Node((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, string id, Func<WorkflowNodeSpec, WorkflowNodeSpec> change)
            => Manifest(b, x => V25Package.WithNode(x, id, change));
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Deliverable((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Func<WorkflowDeliverableSpec, WorkflowDeliverableSpec> change)
            => Manifest(b, x => x with { Deliverables = x.Deliverables!.Select(change).ToList() });
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Macro((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Func<WorkflowMacroSpec, WorkflowMacroSpec> change)
            => Manifest(b, x => x with { Macros = x.Macros!.Select(change).ToList() });
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) P(string text) => V25Package.WithPrompt(m, "assessment", text);
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) D(string when) => Deliverable(m, d => d with { When = when });
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) L(string formatFile) => Files(l, fs => fs["formats/meds.json"] = formatFile);
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Fan(string forEach) => Node(f, "section_notes", n => n with { ForEach = forEach });
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) With(string nodeId, Dictionary<string, JsonNode?> with) => Node(m, nodeId, n => n with { With = with });
        static Dictionary<string, JsonNode?> Coverage(string of = "node.extract", string @in = "node.extract", string? failWith = "The note names problems the source does not.")
        {
            var with = new Dictionary<string, JsonNode?> { ["of"] = of, ["in"] = @in };
            if (failWith is not null) { with["failWith"] = failWith; }
            return with;
        }

        // ======================================================= valid
        Valid("v25-minimal",
            "The design's minimal 25 package: a prose node reading an input and a snippet, a concept_list node, a classification taking its values through with, a terms_subset check, a node fanning a snippet folder, an aggregator, a macro with an instruction placed after the prose node, one deliverable naming the aggregator and the check.",
            m);

        Valid("v25-minimal-is-the-migrated-v24-minimal",
            "The migration v25 promises: the v24 minimal — its prelude already a node macro, as the 23 editor leaves it — run through WorkflowPackageMigration24To25 and nothing else changed. Valid with no warnings: each concept-list .content read the rewrite spelled through a renderer pipe is typed by the pipe (#1058).",
            MigratedV24Minimal());

        {
            var rich = Manifest(m, x => x with
            {
                Inputs = x.Inputs!.Append(new WorkflowInputSpec("history", "History", false, "array", Items: new WorkflowElementSpec("text"))).ToList(),
                Prompts = x.Prompts!
                    .Append(new WorkflowPromptSpec("summary", "prompts/summary.md", new List<string>()))
                    .Append(new WorkflowPromptSpec("per_event", "prompts/per_event.md", new List<string>()))
                    .ToList(),
                Macros = x.Macros!.Append(new WorkflowMacroSpec("scope_line", "Scope line", "macros/scope_line.md", Instruction: "State the scope once.")).ToList(),
                Nodes = x.Nodes!
                    .Select(n => n.Id switch
                    {
                        "assessment" => n with { FailIfEmpty = "The assessment is empty." },
                        "extract" => n with { FailIfEmpty = "No concepts were found." },
                        "all" => n with { Aggregate = n.Aggregate!.Append("node.summary").Append("node.per_event").ToList() },
                        _ => n,
                    })
                    .Append(new WorkflowNodeSpec("summary", "Summary", Prompt: "summary", Format: "rendered"))
                    .Append(new WorkflowNodeSpec("per_event", "Per event", Prompt: "per_event", Format: "prose", ForEach: "input.history"))
                    .ToList(),
                Deliverables = new List<WorkflowDeliverableSpec>
                {
                    new("consult", "node.all", "Consultation note", Check: "node.coverage", Macros: new List<WorkflowDeliverableMacroSpec>
                    {
                        new("closing", At: WorkflowDeliverableMacroSpec.Prepend, When: "input.kind == \"follow_up\""),
                        new("closing", At: WorkflowDeliverableMacroSpec.Append),
                        new("scope_line", At: WorkflowDeliverableMacroSpec.Prepend),
                    }),
                },
            });
            rich = Files(rich, fs =>
            {
                fs["macros/scope_line.md"] = "Scope: {{ node.scope.value }}.";
                fs["prompts/section.md"] = "{{ item.name }}: {{ item.content }} {{ macro.closing.prompt }}";
                fs["prompts/summary.md"] = "Summary of {{ node.assessment.content }}.";
                fs["prompts/per_event.md"] = "Event: {{ item.value }}";
            });
            Valid("v25-placed-and-inline-macros",
                "The rich case: a deliverable macro prepended under a gate, appended, and a second prepended; failIfEmpty on a text and an array format; the fanned prompt reading the item and the inline macro per item; a rendered node over the prose; an optional array input fanned per event; one aggregator collecting all.",
                rich);
        }

        {
            // #1065: a template may read an aggregator's composed text — the letter
            // node reads the note aggregator (the two-documents shape); its own
            // aggregator and deliverable keep the fan rule satisfied through the read.
            var letter = Files(Manifest(m, x => x with
            {
                Prompts = x.Prompts!.Append(new WorkflowPromptSpec("letter", "prompts/letter.md", new List<string>())).ToList(),
                Nodes = x.Nodes!
                    .Append(new WorkflowNodeSpec("letter", "Letter", Prompt: "letter", Format: "prose"))
                    .Append(new WorkflowNodeSpec("letter_all", "Letter, assembled", Aggregate: new List<string> { "node.letter" }))
                    .ToList(),
                Deliverables = x.Deliverables!.Append(new WorkflowDeliverableSpec("letter", "node.letter_all", "Letter")).ToList(),
            }), fs => fs["prompts/letter.md"] = "Letter from:\n{{ node.all.content }}");
            (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) LetterPrompt(string text) => Files(letter, fs => fs["prompts/letter.md"] = text);

            Valid("v25-prompt-reads-an-aggregator",
                "A letter node reading the note aggregator's composed text (node.all.content), its own aggregator and deliverable — the two-documents shape; the read is the edge that reaches the fan.",
                letter);
            Invalid("invalid-aggregator-read-as-a-value", "An aggregator's .value — its composed text is no format's output and has no value (the .content read beside it keeps the deliverable's fan edge, so the case names one rule).",
                LetterPrompt("{{ node.all.content }} {{ node.all.value }}"));
            Invalid("invalid-aggregator-read-through-a-renderer-pipe", "An aggregator's text through a renderer pipe — a renderer renders a concept_list node's text.",
                LetterPrompt("{{ node.all.content | concept_bullets }}"));
            Invalid("invalid-aggregator-declares-for-each", "An aggregator declaring forEach — an aggregator is structural.",
                Node(m, "all", n => n with { ForEach = "snippet.sections" }));
            Invalid("invalid-condition-reads-an-aggregator", "A node's when reading an aggregator's text — a condition decides before the sections compose.",
                Node(letter, "letter", n => n with { When = "node.all.content != \"\"" }));
        }

        Valid("v25-structured-content-read-warns",
            "A prompt reading node.<id>.content of an object format — valid, with the warning that the JSON renders as text and .value has the fields.",
            P("{{ snippet.standards.content }} {{ input.notes }} {{ node.extract.content }}"));

        Valid("v25-folder-item-lacking-the-inline-read-warns",
            "One item of a fanned folder carries the inline macro read and the other does not — valid, with the warning that nothing tells the model to write the macro for the items without it.",
            Files(m, fs => fs["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}"));

        // ======================================================= the v24 gates (schema-catchable: the member is absent from the v24 schema)
        {
            var v24 = (Manifest: V18Fixtures.Minimal() with { SpecVersion = 24 }, Files: (Dictionary<string, string>)null!);
            v24 = (v24.Manifest, V6Fixtures.Files(v24.Manifest));
            var consult = v24.Manifest.Results![0].Id;

            Invalid("invalid-deliverables-at-v24", "deliverables on a v24 manifest. The key arrives at 25 (results until then).",
                Manifest(v24, x => x with { Deliverables = x.Results }), 24);
            Invalid("invalid-snippets-at-v24", "snippets on a v24 manifest. The key arrives at 25 (data until then).",
                Manifest(v24, x => x with { Snippets = new Dictionary<string, string> { ["x"] = "snippets/x.md" } }), 24);
            Invalid("invalid-formats-at-v24", "formats on a v24 manifest. Package-local formats arrive at 25 (schemas until then).",
                Manifest(v24, x => x with { Formats = new List<WorkflowFormatReference> { new("x", "formats/x.json") } }), 24);
            var (withMacro, withMacroFiles) = V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId });
            Invalid("invalid-macro-instruction-at-v24", "A macro's instruction on a v24 manifest. The instruction arrives at 25.",
                (withMacro with { Macros = withMacro.Macros!.Select(mc => mc with { Instruction = "x" }).ToList() }, withMacroFiles), 24);
            var first = v24.Manifest.Nodes![0].Id;
            Invalid("invalid-node-format-at-v24", "A node's format on a v24 manifest. Formats arrive at 25 (output.schema until then).",
                Node(v24, first, n => n with { Format = "prose" }), 24);
            Invalid("invalid-node-with-at-v24", "A node's with on a v24 manifest. Parameters arrive at 25.",
                Node(v24, first, n => n with { With = new Dictionary<string, JsonNode?> { ["x"] = "y" } }), 24);
            Invalid("invalid-node-on-invalid-at-v24", "A node's onInvalid on a v24 manifest. The policy arrives at 25.",
                Node(v24, first, n => n with { OnInvalid = WorkflowOnInvalid.Fail }), 24);
            Invalid("invalid-node-fail-if-empty-at-v24", "failIfEmpty on the node of a v24 manifest. Below 25 it is output.failIfEmpty.",
                Node(v24, first, n => n with { FailIfEmpty = "Empty." }), 24);
            Invalid("invalid-result-macro-at-at-v24", "A deliverable macro's at on a v24 manifest. The prepend|append placement arrives at 25 (bare = appended until then).",
                (withMacro with { Results = withMacro.Results!.Select(r => r.Id == consult ? r with { Macros = new List<WorkflowDeliverableMacroSpec> { new(V24Fixtures.MacroId, At: WorkflowDeliverableMacroSpec.Append) } } : r).ToList() }, withMacroFiles), 24);
            Invalid("invalid-data-item-when-at-v24", "A data collection item's when on a v24 manifest. Per-item gates arrive at 25 (a snippet folder's index).",
                Files(v24, fs =>
                {
                    var index = JsonNode.Parse(fs["data/standards/index.json"])!.AsObject();
                    index["items"]![0]!["when"] = "x";
                    fs["data/standards/index.json"] = index.ToJsonString();
                }), 24);
        }

        // ======================================================= § 10 retired keys (schema-catchable: the key leaves the closed schema)
        Invalid("invalid-preludes-at-25", "preludes at 25 — retired; the text is read inline from a snippet.",
            Manifest(m, x => x with { Preludes = new Dictionary<string, string> { ["x"] = "prompts/_x.md" } }));
        Invalid("invalid-data-at-25", "data at 25 — retired; snippets replace it.",
            Manifest(m, x => x with { Data = new Dictionary<string, string> { ["x"] = "data/x.md" } }));
        Invalid("invalid-schemas-at-25", "schemas at 25 — retired; formats replace it.",
            Manifest(m, x => x with { Schemas = new Dictionary<string, string> { ["x"] = "schemas/x.json" } }));
        Invalid("invalid-schema-refs-at-25", "schemaRefs at 25 — retired; formats replace it.",
            Manifest(m, x => x with { SchemaRefs = new Dictionary<string, string> { ["x"] = "concept-list" } }));
        Invalid("invalid-custom-schemas-at-25", "customSchemas at 25 — retired; a package-local format replaces it.",
            Manifest(m, x => x with { CustomSchemas = new Dictionary<string, string> { ["x"] = "schemas/x.json" } }));
        Invalid("invalid-results-at-25", "results at 25 — retired; deliverables replace it.",
            Manifest(m, x => x with { Results = x.Deliverables }));
        Invalid("invalid-result-at-25", "The string-form result at 25 — retired; deliverables replace it.",
            Manifest(m, x => x with { Result = "node:all" }));
        Invalid("invalid-prompt-prelude-at-25", "A prompt's prelude at 25 — retired; the text is read inline from a snippet.",
            Manifest(m, x => x with { Prompts = x.Prompts!.Select(p => p.Id == "assessment" ? p with { Prelude = "p" } : p).ToList() }));
        Invalid("invalid-prompt-variables-at-25", "A prompt's variables at 25 — retired; a template reads objects and its reads are derived.",
            Manifest(m, x => x with { Prompts = x.Prompts!.Select(p => p.Id == "assessment" ? p with { Variables = new List<string> { "notes" } } : p).ToList() }));
        Invalid("invalid-prompt-raw-at-25", "A prompt's raw flag at 25 — retired; a literal '{{' is escaped.",
            Manifest(m, x => x with { Prompts = x.Prompts!.Select(p => p.Id == "assessment" ? p with { Raw = false } : p).ToList() }));
        Invalid("invalid-node-bindings-at-25", "A node's bindings at 25 — retired; the template's reads are the edges.",
            Node(m, "assessment", n => n with { Bindings = new Dictionary<string, WorkflowBindingValue> { ["notes"] = new("input:notes") } }));
        Invalid("invalid-node-output-at-25", "A node's output at 25 — retired; the node names its format.",
            Node(m, "assessment", n => n with { Output = new WorkflowNodeOutputSpec("s") }));
        Invalid("invalid-node-kind-at-25", "A node's kind at 25 — retired; the format says what the node is.",
            Node(m, "assessment", n => n with { Kind = "template" }));
        Invalid("invalid-node-values-at-25", "A node's values at 25 — retired; a classification takes them through with.",
            Node(m, "assessment", n => n with { Values = new List<string> { "a", "b" } }));
        Invalid("invalid-node-op-at-25", "A check's op at 25 — retired; a check is a node of a computed format.",
            Node(m, "assessment", n => n with { Op = "terms-subset" }));
        Invalid("invalid-node-of-at-25", "A check's of at 25 — retired; with: { of } on a computed format.",
            Node(m, "assessment", n => n with { Of = "node:extract" }));
        Invalid("invalid-node-in-at-25", "A check's in at 25 — retired; with: { in } on a computed format.",
            Node(m, "assessment", n => n with { In = "node:extract" }));
        Invalid("invalid-node-fail-with-at-25", "A check's failWith at 25 — retired; with: { failWith } on a computed format.",
            Node(m, "assessment", n => n with { FailWith = "x" }));
        Invalid("invalid-deliverable-macro-before-at-25", "A deliverable macro anchored before a node at 25 — retired; a node instance places it.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append, Before: "node:assessment") } }));
        Invalid("invalid-deliverable-macro-after-at-25", "A deliverable macro anchored after a node at 25 — retired; a node instance places it.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append, After: "node:assessment") } }));
        Invalid("invalid-deliverable-macro-for-item-at-25", "A deliverable macro's forItem at 25 — retired; a node instance anchors to the item.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append, ForItem: "hpi") } }));
        Invalid("invalid-deliverable-macro-slot-at-25", "A slot entry at 25 — retired; the template reads macro.<id>.prompt where the inline macro goes.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", Slot: true, At: WorkflowDeliverableMacroSpec.Append) } }));

        // ======================================================= ids (schema-catchable: the id patterns)
        Invalid("invalid-node-id-not-snake-case", "A hyphenated node id — a node is a Scriban member at 25.",
            Node(m, "scope", n => n with { Id = "scope-check" }));
        Invalid("invalid-prompt-id-not-snake-case", "A hyphenated prompt id — every declared id is snake_case at 25.",
            Manifest(Node(m, "scope", n => n with { Prompt = "scope-prompt" }), x => x with { Prompts = x.Prompts!.Select(p => p.Id == "scope" ? p with { Id = "scope-prompt" } : p).ToList() }));
        Invalid("invalid-format-id-not-snake-case", "A package-local format id that is not snake_case.",
            Files(Manifest(m, x => x with { Formats = new List<WorkflowFormatReference> { new("Medication-List", "formats/m.json") } }), fs => fs["formats/m.json"] = FormatFixtures.LocalCustom));
        Invalid("invalid-snippet-id-not-snake-case", "A snippet id that is not snake_case — a snippet is a Scriban member.",
            Files(Manifest(m, x => x with { Snippets = new Dictionary<string, string>(x.Snippets!) { ["Extra-Notes"] = "snippets/extra.md" } }), fs => fs["snippets/extra.md"] = "Extra."));
        Invalid("invalid-macro-id-not-snake-case-at-25", "A macro id that is not snake_case — a macro is a Scriban member.",
            Files(Node(Manifest(m, x => x with { Macros = x.Macros!.Append(new WorkflowMacroSpec("Closing-Line", "Closing line", "macros/closing_line.md", Instruction: "x")).ToList() }),
                "assessment", n => n with { Macros = n.Macros!.Append(new WorkflowNodeMacroSpec("Closing-Line", At: WorkflowNodeMacroSpec.After)).ToList() }),
                fs => fs["macros/closing_line.md"] = "Line."));
        Invalid("invalid-deliverable-id-not-snake-case", "A deliverable id that is not snake_case.",
            Deliverable(m, d => d with { Id = "Consult-Note" }));

        // ======================================================= formats (SHAPE_BLIND: a format's rules live in its file and the catalogue)
        Invalid("invalid-format-declared-twice", "The same package-local format id declared twice.",
            Manifest(l, x => x with { Formats = x.Formats!.Concat(x.Formats!).ToList() }));
        Invalid("invalid-format-is-a-catalogue-id", "A package-local format taking a catalogue id — catalogue ids are reserved.",
            Files(Manifest(m, x => x with { Formats = new List<WorkflowFormatReference> { new("prose", "formats/prose.json") } }), fs => fs["formats/prose.json"] = FormatFixtures.LocalCustom));
        Invalid("invalid-format-file-missing", "A declared format whose file is not in the package.",
            Files(l, fs => fs.Remove("formats/meds.json")));
        Invalid("invalid-format-file-not-json", "A format file that is not JSON.", L("{ nope"));
        Invalid("invalid-format-unknown-type", "A format of a type the grammar does not have.",
            L("""{ "type": "blob", "producer": "custom", "description": "x" }"""));
        Invalid("invalid-format-unknown-producer", "A format of a producer the grammar does not have.",
            L("""{ "type": "object", "producer": "magic", "description": "x" }"""));
        Invalid("invalid-format-not-custom", "A package-local format whose producer is not custom — attested, rendered and computed formats come from the catalogue.",
            L(FormatFixtures.Prose));
        Invalid("invalid-format-custom-not-object", "A custom format that is not an object — the package's own shape is an object the engine validates.",
            L("""{ "type": "text", "producer": "custom", "description": "x" }"""));
        Invalid("invalid-format-object-without-schema", "An object format without a schema.",
            L("""{ "type": "object", "producer": "custom", "description": "x" }"""));
        Invalid("invalid-format-description-blank", "A format with a blank description.",
            L(FormatFixtures.LocalCustom.Replace("\"A medication list.\"", "\" \"")));
        Invalid("invalid-format-custom-with-parameters", "A custom format declaring parameters — only attested and computed formats take input through with.",
            L(FormatFixtures.LocalCustom.Replace("\"description\": \"A medication list.\",", "\"description\": \"A medication list.\", \"parameters\": { \"x\": { \"kind\": \"text\" } },")));
        Invalid("invalid-format-values-without-enum", "values on a format that is not an enum.",
            L(FormatFixtures.LocalCustom.Replace("\"description\": \"A medication list.\",", "\"description\": \"A medication list.\", \"values\": [\"a\", \"b\"],")));
        Invalid("invalid-format-output-macros-on-object", "outputMacros on an object format — macros are placed on prose only.",
            L(FormatFixtures.LocalCustom.Replace("\"outputMacros\": false", "\"outputMacros\": true")));

        // ======================================================= nodes
        Invalid("invalid-node-without-format", "A node that is not an aggregator and names no format.",
            Node(m, "assessment", n => n with { Format = null }));
        Invalid("invalid-node-format-unknown", "A node naming a format that is neither in the package nor in the catalogue.",
            Node(m, "assessment", n => n with { Format = "essay" }));
        Invalid("invalid-node-label-missing", "A node without a label.",
            Node(m, "scope", n => n with { Label = "" }));
        Invalid("invalid-duplicate-node-id", "Two nodes with one id.",
            Manifest(m, x => x with { Nodes = x.Nodes!.Append(V25Package.Node(x, "scope")).ToList() }));
        Invalid("invalid-aggregator-declares-format", "An aggregator naming a format — an aggregator is structural.",
            Node(m, "all", n => n with { Format = "prose" }));
        Invalid("invalid-aggregator-declares-prompt", "An aggregator naming a prompt — an aggregator is structural.",
            Node(m, "all", n => n with { Prompt = "assessment" }));
        Invalid("invalid-aggregate-source-colon-spelling", "An aggregate source in the ≤24 spelling (node:<id>).",
            Node(m, "all", n => n with { Aggregate = n.Aggregate!.Append("node:assessment").ToList() }));
        Invalid("invalid-aggregate-source-not-a-reference", "An aggregate source that is a bare id, not node.<id>.",
            Node(m, "all", n => n with { Aggregate = n.Aggregate!.Append("assessment").ToList() }));
        Invalid("invalid-aggregate-source-with-member", "An aggregate source reading a member — a source is the node bare.",
            Node(m, "all", n => n with { Aggregate = n.Aggregate!.Append("node.assessment.content").ToList() }));
        Invalid("invalid-aggregate-source-unknown", "An aggregate source naming a node the package does not declare.",
            Node(m, "all", n => n with { Aggregate = n.Aggregate!.Append("node.nope").ToList() }));
        Invalid("invalid-aggregator-aggregates-itself", "An aggregator among its own sources — and the cycle it makes.",
            Node(m, "all", n => n with { Aggregate = n.Aggregate!.Append("node.all").ToList() }));
        Invalid("invalid-computed-node-declares-prompt", "A prompt on a node of a computed format — an operation takes none.",
            Node(m, "coverage", n => n with { Prompt = "assessment" }));
        Invalid("invalid-node-without-prompt", "No prompt on a node of an attested format.",
            Node(m, "assessment", n => n with { Prompt = null }));
        Invalid("invalid-node-prompt-unknown", "A node naming a prompt the package does not declare.",
            Node(m, "assessment", n => n with { Prompt = "nope" }));
        Invalid("invalid-on-invalid-on-a-text-format", "onInvalid on a text format, which validates nothing.",
            Node(m, "assessment", n => n with { OnInvalid = WorkflowOnInvalid.Fail }));
        Invalid("invalid-on-invalid-missing", "No onInvalid on a validated format.",
            Node(m, "extract", n => n with { OnInvalid = null }));
        Invalid("invalid-on-invalid-unknown", "An onInvalid outside fail | retry | flag.",
            Node(m, "extract", n => n with { OnInvalid = "ignore" }));
        Invalid("invalid-fail-if-empty-blank", "A blank failIfEmpty.",
            Node(m, "assessment", n => n with { FailIfEmpty = "" }));
        Invalid("invalid-fail-if-empty-on-a-computed-format", "failIfEmpty on a computed format — no reply to judge.",
            Node(m, "coverage", n => n with { FailIfEmpty = "Empty." }));
        Invalid("invalid-reproducible-on-a-computed-format", "reproducible on a computed format — deterministic by construction, the claim is not its to make.",
            Node(m, "coverage", n => n with { Reproducible = true }));
        Invalid("invalid-reproducible-on-a-rendered-format", "reproducible on a rendered format — deterministic by construction, the claim is not its to make.",
            Files(Manifest(m, x => x with
            {
                Prompts = x.Prompts!.Append(new WorkflowPromptSpec("summary", "prompts/summary.md", new List<string>())).ToList(),
                Nodes = x.Nodes!.Select(n => n.Id == "all" ? n with { Aggregate = n.Aggregate!.Append("node.summary").ToList() } : n)
                    .Append(new WorkflowNodeSpec("summary", "Summary", Prompt: "summary", Format: "rendered", Reproducible: true)).ToList(),
            }), fs => fs["prompts/summary.md"] = "Summary of {{ node.assessment.content }}."));

        // forEach
        Invalid("invalid-foreach-colon-spelling", "A forEach in the ≤24 spelling (data:<id>).", Fan("data:sections"));
        Invalid("invalid-foreach-not-a-reference", "A forEach that is a bare id.", Fan("sections"));
        Invalid("invalid-foreach-with-member", "A forEach reading a member — a fan names the folder or the input bare.", Fan("snippet.sections.content"));
        Invalid("invalid-foreach-unknown-folder", "A forEach over a snippet folder the package does not declare.", Fan("snippet.nope"));
        Invalid("invalid-foreach-fans-a-file", "A forEach over a snippet that is a file, not a folder.", Fan("snippet.standards"));
        Invalid("invalid-foreach-fans-a-non-array-input", "A forEach over a text input — only an array fans.", Fan("input.notes"));
        Invalid("invalid-foreach-fans-undeclared-input", "A forEach over an input the package does not declare.", Fan("input.nope"));
        Invalid("invalid-non-fan-reads-a-fan", "A scalar node reading a fanned node — aggregation is explicit.",
            P("{{ node.section_notes.content }}"));
        {
            var two = Manifest(m, x => x with
            {
                Inputs = x.Inputs!.Append(new WorkflowInputSpec("history", "History", true, "array", Items: new WorkflowElementSpec("text"))).ToList(),
                Prompts = x.Prompts!.Append(new WorkflowPromptSpec("per_event", "prompts/per_event.md", new List<string>())).ToList(),
                Nodes = x.Nodes!.Select(n => n.Id == "all" ? n with { Aggregate = n.Aggregate!.Append("node.per_event").ToList() } : n)
                    .Append(new WorkflowNodeSpec("per_event", "Per event", Prompt: "per_event", Format: "prose", ForEach: "input.history")).ToList(),
            });
            Invalid("invalid-fans-misaligned", "A fan over an input reading a fan over a snippet folder — two fans align only over one source.",
                Files(two, fs => fs["prompts/per_event.md"] = "{{ item.value }} {{ node.section_notes.content }}"));
        }

        // with (SHAPE_BLIND: typed by the format the node names)
        Invalid("invalid-with-on-a-format-without-parameters", "with on a format that declares no parameters.",
            With("assessment", new Dictionary<string, JsonNode?> { ["x"] = "y" }));
        Invalid("invalid-with-missing-parameter", "with leaving a parameter of the format unsupplied.",
            With("coverage", Coverage(failWith: null)));
        Invalid("invalid-with-names-no-parameter", "with naming something that is not a parameter of the format.",
            With("coverage", new Dictionary<string, JsonNode?>(Coverage()) { ["extra"] = "x" }));
        Invalid("invalid-with-node-of-another-format", "A node parameter given a node of the wrong format.",
            With("coverage", Coverage(of: "node.assessment")));
        Invalid("invalid-with-node-not-bare", "A node parameter given a member read — the parameter reads the node's value.",
            With("coverage", Coverage(of: "node.extract.value")));
        Invalid("invalid-with-node-not-a-reference", "A node parameter given a bare id.",
            With("coverage", Coverage(of: "extract")));
        Invalid("invalid-with-node-unknown", "A node parameter naming a node the package does not declare.",
            With("coverage", Coverage(of: "node.nope")));
        Invalid("invalid-with-text-from-a-non-text-input", "A text parameter given an input that is not a text.",
            With("coverage", Coverage(failWith: "input.kind")));
        Invalid("invalid-with-values-not-snake-case", "A values parameter carrying a value that is not snake_case.",
            With("scope", new Dictionary<string, JsonNode?> { ["values"] = new JsonArray("In Scope", "out_of_scope") }));
        Invalid("invalid-with-values-from-a-non-enum-input", "A values parameter given an input that is not an enum.",
            With("scope", new Dictionary<string, JsonNode?> { ["values"] = "input.notes" }));

        // ======================================================= macros
        Invalid("invalid-macro-without-label-at-25", "A macro without a label.", Macro(m, mc => mc with { Label = "" }));
        Invalid("invalid-macro-without-instruction", "A macro without an instruction — placed text tells the model when to write its read.",
            Macro(m, mc => mc with { Instruction = null }));
        Invalid("invalid-macro-file-missing-at-25", "A macro whose file is not in the package.", Files(m, fs => fs.Remove("macros/closing.md")));
        Invalid("invalid-duplicate-macro-id", "Two macros with one id.", Manifest(m, x => x with { Macros = x.Macros!.Concat(x.Macros!).ToList() }));
        Invalid("invalid-macro-never-placed-or-read", "A declared macro that no node, deliverable or template uses.",
            Files(Manifest(m, x => x with { Macros = x.Macros!.Append(new WorkflowMacroSpec("unused", "Unused", "macros/unused.md", Instruction: "x")).ToList() }), fs => fs["macros/unused.md"] = "Nothing."));
        // #1067: a use is a read from a template the run renders, or a placement — not
        // a read from any walked template. An unplaced macro's own instruction and
        // text are not reached, so what they read does not count.
        Invalid("invalid-macro-read-only-by-its-own-instruction", "An unplaced macro whose only reader is its own instruction — the instruction renders only where macro.<id>.prompt is read, so nothing renders it; the self-read is also warned.",
            Files(Manifest(m, x => x with { Macros = x.Macros!.Append(new WorkflowMacroSpec("spare", "Spare", "macros/spare.md", Instruction: "Write {{ macro.spare.content }} once, as the last line of the note.")).ToList() }), fs => fs["macros/spare.md"] = "Spare."));
        Invalid("invalid-macro-read-only-by-an-unplaced-macros-text", "A macro read only by the text of a macro nothing places or reads — neither renders, both are refused.",
            Files(Manifest(m, x => x with { Macros = x.Macros!.Concat(new[] { new WorkflowMacroSpec("spare", "Spare", "macros/spare.md", Instruction: "x"), new WorkflowMacroSpec("other", "Other", "macros/other.md", Instruction: "x") }).ToList() }),
                fs => { fs["macros/spare.md"] = "Spare."; fs["macros/other.md"] = "Other says: {{ macro.spare.content }}"; }));
        Valid("valid-macro-instruction-reads-its-own-content", "A placed macro whose instruction reads its own content — valid, and warned: the instruction renders where macro.<id>.prompt is read, so the read puts the text in the prompt; the mechanics sentence names the read.",
            Macro(m, mc => mc with { Instruction = "If the plan changed, write {{ macro.closing.content }} at the end." }));
        Invalid("invalid-node-instance-undeclared", "A node instance naming a macro the package does not declare.",
            Node(m, "assessment", n => n with { Macros = n.Macros!.Append(new WorkflowNodeMacroSpec("ghost", At: WorkflowNodeMacroSpec.After)).ToList() }));
        Invalid("invalid-node-instance-at-unknown", "A node instance placed somewhere other than before or after.",
            Node(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "middle") } }));
        Invalid("invalid-node-instance-placed-twice", "One macro placed twice at one side of one node — one identity once.",
            Node(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After), new("closing", At: WorkflowNodeMacroSpec.After, When: "input.kind == \"new\"") } }));
        Invalid("invalid-node-instance-on-a-format-without-output-macros", "A macro placed after the output of a format that takes no output macros.",
            Node(m, "extract", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) } }));
        Invalid("invalid-inline-macro-on-a-format-without-output-macros", "An inline macro read in the prompt of a format that takes no output macros.",
            V25Package.WithPrompt(m, "extract", "{{ input.notes }} {{ macro.closing.prompt }}"));
        Invalid("invalid-aggregator-places-a-macro", "A macro placed on an aggregator — place it on the nodes it aggregates or on the deliverable.",
            Node(m, "all", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) } }));
        Invalid("invalid-node-instance-for-item-without-a-folder-fan", "forItem on a node that fans no snippet folder.",
            Node(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, ForItem: "hpi") } }));
        Invalid("invalid-node-instance-for-item-unknown", "forItem naming an item the fanned folder does not contain.",
            Node(m, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, ForItem: "ros") } }));

        // ======================================================= item reads (SHAPE_BLIND: inside a template file, judged at the reading node)
        Invalid("invalid-node-instance-reads-item-without-a-fan", "A macro reading item.<member>, placed on a node that does not fan.",
            Files(m, fs => fs["macros/closing.md"] = "Closing for {{ item.name }}."));
        Invalid("invalid-prompt-reaches-item-through-a-snippet", "A prompt reaching item.<member> through a snippet it reads, on a node that does not fan.",
            Files(m, fs => fs["snippets/standards.md"] = "Standards for {{ item.name }}."));
        Invalid("invalid-inline-fill-reads-item-without-a-fan", "An inline macro whose fill reads item.<member>, read by a node that does not fan.",
            Files(Node(m, "assessment", n => n with { Macros = null }), fs =>
            {
                fs["macros/closing.md"] = "Closing for {{ item.name }}.";
                fs["prompts/assessment.md"] = "Assess {{ input.notes }}. {{ macro.closing.prompt }}";
            }));
        Invalid("invalid-deliverable-instance-reads-item", "A deliverable macro reading item.<member> — a deliverable composes no fan item.",
            Files(Deliverable(Node(m, "assessment", n => n with { Macros = null }), d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) } }),
                fs => fs["macros/closing.md"] = "Closing for {{ item.name }}."));
        Invalid("invalid-prompt-reads-item-without-a-fan", "A prompt reading item.<member> on a node that does not fan.", P("{{ item.name }}"));
        Invalid("invalid-fan-item-member-unknown", "A fanned prompt reading a member a snippet fan item does not carry.",
            V25Package.WithPrompt(m, "section", "{{ item.text }}"));

        // ======================================================= deliverables
        Invalid("invalid-deliverables-missing", "No deliverables.", Manifest(m, x => x with { Deliverables = null }));
        Invalid("invalid-deliverables-empty", "An empty deliverables list.", Manifest(m, x => x with { Deliverables = new List<WorkflowDeliverableSpec>() }));
        Invalid("invalid-deliverable-label-missing", "A deliverable without a label.", Deliverable(m, d => d with { Label = "" }));
        Invalid("invalid-duplicate-deliverable-id", "Two deliverables with one id (each over its own aggregator).",
            Manifest(m, x => x with
            {
                Nodes = x.Nodes!.Append(new WorkflowNodeSpec("all_again", "Everything again", Aggregate: new List<string> { "node.section_notes" })).ToList(),
                Deliverables = x.Deliverables!.Append(new WorkflowDeliverableSpec("consult", "node.all_again", "Second")).ToList(),
            }));
        Invalid("invalid-deliverable-node-colon-spelling", "A deliverable's node in the ≤24 spelling (node:<id>).", Deliverable(m, d => d with { Node = "node:all" }));
        Invalid("invalid-deliverable-node-not-a-reference", "A deliverable's node as a bare id.", Deliverable(m, d => d with { Node = "all" }));
        Invalid("invalid-deliverable-node-unknown", "A deliverable naming a node the package does not declare.", Deliverable(m, d => d with { Node = "node.nope" }));
        Invalid("invalid-deliverable-node-not-an-aggregator", "A deliverable naming a node that does not aggregate — and what follows: no fan reached, the aggregator and the fan left unfed.",
            Deliverable(m, d => d with { Node = "node.assessment" }));
        Invalid("invalid-deliverables-share-an-aggregator", "Two deliverables over one aggregator — each needs its own.",
            Manifest(m, x => x with { Deliverables = x.Deliverables!.Append(new WorkflowDeliverableSpec("second", "node.all", "Second")).ToList() }));
        Invalid("invalid-deliverable-check-colon-spelling", "A check in the ≤24 spelling (node:<id>) — and the check it leaves unnamed.",
            Deliverable(m, d => d with { Check = "node:coverage" }));
        Invalid("invalid-deliverable-check-not-computed", "A check naming a node whose format is not computed — and the check it leaves unnamed.",
            Deliverable(m, d => d with { Check = "node.assessment" }));
        Invalid("invalid-deliverable-check-unknown", "A check naming a node the package does not declare — and the check it leaves unnamed.",
            Deliverable(m, d => d with { Check = "node.nope" }));
        Invalid("invalid-check-not-named", "A computed node no deliverable names as its check — dead weight.",
            Deliverable(m, d => d with { Check = null }));
        Invalid("invalid-deliverable-instance-without-at", "A deliverable macro as a bare id — at 25 the instance says prepend or append.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing") } }));
        Invalid("invalid-deliverable-instance-at-unknown", "A deliverable instance placed somewhere other than prepend or append.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "before") } }));
        Invalid("invalid-deliverable-instance-placed-twice", "One macro placed twice at one side of one deliverable — one identity once.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append), new("closing", At: WorkflowDeliverableMacroSpec.Append) } }));
        Invalid("invalid-deliverable-instance-undeclared", "A deliverable instance naming a macro the package does not declare.",
            Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("ghost", At: WorkflowDeliverableMacroSpec.Append) } }));
        Invalid("invalid-deliverable-instance-reads-a-fanned-node", "A deliverable macro reading a fanned node — a deliverable macro reads scalar nodes and aggregators.",
            Files(Deliverable(Node(m, "assessment", n => n with { Macros = null }), d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) } }),
                fs => fs["macros/closing.md"] = "Closing: {{ node.section_notes.content }}."));
        Invalid("invalid-deliverable-signature-flag-and-macro", "signature: true beside a macro that reads profile.signature — signed once.",
            Files(Deliverable(m, d => d with { Signature = true, Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) } }),
                fs => fs["macros/closing.md"] = "{{ profile.signature }}"));
        Invalid("invalid-deliverable-two-signature-macros", "Two placed macros that both read profile.signature — signed once.",
            Files(Deliverable(Manifest(m, x => x with { Macros = x.Macros!.Append(new WorkflowMacroSpec("scope_line", "Scope line", "macros/scope_line.md", Instruction: "x")).ToList() }),
                    d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append), new("scope_line", At: WorkflowDeliverableMacroSpec.Append) } }),
                fs =>
                {
                    fs["macros/closing.md"] = "{{ profile.signature }}";
                    fs["macros/scope_line.md"] = "{{ profile.signature }}";
                }));
        Invalid("invalid-deliverable-without-a-fan", "A deliverable reaching no forEach source — a deliverable with no fan has no consult.",
            Manifest(m, x => x with
            {
                Nodes = x.Nodes!.Where(n => n.Id != "section_notes").Select(n => n.Id == "all" ? n with { Aggregate = new List<string> { "node.assessment" } } : n).ToList(),
            }));
        Invalid("invalid-node-does-not-feed-the-deliverable", "A node nothing reads and no aggregator collects — every node must reach a deliverable.",
            Manifest(m, x => x with { Nodes = x.Nodes!.Append(new WorkflowNodeSpec("orphan", "Orphan", Prompt: "extract", Format: "prose")).ToList() }));

        // ======================================================= templates — the reference graph and the walker (SHAPE_BLIND: the read is in a template file)
        Invalid("invalid-snippet-cycle", "Two snippets reading each other.",
            Files(Manifest(m, x => x with { Snippets = new Dictionary<string, string>(x.Snippets!) { ["intro"] = "snippets/intro.md" } }), fs =>
            {
                fs["snippets/standards.md"] = "{{ snippet.intro.content }}";
                fs["snippets/intro.md"] = "{{ snippet.standards.content }}";
            }));
        Invalid("invalid-instruction-cycle", "A macro's instruction reading a snippet that reads the macro's inline prompt.",
            Files(Macro(m, mc => mc with { Instruction = "Close as {{ snippet.standards.content }} says." }), fs => fs["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}"));
        Invalid("invalid-macro-text-reaches-an-inline-read", "A macro's text reaching its own inline prompt read — text placed after a reply instructs no model.",
            Files(m, fs =>
            {
                fs["macros/closing.md"] = "{{ snippet.standards.content }}";
                fs["snippets/standards.md"] = "{{ macro.closing.prompt }}";
            }));
        Invalid("invalid-template-assigns-a-reserved-object", "A template assigning to a reserved object.", P("{{ node = 1 }}"));
        Invalid("invalid-template-captures-a-reserved-object", "A template capturing into a reserved object.", P("{{ capture item }}x{{ end }}"));
        Invalid("invalid-template-loop-variable-is-reserved", "A loop variable named after a reserved object.", P("{{ for input in node.extract.value.concepts }}{{ end }}"));
        Invalid("invalid-template-iterates-a-non-array", "A for over a text.", P("{{ for c in input.notes }}{{ c }}{{ end }}"));
        Invalid("invalid-template-indexes-a-non-array", "An index into a text.", P("{{ input.notes[0] }}"));
        Invalid("invalid-template-dynamic-access", "A dynamic member access on an object — static access only.", P("{{ node[input.notes].content }}"));
        Invalid("invalid-template-reads-data", "A template reading data — snippet at 25.", P("{{ data.standards }}"));
        Invalid("invalid-template-reads-bindings", "A template reading bindings — derived from object reads at 25.", P("{{ bindings.notes }}"));
        Invalid("invalid-template-placeholder-grammar", "The ≤24 placeholder grammar ({{input:id}}) in a template.", P("{{input:notes}}"));
        Invalid("invalid-template-reads-undeclared-node", "A template reading a node the package does not declare.", P("{{ node.nope.content }}"));
        Invalid("invalid-template-reads-undeclared-input", "A template reading an input the package does not declare.", P("{{ input.nope }}"));
        Invalid("invalid-template-reads-undeclared-snippet", "A template reading a snippet the package does not declare.", P("{{ snippet.nope.content }}"));
        Invalid("invalid-template-reads-undeclared-macro", "A template reading a macro the package does not declare.", P("{{ macro.nope.content }}"));
        Invalid("invalid-template-reads-a-node-bare", "A template reading a node bare — .content or .value.", P("{{ node.extract }}"));
        Invalid("invalid-template-reads-a-snippet-bare", "A template reading a snippet bare — .content.", P("{{ snippet.standards }}"));
        Invalid("invalid-template-reads-value-of-a-text-format", "A read of .value on a text format, which has none.",
            Files(m, fs => fs["macros/closing.md"] = "{{ node.assessment.value }}"));
        Invalid("invalid-template-unknown-node-member", "A read of a member node.<id> does not have.", P("{{ node.extract.text }}"));
        Invalid("invalid-template-unknown-field", "A read of a field the format's schema does not declare.", P("{{ node.extract.value.items }}"));
        Invalid("invalid-template-field-of-a-scalar", "A field read on a text.", P("{{ input.notes.length }}"));
        Invalid("invalid-template-reads-a-folder-as-a-value", "A snippet folder read as a value — a folder is iterated.", P("{{ snippet.sections }}"));
        Invalid("invalid-template-unknown-root", "A read of a name that is no object, loop variable or builtin.", P("{{ foo }}"));
        Invalid("invalid-template-unknown-run-fact", "A read of a run fact the run object does not carry.", P("{{ run.user }}"));
        Invalid("invalid-template-unclosed-code", "A template that does not parse — an unclosed {{.", P("{{ input.notes"));
        Invalid("invalid-node-cycle", "Two nodes reading each other.", V25Package.WithPrompt(m, "extract", "{{ node.assessment.content }}"));
        Invalid("invalid-template-fails-strict-rendering", "A template that parses and types but fails the strict render probe.",
            P("{{ input.notes | date.to_string \"%Y\" }}"));
        // #1058: renderer pipes are typed — each renders the .content of a concept_list node and nothing else.
        Invalid("invalid-template-renderer-pipe-on-another-format", "A renderer pipe over the text of a node whose format is not the one it renders (a classification through concept_bullets).", P("{{ node.scope.content | concept_bullets }}"));
        Invalid("invalid-template-renderer-pipe-on-a-value", "A renderer pipe over a node's parsed value — a renderer renders a node's text.", P("{{ node.extract.value | concept_bullets }}"));
        Invalid("invalid-template-renderer-pipe-on-a-snippets-text", "A renderer pipe over a text that is not a node's.", P("{{ snippet.standards.content | concept_bullets }}"));
        Invalid("invalid-instruction-fails-strict-rendering", "An instruction that fails the strict render probe.",
            Macro(m, mc => mc with { Instruction = "Close {{ 1 | date.to_string '%Y' }}." }));

        // ======================================================= conditions (SHAPE_BLIND: one string)
        Invalid("invalid-condition-legacy-grammar", "A when in the ≤24 condition grammar.", D("node:scope == in_scope and kind == new"));
        Invalid("invalid-condition-blank-at-25", "A blank when.", D(" "));
        Invalid("invalid-condition-is-a-template", "A when written as a template ({{ … }}) rather than an expression.", D("{{ input.kind }}"));
        Invalid("invalid-condition-enum-value-undeclared-at-25", "A when comparing a classification to a value it does not declare.", D("node.scope.value == \"maybe\""));
        Invalid("invalid-condition-orders-an-enum-at-25", "A when ordering an enum — == or != only.", D("node.scope.value > \"a\""));
        Invalid("invalid-condition-tests-a-text-at-25", "A when testing a text for truth.", D("input.notes"));
        Invalid("invalid-condition-tests-an-enum", "A when testing an enum for truth.", D("input.kind"));
        Invalid("invalid-condition-reads-a-text-value", "A when reading .value of a text format.", D("node.assessment.value == \"x\""));
        Invalid("invalid-condition-reads-undeclared-input-at-25", "A when reading an input the package does not declare.", D("input.nope == \"x\""));
        Invalid("invalid-condition-operator-not-allowed", "A when using an operator outside the condition set.", D("input.notes ?? \"x\""));
        Invalid("invalid-condition-yields-a-text", "A when that yields a text, not a boolean.", D("input.notes | string.upcase"));
        Invalid("invalid-condition-item-id-undeclared", "A node instance's when comparing item.id to an id the folder does not contain.",
            Node(m, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, When: "item.id == \"ros\"") } }));
        Invalid("invalid-folder-item-when-reads-a-node", "A snippet folder item's when reading a node — inputs only, so the fan's size is known at job start.",
            Files(m, fs => fs["snippets/sections/index.json"] = fs["snippets/sections/index.json"].Replace("\"file\": \"hpi.md\"", "\"file\": \"hpi.md\", \"when\": \"node.scope.value == \\\"in_scope\\\"\"")));

        // ======================================================= snippets (SHAPE_BLIND: the bundle)
        Invalid("invalid-snippet-file-missing", "A snippet whose file is not in the package.",
            Manifest(m, x => x with { Snippets = new Dictionary<string, string>(x.Snippets!) { ["extra"] = "snippets/extra.md" } }));
        Invalid("invalid-snippet-folder-without-index", "A snippet folder without its index.json.",
            Manifest(m, x => x with { Snippets = new Dictionary<string, string>(x.Snippets!) { ["more"] = "snippets/more/" } }));
        Invalid("invalid-snippet-folder-item-file-missing", "A folder item whose file is not in the package.",
            Files(m, fs => fs["snippets/sections/index.json"] = fs["snippets/sections/index.json"].Replace("] }", ", { \"id\": \"ros\", \"name\": \"ROS\", \"file\": \"ros.md\" } ] }")));
    }
}
