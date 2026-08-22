using System.Reflection;
using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// The byte-parity seam of the interpreter cutover: resolving variables over the
/// canonical v5 DAG must produce exactly the dictionaries the deleted pre-DAG
/// analysis activities built (transcribed verbatim below), and forEach instances
/// must resolve the same values the deleted prose-step builder did.
/// </summary>
public class NodeVariableResolverTests
{
    private const string Draft = "62-year-old woman with newly diagnosed left breast invasive ductal carcinoma.";

    private static readonly IReadOnlyList<ClinicalConcept> PatientConcepts = new[]
    {
        new ClinicalConcept("Malignant neoplasm of breast", "disorder", "254837009", true, true, "patient"),
        new ClinicalConcept("Family support strong", "", "", false, false, "patient")
    };

    private static readonly IReadOnlyList<ClinicalConcept> ProblemConcepts = new[]
    {
        new ClinicalConcept("Malignant neoplasm of breast", "disorder", "254837009", true, true, "problem")
    };

    private static readonly IReadOnlyList<ClinicalConcept> TypicalConcepts = new[]
    {
        new ClinicalConcept("Tamoxifen therapy", "procedure", "75367002", true, true, "typical-trajectory", "adjuvant endocrine therapy")
    };

    private static readonly IReadOnlyList<ClinicalConcept> TrajectoryConcepts = new[]
    {
        new ClinicalConcept("Malignant neoplasm of breast", "disorder", "254837009", true, true, "patient-trajectory")
    };

    private static readonly IReadOnlyList<ConsultNodeDescriptor> Nodes =
        V5Fixtures.Manifest().Nodes!
            .Select(Describe)
            .ToList();

    private static readonly IReadOnlyDictionary<string, ConsultNodeDescriptor> NodesById =
        Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, NodeRunResult> Outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
    {
        ["extract-patient-concepts"] = new("{}", PatientConcepts, "in1", "out1"),
        ["identify-problem"] = new("{}", ProblemConcepts, "in2", "out2"),
        ["create-typical-trajectory"] = new("{}", TypicalConcepts, "in3", "out3"),
        ["create-patient-trajectory"] = new("{}", TrajectoryConcepts, "in4", "out4")
    };

    private static ConsultNodeDescriptor Describe(WorkflowNodeSpec node) => new(
        node.Id,
        node.Label,
        node.Prompt,
        node.Bindings?.ToDictionary(
            pair => pair.Key,
            pair => new ConsultNodeBindingDescriptor(pair.Value.From, pair.Value.As),
            StringComparer.Ordinal),
        OutputContract: node.Output is null ? null : OutputContracts.ConceptList,
        FailIfEmpty: node.Output?.FailIfEmpty,
        ForEach: node.ForEach);

    private static Dictionary<string, string> DraftInputs(string draft) =>
        new(StringComparer.Ordinal) { ["consult_draft"] = draft };

    private static Dictionary<string, string> Resolve(string nodeId) =>
        ConsultNodeVariableResolver.Resolve(NodesById[nodeId], DraftInputs(Draft), null, null, NodesById, Outputs);

    [Fact]
    public void ExtractPatientConcepts_Parity()
    {
        // Deleted activity body: { [ConsultDraft] = input.ConsultDraft }
        Assert.Equal(
            new Dictionary<string, string> { ["consult_draft"] = Draft },
            Resolve("extract-patient-concepts"));
    }

    [Fact]
    public void IdentifyProblem_Parity()
    {
        // Deleted activity body: { [PatientConcepts] = ConsultGenerationConceptFormatter.Format(input.PatientConcepts) }
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["patient_concepts"] = ConsultGenerationConceptFormatter.Format(PatientConcepts)
            },
            Resolve("identify-problem"));
    }

    [Fact]
    public void CreateTypicalTrajectory_Parity()
    {
        // Deleted activity body: { [ProblemConcepts] = Format(input.ProblemContext) }
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["problem_concepts"] = ConsultGenerationConceptFormatter.Format(ProblemConcepts)
            },
            Resolve("create-typical-trajectory"));
    }

    [Fact]
    public void CreatePatientTrajectory_Parity()
    {
        // Deleted activity body: problem + patient + typical, all analysis-formatted.
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["problem_concepts"] = ConsultGenerationConceptFormatter.Format(ProblemConcepts),
                ["patient_concepts"] = ConsultGenerationConceptFormatter.Format(PatientConcepts),
                ["typical_trajectory_concepts"] = ConsultGenerationConceptFormatter.Format(TypicalConcepts)
            },
            Resolve("create-patient-trajectory"));
    }

    // The strict-render-against-repo-templates case retired with #16: the
    // general package's sources live in the consultologist-workflows repo, and
    // the validator's strict render covers every published template at
    // publish time (fixture templates cover it in WorkflowV5Tests here).

    [Fact]
    public void Render_DispatchesToTheBytePinnedFormatters()
    {
        Assert.Equal(
            ConsultGenerationConceptFormatter.Format(PatientConcepts),
            ConsultNodeVariableResolver.Render(WorkflowConceptRenderers.ConceptBullets, PatientConcepts));
        Assert.Equal(
            AgentSectionGenerator.FormatConcepts(TrajectoryConcepts),
            ConsultNodeVariableResolver.Render(WorkflowConceptRenderers.ConceptContext, TrajectoryConcepts));
        Assert.Throws<InvalidOperationException>(() => ConsultNodeVariableResolver.Render("markdown", PatientConcepts));
    }

    [Fact]
    public void LoweredForEachChain_ResolvesTheSameValuesTheProseBuilderDid()
    {
        var firstStep = Nodes.First(n => n.ForEach != null);
        var item = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "hpi", ["name"] = "History of Present Illness", ["standard"] = "Chronological prose."
        };

        var variables = ConsultNodeVariableResolver.Resolve(firstStep, DraftInputs(Draft), item, null, NodesById, Outputs);

        // The R3 pin: the concept-context rendering carries source: patient-trajectory.
        Assert.Equal(AgentSectionGenerator.FormatConcepts(TrajectoryConcepts), variables["patient_trajectory_concepts"]);
        Assert.Contains("source: patient-trajectory", variables["patient_trajectory_concepts"]);
        Assert.Equal("History of Present Illness", variables["section_name"]);
    }
}

