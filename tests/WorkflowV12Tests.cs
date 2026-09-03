using System.Reflection;
using System.Text.Json;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.PackageFormat;
using NSubstitute;

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

/// <summary>
/// v12 rung (d) (#620, design § 5/§ 6): the signature token at run time — it
/// embeds the snapshotted block where its macro sits, names itself in
/// appended[] beside its carrier with the as-of date, renders empty when no
/// block was chosen, and Finish keeps the signed-once rule: an embedded
/// signature is never also appended.
/// </summary>
public class WorkflowV12SignatureTokenRuntimeTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static readonly Consultologist.Api.Jobs.ConsultSignatureSnapshot Snapshot =
        new("clinic-letters", "Taylor Reyes, MD", "2026-09-01");

    private static Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts Facts(Consultologist.Api.Jobs.ConsultSignatureSnapshot? snapshot) =>
        new(new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1", "east.ca.api.consultologist.ai", "Taylor Reyes", snapshot);

    private static readonly string[] SourceRefs = { "node:intro" };

    private static readonly IReadOnlyList<Consultologist.Api.Jobs.ConsultAggregateRenderer.Part> Parts =
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.Part[] { new Consultologist.Api.Jobs.ConsultAggregateRenderer.ScalarPart("Intro.") };

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["signoff"] = "Sincerely,\n{{profile:signature}}",
        ["disclaimer"] = "This disclaimer is fixed."
    };

    private static (string Text, IReadOnlyList<Consultologist.Api.Models.ConsultAppendedEntry>? Appended, bool TokenCarried) Compose(
        IReadOnlyList<string>? macroIds,
        Consultologist.Api.Jobs.ConsultSignatureSnapshot? snapshot,
        IReadOnlyList<Consultologist.Api.Models.ConsultMacroPlacement>? placements = null) =>
        Consultologist.Api.Jobs.ConsultMacroExpander.Compose(
            SourceRefs, Parts, macroIds, placements, Texts, NoValues, null, NoValues, Facts(snapshot));

    [Fact]
    public void TheToken_RendersTheSnapshot_AndEmptyWhenNoneChosen()
    {
        Assert.Equal("Signed Taylor Reyes, MD.", Consultologist.Api.Jobs.ConsultMacroExpander.Expand(
            "Signed {{profile:signature}}.", NoValues, null, NoValues, Facts(Snapshot)));
        // The § 4 optional-input semantic: the slot renders empty, the
        // surrounding text lands, and the record names why downstream.
        Assert.Equal("Signed .", Consultologist.Api.Jobs.ConsultMacroExpander.Expand(
            "Signed {{profile:signature}}.", NoValues, null, NoValues, Facts(null)));
        // profile:name is untouched by the fold.
        Assert.Equal("By Taylor Reyes.", Consultologist.Api.Jobs.ConsultMacroExpander.Expand(
            "By {{profile:name}}.", NoValues, null, NoValues, Facts(null)));
    }

    [Fact]
    public void TheEntry_FollowsItsCarrier_WithTheAsOfDate()
    {
        var (text, appended, carried) = Compose(new[] { "disclaimer", "signoff" }, Snapshot);

        Assert.True(carried);
        Assert.Equal("Intro.\n\nThis disclaimer is fixed.\n\nSincerely,\nTaylor Reyes, MD", text);
        Assert.Equal(
            new[] { ("macro", "disclaimer", (string?)null), ("macro", "signoff", (string?)null), ("signature", "clinic-letters", (string?)"2026-09-01") },
            appended!.Select(e => (e.Kind, e.Id, e.AsOf)));
    }

    [Fact]
    public void APlacedCarrier_TakesItsEntryPairWithIt()
    {
        // Document order (§ 6): the carrier is placed before the section, so
        // its macro entry AND its signature entry precede the unplaced
        // disclaimer's.
        var (text, appended, _) = Compose(
            new[] { "disclaimer", "signoff" },
            Snapshot,
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("signoff", Before: "node:intro") });

        Assert.Equal("Sincerely,\nTaylor Reyes, MD\n\nIntro.\n\nThis disclaimer is fixed.", text);
        Assert.Equal(new[] { "signoff", "clinic-letters", "disclaimer" }, appended!.Select(e => e.Id));
        Assert.Equal(new[] { "macro", "signature", "macro" }, appended.Select(e => e.Kind));
    }

    [Fact]
    public void NoChosenBlock_CarriesTheToken_WritesNoEntry()
    {
        var (text, appended, carried) = Compose(new[] { "signoff" }, snapshot: null);

        Assert.True(carried);
        Assert.Equal("Intro.\n\nSincerely,\n", text);
        var entry = Assert.Single(appended!);
        Assert.Equal(("macro", "signoff"), (entry.Kind, entry.Id));
    }

    [Fact]
    public void Finish_Embedded_ChangesNothing_AndNamesUnsigned()
    {
        var appended = new[] { new Consultologist.Api.Models.ConsultAppendedEntry("macro", "signoff") };

        var (text, entries, unsigned) = Consultologist.Api.Jobs.ConsultSignatureAppend.Finish(
            "Body", appended, signed: false, tokenCarried: true, Snapshot);
        Assert.Equal("Body", text);
        Assert.Same(appended, entries);
        Assert.Null(unsigned);

        var (_, _, unsignedNone) = Consultologist.Api.Jobs.ConsultSignatureAppend.Finish(
            "Body", appended, signed: false, tokenCarried: true, snapshot: null);
        Assert.True(unsignedNone);
    }

    [Fact]
    public void Finish_NotEmbedded_IsApplyByteForByte()
    {
        // The v11 flag path, unmoved: Finish without carriage is Apply.
        var appended = new[] { new Consultologist.Api.Models.ConsultAppendedEntry("macro", "disclaimer") };
        var viaApply = Consultologist.Api.Jobs.ConsultSignatureAppend.Apply("Body", appended, signed: true, Snapshot);
        var viaFinish = Consultologist.Api.Jobs.ConsultSignatureAppend.Finish("Body", appended, signed: true, tokenCarried: false, Snapshot);

        Assert.Equal(viaApply.Text, viaFinish.Text);
        Assert.Equal(viaApply.Appended!.Select(e => (e.Kind, e.Id, e.AsOf)), viaFinish.Appended!.Select(e => (e.Kind, e.Id, e.AsOf)));
        Assert.Equal(viaApply.Unsigned, viaFinish.Unsigned);
    }
}

/// <summary>
/// v12 rung (h) (#624, design § 13): the check executor and the third state —
/// pure set arithmetic by active SNOMED id, the untested named, the job
/// outcome per-document, and the reply leg skipping what the check refused.
/// </summary>
public class WorkflowV12CheckRuntimeTests
{
    private static ClinicalConcept Coded(string term, string id) => new(term, "disorder", id, true, true, "test");
    private static ClinicalConcept Uncoded(string term) => new(term, "finding", "", false, false, "test");

