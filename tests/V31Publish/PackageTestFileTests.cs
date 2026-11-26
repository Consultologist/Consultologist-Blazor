using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 10; part 07 §§ 8.1 to 8.3; R9; Review Focus 3): a test file read against the compiled
/// package (<see cref="PackageTestFiles.Read"/>), and the sentences publish says about it (<see cref="PackagePublishSentences"/>).
/// A test file is valid JSON of any shape here: each shape gives a sentence at the test's path, never an exception, and no
/// sentence repeats a key or value of the file. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PackageTestFileTests
{
    private const string Path = "tests/two_sections.json";

    private static readonly RunPlan Plan = RunFixture.Plan(RunGoldenTests.Macros());

    /// <summary>A package whose macro reads two profile fields (signature, name) and whose draft reads the run's date.</summary>
    private static readonly RunPlan ProfilePlan = RunFixture.Plan(TemplateFixture.Package()
        .WithMacro("signoff", "With thanks,\n{{ profile.signature }}\n{{ profile.name }}\n")
        .WithAgent("draft", "Draft the note for Margaret Okafor on {{ run.date | date.to_string \"%d %B %Y\" }}.\n{{~ macro.signoff }}\n").RunDependent("nodes/draft.md")
        .WithJson("nodes/draft.md", h => h["profileCarrying"] = true));

    private static (PackageTestFile? Test, IReadOnlyList<SourceRefusal> Refusals) ReadWith(RunPlan plan, JsonObject json) =>
        PackageTestFiles.Read(new SourceTest("two_sections", Path, json), json.ToJsonString(), plan);

    private static JsonObject ValidFor(RunPlan plan)
    {
        var json = new JsonObject
        {
            ["description"] = null,
            ["inputs"] = new JsonObject(),
            ["profile"] = new JsonObject(),
            ["run"] = new JsonObject(),
            ["outputs"] = new JsonObject(),
            ["expect"] = new JsonObject(),
        };
        foreach (var id in plan.InputOrder)
            ((JsonObject)json["inputs"]!)[id] = null;
        foreach (var field in plan.NeedsProfile.Distinct())
            ((JsonObject)json["profile"]!)[field] = "Dr A. Sample";
        foreach (var fact in plan.RunFacts.Select(f => f.StartsWith("run.", StringComparison.Ordinal) ? f[4..] : f).Distinct())
            ((JsonObject)json["run"]!)[fact] = fact == "date" ? new JsonObject { ["value"] = "2026-11-16", ["timeZone"] = "America/Toronto" } : "x";
        return json;
    }

    private static JsonObject Valid()
    {
        var inputs = new JsonObject();
        foreach (var id in Plan.InputOrder)
            inputs[id] = null;
        var profile = new JsonObject();
        foreach (var field in Plan.NeedsProfile.Distinct())
            profile[field] = "Dr A. Sample";
        var run = new JsonObject();
        foreach (var fact in Plan.RunFacts.Select(f => f.StartsWith("run.", StringComparison.Ordinal) ? f[4..] : f).Distinct())
            run[fact] = fact == "date" ? new JsonObject { ["value"] = "2026-11-16", ["timeZone"] = "America/Toronto" } : "x";
        return new JsonObject
        {
            ["description"] = null,
            ["inputs"] = inputs,
            ["profile"] = profile,
            ["run"] = run,
            ["outputs"] = new JsonObject { ["node.plan"] = JsonNode.Parse("""{"sections":[]}""") },
            ["expect"] = new JsonObject(),
        };
    }

    private static (PackageTestFile? Test, IReadOnlyList<SourceRefusal> Refusals) Read(JsonObject json, string id = "two_sections")
    {
        var text = json.ToJsonString();
        return PackageTestFiles.Read(new SourceTest(id, Path, json), text, Plan);
    }

    private static IReadOnlyList<SourceRefusal> Refused(JsonObject json)
    {
        var (test, refusals) = Read(json);
        Assert.Null(test);
        Assert.NotEmpty(refusals);
        Assert.All(refusals, r =>
        {
            Assert.Equal(Path, r.Path);
            V31Contract.AssertSentence(r);
        });
        return refusals;
    }

    [Fact]
    public void ATestThatSaysWhatThePackageNeeds_IsReadStrictly()
    {
        var json = Valid();
        json["expect"] = JsonNode.Parse("""{"ran":["node.plan"],"deliverables":{"deliverable.note":{"state":"skipped"}}}""");

        var (test, refusals) = Read(json);

        Assert.Empty(refusals);
        Assert.NotNull(test);
        Assert.Equal("two_sections", test!.Id);
        Assert.Equal(Path, test.Path);
        Assert.False(test.Request.GenerateMissing);
        Assert.Equal(json["outputs"]!.ToJsonString(), JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(test.Request.Outputs))!.ToJsonString());
        Assert.Equal(json["expect"]!.ToJsonString(), test.Expect.ToJsonString());
        Assert.Matches("^sha256:[0-9a-f]{64}$", test.FileHash);
    }

    [Fact]
    public void TheFileHash_IsTheHashOfTheTextAsStored_WithLineEndingsNormalised()
    {
        var json = Valid();
        var (crlf, _) = PackageTestFiles.Read(new SourceTest("t", Path, json), json.ToJsonString().Replace("{", "{\r\n", StringComparison.Ordinal), Plan);
        var (lf, _) = PackageTestFiles.Read(new SourceTest("t", Path, json), json.ToJsonString().Replace("{", "{\n", StringComparison.Ordinal), Plan);
        var (other, _) = PackageTestFiles.Read(new SourceTest("t", Path, json), json.ToJsonString() + " ", Plan);

        Assert.Equal(lf!.FileHash, crlf!.FileHash);
        Assert.Equal(PackageFormat.V31.CanonicalJson.HashText(json.ToJsonString().Replace("{", "{\n", StringComparison.Ordinal)), lf.FileHash);
        Assert.NotEqual(lf.FileHash, other!.FileHash);
    }

    [Fact]
    public void AnInputTheFileDoesNotList_IsS07010_AndAListedNullIsFine()
    {
        var json = Valid();
        var dropped = Plan.InputOrder[0];
        ((JsonObject)json["inputs"]!).Remove(dropped);

        var refusals = Refused(json);

        var refusal = Assert.Single(refusals);
        Assert.Equal("S07-010", refusal.Id);
        Assert.Contains($"input '{dropped}'", refusal.Sentence, StringComparison.Ordinal);
        Assert.Contains("Test 'two_sections'", refusal.Sentence, StringComparison.Ordinal);

        ((JsonObject)json["inputs"]!)[dropped] = null;
        Assert.Empty(Read(json).Refusals);
    }

    [Fact]
    public void EveryInputMissing_IsSaidOnce_InTheOrderTheyAreDeclared()
    {
        var json = Valid();
        json["inputs"] = new JsonObject();

        var refusals = Refused(json);

        Assert.Equal(Plan.InputOrder.Select(id => $"S07-010 {id}"), refusals.Select(r => $"{r.Id} {id(r)}"));

        static string id(SourceRefusal r) => r.Sentence.Split('\'')[3];
    }

    [Fact]
    public void AProfileThatIsNotExactlyWhatThePackageReads_IsS07011_WithTheSortedFields()
    {
        Assert.Equal(["name", "signature"], ProfilePlan.NeedsProfile.Distinct().Order(StringComparer.Ordinal));
        Assert.Empty(ReadWith(ProfilePlan, ValidFor(ProfilePlan)).Refusals);

        var fewer = ValidFor(ProfilePlan);
        ((JsonObject)fewer["profile"]!).Remove("name");
        var more = ValidFor(ProfilePlan);
        ((JsonObject)more["profile"]!)["closing"] = "Kind regards,";
        var none = ValidFor(ProfilePlan);
        ((JsonObject)none["profile"]!).Clear();

        foreach (var json in new[] { fewer, more, none })
        {
            var (test, refusals) = ReadWith(ProfilePlan, json);
            Assert.Null(test);
            var refusal = Assert.Single(refusals);
            Assert.Equal("S07-011", refusal.Id);
            Assert.Equal("Test 'two_sections' must give exactly the profile fields the workflow reads: name, signature.", refusal.Sentence);
            Assert.Equal(Path, refusal.Path);
            V31Contract.AssertSentence(refusal);
        }

        // a package that reads none: any field given is too many, and the sentence says none
        var extra = Valid();
        ((JsonObject)extra["profile"]!)["closing"] = "Kind regards,";
        Assert.EndsWith("the workflow reads: none.", Assert.Single(Refused(extra)).Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileValueThatIsNotText_IsA11005_ByPosition()
    {
        var json = ValidFor(ProfilePlan);
        ((JsonObject)json["profile"]!)["signature"] = 7;

        var (test, refusals) = ReadWith(ProfilePlan, json);

        Assert.Null(test);
        var refusal = Assert.Single(refusals);
        Assert.Equal("A11-005", refusal.Id);
        Assert.Equal("Test 'two_sections' profile is not written as the test grammar says: entry number 2 is not text.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AnEmptyList_IsWrittenNone_AndAFieldListIsSorted()
    {
        var none = PackagePublishSentences.ProfileMismatch("t", [], Path);
        var sorted = PackagePublishSentences.RunMismatch("t", ["id", "date", "package"], Path);

        Assert.EndsWith("the workflow reads: none.", none.Sentence, StringComparison.Ordinal);
        Assert.EndsWith("the workflow reads: date, id, package.", sorted.Sentence, StringComparison.Ordinal);
        V31Contract.AssertSentence(none);
        V31Contract.AssertSentence(sorted);
    }

    [Fact]
    public void ARunThatIsNotExactlyTheFactsThePackageReads_IsS07012()
    {
        var extra = Valid();
        ((JsonObject)extra["run"]!)["engine"] = "offline";
        ((JsonObject)extra["run"]!)["package"] = "x";
        ((JsonObject)extra["run"]!)["id"] = "x";
        var fewer = Valid();
        ((JsonObject)fewer["run"]!).Clear();

        Assert.Contains(Refused(extra), r => r.Id == "S07-012");
        Assert.Contains(Refused(fewer), r => r.Id == "S07-012");
    }

    [Theory]
    [InlineData("""20261116""")]
    [InlineData("\"2026-11-16\"")]
    [InlineData("""{"value":"2026-11-16"}""")]
    [InlineData("""{"value":2026,"timeZone":"America/Toronto"}""")]
    [InlineData("""{"value":"2026-11-16","timeZone":null}""")]
    [InlineData("""{"value":"2026-11-16","timeZone":"America/Toronto","extra":"x"}""")]
    [InlineData("""[]""")]
    public void ARunDateThatIsNotValueAndTimeZoneAsText_IsA11005(string date)
    {
        Assert.Contains("date", Plan.RunFacts.Select(f => f.Replace("run.", "", StringComparison.Ordinal)));
        var json = Valid();
        ((JsonObject)json["run"]!)["date"] = JsonNode.Parse(date);

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.StartsWith("Test 'two_sections' run is not written as the test grammar says: date", refusal.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("node.")]
    [InlineData("node.x:")]
    [InlineData("node.x:a/")]
    [InlineData("node.x#0")]
    [InlineData("check.")]
    [InlineData("check.x:")]
    [InlineData("deliverable.note")]
    [InlineData("input.referral")]
    [InlineData("Node.x")]
    [InlineData("node.x\n")]
    [InlineData("plan")]
    [InlineData("")]
    public void AnOutputsKeyThatIsNoNodeOrCheckRowKey_IsA11005_WithoutTheKey(string key)
    {
        var json = Valid();
        ((JsonObject)json["outputs"]!)[key] = "text";

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.Contains(" outputs ", refusal.Sentence, StringComparison.Ordinal);
        Assert.Contains("key number 2", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ATenThousandCharacterOutputsKey_IsRefused_NeverEchoed_AndNeverThrows()
    {
        var key = "node." + new string('a', 10_000) + ":";
        var json = Valid();
        ((JsonObject)json["outputs"]!)[key] = "text";

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.True(refusal.Sentence.Length < 400);
    }

    [Theory]
    [InlineData("node.plan")]
    [InlineData("node.sections.draft:2/1")]
    [InlineData("node.refine.judge#3")]
    [InlineData("node.sections.paragraphs.draft_paragraph#2:2/1#3")]
    [InlineData("node.hpi_section.standard_draft")]
    public void AnOutputsKeyOfThePartsGrammar_IsAccepted(string key)
    {
        var json = Valid();
        ((JsonObject)json["outputs"]!)[key] = "text";

        Assert.Empty(Read(json).Refusals);
    }

    [Theory]
    [InlineData("check.all_covered")]
    [InlineData("check.part_coverage.coverage:1")]
    [InlineData("check.refine.judge#2")]
    [InlineData("check.sections.outer#2:1/3")]
    public void SnapshotOutputs_MayHoldCheckKeys(string key)
    {
        // #1508 (2026-11-25, item 9): a check's row key is a stub key too; the reader and test.schema.json agree.
        var json = Valid();
        ((JsonObject)json["outputs"]!)[key] = "text";

        var (test, refusals) = Read(json);

        Assert.Empty(refusals);
        Assert.True(test!.Request.Outputs.ContainsKey(key));
        Assert.True(V31Migration.SnapshotTestTests.TestSchema().Validate(json.ToJsonString(), null), $"{key} does not fit test.schema.json.");

        var neither = Valid();
        ((JsonObject)neither["outputs"]!)["deliverable.note"] = "text";
        Assert.Equal("A11-005", Assert.Single(Refused(neither)).Id);
        Assert.False(V31Migration.SnapshotTestTests.TestSchema().Validate(neither.ToJsonString(), null), "deliverable.note fits test.schema.json.");
    }

    [Theory]
    [InlineData("ran", """{"a":1}""")]
    [InlineData("ran", """["node.x:"]""")]
    [InlineData("ran", """[3]""")]
    [InlineData("skipped", "\"node.x\"")]
    [InlineData("decisions", """[]""")]
    [InlineData("decisions", """{"if_one":1}""")]
    [InlineData("decisions", """{"":true}""")]
    [InlineData("loops", """{"node.x":{"count":"3"}}""")]
    [InlineData("loops", """{"node.x":{"count":3.5}}""")]
    [InlineData("loops", """{"node.x":{"passes":2}}""")]
    [InlineData("loops", """{"node.x":{"count":1,"passes":2,"stopped":"max"}}""")]
    [InlineData("values", """[1,2]""")]
    [InlineData("values", """{"node.x:":1}""")]
    [InlineData("prompts", """{"node.x":"text"}""")]
    [InlineData("prompts", """{"node.x":{"text":1}}""")]
    [InlineData("prompts", """{"node.x":{"hash":"abc"}}""")]
    [InlineData("prompts", """{"node.x":{"text":"a","hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}}""")]
    [InlineData("checks", """{"check.x":{"pass":"yes"}}""")]
    [InlineData("checks", """{"node.x":{"pass":true}}""")]
    [InlineData("deliverables", """{"deliverable.note":{"state":"released"}}""")]
    [InlineData("deliverables", """{"deliverable.note":{"state":"sent","text":"x"}}""")]
    [InlineData("deliverables", """{"deliverable.note":{"state":"held","hash":"sha256:XYZ"}}""")]
    [InlineData("deliverables", """{"deliverable.note":{"state":"skipped","text":"a","hash":"b"}}""")]
    [InlineData("deliverables", """{"node.note":{"state":"skipped"}}""")]
    [InlineData("deliverables", """null""")]
    [InlineData("deliverables", """7""")]
    public void AnExpectMemberOfTheWrongShape_IsA11005_NamingTheMember(string member, string value)
    {
        var json = Valid();
        ((JsonObject)json["expect"]!)[member] = JsonNode.Parse(value);

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.StartsWith($"Test 'two_sections' expect.{member} is not written as the test grammar says: ", refusal.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"state":"skipped"}""")]
    [InlineData("""{"state":"released","text":"Margaret Okafor will start letrozole."}""")]
    [InlineData("""{"state":"held","hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""")]
    [InlineData("""{"state":"skipped","hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""")]
    public void ADeliverableExpectation_AcceptsSkippedAlone_R9(string expectation)
    {
        var json = Valid();
        json["expect"] = JsonNode.Parse("{\"deliverables\":{\"deliverable.note\":" + expectation + "}}");

        Assert.Empty(Read(json).Refusals);
    }

    [Fact]
    public void EveryExpectMemberOfTheGrammar_IsAccepted()
    {
        var json = Valid();
        json["expect"] = JsonNode.Parse("""
            {
              "ran": ["node.plan", "node.sections.draft:1"],
              "skipped": ["node.sections.draft:2"],
              "decisions": { "if_one": true, "case_two": "branch_a" },
              "loops": { "node.sections": { "count": 2 }, "node.refine": { "passes": 2, "stopped": "until" } },
              "values": { "node.total": { "n": [1, 2] }, "node.flag": null },
              "prompts": { "node.plan": { "text": "Plan for Margaret Okafor." }, "node.draft": { "hash": "sha256:0000000000000000000000000000000000000000000000000000000000000000" } },
              "checks": { "check.part_coverage.coverage:1": { "pass": true } },
              "deliverables": { "deliverable.note": { "state": "held", "text": "x" } }
            }
            """);

        var (test, refusals) = Read(json);

        Assert.Empty(refusals);
        Assert.Equal(8, test!.Expect.Count);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("Ran")]
    [InlineData("node.plan")]
    public void AnExpectationTheGrammarDoesNotHave_IsA11005_AndItsNameIsNotCopiedUnlessPlain(string name)
    {
        var json = Valid();
        ((JsonObject)json["expect"]!)[name] = JsonNode.Parse("[]");

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.Contains("member number 1 is not an expectation", refusal.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inputs")]
    [InlineData("profile")]
    [InlineData("run")]
    [InlineData("outputs")]
    [InlineData("expect")]
    public void AMemberThatIsNotAnObject_IsA11005_AndNothingElseIsSaidOfIt(string member)
    {
        foreach (var hostile in new[] { "[]", "7", "null", "\"x\"" })
        {
            var json = Valid();
            json[member] = JsonNode.Parse(hostile);

            var refusal = Assert.Single(Refused(json));

            Assert.Equal("A11-005", refusal.Id);
            Assert.StartsWith($"Test 'two_sections' {member} is not written as the test grammar says: it is not an object", refusal.Sentence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryRefusalFound_IsReturned_InMemberOrder()
    {
        var json = Valid();
        ((JsonObject)json["inputs"]!).Remove(Plan.InputOrder[0]);
        ((JsonObject)json["profile"]!)["closing"] = "Kind regards,";
        ((JsonObject)json["run"]!)["date"] = 5;
        ((JsonObject)json["outputs"]!)["plan"] = "x";
        ((JsonObject)json["expect"]!)["ran"] = JsonNode.Parse("{}");
        ((JsonObject)json["expect"]!)["values"] = JsonNode.Parse("[]");

        var refusals = Refused(json);

        var ids = refusals.Select(r => r.Id).ToList();
        Assert.Equal(["S07-010", "S07-011", "A11-005", "A11-005", "A11-005", "A11-005"], ids);
        var members = refusals.Where(r => r.Id == "A11-005").Select(r => r.Sentence.Split(' ')[2]).ToList();
        Assert.Equal(["run", "outputs", "expect.ran", "expect.values"], members);
    }

    [Fact]
    public void AnInputTheFileListsAndThePackageDoesNotDeclare_IsA11005_ByPosition()
    {
        var json = Valid();
        var inputs = (JsonObject)json["inputs"]!;
        inputs["Margaret Okafor, 58"] = "x";
        inputs[new string('k', 10_000)] = "y";

        var refusal = Assert.Single(Refused(json));

        Assert.Equal("A11-005", refusal.Id);
        Assert.Equal($"Test 'two_sections' inputs is not written as the test grammar says: key number {Plan.InputOrder.Count + 1} is not a declared input.", refusal.Sentence);
        Assert.DoesNotContain("Okafor", refusal.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("kkkk", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSentence_RepeatsAValueOrAKeyOfTheFile()
    {
        var json = Valid();
        ((JsonObject)json["inputs"]!)["Margaret Okafor"] = "x";
        ((JsonObject)json["profile"]!)["Okafor_field"] = 7;
        ((JsonObject)json["run"]!)["Okafor_fact"] = 7;
        ((JsonObject)json["outputs"]!)["Margaret Okafor"] = "x";
        ((JsonObject)json["expect"]!)["Dr A. Sample"] = JsonNode.Parse("[]");
        ((JsonObject)json["expect"]!)["values"] = JsonNode.Parse("""{"Margaret Okafor":"a"}""");
        ((JsonObject)json["expect"]!)["prompts"] = JsonNode.Parse("""{"node.plan":{"text":7,"Okafor":1}}""");

        var refusals = Refused(json);

        Assert.True(refusals.Count >= 6);
        Assert.All(refusals, r =>
        {
            Assert.DoesNotContain("Okafor", r.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("Sample", r.Sentence, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ARunFactThatIsNotText_IsA11005_ByPosition()
    {
        var json = ValidFor(ProfilePlan);
        ((JsonObject)json["run"]!)["Okafor_fact"] = 1;
        var plain = ValidFor(Plan);
        ((JsonObject)plain["run"]!)["id"] = 1;

        Assert.Contains(Refused(plain), r => r.Id == "S07-012");
        var (_, refusals) = ReadWith(ProfilePlan, json);
        Assert.DoesNotContain(refusals, r => r.Sentence.Contains("Okafor", StringComparison.Ordinal));
    }

    [Fact]
    public void ABadArgument_IsTheOnlyException()
    {
        var json = Valid();
        Assert.Throws<ArgumentNullException>(() => PackageTestFiles.Read(null!, "{}", Plan));
        Assert.Throws<ArgumentNullException>(() => PackageTestFiles.Read(new SourceTest("t", Path, json), null!, Plan));
        Assert.Throws<ArgumentNullException>(() => PackageTestFiles.Read(new SourceTest("t", Path, json), "{}", null!));
    }

    // ---- the sentences ---------------------------------------------------------------------------

    [Fact]
    public void EachPublishSentence_IsItsRowWithItsFillersAndItsId()
    {
        var built = new[]
        {
            PackagePublishSentences.TestFailed("two_sections", "deliverables", "deliverable.note", "sha256:aa", "sha256:bb", Path),
            PackagePublishSentences.NoStub("two_sections", "node.plan", Path),
            PackagePublishSentences.StubInvalid("two_sections", "node.plan", "sections", "/sections/0 required", Path),
            PackagePublishSentences.InputMissing("two_sections", "referral", Path),
            PackagePublishSentences.ProfileMismatch("two_sections", ["signature", "name"], Path),
            PackagePublishSentences.RunMismatch("two_sections", [], Path),
            PackagePublishSentences.TestsTookTooLong(),
            PackagePublishSentences.StubNotUsed("two_sections", "node.extra", Path),
            PackagePublishSentences.EvidenceCommitted("resolve.json"),
            PackagePublishSentences.ImportIgnored(["resolve.json"])!,
            PackagePublishSentences.ManifestNameAndVersion(),
            PackagePublishSentences.TestGrammar("two_sections", "expect.ran", "it is not a list of row keys", Path),
            PackagePublishSentences.AlreadyPublished("example-checks", "2026.11.16.1"),
            PackagePublishSentences.EngineFailed("InvalidOperationException"),
            PackagePublishSentences.StaysAt30("example-checks", "its diagram cannot be migrated"),
        };

        Assert.Equal(
            ["S07-007", "S07-008", "S07-009", "S07-010", "S07-011", "S07-012", "S07-013", "S07-014", "S07-015", "S07-016", "A11-004", "A11-005", "A11-006", "A11-007", "S10-005"],
            built.Select(r => r.Id));
        Assert.All(built, V31Contract.AssertSentence);
        Assert.Equal("InvalidOperationException", built[13].EngineFault);
        Assert.Null(PackagePublishSentences.ImportIgnored(["dag.mmd"]));
    }

    [Fact]
    public void AnImportIgnoredAndAnEvidenceCommitted_AreTheEarlierAreasSentences_NotCopies()
    {
        Assert.Equal(ResolveSentences.EvidenceCommitted("lock.json"), PackagePublishSentences.EvidenceCommitted("lock.json"));
        Assert.Equal(ResolveSentences.ImportIgnored(["lock.json"]), PackagePublishSentences.ImportIgnored(["lock.json"]));
    }
}