public class ConsultNodeSchedulerTests
{
    private static readonly ConsultNodeDescriptor Trajectory = new(
        "create-patient-trajectory", "Building patient trajectory", OutputContract: "concept-list");

    private static readonly ConsultNodeDescriptor Step1 = new(
        "standard-section-draft", "Drafting section",
        Bindings: new Dictionary<string, ConsultNodeBindingDescriptor>
        {
            ["patient_trajectory_concepts"] = new("node:create-patient-trajectory", "concept-context")
        },
        ForEach: "input:sections");

    private static readonly ConsultNodeDescriptor Step2 = new(
        "patient-section-draft", "Applying patient information",
        Bindings: new Dictionary<string, ConsultNodeBindingDescriptor>
        {
            ["standard_section_draft"] = new("node:standard-section-draft")
        },
        ForEach: "input:sections");

    private static readonly IReadOnlyDictionary<string, ConsultNodeDescriptor> NodesById =
        new[] { Trajectory, Step1, Step2 }.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static Dictionary<string, NodeRunResult> Outputs(params string[] keys) =>
        keys.ToDictionary(k => k, _ => new NodeRunResult("x", null, "i", "o"), StringComparer.Ordinal);

    [Fact]
    public void InstanceKeys_ScalarById_InstancesComposite()
    {
        Assert.Equal("extract", ConsultNodeScheduler.InstanceKey("extract", null));
        Assert.Equal("standard-section-draft:hpi", ConsultNodeScheduler.InstanceKey("standard-section-draft", "hpi"));
    }

    [Fact]
    public void ForEachInstance_WaitsOnBroadcastScalarDependencies()
    {
        Assert.False(ConsultNodeScheduler.InstanceReady(Step1, "hpi", NodesById, Outputs()));
        Assert.True(ConsultNodeScheduler.InstanceReady(Step1, "hpi", NodesById, Outputs("create-patient-trajectory")));
    }

    [Fact]
    public void ItemAlignment_UnlocksPerItem_NotPerWave()
    {
        // The conservatism removal: section hpi's second step is ready the moment
        // ITS first step completes — section pmh's first step is still pending.
        var outputs = Outputs("create-patient-trajectory", "standard-section-draft:hpi");

        Assert.True(ConsultNodeScheduler.InstanceReady(Step2, "hpi", NodesById, outputs));
        Assert.False(ConsultNodeScheduler.InstanceReady(Step2, "pmh", NodesById, outputs));
    }

    [Fact]
    public void NodeDependencies_ReadTheNodeEdges()
    {
        Assert.Equal(new[] { "standard-section-draft" }, ConsultNodeScheduler.NodeDependencies(Step2));
        Assert.Empty(ConsultNodeScheduler.NodeDependencies(Trajectory));
    }
}

public class ProvenanceHashTests
{
    [Fact]
    public void Sha256Hex_MatchesKnownVector()
    {
        // echo -n "abc" | sha256sum
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ConsultGenerationProvenance.Sha256Hex("abc"));
    }

    [Fact]
    public void DraftOnlyHash_PinsTheCanonicalShape()
    {
        var request = new ConsultGenerationRequest("Draft text.");

        // Canonical shape pin: {"consultDraft":"Draft text."} — the definition jobs
        // record as effectiveInputHashVersion 2.
        Assert.Equal(
            ConsultGenerationProvenance.Sha256Hex("""{"consultDraft":"Draft text."}"""),
            ConsultGenerationProvenance.ComputeDraftOnlyHash(request));
    }

    [Fact]
    public void DeclaredInputsHash_PinsTheCanonicalShape()
    {
        // Canonical shape pin: ordinal-sorted {id: text}, raw values, no wrapper —
        // the definition v7 jobs record as effectiveInputHashVersion 3. Supplied
        // map only: an absent optional input never appears.
        var supplied = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prior_notes"] = "Old notes.",
            ["consult_draft"] = "Draft text."
        };

        Assert.Equal(
            ConsultGenerationProvenance.Sha256Hex("""{"consult_draft":"Draft text.","prior_notes":"Old notes."}"""),
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(supplied));
        Assert.Equal(3, ConsultGenerationProvenance.DeclaredInputsHashVersion);
    }

    [Fact]
    public void ResultSetHash_PinsTheCanonicalShape()
    {
        // Canonical shape pin: ordinal-sorted {resultId: sha256hex(document)} —
        // the definition v7 jobs record as workflowOutputHashVersion 3.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["patient_letter"] = "Letter body.",
            ["consult_note"] = "Note body."
        };

        var expected = ConsultGenerationProvenance.Sha256Hex(
            $$"""{"consult_note":"{{ConsultGenerationProvenance.Sha256Hex("Note body.")}}","patient_letter":"{{ConsultGenerationProvenance.Sha256Hex("Letter body.")}}"}""");

        Assert.Equal(expected, ConsultGenerationProvenance.ComputeResultSetHash(documents));
        Assert.Equal(3, ConsultGenerationProvenance.ResultSetHashVersion);
    }
}

