using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1508 (specVersion 31, area 11): the cases of the publish refusals the owner ruled on #1508: A11-001 (R1, a worst case
/// that opens more instances than the engine runs for one job), A11-002 (R4, a format file that names a format and defines
/// the same name itself) and A11-003 (R5, a format past the run's expansion budget). A11-002 is the type step's;
/// A11-001 and A11-003 are publish's compiler's (<see cref="ResolveCompiler.Compile"/>), so an <c>A11-</c> case is judged
/// by <see cref="Judge"/>: the check, then the compiler against <see cref="CaseLimits"/>, the engine's default limits, which
/// each case's <c>about</c> states.
/// </summary>
internal static partial class RefusalPackages
{
    /// <summary>
    /// The limits an <c>A11-</c> case is judged against: the engine's defaults (<c>RunLimits</c>: 16 agent calls at once,
    /// 200 items a loop, 500 agent calls and 10,000 instances a job).
    /// </summary>
    internal static readonly PublishLimits CaseLimits = new(16, 200, 500, Instances: 10_000);

    /// <summary>
    /// What publish says of the case <paramref name="id"/>'s package <paramref name="files"/>: <see cref="SourceCheck.Run"/>'s
    /// refusals; for an <c>A11-</c> case and (#1508, Task 22) a test's <c>S07-</c> case, the refusals of
    /// <see cref="PackagePublish.CheckAsync"/> (steps 0 to 3: the check, the compiler against <see cref="CaseLimits"/>, and the
    /// package's tests run offline), with <see cref="CompiledGoldenTests.Fixed"/> and a clock that does not move.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Judge(string id, IReadOnlyDictionary<string, byte[]> files, TypeRegistry registry)
    {
        if (!id.StartsWith("A11-", StringComparison.Ordinal) && !id.StartsWith("S07-", StringComparison.Ordinal))
            return SourceCheck.Run(files, registry).Refusals;
        var options = new PublishOptions(registry, CompiledGoldenTests.Fixed, CaseLimits, "ci", new CompiledGoldenTests.StoppedClock());
        return PackagePublish.CheckAsync(files, options).GetAwaiter().GetResult().Report.Refusals;
    }

    private static IEnumerable<RefusalCase> PublishRefusals()
    {
        foreach (var testCase in TestRefusals())
            yield return testCase;

        yield return new RefusalCase("A11-001",
            "Publish refuses a package whose worst case opens more instances than the engine runs for one job (chapter 8 § 8.7; "
            + "ruled by the owner on #1508). Judged against the engine's default limits: 10,000 instances a job, 200 items a "
            + "loop. Every instance counts: a node, check or deliverable instance, a loop item, a pass, a call's body. The loop "
            + "outer (max 49) holds inner (max 101) over the points, which holds the computed echo; beside it the plan, the "
            + "computed count and the note. Valid: 4 + 49 × (1 + 1 + 101 × 2) = 10,000 instances, the limit itself. Invalid: "
            + "inner's max is 102, 10,098 instances.",
            NestedEchoes("101"),
            NestedEchoes("101").Edit("nodes/outer/inner/index.json", "\"max\": 101", "\"max\": 102"));

        yield return new RefusalCase("A11-002",
            "A package's format file that names a format and also defines the same name in its own $defs (chapter 2 § 2.3; "
            + "ruled by the owner on #1508). The format visit names the package format timing; the input visit is of it. "
            + "Invalid: visit.json's own definition is named timing. Valid: it is named own_timing.",
            WithOwnTiming(),
            WithOwnTiming().Edit("formats/visit.json", "\"own_timing\"", "\"timing\""));

        yield return new RefusalCase("A11-003",
            "A format whose expansion passes the run's budget of 1,048,576 nodes (PlanFormats.NodeBudget, chapter 7 § 7.10; ruled by "
            + "the owner on #1508): publish refuses what the run could not expand. The input chart is of the format chart, 600 fields "
            + "each the format codes, whose field code refers to one of codes.json's own definitions. Invalid: code is the "
            + "definition big, an enum of 2,000 values, so chart expands to more than 1.2 million nodes. Valid: code is the "
            + "definition small, an enum of 2 values.",
            WithChart(),
            WithChart().Edit("formats/codes.json", "\"$ref\": \"#/$defs/small\"", "\"$ref\": \"#/$defs/big\""));
    }

