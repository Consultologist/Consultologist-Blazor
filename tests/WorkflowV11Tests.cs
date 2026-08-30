using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// v11 step (a) (#563, package-format-v11-design.md § 4–§ 6, § 8): the
/// validator accepts 11 and knows its three declaration shapes — macros with
/// their placeholder grammar, a deliverable's macro list and signature flag,
/// and reproducible on nodes — refusing each below 11 by name. Nothing runs
/// yet.
/// </summary>
public static class V11Fixtures
{
    public static WorkflowPackageManifest Minimal() => WithResultsList(V10Fixtures.Minimal() with { SpecVersion = 11 });

    /// <summary>
    /// The lineage fixtures use the v6 string-result sugar; a results list is
    /// where macros and the signature live, so materialise the one-entry form.
    /// </summary>
    public static WorkflowPackageManifest WithResultsList(WorkflowPackageManifest manifest) =>
        manifest.Results != null ? manifest : manifest with
        {
            Result = null,
            Results = new List<WorkflowResultSpec> { new("consult", manifest.Result!, "Consultation note") }
        };

    /// <summary>A macro over the fixture's declared inputs, named by the deliverable.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithMacro(
        string template = "This disclaimer is fixed text.",
        WorkflowPackageManifest? from = null,
        string macroId = "disclaimer")
    {
        var manifest = from ?? Minimal();
        manifest = manifest with
        {
            Macros = new List<WorkflowMacroSpec> { new(macroId, "Standing disclaimer", $"macros/{macroId}.md") },
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { macroId } } : r).ToList()
        };
        var files = V6Fixtures.Files(manifest);
        files[$"macros/{macroId}.md"] = template;
        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV11GateTests
{
    [Fact]
    public void TheValidatorAccepts11_ButTheStoreDoesNotRunItYet()
    {
        // (a) #563: publishable before runnable, as v8 and v10 shipped.
        Assert.Contains(11, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(11, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
        Assert.Equal(10, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions.Max());

        var result = V11Fixtures.Validate(V11Fixtures.Minimal());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TwelveIsRefused_NamingTheSet()
    {
        Assert.Contains(V11Fixtures.Validate(V11Fixtures.Minimal() with { SpecVersion = 12 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7, 8, 9, 10 or 11"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    public void BelowEleven_TheThreeKeys_AreRefusedByName(int specVersion)
    {
        var manifest = V11Fixtures.WithResultsList(V10Fixtures.Minimal()) with
        {
            SpecVersion = specVersion,
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") }
        };
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<string> { "closing" }, Signature = true }
                : r).ToList(),
            Nodes = manifest.Nodes!.Select((n, i) => i == 0 ? n with { Reproducible = true } : n).ToList()
        };

        var errors = V11Fixtures.Validate(manifest).Errors;

        Assert.Contains("macros requires specVersion 11.", errors);
        Assert.Contains(errors, e => e.EndsWith("declares macros, which requires specVersion 11."));
        Assert.Contains(errors, e => e.EndsWith("declares signature, which requires specVersion 11."));
        Assert.Contains(errors, e => e.EndsWith("declares reproducible, which requires specVersion 11."));
    }

    [Fact]
    public void AV10Manifest_WritesTheBytesItAlwaysWrote()
    {
        // § 7's control: a package using none of v11 is byte-identical.
        var json = WorkflowV10StructureTests.Write(V10Fixtures.Minimal());

        Assert.DoesNotContain("\"macros\"", json);
        Assert.DoesNotContain("\"signature\"", json);
        Assert.DoesNotContain("\"reproducible\"", json);
        Assert.Equal(json, WorkflowV10StructureTests.Write(
            WorkflowPackageManifestJson.Read(json, "v10", WorkflowPackageValidator.AcceptedSpecVersions)));
    }
}

public class WorkflowV11MacroTests
{
    private static IEnumerable<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => V11Fixtures.Validate(bundle).Errors;

    [Fact]
    public void AMacro_Publishes_AndEachSenseResolves()
    {
        // A fixed phrase, a template over the declaration, and the run facts —
        // the three senses under one grammar (§ 4).
        var (manifest, files) = V11Fixtures.WithMacro(
            "Fixed text. Stay: {{input:length_of_stay}}. Guide: {{data:intro}}. On {{run:date}} by {{profile:name}} ({{run:package}}, {{run:job}}, {{run:host}}).");
        manifest = manifest with { Data = new Dictionary<string, string>(manifest.Data!) { ["intro"] = "data/intro.md" } };
        files["data/intro.md"] = "A scalar the macro reads.";

        var result = V11Fixtures.Validate((manifest, files));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Macro"));
    }

    [Fact]
    public void AClassificationPlaceholder_ResolvesAgainstAClassifier()
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var (withMacro, macroFiles) = V11Fixtures.WithMacro(
            "Scope: {{classification:scope}}.", from: V11Fixtures.WithResultsList(manifest with { SpecVersion = 11 }));
        foreach (var (k, v) in files) macroFiles.TryAdd(k, v);

        Assert.DoesNotContain(Errors((withMacro, macroFiles)), e => e.Contains("Macro"));
    }

    [Theory]
    [InlineData("{{input:nope}}", "input:nope")]
    [InlineData("{{data:missing}}", "data:missing")]
    [InlineData("{{classification:assemble}}", "classification:assemble")]
    [InlineData("{{run:time}}", "run:time")]
    [InlineData("{{profile:signature}}", "profile:signature")]
    [InlineData("{{no_namespace}}", "no_namespace")]
    [InlineData("{{sql:drop}}", "sql:drop")]
    public void APlaceholderThatDoesNotResolve_IsRefusedNamingTheToken(string template, string token)
    {
        Assert.Contains($"Macro 'disclaimer' placeholder '{{{{{token}}}}}' does not resolve.",
            Errors(V11Fixtures.WithMacro($"Text {template} text.")));
    }

    [Fact]
    public void AnOptionalInputPlaceholder_Warns_AndStillPublishes()
    {
        var result = V11Fixtures.Validate(V11Fixtures.WithMacro("Stay: {{input:length_of_stay}}."));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Contains("Macro 'disclaimer' references optional input 'length_of_stay', which renders as empty when not supplied.", result.Warnings);
    }

    [Fact]
    public void TheFileRules_MissingEmptyIdAndLabel()
    {
        var (manifest, files) = V11Fixtures.WithMacro();
        files.Remove("macros/disclaimer.md");
        Assert.Contains("Macro 'disclaimer' file 'macros/disclaimer.md' is missing from the package.", Errors((manifest, files)));

        var (m2, f2) = V11Fixtures.WithMacro("   ");
        Assert.Contains("Macro 'disclaimer' file 'macros/disclaimer.md' is empty.", Errors((m2, f2)));

        var (m3, f3) = V11Fixtures.WithMacro(macroId: "Bad-Id");
        Assert.Contains("Macro id 'Bad-Id' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).", Errors((m3, f3)));

        var (m4, f4) = V11Fixtures.WithMacro();
        m4 = m4 with { Macros = new List<WorkflowMacroSpec> { m4.Macros![0] with { Label = " " } } };
        Assert.Contains("Macro 'disclaimer' has no label.", Errors((m4, f4)));
    }

    [Fact]
    public void TheReferenceRules_OrphanUndeclaredAndDuplicate()
    {
        // An orphan macro.
        var orphan = V11Fixtures.Minimal() with
        {
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") }
        };
        var files = V6Fixtures.Files(orphan);
        files["macros/closing.md"] = "Closing words.";
        Assert.Contains("Macro 'closing' is not referenced by any result.", Errors((orphan, files)));

        // An undeclared reference.
        var undeclared = V11Fixtures.Minimal();
        undeclared = undeclared with
        {
            Results = undeclared.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { "ghost" } } : r).ToList()
        };
        Assert.Contains("Result 'consult' references undeclared macro 'ghost'.", V11Fixtures.Validate(undeclared).Errors);

        // The same macro twice on one deliverable.
        var (twice, twiceFiles) = V11Fixtures.WithMacro();
        twice = twice with
        {
            Results = twice.Results!.Select((r, i) => i == 0 ? r with { Macros = new List<string> { "disclaimer", "disclaimer" } } : r).ToList()
        };
        Assert.Contains("Result 'consult' lists macro 'disclaimer' more than once.", Errors((twice, twiceFiles)));
    }

    [Fact]
    public void ASignedDeliverable_AndAReproducibleNode_Publish()
    {
        var manifest = V11Fixtures.Minimal();
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { Signature = true } : r).ToList(),
            Nodes = manifest.Nodes!.Select((n, i) => i == 0 ? n with { Reproducible = true } : n).ToList()
        };

        var result = V11Fixtures.Validate(manifest);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

/// <summary>
/// v11 rung (b) (#513, § 4 append rule): the engine's expansion — substitution
/// over the closed namespaces, no model, no Scriban, no recursion — and the
/// append after the aggregated sections: declared order, blank-line separated,
/// no invented heading. The control: no macros, the text untouched, nothing
/// recorded.
/// </summary>
public class ConsultMacroExpanderTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static ConsultMacroExpander.RunFacts Facts(string? apiHost = "east.ca.api.consultologist.ai", string? profileName = "Taylor Reyes") =>
        new(new DateTime(2026, 8, 30, 21, 5, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1", apiHost, profileName);

    private static string Expand(
        string template,
        Dictionary<string, string>? inputs = null,
        Dictionary<string, string>? data = null,
        Dictionary<string, string>? classifications = null,
        ConsultMacroExpander.RunFacts? facts = null)
        => ConsultMacroExpander.Expand(template, inputs ?? NoValues, data, classifications ?? NoValues, facts ?? Facts());

    [Fact]
    public void FixedText_ExpandsVerbatim()
    {
        // The SmartPhrase sense: a macro file with no placeholders is the
        // file itself — its own markdown, no heading invented for it.
        Assert.Equal("**Disclaimer.** This text is fixed.", Expand("**Disclaimer.** This text is fixed."));
    }

    [Fact]
    public void EachSenseResolves()
    {
        var expanded = Expand(
            "Stay {{input:length_of_stay}}; guide {{ data:intro }}; scope {{classification:scope}}; on {{run:date}} job {{run:job}} of {{run:package}} at {{run:host}} by {{profile:name}}.",
            inputs: new(StringComparer.Ordinal) { ["length_of_stay"] = "3 days" },
            data: new(StringComparer.Ordinal) { ["intro"] = "the guide" },
            classifications: new(StringComparer.Ordinal) { ["scope"] = "in_scope" });

        Assert.Equal(
            "Stay 3 days; guide the guide; scope in_scope; on 2026-08-30 job 01234567 of general@v2026.09.1 at east.ca.api.consultologist.ai by Taylor Reyes.",
            expanded);
    }

    [Fact]
    public void AnAbsentOptionalInput_RendersEmpty()
    {
        // The effective map carries every declared id; an absent optional is
        // already the empty string (§ 4) — nothing special happens here.
        Assert.Equal("Stay .", Expand("Stay {{input:length_of_stay}}.", inputs: new(StringComparer.Ordinal) { ["length_of_stay"] = "" }));
    }

    [Fact]
    public void HostAndProfileName_AreDataAbsence_NotGrammarFailure()
    {
        // A deployment naming no host, an account with no display name: the
        // token resolves — to nothing — mirroring the optional-input rule.
        Assert.Equal("At  by .", Expand("At {{run:host}} by {{profile:name}}.", facts: Facts(apiHost: null, profileName: null)));
    }

    [Fact]
    public void AShortJobId_RendersWhole()
    {
        Assert.Equal("job-1", Expand("{{run:job}}", facts: Facts() with { JobId = "job-1" }));
    }

    [Theory]
    [InlineData("{{input:nope}}", "input:nope")]
    [InlineData("{{data:intro}}", "data:intro")]
    [InlineData("{{classification:scope}}", "classification:scope")]
    [InlineData("{{run:time}}", "run:time")]
    [InlineData("{{profile:signature}}", "profile:signature")]
    [InlineData("{{no_namespace}}", "no_namespace")]
    [InlineData("{{sql:drop}}", "sql:drop")]
    public void ATokenThatDoesNotResolve_FailsLoud_NamingIt(string template, string token)
    {
        // Unreachable for a validated package — the publish scanner shares
        // this grammar — so a miss is a broken snapshot, and a loud failure
        // beats a silently wrong clinical document.
        var exception = Assert.Throws<InvalidOperationException>(() => Expand(template));
        Assert.Equal($"Macro placeholder '{{{{{token}}}}}' does not resolve.", exception.Message);
    }

    [Fact]
    public void NoMacros_IsTheControl_TextUntouchedAndNothingRecorded()
    {
        var (text, appended) = ConsultMacroExpander.Append("## A\n\nBody", null, null, NoValues, null, NoValues, Facts());
        Assert.Equal("## A\n\nBody", text);
        Assert.Null(appended);

        // An empty-but-present list is no append either.
        (text, appended) = ConsultMacroExpander.Append("## A\n\nBody", Array.Empty<string>(), null, NoValues, null, NoValues, Facts());
        Assert.Equal("## A\n\nBody", text);
        Assert.Null(appended);
    }

    [Fact]
    public void MacrosAppend_InDeclaredOrder_BlankLineSeparated_NoInventedHeading()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["disclaimer"] = "This disclaimer is fixed.",
            ["closing"] = "Signed on {{run:date}}."
        };

        var (text, appended) = ConsultMacroExpander.Append(
            "## History\n\nUnremarkable.", new[] { "disclaimer", "closing" }, texts, NoValues, null, NoValues, Facts());

        Assert.Equal("## History\n\nUnremarkable.\n\nThis disclaimer is fixed.\n\nSigned on 2026-08-30.", text);
        Assert.Equal(new[] { ("macro", "disclaimer"), ("macro", "closing") }, appended!.Select(entry => (entry.Kind, entry.Id)));
    }

    [Fact]
    public void AMissingTemplate_FailsLoud()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ConsultMacroExpander.Append(
            "Body", new[] { "ghost" }, new Dictionary<string, string>(StringComparer.Ordinal), NoValues, null, NoValues, Facts()));
        Assert.Equal("Macro 'ghost' has no snapshotted template.", exception.Message);
    }

    [Fact]
    public void TheDeliverableTable_CarriesTheMacroIds()
    {
        var deliverable = ConsultDeliverables.Resolve(
            new[] { new ConsultResultDescriptor("consult", "assemble-note", "Consultation note", new[] { "closing" }) },
            null,
            new Dictionary<string, ConsultNodeDescriptor>(StringComparer.Ordinal)).Single();

        Assert.Equal(new[] { "closing" }, deliverable.MacroIds);
    }
}