public class StartRequestValidationTests
{
    [Fact]
    public void ValidateRequest_RequiresBodyAndExactlyOneInputForm()
    {
        Assert.Equal("Request body is required.", ConsultGenerationJobs.ValidateRequest(null));
        Assert.Equal("ConsultDraft, Inputs or InputFiles is required.", ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(" ")));
        Assert.Null(ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest("Draft.")));

        Assert.Equal(
            "Send ConsultDraft or Inputs, not both.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                "Draft.",
                Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." })));
        Assert.Null(ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." })));
    }

    [Fact]
    public void ValidateRequest_ChecksAttachedDocuments()
    {
        // #238: a slot filled from both directions is the same ambiguity the
        // v7 contract already refuses for ConsultDraft-vs-Inputs — nobody
        // needs both, and picking one would drop the other in silence.
        Assert.Equal(
            "Input 'consult_draft' was supplied as both text and a file.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null,
                Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Typed." },
                InputFiles: new Dictionary<string, InputFilePayload>
                {
                    ["consult_draft"] = new("text/plain", "From a file."u8.ToArray())
                })));

        Assert.Equal(
            "InputFiles contains a blank id.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null,
                InputFiles: new Dictionary<string, InputFilePayload> { [" "] = new("text/plain", "x"u8.ToArray()) })));

        Assert.Equal(
            "Input file 'consult_draft' is empty.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null,
                InputFiles: new Dictionary<string, InputFilePayload> { ["consult_draft"] = new("text/plain", []) })));

        // A per-file bound does not bound a request carrying several, which is
        // why the total exists at all.
        Assert.Equal(
            "Input files exceed 20 MB in total.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null,
                InputFiles: new Dictionary<string, InputFilePayload>
                {
                    ["a"] = new("application/pdf", new byte[9 * 1024 * 1024]),
                    ["b"] = new("application/pdf", new byte[9 * 1024 * 1024]),
                    ["c"] = new("application/pdf", new byte[9 * 1024 * 1024])
                })));

        Assert.Null(ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, InputFilePayload>
            {
                ["consult_draft"] = new("text/plain", "From a file."u8.ToArray())
            })));
    }

    [Fact]
    public void ValidateRequest_ChecksInputEntries()
    {
        Assert.Equal(
            "Inputs contains a blank id.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null, Inputs: new Dictionary<string, ConsultInputValue> { [" "] = "text" })));
        Assert.Equal(
            "Input 'prior_notes' is blank.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null, Inputs: new Dictionary<string, ConsultInputValue> { ["prior_notes"] = " " })));
        Assert.Equal(
            "Input 'consult_draft' exceeds 256 KB.",
            ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(
                null, Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = new string('x', ConsultGenerationJobs.MaxInputLength + 1) })));
    }

    private static string? Validate(Dictionary<string, ConsultInputValue> inputs) =>
        ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest(null, Inputs: inputs));

    [Fact]
    public void ValidateRequest_AdmitsNumbersAndEmptyArrays()
    {
        // #421: a number has no length to run away with, and an empty array is
        // present and empty — whether that satisfies the slot is the
        // starter's 422, not the door's 400.
        Assert.Null(Validate(new Dictionary<string, ConsultInputValue> { ["length_of_stay"] = ConsultInputValue.OfNumber("3") }));
        Assert.Null(Validate(new Dictionary<string, ConsultInputValue> { ["prior_notes"] = ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()) }));
    }

    [Fact]
    public void ValidateRequest_BoundsStructuredValues()
    {
        // Shape limits in the same sense as MaxInputLength: refused, never
        // truncated, and applied before the starter allocates anything.
        var big = new string('x', ConsultGenerationJobs.MaxInputLength + 1);

        Assert.Equal(
            $"Input 'prior_notes' has more than {ConsultGenerationJobs.MaxArrayElements} elements.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["prior_notes"] = ConsultInputValue.OfArray(Enumerable.Repeat(ConsultInputValue.OfText("a"), ConsultGenerationJobs.MaxArrayElements + 1))
            }));
        Assert.Equal(
            "Input 'prior_notes' element 1 exceeds 256 KB.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.OfText(big) })
            }));
        Assert.Equal(
            $"Input 'patient' has more than {ConsultGenerationJobs.MaxObjectFields} fields.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["patient"] = ConsultInputValue.OfObject(Enumerable.Range(0, ConsultGenerationJobs.MaxObjectFields + 1)
                    .Select(i => new ConsultInputEntry($"f{i}", ConsultInputValue.OfText("a"))))
            }));
        Assert.Equal(
            "Input 'patient' field 'note' exceeds 256 KB.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["patient"] = ConsultInputValue.OfObject(new[] { new ConsultInputEntry("note", ConsultInputValue.OfText(big)) })
            }));
        Assert.Equal(
            "Input 'patient' has a blank field id.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["patient"] = ConsultInputValue.OfObject(new[] { new ConsultInputEntry(" ", ConsultInputValue.OfText("a")) })
            }));
        Assert.Equal(
            "Input 'prior_notes' element 0 field 'note' exceeds 256 KB.",
            Validate(new Dictionary<string, ConsultInputValue>
            {
                ["prior_notes"] = ConsultInputValue.OfArray(new[]
                {
                    ConsultInputValue.OfObject(new[] { new ConsultInputEntry("note", ConsultInputValue.OfText(big)) })
                })
            }));
    }

    [Fact]
    public void MalformedInputMessage_NamesTheTokenAndThePath_NeverTheValue()
    {
        // The door's 400 for a shape the converter refused, as a caller reads
        // it: what was wrong and where — and nothing the caller sent.
        var exception = Assert.Throws<ConsultInputShapeException>(() =>
            JsonSerializer.Deserialize<ConsultGenerationRequest>(
                """{"inputs":{"consult_draft":[["secret"]]}}""",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

        var message = ConsultGenerationJobs.MalformedInputMessage(exception);

        Assert.Contains("element 0 is an array", message, StringComparison.Ordinal);
        Assert.Contains("consult_draft", message, StringComparison.Ordinal);
        Assert.Contains(" At $.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
    }
}

public class ConsultGenerationNodeEntityTests
{
    private static readonly PropertyInfo StateProperty = typeof(ConsultGenerationJobEntity)
        .GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static IReadOnlyDictionary<string, string> Item(string id, string name) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id, ["name"] = name, ["content"] = "std" };

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) CreateEntity()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>());
        var state = ConsultGenerationJobState.Create(
            "job-1", "user-1", new[] { Item("hpi", "History of Present Illness") });
        StateProperty.SetValue(entity, state);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    [Fact]
    public async Task Initialize_StampsTheFanRosterOnceAndItReachesTheResponse()
    {
        // #361: the rail reads this off the response, so the hop that matters is
        // ToResponse, not the entity field. Stamped write-once beside Nodes: a
        // job's graph is the one it started with, not the one a later signal
        // happens to carry.
        var (entity, state) = CreateEntity();

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1",
            "user-1",
            new[] { Item("hpi", "History of Present Illness") },
            Collections: new[]
            {
                new ConsultCollectionRoster("standards", new[] { new ConsultCollectionItem("hpi", "History of Present Illness") })
            }));

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1",
            "user-1",
            new[] { Item("hpi", "History of Present Illness") },
            Collections: new[]
            {
                new ConsultCollectionRoster("standards", new[] { new ConsultCollectionItem("later", "Added afterwards") })
            }));

        var roster = Assert.Single(state().ToResponse().Collections!);

        Assert.Equal("standards", roster.CollectionId);
        Assert.Equal(new[] { "hpi" }, roster.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void MarkNodeCompleted_RecordsOutputAndCounts()
    {
        var (entity, state) = CreateEntity();

        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate(
            "extract-patient-concepts", "Extracting clinical concepts",
            new[] { new ClinicalConcept("Term", "disorder", "1", true, true, "patient") },
            "hash-in", "hash-out", 1, 5));

        var node = state().NodeOutputs!["extract-patient-concepts"];
        Assert.Equal(6, state().SchemaVersion);
        Assert.Equal(ConsultGenerationNodeStatuses.Completed, node.Status);
        Assert.Equal("hash-in", node.InputHash);
        Assert.Equal("hash-out", node.OutputHash);
        Assert.Single(node.Concepts!);
        Assert.Equal(1, state().CompletedStageCount);
        Assert.Equal(5, state().TotalStageCount);
        Assert.Contains(state().History, h => h is { Kind: "success", Label: "Extracting clinical concepts" });
    }

    [Fact]
    public void MarkNodeItemCompleted_RecordsPerItemProvenanceAndSectionProgress()
    {
        var (entity, state) = CreateEntity();

        entity.MarkNodeItemCompleted(new ConsultGenerationNodeItemUpdate(
            "standard-section-draft", "Drafting section", "hpi", "History of Present Illness",
            null, "hash-in", "hash-out", 1, 3));

        var s = state();
        Assert.Equal(6, s.SchemaVersion);
        var output = s.NodeOutputs!["standard-section-draft:hpi"];
        Assert.Equal("standard-section-draft", output.NodeId);
        Assert.Equal("hpi", output.ItemId);
        Assert.Equal(ConsultGenerationNodeStatuses.Completed, output.Status);
        Assert.Equal("hash-in", output.InputHash);
        Assert.Equal("hash-out", output.OutputHash);

        var progress = s.ItemProgress["hpi"];
        Assert.Equal("standard-section-draft", progress.Step);
        Assert.Equal(1, progress.CompletedStepCount);
        Assert.Equal(3, progress.TotalStepCount);

        // The per-item entry surfaces on the response under its composite key.
        var response = s.ToResponse();
        Assert.Equal("hash-in", response.NodeOutputs!["standard-section-draft:hpi"].InputHash);
    }

    [Fact]
    public void MarkNodeFailed_RecordsSkippedSetAndFailsJob()
    {
        var (entity, state) = CreateEntity();

        entity.MarkNodeFailed(new ConsultGenerationNodeFailure(
            "identify-problem", "Identifying primary problem",
            "identify-problem-failed", "No valid disease or problem concept was identified.",
            new[]
            {
                new ConsultItemStepDescriptor("create-typical-trajectory", "Building reference trajectory"),
                new ConsultItemStepDescriptor("sections", "Generating sections")
            })).GetAwaiter().GetResult();

        var s = state();
        Assert.Equal("identify-problem-failed", s.AnalysisStatus);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, s.Status);
        Assert.Equal(ConsultGenerationNodeStatuses.Failed, s.NodeOutputs!["identify-problem"].Status);
        Assert.Equal(ConsultGenerationNodeStatuses.Skipped, s.NodeOutputs["sections"].Status);
        Assert.Contains(s.History, h => h is { Kind: "skipped", Label: "Building reference trajectory" });
        Assert.Contains(s.History, h => h.Kind == "skipped" && h.Label.Contains("History of Present Illness"));
    }

    [Fact]
    public void WorkflowOutputHash_PinnedDefinition_AndOnlyOnCompletedJobs()
    {
        // Definition v1 pin: sha256 of canonical {sectionId: sha256(text)} with
        // ordinal-sorted keys — recomputable by anyone from GeneratedBlocks.
        Assert.Equal(
            "6f52afd079adaed357b430559c22864adb46a075e6ca0d6922201230cfd50a73",
            ConsultGenerationProvenance.ComputeWorkflowOutputHash(new Dictionary<string, string>
            {
                ["hpi"] = "alpha",
                ["allergies"] = "beta"
            }));

        var (entity, state) = CreateEntity();
        var items = new[] { Item("hpi", "History of Present Illness") };
        entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", items)).GetAwaiter().GetResult();
        entity.MarkRunning().GetAwaiter().GetResult();

        Assert.Null(state().ToResponse().WorkflowOutputHash);

        entity.CompleteBlock(new BlockGenerationResult("hpi", "History of Present Illness", true, "alpha", null)).GetAwaiter().GetResult();
        entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed)).GetAwaiter().GetResult();

        var response = state().ToResponse();
        Assert.NotNull(response.WorkflowOutputHash);
        Assert.Equal(ConsultGenerationProvenance.WorkflowOutputHashVersion, response.WorkflowOutputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeWorkflowOutputHash(response.GeneratedBlocks),
            response.WorkflowOutputHash);
    }

    [Fact]
    public void Initialize_RecordsThePackageSpecVersionWriteOnce_AndSurfacesItOnTheResponse()
    {
        // #373: the package's format, recorded rather than resolved later. A
        // fork lives in the private registry nothing outside can read, and a
        // pin can be re-pointed — provenance says what the job ran.
        var (entity, state) = CreateEntity();
        var items = new[] { Item("hpi", "History of Present Illness") };

        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", items, PackageSpecVersion: 8)).GetAwaiter().GetResult();

        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", items, PackageSpecVersion: 5)).GetAwaiter().GetResult();

        Assert.Equal(8, state().PackageSpecVersion);
        Assert.Equal(8, state().ToResponse().PackageSpecVersion);
    }

    [Fact]
    public void Initialize_WithNoPackageSpecVersion_LeavesItUnknown()
    {
        // Every job recorded before #373 is this case. Null is the record
        // saying it does not know, which the row renders as no chip — a
        // number would be a guess and a dash would read as "no format".
        var (entity, state) = CreateEntity();

        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", new[] { Item("hpi", "History of Present Illness") }))
            .GetAwaiter().GetResult();

        Assert.Null(state().PackageSpecVersion);
        Assert.Null(state().ToResponse().PackageSpecVersion);
    }

    [Fact]
    public void PackageSpecVersion_IsNotTheRecordsOwnStorageVersion()
    {
        // The confusion #373 exists to end: two unrelated ladders that collide
        // at 7 by coincidence. SchemaVersion is stamped 6 or 7 by whichever
        // code path produced the record and is never 8 — so a v8 job used to
        // display "schema v7".
        var (entity, state) = CreateEntity();

        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", new[] { Item("hpi", "History of Present Illness") },
            PackageSpecVersion: 8)).GetAwaiter().GetResult();
        entity.CompleteResultDocument(new ConsultGenerationResultDocument(
            "consult_note", "Consultation note", "The note.", 0)).GetAwaiter().GetResult();

        Assert.Equal(8, state().PackageSpecVersion);
        Assert.Equal(7, state().SchemaVersion);
    }

    [Fact]
    public void Initialize_RecordsTheCatalogRefWriteOnce_AndSurfacesItOnTheResponse()
    {
        var (entity, state) = CreateEntity();
        var items = new[] { Item("hpi", "History of Present Illness") };

        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", items,
            CatalogRef: "output-contracts@v2026.07.1")).GetAwaiter().GetResult();

        // Write-once: a second Initialize (Durable replay) must not overwrite.
        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", items,
            CatalogRef: "output-contracts@v2026.07.9")).GetAwaiter().GetResult();

        Assert.Equal("output-contracts@v2026.07.1", state().CatalogRef);
        Assert.Equal("output-contracts@v2026.07.1", state().ToResponse().CatalogRef);
    }

    [Fact]
    public void AgentVersions_LegacyRecords_StillDeserializeAndSurfaceTheirStoredMap()
    {
        // Records <= 2026-07-17 stored the contract -> agent-version map; since
        // #105 new records carry catalogRef only. The field is legacy-read-only:
        // old state blobs must keep serving their map on the response.
        var legacyStateJson = """
            {
              "JobId": "legacy-1",
              "AppUserId": "user-1",
              "Status": "Completed",
              "AgentVersions": { "text": "47", "concept-list": "1" },
              "CatalogRef": null
            }
            """;

        var state = System.Text.Json.JsonSerializer.Deserialize<ConsultGenerationJobState>(legacyStateJson)!;

        var response = state.ToResponse();
        Assert.Equal("47", response.AgentVersions!["text"]);
        Assert.Equal("1", response.AgentVersions!["concept-list"]);

        // And the purified path: a fresh Initialize stamps no map.
        var (entity, freshState) = CreateEntity();
        entity.Initialize(new ConsultGenerationJobInitialize(
            "job-2", "user-1", new[] { Item("hpi", "History of Present Illness") },
            CatalogRef: "output-contracts@v2026.07.1")).GetAwaiter().GetResult();

        Assert.Null(freshState().AgentVersions);
        Assert.Null(freshState().ToResponse().AgentVersions);
        Assert.Equal("output-contracts@v2026.07.1", freshState().ToResponse().CatalogRef);
    }

    [Fact]
    public void FinalizeJob_CompletesLingeringRunningNodes_AndCatchesUpTheCounts()
    {
        var (entity, state) = CreateEntity();
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("extract", "Extracting clinical concepts", null, "i", "o", 4, 5));
        // A node left Running (legacy in-flight tolerance) completes at finalize.
        state().GetOrAddNodeOutput("sections", "Generating sections").Status = ConsultGenerationNodeStatuses.Running;

        entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed)).GetAwaiter().GetResult();

        Assert.Equal(ConsultGenerationNodeStatuses.Completed, state().NodeOutputs!["sections"].Status);
        Assert.Equal(state().TotalStageCount, state().CompletedStageCount);
    }
}