    [Fact]
    public void ASubset_Passes_AndWordingNeverMatters()
    {
        // Same ids, different surface wording — the comparison is by concept
        // id, insensitive to how either model spelled the term.
        var outcome = Consultologist.Api.Jobs.ConsultCheckExecutor.TermsSubset(
            new[] { Coded("breast cancer", "254837009") },
            new[] { Coded("Malignant neoplasm of breast", "254837009"), Coded("Hypertension", "38341003") });

        Assert.True(outcome.Passed);
        Assert.Null(outcome.Uncovered);
        Assert.Null(outcome.Untested);
    }

    [Fact]
    public void AnUncoveredTerm_Fails_AndIsNamed()
    {
        var outcome = Consultologist.Api.Jobs.ConsultCheckExecutor.TermsSubset(
            new[] { Coded("breast cancer", "254837009"), Coded("diabetes", "44054006") },
            new[] { Coded("breast cancer", "254837009") });

        Assert.False(outcome.Passed);
        Assert.Equal(new[] { "diabetes" }, outcome.Uncovered);
    }

    [Fact]
    public void AnEmptyInputSide_Passes_Vacuously()
    {
        var outcome = Consultologist.Api.Jobs.ConsultCheckExecutor.TermsSubset(
            Array.Empty<ClinicalConcept>(),
            new[] { Coded("anything", "1") });

        Assert.True(outcome.Passed);
    }

    [Fact]
    public void Uncodables_NeverEnterTheTest_AndAreNamedUntested()
    {
        // The ""-id trap: ConceptOutputContract coalesces a null id to the
        // empty string — an active "SNOMED" concept with an empty id is
        // uncoded, and a mutant that lets it into the subset test would fail
        // this pass (the empty of-side id is absent from the in-side).
        var emptyIdButFlagged = new ClinicalConcept("mystery finding", "finding", "", true, true, "test");
        var outcome = Consultologist.Api.Jobs.ConsultCheckExecutor.TermsSubset(
            new[] { Coded("breast cancer", "254837009"), emptyIdButFlagged, Uncoded("family support strong") },
            new[] { Coded("breast cancer", "254837009"), Uncoded("free-text impression") });

        Assert.True(outcome.Passed);
        Assert.Null(outcome.Uncovered);
        Assert.Equal(new[] { "mystery finding", "family support strong", "free-text impression" }, outcome.Untested);
    }

    [Fact]
    public void FinalOutcome_IsPerDocument_AndAllFailedSaysWhy()
    {
        var nodes = new Dictionary<string, Consultologist.Api.Models.ConsultNodeDescriptor>(StringComparer.Ordinal)
        {
            ["assemble-a"] = new("assemble-a", "A", Aggregate: new[] { "node:x" }),
            ["assemble-b"] = new("assemble-b", "B", Aggregate: new[] { "node:x" })
        };
        var deliverables = Consultologist.Api.Jobs.ConsultDeliverables.Resolve(
            new[]
            {
                new Consultologist.Api.Models.ConsultResultDescriptor("a", "assemble-a", "A"),
                new Consultologist.Api.Models.ConsultResultDescriptor("b", "assemble-b", "B")
            }, null, nodes);
        var outputs = new Dictionary<string, Consultologist.Api.Jobs.NodeRunResult>(StringComparer.Ordinal)
        {
            ["assemble-a"] = new("text", null, "i", "o"),
            ["assemble-b"] = new("text", null, "i", "o")
        };
        var none = new Dictionary<string, string>(StringComparer.Ordinal);

        // One failed of two: the siblings produce, the job completes.
        var mixed = Consultologist.Api.Jobs.ConsultDeliverables.FinalOutcome(deliverables, outputs, none,
            new[] { new Consultologist.Api.Models.ConsultFailedDocument("a", "A", "The note does not cover the referral.") });
        Assert.Equal("Completed", mixed.Status);

        // Every deliverable failed: no documents, and the record says why.
        var all = Consultologist.Api.Jobs.ConsultDeliverables.FinalOutcome(deliverables, outputs, none,
            new[]
            {
                new Consultologist.Api.Models.ConsultFailedDocument("a", "A", "The note does not cover the referral."),
                new Consultologist.Api.Models.ConsultFailedDocument("b", "B", "Likewise.")
            });
        Assert.Equal("Failed", all.Status);
        Assert.Equal("The note does not cover the referral.", all.Error);

        // The missing-output rule is untouched.
        var missing = Consultologist.Api.Jobs.ConsultDeliverables.FinalOutcome(
            deliverables, new Dictionary<string, Consultologist.Api.Jobs.NodeRunResult>(StringComparer.Ordinal), none);
        Assert.Equal("Failed", missing.Status);
    }

    [Fact]
    public void TheReplyLeg_SkipsTheFailed_AndStaysStrictForTheProduced()
    {
        var nodes = new Dictionary<string, Consultologist.Api.Models.ConsultNodeDescriptor>(StringComparer.Ordinal)
        {
            ["assemble-a"] = new("assemble-a", "A", Aggregate: new[] { "node:x" }),
            ["assemble-b"] = new("assemble-b", "B", Aggregate: new[] { "node:x" })
        };
        var deliverables = Consultologist.Api.Jobs.ConsultDeliverables.Resolve(
            new[]
            {
                new Consultologist.Api.Models.ConsultResultDescriptor("a", "assemble-a", "A"),
                new Consultologist.Api.Models.ConsultResultDescriptor("b", "assemble-b", "B")
            }, null, nodes);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal) { ["b"] = "Produced text." };

        var documents = Consultologist.Api.Jobs.ConsultDeliverables.ReplyDocumentsFor(
            deliverables, texts, new HashSet<string>(StringComparer.Ordinal) { "a" });
        Assert.Equal("b", Assert.Single(documents).ResultId);

        // A produced deliverable with no recorded text is still a broken
        // invariant, failing loud.
        Assert.Throws<KeyNotFoundException>(() => Consultologist.Api.Jobs.ConsultDeliverables.ReplyDocumentsFor(
            deliverables, new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) { "a" }));
    }
}

