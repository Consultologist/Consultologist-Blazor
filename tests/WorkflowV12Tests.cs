using System.Text.Json;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v12 step (a) (#617, package-format-v12-design.md §§ 3–5, 8, 13): the
/// validator accepts 12 and knows its four declaration shapes — the optional
/// macro pair, the placed macro entry, the signature token, and the check
/// node — refusing each below 12 by name. Nothing runs yet.
/// </summary>
public static class V12Fixtures
{
    public static WorkflowPackageManifest Minimal() => V11Fixtures.Minimal() with { SpecVersion = 12 };

    /// <summary>
    /// The § 13 shape end to end: a document-terms extraction over the result
    /// aggregator (prompt shared with the input extraction — v6 allows it),
    /// and a terms-subset check gating the deliverable.
    /// </summary>
    public static WorkflowPackageManifest WithCheck(WorkflowPackageManifest? from = null)
    {
        var manifest = from ?? Minimal();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("extract-document-terms", "Extracting document terms",
                Prompt: "extract-patient-concepts",
                Bindings: new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("node:assemble-note") },
                Output: new WorkflowNodeOutputSpec("concept-list")),
            new("coverage", "Coverage check",
                Kind: WorkflowNodeKinds.Check,
                Op: WorkflowCheckOps.TermsSubset,
                Of: "node:extract-patient-concepts",
                In: "node:extract-document-terms",
                FailWith: "The note does not cover every clinical term found in the referral.")
        };

        return manifest with
        {
            Nodes = nodes,
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Check = "node:coverage" } : r).ToList()
        };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV12GateTests
{
    [Fact]
    public void TwelveIsAccepted_ButDoesNotRunYet()
    {
        // (a) #617: the validator's gate moved first, the engine's follows at
        // rung (g) — publishable before runnable, v8's own shipping shape.
        Assert.Contains(12, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(12, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);

        var result = V12Fixtures.Validate(V12Fixtures.Minimal());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ThirteenIsRefused_NamingTheSet()
    {
        Assert.Contains(V12Fixtures.Validate(V12Fixtures.Minimal() with { SpecVersion = 13 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7, 8, 9, 10, 11 or 12"));
    }

    [Fact]
    public void AtEleven_EveryNewKey_IsRefusedByName()
    {
        // One manifest carrying all of v12 at 11: every key answers with its
        // version requirement, none with a parse error or a wrong sentence.
        var (manifest, files) = V11Fixtures.WithMacro();
        manifest = V12Fixtures.WithCheck(manifest with { SpecVersion = 11 }) with { SpecVersion = 11 };
        manifest = manifest with
        {
            Macros = manifest.Macros!.Select(m => m with { Optional = true, Default = true }).ToList(),
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new("disclaimer", Before: "node:section-instructions") } }
                : r).ToList()
        };

        var errors = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors;

        Assert.Contains("Macro 'disclaimer' declares optional, which requires specVersion 12.", errors);
        Assert.Contains("Macro 'disclaimer' declares default, which requires specVersion 12.", errors);
        Assert.Contains("Result 'consult' places macro 'disclaimer', which requires specVersion 12.", errors);
        Assert.Contains("Result 'consult' declares check, which requires specVersion 12.", errors);
        Assert.Contains("Node 'coverage' declares kind 'check', which requires specVersion 12.", errors);
        Assert.Contains("Node 'coverage' declares op, which requires specVersion 12.", errors);
        Assert.Contains("Node 'coverage' declares of, which requires specVersion 12.", errors);
        Assert.Contains("Node 'coverage' declares in, which requires specVersion 12.", errors);
        Assert.Contains("Node 'coverage' declares failWith, which requires specVersion 12.", errors);
    }

    [Fact]
    public void AV11Manifest_WritesTheBytesItAlwaysWrote()
    {
        // § 7's control, and the converter's one real failure mode: a bare
        // macro entry must serialize as the v11 string, never as an object —
        // and the round trip must be a fixed point.
        var (manifest, _) = V11Fixtures.WithMacro();
        var json = WorkflowV10StructureTests.Write(manifest);

        Assert.Contains("\"macros\":[\"disclaimer\"]", json);
        Assert.DoesNotContain("\"optional\"", json);
        Assert.DoesNotContain("\"default\"", json);
        Assert.DoesNotContain("\"check\"", json);
        Assert.DoesNotContain("\"op\"", json);
        Assert.Equal(json, WorkflowV10StructureTests.Write(
            WorkflowPackageManifestJson.Read(json, "v11", WorkflowPackageValidator.AcceptedSpecVersions)));
    }
}

public class WorkflowV12OptionalMacroTests
{
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) OptionalMacro(bool? optional, bool? @default)
    {
        var (manifest, files) = V11Fixtures.WithMacro(from: V12Fixtures.Minimal());
        return (manifest with
        {
            Macros = manifest.Macros!.Select(m => m with { Optional = optional, Default = @default }).ToList()
        }, files);
    }

    [Fact]
    public void AnOptionalMacro_WithItsDefault_Publishes()
    {
        var result = V12Fixtures.Validate(OptionalMacro(true, false));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void OptionalWithoutDefault_IsRefused()
    {
        // § 3: the email door has no form — the package must say what a run
        // that makes no choice does.
        Assert.Contains(
            "Macro 'disclaimer' is optional and declares no default; an optional macro must say what a run that makes no choice does.",
            V12Fixtures.Validate(OptionalMacro(true, null)).Errors);
    }

    [Fact]
    public void DefaultWithoutOptional_IsRefused()
    {
        Assert.Contains(
            "Macro 'disclaimer' declares default but is not optional; only optional: true takes a per-run choice.",
            V12Fixtures.Validate(OptionalMacro(null, true)).Errors);
    }
}

public class WorkflowV12PlacementTests
{
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Placed(string? before, string? after)
    {
        var (manifest, files) = V11Fixtures.WithMacro(from: V12Fixtures.Minimal());
        return (manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new("disclaimer", before, after) } }
                : r).ToList()
        }, files);
    }

    [Fact]
    public void APlacedMacro_OnAnAggregatedSource_Publishes()
    {
        // The § 4 shape: before a section the deliverable's aggregator holds.
        var result = V12Fixtures.Validate(Placed("node:section-instructions", null));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void BothAnchors_AreRefused()
    {
        Assert.Contains(
            "Result 'consult' places macro 'disclaimer' with both before and after; a placement names exactly one.",
            V12Fixtures.Validate(Placed("node:section-instructions", "node:section-instructions")).Errors);
    }

    [Fact]
    public void AnAnchorTheAggregatorDoesNotHold_IsRefused()
    {
        Assert.Contains(
            "Result 'consult' places macro 'disclaimer' before 'node:extract-patient-concepts', which its aggregator 'assemble-note' does not aggregate.",
            V12Fixtures.Validate(Placed("node:extract-patient-concepts", null)).Errors);
    }

    [Fact]
    public void APlacedMacro_StillCountsAsReferenced()
    {
        // The orphan rule reads the entry's id — a placed macro is never an
        // orphan by virtue of being placed.
        var errors = V12Fixtures.Validate(Placed("node:section-instructions", null)).Errors;
        Assert.DoesNotContain(errors, e => e.Contains("is not referenced by any result"));
    }
}