// The pre-rebase legacy-snapshot tolerance tests retired with #175: all stored
// job records were wiped (prerelease), so no old-shape state survives to
// deserialize — the split Blocks/ItemProgress model is the only shape.

public class NodeEventCandidateTests
{
    private static ConsultGenerationJobResponse Response(
        string mapStatus = ConsultGenerationNodeStatuses.Running,
        string? analysisStatus = null,
        string? analysisError = null)
    {
        var nodes = new[]
        {
            new ConsultNodeDescriptor("extract", "Extracting clinical concepts"),
            new ConsultNodeDescriptor("sections", "Generating sections", ForEach: "data:standards")
        };
        var outputs = new Dictionary<string, ConsultGenerationNodeStatusResponse>
        {
            ["extract"] = new("extract", "Extracting clinical concepts", ConsultGenerationNodeStatuses.Completed, "in", "out"),
            ["sections"] = new("sections", "Generating sections", mapStatus)
        };

        return new ConsultGenerationJobResponse(
            "job-1", "user-1", ConsultGenerationJobStatuses.Running, 1, 0, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), false,
            SchemaVersion: 3,
            AnalysisStatus: analysisStatus,
            AnalysisError: analysisError,
            Nodes: nodes,
            NodeOutputs: outputs);
    }

    [Fact]
    public void Candidates_EmitOnlyCompletedNodes_InDescriptorOrder()
    {
        // A running forEach node emits nothing at the node level — its per-item
        // progress rides the section-prose-step events instead.
        var running = ConsultGenerationJobs.CreateSemanticEventCandidates(Response())
            .Where(c => c.EventType == ConsultGenerationNodeEvents.EventName)
            .ToList();

        Assert.Equal(new[] { "node:extract" }, running.Select(c => c.EventKey));
        Assert.Contains("Extracting clinical concepts completed.", running[0].PayloadJson);

        var completed = ConsultGenerationJobs.CreateSemanticEventCandidates(
                Response(mapStatus: ConsultGenerationNodeStatuses.Completed))
            .Where(c => c.EventType == ConsultGenerationNodeEvents.EventName)
            .ToList();

        Assert.Equal(new[] { "node:extract", "node:sections" }, completed.Select(c => c.EventKey));
        Assert.Contains("Generating sections completed.", completed[1].PayloadJson);
    }

    [Fact]
    public void Candidates_EmitErrorForFailedNodeStatus()
    {
        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(
            Response(mapStatus: ConsultGenerationNodeStatuses.Skipped,
                     analysisStatus: "identify-problem-failed",
                     analysisError: "No valid disease or problem concept was identified."));

        Assert.Contains(candidates, c => c.EventType == "error" && c.EventKey == "error:identify-problem-failed");
    }

    [Fact]
    public void Candidates_SkipLegacyStageLoopForNodeSnapshots()
    {
        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(Response());

        Assert.DoesNotContain(candidates, c => c.EventKey.StartsWith("analysis:"));
    }
}

