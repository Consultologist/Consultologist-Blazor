using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.Api.Tests.V31Source;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1497 (specVersion 31, area 00): the contracts of 31 are published as a preview in the three
/// content repos, vendored here as submodules. These hold what they publish against the design
/// and hold the engine at 30 to its own sets: nothing in a 31 contract is something the engine at
/// 30 reads, and the engine at 30 does not yet accept 31.
///
/// Read off the submodules rather than fetched on purpose, as in <see cref="SpecVersionSetTests"/>.
/// </summary>
public class V31ContractsTests
{
    private static readonly string[] SchemaNames =
    [
        "agents", "bindings", "bounds", "check", "common", "conformance-index", "constants",
        "deliverable", "deliverables-index", "flow-case", "flow-for", "flow-if", "flow-while",
        "function-index", "index", "lock", "macro-folder-index", "macro", "manifest",
        "migration-report", "node-agent", "node-call", "node-computed", "node-rendered",
        "profile", "publish-report", "renderer", "resolve", "sentences", "snippet-folder-index",
        "snippet", "test",
    ];

    private static readonly string[] BuiltInFormats =
    [
        "string", "number", "boolean", "date", "plaintext", "markdown_block", "markdown_inline",
        "check_result", "judgement",
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string Existing(string path)
    {
        Assert.True(
            File.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init).");
        return path;
    }

    private static JsonNode Json(string path) => JsonNode.Parse(File.ReadAllText(Existing(path)))!;

    private static string PackageFormat(params string[] parts) =>
        Path.Combine([RepoRoot(), "external", "consultologist-package-format", .. parts]);

    private static string Agents(params string[] parts) =>
        Path.Combine([RepoRoot(), "external", "consultologist-agents", .. parts]);

    private static string Provenance(params string[] parts) =>
        Path.Combine([RepoRoot(), "external", "consultologist-provenance", .. parts]);

    private static string[] Strings(JsonNode? array) => array!.AsArray().Select(v => (string)v!).ToArray();

    private static int[] Ints(JsonNode? array) => array!.AsArray().Select(v => (int)v!).ToArray();

    [Fact]
    public void TheEngineAt30_StillSeesItsOwnSets()
    {
        var specVersions = Json(PackageFormat("spec-versions.json"));
        Assert.DoesNotContain(31, Ints(specVersions["supported"]));
        Assert.False(specVersions["documents"]!.AsObject().ContainsKey("31"));

        var document = (string)specVersions["preview"]!["31"]!["document"]!;
        Assert.Equal("package-format-v31.md", document);
        Assert.Contains("specVersion 31", File.ReadAllText(Existing(PackageFormat(document))));

        var provenance = Json(Provenance("provenance-versions.json"));
        Assert.DoesNotContain(12, Ints(provenance["recordStorageVersions"]));

        var preview = provenance["preview"]!;
        Assert.Equal([12], Ints(preview["recordStorageVersions"]));
        Assert.Equal([7], Ints(preview["nodeHashVersions"]));
        Assert.Equal([7], Ints(preview["effectiveInputHashVersions"]));
        Assert.Equal([4], Ints(preview["workflowOutputHashVersions"]));

        Assert.DoesNotContain(31, WorkflowPackageStore.SupportedSpecVersions);
        Assert.DoesNotContain(31, WorkflowPackageValidator.AcceptedSpecVersions);
    }

    [Fact]
    public void Bounds_AreTheDesignsNumbers()
    {
        var bounds = Json(PackageFormat("v31", "bounds.json"));

        var source = bounds["sourceBounds"]!;
        Assert.Equal(512, (int)source["files"]!);
        Assert.Equal(262144, (int)source["fileBytes"]!);
        Assert.Equal(2097152, (int)source["totalBytes"]!);
        Assert.Equal(200, (int)source["pathLength"]!);
        Assert.Equal(64, (int)source["idLength"]!);
        Assert.Equal(32, (int)source["jsonDepth"]!);
        Assert.Equal(1000, (int)source["expressionNodes"]!);

        var format = bounds["formatBounds"]!;
        Assert.Equal(15, (int)format["passes"]!);
        Assert.Equal(15, (int)format["depth"]!);
        Assert.Equal(15, (int)format["parallel"]!);

        Assert.Equal(22, bounds["windowsReservedIds"]!.AsArray().Count);

        var reserved = Strings(bounds["reservedIds"]);
        Assert.Equal(36, reserved.Length);
        Assert.Contains("parts", reserved);
        Assert.Contains("value", reserved);
    }

    [Fact]
    public void EverySentenceOfTheDesign_IsPublishedWordForWord()
    {
        var table = Path.Combine(
            RepoRoot(), "docs", "customizable-workflow", "package-format-v31", "refusal-sentences.md");

        // The cell is *"…"* with <, >, | and * written as escapes and a backslash before
        // punctuation; the first two and last two characters are the markers (render-sentences.py).
        var cellSplit = new Regex(@"(?<!\\)\|");
        var unescape = new Regex(@"\\([!-/:-@\[-`{-~])");
        var designed = File.ReadAllLines(Existing(table))
            .Where(line => Regex.IsMatch(line, @"^\| S\d\d-\d+ \|"))
            .Select(line => cellSplit.Split(line).Skip(1).SkipLast(1).Select(c => c.Trim()).ToArray())
            .Select(cells => (Id: cells[0], Sentence: unescape.Replace(cells[2][2..^2], "$1"), Audience: cells[3]))
            .ToList();

        var published = Json(PackageFormat("v31", "sentences.json"))["sentences"]!.AsArray()
            .Select(s => (Id: (string)s!["id"]!, Sentence: (string)s["sentence"]!, Audience: (string)s["audience"]!))
            .ToList();

        Assert.NotEmpty(designed);
        Assert.Equal(designed, published);
    }

    [Fact]
    public void EveryFileKind_HasItsSchema()
    {
        foreach (var name in SchemaNames)
        {
            var schema = Json(PackageFormat("v31", "schemas", name + ".schema.json"));
            Assert.Equal($"urn:consultologist:spec:31:schema:{name}", (string)schema["$id"]!);
        }
    }

    [Fact]
    public void BuiltInFormats_AreTheNine_InTheDialect()
    {
        var index = Json(PackageFormat("v31", "formats", "index.json"));
        Assert.Equal(BuiltInFormats, Strings(index["items"]));

        foreach (var id in BuiltInFormats)
        {
            var format = Json(PackageFormat("v31", "formats", id + ".json"));
            Assert.Equal("urn:consultologist:spec:31:dialect", (string)format["$schema"]!);
            Assert.Equal($"urn:consultologist:spec:31:format:{id}", (string)format["$id"]!);
        }

        Assert.Equal(
            ["answer", "reason"],
            Strings(Json(PackageFormat("v31", "formats", "judgement.json"))["required"]));
        Assert.Equal(
            ["pass", "message", "uncovered", "untested", "passed", "total"],
            Strings(Json(PackageFormat("v31", "formats", "check_result.json"))["required"]));
    }

    [Fact]
    public void EveryBinding_NamesAFormatAndAnAttestedAgentVersion()
    {
        var bindings = Json(Agents("agents", "v31", "bindings.json")).AsObject();
        var catalogue = Strings(Json(Agents("agents", "v31", "formats", "index.json"))["items"]);

        var bound = bindings["formats"]!.AsObject();
        Assert.NotEmpty(bound);

        var generated = new List<(string Name, JsonNode Binding)>();
        foreach (var (format, binding) in bound)
        {
            Assert.True(
                BuiltInFormats.Contains(format) || catalogue.Contains(format),
                $"Binding '{format}' is neither a built-in format nor a file in agents/v31/formats.");
            if (catalogue.Contains(format))
            {
                Existing(Agents("agents", "v31", "formats", format + ".json"));
            }

            generated.Add((format, binding!));
        }

        generated.Add(("custom", bindings["custom"]!));

        foreach (var (name, binding) in generated)
        {
            Assert.False(binding.AsObject().ContainsKey("via"), $"Binding '{name}' writes via.");
            var agent = binding["generated"]!;
            var agentName = (string)agent["agentName"]!;
            var manifest = AttestedAgentManifest.Load(File.ReadAllText(Existing(Agents("agents", agentName + ".yaml"))));
            Assert.Equal(((int)agent["agentVersion"]!).ToString(), manifest.Version);
        }
    }

    [Fact]
    public void TheCatalogue_IsTheSchemasExamples()
    {
        // package-format's examples of the bindings and agents schemas are copies of the
        // catalogue's files, so the catalogue is shown valid by the schemas' own check.
        var examples = PackageFormat("v31", "conformance", "schemas");
        foreach (var (catalogueFile, example) in new[]
                 {
                     ("bindings.json", Path.Combine(examples, "bindings", "valid-agents-catalogue-bindings.json")),
                     ("agents.json", Path.Combine(examples, "agents", "valid-attested-agents.json")),
                 })
        {
            Assert.True(
                JsonNode.DeepEquals(Json(Agents("agents", "v31", catalogueFile)), Json(example)["instance"]),
                $"agents/v31/{catalogueFile} is not the instance of {Path.GetFileName(example)}.");
        }
    }

    [Fact]
    public void TheJudgementAgent_EmbedsWhatSentFormatPrepares()
    {
        // #1504 ruling 1 (R1): the copy of a format a model receives joins descriptions with a newline,
        // and the attested agent embeds exactly that copy.
        var manifest = AttestedAgentManifest.Load(File.ReadAllText(Existing(Agents("agents", "judgement.yaml"))));
        Assert.NotNull(manifest.Definition.Text);
        Assert.NotNull(manifest.Definition.Text.Format);
        Assert.Equal("judgement", manifest.Definition.Text.Format.Name);
        Assert.NotNull(manifest.Definition.Text.Format.Schema);
        var embedded = JsonNode.Parse(manifest.Definition.Text.Format.Schema)!;

        var prepared = SentFormat.Prepare("judgement", FormatSet.ForPackage(V31Contract.PublishedTypes(), []), ["format"]);
        Assert.Equal(
            CanonicalJson.Write(prepared.Schema),
            CanonicalJson.Write(embedded));
        Assert.Equal(
            "Plain text, with no markup.\nWhy, in a sentence or two, from the text given.",
            (string)embedded["properties"]!["reason"]!["description"]!);
        Assert.Equal(["answer", "reason"], embedded["properties"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal(["answer", "reason"], Strings(embedded["required"]));
    }

    [Fact]
    public void ConceptList_CarriesSource()
    {
        // #1504 ruling 3 (R3): concept_context reads each concept's `source`, so the format has it.
        var concept = Json(Agents("agents", "v31", "formats", "concept_list.json"))["properties"]!["concepts"]!["items"]!;
        Assert.Contains("source", Strings(concept["required"]));
        Assert.Equal("string", (string)concept["properties"]!["source"]!["type"]!);

        var renderer = File.ReadAllText(Existing(PackageFormat("v31", "renderers", "concept_context.md")));
        var read = Regex.Matches(renderer, @"\bc\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(read);
        var has = concept["properties"]!.AsObject().Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(read, field => Assert.Contains(field, has));
    }

    [Fact]
    public void The30Catalogue_IsUntouchedBesideThe31One()
    {
        var formats = Json(Agents("agents", "formats.json"))["formats"]!.AsObject();
        Assert.Equal(
            ["classification", "concept_list", "prose", "rendered", "terms_subset"],
            formats.Select(f => f.Key).Order(StringComparer.Ordinal).ToArray());
    }
}