public class WorkflowV12SignatureTokenTests
{
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) TokenMacro(
        string template = "Sincerely,\n{{profile:signature}}",
        bool? flag = null,
        bool? optional = null,
        bool? @default = null)
    {
        var (manifest, files) = V11Fixtures.WithMacro(template, from: V12Fixtures.Minimal());
        return (manifest with
        {
            Macros = manifest.Macros!.Select(m => m with { Optional = optional, Default = @default }).ToList(),
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Signature = flag } : r).ToList()
        }, files);
    }

    [Fact]
    public void TheToken_AtTwelve_Publishes()
    {
        var result = V12Fixtures.Validate(TokenMacro());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheFlagBesideATokenMacro_IsRefused_SignedOnce()
    {
        Assert.Contains(
            "Result 'consult' declares signature and references macro 'disclaimer', which contains {{profile:signature}}; a deliverable is signed once.",
            V12Fixtures.Validate(TokenMacro(flag: true)).Errors);
    }

    [Fact]
    public void TwoTokens_AreRefused_SignedOnce()
    {
        Assert.Contains(
            "Result 'consult' references {{profile:signature}} more than once across its macros; a deliverable is signed once.",
            V12Fixtures.Validate(TokenMacro("{{profile:signature}} and again {{profile:signature}}")).Errors);
    }

    [Fact]
    public void TheToken_InAnOptionalMacro_IsRefused()
    {
        // § 5: per-run signature choice was rejected via #516 option 2, and
        // stays rejected.
        Assert.Contains(
            "Macro 'disclaimer' is optional and carries {{profile:signature}}; a per-run signature choice was rejected (#516) and stays rejected.",
            V12Fixtures.Validate(TokenMacro(optional: true, @default: false)).Errors);
    }
}