public class ForEachInstanceResolutionTests
{
    // Byte parity with the deleted ProseStepVariableBuilder: a lowered map step's
    // bindings, resolved as a forEach instance, must produce the exact values the
    // prose-step activity used to build.
    private static readonly IReadOnlyList<ClinicalConcept> Concepts = new[]
    {
        new ClinicalConcept("Malignant neoplasm of breast", "disorder", "254837009", true, true, "draft")
    };

    private static readonly IReadOnlyDictionary<string, string> Item = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["id"] = "hpi",
        ["name"] = "History of Present Illness",
        ["standard"] = "Chronological prose."
    };

    private static readonly ConsultNodeDescriptor Trajectory = new(
        "create-patient-trajectory", "Building patient trajectory", OutputContract: "concept-list");

    private static readonly ConsultNodeDescriptor PreviousStep = new(
        "standard-section-draft", "Drafting section", ForEach: "input:sections");

    private static ConsultNodeDescriptor Node(params (string Variable, string From, string? As)[] bindings) => new(
        "patient-section-draft",
        "Applying patient information",
        PromptId: "patient-section-draft",
        Bindings: bindings.ToDictionary(
            b => b.Variable,
            b => new ConsultNodeBindingDescriptor(b.From, b.As),
            StringComparer.Ordinal),
        ForEach: "input:sections");

    private static readonly IReadOnlyDictionary<string, ConsultNodeDescriptor> NodesById =
        new[] { Trajectory, PreviousStep }.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, NodeRunResult> Outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
    {
        ["create-patient-trajectory"] = new("{}", Concepts, "in", "out"),
        ["standard-section-draft:hpi"] = new("Previous step prose.", null, "in2", "out2")
    };

    [Fact]
    public void Resolve_MapsEveryLoweredSourceToItsLegacyValue()
    {
        var variables = ConsultNodeVariableResolver.Resolve(
            Node(
                ("draft", "input:consult_draft", null),
                ("name", "item:name", null),
                ("standard", "item:standard", null),
                ("concepts", "node:create-patient-trajectory", "concept-context"),
                ("previous", "node:standard-section-draft", null)),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "Draft consult text." },
            Item,
            dataScalars: null,
            NodesById,
            Outputs);

        Assert.Equal("Draft consult text.", variables["draft"]);
        Assert.Equal("History of Present Illness", variables["name"]);
        Assert.Equal("Chronological prose.", variables["standard"]);
        Assert.Equal(AgentSectionGenerator.FormatConcepts(Concepts), variables["concepts"]);
        Assert.Equal("Previous step prose.", variables["previous"]);
    }

    [Fact]
    public void Resolve_ItemAlignment_ReadsTheInstancesOwnUpstreamOutput()
    {
        var outputs = new Dictionary<string, NodeRunResult>(Outputs.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)
        {
            ["standard-section-draft:pmh"] = new("Other section prose.", null, "in3", "out3")
        };
        var pmhItem = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "pmh", ["name"] = "Past Medical History", ["standard"] = "List."
        };

        var variables = ConsultNodeVariableResolver.Resolve(
            Node(("previous", "node:standard-section-draft", null)),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "draft" },
            pmhItem, null, NodesById, outputs);

        Assert.Equal("Other section prose.", variables["previous"]);
    }

    [Theory]
    [InlineData("item:title", "which the item does not carry")]
    [InlineData("data:notes", "carries no data scalars")]
    [InlineData("not_a_source", "cannot resolve")]
    public void Resolve_ThrowsOnUnresolvableSources(string from, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConsultNodeVariableResolver.Resolve(
            Node(("value", from, null)),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "draft" },
            Item, null, NodesById, Outputs));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Resolve_DataScalars_BindByEntryId()
    {
        var variables = ConsultNodeVariableResolver.Resolve(
            Node(("value", "data:clinic-guidelines", null)),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "draft" },
            Item,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["clinic-guidelines"] = "Local guidance." },
            NodesById, Outputs);

        Assert.Equal("Local guidance.", variables["value"]);
    }
}