/// <summary>
/// v11 rung (b) (#513, § 7): the appended text is inside Text before the
/// entity stores it, so documentHash and the workflow output hash cover it
/// with no definition moving; appended[] names what was appended, in applied
/// order — and its absence stores the bytes of before.
/// </summary>
public class WorkflowV11AppendedRecordTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static async Task CompleteAsync(ConsultGenerationJobEntity entity, string text, IReadOnlyList<ConsultAppendedEntry>? appended)
    {
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", text, 0, appended));
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
    }

    [Fact]
    public async Task TheAppendedText_IsInsideBothHashes_AndAppendedNamesIt()
    {
        var (entity, state) = Job();
        const string appendedText = "Consultation note\n\nThis disclaimer is fixed.";

        await CompleteAsync(entity, appendedText, new[] { new ConsultAppendedEntry(ConsultAppendedKinds.Macro, "disclaimer") });

        var document = state().AssembledDocuments!.Single();
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(appendedText), document.DocumentHash);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeResultSetHash(new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = appendedText }),
            state().WorkflowOutputHash);
        var entry = Assert.Single(document.Appended!);
        Assert.Equal(("macro", "disclaimer"), (entry.Kind, entry.Id));
        var response = Assert.Single(state().ToResponse().AssembledDocuments!);
        Assert.Equal("disclaimer", Assert.Single(response.Appended!).Id);
    }

    [Fact]
    public async Task TheControl_NoAppends_StoresNullAndTheBytesOfBefore()
    {
        var (entity, state) = Job();

        await CompleteAsync(entity, "Consultation note", null);

        var document = state().AssembledDocuments!.Single();
        Assert.Null(document.Appended);
        Assert.Null(Assert.Single(state().ToResponse().AssembledDocuments!).Appended);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Consultation note"), document.DocumentHash);

        // An empty list is no append either — stored as the null of before.
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0, Array.Empty<ConsultAppendedEntry>()));
        Assert.Null(state().AssembledDocuments!.Single().Appended);
    }
}

