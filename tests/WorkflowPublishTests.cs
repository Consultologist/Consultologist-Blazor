using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// The publish slice of the in-app editor (#57): CalVer assignment, acct-*
/// naming and the owner-only access rule, and the publisher pipeline against
/// in-memory fakes — stamping, structural guards, validator wiring, the
/// conditional-create retry, activation, and the content→publish round trip.
/// </summary>
public class CalVerAssignNextTests
{
    private static DateTimeOffset At(int year, int month) => new(year, month, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstPublish_OpensTheCurrentMonth()
    {
        Assert.Equal("v2026.07.1", CalVerVersion.AssignNext(null, At(2026, 7)).ToString());
    }

    [Fact]
    public void SameMonth_IncrementsTheCounter()
    {
        CalVerVersion.TryParse("v2026.07.3", out var latest);
        Assert.Equal("v2026.07.4", CalVerVersion.AssignNext(latest, At(2026, 7)).ToString());
    }

    [Fact]
    public void NewMonth_RestartsTheCounter()
    {
        CalVerVersion.TryParse("v2026.06.9", out var latest);
        Assert.Equal("v2026.07.1", CalVerVersion.AssignNext(latest, At(2026, 7)).ToString());
    }

    [Fact]
    public void YearRollover_RestartsTheCounter()
    {
        CalVerVersion.TryParse("v2025.12.5", out var latest);
        Assert.Equal("v2026.01.1", CalVerVersion.AssignNext(latest, At(2026, 1)).ToString());
    }

    [Fact]
    public void LatestAheadOfClock_KeepsItsMonth_NeverMovesBackwards()
    {
        CalVerVersion.TryParse("v2026.08.2", out var latest);
        Assert.Equal("v2026.08.3", CalVerVersion.AssignNext(latest, At(2026, 7)).ToString());
    }
}

public class WorkflowPackageNamingTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void ForAccount_TakesTwelveHex_Lowercased()
    {
        Assert.Equal("acct-0123456789ab", WorkflowPackageNaming.ForAccount(OwnerId));
        Assert.Equal("acct-0123456789ab", WorkflowPackageNaming.ForAccount(OwnerId.ToUpperInvariant()));
    }

    [Fact]
    public void ForAccount_RejectsShortIds()
    {
        Assert.Throws<ArgumentException>(() => WorkflowPackageNaming.ForAccount("abc"));
    }

    [Theory]
    [InlineData("general", true)]
    [InlineData("breast-oncology", true)]
    [InlineData("acct-0123456789ab", true)]
    [InlineData("acct-999999999999", false)]
    [InlineData("acct-0123456789abcd", false)]
    public void CanAccess_Matrix(string name, bool expected)
    {
        Assert.Equal(expected, WorkflowPackageNaming.CanAccess(name, OwnerId));
    }

    [Theory]
    [InlineData("acct-0123456789ab", true)]
    [InlineData("general", false)]
    [InlineData("account-x", false)]
    public void IsAccountPackage_ByPrefix(string name, bool expected)
    {
        Assert.Equal(expected, WorkflowPackageNaming.IsAccountPackage(name));
    }
}

public class ForeignPinFallbackTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task ForeignAccountPin_FallsThroughToDefault()
    {
        var settings = new FakeSettingsStore();
        await settings.SaveAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, "acct-999999999999@v2026.07.1", "text/plain", CancellationToken.None);
        var resolver = new WorkflowPackagePinResolver(settings, NullLogger<WorkflowPackagePinResolver>.Instance);

        var resolved = await resolver.ResolvePinAsync(OwnerId, CancellationToken.None);

        Assert.Equal("general", resolved.Name);
    }

    [Fact]
    public async Task OwnAccountPin_Resolves()
    {
        var settings = new FakeSettingsStore();
        await settings.SaveAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, "acct-0123456789ab@v2026.07.1", "text/plain", CancellationToken.None);
        var resolver = new WorkflowPackagePinResolver(settings, NullLogger<WorkflowPackagePinResolver>.Instance);

        var resolved = await resolver.ResolvePinAsync(OwnerId, CancellationToken.None);

        Assert.Equal("acct-0123456789ab@v2026.07.1", resolved.ToString());
    }
}

public class WorkflowPackagePublisherTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string AccountName = "acct-0123456789ab";
    private const string SourceRef = "general@v2026.08.1";

    private static readonly OutputContractCatalog Catalog = LoadCatalog();

    private static OutputContractCatalog LoadCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "external", "consultologist-agents", "agents", "output-contracts.json")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }

    private static (WorkflowPackagePublisher Publisher, FakeRegistryWriter Writer, FakeSettingsStore Settings) CreatePublisher(
        DateTimeOffset? nowUtc = null,
        FakeRegistryWriter? writer = null,
        WorkflowPackageManifest? parentManifest = null,
        Dictionary<string, string>? parentFiles = null,
        string sourceRef = SourceRef)
    {
        writer ??= new FakeRegistryWriter();
        var settings = new FakeSettingsStore();
        var store = new FakePackageStore(sourceRef, parentManifest, parentFiles);
        var publisher = new WorkflowPackagePublisher(
            store,
            writer,
            settings,
            Catalog,
            new FixedTimeProvider(nowUtc ?? new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<WorkflowPackagePublisher>.Instance);
        return (publisher, writer, settings);
    }

    private static WorkflowPackagePublishRequest Request(
        string? source = SourceRef,
        WorkflowPackageManifest? manifest = null,
        Dictionary<string, string>? files = null)
    {
        manifest ??= V5Fixtures.Manifest();
        files ??= V5Fixtures.Files(manifest);
        return new WorkflowPackagePublishRequest(source, manifest, files);
    }

    [Fact]
    public async Task Publish_StampsNameVersionAndLineage_UploadsAndActivates()
    {
        var (publisher, writer, settings) = CreatePublisher();
        // The client's name/version/derivedFrom are hostile on purpose.
        var manifest = V5Fixtures.Manifest() with { Name = "general", Version = "v9999.01.1", DerivedFrom = "evil@v2020.01.1" };

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: V5Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(AccountName, result.Response!.Name);
        Assert.Equal("v2026.07.1", result.Response.Version);
        Assert.Equal($"{AccountName}@v2026.07.1", result.Response.Ref);

        var stored = writer.ReadManifest(AccountName, "v2026.07.1");
        Assert.Equal(AccountName, stored.Name);
        Assert.Equal("v2026.07.1", stored.Version);
        Assert.Equal(SourceRef, stored.DerivedFrom);

        Assert.Equal("v2026.07.1", writer.LatestPointers[AccountName]);
        var pin = await settings.GetAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, CancellationToken.None);
        Assert.Equal($"{AccountName}@v2026.07.1", pin!.Value);
    }

    [Fact]
    public async Task Publish_RoundTrips_EveryFileByteIdentical_AndManifestRevalidates()
    {
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));

        foreach (var (path, content) in files)
        {
            Assert.Equal(content, writer.Blobs[$"{AccountName}/v2026.07.1/{path}"]);
        }

        // The stored manifest deserializes and passes the same validator again —
        // the registry copy is as valid as the request was.
        var reparsed = writer.ReadManifest(AccountName, "v2026.07.1");
        var revalidated = V5Fixtures.Validate(reparsed, files);
        Assert.True(revalidated.IsValid, string.Join(" | ", revalidated.Errors));
    }

    [Fact]
    public async Task Publish_AcceptsEditedNodeFields_TheEditorShape()
    {
        // Node editing (#117): a relabeled node and a node whose output
        // contract was dropped (its binding uses no concept renderer, so
        // nothing else must change) publish and round-trip.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var nodes = manifest.Nodes!.Select(node => node.Id switch
        {
            "extract-patient-concepts" => node with { Label = "Reading the draft" },
            "create-typical-trajectory" => node with { Output = null },
            _ => node
        }).ToList();
        manifest = manifest with { Nodes = nodes };

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: V5Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stored = writer.ReadManifest(AccountName, "v2026.07.1");
        Assert.Equal("Reading the draft", stored.Nodes!.Single(n => n.Id == "extract-patient-concepts").Label);
        Assert.Null(stored.Nodes!.Single(n => n.Id == "create-typical-trajectory").Output);
    }

    [Fact]
    public async Task Publish_AcceptsAggregateEditAndFlippedResult_TheEditorShape()
    {
        // Aggregator authoring (#117), v6: a second aggregator over reordered
        // sources becomes the deliverable; the old result aggregator joins its
        // sources so reachability holds.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V6Fixtures.MultiCollection();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!
                .Append(new WorkflowNodeSpec("assemble-alt", "Alternate assembly",
                    Aggregate: new List<string> { "node:contextualize", "node:assemble-note" }))
                .ToList(),
            Result = "node:assemble-alt"
        };

        var result = await publisher.PublishAsync(OwnerId, new WorkflowPackagePublishRequest(SourceRef, manifest, V6Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stored = writer.ReadManifest(AccountName, "v2026.07.1");
        Assert.Equal("node:assemble-alt", stored.Result);
        Assert.Equal(
            new[] { "node:contextualize", "node:assemble-note" },
            stored.Nodes!.Single(n => n.Id == "assemble-alt").Aggregate);
    }

    [Fact]
    public async Task Publish_AcceptsAnAddedNodeWithItsPrompt_TheEditorShape()
    {
        // Node authoring (#117): a new forEach node born with its 1:1 prompt
        // (same id, prompts/{id}.md, bindings matching the declared
        // variables) publishes and round-trips.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        manifest = manifest with
        {
            Prompts = manifest.Prompts!
                .Append(new WorkflowPromptSpec("summarize-standards", "prompts/summarize-standards.md",
                    new List<string> { "section_name" }))
                .ToList(),
            Nodes = manifest.Nodes!
                .Append(new WorkflowNodeSpec("summarize-standards", "Summarizing standards",
                    Prompt: "summarize-standards",
                    Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                    {
                        ["section_name"] = new("item:name")
                    },
                    ForEach: "data:standards"))
                .ToList()
        };

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: V5Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stored = writer.ReadManifest(AccountName, "v2026.07.1");
        Assert.Equal("data:standards", stored.Nodes!.Single(n => n.Id == "summarize-standards").ForEach);
        Assert.Contains(stored.Prompts!, p => p.Id == "summarize-standards");
        Assert.Contains($"{AccountName}/v2026.07.1/prompts/summarize-standards.md", writer.Blobs.Keys);
    }

    [Fact]
    public async Task Publish_AcceptsAnAddedSecondCollection_TheEditorShape()
    {
        // The "+ data folder" contract (#154): a fork adds one data-map entry,
        // a freshly composed index.json, and the first item's file — inert
        // (no node reads it) but valid, on v5 as well as v6.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        manifest = manifest with
        {
            Data = new Dictionary<string, string>(manifest.Data!) { ["guidelines"] = "data/guidelines/" }
        };
        var files = V5Fixtures.Files(manifest);
        files["data/guidelines/index.json"] = """
            {
              "fields": ["id", "name", "content"],
              "items": [
                { "id": "neutropenic-fever", "name": "Neutropenic Fever", "file": "neutropenic-fever.md" }
              ]
            }
            """;
        files["data/guidelines/neutropenic-fever.md"] = "Assess for neutropenic fever risk.";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("data/guidelines/", writer.ReadManifest(AccountName, "v2026.07.1").Data!["guidelines"]);
        Assert.Equal(
            files["data/guidelines/neutropenic-fever.md"],
            writer.Blobs[$"{AccountName}/v2026.07.1/data/guidelines/neutropenic-fever.md"]);
    }

    // #310: values name the note a package produces; the knowledge is in the
    // collections. A fork relabelled without its content changed says
    // "cardiology" over oncology standards — coherent English, wrong at the
    // source. Warned, not blocked: relabelling first and replacing standards
    // next is legitimate staged authoring, and versions are immutable.

    /// <summary>A parent and a child differing only as the test states.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithValue(string? text)
    {
        var manifest = V5Fixtures.Manifest();
        manifest = manifest with
        {
            Data = new Dictionary<string, string>(manifest.Data!) { ["specialty"] = "data/specialty.txt" }
        };
        var files = V5Fixtures.Files(manifest);

        if (text != null)
        {
            files["data/specialty.txt"] = text;
        }

        return (manifest, files);
    }

    [Fact]
    public async Task Publish_WarnsWhenAValueIsRelabelledButNoCollectionChanged()
    {
        var (parentManifest, parentFiles) = WithValue("oncology");
        var (childManifest, childFiles) = WithValue("cardiology");
        var (publisher, _, _) = CreatePublisher(parentManifest: parentManifest, parentFiles: parentFiles);

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: childManifest, files: childFiles), CancellationToken.None);

        // A warning, never a rejection.
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(result.Response!.Warnings!, w => w.Contains("'specialty'") && w.Contains("no data collection did"));
    }

    [Fact]
    public async Task Publish_DoesNotWarnWhenAValueIsAdded()
    {
        // Measured against acct-7bca2dcc1ed4@v2026.08.3: adding note_type left
        // the standards untouched, and the rule as first written would have
        // warned. A warning that fires on ordinary authoring is ignored.
        var parentManifest = V5Fixtures.Manifest();
        var (childManifest, childFiles) = WithValue("oncology");
        var (publisher, _, _) = CreatePublisher(
            parentManifest: parentManifest, parentFiles: V5Fixtures.Files(parentManifest));

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: childManifest, files: childFiles), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.DoesNotContain(result.Response!.Warnings ?? new List<string>(), w => w.Contains("no data collection did"));
    }

    [Fact]
    public async Task Publish_DoesNotWarnWhenAnEmptyValueIsFilledIn()
    {
        // acct-7bca2dcc1ed4@v2026.08.4: note_type went from "" to real text.
        // A blank prior is not a label being changed.
        var (parentManifest, parentFiles) = WithValue(string.Empty);
        var (childManifest, childFiles) = WithValue("oncology");
        var (publisher, _, _) = CreatePublisher(parentManifest: parentManifest, parentFiles: parentFiles);

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: childManifest, files: childFiles), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.DoesNotContain(result.Response!.Warnings ?? new List<string>(), w => w.Contains("no data collection did"));
    }

    [Fact]
    public async Task Publish_DoesNotWarnWhenTheContentChangedToo()
    {
        // The whole point: relabel plus new standards is a real re-specialisation.
        var (parentManifest, parentFiles) = WithValue("oncology");
        var (childManifest, childFiles) = WithValue("cardiology");
        var standard = childFiles.Keys.First(k => k.StartsWith("data/standards/") && k.EndsWith(".md"));
        childFiles[standard] = "Cardiology-specific guidance for this section.";

        var (publisher, _, _) = CreatePublisher(parentManifest: parentManifest, parentFiles: parentFiles);

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: childManifest, files: childFiles), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.DoesNotContain(result.Response!.Warnings ?? new List<string>(), w => w.Contains("no data collection did"));
    }

    [Fact]
    public async Task Publish_AcceptsASingleValueDataEntry()
    {
        // #318: a data path with no directory segment is a value —
        // WorkflowDataResolver reads it as a scalar and nodes bind it as
        // data:<id>. This door refused it, so a fork could never publish one
        // while repo packages could, because the content repo's publish script
        // has no path allowlist. Underscore included on purpose: note_type is
        // published and running.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        manifest = manifest with
        {
            Data = new Dictionary<string, string>(manifest.Data!) { ["note_type"] = "data/note_type.txt" }
        };
        var files = V5Fixtures.Files(manifest);
        files["data/note_type.txt"] = "consult note";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));

        // No trailing slash survives the round trip — that one character is
        // the whole discriminator between a value and a collection.
        Assert.Equal("data/note_type.txt", writer.ReadManifest(AccountName, "v2026.07.1").Data!["note_type"]);
        Assert.Equal("consult note", writer.Blobs[$"{AccountName}/v2026.07.1/data/note_type.txt"]);
    }

    [Fact]
    public async Task Publish_SecondVersion_IncrementsFromLatest()
    {
        var (publisher, writer, _) = CreatePublisher();
        Assert.True((await publisher.PublishAsync(OwnerId, Request(), CancellationToken.None)).Succeeded);

        var second = await publisher.PublishAsync(OwnerId, Request(), CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.Equal("v2026.07.2", second.Response!.Version);
        Assert.Equal("v2026.07.2", writer.LatestPointers[AccountName]);
    }

    // ----- #432: a fork across names starts with no title ------------------

    private static WorkflowPackageManifest Titled() => V9Fixtures.Minimal() with
    {
        Title = "Breast oncology consults",
        Description = "Referral triage and consult notes for the breast clinic.",
        // #453: tags ride along, and are cleared on the same rule.
        Tags = new List<string> { "oncology", "breast" }
    };

    [Fact]
    public async Task Publish_AForkAcrossNames_ClearsTitleAndDescription()
    {
        // The parent's words about the parent: inherited, they would name a
        // diverging package after something it no longer is.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = Titled();

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: manifest, files: V5Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stored = writer.ReadManifest(AccountName, "v2026.07.1");
        Assert.Null(stored.Title);
        Assert.Null(stored.Description);
        var raw = writer.Blobs[$"{AccountName}/v2026.07.1/manifest.json"];
        Assert.DoesNotContain("\"title\"", raw);
        Assert.DoesNotContain("\"description\"", raw);

        // #453: the tags are cleared to the EMPTY SET, not removed — a v9
        // manifest must state them, and the bytes say "tags": [].
        Assert.NotNull(stored.Tags);
        Assert.Empty(stored.Tags!);
        Assert.Contains("\"tags\": []", raw);
    }

    [Fact]
    public async Task Publish_AForkAcrossNames_OfAPreV9Package_LeavesTagsAbsent()
    {
        // A v8 source has no tags section; clearing must not invent one,
        // which the validator would refuse below 9.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V8Fixtures.Typed();

        var result = await publisher.PublishAsync(
            OwnerId, Request(manifest: manifest, files: V5Fixtures.Files(manifest)), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Null(writer.ReadManifest(AccountName, "v2026.07.1").Tags);
        Assert.DoesNotContain("\"tags\"", writer.Blobs[$"{AccountName}/v2026.07.1/manifest.json"]);
    }

    [Fact]
    public async Task Publish_ARepublishOfTheSamePackage_KeepsTitleAndDescription()
    {
        // The source is the account's own package: no name is crossed.
        var writer = new FakeRegistryWriter();
        await writer.CreateManifestAsync(AccountName, "v2026.07.1", "{}", CancellationToken.None);
        var (publisher, _, _) = CreatePublisher(writer: writer, sourceRef: $"{AccountName}@v2026.07.1");
        var manifest = Titled();

        var result = await publisher.PublishAsync(
            OwnerId,
            Request(source: $"{AccountName}@v2026.07.1", manifest: manifest, files: V5Fixtures.Files(manifest)),
            CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stored = writer.ReadManifest(AccountName, "v2026.07.2");
        Assert.Equal("Breast oncology consults", stored.Title);
        Assert.Equal("Referral triage and consult notes for the breast clinic.", stored.Description);
        Assert.Equal(new[] { "oncology", "breast" }, stored.Tags);
    }

    // ----- #433: the publication stamp -------------------------------------

    private static string StampBlob(string version) => $"{AccountName}/{version}/{WorkflowPackageStamp.FileName}";

    [Fact]
    public async Task Publish_WritesTheStamp_WithTheCatalogRefAndEveryResolvedContract()
    {
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var expected = WorkflowPackageStamp.Compute(manifest, files, Catalog, new List<string>());
        Assert.Equal(expected.ToJson(), writer.Blobs[StampBlob("v2026.07.1")]);
        Assert.Equal(Catalog.ResolvedRef, WorkflowPackageStamp.Read(writer.Blobs[StampBlob("v2026.07.1")], "x@v1").CatalogRef);
        Assert.Equal("concept-list", expected.Contracts["concept-list"]);

        // Exactly the request's files, the manifest and the stamp — nothing else.
        Assert.Equal(
            files.Keys.Append("manifest.json").Append(WorkflowPackageStamp.FileName).Select(path => $"{AccountName}/v2026.07.1/{path}").Order(StringComparer.Ordinal),
            writer.Blobs.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Publish_NoSchemas_StampsAnEmptyContractsMap()
    {
        var (publisher, writer, _) = CreatePublisher();
        // Every fixture declares concept-list; a package with no schemas is one
        // whose prompt nodes are all text.
        var typed = V9Fixtures.Minimal();
        var manifest = typed with
        {
            Schemas = null,
            Nodes = typed.Nodes!.Select(node => node with
            {
                Output = null,
                // A concept renderer needs a concept-list output; plain text does not.
                Bindings = node.Bindings?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { As = null },
                    StringComparer.Ordinal)
            }).ToList()
        };
        var files = V5Fixtures.Files(manifest).Where(pair => !pair.Key.StartsWith("schemas/", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var stamp = WorkflowPackageStamp.Read(writer.Blobs[StampBlob("v2026.07.1")], "x@v1");
        Assert.Equal(Catalog.ResolvedRef, stamp.CatalogRef);
        Assert.Empty(stamp.Contracts);
    }

    [Fact]
    public async Task Publish_UploadsTheStampBeforeTheManifest()
    {
        // The manifest is the commit marker: a version is never reachable
        // without its stamp.
        var (publisher, writer, _) = CreatePublisher();

        await publisher.PublishAsync(OwnerId, Request(), CancellationToken.None);

        var stampAt = writer.Writes.IndexOf(StampBlob("v2026.07.1"));
        var manifestAt = writer.Writes.IndexOf($"{AccountName}/v2026.07.1/manifest.json");
        Assert.True(stampAt >= 0 && stampAt < manifestAt, string.Join(", ", writer.Writes));
        Assert.All(writer.Writes.Take(stampAt), path => Assert.DoesNotContain("manifest.json", path));
    }

    [Fact]
    public async Task Publish_VersionConflict_StampsTheFinalVersion()
    {
        var writer = new FakeRegistryWriter();
        await writer.CreateManifestAsync(AccountName, "v2026.07.1", "{}", CancellationToken.None);
        var (publisher, _, _) = CreatePublisher(writer: writer);

        var result = await publisher.PublishAsync(OwnerId, Request(), CancellationToken.None);

        Assert.Equal("v2026.07.2", result.Response!.Version);
        Assert.Contains(StampBlob("v2026.07.2"), writer.Blobs.Keys);
        // The conflicted candidate's stamp is orphaned with its files — the
        // same unreachable orphan the loop's comment already describes.
        Assert.Equal(writer.Blobs[StampBlob("v2026.07.1")], writer.Blobs[StampBlob("v2026.07.2")]);
    }

    [Fact]
    public async Task Publish_RejectsPublishJsonInTheRequest_ByPath()
    {
        // The stamp is the publisher's to write. A client-supplied one is a
        // root-level file, which the path rule already refuses.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files[WorkflowPackageStamp.FileName] = "{}";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(
            "File path 'publish.json' is not allowed: expected prompts/<file>, schemas/<file>, data/<collection>/<file>, or data/<file>.",
            result.Errors);
        Assert.Empty(writer.Blobs);
    }

    [Fact]
    public async Task Publish_ADeclaredSchemaMatchingNothing_IsRefusedAtTheDesk_AndWritesNothing()
    {
        // Unreferenced, so the validator's closure never saw it; it used to
        // publish and strand at load.
        var (publisher, writer, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        manifest = manifest with { Schemas = new Dictionary<string, string>(manifest.Schemas!) { ["extra"] = "schemas/extra.json" } };
        var files = V5Fixtures.Files(manifest);
        files["schemas/extra.json"] = TestOutputContracts.ConceptListSchema.TrimEnd().TrimEnd('}') + ", \"x-drift\": true }";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains($"Schema 'extra' matches no contract in {Catalog.ResolvedRef}.", result.Errors);
        Assert.Empty(writer.Blobs);
    }

    [Fact]
    public async Task Publish_VersionConflict_RereadsBumpsAndRetries()
    {
        // A manifest already exists at the candidate version but the latest
        // pointer lags behind (stale pointer) — the retry must bump past the
        // collision, not re-collide.
        var writer = new FakeRegistryWriter();
        await writer.CreateManifestAsync(AccountName, "v2026.07.1", "{}", CancellationToken.None);
        var (publisher, _, _) = CreatePublisher(writer: writer);

        var result = await publisher.PublishAsync(OwnerId, Request(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("v2026.07.2", result.Response!.Version);
        Assert.Equal("v2026.07.2", writer.LatestPointers[AccountName]);
    }

    [Fact]
    public async Task Publish_ForeignSource_IsForbidden()
    {
        var (publisher, _, _) = CreatePublisher();

        var result = await publisher.PublishAsync(OwnerId, Request(source: "acct-999999999999@v2026.07.1"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a ref")]
    [InlineData("general@latest")]
    public async Task Publish_RejectsMissingOrNonConcreteSource(string? source)
    {
        var (publisher, _, _) = CreatePublisher();

        var result = await publisher.PublishAsync(OwnerId, Request(source: source), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Forbidden);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Publish_UnresolvableSource_Errors()
    {
        var (publisher, _, _) = CreatePublisher();

        var result = await publisher.PublishAsync(OwnerId, Request(source: "general@v2020.01.1"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("could not be resolved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wwwroot/evil.md")]
    [InlineData("prompts/..")]
    [InlineData("data/standards/nested/deeper.md")]
    [InlineData("prompts/sub/dir.md")]
    // #318: the value pattern matches "data/.." and "data/." by shape, so the
    // traversal guard is the only thing rejecting them. It became load-bearing
    // the moment one-segment data paths were allowed.
    [InlineData("data/..")]
    [InlineData("data/.")]
    [InlineData("data/../escape.txt")]
    [InlineData("data/./x.txt")]
    // "data/" is the one string the value pattern's + and * forms disagree on,
    // and the traversal guard rejects it under either. Pinned here so the
    // behaviour is covered no matter which check happens to catch it.
    [InlineData("data/")]
    public async Task Publish_RejectsIllegalPaths(string path)
    {
        var (publisher, _, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files[path] = "content";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(path, StringComparison.Ordinal) && error.Contains("not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_RejectsStrayFiles_TheReverseClosure()
    {
        var (publisher, _, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files["prompts/unreferenced.md"] = "not in the manifest";

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("prompts/unreferenced.md", StringComparison.Ordinal) && error.Contains("not referenced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_RejectsOversizedFiles()
    {
        var (publisher, _, _) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files[V5Fixtures.StandardsDir + "hpi.md"] = new string('x', 300 * 1024);

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("per-file limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_InvalidPackage_ReturnsValidatorErrors_AndWritesNothing()
    {
        var (publisher, writer, settings) = CreatePublisher();
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        files.Remove("prompts/identify-problem.md");

        var result = await publisher.PublishAsync(OwnerId, Request(manifest: manifest, files: files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(writer.Blobs);
        Assert.Null(await settings.GetAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, CancellationToken.None));
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _nowUtc;

    public FixedTimeProvider(DateTimeOffset nowUtc)
    {
        _nowUtc = nowUtc;
    }

    public override DateTimeOffset GetUtcNow() => _nowUtc;
}

internal sealed class FakeRegistryWriter : IWorkflowPackageRegistryWriter
{
    public Dictionary<string, string> Blobs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LatestPointers { get; } = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manifests = new(StringComparer.Ordinal);

    /// <summary>#433: every blob path in the order it was written — the stamp must precede the manifest.</summary>
    public List<string> Writes { get; } = new();

    public Task<string?> ReadLatestVersionAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(LatestPointers.TryGetValue(name, out var version) ? version : null);

    public Task UploadFileAsync(string name, string version, string path, string content, CancellationToken cancellationToken)
    {
        Blobs[$"{name}/{version}/{path}"] = content;
        Writes.Add($"{name}/{version}/{path}");
        return Task.CompletedTask;
    }

    public Task CreateManifestAsync(string name, string version, string manifestJson, CancellationToken cancellationToken)
    {
        var key = $"{name}/{version}/manifest.json";

        if (!_manifests.Add(key))
        {
            throw new WorkflowPackageVersionConflictException(
                $"{name}@{version}",
                new InvalidOperationException("manifest exists"));
        }

        Blobs[key] = manifestJson;
        Writes.Add(key);
        return Task.CompletedTask;
    }

    public Task SetLatestPointerAsync(string name, string version, CancellationToken cancellationToken)
    {
        LatestPointers[name] = version;
        return Task.CompletedTask;
    }

    public WorkflowPackageManifest ReadManifest(string name, string version)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return System.Text.Json.JsonSerializer.Deserialize<WorkflowPackageManifest>(
            Blobs[$"{name}/{version}/manifest.json"], options)!;
    }
}

internal sealed class FakePackageStore : IWorkflowPackageStore
{
    private readonly string _knownRef;
    private readonly WorkflowPackageManifest _manifest;
    private readonly Dictionary<string, string> _files;

    public FakePackageStore(
        string knownRef,
        WorkflowPackageManifest? manifest = null,
        Dictionary<string, string>? files = null)
    {
        _knownRef = knownRef;
        _manifest = manifest ?? V5Fixtures.Manifest();
        _files = files ?? V5Fixtures.Files(_manifest);
    }

    public Task<WorkflowPackage> ResolveAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken)
    {
        if (!string.Equals(packageRef.ToString(), _knownRef, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Workflow package blob '{packageRef}' was not found (fake).");
        }

        // Resolve the data table as the real store does. It used to come back
        // null, which made the parent unusable for anything that compares a
        // fork against its origin (#310).
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(_manifest, _files, errors);

        return Task.FromResult(new WorkflowPackage(_manifest, Data: data, SourceFiles: _files));
    }
}

internal sealed class FakeSettingsStore : IAccountSettingsStore
{
    private readonly Dictionary<(string AppUserId, string Key), AccountSetting> _settings = new();

    public Task<AccountSetting?> GetAsync(string appUserId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(_settings.TryGetValue((appUserId, key), out var setting) ? setting : null);

    public Task SaveAsync(string appUserId, string key, string value, string contentType, CancellationToken cancellationToken)
    {
        _settings[(appUserId, key)] = new AccountSetting(key, value, contentType, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string appUserId, string key, CancellationToken cancellationToken)
    {
        _settings.Remove((appUserId, key));
        return Task.CompletedTask;
    }
}

public class ContainerRoutingTests
{
    private static WorkflowPackageBlobContainerFactory CreateFactory(bool withPublicUri)
    {
        var settings = new Dictionary<string, string?>
        {
            ["WorkflowPackages:ConnectionStringName"] = "TestRegistryConnection",
            ["TestRegistryConnection"] = "UseDevelopmentStorage=true"
        };

        if (withPublicUri)
        {
            settings["WorkflowPackages:PublicBlobServiceUri"] = "https://public.example.net";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new WorkflowPackageBlobContainerFactory(
            configuration,
            NSubstitute.Substitute.For<Azure.Core.TokenCredential>(),
            NullLogger<WorkflowPackageBlobContainerFactory>.Instance);
    }

    [Fact]
    public void RepoOwnedName_RoutesToThePublicContainer()
    {
        var factory = CreateFactory(withPublicUri: true);
        Assert.Equal("public.example.net", factory.GetContainerFor("general").Uri.Host);
    }

    [Fact]
    public void AccountName_RoutesToThePrivateContainer_EvenWithPublicConfigured()
    {
        var factory = CreateFactory(withPublicUri: true);
        Assert.NotEqual("public.example.net", factory.GetContainerFor("acct-0123456789ab").Uri.Host);
    }

    [Fact]
    public void NoPublicUri_EverythingRoutesPrivate()
    {
        var factory = CreateFactory(withPublicUri: false);
        Assert.Equal(factory.GetContainer().Uri, factory.GetContainerFor("general").Uri);
    }

    [Fact]
    public void TheWriterTarget_IsAlwaysThePrivateContainer()
    {
        var factory = CreateFactory(withPublicUri: true);
        Assert.NotEqual("public.example.net", factory.GetContainer().Uri.Host);
    }
}

public class PublicChainAssembleTests
{
    private static readonly IReadOnlyList<string> PackageBlobs = new[]
    {
        "general/latest.json",
        "general/v2026.07.1/manifest.json",
        "general/v2026.07.6/manifest.json",
        "general/v2026.07.6/prompts/identify-problem.md",
        "general/v2026.07.10/manifest.json"
    };

    private static readonly IReadOnlyList<string> ContractBlobs = new[]
    {
        "latest.json",
        "v2026.07.1/output-contracts.json",
        "v2026.07.1/schemas/concept-list.json"
    };

    private static readonly IReadOnlyList<string> AgentBlobs = new[]
    {
        "test-json/latest.json",
        "test-json/47/definition.yaml",
        "concept-extraction/latest.json",
        "concept-extraction/1/definition.yaml"
    };

    private static readonly IReadOnlyDictionary<string, string> SmallFiles = new Dictionary<string, string>
    {
        ["workflow-packages/general/latest.json"] = """{"version": "v2026.07.10"}""",
        ["output-contracts/latest.json"] = """{"version": "v2026.07.1"}""",
        ["output-contracts/v2026.07.1/output-contracts.json"] =
            """{"version": "v2026.07.1", "contracts": {"text": {"agentName": "test-json", "agentVersion": "47"}, "concept-list": {"agentName": "concept-extraction", "agentVersion": "1", "schemaFile": "schemas/concept-list.json"}}}""",
        ["agent-definitions/test-json/latest.json"] = """{"version": "47"}""",
        ["agent-definitions/concept-extraction/latest.json"] = """{"version": "1"}"""
    };

    [Fact]
    public void Assemble_BuildsTheWholeChainView()
    {
        var chain = PublicRegistryReader.Assemble(PackageBlobs, ContractBlobs, AgentBlobs, SmallFiles, DateTimeOffset.UnixEpoch);

        var package = Assert.Single(chain.Packages);
        Assert.Equal("general", package.Name);
        Assert.Equal("v2026.07.10", package.Latest);
        // CalVer-numeric ordering, not lexicographic: .10 sorts after .6.
        Assert.Equal(new[] { "v2026.07.1", "v2026.07.6", "v2026.07.10" }, package.Versions);

        Assert.NotNull(chain.OutputContracts);
        Assert.Equal("v2026.07.1", chain.OutputContracts!.Latest);
        Assert.Equal(new[] { "v2026.07.1" }, chain.OutputContracts.Versions);
        Assert.Equal("test-json", chain.OutputContracts.Contracts!["text"].AgentName);
        Assert.False(chain.OutputContracts.Contracts["text"].HasSchema);
        Assert.True(chain.OutputContracts.Contracts["concept-list"].HasSchema);

        Assert.Equal(2, chain.AgentDefinitions.Count);
        var conceptAgent = chain.AgentDefinitions.First(a => a.Name == "concept-extraction");
        Assert.Equal("1", conceptAgent.Latest);
        Assert.Equal(new[] { "1" }, conceptAgent.Versions);
    }

    [Fact]
    public void Assemble_EmptyRegistries_ProducesEmptyView()
    {
        var chain = PublicRegistryReader.Assemble(
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);

        Assert.Empty(chain.Packages);
        Assert.Null(chain.OutputContracts);
        Assert.Empty(chain.AgentDefinitions);
    }

    [Fact]
    public void Assemble_UnparseablePointerOrCatalog_DegradesGracefully()
    {
        var files = new Dictionary<string, string>
        {
            ["workflow-packages/general/latest.json"] = "not json",
            ["output-contracts/latest.json"] = """{"version": "v2026.07.1"}""",
            ["output-contracts/v2026.07.1/output-contracts.json"] = "also not json"
        };

        var chain = PublicRegistryReader.Assemble(PackageBlobs, ContractBlobs, Array.Empty<string>(), files, DateTimeOffset.UnixEpoch);

        Assert.Null(chain.Packages[0].Latest);
        Assert.NotNull(chain.OutputContracts);
        Assert.Null(chain.OutputContracts!.Contracts);
    }
}

public class WorkflowPackageLineageTests
{
    private static WorkflowPackageRef Ref(string value)
    {
        Assert.True(WorkflowPackageRef.TryParse(value, out var parsed));
        return parsed!;
    }

    private static Func<WorkflowPackageRef, Task<string?>> Reader(Dictionary<string, string?> derivedFrom) =>
        reference => derivedFrom.TryGetValue(reference.ToString(), out var parent)
            ? Task.FromResult(parent)
            : throw new InvalidOperationException($"Workflow package '{reference}' was not found in the registry.");

    [Fact]
    public async Task Walk_RootPackage_SingleElementChain()
    {
        var chain = await WorkflowPackageLineageResolver.WalkAsync(
            Ref("general@v2026.07.6"),
            Reader(new Dictionary<string, string?> { ["general@v2026.07.6"] = null }));

        Assert.Equal(new[] { "general@v2026.07.6" }, chain);
    }

    [Fact]
    public async Task Walk_ForkToRoot_OrderedChain()
    {
        var chain = await WorkflowPackageLineageResolver.WalkAsync(
            Ref("acct-0123456789ab@v2026.07.2"),
            Reader(new Dictionary<string, string?>
            {
                ["acct-0123456789ab@v2026.07.2"] = "acct-0123456789ab@v2026.07.1",
                ["acct-0123456789ab@v2026.07.1"] = "general@v2026.07.6",
                ["general@v2026.07.6"] = null
            }));

        Assert.Equal(
            new[] { "acct-0123456789ab@v2026.07.2", "acct-0123456789ab@v2026.07.1", "general@v2026.07.6" },
            chain);
    }

    [Fact]
    public async Task Walk_LatestStart_Rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            WorkflowPackageLineageResolver.WalkAsync(Ref("general@latest"), Reader(new())));
    }

    [Fact]
    public async Task Walk_Cycle_Trips()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowPackageLineageResolver.WalkAsync(
                Ref("a@v2026.07.1"),
                Reader(new Dictionary<string, string?>
                {
                    ["a@v2026.07.1"] = "b@v2026.07.1",
                    ["b@v2026.07.1"] = "a@v2026.07.1"
                })));

        Assert.Contains("cycle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Walk_DepthCap_Trips()
    {
        var derivedFrom = new Dictionary<string, string?>();
        for (var i = 1; i <= WorkflowPackageLineageResolver.MaxDepth + 2; i++)
        {
            derivedFrom[$"p@v2026.07.{i}"] = $"p@v2026.07.{i + 1}";
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowPackageLineageResolver.WalkAsync(Ref("p@v2026.07.1"), Reader(derivedFrom)));

        Assert.Contains("depth cap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Walk_InvalidDerivedFrom_FailsLoud()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowPackageLineageResolver.WalkAsync(
                Ref("a@v2026.07.1"),
                Reader(new Dictionary<string, string?> { ["a@v2026.07.1"] = "general@latest" })));

        Assert.Contains("invalid derivedFrom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Walk_MissingPackage_SurfacesTheRef()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowPackageLineageResolver.WalkAsync(
                Ref("a@v2026.07.1"),
                Reader(new Dictionary<string, string?> { ["a@v2026.07.1"] = "gone@v2026.07.1" })));

        Assert.Contains("gone@v2026.07.1", ex.Message, StringComparison.Ordinal);
    }
}

public class AccountPackageListingTests
{
    [Fact]
    public void Build_ParsesVersionsAndLatestPointer()
    {
        var summary = AccountPackageListing.Build(
            "acct-7bca2dcc1ed4",
            new[]
            {
                "acct-7bca2dcc1ed4/v2026.07.1/manifest.json",
                "acct-7bca2dcc1ed4/v2026.07.1/prompts/section.md",
                "acct-7bca2dcc1ed4/v2026.07.2/manifest.json",
                "acct-7bca2dcc1ed4/latest.json"
            },
            """{"version":"v2026.07.2"}""");

        Assert.Equal("acct-7bca2dcc1ed4", summary.Name);
        Assert.Equal("v2026.07.2", summary.Latest);
        Assert.Equal(new[] { "v2026.07.1", "v2026.07.2" }, summary.Versions);
    }

    [Fact]
    public void Build_NoBlobs_IsAnEmptySummary()
    {
        var summary = AccountPackageListing.Build("acct-7bca2dcc1ed4", Array.Empty<string>(), null);

        Assert.Null(summary.Latest);
        Assert.Empty(summary.Versions);
    }

    [Fact]
    public void Build_MalformedPointer_DegradesToNoLatest()
    {
        var summary = AccountPackageListing.Build(
            "acct-7bca2dcc1ed4",
            new[] { "acct-7bca2dcc1ed4/v2026.07.1/manifest.json" },
            "{not json");

        Assert.Null(summary.Latest);
        Assert.Equal(new[] { "v2026.07.1" }, summary.Versions);
    }

    [Fact]
    public void Build_IgnoresForeignAndNonManifestBlobs()
    {
        var summary = AccountPackageListing.Build(
            "acct-7bca2dcc1ed4",
            new[]
            {
                "acct-000000000000/v2026.07.9/manifest.json",
                "acct-7bca2dcc1ed4/v2026.07.1/data/standards/index.json",
                "acct-7bca2dcc1ed4/v2026.07.1/manifest.json"
            },
            null);

        Assert.Equal(new[] { "v2026.07.1" }, summary.Versions);
    }
}

public class AccountPackageListingSpecTests
{
    [Fact]
    public void Build_CarriesSpecVersions()
    {
        var summary = AccountPackageListing.Build(
            "acct-7bca2dcc1ed4",
            new[] { "acct-7bca2dcc1ed4/v2026.07.1/manifest.json" },
            null,
            new Dictionary<string, int> { ["v2026.07.1"] = 5 });

        Assert.NotNull(summary.SpecVersions);
        Assert.Equal(5, summary.SpecVersions!["v2026.07.1"]);
    }

    [Theory]
    [InlineData("""{"specVersion":5}""", 5)]
    [InlineData("""{"specVersion":2,"prompts":[]}""", 2)]
    [InlineData("""{"name":"x"}""", null)]
    [InlineData("""{"specVersion":"five"}""", null)]
    [InlineData("{not json", null)]
    public void ReadSpecVersion_ParsesOrDegrades(string manifestJson, int? expected)
    {
        Assert.Equal(expected, AccountPackageListing.ReadSpecVersion(manifestJson));
    }

    [Fact]
    public void Assemble_MapsSpecVersionsPerPackage()
    {
        var chain = PublicRegistryReader.Assemble(
            new[]
            {
                "general/v2026.07.3/manifest.json",
                "general/v2026.07.6/manifest.json"
            },
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, int>
            {
                ["general/v2026.07.3"] = 2,
                ["general/v2026.07.6"] = 5
            });

        var package = Assert.Single(chain.Packages);
        Assert.Equal(2, package.SpecVersions!["v2026.07.3"]);
        Assert.Equal(5, package.SpecVersions!["v2026.07.6"]);
    }

    [Fact]
    public void Assemble_WithoutSpecVersions_LeavesSummariesNull()
    {
        var chain = PublicRegistryReader.Assemble(
            new[] { "general/v2026.07.6/manifest.json" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        Assert.Null(Assert.Single(chain.Packages).SpecVersions);
    }
}
