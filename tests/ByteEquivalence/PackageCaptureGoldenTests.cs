using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: what today's engine (specVersion 30) does with each public package, frozen. Every
/// <c>scenarios/&lt;package&gt;/&lt;scenario&gt;.json</c> is run by <see cref="CaptureRun"/> and its
/// capture document must equal <c>captures/&lt;package&gt;/&lt;scenario&gt;.json</c> byte for byte; a
/// missing capture is written and fails once, to be read and committed. The coverage rules hold
/// the scenarios to their purpose: every deliverable is composed, and every agent node's prompt
/// is captured, in some scenario of its package — or the exception tables say why not.
/// </summary>
public class PackageCaptureGoldenTests
{
    /// <summary>A deliverable no scenario of its package can compose on today's engine, and why.</summary>
    private static readonly IReadOnlyDictionary<(string Package, string Deliverable), string> NeverComposed =
        new Dictionary<(string Package, string Deliverable), string>();

    /// <summary>A prompt node no scenario of its package can reach on today's engine, and why.</summary>
    private static readonly IReadOnlyDictionary<(string Package, string Node), string> NeverPrompted =
        new Dictionary<(string Package, string Node), string>();

    private static string ScenarioRoot => Path.Combine(ByteEquivalenceFixtures.Root, "scenarios");

    /// <summary>Every <c>(package, scenario)</c> under <c>scenarios/</c>; a package name may hold <c>/</c>.</summary>
    public static TheoryData<string, string> Scenarios()
    {
        var rows = new TheoryData<string, string>();

        foreach (var (package, scenario) in ScenarioFiles())
        {
            rows.Add(package, scenario);
        }

        return rows;
    }

    /// <summary>Every package that has at least one scenario.</summary>
    public static TheoryData<string> Packages()
    {
        var rows = new TheoryData<string>();

        foreach (var package in ScenarioFiles().Select(file => file.Package).Distinct(StringComparer.Ordinal))
        {
            rows.Add(package);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task AScenario_RunsToTheCapturedBytes(string package, string scenario)
    {
        var loaded = ByteEquivalenceFixtures.Load(package);
        var read = CaptureScenario.Read(ByteEquivalenceFixtures.ReadText(ScenarioPath(package, scenario)));
        var tag = ByteEquivalenceFixtures.Vendored.FirstOrDefault(vendored => vendored.Name == package)?.Tag;

        var run = await CaptureRun.StartAsync(loaded, read);

        ByteEquivalenceFixtures.Matches($"captures/{package}/{scenario}.json", PackageCapture.Write(package, tag, scenario, run));
    }

    [Fact]
    public void AMissingCapture_IsWritten_AndFailsTheTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "byte-equivalence-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string relative = "captures/test/general/default.json";
            var path = Path.Combine(root, "captures", "test", "general", "default.json");

            var wrote = Assert.ThrowsAny<Exception>(() => ByteEquivalenceFixtures.Matches(relative, "{ \"first\": 1 }\n", root));
            Assert.Equal($"Wrote {path}; review it and commit it.", wrote.Message);
            Assert.Equal("{ \"first\": 1 }\n", ByteEquivalenceFixtures.ReadText(path));

            ByteEquivalenceFixtures.Matches(relative, "{ \"first\": 1 }\n", root);

            Assert.ThrowsAny<Exception>(() => ByteEquivalenceFixtures.Matches(relative, "{ \"first\": 2 }\n", root));
            Assert.Equal("{ \"first\": 1 }\n", ByteEquivalenceFixtures.ReadText(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EveryCapture_HasItsScenario()
    {
        var captures = Path.Combine(ByteEquivalenceFixtures.Root, "captures");
        var orphans = (Directory.Exists(captures) ? Directory.EnumerateFiles(captures, "*.json", SearchOption.AllDirectories) : [])
            .Select(path => Path.GetRelativePath(captures, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !File.Exists(Path.Combine([ScenarioRoot, .. relative.Split('/')])))
            .ToList();

        Assert.Empty(orphans);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void EveryDeliverable_IsComposedInSomeScenario(string package)
    {
        var captures = CapturesOf(package);
        var composed = captures
            .SelectMany(capture => capture["deliverables"]!.AsArray())
            .Where(deliverable => deliverable!["state"]!.GetValue<string>() == "composed")
            .Select(deliverable => deliverable!["id"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        var never = ByteEquivalenceFixtures.Load(package).Manifest.EffectiveDeliverables
            .Select(deliverable => deliverable.Id)
            .Where(id => !composed.Contains(id) && !NeverComposed.ContainsKey((package, id)))
            .ToList();

        Assert.Empty(never);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void EveryAgentNode_HasACapturedPrompt(string package)
    {
        var captures = CapturesOf(package);
        var prompted = captures
            .SelectMany(capture => capture["prompts"]!.AsArray().Concat(capture["renders"]!.AsArray()))
            .Select(entry => WorkflowItemPath.NodeOf(entry!["key"]!.GetValue<string>()))
            .ToHashSet(StringComparer.Ordinal);

        var never = (ByteEquivalenceFixtures.Load(package).Manifest.Nodes ?? [])
            .Where(node => node.Prompt is not null)
            .Select(node => node.Id)
            .Where(id => !prompted.Contains(id) && !NeverPrompted.ContainsKey((package, id)))
            .ToList();

        Assert.Empty(never);
    }

    /// <summary>The package's captures, one per scenario; a scenario not yet captured fails the rule.</summary>
    private static List<JsonNode> CapturesOf(string package)
    {
        var scenarios = ScenarioFiles().Where(file => file.Package == package).ToList();
        Assert.NotEmpty(scenarios);

        return scenarios
            .Select(file => Path.Combine([ByteEquivalenceFixtures.Root, "captures", .. package.Split('/'), file.Scenario + ".json"]))
            .Select(path => File.Exists(path)
                ? JsonNode.Parse(ByteEquivalenceFixtures.ReadText(path))!
                : throw new FileNotFoundException($"No capture {path}; run AScenario_RunsToTheCapturedBytes first."))
            .ToList();
    }

    private static string ScenarioPath(string package, string scenario) =>
        Path.Combine([ScenarioRoot, .. package.Split('/'), scenario + ".json"]);

    private static IEnumerable<(string Package, string Scenario)> ScenarioFiles() =>
        (Directory.Exists(ScenarioRoot) ? Directory.EnumerateFiles(ScenarioRoot, "*.json", SearchOption.AllDirectories) : [])
            .Select(path => Path.GetRelativePath(ScenarioRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Select(relative => (Package: relative[..relative.LastIndexOf('/')], Scenario: Path.GetFileNameWithoutExtension(relative)))
            .OrderBy(file => file.Package, StringComparer.Ordinal)
            .ThenBy(file => file.Scenario, StringComparer.Ordinal);
}