/// <summary>
/// v11 rung (c) (#516, § 5): the signature block, strictly last — applied to
/// the expander's output, so it follows every macro; verbatim, blank-line
/// separated, no invented heading, named in appended[] with its as-of date.
/// Not signed is the § 7 control (same references); signed with no chosen
/// block is the unsigned-although-requested state, never a hold or refusal.
/// </summary>
public class ConsultSignatureAppendTests
{
    private static readonly ConsultSignatureSnapshot Snapshot =
        new("clinic-letters", "Taylor Reyes, MD\nDept. of Medicine", "2026-08-30");

    [Fact]
    public void NotSigned_IsTheControl_SameReferences()
    {
        var text = "## History\n\nUnremarkable.";
        var appended = new[] { new ConsultAppendedEntry(ConsultAppendedKinds.Macro, "disclaimer") };

        var (resultText, resultAppended, unsigned) = ConsultSignatureAppend.Apply(text, appended, false, Snapshot);

        Assert.Same(text, resultText);
        Assert.Same(appended, resultAppended);
        Assert.Null(unsigned);
    }

    [Fact]
    public void Signed_AppendsLast_AfterEveryMacro_AndNamesTheAsOf()
    {
        // The expander's output is the input: two macros already appended.
        var expanded = "## History\n\nUnremarkable.\n\nDisclaimer.\n\nClosing.";
        var appended = new[]
        {
            new ConsultAppendedEntry(ConsultAppendedKinds.Macro, "disclaimer"),
            new ConsultAppendedEntry(ConsultAppendedKinds.Macro, "closing")
        };

        var (text, entries, unsigned) = ConsultSignatureAppend.Apply(expanded, appended, true, Snapshot);

        Assert.Equal(expanded + "\n\nTaylor Reyes, MD\nDept. of Medicine", text);
        Assert.Equal(
            new[] { ("macro", "disclaimer", (string?)null), ("macro", "closing", null), ("signature", "clinic-letters", "2026-08-30") },
            entries!.Select(entry => (entry.Kind, entry.Id, entry.AsOf)));
        Assert.Null(unsigned);
    }