public class WorkflowV12CheckNodeTests
{
    [Fact]
    public void TheCheckShape_EndToEnd_Publishes()
    {
        // § 13's worked example: extraction over the aggregator, the subset
        // check, the result's gate — and the check chain exempt from feeding
        // the result the way a classifier is.
        var result = V12Fixtures.Validate(V12Fixtures.WithCheck());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ACheck_MissingItsParts_IsRefusedPartByPart()
    {
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "coverage"
                ? n with { Op = null, Of = null, In = null, FailWith = null }
                : n).ToList()
        };

        var errors = V12Fixtures.Validate(manifest).Errors;
        Assert.Contains("Check 'coverage' declares no op; the operations are terms-subset.", errors);
        Assert.Contains("Check 'coverage' declares no of; a check names its two concept-list operands as node:<id> references.", errors);
        Assert.Contains("Check 'coverage' declares no in; a check names its two concept-list operands as node:<id> references.", errors);
        Assert.Contains("Check 'coverage' declares no failWith; a failed check must speak the package's own sentence.", errors);
    }

    [Fact]
    public void AnUnknownOp_IsRefusedByName()
    {
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "coverage" ? n with { Op = "terms-equal" } : n).ToList()
        };

        Assert.Contains(
            "Check 'coverage' declares unknown op 'terms-equal' (accepted: terms-subset).",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void PromptFamilyFields_OnACheck_AreRefused()
    {
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "coverage" ? n with { Prompt = "extract-patient-concepts" } : n).ToList()
        };

        Assert.Contains(
            "Check 'coverage' must declare only op, of, in and failWith (no prompt, bindings, output, forEach, or values).",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AnOperand_ThatIsNotConceptList_IsRefused()
    {
        // The aggregator declares no output at all — and the check must say
        // so by contract, not by presence.
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "coverage" ? n with { In = "node:assemble-note" } : n).ToList()
        };

        Assert.Contains(
            "Check 'coverage' in names node 'assemble-note', which does not declare the concept-list contract.",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AnOperand_WithSomeOtherContract_IsRefused_ByContractNotPresence()
    {
        // The trap the design names: "declares any output" is not
        // "declares concept-list". The document-terms node here declares the
        // catalog's classification contract — real output, wrong contract.
        var manifest = V12Fixtures.WithCheck();
        var schemas = new Dictionary<string, string>(manifest.Schemas!) { ["verdict"] = "schemas/verdict.json" };
        manifest = manifest with
        {
            Schemas = schemas,
            Nodes = manifest.Nodes!.Select(n => n.Id == "extract-document-terms"
                ? n with { Output = new WorkflowNodeOutputSpec("verdict") }
                : n).ToList()
        };
        var files = V6Fixtures.Files(manifest);
        files["schemas/verdict.json"] = TestOutputContracts.CatalogSchemas["classification"];

        Assert.Contains(
            "Check 'coverage' in names node 'extract-document-terms', which does not declare the concept-list contract.",
            WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors);
    }

    [Fact]
    public void AStampedPackage_AnswersByItsStamp_NotTheRunningCatalog()
    {
        // #433's discipline carried into the operand check: an immutable
        // package matched once at publish; the stamp is the authority even
        // when the running catalog is empty.
        var manifest = V12Fixtures.WithCheck();
        var result = WorkflowPackageValidator.Validate(
            manifest,
            V6Fixtures.Files(manifest),
            new Dictionary<string, string>(StringComparer.Ordinal),
            stampedContracts: new Dictionary<string, string>(StringComparer.Ordinal) { ["concept-list"] = WorkflowNodeDefaults.ConceptListSchemaId });
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void CheckMembers_OnAPromptNode_AreRefused()
    {
        var manifest = V12Fixtures.Minimal();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "identify-problem" ? n with { FailWith = "nope" } : n).ToList()
        };

        Assert.Contains(
            "Node 'identify-problem' declares failWith but is not a check node; only kind 'check' declares it.",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AResultCheck_NamingANonCheckNode_IsRefused()
    {
        var manifest = V12Fixtures.Minimal();
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Check = "node:identify-problem" } : r).ToList()
        };

        Assert.Contains(
            "Result 'consult' check names 'identify-problem', which is not a check node.",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void ACheck_NamedByNoResult_IsAnOrphan()
    {
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Results = manifest.Results!.Select(r => r with { Check = null }).ToList()
        };

        Assert.Contains(
            "Check 'coverage' is not named by any result; a check gates a deliverable, or it is dead weight.",
            V12Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void ADanglingNode_OutsideTheCheckChain_IsStillRefused()
    {
        // The exemption is the check chain, not a general amnesty.
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
            {
                new("dangler", "Dangles",
                    Prompt: "extract-patient-concepts",
                    Bindings: new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("input:consult_draft") })
            }
        };

        Assert.Contains(V12Fixtures.Validate(manifest).Errors,
            e => e.StartsWith("Node 'dangler' does not feed the result", StringComparison.Ordinal));
    }

    [Fact]
    public void ACheckCycle_IsRefusedAsACycle()
    {
        // of/in are real edges: a check whose operand chain loops back is the
        // acyclicity rule's business, not a stack overflow's.
        var manifest = V12Fixtures.WithCheck();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "extract-document-terms"
                ? n with { Bindings = new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("node:coverage") } }
                : n).ToList()
        };

        Assert.Contains(V12Fixtures.Validate(manifest).Errors, e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// v12 rung (c) (#619, design § 4/§ 6/§ 7): the placement composer — per
/// source, before-macros, the part, after-macros; then the unplaced; the
/// appended entries in document order; and the aggregator's hash input
/// (Render's bytes) untouched by any of it.
/// </summary>
public class WorkflowV12PlacementRuntimeTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["disclaimer"] = "This disclaimer is fixed.",
        ["closing"] = "Signed on {{run:date}}."
    };

    private static Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts Facts() =>
        new(new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1", "east.ca.api.consultologist.ai", "Taylor Reyes");

    private static readonly string[] SourceRefs = { "node:intro", "node:findings" };

    private static readonly IReadOnlyList<Consultologist.Api.Jobs.ConsultAggregateRenderer.Part> Parts = new Consultologist.Api.Jobs.ConsultAggregateRenderer.Part[]
    {
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.ScalarPart("Intro."),
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.ForEachPart(new[] { ("History", "Unremarkable."), ("Exam", "Benign.") })
    };

    private static (string Text, IReadOnlyList<Consultologist.Api.Models.ConsultAppendedEntry>? Appended, bool TokenCarried) Compose(
        IReadOnlyList<string>? macroIds,
        IReadOnlyList<Consultologist.Api.Models.ConsultMacroPlacement>? placements,
        IReadOnlyList<string>? sourceRefs = null,
        IReadOnlyList<Consultologist.Api.Jobs.ConsultAggregateRenderer.Part>? parts = null) =>
        Consultologist.Api.Jobs.ConsultMacroExpander.Compose(
            sourceRefs ?? SourceRefs, parts ?? Parts, macroIds, placements, Texts, NoValues, null, NoValues, Facts());

    [Fact]
    public void NothingPlaced_ComposesTheAppendBytesExactly()
    {
        // The v11 control: the composer with no placements is byte-identical
        // to Render-then-Append — the join is associative, and this pin is
        // what lets the engine route every deliverable through Compose.
        var rendered = Consultologist.Api.Jobs.ConsultAggregateRenderer.Render(Parts);
        var (appendText, appendEntries) = Consultologist.Api.Jobs.ConsultMacroExpander.Append(
            rendered, new[] { "disclaimer", "closing" }, Texts, NoValues, null, NoValues, Facts());

        var (composed, composedEntries, _) = Compose(new[] { "disclaimer", "closing" }, placements: null);

        Assert.Equal(appendText, composed);
        Assert.Equal(
            appendEntries!.Select(e => (e.Kind, e.Id)),
            composedEntries!.Select(e => (e.Kind, e.Id)));

        // And with no macros at all, Render's own bytes.
        var (bare, none, _) = Compose(null, null);
        Assert.Equal(rendered, bare);
        Assert.Null(none);
    }

    [Fact]
    public void APlacedMacro_SitsBeforeItsSection()
    {
        var (text, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:findings") });

        Assert.Equal(
            "Intro.\n\nThis disclaimer is fixed.\n\n## History\n\nUnremarkable.\n\n## Exam\n\nBenign.",
            text);
    }

    [Fact]
    public void AFannedSource_IsOneBlock_NeverSplitByAPlacement()
    {
        // § 11 assumption 1, held: after the fanned source means after the
        // WHOLE block — never between History and Exam.
        var (text, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", After: "node:findings") });

        Assert.Equal(
            "Intro.\n\n## History\n\nUnremarkable.\n\n## Exam\n\nBenign.\n\nThis disclaimer is fixed.",
            text);
    }

    [Fact]
    public void PlacedAndUnplaced_ComposeInDocumentOrder_AndAppendedSaysSo()
    {
        // 'closing' is DECLARED first but placed nowhere; 'disclaimer' is
        // declared second and placed before the first section. Document
        // order wins in the text and in appended[] alike (§ 6).
        var (text, appended, _) = Compose(
            new[] { "closing", "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:intro") });

        Assert.Equal(
            "This disclaimer is fixed.\n\nIntro.\n\n## History\n\nUnremarkable.\n\n## Exam\n\nBenign.\n\nSigned on 2026-09-02.",
            text);
        Assert.Equal(new[] { "disclaimer", "closing" }, appended!.Select(e => e.Id));
    }

    [Fact]
    public void BeforeAndAfterTheSameSection_BothLand()
    {
        var (text, appended, _) = Compose(
            new[] { "disclaimer", "closing" },
            new[]
            {
                new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:intro"),
                new Consultologist.Api.Models.ConsultMacroPlacement("closing", After: "node:intro")
            });

        Assert.Equal(
            "This disclaimer is fixed.\n\nIntro.\n\nSigned on 2026-09-02.\n\n## History\n\nUnremarkable.\n\n## Exam\n\nBenign.",
            text);
        Assert.Equal(new[] { "disclaimer", "closing" }, appended!.Select(e => e.Id));
    }

    [Fact]
    public void TheSignature_StillFollowsEveryPlacedMacro()
    {
        var (text, appended, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:intro") });
        var snapshot = new Consultologist.Api.Jobs.ConsultSignatureSnapshot("s1", "Dr. Reyes", "2026-09-01");

        var (finalText, finalAppended, unsigned) = Consultologist.Api.Jobs.ConsultSignatureAppend.Apply(
            text, appended, signed: true, snapshot);

        Assert.EndsWith("Dr. Reyes", finalText);
        Assert.Equal(new[] { "disclaimer", "signature" }, finalAppended!.Select(e => e.Id == "disclaimer" ? e.Id : e.Kind));
        Assert.Null(unsigned);
    }

    [Fact]
    public void TheAggregatorsHashInput_NeverLearnsAboutPlacement()
    {
        // § 7 pinned at the unit the engine stamps from: Render's bytes — and
        // so Sha256Hex(Render) — are identical across no-macros, appended and
        // placed, while the composed document differs each time.
        var hash = Consultologist.Api.Workflow.ConsultGenerationProvenance.Sha256Hex(
            Consultologist.Api.Jobs.ConsultAggregateRenderer.Render(Parts));

        var (bare, _, _) = Compose(null, null);
        var (appendedText, _, _) = Compose(new[] { "disclaimer" }, null);
        var (placedText, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:intro") });

        Assert.Equal(hash, Consultologist.Api.Workflow.ConsultGenerationProvenance.Sha256Hex(
            Consultologist.Api.Jobs.ConsultAggregateRenderer.Render(Parts)));
        Assert.NotEqual(bare, appendedText);
        Assert.NotEqual(appendedText, placedText);
        Assert.NotEqual(bare, placedText);
    }

    [Fact]
    public void MismatchedPartsAndSources_FailLoud()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Compose(
            new[] { "disclaimer" }, null,
            sourceRefs: new[] { "node:intro" }));
        Assert.Equal("Aggregate composition received 2 parts for 1 sources.", exception.Message);
    }

    [Fact]
    public void APlacementForAMissingMacro_PlacesNothing()
    {
        // The filters keep ids and placements in lockstep; this is the belt
        // to that suspender — a stray placement never mis-places or throws.
        var (text, _, _) = Compose(
            new[] { "disclaimer" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("ghost", Before: "node:intro") });

        Assert.StartsWith("Intro.", text);
        Assert.EndsWith("This disclaimer is fixed.", text);
    }

    [Fact]
    public void TheFilters_DropAPlacement_WithItsDeclinedMacro()
    {
        var descriptors = new List<Consultologist.Api.Models.ConsultResultDescriptor>
        {
            new("consult", "assemble-note", "Consultation note",
                new[] { "disclaimer", "closing" },
                Signature: null,
                MacroPlacements: new[] { new Consultologist.Api.Models.ConsultMacroPlacement("closing", After: "node:intro") })
        };

        var filtered = Consultologist.Api.Jobs.ConsultGenerationJobStarter.FilterDescriptorMacros(
            descriptors, new Dictionary<string, bool> { ["closing"] = false });

        Assert.Equal(new[] { "disclaimer" }, filtered[0].Macros);
        Assert.Null(filtered[0].MacroPlacements);
    }

    [Fact]
    public void TheDeliverableTable_CarriesThePlacements()
    {
        var deliverable = Consultologist.Api.Jobs.ConsultDeliverables.Resolve(
            new[]
            {
                new Consultologist.Api.Models.ConsultResultDescriptor("consult", "assemble-note", "Consultation note",
                    new[] { "closing" },
                    MacroPlacements: new[] { new Consultologist.Api.Models.ConsultMacroPlacement("closing", Before: "node:intro") })
            },
            null,
            new Dictionary<string, Consultologist.Api.Models.ConsultNodeDescriptor>(StringComparer.Ordinal)).Single();

        var placement = Assert.Single(deliverable.MacroPlacements!);
        Assert.Equal(("closing", "node:intro"), (placement.Id, placement.Before));
    }
}