/// <summary>
/// v12 rung (h) (#624, design § 13): the record's third state — a refused
/// document is upserted by ResultId with a "failure" history event naming
/// the package's sentence, and a check's verdict lives in its own node
/// slots (never Concepts, which FinalizeJob sheds) and round-trips through
/// ToResponse.
/// </summary>
public class WorkflowV12CheckRecordTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    [Fact]
    public void ARefusedDocument_IsRecorded_UpsertedByResultId_AndAnswered()
    {
        var (entity, state) = Job();

        entity.RecordFailedDocument(new ConsultFailedDocument("a", "Consultation note", "The note does not cover the referral.", new[] { "diabetes" }));
        entity.RecordFailedDocument(new ConsultFailedDocument("b", "Family letter", "Likewise."));

        Assert.Equal(new[] { "a", "b" }, state().FailedDocuments!.Select(d => d.ResultId));

        // A replay re-signals identically — and an upsert never duplicates.
        entity.RecordFailedDocument(new ConsultFailedDocument("a", "Consultation note", "The note does not cover the referral.", new[] { "diabetes" }));
        Assert.Equal(2, state().FailedDocuments!.Count);
        var kept = state().FailedDocuments!.Single(d => d.ResultId == "a");
        Assert.Equal(new[] { "diabetes" }, kept.Uncovered);

        Assert.Contains(state().History, e =>
            e.Kind == "failure" && e.Label == "Document refused by its check: Consultation note — The note does not cover the referral.");

        var response = state().ToResponse();
        Assert.Equal(2, response.FailedDocuments!.Count);
        Assert.Equal("Likewise.", response.FailedDocuments.Single(d => d.ResultId == "b").Reason);
    }

    [Fact]
    public void ACheckVerdict_LivesInItsOwnSlots_AndRoundTrips()
    {
        var (entity, state) = Job();

        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate(
            "coverage", "Coverage check", null, "in-hash", "out-hash", 1, 2,
            Check: new ConsultCheckOutcome(false, new[] { "diabetes" }, new[] { "free-text impression" })));
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate(
            "assemble-note", "Assemble the note", null, "in-hash", "out-hash", 2, 2));

        var node = state().NodeOutputs!["coverage"];
        Assert.False(node.CheckPassed);
        Assert.Equal(new[] { "diabetes" }, node.CheckUncovered);
        Assert.Equal(new[] { "free-text impression" }, node.CheckUntested);
        Assert.Null(node.Concepts);

        var projected = state().ToResponse().NodeOutputs!;
        var check = projected["coverage"].Check!;
        Assert.False(check.Passed);
        Assert.Equal(new[] { "diabetes" }, check.Uncovered);
        Assert.Equal(new[] { "free-text impression" }, check.Untested);

        // The control: a node with no check projects the null of before.
        Assert.Null(projected["assemble-note"].Check);
    }
}

/// <summary>
/// v12 rung (i) (#631, design § 14): the conditional macro — the entry's
/// when speaks the result-level condition grammar, gated at 12, refused
/// below by name; the writer keeps the v11 bytes and never drops a clause.
/// </summary>
public class WorkflowV12ConditionalMacroTests
{
    /// <summary>The § 14 shape: a classifier and a when-gated macro entry.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Gated(
        string when,
        string macroId = "disclaimer",
        string template = "This paragraph is fixed text.")
    {
        var (manifest, files) = V10Fixtures.WithClassifier(from: V12Fixtures.Minimal());
        manifest = manifest with
        {
            Macros = new List<WorkflowMacroSpec> { new(macroId, "Gated paragraph", $"macros/{macroId}.md") },
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new(macroId, When: when) } }
                : r).ToList()
        };
        var all = new Dictionary<string, string>(files) { [$"macros/{macroId}.md"] = template };
        return (manifest, all);
    }

    [Fact]
    public void TheGatedParagraph_Publishes()
    {
        var result = V12Fixtures.Validate(Gated("node:scope == in_scope"));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AMatchCase_TwoArms_Publishes()
    {
        var (manifest, files) = V10Fixtures.WithClassifier(from: V12Fixtures.Minimal());
        manifest = manifest with
        {
            Macros = new List<WorkflowMacroSpec>
            {
                new("arm_in", "In-scope paragraph", "macros/arm_in.md"),
                new("arm_out", "Out-of-scope paragraph", "macros/arm_out.md")
            },
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with
                {
                    Macros = new List<WorkflowResultMacroSpec>
                    {
                        new("arm_in", When: "node:scope == in_scope"),
                        new("arm_out", When: "node:scope == out_of_scope")
                    }
                }
                : r).ToList()
        };
        var all = new Dictionary<string, string>(files)
        {
            ["macros/arm_in.md"] = "The in-scope paragraph.",
            ["macros/arm_out.md"] = "The out-of-scope paragraph."
        };

        var result = WorkflowPackageValidator.Validate(manifest, all, TestOutputContracts.CatalogSchemas);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AtEleven_TheGate_RefusesByName_AndAPlacedGatedEntry_EarnsBothSentences()
    {
        var (manifest, files) = Gated("node:scope == in_scope");
        manifest = manifest with
        {
            SpecVersion = 11,
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new("disclaimer", Before: "node:section-instructions", When: "node:scope == in_scope") } }
                : r).ToList()
        };

        var errors = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors;

        Assert.Contains("Result 'consult' gates macro 'disclaimer' with when, which requires specVersion 12.", errors);
        Assert.Contains("Result 'consult' places macro 'disclaimer', which requires specVersion 12.", errors);
    }

    [Fact]
    public void ABlankClause_IsRefused()
    {
        Assert.Contains(
            "Result 'consult' macro 'disclaimer' condition is blank.",
            V12Fixtures.Validate(Gated("   ")).Errors);
    }

    [Fact]
    public void TheVocabulary_IsTheResultLevelOne_WithTheLongerPrefix()
    {
        // One grammar, two doors: the reused refusals speak the macro prefix.
        var errors = V12Fixtures.Validate(Gated("node:sideways == in_scope")).Errors;
        Assert.Contains(errors, e => e.StartsWith("Result 'consult' macro 'disclaimer' condition reads 'node:sideways', which is not a classifier", StringComparison.Ordinal));

        Assert.Contains(
            "Result 'consult' macro 'disclaimer' condition compares 'node:scope' to 'maybe', which it does not declare (values: in_scope, out_of_scope).",
            V12Fixtures.Validate(Gated("node:scope == maybe")).Errors);

        var undeclared = V12Fixtures.Validate(Gated("include_counseling")).Errors;
        Assert.Contains(undeclared, e => e.StartsWith("Result 'consult' macro 'disclaimer' condition reads undeclared input 'include_counseling'", StringComparison.Ordinal));
    }

    [Fact]
    public void AConditionalSignature_IsRefused()
    {
        // § 14: whether a document is signed must not turn on a classifier's
        // answer — the § 5 never-optional rule's sibling.
        Assert.Contains(
            "Result 'consult' gates macro 'disclaimer' with when, and the macro carries {{profile:signature}}; a conditional signature was rejected (#516) and stays rejected.",
            V12Fixtures.Validate(Gated("node:scope == in_scope", template: "Sincerely,\n\n{{profile:signature}}")).Errors);
    }

    [Fact]
    public void TheWriter_KeepsTheClause_AndTheBareForm()
    {
        // The IsBare edge: a when-only entry must serialize as an object — a
        // bare string would silently drop the clause on republish.
        var (manifest, _) = Gated("node:scope == in_scope");
        var json = WorkflowV10StructureTests.Write(manifest);

        Assert.Contains("{\"id\":\"disclaimer\",\"when\":\"node:scope == in_scope\"}", json);
        Assert.Equal(json, WorkflowV10StructureTests.Write(
            WorkflowPackageManifestJson.Read(json, "v12", WorkflowPackageValidator.AcceptedSpecVersions)));

        // A placed-and-gated entry carries all three keys in declared order.
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new("disclaimer", Before: "node:section-instructions", When: "node:scope == in_scope") } }
                : r).ToList()
        };
        var placed = WorkflowV10StructureTests.Write(manifest);
        Assert.Contains("{\"id\":\"disclaimer\",\"before\":\"node:section-instructions\",\"when\":\"node:scope == in_scope\"}", placed);
        Assert.Equal(placed, WorkflowV10StructureTests.Write(
            WorkflowPackageManifestJson.Read(placed, "v12", WorkflowPackageValidator.AcceptedSpecVersions)));
    }
}