    [Fact]
    public void Signed_WithNoMacrosBefore_StartsTheList()
    {
        var (text, entries, unsigned) = ConsultSignatureAppend.Apply("Body.", null, true, Snapshot);

        Assert.Equal("Body.\n\nTaylor Reyes, MD\nDept. of Medicine", text);
        var entry = Assert.Single(entries!);
        Assert.Equal(("signature", "clinic-letters", "2026-08-30"), (entry.Kind, entry.Id, entry.AsOf));
        Assert.Null(unsigned);
    }

    [Fact]
    public void SignedWithNoChosenBlock_IsUnsignedByName_NothingChanged()
    {
        var text = "Body.";
        var appended = new[] { new ConsultAppendedEntry(ConsultAppendedKinds.Macro, "disclaimer") };

        var (resultText, resultAppended, unsigned) = ConsultSignatureAppend.Apply(text, appended, true, null);

        Assert.Same(text, resultText);
        Assert.Same(appended, resultAppended);
        Assert.True(unsigned);
    }

    [Fact]
    public void TheReplyLeg_ReadsTheRecordedText_NotTheAggregatorsOutput()
    {
        // v11 #516: the fork close — the reply/PDF documents come from what
        // CompleteResultDocument stored, appends and all.
        var deliverables = ConsultDeliverables.Resolve(
            new[] { new ConsultResultDescriptor("consult", "assemble-note", "Consultation note") },
            null,
            new Dictionary<string, ConsultNodeDescriptor>(StringComparer.Ordinal));
        var recorded = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["consult"] = "Rendered.\n\nAppended block."
        };

