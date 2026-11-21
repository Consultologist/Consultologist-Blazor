using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): what an engine checks before a job (part 07 §§ 3, 4.2, 4.4, § 10 "At start"; D10):
/// <c>requires</c> against what it runs, <c>lock.json</c>'s exact versions against its break points (S07-003 to
/// S07-005), and <c>needs.profile</c> against the account's profile (the two profile sentences). Pure functions: a
/// document missing a member it reads is refused, never thrown on.
/// </summary>
public class RequiresCheckTests
{
    private static readonly string[] AllKinds = ["agent", "rendered", "computed", "for", "while", "if", "case", "call"];

    private static readonly string[] AllFeatures = ["parallel", "macro-markers", "parts", "run-spots", "check-loops"];

    private static readonly Lazy<(JsonObject Resolve, JsonObject Lock)> Pair = new(() =>
    {
        var (_, compiled) = CompiledPackage.Publish(
            ResolveFixture.NestedNote().WithJson("manifest.json", m => m["name"] = "example-nested-note"),
            ResolveFixture.Published(),
            LockCompilerTests.Example);
        return (JsonNode.Parse(compiled!.Resolve)!.AsObject(), JsonNode.Parse(compiled.Lock)!.AsObject());
    });

    private static JsonObject Resolve() => Pair.Value.Resolve.DeepClone().AsObject();

    private static JsonObject Lock() => Pair.Value.Lock.DeepClone().AsObject();

    /// <summary>An engine that runs everything the example needs: Scriban <paramref name="scriban"/>, every kind and feature, its agents.</summary>
    private static EngineCapabilities Engine(
        string scriban = "7.2.6",
        IReadOnlyList<BreakPoint>? breakPoints = null,
        IEnumerable<string>? kinds = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? agents = null,
        IReadOnlyDictionary<string, string>? libraries = null)
    {
        var required = Pair.Value.Resolve["requires"]!["agents"]!.AsObject()
            .ToDictionary(
                a => a.Key,
                a => (IReadOnlySet<int>)new HashSet<int> { int.Parse((string)a.Value!, CultureInfo.InvariantCulture) },
                StringComparer.Ordinal);
        return new EngineCapabilities(
            "1.0.0",
            scriban,
            JsonLogicOperations.All.Where(o => o.Custom).ToDictionary(o => o.Name, _ => "1.0.0", StringComparer.Ordinal),
            (kinds ?? AllKinds).ToHashSet(StringComparer.Ordinal),
            AllFeatures.ToHashSet(StringComparer.Ordinal),
            agents ?? required,
            breakPoints ?? [],
            libraries ?? Pair.Value.Lock["libraries"]!.AsObject().ToDictionary(l => l.Key, l => (string)l.Value!, StringComparer.Ordinal));
    }

    private static IReadOnlyList<SourceRefusal> Said(IReadOnlyList<SourceRefusal> refusals)
    {
        Assert.All(refusals, V31Contract.AssertSentence);
        return refusals;
    }

    [Fact]
    public void AnEngineInRange_AdmitsTheLock()
    {
        Assert.Equal("7.2.5", (string)Lock()["libraries"]!["Scriban"]!);
        Assert.Empty(RequiresCheck.Engine(Resolve(), Lock(), Engine("7.2.6")));
        Assert.Empty(RequiresCheck.Engine(Resolve(), Lock(), Engine("7.2.5")));
    }