/// <summary>
/// v12 rung (i) (#631, design § 14): the starter's when judgment — the pure
/// seam both evaluation moments call. Held keeps, not-held excludes with the
/// explainer's sentence, absent is never held, the two gates are independent
/// facts, and an emptied list becomes null (the byte control).
/// </summary>
public class WorkflowV12ConditionalMacroStarterTests
{
    private static Consultologist.Api.Workflow.WorkflowResolvedResult Gated(
        params (string MacroId, string When)[] gates)
    {
        var macros = new[] { "opening", "letrozole_counseling", "tamoxifen_counseling" };
        var conditions = gates
            .Select(gate =>
            {
                Assert.True(WorkflowResultConditions.TryParseExpression(gate.When, out var condition, out var error), error);
                return new Consultologist.Api.Workflow.WorkflowResolvedMacroCondition(gate.MacroId, condition!);
            })
            .ToList();
        return new("consult", "assemble-note", "Consultation note",
            Macros: macros, MacroConditions: conditions.Count > 0 ? conditions : null);
    }

    [Fact]
    public void HeldKeeps_NotHeldExcludes_WithTheExplainersSentence()
    {
        var result = Gated(
            ("letrozole_counseling", "node:hormone == letrozole"),
            ("tamoxifen_counseling", "node:hormone == tamoxifen"));
        var classifications = new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "letrozole" };

        var (kept, excluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(
            result, null, classifications);

        Assert.Equal(new[] { "opening", "letrozole_counseling" }, kept);
        var entry = Assert.Single(excluded);
        Assert.Equal(("consult", "tamoxifen_counseling"), (entry.ResultId, entry.MacroId));
        Assert.Equal("needs node:hormone to be 'tamoxifen'; it is 'letrozole'", entry.Reason);
    }

    [Fact]
    public void AnAbsentOperand_IsNeverHeld_AndTheSentenceSaysSo()
    {
        var result = Gated(("letrozole_counseling", "include_counseling"));

        var (kept, excluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(
            result, new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal), null);

        Assert.Equal(new[] { "opening", "tamoxifen_counseling" }, kept);
        Assert.Contains("not supplied", Assert.Single(excluded).Reason);
    }

    [Fact]
    public void TheTwoGates_AreIndependentFacts()
    {
        // A macro out by choice AND out by clause: the when judgment records
        // its fact over the declared list (§ 14.4 — the boundary never sees
        // the request), and the choice filter then drops what it drops.
        var result = Gated(("letrozole_counseling", "node:hormone == letrozole"));
        var classifications = new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "tamoxifen" };

        var (whenKept, excluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(
            result, null, classifications);
        var kept = Consultologist.Api.Jobs.ConsultGenerationJobStarter.FilterMacros(
            whenKept, new Dictionary<string, bool> { ["letrozole_counseling"] = false, ["opening"] = false });

        Assert.Equal("letrozole_counseling", Assert.Single(excluded).MacroId);
        Assert.Equal(new[] { "tamoxifen_counseling" }, kept);

        // The other direction: declined by choice, held by when — no
        // exclusion record; the record of that absence is the choice's.
        var held = new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "letrozole" };
        var (heldKept, heldExcluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(
            result, null, held);
        Assert.Empty(heldExcluded);
        Assert.Contains("letrozole_counseling", heldKept!);
    }

    [Fact]
    public void AnEmptiedList_BecomesNull_TheByteControl()
    {
        var result = new Consultologist.Api.Workflow.WorkflowResolvedResult(
            "consult", "assemble-note", "Consultation note",
            Macros: new[] { "letrozole_counseling" },
            MacroConditions: new[]
            {
                new Consultologist.Api.Workflow.WorkflowResolvedMacroCondition(
                    "letrozole_counseling",
                    Parse("node:hormone == letrozole"))
            });

        var (kept, excluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(
            result, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "tamoxifen" });

        Assert.Null(kept);
        Assert.Single(excluded);
    }

    [Fact]
    public void AResultWithoutGates_PassesThroughUntouched()
    {
        var result = Gated();

        var (kept, excluded) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(result, null, null);

        Assert.Same(result.Macros, kept);
        Assert.Empty(excluded);
    }

    [Fact]
    public async Task TheExclusions_RideTheInitializeSignal_IntoState()
    {
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        var stateProperty = typeof(ConsultGenerationJobEntity).GetProperty(
            "State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", new List<IReadOnlyDictionary<string, string>>(),
            ExcludedMacros: new[]
            {
                new ConsultExcludedMacro("consult", "tamoxifen_counseling", "needs node:hormone to be 'tamoxifen'; it is 'letrozole'")
            }));

        var state = (ConsultGenerationJobState)stateProperty.GetValue(entity)!;
        var recorded = Assert.Single(state.ExcludedMacros!);
        Assert.Equal("tamoxifen_counseling", recorded.MacroId);
    }

    private static WorkflowConditionExpression Parse(string when)
    {
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var condition, out var error), error);
        return condition!;
    }
}

