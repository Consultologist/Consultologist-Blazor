using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): the in-memory plan of part 09 § 1.2 step 5, built from <c>resolve.json</c> alone: its
/// entries by key with their parents, bodies, branches, compiled rules (each with its <c>plainConverts</c>, D3) and what
/// they depend on; its functions with their local maps; its own evaluator (04-D47). A document it cannot make a plan
/// from is refused with S07-006, never with a throw.
/// </summary>
public class RunPlanTests
{
    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public void TheGoldens_Build(string name)
    {
        var plan = RunFixture.Plan(name);
        var document = JsonNode.Parse(GoldenText(name))!;

        Assert.Equal((string)document["package"]!["name"]!, plan.Package);
        Assert.Equal((string)document["package"]!["version"]!, plan.Version);
        Assert.Equal(CanonicalJson.HashText(GoldenText(name)), plan.ResolveHash);
        Assert.Equal(
            document["nodes"]!.AsObject().Concat(document["checks"]!.AsObject()).Concat(document["deliverables"]!.AsObject())
                .Select(p => p.Key).Order(StringComparer.Ordinal),
            plan.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.All(plan.Entries.Values, e => Assert.True(e.Function == null || e.Kind == PlanKind.Call, $"{e.Key} names a function"));
        Assert.All(plan.Entries.Values, e => Assert.Equal(document["graph"]!["order"]!.AsArray().Select(k => (string)k!).ToList().IndexOf(e.Key), e.Order));
        AssertConsistent(plan);
    }

    [Fact]
    public void NestedNote_HasItsShape()
    {
        var plan = RunFixture.Plan("nested-note");

        Assert.Equal(["node.plan", "node.section_count", "node.sections", "node.refine", "deliverable.note"], plan.TopLevel);
        Assert.Equal(["consult_draft"], plan.InputOrder);
        Assert.Empty(plan.NeedsProfile);
        Assert.Empty(plan.RunFacts);

        var draft = plan.Entries["node.sections.paragraphs.draft_paragraph"];
        Assert.Equal(PlanKind.Agent, draft.Kind);
        Assert.Equal("draft_paragraph", draft.Id);
        Assert.Equal("node.sections.paragraphs", draft.Parent);
        Assert.Equal(["item.parent.value.title", "item.value"], draft.Reads);
        Assert.Empty(draft.Depends);
        Assert.NotNull(draft.If);
        Assert.Null(draft.If!.Declined);
        Assert.Equal(6, draft.Order);

        Assert.Equal(["node.plan"], plan.Entries["node.section_count"].Depends);
        Assert.NotNull(plan.Entries["node.section_count"].Expression);

        var sections = plan.Entries["node.sections"];
        Assert.Equal(PlanKind.For, sections.Kind);
        Assert.Equal(["node.sections.outline", "node.sections.paragraphs"], sections.Body);
        Assert.Equal("node.sections.paragraphs", sections.Ret);
        Assert.Equal(["node.plan"], sections.Depends);

        var refine = plan.Entries["node.refine"];
        Assert.Equal(PlanKind.While, refine.Kind);
        Assert.NotNull(refine.While);
        Assert.Null(refine.While!.Declined);
        Assert.Null(refine.Until);
        Assert.Equal("node.refine.judge.value.verdict ≠ \"good\"", refine.Printed("while"));

        Assert.Equal(PlanKind.Deliverable, plan.Entries["deliverable.note"].Kind);
        Assert.Equal(["node.refine"], plan.Entries["deliverable.note"].Depends);
        Assert.Equal("""{"$ref":"#/$defs/markdown_block"}""", plan.Entries["deliverable.note"].Format!.ToJsonString());
    }

    [Fact]
    public void Conditional_HasItsBranches()
    {
        var plan = RunFixture.Plan("conditional");

        var route = plan.Entries["node.sections_draft.route"];
        Assert.Equal(PlanKind.Case, route.Kind);
        Assert.Equal(["item.value.kind"], route.Reads);
        Assert.Empty(route.Depends);
        Assert.Equal(4, route.Branches.Count);
        Assert.Equal(["node.sections_draft.route.narrative_draft"], route.Branches[0].Nodes);
        Assert.Equal("node.sections_draft.route.narrative_draft", route.Branches[0].Ret);
        Assert.Equal("[\"narrative\"]", new JsonArray(route.Branches[0].When!.Select(w => w?.DeepClone()).ToArray()).ToJsonString());
        Assert.Empty(route.Branches[2].Nodes);
        Assert.Null(route.Branches[2].Ret);
        Assert.Null(route.Branches[3].When);
        Assert.Empty(route.Branches[3].Nodes);

        var applies = plan.Entries["node.standing_sections.applies"];
        Assert.Equal(PlanKind.If, applies.Kind);
        Assert.Equal(2, applies.Branches.Count);
        Assert.Equal("node.standing_sections.applies.standing_draft", applies.Branches[0].Ret);
        Assert.Empty(applies.Branches[1].Nodes);
        Assert.Equal(["input.include_billing.value", "item.id"], applies.Reads);
        Assert.Empty(applies.Depends);
        Assert.Equal(["consult_draft", "sections", "include_billing"], plan.InputOrder);
        Assert.Equal(3, plan.Inputs.Count);
    }

    [Fact]
    public void Functions_HasItsFunction()
    {
        var plan = RunFixture.Plan("functions");

        var function = plan.Functions["draft_section"];
        Assert.Equal("draft_section", function.Id);
        Assert.Equal(["standard", "draft"], function.Inputs);
        Assert.Equal(["node.standard_draft", "node.patient_draft", "node.apply_instructions"], function.Body);
        Assert.Equal(3, function.Entries.Count);
        Assert.Equal("node.apply_instructions", function.Ret);
        Assert.Equal("""{"$ref":"#/$defs/plaintext"}""", function.Format!.ToJsonString());

        var apply = function.Entries["node.apply_instructions"];
        Assert.Equal(["node.patient_draft", "node.standard_draft"], apply.Depends);
        Assert.Equal(2, apply.Order);
        Assert.Empty(function.Entries["node.standard_draft"].Depends);
        Assert.NotNull(function.Entries["node.standard_draft"].If);

        var call = plan.Entries["node.standards.section"];
        Assert.Equal(PlanKind.Call, call.Kind);
        Assert.Equal("draft_section", call.Function);
        Assert.Equal(["input.consult_draft.content", "item"], call.Reads);
        Assert.Empty(call.Depends);

        Assert.Equal(["node.extract_input_terms", "node.extract_note_terms"], plan.Entries["check.section_coverage.coverage"].Depends);
        Assert.Equal(["check.section_coverage"], plan.Entries["check.all_sections"].Depends);
        Assert.Equal(["node.standards"], plan.Entries["deliverable.emr.consult"].Depends);
        Assert.Equal(["snippet.standards", "snippet.standards.assessment_plan", "snippet.standards.hpi"], plan.Snippets.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EachPlan_HasItsOwnEvaluator()
    {
        var text = GoldenText("nested-note");
        var hash = CanonicalJson.HashText(text);
        var limit = TimeSpan.FromSeconds(2);

        var (first, _) = RunPlan.Build(text, hash, "sha256:" + new string('0', 64), limit);
        var (second, _) = RunPlan.Build(text, hash, "sha256:" + new string('0', 64), limit);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first!.Evaluator, second!.Evaluator);
        Assert.Equal(limit, first.Evaluator.MatchTimeLimit);
        Assert.Equal(limit, second.Evaluator.MatchTimeLimit);
    }

    [Fact]
    public void NotADocument_IsRefused()
    {
        foreach (var text in new[] { "", "not json", "[]", "null", "{}" })
        {
            var (plan, refusal) = RunPlan.Build(text, "sha256:x", "sha256:y", null);
            Assert.Null(plan);
            Assert.NotNull(refusal);
            Assert.Equal("S07-006", refusal!.Id);
        }
    }

    [Fact]
    public void ARuleTheEvaluatorDeclines_IsRefusedWithS07006()
    {
        var document = JsonNode.Parse(GoldenText("nested-note"))!.AsObject();
        document["nodes"]!["node.section_count"]!["expression"] = JsonNode.Parse("""{"eval":[{"var":"node.plan.value.sections"}]}""");

        var (plan, refusal) = RunPlan.Build(document.ToJsonString(), "sha256:x", "sha256:y", null);

        Assert.Null(plan);
        Assert.NotNull(refusal);
        Assert.Equal("Workflow example-nested-note@v2026.12.1 can't run: its compiled document doesn't match the hash published for it.", refusal!.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    public static TheoryData<string> Corruptions() => new(
        "a key named and absent",
        "a member of the wrong kind",
        "a ret outside its body",
        "a body that contains an ancestor",
        "a loop in its own body",
        "a key dropped from the order",
        "a rule without its plainConverts");

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void EachCorruption_IsRefused(string corruption)
    {
        var document = JsonNode.Parse(GoldenText("nested-note"))!.AsObject();
        var nodes = document["nodes"]!.AsObject();
        switch (corruption)
        {
            case "a key named and absent":
                nodes["node.sections"]!["nodes"]!.AsArray().Add("node.sections.summary");
                break;
            case "a member of the wrong kind":
                nodes["node.sections.outline"]!["reads"] = "item.value.title";
                break;
            case "a ret outside its body":
                nodes["node.sections"]!["ret"] = "node.sections.paragraphs.draft_paragraph";
                break;
            case "a body that contains an ancestor":
                nodes["node.sections.paragraphs"]!["nodes"]!.AsArray().Add("node.sections");
                nodes["node.sections"]!["parent"] = "node.sections.paragraphs";
                break;
            case "a loop in its own body":
                nodes["node.refine"]!["nodes"]!.AsArray().Add("node.refine");
                break;
            case "a key dropped from the order":
                document["graph"]!["order"]!.AsArray().RemoveAt(2);
                break;
            default:
                nodes["node.refine"]!["plainConverts"] = new JsonObject();
                break;
        }

        var (plan, refusal) = RunPlan.Build(document.ToJsonString(), "sha256:x", "sha256:y", null);

        Assert.Null(plan);
        Assert.Equal("S07-006", refusal!.Id);
    }

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public void ACorruptDocument_IsRefusedOrFails_NeverThrows(string name)
    {
        var random = new Random(31);
        var text = GoldenText(name);
        var built = 0;
        for (var i = 0; i < 300; i++)
        {
            var document = JsonNode.Parse(text)!.AsObject();
            var what = Mutate(document, random);
            var mutated = document.ToJsonString();

            var (plan, refusal) = RunPlan.Build(mutated, "sha256:x", "sha256:y", null);

            Assert.True((plan == null) != (refusal == null), $"{name} #{i} ({what}): exactly one of a plan and a refusal.");
            if (refusal != null)
            {
                Assert.Equal("S07-006", refusal.Id);
                continue;
            }

            built++;
            try
            {
                AssertConsistent(plan!);
            }
            catch (Exception e)
            {
                throw new Xunit.Sdk.XunitException($"{name} #{i} ({what}): {e.Message}");
            }
        }

        Assert.InRange(built, 1, 299);
    }

    /// <summary>Every <c>Parent</c>, <c>Ret</c>, body id, branch id and called function of <paramref name="plan"/> names something present.</summary>
    private static void AssertConsistent(RunPlan plan)
    {
        static void Map(IReadOnlyDictionary<string, PlanEntry> entries, RunPlan plan)
        {
            foreach (var entry in entries.Values)
            {
                if (entry.Parent != null)
                    Assert.True(entries.ContainsKey(entry.Parent), $"{entry.Key}: parent {entry.Parent}");
                if (entry.Ret != null)
                    Assert.True(entries.ContainsKey(entry.Ret), $"{entry.Key}: ret {entry.Ret}");
                foreach (var child in entry.Body.Concat(entry.Branches.SelectMany(b => b.Nodes)))
                    Assert.True(entries.ContainsKey(child), $"{entry.Key}: child {child}");
                foreach (var branch in entry.Branches.Where(b => b.Ret != null))
                    Assert.True(entries.ContainsKey(branch.Ret!), $"{entry.Key}: branch ret {branch.Ret}");
                foreach (var depends in entry.Depends)
                    Assert.True(entries.ContainsKey(depends), $"{entry.Key}: depends {depends}");
                if (entry.Kind == PlanKind.Call)
                    Assert.True(entry.Function != null && plan.Functions.ContainsKey(entry.Function), $"{entry.Key}: function {entry.Function}");
            }
        }

        Map(plan.Entries, plan);
        foreach (var key in plan.TopLevel)
            Assert.Null(plan.Entries[key].Parent);
        foreach (var function in plan.Functions.Values)
        {
            Map(function.Entries, plan);
            foreach (var key in function.Body)
                Assert.True(function.Entries.ContainsKey(key), $"function {function.Id}: body {key}");
            if (function.Ret != null)
                Assert.True(function.Entries.ContainsKey(function.Ret), $"function {function.Id}: ret {function.Ret}");
        }
    }

    // ---- mutations ----------------------------------------------------------------------------

    private static readonly string[] Pointing = ["ret", "parent", "in", "function", "nodes"];
    private static readonly string[] Rules = ["if", "expression", "while", "until"];

    /// <summary>One mutation of <paramref name="document"/>, chosen by <paramref name="random"/>; what it did.</summary>
    private static string Mutate(JsonObject document, Random random)
    {
        var entries = Entries(document).ToList();
        switch (random.Next(7))
        {
            case 0:
            {
                var target = Pick(Objects(document).Where(o => o.Count > 0).ToList(), random);
                var member = Pick(target.Select(p => p.Key).ToList(), random);
                target.Remove(member);
                return $"delete {member}";
            }

            case 1:
            {
                var target = Pick(Objects(document).Where(o => o.Count > 0).ToList(), random);
                var member = Pick(target.Select(p => p.Key).ToList(), random);
                target[member] = OtherKind(target[member], random);
                return $"swap {member}";
            }

            case 2:
            {
                var nodes = document["nodes"]!.AsObject();
                if (nodes.Count == 0)
                    goto case 0;
                var key = Pick(nodes.Select(p => p.Key).ToList(), random);
                var entry = nodes[key]!;
                nodes.Remove(key);
                nodes[key + "_renamed"] = entry;
                return $"rename {key}";
            }

            case 3:
            {
                var candidates = entries.SelectMany(e => Pointing.Where(e.ContainsKey).Select(m => (Entry: e, Member: m))).ToList();
                if (candidates.Count == 0)
                    goto case 0;
                var (entry, member) = Pick(candidates, random);
                if (member == "nodes" && entry["nodes"] is JsonArray body)
                    body.Add("node.not_there");
                else
                    entry[member] = "node.not_there";
                return $"point {member} of {entry["id"]}";
            }

            case 4:
            {
                var loops = document["nodes"]!.AsObject().Where(p => p.Value!["kind"]?.GetValue<string>() is "for" or "while").ToList();
                if (loops.Count == 0)
                    goto case 0;
                var (key, loop) = Pick(loops, random);
                loop!["nodes"]!.AsArray().Add(key);
                return $"loop {key} in itself";
            }

            case 5:
            {
                var order = document["graph"]!["order"]!.AsArray();
                var at = random.Next(order.Count);
                var dropped = order[at]!.GetValue<string>();
                order.RemoveAt(at);
                return $"drop {dropped} from the order";
            }

            default:
            {
                var candidates = entries.SelectMany(e => Rules.Where(m => e[m] is JsonObject).Select(m => (Entry: e, Member: m))).ToList();
                if (candidates.Count == 0)
                    goto case 0;
                var (entry, member) = Pick(candidates, random);
                var rule = entry[member]!.AsObject();
                var operation = rule.First();
                entry[member] = new JsonObject { ["eval"] = operation.Value?.DeepClone() };
                return $"eval in {member} of {entry["id"]}";
            }
        }
    }

    /// <summary>Every entry of the document's <c>nodes</c>, <c>checks</c>, <c>deliverables</c> and functions' <c>nodes</c>.</summary>
    private static IEnumerable<JsonObject> Entries(JsonObject document)
    {
        foreach (var map in new[] { "nodes", "checks", "deliverables" })
            foreach (var (_, entry) in document[map]!.AsObject())
                yield return entry!.AsObject();
        foreach (var (_, function) in document["functions"]!.AsObject())
            foreach (var (_, entry) in function!["nodes"]!.AsObject())
                yield return entry!.AsObject();
    }

    /// <summary>Every object in <paramref name="node"/>, itself first.</summary>
    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var (_, child) in obj)
                foreach (var inner in Objects(child))
                    yield return inner;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                foreach (var inner in Objects(child))
                    yield return inner;
        }
    }

    private static JsonNode? OtherKind(JsonNode? value, Random random)
    {
        JsonNode?[] kinds = [null, JsonValue.Create(7), JsonValue.Create("node.plan"), JsonValue.Create(true), new JsonArray(), new JsonObject()];
        var others = kinds.Where(k => (k?.GetValueKind()) != (value?.GetValueKind())).ToList();
        return others[random.Next(others.Count)];
    }

    private static T Pick<T>(IReadOnlyList<T> items, Random random) => items[random.Next(items.Count)];

    private static string GoldenText(string name) =>
        new UTF8Encoding(false).GetString(File.ReadAllBytes(Path.Combine(CompiledGoldenTests.GoldenFolder(), $"{name}.resolve.json")));
}
