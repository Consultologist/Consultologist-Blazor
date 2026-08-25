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
        string? provenanceRef = null)
    {
        // Terminal status only: a non-terminal row would start the page's real
        // 5-second polling loop.
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 9, FailedBlockCount: 0)
            },
            null));

        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            "Completed",
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
            AgentVersions: agentVersions,
            CatalogRef: catalogRef));
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
}