/// <summary>
/// v12 rung (i) (#631, design § 14): the proof end to end — the judged
/// descriptor composes without the excluded paragraph, appended[] and the
/// document bytes cover only what landed, and the aggregator's hash input
/// never learns any of it (§ 7 purity, again).
/// </summary>
public class WorkflowV12ConditionalMacroCompositionTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["opening"] = "Thank you for this referral.",
        ["letrozole_counseling"] = "We discussed letrozole's side effects.",
        ["tamoxifen_counseling"] = "We discussed tamoxifen's side effects."
    };

    private static Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts Facts() =>
        new(new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1", "east.ca.api.consultologist.ai", "Taylor Reyes");

    private static readonly string[] SourceRefs = { "node:plan" };

    private static readonly IReadOnlyList<Consultologist.Api.Jobs.ConsultAggregateRenderer.Part> Parts =
        new Consultologist.Api.Jobs.ConsultAggregateRenderer.Part[]
        {
            new Consultologist.Api.Jobs.ConsultAggregateRenderer.ScalarPart("The plan.")
        };

    private static Consultologist.Api.Workflow.WorkflowResolvedResult MatchCase() =>
        new("consult", "assemble-note", "Consultation note",
            Macros: new[] { "opening", "letrozole_counseling", "tamoxifen_counseling" },
            MacroPlacements: new[]
            {
                new Consultologist.Api.Models.ConsultMacroPlacement("letrozole_counseling", After: "node:plan"),
                new Consultologist.Api.Models.ConsultMacroPlacement("tamoxifen_counseling", After: "node:plan")
            },
            MacroConditions: new[]
            {
                new Consultologist.Api.Workflow.WorkflowResolvedMacroCondition("letrozole_counseling", Parse("node:hormone == letrozole")),
                new Consultologist.Api.Workflow.WorkflowResolvedMacroCondition("tamoxifen_counseling", Parse("node:hormone == tamoxifen"))
            });

    private static WorkflowConditionExpression Parse(string when)
    {
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var condition, out var error), error);
        return condition!;
    }

    private static (string Text, IReadOnlyList<Consultologist.Api.Models.ConsultAppendedEntry>? Appended) ComposeJudged(
        IReadOnlyDictionary<string, string> classifications)
    {
        var result = MatchCase();
        var (kept, _) = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DecideMacroWhens(result, null, classifications);
        var placements = Consultologist.Api.Jobs.ConsultGenerationJobStarter.FilterPlacements(result.MacroPlacements, kept);

        var (text, appended, _) = Consultologist.Api.Jobs.ConsultMacroExpander.Compose(
            SourceRefs, Parts, kept, placements, Texts, NoValues, null, NoValues, Facts());
        return (text, appended);
    }

    [Fact]
    public void TheDocument_CarriesTheHeldArm_AndNotTheOther()
    {
        var (text, appended) = ComposeJudged(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "letrozole" });

        Assert.Equal(
            "The plan.\n\nWe discussed letrozole's side effects.\n\nThank you for this referral.",
            text);
        Assert.DoesNotContain("tamoxifen", text, StringComparison.Ordinal);
        Assert.Equal(new[] { "letrozole_counseling", "opening" }, appended!.Select(e => e.Id));
    }

    [Fact]
    public void TheOtherAnswer_SwapsTheParagraph()
    {
        var (text, appended) = ComposeJudged(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "tamoxifen" });

        Assert.Contains("tamoxifen's side effects", text, StringComparison.Ordinal);
        Assert.DoesNotContain("letrozole", text, StringComparison.Ordinal);
        Assert.Equal(new[] { "tamoxifen_counseling", "opening" }, appended!.Select(e => e.Id));
    }

    [Fact]
    public void AnUnmatchedCase_AppendsNothing_NoFallback()
    {
        // § 14.7 assumption 3: an unmatched match/case appends nothing and
        // the record says why — a default arm is the package's to declare.
        var (text, appended) = ComposeJudged(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "none" });

        Assert.Equal("The plan.\n\nThank you for this referral.", text);
        Assert.Equal(new[] { "opening" }, appended!.Select(e => e.Id));
    }

    [Fact]
    public void TheAggregatorsHashInput_NeverLearnsTheJudgment()
    {
        // § 7, § 14.3: the exclusion changes the composed document, never the
        // aggregator's recorded output — same Render bytes either way.
        var rendered = Consultologist.Api.Jobs.ConsultAggregateRenderer.Render(Parts);
        var hash = Consultologist.Api.Workflow.ConsultGenerationProvenance.Sha256Hex(rendered);

        var (letrozole, _) = ComposeJudged(new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "letrozole" });
        var (none, _) = ComposeJudged(new Dictionary<string, string>(StringComparer.Ordinal) { ["hormone"] = "none" });

        Assert.NotEqual(letrozole, none);
        Assert.Equal(hash, Consultologist.Api.Workflow.ConsultGenerationProvenance.Sha256Hex(
            Consultologist.Api.Jobs.ConsultAggregateRenderer.Render(Parts)));
    }
}

