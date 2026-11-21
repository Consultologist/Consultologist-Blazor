using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): a <c>case</c>'s subject and its values, held to the subject's enum (part 04 § 5.2;
/// § 10.1's <c>case</c> rows), and what is "always valued" (§§ 5.3, 7.6; D14). Each refusal is one change to
/// <see cref="ResolveFixture.Conditional"/>, whose <c>route</c> switches on <c>item.value.kind</c> (narrative, table,
/// omit), and is the only one said.
/// </summary>
public class SourceResolveBranchesTests
{
    private const string Route = "nodes/sections_draft/route/index.json";

    private const string Values = "\"narrative\", \"table\", \"omit\"";

    /// <summary>The one refusal <see cref="SourceCheck.Run"/> says of <paramref name="files"/>, held to its row.</summary>
    private static SourceRefusal Refused(Dictionary<string, byte[]> files)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        var refusal = result.Refusals[0];
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static void AssertRefusal(Dictionary<string, byte[]> files, string id, string sentence, string path)
    {
        var refusal = Refused(files);
        Assert.Equal((id, sentence, path), (refusal.Id, refusal.Sentence, refusal.Path));
    }

    private static void AssertAccepted(Dictionary<string, byte[]> files)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }

    /// <summary><see cref="ResolveFixture.Conditional"/> with <c>route</c>'s member <paramref name="member"/> set to <paramref name="json"/>.</summary>
    private static Dictionary<string, byte[]> Set(string member, string json) =>
        ResolveFixture.Conditional().WithJson(Route, h => h[member] = JsonNode.Parse(json));

    /// <summary><see cref="ResolveFixture.Conditional"/> with <c>route</c>'s branch <paramref name="n"/> listing <paramref name="when"/>.</summary>
    private static Dictionary<string, byte[]> When(int n, params string[] when) =>
        ResolveFixture.Conditional().WithJson(Route,
            h => h["branches"]![n - 1]!["when"] = new JsonArray(when.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()));

    /// <summary><paramref name="files"/> with an <c>else</c> on <c>route</c>, running the agent <c>other_draft</c>.</summary>
    private static Dictionary<string, byte[]> WithElse(Dictionary<string, byte[]> files) =>
        files.WithJson(Route, h => h["else"] = JsonNode.Parse(
                "{ \"nodes\": [\"other_draft\"], \"ret\": \"node.sections_draft.route.other_draft\" }"))
            .With("nodes/sections_draft/route/other_draft.md", Agent("Drafting any other section", "\"plaintext\"",
                "Write the section '{{ item.value.title }}' briefly.\n"));

    // ---- The subject ---------------------------------------------------------------------------

    [Theory]
    [InlineData("{ \"var\": \"item.value.kind\" }")]
    [InlineData("[\"item.value.kind\"]")]
    public void ACaseThatIsAnExpression_IsS04017(string json) =>
        AssertRefusal(Set("case", json), "S04-017",
            "Case 'route' case is an expression; it names one readable thing: compute the value in a computed node and name that.",
            Route);

    [Theory]
    [InlineData("item..value.kind")]
    [InlineData("item.value.kind.0")]
    public void ACaseThatIsNotAReference_IsA05001(string text) =>
        AssertRefusal(Set("case", JsonValue.Create(text)!.ToJsonString()), "A05-001",
            $"Case 'route' case '{text}' is not a reference; a reference is a root, then names joined by dots.", Route);

    [Theory]
    [InlineData("item.value.sort")]
    [InlineData("node.nothing.value")]
    [InlineData("item.value.kind.extra")]
    public void ACaseThatNamesNothing_IsS03014(string reference) =>
        AssertRefusal(Set("case", JsonValue.Create(reference)!.ToJsonString()), "S03-014",
            $"Case 'route' reads {reference}, which doesn't exist here.", Route);

    [Theory]
    [InlineData("item.value.title", "string")]
    [InlineData("input.consult_draft.value", "markdown_block")]
    [InlineData("item.id", "string")]
    [InlineData("item.value", "section_request")]
    public void ASubjectThatIsNotAnEnum_IsS04044(string reference, string format) =>
        AssertRefusal(Set("case", JsonValue.Create(reference)!.ToJsonString()), "S04-044",
            $"Case 'route' switches on {reference}, which is {format}; a case switches on an enumerated string.", Route);

    [Fact]
    public void ASubjectOfConsts_IsAnEnum()
    {
        // oneOf with const is an enumerated string as enum is (part 02 § 9)
        var files = ResolveFixture.Conditional().WithJson("formats/section_request.json", f => f["properties"]!["kind"] = JsonNode.Parse(
            "{ \"type\": \"string\", \"title\": \"Kind\", \"oneOf\": [{ \"const\": \"narrative\", \"title\": \"Narrative\" }, "
            + "{ \"const\": \"table\", \"title\": \"Table\" }, { \"const\": \"omit\", \"title\": \"Omit\" }] }"));

        AssertAccepted(files);
    }

    // ---- The values ----------------------------------------------------------------------------

    [Fact]
    public void AValueNotTheSubjects_IsS04045() =>
        AssertRefusal(When(1, "narrative", "prose"), "S04-045",
            $"Case 'route' branch 1 lists \"prose\", which is not one of item.value.kind's values ({Values}).", Route);

    [Fact]
    public void AValueInTwoBranches_IsS04046() =>
        AssertRefusal(When(3, "omit", "table"), "S04-046",
            "Case 'route' lists \"table\" in branches 2 and 3; a value belongs to one branch.", Route);

    [Fact]
    public void UnlistedValuesWithNoElse_IsS04047()
    {
        var one = ResolveFixture.Conditional().WithJson(Route, h => h["branches"]!.AsArray().RemoveAt(2));
        AssertRefusal(one, "S04-047",
            "Case 'route' doesn't list \"omit\" and has no else; a case has a branch for every value: add a branch, an empty branch, or an else.",
            Route);

        var two = ResolveFixture.Conditional()
            .WithJson(Route, h =>
            {
                h["branches"]!.AsArray().RemoveAt(2);
                h["branches"]!.AsArray().RemoveAt(1);
            })
            .Without("nodes/sections_draft/route/table_draft.md");
        AssertRefusal(two, "S04-047",
            "Case 'route' doesn't list \"table\", \"omit\" and has no else; a case has a branch for every value: add a branch, an empty branch, or an else.",
            Route);

        // an else takes what the branches don't list
        AssertAccepted(WithElse(ResolveFixture.Conditional().WithJson(Route, h => h["branches"]!.AsArray().RemoveAt(2))));
    }

    // ---- A subject that can be null ------------------------------------------------------------

    /// <summary><see cref="ResolveFixture.Conditional"/> whose sections' <c>kind</c> may be null.</summary>
    private static Dictionary<string, byte[]> WithNullableKind() =>
        ResolveFixture.Conditional().WithJson("formats/section_request.json", f => f["properties"]!["kind"] = JsonNode.Parse(
            "{ \"type\": [\"string\", \"null\"], \"enum\": [\"narrative\", \"table\", \"omit\", null], \"title\": \"Kind\" }"));

    /// <summary>
    /// <see cref="ResolveFixture.Conditional"/> with the input <c>urgency</c> (routine or urgent; <paramref name="required"/>
    /// or not) and, at the top level, the case <c>by_urgency</c> on it.
    /// </summary>
    private static Dictionary<string, byte[]> WithUrgency(bool required) =>
        ResolveFixture.Conditional()
            .WithJson("manifest.json", m => m["inputs"]!["urgency"] = JsonNode.Parse(
                "{ \"label\": \"Urgency\", \"description\": null, \"format\": { \"type\": \"string\", \"enum\": [\"routine\", \"urgent\"] }, "
                + $"\"required\": {(required ? "true" : "false")}, \"channel\": null }}"))
            .WithJson("nodes/index.json", i => i["items"]!.AsArray().Add("by_urgency"))
            .With("nodes/by_urgency/index.json", Json(Node("case", "Routing the note by its urgency", "\"plaintext\"", false, true,
                "\"case\": \"input.urgency.value\", \"branches\": ["
                + "{ \"when\": [\"urgent\"], \"nodes\": [\"urgent_note\"], \"ret\": \"node.by_urgency.urgent_note\" }, "
                + "{ \"when\": [\"routine\"], \"nodes\": [], \"ret\": null }], \"else\": null")))
            .With("nodes/by_urgency/urgent_note.md", Agent("Writing the urgent note", "\"plaintext\"",
                "Write a short urgent note for Dr A. Sample about Margaret Okafor.\n"));

    [Fact]
    public void ANullableSubject_NeedsAnElse()
    {
        AssertRefusal(WithNullableKind(), "S04-075",
            "Case 'route' switches on item.value.kind, which can be null, and has no else; a null subject goes to the else: add one.",
            Route);
        AssertAccepted(WithElse(WithNullableKind()));
    }

    [Fact]
    public void ASubjectFromAnInputNotRequired_NeedsAnElse()
    {
        AssertAccepted(WithUrgency(required: true));
        AssertRefusal(WithUrgency(required: false), "S04-075",
            "Case 'by_urgency' switches on input.urgency.value, which can be null, and has no else; a null subject goes to the else: add one.",
            "nodes/by_urgency/index.json");
    }

    /// <summary>
    /// <see cref="ResolveFixture.Conditional"/> with, at the top level, the agent <c>triage</c> (a <c>section_request</c>,
    /// run when <paramref name="condition"/> holds) and the case <c>by_kind</c> on <c>node.triage.value.kind</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithTriage(string condition) =>
        ResolveFixture.Conditional()
            .WithJson("nodes/index.json", i =>
            {
                i["items"]!.AsArray().Add("triage");
                i["items"]!.AsArray().Add("by_kind");
            })
            .With("nodes/triage.md", Md(Node("agent", "Triaging the note", "\"section_request\"", false, true,
                    $"\"if\": {condition}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
                "Say what kind of section the note for Margaret Okafor most needs.\n"))
            .With("nodes/by_kind/index.json", Json(Node("case", "Routing the note by its triage", "\"plaintext\"", false, true,
                "\"case\": \"node.triage.value.kind\", \"branches\": ["
                + "{ \"when\": [\"narrative\", \"table\"], \"nodes\": [\"kind_note\"], \"ret\": \"node.by_kind.kind_note\" }, "
                + "{ \"when\": [\"omit\"], \"nodes\": [], \"ret\": null }], \"else\": null")))
            .With("nodes/by_kind/kind_note.md", Agent("Writing the note of its kind", "\"plaintext\"",
                "Write the note for Dr A. Sample.\n"));

    [Fact]
    public void ASubjectFromASkippableNode_NeedsAnElse()
    {
        // triage runs only when billing is asked for, so it may be skipped and its kind be null (D14)
        AssertAccepted(WithTriage("null"));
        AssertRefusal(WithTriage("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"), "S04-075",
            "Case 'by_kind' switches on node.triage.value.kind, which can be null, and has no else; a null subject goes to the else: add one.",
            "nodes/by_kind/index.json");
    }

    // ---- The examples --------------------------------------------------------------------------

    [Fact]
    public void TheMigratedExamples_Pass()
    {
        // part 04 § 5.4's three files, verbatim
        var files = ResolveFixture.Conditional()
            .With(Route, """
                { "kind": "case", "producer": "computed", "label": "Routing a section by its kind", "description": null,
                  "format": "plaintext",
                  "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
                  "case": "item.value.kind",
                  "branches": [
                    { "when": ["narrative"], "nodes": ["narrative_draft"], "ret": "node.sections_draft.route.narrative_draft" },
                    { "when": ["table"],     "nodes": ["table_draft"],     "ret": "node.sections_draft.route.table_draft" },
                    { "when": ["omit"],      "nodes": [],                  "ret": null } ],
                  "else": null }

                """)
            .With("nodes/patient_summaries/narrative_only/index.json", """
                { "kind": "if", "producer": "computed", "label": "Narrative sections only", "description": null,
                  "format": "plaintext",
                  "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
                  "if": { "===": [ { "var": "item.value.kind" }, "narrative" ] },
                  "then": { "nodes": ["patient_summary"], "ret": "node.patient_summaries.narrative_only.patient_summary" },
                  "else": null }

                """)
            .With("nodes/standing_sections/applies/index.json", """
                { "kind": "if", "producer": "computed", "label": "Holding the billing section back unless asked", "description": null,
                  "format": "plaintext",
                  "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
                  "if": { "or": [ { "!==": [ { "var": "item.id" }, "billing" ] },
                                  { "===": [ { "var": "input.include_billing.value" }, true ] } ] },
                  "then": { "nodes": ["standing_draft"], "ret": "node.standing_sections.applies.standing_draft" },
                  "else": null }

                """);

        AssertAccepted(files);
        var (package, _, _) = ResolveFixture.Tree(files);
        Assert.Equal(["omit"], ScopeTree.EmptyValues((SourceCase)ResolveFixture.Node(package, "node.sections_draft.route")));
    }

    [Fact]
    public void AnySubject_IsTypedOrRefusedOnce_NeverThrows()
    {
        string[] written =
        [
            "item", "item.value", "item.parent", "node", "node.route", "node.route.state", "node.sections_draft.items",
            "input.sections.value", "input.sections", "run.date", "pass.number", "param.x.value", "check.x.value",
            "snippet.x", "profile.x.y", "é.ü", "item.value.kind.title", string.Join('.', Enumerable.Repeat("item", 300)),
        ];
        foreach (var text in written)
        {
            // a subject that is an enum of other values is held to them branch by branch; anything else is one sentence
            var result = ResolveFixture.Run(Set("case", JsonValue.Create(text)!.ToJsonString()));
            var subject = result.Refusals.Where(r => r.Id is not ("S04-045" or "S04-047")).ToList();
            Assert.True(subject.Count <= 1, text + ": " + ResolveFixture.Said(result));
        }
    }

    // ---- Always valued -------------------------------------------------------------------------

    private static readonly JsonObject NoHeader = new();

    private static SourceLeaf Leaf(string id, bool conditional, string kind = "agent") =>
        new(id, $"node.x.{id}", $"nodes/x/{id}.md", kind,
            new JsonObject { ["if"] = conditional ? JsonNode.Parse("{ \"var\": \"input.include_billing.value\" }") : null }, "");

    private static SourceBlock Block(SourceNode node) => new([node], node.Key);

    private static readonly SourceBlock Empty = new([], null);

    public static TheoryData<string, bool> Kinds => new()
    {
        { "a leaf", true },
        { "a leaf with an if", false },
        { "a call", true },
        { "a for", true },
        { "a while", true },
        { "a checkFirst while", false },
        { "an if with an else", true },
        { "an if with no else", false },
        { "an if whose else may be skipped", false },
        { "a case", true },
        { "a case with an empty branch", false },
        { "a case whose branch may be skipped", false },
        { "a case whose else may be skipped", false },
    };

    private static SourceNode Of(string kind)
    {
        var a = Leaf("a", false);
        var b = Leaf("b", false);
        var skipped = Leaf("c", true);
        return kind switch
        {
            "a leaf" => a,
            "a leaf with an if" => skipped,
            "a call" => Leaf("call", false, "call"),
            "a for" => new SourceFor("x", "node.x", "nodes/x", "for", NoHeader, "input.sections.value", false, null, null, null, null, Block(a)),
            "a while" => new SourceWhile("x", "node.x", "nodes/x", "while", NoHeader, JsonValue.Create(true)!, 3, null, false, Block(a)),
            "a checkFirst while" => new SourceWhile("x", "node.x", "nodes/x", "while", NoHeader, JsonValue.Create(true)!, 3, null, true, Block(a)),
            "an if with an else" => new SourceIf("x", "node.x", "nodes/x", "if", NoHeader, JsonValue.Create(true)!, Block(a), Block(b)),
            "an if with no else" => new SourceIf("x", "node.x", "nodes/x", "if", NoHeader, JsonValue.Create(true)!, Block(a), null),
            "an if whose else may be skipped" => new SourceIf("x", "node.x", "nodes/x", "if", NoHeader, JsonValue.Create(true)!, Block(a), Block(skipped)),
            "a case" => Case([new(["one"], Block(a)), new(["two"], Block(b))], null),
            "a case with an empty branch" => Case([new(["one"], Block(a)), new(["two"], Empty)], null),
            "a case whose branch may be skipped" => Case([new(["one"], Block(a)), new(["two"], Block(skipped))], null),
            _ => Case([new(["one"], Block(a))], Block(skipped)),
        };
    }

    private static SourceCase Case(IReadOnlyList<SourceBranch> branches, SourceBlock? otherwise) =>
        new("x", "node.x", "nodes/x", "case", NoHeader, "item.value.kind", branches, otherwise);

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AlwaysValued(string kind, bool expected) => Assert.Equal(expected, ScopeTree.AlwaysValued(Of(kind)));

    [Fact]
    public void EmptyValues_AreTheEmptyBranchesValues_InOrder()
    {
        var a = Leaf("a", false);
        var c = Case([new(["one", "two"], Empty), new(["three"], Block(a)), new(["four"], Empty)], null);

        Assert.Equal(["one", "two", "four"], ScopeTree.EmptyValues(c));
        Assert.Empty(ScopeTree.EmptyValues((SourceCase)Of("a case")));
    }

    [Fact]
    public void AnIfWhoseElseMayBeSkipped_IsReadAsNullable()
    {
        // D14 through AlwaysValued: the if's value is its body's ret, which may be skipped
        var files = ResolveFixture.Conditional()
            .WithJson("nodes/patient_summaries/narrative_only/index.json", h => h["else"] = JsonNode.Parse(
                "{ \"nodes\": [\"brief_summary\"], \"ret\": \"node.patient_summaries.narrative_only.brief_summary\" }"))
            .With("nodes/patient_summaries/narrative_only/brief_summary.md", Md(Node("agent", "Summarising briefly", "\"plaintext\"", false, true,
                    "\"if\": { \"===\": [{ \"var\": \"item.value.kind\" }, \"table\"] }, \"onInvalid\": \"retry\", \"failIfEmpty\": null, "
                    + "\"parts\": null, \"ordered\": false"),
                "Summarise the section '{{ item.value.title }}' in one line.\n"));
        var (package, _, tree) = ResolveFixture.Tree(files);
        var frame = tree.FrameOf(ResolveFixture.Node(package, "node.patient_summaries.narrative_only"));

        var read = tree.Resolve(ReferenceString.Parse("node.narrative_only.content")!, frame, "Node 'x'", "nodes/x.md", true);

        Assert.True(read.Nullable);
    }

    [Fact]
    public void AlwaysValued_TakesNoNull()
    {
        Assert.Throws<ArgumentNullException>(() => ScopeTree.AlwaysValued(null!));
        Assert.Throws<ArgumentNullException>(() => ScopeTree.EmptyValues(null!));
    }
}