    /// <summary>
    /// Minimal with the required list input <c>points</c>; the computed <c>count</c> of them; the sequential loop <c>outer</c>
    /// (max 49) over them holding <c>inner</c> (max <paramref name="innerMax"/>) over them, holding the computed <c>echo</c>.
    /// </summary>
    private static Dictionary<string, byte[]> NestedEchoes(string innerMax)
    {
        static string Loop(string label, string format, string max, string body, string ret) =>
            SourceFixture.Json(Node("for", label, format, true, false,
                "\"in\": \"input.points.value\", \"parallel\": false, \"maxParallel\": null, \"until\": null, "
                + $"\"max\": {max}, \"itemLabel\": null, \"nodes\": [\"{body}\"], \"ret\": \"{ret}\""));

        return Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["points"] = System.Text.Json.Nodes.JsonNode.Parse(
                "{ \"label\": \"Points\", \"description\": null, \"format\": " + ArrayOf(Plain) + ", \"required\": true, \"channel\": null }"))
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"count\", \"outer\"]")
            .With("nodes/count.md", Md(Node("computed", "Counting the points", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"input.points.value\" }] }"), ""))
            .With("nodes/outer/index.json", Loop("Outer", ArrayOf(ArrayOf(Plain)), "49", "inner", "node.outer.inner"))
            .With("nodes/outer/inner/index.json", Loop("Inner", ArrayOf(Plain), innerMax, "echo", "node.outer.inner.echo"))
            .With("nodes/outer/inner/echo.md", Md(Node("computed", "Repeating a point", Plain, true, false,
                "\"if\": null, \"expression\": { \"var\": \"item.value\" }"), ""));
    }

    /// <summary>
    /// #1508 (Task 22; part 07 §§ 8.1 to 8.3, § 10): the sentences a package's own test gives at publish, step 3, each on
    /// <see cref="WithTest"/>: the valid side is that package, whose one test passes; the invalid side changes one member of
    /// the test. S07-013 (the tests together past 60 seconds) has no case: no package can state a clock.
    /// </summary>
    private static IEnumerable<RefusalCase> TestRefusals()
    {
        const string common = "A package's test (tests/note.json, chapter 7 § 7.8) is run offline at publish, step 3, on its stubs, "
            + "and a test that is not as the grammar says, or does not hold, stops publish. Valid: the test gives Margaret Okafor's "
            + "draft (fictional), no profile field and no run fact (the workflow reads none), the plan's stub, and expects the plan "
            + "to run; it passes. ";

        yield return new RefusalCase("S07-007",
            common + "Invalid: it expects the plan to be skipped; the plan runs, so the test fails on its first mismatch.",
            WithTest(), WithTest().Edit("tests/note.json", "\"ran\": [", "\"skipped\": ["));

        yield return new RefusalCase("S07-008",
            common + "Invalid: outputs gives no stub for the plan, an agent node that runs.",
            WithTest(), WithTest().Edit("tests/note.json", "{ \"node.plan\": \"History, then the plan (fictional).\" }", "{}"));

        yield return new RefusalCase("S07-009",
            common
            + "Here the plan returns a judgement (the registry's format: answer, a boolean, and reason), and its stub is one. "
            + "Invalid: the stub's answer is the text \"yes\", not a boolean; the sentence names the format, the path and the "
            + "keyword that fails, never the value.",
            JudgedPlan(), JudgedPlan().Edit("tests/note.json", "\"answer\": true", "\"answer\": \"yes\""));

        yield return new RefusalCase("S07-010",
            common + "Invalid: inputs gives no value for consult_draft.",
            WithTest(), WithTest().Edit("tests/note.json", "{ \"consult_draft\": \"Margaret Okafor, 58, seen by Dr A. Sample at Sample Oncology Clinic (fictional).\" }", "{}"));

        yield return new RefusalCase("S07-011",
            common + "Invalid: profile gives a signature, a field the workflow does not read.",
            WithTest(), WithTest().Edit("tests/note.json", "\"profile\": {}", "\"profile\": { \"signature\": \"Dr A. Sample\" }"));

        yield return new RefusalCase("S07-012",
            common + "Invalid: run gives the date, a fact the workflow does not read.",
            WithTest(), WithTest().Edit("tests/note.json", "\"run\": {}", "\"run\": { \"date\": { \"value\": \"2026-11-16\", \"timeZone\": \"America/Toronto\" } }"));

        yield return new RefusalCase("A11-005",
            common + "Invalid: expect's values is an array, not an object keyed by row key; the sentence names the member and what "
            + "is wrong, never a key or value of the file.",
            WithTest(), WithTest().Edit("tests/note.json", "\"ran\": [\"node.plan\"]", "\"values\": []"));
    }

    /// <summary>
    /// Minimal with the test <c>note</c>: Margaret Okafor's draft (fictional), no profile field and no run fact, the plan's
    /// stub, and the expectation that the plan runs.
    /// </summary>
    private static Dictionary<string, byte[]> WithTest() =>
        Minimal()
            .Edit("tests/index.json", "\"items\": []", "\"items\": [\"note\"]")
            .With("tests/note.json", SourceFixture.Json(
                "{ \"description\": [\"The plan of Margaret Okafor's note runs (fictional).\"], "
                + "\"inputs\": { \"consult_draft\": \"Margaret Okafor, 58, seen by Dr A. Sample at Sample Oncology Clinic (fictional).\" }, "
                + "\"profile\": {}, \"run\": {}, "
                + "\"outputs\": { \"node.plan\": \"History, then the plan (fictional).\" }, "
                + "\"expect\": { \"ran\": [\"node.plan\"] } }"));

    /// <summary><see cref="WithTest"/> with the plan returning the registry's <c>judgement</c>, stubbed as one.</summary>
    private static Dictionary<string, byte[]> JudgedPlan() =>
        WithTest()
            .Edit("nodes/plan.md", "\"format\": \"plaintext\"", "\"format\": \"judgement\"")
            .Edit("tests/note.json", "\"History, then the plan (fictional).\"", "{ \"answer\": true, \"reason\": \"History, then the plan (fictional).\" }");

    private const string DialectSchema = "\"$schema\": \"urn:consultologist:spec:31:dialect\"";

    /// <summary>Minimal with the package formats <paramref name="formats"/> and the optional input of the first, named as it.</summary>
    private static Dictionary<string, byte[]> WithInputFormats(params (string Id, string Schema)[] formats)
    {
        var first = formats[0].Id;
        var files = Minimal()
            .Edit("formats/index.json", "\"items\": []", "\"items\": [" + string.Join(", ", formats.Select(f => $"\"{f.Id}\"")) + "]")
            .WithJson("manifest.json", m => m["inputs"]![first] = System.Text.Json.Nodes.JsonNode.Parse(
                $"{{ \"label\": \"The {first}\", \"description\": null, \"format\": \"{first}\", \"required\": false, \"channel\": null }}"));
        foreach (var (id, schema) in formats)
            files.With($"formats/{id}.json", SourceFixture.Json(schema));
        return files;
    }

    /// <summary>The format <c>visit</c>, naming the package format <c>timing</c>, with its own definition <c>own_timing</c>.</summary>
    private static Dictionary<string, byte[]> WithOwnTiming()
    {
        const string timing = "{ " + DialectSchema + ", \"type\": \"object\", \"required\": [\"slot\"], \"additionalProperties\": false, "
            + "\"properties\": { \"slot\": { \"title\": \"Slot\", \"type\": \"string\", \"enum\": [\"morning\", \"afternoon\"] } } }";
        const string visit = "{ " + DialectSchema + ", \"type\": \"object\", \"required\": [\"when\"], \"additionalProperties\": false, "
            + "\"properties\": { \"when\": { \"title\": \"When\", \"$ref\": \"timing\" } }, "
            + "\"$defs\": { \"own_timing\": { \"type\": \"string\", \"enum\": [\"today\", \"next_visit\"] } } }";
        return WithInputFormats(("visit", visit), ("timing", timing));
    }

    /// <summary>
    /// The format <c>chart</c>: 600 fields, each the format <c>codes</c>, whose field <c>code</c> is its file's own definition
    /// <c>small</c> (an enum of 2 values); its definition <c>big</c>, an enum of 2,000, is named by nothing.
    /// </summary>
    private static Dictionary<string, byte[]> WithChart()
    {
        var names = Enumerable.Range(0, 600).Select(i => $"f{i}").ToList();
        var chart = "{ " + DialectSchema + ", \"type\": \"object\", \"required\": [" + string.Join(", ", names.Select(n => $"\"{n}\"")) + "], "
            + "\"additionalProperties\": false, \"properties\": { "
            + string.Join(", ", names.Select(n => $"\"{n}\": {{ \"title\": \"Field {n}\", \"$ref\": \"codes\" }}")) + " } }";
        var codes = "{ " + DialectSchema + ", \"type\": \"object\", \"required\": [\"code\"], \"additionalProperties\": false, "
            + "\"properties\": { \"code\": { \"title\": \"Code\", \"$ref\": \"#/$defs/small\" } }, \"$defs\": { "
            + "\"small\": { \"type\": \"string\", \"enum\": [\"code_a\", \"code_b\"] }, "
            + "\"big\": { \"type\": \"string\", \"enum\": [" + string.Join(", ", Enumerable.Range(0, 2000).Select(i => $"\"code_{i}\"")) + "] } } }";
        return WithInputFormats(("chart", chart), ("codes", codes));
    }
}