/// <summary>
/// v12 rung (j) (#634, design § 15): the template node's grammar — the kind
/// joins the closed set at 12, reuses the prompts table whole, may declare a
/// contract output, and is refused the two claims that are not its to make.
/// </summary>
public class WorkflowV12TemplateNodeTests
{
    /// <summary>The § 15 shape: a template over a declared input, feeding the deliverable's aggregator.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithTemplate(
        WorkflowNodeSpec? node = null,
        string templateText = "Seen on {{ seen_on }}.",
        List<string>? variables = null)
    {
        var manifest = V12Fixtures.Minimal();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("header", "prompts/header.md", variables ?? new List<string> { "seen_on" })
        };
        var template = node ?? new WorkflowNodeSpec("patient-header", "Patient header",
            Prompt: "header",
            Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
            Kind: WorkflowNodeKinds.Template);
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!) { template };
        nodes = nodes.Select(n => n.Aggregate != null
            ? n with { Aggregate = new List<string>(n.Aggregate) { $"node:{template.Id}" } }
            : n).ToList();
        manifest = manifest with { Prompts = prompts, Nodes = nodes };
        var files = V6Fixtures.Files(manifest);
        files["prompts/header.md"] = templateText;
        return (manifest, files);
    }

    [Fact]
    public void AScalarTemplate_Publishes()
    {
        var result = V12Fixtures.Validate(WithTemplate());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AForEachTemplate_PublishesAsAChainStep()
    {
        var result = V12Fixtures.Validate(WithTemplate(
            new WorkflowNodeSpec("section-header", "Section header",
                Prompt: "header",
                Bindings: new Dictionary<string, WorkflowBindingValue> { ["section_name"] = new("item:name") },
                ForEach: "data:standards",
                Kind: WorkflowNodeKinds.Template),
            templateText: "## {{ section_name }}",
            variables: new List<string> { "section_name" }));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ASchemadTemplate_Publishes_WithFailIfEmpty()
    {
        var result = V12Fixtures.Validate(WithTemplate(
            new WorkflowNodeSpec("fixed-terms", "Fixed terms",
                Prompt: "header",
                Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
                Output: new WorkflowNodeOutputSpec("concept-list", FailIfEmpty: "The fixed term list rendered empty."),
                Kind: WorkflowNodeKinds.Template),
            templateText: "{\"concepts\": []}"));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AtEleven_TheKind_IsRefusedByName()
    {
        var (manifest, files) = WithTemplate();
        manifest = manifest with { SpecVersion = 11 };

        Assert.Contains(
            "Node 'patient-header' declares kind 'template', which requires specVersion 12.",
            WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors);
    }

    [Fact]
    public void Reproducible_IsRefused_TheClaimIsNotItsToMake()
    {
        var (manifest, files) = WithTemplate();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "patient-header" ? n with { Reproducible = true } : n).ToList()
        };

        Assert.Contains(
            "Template 'patient-header' declares reproducible; a template is deterministic by construction, and the claim is not its to make.",
            WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors);
    }

    [Fact]
    public void AClassificationOutput_IsRefused()
    {
        var (manifest, files) = WithTemplate(
            new WorkflowNodeSpec("verdict", "Verdict",
                Prompt: "header",
                Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
                Output: new WorkflowNodeOutputSpec("verdict"),
                Kind: WorkflowNodeKinds.Template),
            templateText: "{\"value\": \"yes\"}");
        manifest = manifest with
        {
            Schemas = new Dictionary<string, string>(manifest.Schemas ?? new Dictionary<string, string>())
            {
                ["verdict"] = "schemas/verdict.json"
            }
        };
        files["schemas/verdict.json"] = TestOutputContracts.CatalogSchemas["classification"];

        Assert.Contains(
            "Template 'verdict' output schema resolves to the classification contract; a classification is answered from a value set, and a template renders, it does not answer.",
            WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas).Errors);
    }

    [Fact]
    public void TheInheritedRules_FireForATemplate()
    {
        // No continue after CheckTemplateNode: the prompt-family rules run.
        var noPrompt = WithTemplate(new WorkflowNodeSpec("patient-header", "Patient header",
            Kind: WorkflowNodeKinds.Template));
        Assert.Contains("Node 'patient-header' declares no prompt.",
            V12Fixtures.Validate(noPrompt).Errors);

        var values = WithTemplate(new WorkflowNodeSpec("patient-header", "Patient header",
            Prompt: "header",
            Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
            Kind: WorkflowNodeKinds.Template,
            Values: new List<string> { "a", "b" }));
        Assert.Contains("Node 'patient-header' declares values but is not a classifier; only kind 'classifier' answers from a value set.",
            V12Fixtures.Validate(values).Errors);

        var mismatch = WithTemplate(new WorkflowNodeSpec("patient-header", "Patient header",
            Prompt: "header",
            Bindings: new Dictionary<string, WorkflowBindingValue> { ["wrong_name"] = new("input:seen_on") },
            Kind: WorkflowNodeKinds.Template));
        Assert.Contains(V12Fixtures.Validate(mismatch).Errors,
            e => e.StartsWith("Node 'patient-header' bindings [wrong_name]", StringComparison.Ordinal));

        var checkMember = WithTemplate(new WorkflowNodeSpec("patient-header", "Patient header",
            Prompt: "header",
            Bindings: new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") },
            Kind: WorkflowNodeKinds.Template,
            FailWith: "never"));
        Assert.Contains("Node 'patient-header' declares failWith but is not a check node; only kind 'check' declares it.",
            V12Fixtures.Validate(checkMember).Errors);
    }

    [Fact]
    public void TheUnknownKindSentence_NamesFourKindsAtTwelve_AndTheOldSetsStand()
    {
        var (manifest, files) = WithTemplate();

        // Ordinal: "Template" is an unknown word, not the template kind.
        var cased = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "patient-header" ? n with { Kind = "Template" } : n).ToList()
        };
        Assert.Contains(
            "Node 'patient-header' declares unknown kind 'Template' (accepted: prompt, classifier, check, template).",
            WorkflowPackageValidator.Validate(cased, files, TestOutputContracts.CatalogSchemas).Errors);

        // The v10/v11 sentence does not move.
        var eleven = V11Fixtures.Minimal() with
        {
            Nodes = V11Fixtures.Minimal().Nodes!.Select((n, i) => i == 0 ? n with { Kind = "router" } : n).ToList()
        };
        Assert.Contains(V11Fixtures.Validate(eleven).Errors,
            e => e.Contains("declares unknown kind 'router' (accepted: prompt, classifier).", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProbeRender_RefusesABrokenTemplate_AtPublish()
    {
        // "What publishes is what runs" — for a template, the strict-mode
        // probe renders the very artifact the run outputs: an undeclared
        // variable refuses at publish, not at run time.
        var errors = V12Fixtures.Validate(WithTemplate(templateText: "Seen on {{ undeclared_var }}.")).Errors;
        Assert.Contains(errors, e => e.Contains("header", StringComparison.Ordinal) && e.Contains("undeclared_var", StringComparison.Ordinal));
    }
}

/// <summary>
/// v12 rung (j) (#634, design § 15): the discriminator on the wire — only a
/// template node earns Template: true; every other kind writes the bytes it
/// always wrote.
/// </summary>
public class WorkflowV12TemplateDescriptorTests
{
    [Fact]
    public void DescribeNode_StampsTheKindMatrix()
    {
        var bindings = new Dictionary<string, WorkflowBindingValue> { ["seen_on"] = new("input:seen_on") };

        var template = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("header", "Header", Prompt: "header", Bindings: bindings, Kind: WorkflowNodeKinds.Template), null);
        Assert.True(template.Template);
        Assert.Null(template.OutputContract);

        var prompt = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("draft", "Draft", Prompt: "draft", Bindings: bindings), null);
        Assert.Null(prompt.Template);

        var classifier = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("scope", "Scope", Prompt: "classify", Bindings: bindings,
                Kind: WorkflowNodeKinds.Classifier, Values: new List<string> { "a", "b" }), null);
        Assert.Null(classifier.Template);

        var check = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("coverage", "Coverage", Kind: WorkflowNodeKinds.Check,
                Op: WorkflowCheckOps.TermsSubset, Of: "node:a", In: "node:b", FailWith: "no"), null);
        Assert.Null(check.Template);

        // Ordinal at every seam: "Template" is not the template kind here
        // either — the validator's set already refuses it, and the stamp
        // must agree.
        var cased = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("cased", "Cased", Prompt: "header", Bindings: bindings, Kind: "Template"), null);
        Assert.Null(cased.Template);

        // A schema'd template carries its contract like a prompt node would.
        var schemad = Consultologist.Api.Jobs.ConsultGenerationJobStarter.DescribeNode(
            new WorkflowNodeSpec("fixed-terms", "Fixed terms", Prompt: "header", Bindings: bindings,
                Output: new WorkflowNodeOutputSpec("concept-list"), Kind: WorkflowNodeKinds.Template),
            new Dictionary<string, string> { ["concept-list"] = "concept-list" });
        Assert.True(schemad.Template);
        Assert.Equal("concept-list", schemad.OutputContract);
    }
}

