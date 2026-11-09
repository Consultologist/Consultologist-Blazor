using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 A3 (package-format-v30-design.md § 4): a <c>let</c> at 30, through the seam.
/// <see cref="AddCount"/> adds to the 30 minimal a <c>problem_count</c> let — the
/// size of the problems the referral names — read by each drafted problem.
/// </summary>
public class WorkflowV30LetTests
{
    public static void AddCount(JsonObject root, Dictionary<string, string> files)
    {
        root["nodes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "problem_count", ["label"] = "Counting the problems",
            ["let"] = "node.find_problems.value.problems | array.size", ["type"] = "number", ["onInvalid"] = "fail"
        });
        files["prompts/draft_problem.md"] = "Problem {{ item.id }} of {{ node.problem_count.value }}: {{ item.value.name }} ({{ item.value.severity }}).";
    }

    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Counting(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundleFiles) = V30Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        AddCount(root, bundleFiles);
        change?.Invoke(root);
        files?.Invoke(bundleFiles);
        return (V30Package.Read(root.ToJsonString()), bundleFiles);
    }

    private static List<string> Errors(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        V30Package.Errors(Counting(change, files));

    private static JsonObject Node(JsonObject root, string id) => V30Package.Node(root, id);

    private static void Let(JsonObject root, string expression, string type = "number") =>
        (Node(root, "problem_count")["let"], Node(root, "problem_count")["type"]) = (expression, type);

    private static void NoErrors(List<string> errors) => Assert.True(errors.Count == 0, string.Join("\n", errors));

    private static void Says(string sentence, List<string> errors) =>
        Assert.True(errors.Contains(sentence), $"Expected: {sentence}\nGot:\n{string.Join("\n", errors)}");

    private static WorkflowDerivedEdges.Walk Walk((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var manifest = WorkflowImpliedPaths.WithImpliedPrompts(bundle.Manifest, bundle.Files);
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, bundle.Files, V25Package.Catalog(), out var snippets, out var contextFor);
        return WorkflowDerivedEdges.WalkAll(manifest, bundle.Files, nodes, snippets, contextFor);
    }

    // ---- § 4.1 the shape -------------------------------------------------------------------

    [Fact]
    public void ANumberLet_IsValid_AndItsReadsAreItsEdges()
    {
        var bundle = Counting();
        var result = V30Package.Validate(bundle);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);

        var walk = Walk(bundle);
        Assert.Contains("find_problems", walk.EdgesOf("problem_count"));
        Assert.Contains("problem_count", walk.EdgesOf("draft_problem"));
    }

    [Fact]
    public void ABooleanLet_IsReadAsATruth() =>
        NoErrors(Errors(root => Let(root, "(node.find_problems.value.problems | array.size) > 2", "boolean"), files =>
            files["prompts/draft_problem.md"] = "{{ if node.problem_count.value }}One of many: {{ end }}{{ item.value.name }}"));

    [Fact]
    public void ATextLet_IsReadAsText() =>
        NoErrors(Errors(root => Let(root, "node.find_problems.value.summary | string.upcase", "text"), files =>
            files["prompts/draft_problem.md"] = "{{ node.problem_count.content }}: {{ item.value.name }}"));

    [Fact]
    public void AFormatLet_IsReadAsThatFormat_AndALoopMayIterateIt() =>
        NoErrors(Errors(root =>
        {
            Let(root, "node.find_problems.value", "problem_list");
            Node(root, "problems")["for"] = "item in node.problem_count.value.problems";
        }, files => files["prompts/draft_problem.md"] = "{{ item.value.name }}: {{ node.problem_count.value.summary }}"));

    [Fact]
    public void ALetInALoop_ReadsTheItem()
    {
        NoErrors(Errors(root =>
        {
            Let(root, "item.value.name | string.size");
            Node(root, "problems")["nodes"]!.AsArray().Add("problem_count");
        }));
    }

    [Fact]
    public void ALetInAWhile_ReadsThePass()
    {
        NoErrors(V30Package.Errors(WorkflowV30WhileTests.Refining(root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "remaining", ["label"] = "Passes left", ["let"] = "3 - pass.number", ["type"] = "number", ["onInvalid"] = "fail"
            });
            V30Package.Node(root, "refine")["nodes"]!.AsArray().Add("remaining");
        }, files => files["prompts/judge.md"] = "Judge {{ node.draft.content }} with {{ node.remaining.value }} passes left")));
    }

    [Theory]
    [InlineData("format", "\"prose\"")]
    [InlineData("with", "{}")]
    [InlineData("failIfEmpty", "\"empty\"")]
    public void ALet_CarriesNoModelKeys(string key, string value) =>
        Says($"Let 'problem_count' declares {key}, which a let does not carry; a let computes its value from its expression and names it with type.",
            Errors(root => Node(root, "problem_count")[key] = JsonNode.Parse(value)));

    [Fact]
    public void ALet_DeclaresNoType_IsRefused() =>
        Says("Let 'problem_count' declares no type; a let names its value's type — text, number, boolean or an object format.",
            Errors(root => Node(root, "problem_count").Remove("type")));

    [Fact]
    public void ALet_OfAnUnknownType_IsRefused() =>
        Says("Let 'problem_count' type 'list' is neither text, number, boolean nor a package or catalogue format.",
            Errors(root => Node(root, "problem_count")["type"] = "list"));

    [Fact]
    public void ALet_OfATextFormat_IsRefused() =>
        Says("Let 'problem_count' type 'prose' is a text format; a let's format types an object — or write type text, number or boolean.",
            Errors(root => Node(root, "problem_count")["type"] = "prose"));

    [Fact]
    public void ALet_MustDeclareOnInvalid() =>
        Says("Let 'problem_count' must declare onInvalid (fail | flag); its value is checked against its type.",
            Errors(root => Node(root, "problem_count").Remove("onInvalid")));

    [Fact]
    public void ALet_RefusesRetry() =>
        Says("Let 'problem_count' declares onInvalid retry; retry needs a model — use fail or flag.",
            Errors(root => Node(root, "problem_count")["onInvalid"] = "retry"));

    [Fact]
    public void ALet_MayFlag() => NoErrors(Errors(root => Node(root, "problem_count")["onInvalid"] = "flag"));

    [Fact]
    public void ALet_IsOneExpression_NotATemplate() =>
        Says("Let 'problem_count' is a template, not an expression; write the expression bare (node.<id>.value | array.size).",
            Errors(root => Let(root, "{{ node.find_problems.value.problems | array.size }}")));

    [Fact]
    public void ALet_OfTwoStatements_IsRefused() =>
        Says("Let 'problem_count' is not one expression; a let computes one expression.",
            Errors(root => Let(root, "1\n2")));

    [Fact]
    public void ALet_Blank_IsRefused() =>
        Says("Let 'problem_count' is blank; a let computes one expression.", Errors(root => Let(root, " ")));

    [Fact]
    public void ALet_ImpliesNoPromptFile()
    {
        var bundle = Counting();
        Assert.DoesNotContain("prompts/problem_count.md", WorkflowImpliedPaths.CandidatePrompts(bundle.Manifest));

        var stray = Counting(files: files => files["prompts/problem_count.md"] = "Count them.");
        var implied = WorkflowImpliedPaths.WithImpliedPrompts(stray.Manifest, stray.Files);
        Assert.Null(implied.Nodes!.Single(n => n.Id == "problem_count").Prompt);
    }

    [Fact]
    public void ALet_IsNotAFlowNode_SoItCarriesNoDescription() =>
        Assert.Contains(Errors(root => Node(root, "problem_count")["description"] = "How many."),
            e => e.StartsWith("Node 'problem_count' declares description", StringComparison.Ordinal));

    [Fact]
    public void AFlowNode_DeclaringLet_IsStructural() =>
        Says("Node 'problems' declares for and let; a flow node is structural.",
            Errors(root => Node(root, "problems")["let"] = "1"));

    // ---- § 4.1 determinism -------------------------------------------------------------------

    [Theory]
    [InlineData("date.now", "date.now")]
    [InlineData("math.random 1 10", "math.random")]
    [InlineData("math.uuid", "math.uuid")]
    [InlineData("object.eval \"1\"", "object.eval")]
    [InlineData("(node.find_problems.value.problems | array.size) + (math.random 1 2)", "math.random")]
    public void ALet_CallsPureBuiltinsOnly(string expression, string function) =>
        Says($"Let 'problem_count' calls {function}, which is not deterministic; a let must give the same value on replay.",
            Errors(root => Let(root, expression)));

    [Fact]
    public void APromptMay_StillReadTheClock() =>
        NoErrors(Errors(files: files => files["prompts/find_problems.md"] = "Today is {{ date.now }}. List the problems: {{ input.notes }}"));

    [Fact]
    public void EveryScribanBuiltin_IsClassified()
    {
        var builtins = new Scriban.Functions.BuiltinFunctions();
        var names = new List<string>();

        foreach (var module in builtins.Keys)
        {
            if (builtins[module] is Scriban.Runtime.ScriptObject functions && builtins[module] is not Scriban.Runtime.EmptyScriptObject)
            {
                names.AddRange(functions.Keys.Select(f => $"{module}.{f}"));
            }
            else
            {
                names.Add(module);
            }
        }

        var unclassified = names.Where(n => !TemplateObjects.PureBuiltins.Contains(n) && !TemplateObjects.ImpureBuiltins.Contains(n)).Order().ToList();
        Assert.True(unclassified.Count == 0, "Classify these Scriban builtins as pure or impure: " + string.Join(", ", unclassified));
        Assert.Empty(TemplateObjects.PureBuiltins.Intersect(TemplateObjects.ImpureBuiltins));
    }

    // ---- § 4.1 the type the walk can know --------------------------------------------------------

    [Theory]
    [InlineData("node.find_problems.value.problems | array.size", "text", "Let 'problem_count' yields a number; it declares type text.")]
    [InlineData("(node.find_problems.value.problems | array.size) > 1", "number", "Let 'problem_count' yields a boolean; it declares type number.")]
    [InlineData("node.find_problems.value.summary", "boolean", "Let 'problem_count' yields a text; it declares type boolean.")]
    [InlineData("node.find_problems.value.problems", "problem_list", "Let 'problem_count' yields an array; it declares type problem_list.")]
    public void AKnowableMismatch_IsRefusedAtPublish(string expression, string type, string sentence) =>
        Says(sentence, Errors(root => Let(root, expression, type)));

    [Fact]
    public void AnUnknowableType_IsLeftToTheRun() =>
        NoErrors(Errors(root => Let(root, "node.find_problems.value.summary | string.to_int")));

    [Fact]
    public void ALet_ReadingALoopsList_IsAReduce()
    {
        NoErrors(Errors(root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "drafted", ["label"] = "Drafted", ["let"] = "node.problems | array.size", ["type"] = "number", ["onInvalid"] = "fail"
            });
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "closing", ["label"] = "Closing", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.closing");
        }, files => files["prompts/closing.md"] = "Drafted {{ node.drafted.value }} problems."));
    }

    // ---- below 30: the gate ----------------------------------------------------------------------

    [Theory]
    [InlineData("let", "\"1\"", "Node 'x' declares let, which requires specVersion 30.")]
    [InlineData("type", "\"number\"", "Node 'x' declares type, which requires specVersion 30.")]
    public void At29_LetAndType_AreGatedByName(string key, string value, string sentence)
    {
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        var node = new JsonObject { ["id"] = "x", ["label"] = "X", ["format"] = "prose" };
        node[key] = JsonNode.Parse(value);
        root["nodes"]!.AsArray().Add(node);
        files["prompts/x.md"] = "{{ input.notes }}";

        Says(sentence, V29Package.Errors((V29Package.Read(root.ToJsonString()), files)));
    }
}
