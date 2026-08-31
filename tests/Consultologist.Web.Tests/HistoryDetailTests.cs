using Consultologist.Web.Services.Provenance;
using Consultologist.Web.Services.Workflow;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using NSubstitute;

using Consultologist.PackageFormat;
namespace Consultologist.Web.Tests;

/// <summary>
/// History's provenance panel (#224): a v7 job lists each deliverable's own
/// digest under the job-level hash those digests compose; a v5/v6 job lists
/// none. The deep-link route loads the detail eagerly, which is how these
/// reach the panel without simulating a click.
/// </summary>
public class HistoryDetailTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    private void WithJob(
        int outputHashVersion,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? inputOrigins = null,
        IReadOnlyList<ConsultSkippedDocumentResponse>? skipped = null,
        int? packageSpecVersion = null,
        int? schemaVersion = null,
        string? workflowPackage = null,
        string? packageTitle = null,
        IReadOnlyList<string>? packageTags = null,
        IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes = null,
        string? catalogRef = null,
        IReadOnlyDictionary<string, string>? agentVersions = null,
        string workflowOutputHash = "bbbb",
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? nodeOutputs = null,
        string? packageFormatRef = null,
        string? provenanceRef = null,
        TerminologySnapshot? terminology = null,
        string? terminologyServerRef = null,
        DateTimeOffset? textDroppedAtUtc = null,
        IReadOnlyDictionary<string, string>? heldInputs = null,
        DateTimeOffset? inputsDroppedAtUtc = null,
        string? source = null,
        string? apiHost = null,
        string? engineCommit = null,
        string status = "Completed",
        string? rerunOf = null,
        string? rerunVerdict = null,
        string? rerunDivergence = null)
    {
        // Terminal status only: a non-terminal row would start the page's real
        // 5-second polling loop.
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 9, FailedBlockCount: 0, TextDroppedAtUtc: textDroppedAtUtc)
            },
            null));

        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            status,
            TotalBlockCount: 9,
            CompletedBlockCount: 9,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            EffectiveInputHash: "aaaa",
            EffectiveInputHashVersion: outputHashVersion,
            WorkflowOutputHash: workflowOutputHash,
            WorkflowOutputHashVersion: outputHashVersion,
            AssembledDocuments: documents,
            InputOrigins: inputOrigins,
            SkippedDocuments: skipped,
            SchemaVersion: schemaVersion,
            PackageSpecVersion: packageSpecVersion,
            WorkflowPackage: workflowPackage,
            PackageTitle: packageTitle,
            PackageTags: packageTags,
            Nodes: nodes,
            NodeOutputs: nodeOutputs,
            PackageFormatRef: packageFormatRef,
            ProvenanceRef: provenanceRef,
            Terminology: terminology,
            TerminologyServerRef: terminologyServerRef,
            TextDroppedAtUtc: textDroppedAtUtc,
            HeldInputs: heldInputs,
            InputsDroppedAtUtc: inputsDroppedAtUtc,
            AgentVersions: agentVersions,
            CatalogRef: catalogRef,
            Source: source,
            ApiHost: apiHost,
            EngineCommit: engineCommit,
            RerunOf: rerunOf,
            RerunVerdict: rerunVerdict,
            RerunDivergence: rerunDivergence));
    }

    // ----- #514: where the job ran, what ran it, how it was initiated -----

    [Fact]
    public void TheHostAndTheEngine_AreShownAsRecorded_AndLinked()
    {
        var commit = "77a617f453cb2d8875c2b6918ff8e9fe92cce7ac";
        WithJob(3, source: "app", apiHost: "east.ca.api.consultologist.ai", engineCommit: commit);
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("east.ca.api.consultologist.ai", page.Find(".provenance-host span").TextContent.Trim());
        Assert.Contains("east.ca.api.consultologist.ai", page.Find(".provenance-chip--host").TextContent);
        var engine = page.Find(".provenance-engine a");
        Assert.Equal("77a617f", engine.TextContent.Trim());
        Assert.Equal($"https://github.com/Consultologist/Consultologist-Blazor/commit/{commit}", engine.GetAttribute("href"));
        Assert.Equal("via app", page.Find(".provenance-chip--source").TextContent.Trim());
    }

    [Fact]
    public void ARecordWithoutThem_NamesTheAbsence_AndNeverBorrowsTheClientsHost()
    {
        // A deployment that named no host, or a record from before the field:
        // "not recorded", never the base URL this client happens to call.
        WithJob(3, source: "email");
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("not recorded", page.Find(".provenance-host span").TextContent.Trim());
        Assert.Equal("not recorded", page.Find(".provenance-engine span").TextContent.Trim());
        Assert.Empty(page.FindAll(".provenance-chip--host"));
        Assert.DoesNotContain("azurewebsites.net", page.Markup);
        Assert.Equal("via email", page.Find(".provenance-chip--source").TextContent.Trim());
    }

    [Fact]
    public void ARecordFromBeforeTheSource_HasNoSourceChip()
    {
        WithJob(3);
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".provenance-chip--source"));
    }

    private const string CatalogVersion = "v2026.07.2";

    private void WithCatalog() =>
        WorkflowService.GetCatalogAsync(CatalogVersion).Returns(new Dictionary<string, PublicCatalogEntry>
        {
            ["concept-list"] = new("concept-extraction", "1"),
            ["text"] = new("test-json", "47")
        });

    private void WithEngine() =>
        WorkflowService.GetEngineAsync().Returns(new EngineView("9c1ca4ac9373f1dc01e1fec68772304ef7d23ca6", "v2026.08.6", "v2026.08.3"));

    private const string Registry = "https://consultologistpublic.blob.core.windows.net";

    [Fact]
    public void TheDefinitionNumbers_LinkToThePublishedDocument_AtTheEngineAttestedVersion()
    {
        // #402: a number a reader can resolve — the exact contract the
        // deployed engine names — not a bare integer.
        WithEngine();
        WithJob(3, new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Note.", "hash-note") }, packageSpecVersion: 9, schemaVersion: 7, workflowPackage: "general@v2026.08.1");
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var refs = page.FindAll(".provenance-list a.provenance-ref").Select(a => (a.TextContent.Trim(), a.GetAttribute("href"))).ToList();
        Assert.Contains(("v7", $"{Registry}/provenance/v2026.08.3/provenance-record.md"), refs);
        Assert.Equal(2, refs.Count(r => r.Item2 == $"{Registry}/provenance/v2026.08.3/hash-definitions.md" && r.Item1 == "v3"));
        // #373's chip, finished: the format number resolves too.
        Assert.Equal($"{Registry}/package-format/v2026.08.6/package-format-v9.md", page.Find(".provenance-chip a.provenance-ref").GetAttribute("href"));
        Assert.Contains("Output hash (v3)", page.Find(".provenance-list").TextContent);
    }

    [Fact]
    public void TheRecordsOwnRefs_WinOverTheEnginesAttestation()
    {
        // #398: a record from an older build names older documents; the
        // engine attests newer ones. The record's are what its numbers mean.
        WithEngine();
        WithJob(3, new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Note.", "hash-note") },
            packageSpecVersion: 9, schemaVersion: 7, packageFormatRef: "package-format@v2026.08.5", provenanceRef: "provenance@v2026.08.2");
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var refs = page.FindAll(".provenance-list a.provenance-ref").Select(a => a.GetAttribute("href")!).ToList();
        Assert.All(refs, href => Assert.Contains("/provenance/v2026.08.2/", href));
        Assert.Contains("recorded on the job", page.Find(".provenance-list a.provenance-ref[title]").GetAttribute("title"));
        Assert.Equal($"{Registry}/package-format/v2026.08.5/package-format-v9.md", page.Find(".provenance-chip a.provenance-ref").GetAttribute("href"));
        var contract = page.Find(".provenance-contract a");
        Assert.Equal("provenance@v2026.08.2", contract.TextContent.Trim());
        Assert.Equal($"{Registry}/provenance/v2026.08.2/provenance-record.md", contract.GetAttribute("href"));

        // A record from before its own refs falls back to the attestation, and says so.
        WithJob(3, new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Note.", "hash-note") }, packageSpecVersion: 9, schemaVersion: 7);
        var older = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.All(older.FindAll(".provenance-list a.provenance-ref").Select(a => a.GetAttribute("href")!), href => Assert.Contains("/provenance/v2026.08.3/", href));
        Assert.Contains("predates its own", older.Find(".provenance-list a.provenance-ref[title]").GetAttribute("title"));
        Assert.Equal("—", older.Find(".provenance-contract").TextContent.Trim());
    }

    [Fact]
    public void TheTerminologyChip_NamesTheEdition_AndLinksTheServersCommit()
    {
        // #403: the edition the concepts were answered against, and the build
        // that served it; a record that knows neither shows no chip.
        WithJob(3, terminology: new TerminologySnapshot("SNOMEDCT 20251130 import.", "2025-11-30", "2025-12-21T22:39:16.944Z"),
            terminologyServerRef: "snomed-snowstorm-mcp@0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80");
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var chip = page.FindAll(".provenance-chip").Single(c => c.TextContent.Contains("SNOMED CT", StringComparison.Ordinal));
        Assert.Equal("SNOMED CT 2025-11-30", chip.TextContent.Trim());
        Assert.Contains("SNOMEDCT 20251130 import.", chip.GetAttribute("title"));
        Assert.Contains("snomed-snowstorm-mcp@0fff939d", chip.GetAttribute("title"));
        Assert.Equal("https://github.com/Tauheed-Elahee/snomed-snowstorm-mcp/commit/0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80", chip.QuerySelector("a")!.GetAttribute("href"));

        // A hand-deployed server has a version, not a page.
        WithJob(3, terminology: new TerminologySnapshot("e", "2025-11-30", null), terminologyServerRef: "snomed-snowstorm-mcp@1.0.0");
        var unstamped = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Null(unstamped.FindAll(".provenance-chip").Single(c => c.TextContent.Contains("SNOMED CT", StringComparison.Ordinal)).QuerySelector("a"));

        WithJob(3);
        Assert.DoesNotContain(Render<History>(parameters => parameters.Add(p => p.JobId, JobId)).FindAll(".provenance-chip"), c => c.TextContent.Contains("SNOMED CT", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeletedText_IsANamedState_AndVerifySaysNotCheckable()
    {
        // #368: the row says so, the panel says when, and Verify never calls a
        // deleted text a mismatch.
        var dropped = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);
        WithJob(3, new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", null, "abcd") },
            workflowOutputHash: "c8f7", textDroppedAtUtc: dropped);
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains(page.FindAll(".job-source-badge"), b => b.TextContent.Trim() == "text deleted");
        Assert.Contains("Text deleted", page.Find(".provenance-list").TextContent);
        Assert.Contains("retention policy", page.Find(".provenance-text-deleted").TextContent);

        page.Find(".provenance-verify fluent-button").Click();
        var marks = page.FindAll(".hash-check").Select(m => m.TextContent.Trim()).ToList();
        Assert.Equal(2, marks.Count);
        Assert.All(marks, m => Assert.Contains("text deleted on 2026-09-01 — not checkable", m));
        Assert.DoesNotContain(marks, m => m.Contains("does not match"));
        Assert.Contains("cannot be checked against it", page.Find(".provenance-verify").TextContent);
    }

    [Fact]
    public void WithoutTheAttestation_TheNumbersStayPlainText()
    {
        WithJob(3, new[] { new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Note.", "hash-note") }, packageSpecVersion: 9);
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll("a.provenance-ref"));
        Assert.Contains("Input hash (v3)", page.Find(".provenance-list").TextContent);
        Assert.Contains(page.FindAll(".provenance-chip"), chip => chip.TextContent.Contains("format v9", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_RecomputesTheDeliverableHashesFromTheRecord()
    {
        var note = ProvenanceHashes.Sha256Hex("Consultation note");
        var letter = ProvenanceHashes.Sha256Hex("Patient letter");
        var documents = new[]
        {
            new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Consultation note", note),
            new ConsultGenerationResultDocumentResponse("letter", "Patient letter", "Patient letter", letter)
        };
        WithJob(3, documents, workflowOutputHash: ProvenanceHashes.MerkleHash(new Dictionary<string, string> { ["note"] = "Consultation note", ["letter"] = "Patient letter" }));
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Empty(page.FindAll(".hash-check"));

        page.Find(".provenance-verify fluent-button").Click();

        var marks = page.FindAll(".hash-check").Select(m => m.TextContent.Trim()).ToList();
        Assert.Equal(3, marks.Count);
        Assert.All(marks, m => Assert.Equal("recomputed — matches", m));
        Assert.Contains("3 hash(es) recomputed — all match", page.Find(".provenance-verify").TextContent);

        // A record whose output hash does not come from its texts says so, by hash.
        WithJob(3, documents, workflowOutputHash: "not-what-the-texts-give");
        var tampered = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        tampered.Find(".provenance-verify fluent-button").Click();
        Assert.Equal(new[] { "recomputed — does not match", "recomputed — matches", "recomputed — matches" },
            tampered.FindAll(".hash-check").Select(m => m.TextContent.Trim()));
        Assert.Contains("1 of 3 recomputed hash(es) do not match", tampered.Find(".provenance-verify").TextContent);
    }

    [Fact]
    public void TheRail_NamesEachNodesHashDefinition_OrSaysItPredatesTheLadder()
    {
        // #375: a stamped node links its number to the published ladder; a
        // node recorded before the ladder says so instead of showing one.
        WithEngine();
        WithJob(3, nodes: new[] { new ConsultGenerationNodeDescriptor("digest", "Digest"), new ConsultGenerationNodeDescriptor("letter", "Letter") },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["digest"] = new("digest", "Digest", "Completed", "in-1", "out-1", DateTimeOffset.UtcNow, null, HashVersion: 5),
                ["letter"] = new("letter", "Letter", "Completed", "in-2", "out-2", DateTimeOffset.UtcNow, null)
            });
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var stamped = page.Find(".node-row__hashes a.provenance-ref");
        Assert.Equal("v5", stamped.TextContent.Trim());
        Assert.Equal($"{Registry}/provenance/v2026.08.3/hash-definitions.md", stamped.GetAttribute("href"));
        Assert.Contains("per-node definition 5", stamped.GetAttribute("title"));
        var unstamped = page.Find(".node-row__unversioned");
        Assert.Equal("v—", unstamped.TextContent.Trim());
        Assert.Contains("before the per-node hashes had a definition number", unstamped.GetAttribute("title"));
    }

    private static string AgentsRow(IRenderedComponent<History> page) => page.Find(".provenance-agents").TextContent.Trim();

    private static bool HasAgentsRow(IRenderedComponent<History> page) =>
        page.Find(".provenance-list").QuerySelectorAll("dt").Any(term => term.TextContent.Trim() == "Agents");

    [Fact]
    public void APackageWithoutSchemas_NamesOnlyTheTextAgent()
    {
        // #458: the row names what the job's nodes bound, not the catalog's
        // whole map — one text line for two schema-less nodes, no concept-list.
        WithCatalog();
        WithJob(3, catalogRef: $"output-contracts@{CatalogVersion}", nodes: new[]
        {
            new ConsultGenerationNodeDescriptor("digest", "Digest"),
            new ConsultGenerationNodeDescriptor("letter", "Letter")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("text → test-json v47", AgentsRow(page));
    }

    [Fact]
    public void ContractsAppearInNodeDeclarationOrder_OnceEach()
    {
        WithCatalog();
        WithJob(3, catalogRef: $"output-contracts@{CatalogVersion}", nodes: new[]
        {
            new ConsultGenerationNodeDescriptor("write", "Write"),
            new ConsultGenerationNodeDescriptor("extract", "Extract", OutputContract: "concept-list"),
            new ConsultGenerationNodeDescriptor("summarise", "Summarise")
        });
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Equal("text → test-json v47 · concept-list → concept-extraction v1", AgentsRow(page));

        WithJob(3, catalogRef: $"output-contracts@{CatalogVersion}", nodes: new[]
        {
            new ConsultGenerationNodeDescriptor("extract", "Extract", OutputContract: "concept-list"),
            new ConsultGenerationNodeDescriptor("write", "Write")
        });
        var reversed = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Equal("concept-list → concept-extraction v1 · text → test-json v47", AgentsRow(reversed));
    }

    [Fact]
    public void ALegacyRecord_StillShowsItsStoredMap_AndAJobWithNeitherShowsNoRow()
    {
        WithJob(3, agentVersions: new Dictionary<string, string> { ["text"] = "47", ["concept-list"] = "1" });
        var legacy = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Equal("concept-list → v1 · text → v47", AgentsRow(legacy));

        WithJob(3, nodes: Array.Empty<ConsultGenerationNodeDescriptor>());
        Assert.False(HasAgentsRow(Render<History>(parameters => parameters.Add(p => p.JobId, JobId))));
    }

    [Fact]
    public void ATaggedPackage_ShowsItsTagsOnTheProvenanceList()
    {
        // #453: a Tags row beside Lineage and Agents, in authored order; no
        // row for a package that declared none, and none before v9.
        WithJob(3, workflowPackage: "general@v2026.08.1", packageTitle: "Breast oncology consults", packageTags: new[] { "oncology", "Breast" });
        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var list = page.Find(".provenance-list");
        Assert.Contains("Tags", list.QuerySelectorAll("dt").Select(term => term.TextContent.Trim()));
        Assert.Equal("oncology · Breast", page.Find(".provenance-tags").TextContent.Trim());

        WithJob(3, workflowPackage: "general@v2026.08.1", packageTags: Array.Empty<string>());
        var none = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.DoesNotContain("Tags", none.Find(".provenance-list").QuerySelectorAll("dt").Select(term => term.TextContent.Trim()));

        WithJob(3, workflowPackage: "general@v2026.08.1");
        var before = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.DoesNotContain("Tags", before.Find(".provenance-list").QuerySelectorAll("dt").Select(term => term.TextContent.Trim()));
    }

    [Fact]
    public void ATitledPackage_ShowsItsTitleBesideTheRef()
    {
        // #432: the title as it was at the pinned version — stamped on the
        // record, since History cannot read the manifest — beside the ref.
        WithJob(3, workflowPackage: "general@v2026.08.1", packageTitle: "Breast oncology consults");

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("Breast oncology consults · general@v2026.08.1", Chips(page));
    }

    [Fact]
    public void AnUntitledPackage_ShowsTheRefAlone()
    {
        WithJob(3, workflowPackage: "general@v2026.08.1");

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("general@v2026.08.1", Chips(page));
        Assert.DoesNotContain(Chips(page), chip => chip.Contains(" · general@"));
    }

    [Fact]
    public void AnUnsignedDeliverable_IsSaidByName_BesideItsDigest()
    {
        // v11 #516 § 5: signature requested by the package; none chosen on
        // the profile — the record and History say so by name.
        WithJob(3, new[]
        {
            new ConsultGenerationResultDocumentResponse("letter", "Decline letter", "Letter.", "hash-letter", Unsigned: true),
            new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.", "hash-note")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var unsigned = page.FindAll(".document-unsigned");
        Assert.Single(unsigned);
        Assert.Equal(
            "produced unsigned — signature requested by the package; none chosen on the profile",
            unsigned[0].TextContent.Trim());
    }

    [Fact]
    public void HeldInputs_AreShown_EachBehindItsOwnFold()
    {
        // #547: an input can be a whole document — folded, never dumped.
        WithJob(3, heldInputs: new Dictionary<string, string>
        {
            ["consult_draft"] = "Referral text, long enough to matter.",
            ["length_of_stay"] = ""
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var folds = page.FindAll(".held-input");
        Assert.Equal(2, folds.Count);
        Assert.Contains("consult_draft", page.Find(".provenance-held-inputs").TextContent);
        Assert.Contains("Referral text, long enough to matter.", page.Find(".provenance-held-inputs").TextContent);
        Assert.Contains("(empty)", page.Find(".provenance-held-inputs").TextContent);
    }

    [Fact]
    public void DroppedInputs_AreSaidByName_AndNotShown()
    {
        WithJob(3, inputsDroppedAtUtc: new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("the held inputs were deleted under the retention policy", page.Find(".provenance-inputs-deleted").TextContent);
        Assert.Empty(page.FindAll(".provenance-held-inputs"));
    }

    [Fact]
    public void AJobNeverHeld_ShowsNeitherInputsBlockNorDeletionRow()
    {
        WithJob(3);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".provenance-held-inputs"));
        Assert.Empty(page.FindAll(".provenance-inputs-deleted"));
    }

    [Fact]
    public void ADeliverableThatDidNotFire_IsRecordedBesideTheDigests()
    {
        // The absence belongs next to the evidence for the documents that do
        // exist: History is where a reader goes to ask what this job actually
        // produced, and a shorter list with no explanation answers wrongly.
        WithJob(3,
            new[] { new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.", "hash-note") },
            skipped: new[]
            {
                new ConsultSkippedDocumentResponse("billing_summary", "Billing summary",
                    "needs billable to be true; it is 'false'")
            });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("Billing summary", page.Markup);
        Assert.Contains("not produced", page.Markup);
        Assert.Contains("needs billable to be true", page.Markup);
    }

    [Fact]
    public void V7Job_ListsEachDeliverablesDigest()
    {
        WithJob(3, new[]
        {
            new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.", "hash-note"),
            new ConsultGenerationResultDocumentResponse("patient_letter", "Patient letter", "Letter.", "hash-letter")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var nested = page.FindAll(".provenance-list__nested");
        Assert.Equal(
            new[] { "Consultation note", "Patient letter" },
            nested.Select(row => row.TextContent.Trim()).ToArray());

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.Contains("Output hash (v3)", provenance);
        Assert.Contains("hash-note", provenance);
        Assert.Contains("hash-letter", provenance);
    }

    [Fact]
    public void DocumentBackedInput_NamesTheExtractorThatReadIt()
    {
        // #238: beside the input hash, never inside it. This is the fact a
        // reviewer needs when a consult says something the referral did not —
        // whether a machine read it, and with what.
        WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            // The Api's own constant, not a copy of the string: the client
            // record is a hand-written mirror, so the test is where the two
            // are held together.
            ["consult_draft"] = new[] { new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 3) },
            // #240: a reviewer can see that a revision layer existed and was
            // resolved one way to produce this text.
            ["prior_notes"] = new[]
            {
                new ConsultInputOrigin(
                    Consultologist.Api.Models.ConsultInputOriginKinds.Document,
                    "openxml/3.5.1",
                    null,
                    TrackedChangesResolved: true)
            }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.Contains("consult_draft", provenance);
        Assert.Contains("pdfpig/0.1.15", provenance);
        Assert.Contains("3 pages", provenance);
        Assert.Contains("tracked changes resolved to the accepted view", provenance);
    }

    [Fact]
    public void ASinglePreviousRunOrigin_NamesTheRunAndTheDeliverable()
    {
        // #510: an input copied from one of the account's runs names the run
        // and the deliverable — never "read from a document".
        WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("previous-run", TextSha256: "52593837" + new string('0', 56), SourceJobId: "fedcba9876543210fedcba9876543210", SourceResultId: "consult") }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var row = page.Find(".provenance-list__nested + dd");
        // #546 appended the source link; the sentence itself is unchanged.
        Assert.StartsWith("copied from deliverable 'consult' of run fedcba98…", row.TextContent.Trim());
        Assert.Contains("text 52593837…", row.TextContent);
        Assert.DoesNotContain("read from a document", row.TextContent);
        Assert.Equal("/history/fedcba9876543210fedcba9876543210", row.QuerySelector(".provenance-source-link")!.GetAttribute("href"));
    }

    [Fact]
    public void ASlotReadFromOneDocument_KeepsTheSentence()
    {
        // #428 changed the shape, not the words: one document is the plain
        // sentence, with no list around it.
        WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 3) }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".provenance-documents"));
        var row = page.Find(".provenance-list__nested + dd");
        Assert.Equal("read from a document by pdfpig/0.1.15 · 3 pages", row.TextContent.Trim());
    }

    [Fact]
    public void ADocumentsDigests_AreShownShortened_WithTheFullValueInTheTitle()
    {
        // #512: the file and its reading, beside the extractor — shortened in
        // the row, whole in the title, so a holder of the file can check it.
        const string File = "b6a313365b611c7ec0be83d67237876ae56d4fe5fac3b77e758985551f59037d";
        const string Text = "52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b";
        WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 3, false, File, Text) }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var row = page.Find(".provenance-list__nested + dd");
        Assert.Equal("read from a document by pdfpig/0.1.15 · 3 pages · file b6a31336… · text 52593837…", row.TextContent.Trim());
        Assert.Equal($"SHA-256 of the file's bytes as received: {File}", page.Find(".provenance-digest--file").GetAttribute("title"));
        Assert.Equal($"SHA-256 of the text read from it, as the input hash saw it: {Text}", page.Find(".provenance-digest--text").GetAttribute("title"));
        Assert.DoesNotContain(File, row.TextContent);
    }

    [Fact]
    public void ASlotReadFromFourDocuments_ListsOneRowPerDocumentInOrder()
    {
        // One slot, four readings: the slot's row stays one, and the documents
        // are listed under it in the order they were supplied.
        WithJob(5, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["prior_notes"] = new[]
            {
                new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "text/1"),
                new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 4),
                new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "openxml/3.5.1", null, TrackedChangesResolved: true),
                new ConsultInputOrigin(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 1)
            }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal(new[] { "prior_notes" }, page.FindAll(".provenance-list__nested").Select(row => row.TextContent.Trim()));
        Assert.Equal(
            new[]
            {
                "read from a document by text/1",
                "read from a document by pdfpig/0.1.15 · 4 pages",
                "read from a document by openxml/3.5.1 · tracked changes resolved to the accepted view",
                "read from a document by pdfpig/0.1.15 · 1 page"
            },
            page.FindAll(".provenance-documents li").Select(row => row.TextContent.Trim()));
    }

    [Fact]
    public void JobWithNoRecordedOrigin_ClaimsNothingAboutItsInputs()
    {
        // Absence is not an assertion that the input was typed: every job
        // recorded before #238, and every email job until #237, has none.
        WithJob(3);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.DoesNotContain("read from a document", provenance);
        Assert.DoesNotContain("typed", provenance);
    }

    [Fact]
    public void LegacyJob_ListsNoPerDeliverableRows()
    {
        WithJob(2);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".provenance-list__nested"));
        Assert.Contains("Output hash (v2)", page.Find(".provenance-list").TextContent);
    }

    private static IReadOnlyList<string> Chips(IRenderedComponent<History> page) =>
        page.FindAll(".provenance-chip").Select(chip => chip.TextContent.Trim()).ToList();

    private const string NothingApplied =
        "No document applies to these inputs. 'Patient letter' needs billable to be 'true'; it is not supplied.";

    private void WithJobBornFailed()
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, "Failed", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow,
                    TotalBlockCount: 0, CompletedBlockCount: 0, FailedBlockCount: 0, Source: "email", FailedAtStart: true)
            },
            null));

        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            "Failed",
            TotalBlockCount: 0,
            CompletedBlockCount: 0,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: false,
            EffectiveInputHash: "aaaa",
            EffectiveInputHashVersion: 4,
            SkippedDocuments: new[] { new ConsultSkippedDocumentResponse("patient_letter", "Patient letter", "needs billable to be 'true'; it is not supplied") },
            PackageSpecVersion: 8,
            WorkflowPackage: "general@v2026.08.1",
            PackageTitle: "Breast oncology consults",
            StartFailure: NothingApplied));
    }

    [Fact]
    public void AJobBornFailed_IsNamedInTheList_AndSaysWhyInTheDetail()
    {
        // #434: a well-formed request the package produced nothing for. The
        // list names the class — a Failed row with 0 / 0 sections would read as
        // a run that broke — and the detail says what was wanted, beside the
        // provenance a run would have carried.
        WithJobBornFailed();

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("Failed — nothing applied", page.Find(".job-status-badge").TextContent.Trim());
        Assert.Contains("0 / 0 sections", page.Markup, StringComparison.Ordinal);

        var done = page.FindAll(".node-row").Last();
        Assert.Equal("Nothing produced", done.QuerySelector(".node-row__label")!.TextContent.Trim());
        Assert.Contains(NothingApplied, done.TextContent, StringComparison.Ordinal);

        Assert.Contains("not produced — it needs billable to be 'true'; it is not supplied", page.Find(".provenance-list").TextContent, StringComparison.Ordinal);
        Assert.Contains("Breast oncology consults · general@v2026.08.1", Chips(page));
    }

    [Fact]
    public void AJobThatRanAndFailed_IsStillJustFailed()
    {
        // The other half of the distinction: no StartFailure, so nothing here
        // changes.
        WithJob(3);
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, "Failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 4, FailedBlockCount: 1)
            },
            null));
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Failed", 9, 4, 1,
            new Dictionary<string, string>(), new Dictionary<string, string>(), false,
            RuntimeFailureError: "Section generation failed."));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("Failed", page.Find(".job-status-badge").TextContent.Trim());
        Assert.Equal("Failed", page.FindAll(".node-row").Last().QuerySelector(".node-row__label")!.TextContent.Trim());
        Assert.DoesNotContain("Nothing produced", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AJobsPackageFormat_IsOnTheProvenanceRow()
    {
        // #373: the one version on that row an outside reader can act on. It
        // used to show the record's storage version instead, labelled a schema.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("format v8", Chips(page));
    }

    [Fact]
    public void TheRecordsStorageVersion_IsNotOnTheProvenanceRow()
    {
        // It is a storage discriminator — stamped 6 or 7 and never 8, so a v8
        // job read as v7. The row is for evidence a reader can act on.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.DoesNotContain(Chips(page), chip => chip.Contains("schema", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Chips(page), chip => chip.Contains("v7", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecordsStorageVersion_IsStillReadableInTheProvenanceList()
    {
        // Kept, not dropped: it says which fields on the record are the real
        // ones, which has explained real confusion before.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var list = page.Find(".provenance-list").TextContent;
        Assert.Contains("Record storage version", list, StringComparison.Ordinal);
        Assert.Contains("v7", list, StringComparison.Ordinal);
    }

    [Fact]
    public void AJobThatRecordedNoFormat_ShowsNoFormatChip()
    {
        // Every job from before #373. An absent chip is the record saying it
        // does not know; a dash would read as "no format".
        WithJob(3, packageSpecVersion: null, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.DoesNotContain(Chips(page), chip => chip.Contains("format", StringComparison.OrdinalIgnoreCase));
    }

    // ----- #549: the Rerun action -----

    [Fact]
    public void AHeldRun_OffersRerun_WithNoReason()
    {
        WithJob(3, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.False(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".rerun-row__reason"));
    }

    [Fact]
    public void ADroppedRun_GreysRerun_AndSaysTheDate()
    {
        WithJob(3, inputsDroppedAtUtc: new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.True(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Contains("inputs deleted", page.Find(".rerun-row__reason").TextContent);
    }

    [Fact]
    public void ARunNeverHeld_GreysRerun_AndSaysSo()
    {
        WithJob(3);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.True(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Equal("inputs were not held for this run", page.Find(".rerun-row__reason").TextContent.Trim());
    }

    [Fact]
    public void AFailedRun_OffersNoRerun()
    {
        WithJob(3, status: "Failed", heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".rerun-button"));
    }

    [Fact]
    public async Task Rerun_StartsTheReplay_AndOpensItsRunView()
    {
        WithJob(3, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });
        AIService.RerunConsultGenerationJobAsync(JobId).Returns("new-job-1");

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        await page.Find(".rerun-button").ClickAsync(new());

        await AIService.Received(1).RerunConsultGenerationJobAsync(JobId);
        var navigation = (Microsoft.AspNetCore.Components.NavigationManager)Services
            .GetService(typeof(Microsoft.AspNetCore.Components.NavigationManager))!;
        Assert.EndsWith("/consults/new-job-1", navigation.Uri);
    }

    // ----- #549: the per-stage comparison on a rerun's detail -----

    private const string SourceJobId = "fedcba9876543210fedcba9876543210";

    private static IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>> RerunOrigins() =>
        new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["consult_draft"] = new[] { new ConsultInputOrigin("rerun", TextSha256: "aa", SourceJobId: SourceJobId) }
        };

    private void WithRerunSource(IReadOnlyDictionary<string, ConsultGenerationNodeStatus> nodeOutputs, string effectiveInputHash = "aaaa")
    {
        AIService.GetConsultGenerationJobAsync(SourceJobId).Returns(new ConsultGenerationJobResponse(
            SourceJobId, "user-1", "Completed", 9, 9, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            NodeOutputs: nodeOutputs,
            EffectiveInputHash: effectiveInputHash,
            EffectiveInputHashVersion: 3));
    }

    private static ConsultGenerationNodeStatus Node(string id, string? inputHash, string? outputHash) =>
        new(id, id, "Completed", inputHash, outputHash, null, null, HashVersion: 5);

    [Fact]
    public void ARerunDetail_ShowsTheTableAgainstItsSource_WithHonestVerdicts()
    {
        WithJob(3,
            inputOrigins: RerunOrigins(),
            nodes: new[]
            {
                new ConsultGenerationNodeDescriptor("extract", "Extract"),
                new ConsultGenerationNodeDescriptor("draft", "Draft")
            },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["extract"] = Node("extract", "in1", "outA"),
                ["draft"] = Node("draft", "in2", "outX")
            });
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus>
        {
            ["extract"] = Node("extract", "in1", "outA"),
            ["draft"] = Node("draft", "in2", "outB")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var verdicts = page.FindAll(".rerun-comparison__verdict").Select(cell => cell.TextContent.Trim()).ToArray();
        Assert.Contains("same", verdicts);
        Assert.Contains("different", verdicts);
        // The section names its source and links to it.
        Assert.Contains(SourceJobId[..8], page.Find(".rerun-comparison").Parent!.TextContent);
    }

    [Fact]
    public void EqualEffectiveInputs_ReadAsByConstruction_AndUnequalOnesSayBug()
    {
        // Both jobs stamp version 3 here (WithJob's outputHashVersion doubles
        // as the effective-input version in this fixture).
        WithJob(3, inputOrigins: RerunOrigins(),
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Contains("equal by construction", page.Find(".rerun-comparison__inputs").TextContent);

        // A differing hash is a bug and the panel says exactly that.
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") }, effectiveInputHash: "zzzz");
        var mismatchPage = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));
        Assert.Contains("this is a bug", mismatchPage.Find(".rerun-comparison__inputs--bug").TextContent);
    }

    [Fact]
    public void AnUnreachableSource_DegradesToANamedRow_NeverABrokenPanel()
    {
        WithJob(3, inputOrigins: RerunOrigins());
        AIService.GetConsultGenerationJobAsync(SourceJobId)
            .Returns<ConsultGenerationJobResponse>(_ => throw new InvalidOperationException("storage blip"));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("could not be loaded", page.Find(".rerun-comparison__unavailable").TextContent);
    }

    [Fact]
    public void AnOrdinaryRun_ShowsNoComparison()
    {
        WithJob(3);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".rerun-comparison"));
    }

    // ----- #582: the stamped verdict line -----

    [Fact]
    public void AStampedPass_RendersItsLine_AndMarksTheCountedStage()
    {
        WithJob(3,
            rerunOf: SourceJobId, rerunVerdict: "pass",
            nodes: new[] { new ConsultGenerationNodeDescriptor("extract", "Extract", Reproducible: true) },
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("Verdict: pass — every reproducible stage matched", page.Find(".rerun-verdict--pass").TextContent);
        Assert.Single(page.FindAll(".rerun-comparison__counted"));
    }

    [Fact]
    public void AStampedFail_NamesTheStage()
    {
        WithJob(3, rerunOf: SourceJobId, rerunVerdict: "fail", rerunDivergence: "draft",
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["draft"] = Node("draft", "in1", "outX") });
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus> { ["draft"] = Node("draft", "in1", "outB") });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("first divergence at draft", page.Find(".rerun-verdict--fail").TextContent);
    }

    // ----- #546: links both ways -----

    [Fact]
    public void ARunThatWasUsed_ListsItsConsumers_WithLinks()
    {
        WithJob(3);
        AIService.GetConsultGenerationJobLinksAsync(JobId).Returns(new[]
        {
            new ConsultJobLinkResponse("aaaa1111aaaa1111aaaa1111aaaa1111", "rerun"),
            new ConsultJobLinkResponse("bbbb2222bbbb2222bbbb2222bbbb2222", "previous-run", "referrals", "note")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var rows = page.FindAll(".used-by__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("replayed by run", rows[0].TextContent);
        Assert.Contains("deliverable 'note' copied into input 'referrals' of run", rows[1].TextContent);
        Assert.Equal("/history/aaaa1111aaaa1111aaaa1111aaaa1111", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("/history/bbbb2222bbbb2222bbbb2222bbbb2222", rows[1].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void ARunNobodyUsed_AddsNoSection()
    {
        WithJob(3);
        AIService.GetConsultGenerationJobLinksAsync(JobId).Returns(Array.Empty<ConsultJobLinkResponse>());

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".used-by"));
        Assert.Empty(page.FindAll(".used-by__unavailable"));
    }

    [Fact]
    public void ALinksFetchFailure_DegradesToANamedRow()
    {
        WithJob(3);
        AIService.GetConsultGenerationJobLinksAsync(JobId)
            .Returns<IReadOnlyList<ConsultJobLinkResponse>>(_ => throw new InvalidOperationException("storage blip"));

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("could not be loaded", page.Find(".used-by__unavailable").TextContent);
    }

    [Fact]
    public void ACopiedFromLine_LinksToItsSource()
    {
        WithJob(3, inputOrigins: new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>
        {
            ["referrals"] = new[]
            {
                new ConsultInputOrigin("previous-run", TextSha256: "aa", SourceJobId: SourceJobId, SourceResultId: "note")
            }
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var link = page.Find(".provenance-source-link");
        Assert.Equal($"/history/{SourceJobId}", link.GetAttribute("href"));
    }

    [Fact]
    public void A549EraRerun_ShowsTheTable_AndNoVerdictLine()
    {
        // The comparison resolves through the slot origins; the record has no
        // stamped verdict, and no line is guessed.
        WithJob(3, inputOrigins: RerunOrigins(),
            nodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });
        WithRerunSource(new Dictionary<string, ConsultGenerationNodeStatus> { ["extract"] = Node("extract", "in1", "outA") });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.NotEmpty(page.FindAll(".rerun-comparison__table"));
        Assert.Empty(page.FindAll(".rerun-verdict"));
    }
}
