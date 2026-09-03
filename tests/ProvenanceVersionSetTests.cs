using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #400: the provenance record contract and the hash definitions are a
/// published artifact — consultologist-provenance's provenance-versions.json
/// and hash-definitions.md, vendored here as a submodule. These hold the
/// engine's own numbers against that document, and recompute the document's
/// worked examples through the engine's own code, so the published prose and
/// the bytes the engine hashes cannot silently disagree. Read off the
/// submodule, not the registry, for the reason SpecVersionSetTests gives.
/// </summary>
public class ProvenanceVersionSetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string Vendored(string file)
    {
        var path = Path.Combine(RepoRoot(), "external", "consultologist-provenance", file);
        Assert.True(File.Exists(path), $"{path} is missing — the submodule is not checked out (git submodule update --init).");
        return File.ReadAllText(path);
    }

    private static JsonNode Index() => JsonNode.Parse(Vendored("provenance-versions.json"))!;

    private static int[] Ladder(string name) => Index()[name]!.AsArray().Select(v => (int)v!).ToArray();

    [Fact]
    public void TheRecordStorageVersions_AreThePublishedOnes()
    {
        Assert.Equal(Ladder("recordStorageVersions"), ConsultGenerationJobState.StorageVersions);
    }

    [Fact]
    public void TheEffectiveInputLadder_IsThePublishedOne()
    {
        // 1 is historical and 2 has no named constant (ComputeDraftOnlyHash);
        // 3, 4, 5 and 6 are the engine's constants (6 since v10, #499).
        Assert.Equal(
            new[]
            {
                1, 2, ConsultGenerationProvenance.DeclaredInputsHashVersion, ConsultGenerationProvenance.TypedInputsHashVersion,
                ConsultGenerationProvenance.StructuredInputsHashVersion, ConsultGenerationProvenance.NestedInputsHashVersion
            },
            Ladder("effectiveInputHashVersions"));
    }

    [Fact]
    public void TheWorkflowOutputLadder_IsThePublishedOne()
    {
        Assert.Equal(
            new[] { ConsultGenerationProvenance.WorkflowOutputHashVersion, ConsultGenerationProvenance.AssembledDocumentHashVersion, ConsultGenerationProvenance.ResultSetHashVersion },
            Ladder("workflowOutputHashVersions"));
    }

    [Fact]
    public void ThePerNodeLadder_IsThePublishedOne_AndTheEngineStampsItsNewest()
    {
        // #375: five definitions, dated to the commits that changed the
        // rendered-prompt bytes; the engine stamps the newest. A renderer
        // change moves NodeHashVersion and the index together, or fails here.
        Assert.Equal(Enumerable.Range(1, ConsultGenerationProvenance.NodeHashVersion), Ladder("nodeHashVersions"));
        Assert.Equal(Ladder("nodeHashVersions").Max(), ConsultGenerationProvenance.NodeHashVersion);
    }

    [Fact]
    public void EveryDocumentTheIndexNames_IsVendored()
    {
        foreach (var (_, file) in Index()["documents"]!.AsObject())
        {
            Vendored((string)file!);
        }
    }

    // The worked examples, recomputed through the engine — the digests below
    // are copied from hash-definitions.md, and the bytes are what the engine
    // produces, so a definition that drifts from its prose fails here.

    [Fact]
    public void Definition2_DraftOnly() =>
        Assert.Equal("b18720c3b2a3f220df2570021d79cb18ceaa4b1531ea0d1ea1ef9f91bb4e5c79",
            ConsultGenerationProvenance.ComputeDraftOnlyHash(new ConsultGenerationRequest("Hello")));

    [Fact]
    public void Definition3_SortedTextMap_EscapesNonAscii()
    {
        Assert.Equal("8f770258ab53f8b20001e6ba82ae42d66479db3053a3b74776bafa2a92674514",
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(new Dictionary<string, string> { ["b"] = "two", ["a"] = "one" }));
        Assert.Equal("3849b4da05d4d8716eca76c57d1d952e687bd164ef2e3dd53e31b1f1666ca979",
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(new Dictionary<string, string> { ["accent"] = "café" }));
    }

    [Fact]
    public void Definition4_TypedScalars() =>
        Assert.Equal("cba13032e0d21f1098f9c60b8253ace104e399ff0a9c0ded9a26a356ce172756",
            ConsultGenerationProvenance.ComputeTypedInputsHash(new Dictionary<string, ConsultInputValue>
            {
                ["reason"] = ConsultInputValue.OfText("follow-up"),
                ["billable"] = ConsultInputValue.OfBoolean(true)
            }));

    [Fact]
    public void Definition5_StructuredValues_Utf8AsIs() =>
        Assert.Equal("52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b",
            ConsultGenerationProvenance.ComputeStructuredInputsHash(new Dictionary<string, ConsultInputValue>
            {
                ["patient"] = ConsultInputValue.OfObject(new[]
                {
                    new ConsultInputEntry("name", ConsultInputValue.OfText("Ada")),
                    new ConsultInputEntry("age", ConsultInputValue.OfNumber("36"))
                }),
                ["notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("x"), ConsultInputValue.OfText("y") }),
                ["accent"] = ConsultInputValue.OfText("café")
            }));

    [Fact]
    public void Definition6_DefinitionFiveRecursed_AndTheControl()
    {
        // provenance@v2026.08.7 (#499): a nested object and a nested array
        // inside an array element, sorted at every level, order kept.
        Assert.Equal("b6a313365b611c7ec0be83d67237876ae56d4fe5fac3b77e758985551f59037d",
            ConsultGenerationProvenance.ComputeNestedInputsHash(new Dictionary<string, ConsultInputValue>
            {
                ["family_history"] = ConsultInputValue.OfArray(new[]
                {
                    ConsultInputValue.OfObject(new[]
                    {
                        new ConsultInputEntry("relative", ConsultInputValue.OfText("mother")),
                        new ConsultInputEntry("contact", ConsultInputValue.OfObject(new[]
                        {
                            new ConsultInputEntry("preferred", ConsultInputValue.OfText("email")),
                            new ConsultInputEntry("phone", ConsultInputValue.OfText("555"))
                        })),
                        new ConsultInputEntry("conditions", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("b"), ConsultInputValue.OfText("a") }))
                    })
                })
            }));

        // The control the document states: definition 5's own example, under 6, is the same bytes.
        var flat = new Dictionary<string, ConsultInputValue>
        {
            ["patient"] = ConsultInputValue.OfObject(new[]
            {
                new ConsultInputEntry("name", ConsultInputValue.OfText("Ada")),
                new ConsultInputEntry("age", ConsultInputValue.OfNumber("36"))
            }),
            ["notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("x"), ConsultInputValue.OfText("y") }),
            ["accent"] = ConsultInputValue.OfText("café")
        };
        Assert.Equal("52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b", ConsultGenerationProvenance.ComputeNestedInputsHash(flat));
        Assert.Equal(ConsultGenerationProvenance.ComputeStructuredInputsHash(flat), ConsultGenerationProvenance.ComputeNestedInputsHash(flat));
    }

    [Fact]
    public void AClassifiersOutputHash_IsOverTheNormalisedValue()
    {
        // hash-definitions.md § 4 (v2026.08.7): the output side of a classifier
        // is the declared value the reply resolved to, and the number does not move.
        Assert.Equal("9464a24113872b892e176555598c34aa1a900ae21b2a7dadc4916b40a423d0cf", ConsultGenerationProvenance.Sha256Hex("in_scope"));
        Assert.Equal("in_scope", Consultologist.Api.Workflow.ClassificationOutputContract.Normalize("""{"value": " In_Scope "}""", new[] { "in_scope", "out_of_scope" }, "scope"));
        Assert.Equal(5, ConsultGenerationProvenance.NodeHashVersion);
    }

    [Fact]
    public void ThePerInputDocumentPair_IsTheFileAndItsCanonicalReading()
    {
        // provenance@v2026.08.9 (#512), hash-definitions.md § 6: the file's
        // bytes as received, and the text read from it as the input hash saw
        // it — two trailing newlines trimmed away by the canonical form.
        var file = System.Text.Encoding.UTF8.GetBytes("Hello\n\n");
        Assert.Equal(WorkedExample("fileSha256"), ConsultGenerationProvenance.Sha256Hex(file));
        Assert.Equal(WorkedExample("textSha256"), ConsultGenerationProvenance.Sha256Hex(CanonicalText.Normalize("Hello\n\n")!));
        Assert.Equal("185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969", WorkedExample("textSha256"));
    }

    /// <summary>The digest a named § 6 worked-example row publishes, read from the vendored document.</summary>
    private static string WorkedExample(string digest)
    {
        var line = Vendored("hash-definitions.md").Split('\n').Single(l => l.StartsWith($"| `{digest}` |", StringComparison.Ordinal));
        return System.Text.RegularExpressions.Regex.Match(line, "`([0-9a-f]{64})`").Groups[1].Value;
    }

    [Fact]
    public void ThePerDocumentDigest_IsUnchangedByAnAppendedBlock()
    {
        // provenance@v2026.08.11 (#565), hash-definitions.md § 5: the appended
        // text (a macro's expansion, the signature block) is inside `text`
        // before the digest is taken — one document, never a document plus
        // attachments. The v11 record's § 7 control, as the registry's worked
        // example row publishes it.
        Assert.Equal(WorkedExample("documentHash"), ConsultGenerationProvenance.Sha256Hex("Note.\n\nAppended disclaimer."));
        Assert.Equal(
            "4c4be8b3d6ef10d96f96d95f0cefbf81dfcf268030bf44679227b80f9c74aac3",
            ConsultGenerationProvenance.Sha256Hex("Note.\n\nAppended disclaimer."));
    }

    [Fact]
    public void TheV12WorkedExample_IsWhatTheEnginesOwnCompositionProduces()
    {
        // provenance@v2026.09.4 (#622), hash-definitions.md § 5: a placed
        // macro, a chosen optional macro and an embedded signature compose
        // to one text and one digest — recomputed here through the very
        // seams the engine runs (Compose, then Finish), so the registry's
        // published bytes and the engine can never drift apart silently.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["disclaimer"] = "Standing disclaimer.",
            ["closing"] = "Sincerely,\n\n{{profile:signature}}"
        };
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        var facts = new Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts(
            new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.1",
            "east.ca.api.consultologist.ai", "Taylor Reyes",
            new Consultologist.Api.Jobs.ConsultSignatureSnapshot("clinic-letters", "Taylor Reyes, MD", "2026-09-01"));

        var (text, appended, tokenCarried) = Consultologist.Api.Jobs.ConsultMacroExpander.Compose(
            new[] { "node:plan" },
            new Consultologist.Api.Jobs.ConsultAggregateRenderer.Part[] { new Consultologist.Api.Jobs.ConsultAggregateRenderer.ScalarPart("The plan.") },
            new[] { "disclaimer", "closing" },
            new[] { new Consultologist.Api.Models.ConsultMacroPlacement("disclaimer", Before: "node:plan") },
            texts, none, null, none, facts);
        var (finalText, finalAppended, unsigned) = Consultologist.Api.Jobs.ConsultSignatureAppend.Finish(
            text, appended, signed: true, tokenCarried, facts.Signature);

        Assert.Equal("Standing disclaimer.\n\nThe plan.\n\nSincerely,\n\nTaylor Reyes, MD", finalText);
        Assert.Equal(
            "c41f36a58079d55dbfedccb627d6232b98e34f879bdaafef57a4ff9e22c4ba1a",
            ConsultGenerationProvenance.Sha256Hex(finalText));
        // The vendored document publishes the same digest on its labelled row.
        Assert.Contains(
            "`c41f36a58079d55dbfedccb627d6232b98e34f879bdaafef57a4ff9e22c4ba1a`",
            Vendored("hash-definitions.md").Split('\n').Single(l => l.StartsWith("| `documentHash` (specVersion 12) |", StringComparison.Ordinal)));
        // appended[] in document order: placed, appended, embedded.
        Assert.Equal(
            new[] { ("macro", "disclaimer", (string?)null), ("macro", "closing", null), ("signature", "clinic-letters", "2026-09-01") },
            finalAppended!.Select(entry => (entry.Kind, entry.Id, entry.AsOf)));
        Assert.Null(unsigned);
    }

    [Fact]
    public void OutputDefinitions_1_2_3_AndTheAggregateInput()
    {
        Assert.Equal("208471a047a8964edc58a50d8317ad24a711e04b59445006ec06e8e44dc38f85",
            ConsultGenerationProvenance.ComputeWorkflowOutputHash(new Dictionary<string, string> { ["plan"] = "Plan", ["hpi"] = "History" }));
        Assert.Equal("480ef298782bf4aab9a0b181ed6e0a22e049c1f0c51d85cf9659ae6220c23149",
            ConsultGenerationProvenance.ComputeAssembledDocumentHash("Consultation note\n"));
        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5",
            ConsultGenerationProvenance.ComputeResultSetHash(new Dictionary<string, string> { ["note"] = "Consultation note", ["letter"] = "Patient letter" }));
        Assert.Equal("d8429debeb9facdd005d84147a126b01bbb0b5ea60944dad1b96ee1bd2d73c8d",
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[]
            {
                ConsultGenerationProvenance.Sha256Hex("Consultation note"),
                ConsultGenerationProvenance.Sha256Hex("Patient letter")
            }));
        Assert.Equal("185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969", ConsultGenerationProvenance.Sha256Hex("Hello"));
    }
}