/// <summary>
/// v12 rung (j) (#634, design § 15): the template's whole answer at the pure
/// seam — one hash twice, tokens null, contract application against the
/// rendered bytes, and the deterministic fail-fast wrap.
/// </summary>
public class WorkflowV12TemplateResultTests
{
    [Fact]
    public void TheRender_IsTheAnswer_OneHashTwice_NoTokens()
    {
        var result = Consultologist.Api.Jobs.RunPromptNodeActivity.TemplateResult(
            "Seen on 2026-08-10.", null, null, "patient-header");

        Assert.Equal("Seen on 2026-08-10.", result.RawOutput);
        Assert.Null(result.Concepts);
        Assert.Equal("1c3ee9bd2152fab39b31aa9188bc33ad35bda7e07e793c415e9210e8bd56cb24", result.InputHash);
        Assert.Equal(result.InputHash, result.OutputHash);
        Assert.Equal(Consultologist.Api.Workflow.ConsultGenerationProvenance.NodeHashVersion, result.HashVersion);
        Assert.Null(result.Classification);
        // Not recorded, never zero: no model ran.
        Assert.Null(result.Tokens);
    }

    [Fact]
    public void ASchemadRender_GoesThroughTheContract_AndHonorsTheSource()
    {
        const string json = """{"concepts": [{"term": "breast cancer", "type": "disorder", "id": "254837009", "isSnomedConcept": true, "isActive": true}]}""";

        var result = Consultologist.Api.Jobs.RunPromptNodeActivity.TemplateResult(
            json, Consultologist.Api.Agents.OutputContracts.ConceptList, "fixed-terms", "fixed-terms-node");

        var concept = Assert.Single(result.Concepts!);
        Assert.Equal(("breast cancer", "254837009", "fixed-terms"), (concept.Term, concept.Id, concept.Source));
        Assert.Equal(result.InputHash, result.OutputHash);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public void AMalformedSchemadRender_FailsFast_NeverRetries()
    {
        // The same bytes re-render, so the retryable contract exception is
        // re-thrown as the renderer's own fail-fast type — excluded from the
        // activity retry policy by construction.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Consultologist.Api.Jobs.RunPromptNodeActivity.TemplateResult(
                "not json at all", Consultologist.Api.Agents.OutputContracts.ConceptList, null, "fixed-terms-node"));

        Assert.StartsWith("Template 'fixed-terms-node' rendered output that is not valid concept-list JSON", exception.Message);
        Assert.IsType<Consultologist.Api.Workflow.ConceptOutputContractException>(exception.InnerException);
    }
}

/// <summary>
/// v12 rung (j) (#634, design § 15): the proof at the surrounding seams — a
/// template descriptor schedules and depends exactly as a prompt node's, and
/// its equal-hash, token-less result rides the completion updates untouched.
/// </summary>
public class WorkflowV12TemplateSchedulingTests
{
    private static readonly Consultologist.Api.Models.ConsultNodeDescriptor Header = new(
        "patient-header", "Patient header", "header",
        Bindings: new Dictionary<string, Consultologist.Api.Models.ConsultNodeBindingDescriptor>
        {
            ["seen_on"] = new("input:seen_on")
        },
        Template: true);

    private static readonly Consultologist.Api.Models.ConsultNodeDescriptor Draft = new(
        "section-draft", "Drafting section", "draft-section",
        Bindings: new Dictionary<string, Consultologist.Api.Models.ConsultNodeBindingDescriptor>
        {
            ["header"] = new("node:patient-header")
        },
        ForEach: "input:sections");

    [Fact]
    public void ATemplateUpstream_GatesItsReaders_LikeAnyNode()
    {
        var nodes = new[] { Header, Draft }.ToDictionary(n => n.Id, StringComparer.Ordinal);

        Assert.Equal(new[] { "patient-header" }, Consultologist.Api.Jobs.ConsultNodeScheduler.NodeDependencies(Draft));
        Assert.Empty(Consultologist.Api.Jobs.ConsultNodeScheduler.NodeDependencies(Header));

        Assert.False(Consultologist.Api.Jobs.ConsultNodeScheduler.InstanceReady(
            Draft, "hpi", nodes, new Dictionary<string, Consultologist.Api.Jobs.NodeRunResult>(StringComparer.Ordinal)));
        Assert.True(Consultologist.Api.Jobs.ConsultNodeScheduler.InstanceReady(
            Draft, "hpi", nodes, new Dictionary<string, Consultologist.Api.Jobs.NodeRunResult>(StringComparer.Ordinal)
            {
                ["patient-header"] = new("Seen on 2026-08-10.", null, "h", "h")
            }));
    }

    [Fact]
    public void TheCompletionUpdates_CarryTheEqualHashes_AndNoTokens()
    {
        var result = Consultologist.Api.Jobs.RunPromptNodeActivity.TemplateResult(
            "Seen on 2026-08-10.", null, null, "patient-header");

        var update = Consultologist.Api.Jobs.ConsultGenerationOrchestrator.NodeUpdateFrom(Header, result, 1, 3);
        Assert.Equal(update.InputHash, update.OutputHash);
        Assert.Equal("1c3ee9bd2152fab39b31aa9188bc33ad35bda7e07e793c415e9210e8bd56cb24", update.OutputHash);
        Assert.Null(update.Tokens);
        Assert.Null(update.Concepts);
        Assert.Null(update.Classification);

        var itemUpdate = Consultologist.Api.Jobs.ConsultGenerationOrchestrator.ItemUpdateFrom(
            Header, "hpi", "History", result, 1, 2);
        Assert.Equal(itemUpdate.InputHash, itemUpdate.OutputHash);
        Assert.Null(itemUpdate.Tokens);
    }
}

/// <summary>
/// #639: the run's trace — the activity's clock rides every recorded
/// result into the node and item rows; rows no activity ran for record
/// none, not recorded, never zero.
/// </summary>
public class RunTraceTimingTests
{
    [Fact]
    public void TheTemplateResult_CarriesTheClock_WhenTheActivityWrapsIt()
    {
        // TemplateResult itself is pure (no clock); the activity stamps both
        // branches on return — the wrap the early return applies.
        var bare = Consultologist.Api.Jobs.RunPromptNodeActivity.TemplateResult("Rendered.", null, null, "header");
        Assert.Null(bare.StartedAtUtc);
        Assert.Null(bare.DurationMs);

        var started = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var wrapped = bare with { StartedAtUtc = started, DurationMs = 42 };
        Assert.Equal((started, 42L), (wrapped.StartedAtUtc!.Value, wrapped.DurationMs!.Value));
    }

    [Fact]
    public void TheUpdates_CarryTheClock_ToNodeAndItemRows()
    {
        var node = new Consultologist.Api.Models.ConsultNodeDescriptor("draft", "Drafting", "draft-prompt");
        var started = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var result = new Consultologist.Api.Jobs.NodeRunResult("x", null, "i", "o", 5, null, null, started, 1234);

        var update = Consultologist.Api.Jobs.ConsultGenerationOrchestrator.NodeUpdateFrom(node, result, 1, 2);
        Assert.Equal((started, 1234L), (update.NodeStartedAtUtc!.Value, update.DurationMs!.Value));

        var itemUpdate = Consultologist.Api.Jobs.ConsultGenerationOrchestrator.ItemUpdateFrom(node, "hpi", "History", result, 1, 2);
        Assert.Equal((started, 1234L), (itemUpdate.NodeStartedAtUtc!.Value, itemUpdate.DurationMs!.Value));
    }