public class ConsultDeliverablesTests
{
    private static readonly Dictionary<string, ConsultNodeDescriptor> NodesById = new(StringComparer.Ordinal)
    {
        ["fan"] = new("fan", "Fan", ForEach: "data:standards"),
        ["extra"] = new("extra", "Extra"),
        ["note"] = new("note", "Note", Aggregate: new List<string> { "node:fan" }),
        ["letter"] = new("letter", "Letter", Aggregate: new List<string> { "node:fan", "node:extra" })
    };

    private static readonly List<ConsultResultDescriptor> TwoResults = new()
    {
        new("consult_note", "note", "Consultation note"),
        new("patient_letter", "letter", "Patient letter")
    };

    [Fact]
    public void V6_ResolvesOneUnnamedEntryWithEmptyPrefix()
    {
        // The empty prefix is the byte-parity contract: v6 block ids and
        // signal order must reproduce exactly through the shared loops.
        var deliverables = ConsultDeliverables.Resolve(null, "note", NodesById);

        var entry = Assert.Single(deliverables);
        Assert.Null(entry.ResultId);
        Assert.Equal(string.Empty, entry.BlockPrefix);
        Assert.Equal("note", entry.NodeId);
        Assert.Equal(new[] { "fan" }, entry.SourceIds);
    }