        var documents = ConsultDeliverables.ReplyDocumentsFor(deliverables, recorded);

        var document = Assert.Single(documents);
        Assert.Equal("consult", document.ResultId);
        Assert.Equal("Consultation note", document.Label);
        Assert.Equal("Rendered.\n\nAppended block.", document.Text);
    }
}

/// <summary>
/// v11 rung (c) (#516, § 7): a signature entry is inside both hashes and
/// named with its as-of; the unsigned state is stored only when true, said
/// in History by name, and absent on the control.
/// </summary>
public class WorkflowV11SignatureRecordTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static async Task CompleteAsync(ConsultGenerationJobEntity entity, ConsultGenerationResultDocument document)
    {
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(document);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
    }

    [Fact]
    public async Task ASignatureEntry_IsInsideBothHashes_WithItsAsOf()
    {
        var (entity, state) = Job();
        const string signedText = "Consultation note\n\nTaylor Reyes, MD";

        await CompleteAsync(entity, new ConsultGenerationResultDocument("note", "Consultation note", signedText, 0,
            new[] { new ConsultAppendedEntry(ConsultAppendedKinds.Signature, "clinic-letters", "2026-08-30") }));

        var document = state().AssembledDocuments!.Single();
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(signedText), document.DocumentHash);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeResultSetHash(new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = signedText }),
            state().WorkflowOutputHash);
        var entry = Assert.Single(state().ToResponse().AssembledDocuments!.Single().Appended!);
        Assert.Equal(("signature", "clinic-letters", "2026-08-30"), (entry.Kind, entry.Id, entry.AsOf));
        Assert.Null(document.Unsigned);
    }

    [Fact]
    public async Task AnUnsignedDocument_StoresTrue_SurfacesIt_AndHistorySaysSo()
    {
        var (entity, state) = Job();

        await CompleteAsync(entity, new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0, null, true));

        Assert.True(state().AssembledDocuments!.Single().Unsigned);
        Assert.True(state().ToResponse().AssembledDocuments!.Single().Unsigned);
        var said = state().History.Single(h => h.Kind == "unsigned");
        Assert.Equal(
            "Produced unsigned: Consultation note — signature requested by the package; none chosen on the profile",
            said.Label);
    }

    [Fact]
    public async Task TheControl_StoresNoUnsigned_EvenWhenSentFalse()
    {
        var (entity, state) = Job();

        await CompleteAsync(entity, new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0, null, false));

        Assert.Null(state().AssembledDocuments!.Single().Unsigned);
        Assert.Null(state().ToResponse().AssembledDocuments!.Single().Unsigned);
        Assert.DoesNotContain(state().History, h => h.Kind == "unsigned");
    }
}