    [Fact]
    public void AnEngineOutOfRange_IsRefused()
    {
        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), Engine("8.0.0"))));
        Assert.Equal("S07-003", refusal.Id);
        Assert.Equal(
            "This workflow (example-nested-note@v2026.12.1) needs Scriban ^7.2.5; this engine runs Scriban 8.0.0. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusal.Sentence);
    }

    [Fact]
    public void ALockBeforeABreakPoint_IsRefused()
    {
        var breakPoints = new[] { new BreakPoint("Scriban", "7.3.0") };

        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), Engine("7.3.1", breakPoints))));
        Assert.Equal("S07-003", refusal.Id);
        Assert.Equal(
            "This workflow (example-nested-note@v2026.12.1) needs Scriban ^7.2.5; this engine runs Scriban 7.3.1. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusal.Sentence);

        // An engine still before the break point, or a lock at or after it, is not refused for it.
        Assert.Empty(RequiresCheck.Engine(Resolve(), Lock(), Engine("7.2.9", breakPoints)));
        var after = Lock();
        after["libraries"]!["Scriban"] = "7.3.0";
        Assert.Empty(RequiresCheck.Engine(Resolve(), after, Engine("7.3.1", breakPoints)));
    }

    [Fact]
    public void BreakPoints_OfTheEvaluatorAndAnOperation_AreHeld()
    {
        var engine = Engine(breakPoints: [new BreakPoint("the evaluator", "1.0.1"), new BreakPoint("size", "1.1.0"), new BreakPoint("matches", "1.1.0")]);
        var refusals = Said(RequiresCheck.Engine(Resolve(), Lock(), engine with
        {
            Evaluator = "1.0.1",
            Operations = engine.Operations.ToDictionary(o => o.Key, o => o.Key is "size" or "matches" ? "1.1.0" : o.Value, StringComparer.Ordinal),
        }));

        // matches is not used, so its break point is not this package's.
        Assert.Equal(["S07-003", "S07-003"], refusals.Select(r => r.Id));
        Assert.Contains("needs the evaluator ^1.0.0; this engine runs the evaluator 1.0.1.", refusals[0].Sentence);
        Assert.Contains("needs size ^1.0.0; this engine runs size 1.1.0.", refusals[1].Sentence);
    }

    [Fact]
    public void ABreakPointOfALibrary_StatesTheEnginesVersionOfIt()
    {
        // The lock is at Markdig 0.41.3 and Corvus.JsonSchema 4.3.17 (LockCompilerTests.Example).
        var breakPoints = new[] { new BreakPoint("Markdig", "0.42.0"), new BreakPoint("Corvus.JsonSchema", "5.0.0") };
        var libraries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Corvus.JsonSchema"] = "5.8.12",
            ["Corvus.Text.Json.JsonLogic"] = "5.8.11",
            ["Markdig"] = "0.42.1",
            ["Scriban"] = "7.2.6",
        };

        var refusals = Said(RequiresCheck.Engine(Resolve(), Lock(), Engine(breakPoints: breakPoints, libraries: libraries)));
        Assert.Equal(["S07-003", "S07-003"], refusals.Select(r => r.Id));
        Assert.Equal(
            "This workflow (example-nested-note@v2026.12.1) needs Corvus.JsonSchema ^4.3.17; this engine runs Corvus.JsonSchema 5.8.12. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusals[0].Sentence);
        Assert.Equal(
            "This workflow (example-nested-note@v2026.12.1) needs Markdig ^0.41.3; this engine runs Markdig 0.42.1. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusals[1].Sentence);
        Assert.All(refusals, r => Assert.Equal("lock.json", r.Path));

        // An engine still before a break point is not refused for it; one that does not say its version of a library
        // with a break point is refused, naming its version unknown.
        var before = new Dictionary<string, string>(libraries, StringComparer.Ordinal) { ["Markdig"] = "0.41.9", ["Corvus.JsonSchema"] = "4.9.0" };
        Assert.Empty(RequiresCheck.Engine(Resolve(), Lock(), Engine(breakPoints: breakPoints, libraries: before)));
        var unsaid = libraries.Where(l => l.Key != "Markdig").ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        unsaid["Corvus.JsonSchema"] = "4.9.0";
        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), Engine(breakPoints: breakPoints, libraries: unsaid))));
        Assert.Contains("needs Markdig ^0.41.3; this engine runs Markdig unknown.", refusal.Sentence);
    }

    [Fact]
    public void AMissingOperation_IsRefused()
    {
        var engine = Engine();
        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), engine with
        {
            Operations = engine.Operations.Where(o => o.Key != "blank").ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
        })));
        Assert.Equal("S07-003", refusal.Id);
        Assert.StartsWith("This workflow (example-nested-note@v2026.12.1) needs blank ^1.0.0; this engine runs blank ", refusal.Sentence);
    }

    [Fact]
    public void AMissingKind_IsRefused()
    {
        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), Engine(kinds: AllKinds.Where(k => k != "while")))));
        Assert.Equal("S07-004", refusal.Id);
        Assert.Equal(
            "This workflow (example-nested-note@v2026.12.1) needs kind 'while', which this engine doesn't support. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusal.Sentence);
    }

    [Fact]
    public void AMissingFeature_IsRefused()
    {
        var engine = Engine() with { Features = new HashSet<string>(StringComparer.Ordinal) };
        var refusal = Assert.Single(Said(RequiresCheck.Engine(Resolve(), Lock(), engine)));
        Assert.Equal("S07-004", refusal.Id);
        Assert.Contains("needs feature 'parallel', which this engine doesn't support.", refusal.Sentence);
    }

    [Fact]
    public void AMissingAgentVersion_IsRefused()
    {
        var resolve = Resolve();
        var (name, version) = resolve["requires"]!["agents"]!.AsObject().Select(a => (a.Key, (string)a.Value!)).First();
        var agents = Engine().Agents.ToDictionary(
            a => a.Key,
            a => a.Key == name ? (IReadOnlySet<int>)new HashSet<int> { 9999 } : a.Value,
            StringComparer.Ordinal);

        var refusal = Assert.Single(Said(RequiresCheck.Engine(resolve, Lock(), Engine(agents: agents))));
        Assert.Equal("S07-005", refusal.Id);
        Assert.Equal(
            $"This workflow (example-nested-note@v2026.12.1) needs agent {name} version {version}, which this engine doesn't have. "
            + "Publish a new version of the workflow to run it on this engine.",
            refusal.Sentence);

        var without = agents.Where(a => a.Key != name).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        Assert.Equal("S07-005", Assert.Single(RequiresCheck.Engine(resolve, Lock(), Engine(agents: without))).Id);
    }

    [Fact]
    public void EmptyDocuments_AreRefused_NeverThrown()
    {
        var withEmptyResolve = Said(RequiresCheck.Engine(new JsonObject(), Lock(), Engine()));
        Assert.NotEmpty(withEmptyResolve);

        var withEmptyLock = Said(RequiresCheck.Engine(Resolve(), new JsonObject(), Engine()));
        Assert.NotEmpty(withEmptyLock);
        Assert.All(withEmptyLock, r => Assert.Equal("S07-003", r.Id));

        Assert.NotEmpty(Said(RequiresCheck.Engine(new JsonObject(), new JsonObject(), Engine())));
    }

    [Fact]
    public void MalformedDocuments_AreRefused_NeverThrown()
    {
        var shapes = new Func<JsonObject, JsonObject>[]
        {
            d => { d["requires"] = new JsonArray(); return d; },
            d => { d["requires"]!["scriban"] = 5; return d; },
            d => { d["requires"]!["operations"] = new JsonArray("size"); return d; },
            d => { d["requires"]!["kinds"] = new JsonObject(); return d; },
            d => { d["requires"]!["kinds"] = new JsonArray(1, null); return d; },
            d => { d["requires"]!["features"] = "parallel"; return d; },
            d => { d["requires"]!["agents"] = new JsonArray(); return d; },
            d => { d["requires"]!["agents"] = new JsonObject { ["custom-json"] = "one" }; return d; },
            d => { d["requires"]!["agents"] = new JsonObject { ["custom-json"] = 1 }; return d; },
            d => { d["package"] = "x"; d.Remove("requires"); return d; },
        };
        foreach (var shape in shapes)
            Assert.NotEmpty(Said(RequiresCheck.Engine(shape(Resolve()), Lock(), Engine())));

        var lockShapes = new Func<JsonObject, JsonObject>[]
        {
            d => { d["libraries"] = new JsonArray(); return d; },
            d => { d["libraries"]!["Scriban"] = "seven"; return d; },
            d => { d["evaluator"] = null; return d; },
            d => { d["operations"] = new JsonObject(); return d; },
        };
        foreach (var shape in lockShapes)
            Assert.NotEmpty(Said(RequiresCheck.Engine(Resolve(), shape(Lock()), Engine())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\n\t")]
    public void AnUnsetSignature_IsTheSignatureSentence(string? value)
    {
        var resolve = Resolve();
        resolve["needs"] = new JsonObject { ["profile"] = new JsonArray("signature") };
        var profile = new Dictionary<string, string?> { ["name"] = "Dr A. Sample", ["signature"] = value };

        var refusal = Assert.Single(Said(RequiresCheck.Profile(resolve, profile)));
        Assert.Equal("S06-031", refusal.Id);
        Assert.Equal("This workflow needs your signature. Choose one in Profile.", refusal.Sentence);

        Assert.Single(RequiresCheck.Profile(resolve, new Dictionary<string, string?>()));
    }

    [Fact]
    public void AnUnsetField_IsTheFieldSentence_WithItsLabel()
    {
        var resolve = Resolve();
        resolve["needs"] = new JsonObject { ["profile"] = new JsonArray("closing", "name") };

        var refusals = Said(RequiresCheck.Profile(resolve, new Dictionary<string, string?> { ["name"] = "Dr A. Sample", ["closing"] = " " }));

        var refusal = Assert.Single(refusals);
        Assert.Equal("S06-032", refusal.Id);
        Assert.Equal("This workflow needs your closing line. Set it in Profile, under Letter details.", refusal.Sentence);
    }

    [Fact]
    public void ASetProfile_IsAdmitted()
    {
        var resolve = Resolve();
        resolve["needs"] = new JsonObject { ["profile"] = new JsonArray("closing", "signature") };
        Assert.Empty(RequiresCheck.Profile(resolve, new Dictionary<string, string?>
        {
            ["closing"] = "Please call the clinic with any questions.",
            ["signature"] = "Dr A. Sample\nSample Oncology Clinic",
        }));
        Assert.Empty(RequiresCheck.Profile(Resolve(), new Dictionary<string, string?>()));
    }

    [Fact]
    public void Labels_AreProfileJsons()
    {
        var fields = V31Contract.Json("profile.json")["fields"]!.AsObject();
        foreach (var (field, row) in fields)
        {
            var resolve = Resolve();
            resolve["needs"] = new JsonObject { ["profile"] = new JsonArray(field) };
            var refusal = Assert.Single(Said(RequiresCheck.Profile(resolve, new Dictionary<string, string?>())));
            if (field == "signature")
                Assert.Equal("S06-031", refusal.Id);
            else
                Assert.Equal($"This workflow needs your {(string)row!["label"]!}. Set it in Profile, under Letter details.", refusal.Sentence);
        }
    }

    [Fact]
    public void AnEmptyResolve_IsReadAsNeedingEveryField_NeverThrown()
    {
        var refusals = Said(RequiresCheck.Profile(new JsonObject(), new Dictionary<string, string?>()));
        Assert.Equal(5, refusals.Count);
        Assert.Empty(RequiresCheck.Profile(new JsonObject(), V31Contract.Json("profile.json")["fields"]!.AsObject()
            .ToDictionary(f => f.Key, f => (string?)f.Value!["preview"], StringComparer.Ordinal)));

        var malformed = Resolve();
        malformed["needs"] = new JsonObject { ["profile"] = new JsonArray(5, "signature") };
        Assert.NotEmpty(Said(RequiresCheck.Profile(malformed, new Dictionary<string, string?>())));
    }
}