    [Fact]
    public void V6_NonAggregatorResult_ResolvesEmpty()
    {
        Assert.Empty(ConsultDeliverables.Resolve(null, "fan", NodesById));
        Assert.Empty(ConsultDeliverables.Resolve(null, null, NodesById));
    }

    [Fact]
    public void V7_ResolvesPrefixedEntriesInResultSetOrder()
    {
        var deliverables = ConsultDeliverables.Resolve(TwoResults, null, NodesById);

        Assert.Equal(
            new[] { ("consult_note", "consult_note:", "note", 0), ("patient_letter", "patient_letter:", "letter", 1) },
            deliverables.Select(d => (d.ResultId, d.BlockPrefix, d.NodeId, d.Ordinal)).ToArray());
        // The shared forEach source appears in BOTH deliverables' source sets —
        // the double block emission that prefixed ids exist to disambiguate.
        Assert.All(deliverables, d => Assert.Contains("fan", d.SourceIds));
    }

    [Fact]
    public void FinalOutcome_CompletedOnlyWhenEveryDeliverableProduced()
    {
        var deliverables = ConsultDeliverables.Resolve(TwoResults, null, NodesById);
        var bothProduced = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["note"] = new("n", null, "i", "o"),
            ["letter"] = new("l", null, "i", "o")
        };
        var oneProduced = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["letter"] = new("l", null, "i", "o")
        };

        Assert.Equal(
            (ConsultGenerationJobStatuses.Completed, (string?)null),
            ConsultDeliverables.FinalOutcome(deliverables, bothProduced, new Dictionary<string, string>()));

        // The first missing deliverable BY RESULT-SET ORDER selects the error.
        var (status, error) = ConsultDeliverables.FinalOutcome(
            deliverables,
            oneProduced,
            new Dictionary<string, string> { ["note"] = "Note could not assemble." });
        Assert.Equal(ConsultGenerationJobStatuses.Failed, status);
        Assert.Equal("Note could not assemble.", error);

        var (_, fallback) = ConsultDeliverables.FinalOutcome(
            deliverables, oneProduced, new Dictionary<string, string>());
        Assert.Equal("The assembled documents could not be produced.", fallback);
    }

    [Fact]
    public void AFilteredResultSet_IsCompletedByWhatItContains()
    {
        // #315's central distinction, and the reason filtering the package
        // rather than teaching the engine about conditions works: a deliverable
        // that legitimately does not exist is not in the list at all, so it
        // cannot read as missing. One that SHOULD have existed and did not
        // still fails the job.
        var oneResult = TwoResults.Take(1).ToList();
        var deliverables = ConsultDeliverables.Resolve(oneResult, null, NodesById);

        var noteOnly = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["note"] = new("n", null, "i", "o")
        };

        // The letter was filtered out, so producing only the note is Completed.
        Assert.Equal(
            (ConsultGenerationJobStatuses.Completed, (string?)null),
            ConsultDeliverables.FinalOutcome(deliverables, noteOnly, new Dictionary<string, string>()));

        // And a deliverable that IS in the filtered set still has to produce.
        var (status, _) = ConsultDeliverables.FinalOutcome(
            deliverables,
            new Dictionary<string, NodeRunResult>(StringComparer.Ordinal),
            new Dictionary<string, string>());
        Assert.Equal(ConsultGenerationJobStatuses.Failed, status);
    }
}