    [Fact]
    public async Task TheRows_RecordTheClock_AndRowsWithoutOneRecordNone()
    {
        var entity = new Consultologist.Api.Jobs.ConsultGenerationJobEntity(
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IConsultGenerationJobIndexStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobOutputsBlobStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobInputsBlobStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IAccountUsageStore>());
        var stateProperty = typeof(Consultologist.Api.Jobs.ConsultGenerationJobEntity).GetProperty(
            "State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        stateProperty.SetValue(entity, Consultologist.Api.Jobs.ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "a", ["name"] = "A" }
        }));
        var started = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "draft", "Drafting", null, "i", "o", 1, 3, NodeStartedAtUtc: started, DurationMs: 1234));
        entity.MarkNodeItemCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeItemUpdate(
            "section", "Sectioning", "hpi", "History", null, "i", "o", 1, 2, NodeStartedAtUtc: started, DurationMs: 77));
        // The aggregate-style row: no activity, no clock — the roll-up shape.
        entity.MarkNodeCompleted(new Consultologist.Api.Jobs.ConsultGenerationNodeUpdate(
            "assemble", "Assembling", null, "i", "o", 2, 3));
        await Task.CompletedTask;

        var state = (Consultologist.Api.Jobs.ConsultGenerationJobState)stateProperty.GetValue(entity)!;
        Assert.Equal((started, 1234L), (state.NodeOutputs!["draft"].StartedAtUtc!.Value, state.NodeOutputs["draft"].DurationMs!.Value));
        Assert.Equal(77L, state.NodeOutputs["section:hpi"].DurationMs);
        Assert.Null(state.NodeOutputs["assemble"].StartedAtUtc);
        Assert.Null(state.NodeOutputs["assemble"].DurationMs);

        var projected = state.ToResponse().NodeOutputs!;
        Assert.Equal(1234L, projected["draft"].DurationMs);
        Assert.Equal(started, projected["draft"].StartedAtUtc);
        Assert.Null(projected["assemble"].DurationMs);
    }
}

/// <summary>
/// #639: the stack is caught where it falls — frames and the exception
/// type, never the message beyond what Error already carries; recorded on
/// the failed node's row, the job, the deciding stage and the failed
/// section alike.
/// </summary>
public class FailureStackTests
{
    private static (Consultologist.Api.Jobs.ConsultGenerationJobEntity Entity, Func<Consultologist.Api.Jobs.ConsultGenerationJobState> State) Job()
    {
        var entity = new Consultologist.Api.Jobs.ConsultGenerationJobEntity(
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IConsultGenerationJobIndexStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobOutputsBlobStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IJobInputsBlobStore>(),
            NSubstitute.Substitute.For<Consultologist.Api.Jobs.IAccountUsageStore>());
        var stateProperty = typeof(Consultologist.Api.Jobs.ConsultGenerationJobEntity).GetProperty(
            "State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        stateProperty.SetValue(entity, Consultologist.Api.Jobs.ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "consult:s0", ["name"] = "Section 0" }
        }));
        return (entity, () => (Consultologist.Api.Jobs.ConsultGenerationJobState)stateProperty.GetValue(entity)!);
    }

    [Fact]
    public void FailureFacts_PreferDurableDetails_AndNeverTheMessage()
    {
        // The plain-exception arm: type + frames, and the message is not in
        // the captured pair at all.
        Exception thrown;
        try { throw new InvalidOperationException("patient text could be here"); }
        catch (Exception ex) { thrown = ex; }

        var (type, stack) = Consultologist.Api.Jobs.ConsultGenerationOrchestrator.FailureFactsOf(thrown);
        Assert.Equal(typeof(InvalidOperationException).FullName, type);
        Assert.Contains("FailureFacts_PreferDurableDetails", stack!);
        Assert.DoesNotContain("patient text could be here", stack);
    }

    [Fact]
    public async Task TheFailedNodeRow_CarriesItsStack_AndTheJobInheritsIt()
    {
        var (entity, state) = Job();

        await entity.MarkNodeFailed(new Consultologist.Api.Jobs.ConsultGenerationNodeFailure(
            "draft", "Drafting", "draft-failed", "Drafting failed: boom.",
            Array.Empty<Consultologist.Api.Models.ConsultItemStepDescriptor>(),
            "System.InvalidOperationException",
            "   at Engine.Run()"));

        var node = state().NodeOutputs!["draft"];
        Assert.Equal(("System.InvalidOperationException", "   at Engine.Run()"), (node.ErrorType, node.ErrorStack));
        Assert.Equal("   at Engine.Run()", state().FailureStack);

        var projected = state().ToResponse();
        Assert.Equal("   at Engine.Run()", projected.FailureStack);
        Assert.Equal("System.InvalidOperationException", projected.NodeOutputs!["draft"].ErrorType);
    }

    [Fact]
    public async Task TheFailedSection_CarriesItsStack_IntoTheResponseMap()
    {
        var (entity, state) = Job();

        await entity.FailBlock(new Consultologist.Api.Models.BlockGenerationResult(
            "consult:s0", "Section 0", false, null, "Section 0 failed: boom.",
            "System.TimeoutException\n   at Agent.SendAsync()"));

        var response = state().ToResponse();
        Assert.Equal("System.TimeoutException\n   at Agent.SendAsync()",
            Assert.Contains("consult:s0", response.FailedBlockStacks!));
        // A failIfEmpty-style failure has no exception — no stack appears.
        await entity.FailBlock(new Consultologist.Api.Models.BlockGenerationResult(
            "consult:s1", "Section 1", false, null, "The list rendered empty."));
        Assert.DoesNotContain("consult:s1", state().ToResponse().FailedBlockStacks!);
    }

    [Fact]
    public async Task TheTerminalFailure_AndTheDecidingFailure_CarryTheirStacks()
    {
        var (entity, state) = Job();
        await entity.FinalizeJob(new Consultologist.Api.Jobs.ConsultGenerationJobFinalize(
            "Failed", "boom", FailureStack: "System.Exception\n   at Orchestrator.Run()"));
        Assert.Equal("System.Exception\n   at Orchestrator.Run()", state().ToResponse().FailureStack);

        var (second, secondState) = Job();
        await second.RecordDecisionFailure(new Consultologist.Api.Jobs.ConsultGenerationDecisionFailure(
            Consultologist.Api.Jobs.ConsultGenerationDecisionFailureKinds.CouldNotDecide,
            "Classifier 'scope' failed (TaskFailedException).",
            null,
            null,
            FailureStack: "System.Exception\n   at Classifier.Run()"));
        Assert.Equal("System.Exception\n   at Classifier.Run()", secondState().FailureStack);
    }
}