public class SectionProseStepEventTests
{
    /// <summary>
    /// #356: the completed (node, item) pairs are what the events are built
    /// from, so the fixture states them. They are spelled as the node outputs
    /// spell them — "nodeId:itemId" — because that composite key IS the record
    /// that a given node finished a given item.
    /// </summary>
    private static ConsultGenerationJobResponse Response(
        IReadOnlyList<ConsultItemStepDescriptor>? sectionSteps,
        int completedStepCount,
        int totalStepCount,
        params string[] completedPairs)
    {
        return Response(
            sectionSteps,
            new Dictionary<string, ConsultGenerationItemProgress>
            {
                ["hpi"] = new("hpi", "History of Present Illness", null, completedStepCount, totalStepCount)
            },
            completedPairs);
    }

    private static ConsultGenerationJobResponse Response(
        IReadOnlyList<ConsultItemStepDescriptor>? sectionSteps,
        IReadOnlyDictionary<string, ConsultGenerationItemProgress> itemProgress,
        params string[] completedPairs)
    {
        return new ConsultGenerationJobResponse(
            "job-1",
            "user-1",
            ConsultGenerationJobStatuses.Running,
            1,
            0,
            0,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            false,
            ItemProgress: itemProgress,
            ItemSteps: sectionSteps,
            NodeOutputs: completedPairs.ToDictionary(
                pair => pair,
                pair => new ConsultGenerationNodeStatusResponse(
                    pair[..pair.LastIndexOf(':')],
                    pair,
                    ConsultGenerationNodeStatuses.Completed),
                StringComparer.Ordinal));
    }

    [Fact]
    public void Candidates_UsePackageStepIdsAndLabels_UnderTheGenericEventName()
    {
        var steps = new[]
        {
            new ConsultItemStepDescriptor("draft", "Drafting section"),
            new ConsultItemStepDescriptor("tighten", "Tightening prose")
        };

        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(Response(steps, 2, 2, "draft:hpi", "tighten:hpi"))
            .Where(candidate => candidate.EventType == ConsultGenerationItemSteps.EventName)
            .ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Equal("item-step:hpi:draft", candidates[0].EventKey);
        Assert.Equal("item-step:hpi:tighten", candidates[1].EventKey);

        var payload = JsonSerializer.Deserialize<ConsultGenerationItemStepEvent>(
            candidates[1].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("tighten", payload.Step);
        Assert.Equal("Tightening prose", payload.Label);
        Assert.Equal("Tightening prose completed.", payload.Message);
        Assert.Equal(2, payload.CompletedStepCount);
        Assert.Equal(2, payload.TotalStepCount);
    }

    [Fact]
    public void Candidates_NameTheNodeThatRanIt_NotTheOneAtThatPosition()
    {
        // #356, the case the positional version cannot get right. ItemSteps is
        // every forEach node in the package, while CompletedStepCount is
        // counted over ONE collection's chain — so a guideline that has
        // finished its only step scored 1, indexed ItemSteps[0], and was
        // reported as having run 'draft', a node from the standards chain it
        // never touches.
        var steps = new[]
        {
            new ConsultItemStepDescriptor("draft", "Drafting section"),
            new ConsultItemStepDescriptor("tighten", "Tightening prose"),
            new ConsultItemStepDescriptor("summarize-guideline", "Summarizing guideline")
        };

        var progress = new Dictionary<string, ConsultGenerationItemProgress>
        {
            ["hpi"] = new("hpi", "History of Present Illness", null, 2, 2),
            ["dka"] = new("dka", "DKA guideline", null, 1, 1)
        };

        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(
                Response(steps, progress, "draft:hpi", "tighten:hpi", "summarize-guideline:dka"))
            .Where(candidate => candidate.EventType == ConsultGenerationItemSteps.EventName)
            .ToList();

        Assert.Equal(
            new[] { "item-step:dka:summarize-guideline", "item-step:hpi:draft", "item-step:hpi:tighten" },
            candidates.Select(candidate => candidate.EventKey).ToArray());

        var guideline = JsonSerializer.Deserialize<ConsultGenerationItemStepEvent>(
            candidates[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("summarize-guideline", guideline.Step);
        Assert.Equal("Summarizing guideline", guideline.Label);
        Assert.Equal("DKA guideline", guideline.ItemName);
    }

    [Fact]
    public void Candidates_IgnoreAnItemStepThatHasNotCompleted()
    {
        // Only completed pairs are events. A node still running for an item has
        // an entry too, and it must not be reported as finished.
        var steps = new[]
        {
            new ConsultItemStepDescriptor("draft", "Drafting section"),
            new ConsultItemStepDescriptor("tighten", "Tightening prose")
        };

        var response = Response(steps, 1, 2, "draft:hpi");
        var running = new Dictionary<string, ConsultGenerationNodeStatusResponse>(response.NodeOutputs!, StringComparer.Ordinal)
        {
            ["tighten:hpi"] = new("tighten", "tighten:hpi", ConsultGenerationNodeStatuses.Running)
        };

        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(response with { NodeOutputs = running })
            .Where(candidate => candidate.EventType == ConsultGenerationItemSteps.EventName)
            .ToList();

        Assert.Equal(new[] { "item-step:hpi:draft" }, candidates.Select(candidate => candidate.EventKey).ToArray());
    }

    [Fact]
    public void Candidates_SkipLegacySnapshotsWithoutStepLists()
    {
        // Pre-milestone-3 snapshots regenerate no prose candidates; their events were
        // materialized while they ran and replay from the event store.
        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(
                Response(sectionSteps: null, completedStepCount: 2, totalStepCount: 3))
            .Where(candidate => candidate.EventType == ConsultGenerationItemSteps.EventName)
            .ToList();

        Assert.Empty(candidates);
    }
}
