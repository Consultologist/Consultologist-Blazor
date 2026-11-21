using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.2 to 2.4, 2.6, 3.2, 5, 6.3, 7.2; part 08 § 4.2; D1, D12, D13):
/// <see cref="BodyComposer.Compose"/>, the run-time render of one entry's body from its published plan: its reads
/// through <c>names</c>, the snippets and parts placed and not placed, the renderers with one sub-row per pipe step,
/// the dialect's null rules and the invariant culture, the three limits, and never a value rendered or an exception.
/// Every package is published and loaded as the engine loads it (<see cref="RunFixture.Plan"/>). Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class BodyComposerTests
{
    private const string Draft = "Margaret Okafor, 58, referred by Dr A. Sample for fatigue (fictional).";

    private static readonly IReadOnlyDictionary<string, bool> NothingPlaced = new Dictionary<string, bool>(StringComparer.Ordinal);

    internal static Composition Compose(
        RunPlan plan, string entryKey, JsonObject scope, IReadOnlyDictionary<string, bool>? placed = null, string? function = null, int steps = 100_000) =>
        BodyComposer.Compose(plan, new ComposeRequest(entryKey, function, scope, placed ?? NothingPlaced, steps));

    /// <summary>The data a body is given: each path's value at the path itself, as <see cref="RunScope"/> places it.</summary>
    internal static JsonObject Scope(params (string Path, JsonNode? Value)[] paths)
    {
        var scope = new JsonObject();
        foreach (var (path, value) in paths)
        {
            var segments = path.Split('.');
            var at = scope;
            foreach (var segment in segments[..^1])
            {
                if (at[segment] is not JsonObject next)
                {
                    next = new JsonObject();
                    at[segment] = next;
                }

                at = next;
            }

            at[segments[^1]] = value;
        }

        return scope;
    }

    internal static Dictionary<string, bool> Placed(params string[] sites) => sites.ToDictionary(s => s, _ => true, StringComparer.Ordinal);

    internal static string Composed(Composition composition)
    {
        Assert.True(composition.Failure == null, composition.Failure);
        Assert.False(composition.TooManySteps);
        Assert.NotNull(composition.Text);
        return composition.Text!;
    }

    private static void AssertFailed(Composition composition, string id, string sentence)
    {
        Assert.Null(composition.Text);
        Assert.False(composition.TooManySteps);
        Assert.Equal(sentence, composition.Failure);
        V31Contract.AssertSentence(new SourceRefusal(id, composition.Failure!, ""));
    }

    // ---- reads --------------------------------------------------------------------------------

    [Fact]
    public void ABody_RendersItsReads()
    {
        var plan = RunFixture.Plan(Package().WithAgent("draft",
            "Draft the note for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n\nThe plan: {{ node.plan.content }}\n"));

        var composition = Compose(plan, "node.draft", Scope(("input.consult_draft.content", Draft), ("node.plan.content", "History, then plan.")));

        Assert.Equal($"Draft the note for Dr A. Sample.\n\n{Draft}\n\nThe plan: History, then plan.\n", Composed(composition));
        Assert.Null(composition.NeutralText);
        Assert.Empty(composition.Spots);
        Assert.Empty(composition.Renders);
        Assert.Empty(composition.Macros);
        Assert.Empty(composition.Units);
    }

    [Fact]
    public void ALocalName_ReadsItsRootedKey()
    {
        // Area 06's item 13: the judge writes node.draft_summary at its place; the run's scope holds the rooted key (D1).
        var plan = RunFixture.Plan(ResolveFixture.NestedNote());
        var scope = Scope(("node.refine.draft_summary.content", "Margaret Okafor is recovering well."));

        var composition = Compose(plan, "node.refine.judge", scope);

        Assert.Equal("Say whether the summary is good or needs another pass.\n\nMargaret Okafor is recovering well.\n", Composed(composition));
    }

    // ---- placements ---------------------------------------------------------------------------

    [Fact]
    public void APlacedSnippet_IsSpliced_AndOneNotPlaced_LeavesAnEmptyLine()
    {
        // Part 05 § 2.6's rows: a block alone on its line, an inline one against a word, one alone on its line indented
        // (the indentation before it stays; the unit's later lines are its own, not indented again).
        var plan = RunFixture.Plan(Package()
            .WithSnippet("tone", "Keep a calm tone.")
            .WithSnippet("aside", " in brief")
            .WithSnippet("sign", "Sign as Dr A. Sample,\nconsultant.")
            .WithAgent("draft", "Before\n{{~ snippet.tone.content }}\nWrite about diabetes{{ snippet.aside.content }}.\n  {{ snippet.sign.content }}\nAfter\n"));

        var all = Compose(plan, "node.draft", new JsonObject(), Placed("snippet.tone", "snippet.aside", "snippet.sign"));
        var none = Compose(plan, "node.draft", new JsonObject());
        var declined = Compose(plan, "node.draft", new JsonObject(), new Dictionary<string, bool>
        {
            ["snippet.tone"] = false, ["snippet.aside"] = true, ["snippet.sign"] = false,
        });

        Assert.Equal("Before\nKeep a calm tone.\nWrite about diabetes in brief.\n  Sign as Dr A. Sample,\nconsultant.\nAfter\n", Composed(all));
        Assert.Equal("Before\n\nWrite about diabetes.\n  \nAfter\n", Composed(none));
        Assert.Equal("Before\n\nWrite about diabetes in brief.\n  \nAfter\n", Composed(declined));
        Assert.Equal(
            [
                new ComposedUnit("snippet.tone", CanonicalJson.HashText("Keep a calm tone.")),
                new ComposedUnit("snippet.aside", CanonicalJson.HashText(" in brief")),
                new ComposedUnit("snippet.sign", CanonicalJson.HashText("Sign as Dr A. Sample,\nconsultant.")),
            ],
            all.Units);
        Assert.Empty(none.Units);
        Assert.Equal([new ComposedUnit("snippet.aside", CanonicalJson.HashText(" in brief"))], declined.Units);
    }

    /// <summary>
    /// Part 05 § 7.2's example: the loop <c>standards</c> over the snippet folder <c>standards</c>, whose agent
    /// <c>section_instructions</c> places the part <c>no_standard</c> (decided on <c>item.content</c>).
    /// </summary>
    private static Dictionary<string, byte[]> SectionInstructions() =>
        ResolveFixture.WithFormats(Minimal(), "standard_section")
            .Listed("snippets/index.json", "standards")
            .With("snippets/standards/index.json", Index(["hpi", "assessment_plan"],
                description: "The standard sections of a consult note, in order.", extra: ", \"format\": \"standard_section\""))
            .With("snippets/standards/hpi.md", Md("{ \"title\": \"History of Present Illness\" }",
                "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n"))
            .With("snippets/standards/assessment_plan.md", Md("{ \"title\": \"Assessment and Plan\" }", ""))
            .Listed("nodes/index.json", "standards")
            .With("nodes/standards/index.json", Json(SourceFixture.Node("for", "Standards", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section_instructions\"], \"ret\": \"node.standards.section_instructions\"")))
            .With("nodes/standards/section_instructions.md", Agent("Applying section instructions", Plain,
                "Requested section:\n{{ item.value.title }}\n\nSection standard and section-specific instructions:\n{{~ part.no_standard }}\n{{ item.content }}\n"))
            .WithJson("nodes/standards/section_instructions.md", h => h["parts"] = JsonNode.Parse(
                "{ \"no_standard\": { \"if\": { \"blank\": [ { \"var\": \"item.content\" } ] }, "
                + "\"text\": [ \"No changes. Preserve the patient-updated draft's clinical content and\", "
                + "\"produce polished final prose for this section.\" ] } }"));

    [Fact]
    public void APart_IsSplicedWhenItsIfHeld()
    {
        var plan = RunFixture.Plan(SectionInstructions());
        var scope = Scope(("item.value.title", "Assessment and Plan"), ("item.content", ""));

        var held = Compose(plan, "node.standards.section_instructions", scope, Placed("part.no_standard"));
        var not = Compose(plan, "node.standards.section_instructions", scope, new Dictionary<string, bool> { ["part.no_standard"] = false });

        const string part = "No changes. Preserve the patient-updated draft's clinical content and\nproduce polished final prose for this section.";
        Assert.Equal(
            $"Requested section:\nAssessment and Plan\n\nSection standard and section-specific instructions:\n{part}\n\n",
            Composed(held));
        Assert.Equal("Requested section:\nAssessment and Plan\n\nSection standard and section-specific instructions:\n\n\n", Composed(not));
        Assert.Equal([new ComposedUnit("part.no_standard", CanonicalJson.HashText(part))], held.Units);
        Assert.Empty(not.Units);
    }

    [Fact]
    public void ASnippetInASnippet_IsSpliced()
    {
        var plan = RunFixture.Plan(Package()
            .WithSnippet("clinician", "Dr A. Sample")
            .WithSnippet("follow_up", "Refer back to {{ snippet.clinician.content }} in six weeks.")
            .WithAgent("draft", "Plan:\n{{~ snippet.follow_up.content }}\n"));

        var both = Compose(plan, "node.draft", new JsonObject(), Placed("snippet.follow_up", "snippet.clinician"));
        var outer = Compose(plan, "node.draft", new JsonObject(), Placed("snippet.follow_up"));

        Assert.Equal("Plan:\nRefer back to Dr A. Sample in six weeks.\n", Composed(both));
        Assert.Equal("Plan:\nRefer back to  in six weeks.\n", Composed(outer));
        Assert.Equal(
            [
                new ComposedUnit("snippet.follow_up", CanonicalJson.HashText("Refer back to Dr A. Sample in six weeks.")),
                new ComposedUnit("snippet.clinician", CanonicalJson.HashText("Dr A. Sample")),
            ],
            both.Units);
    }

    [Fact]
    public void AFolderItemInALoop_ReadsItemContent()
    {
        // In a loop over a snippet folder, item.content is the item's body and item.value its header (part 05 § 6.2).
        var plan = RunFixture.Plan(SectionInstructions());
        const string body = "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n";
        var scope = Scope(("item.value.title", "History of Present Illness"), ("item.content", body));

        var composition = Compose(plan, "node.standards.section_instructions", scope);

        Assert.Equal(
            $"Requested section:\nHistory of Present Illness\n\nSection standard and section-specific instructions:\n\n{body}\n",
            Composed(composition));
    }

    // ---- renderers ----------------------------------------------------------------------------

    /// <summary><see cref="ConceptSamples.ConceptListJson"/> as the run holds it: each concept with its <c>source</c> (R3).</summary>
    private static JsonNode Concepts()
    {
        var value = JsonNode.Parse(ConceptSamples.ConceptListJson)!;
        foreach (var concept in value["concepts"]!.AsArray())
            concept!["source"] = ConceptSamples.Source;
        return value;
    }

    private static Dictionary<string, byte[]> WithTerms(Dictionary<string, byte[]> files) =>
        files.WithAgent("terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"");

    [Fact]
    public void TheBuiltInRenderers_GiveTheCSharpBytes()
    {
        var plan = RunFixture.Plan(WithTerms(Package()).WithAgent("summary",
            "Terms:\n{{ node.terms.value | renderer.concept_bullets }}\nContext:\n{{ node.terms.value | renderer.concept_context }}\n"));
        var concepts = ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, ConceptSamples.Source);
        var bullets = ConceptRenderers.Bullets(concepts);
        var context = ConceptRenderers.Context(concepts);

        var composition = Compose(plan, "node.summary", Scope(("node.terms.value", Concepts())));

        Assert.Equal($"Terms:\n{bullets}\nContext:\n{context}\n", Composed(composition));
        var renderers = plan.Document["renderers"]!;
        Assert.Equal(
            [
                new ComposedRender(1, "renderer.concept_bullets", (string)renderers["renderer.concept_bullets"]!["bodyHash"]!, "node.terms.value",
                    CanonicalJson.Hash(Concepts()), "2 concepts", CanonicalJson.HashText(bullets), bullets.EnumerateRunes().Count(), false),
                new ComposedRender(2, "renderer.concept_context", (string)renderers["renderer.concept_context"]!["bodyHash"]!, "node.terms.value",
                    CanonicalJson.Hash(Concepts()), "2 concepts", CanonicalJson.HashText(context), context.EnumerateRunes().Count(), false),
            ],
            composition.Renders);
        Assert.Equal(CanonicalJson.HashText(ResolveFixture.Published().BuiltInRenderers["concept_bullets"].Body), composition.Renders[0].TemplateHash);
    }

    [Fact]
    public void AChain_GivesOneSubRowPerStep()
    {
        // A chain gives one sub-row per step in pipe order, the later step's source the step before it; a renderer inside
        // a for gives one sub-row per iteration, each with the entry's number for that pipe (part 05 § 5.5, part 08 § 4.2).
        var plan = RunFixture.Plan(WithTerms(Package())
            .WithRenderer("quote", "\"markdown_block\"", "> {{ value }}")
            .WithRenderer("tag", "\"string\"", "[{{ value }}]", "plaintext")
            .WithAgent("summary",
                "{{ node.terms.value | renderer.concept_bullets | renderer.quote }}\n"
                + "{{ for t in node.terms.value.concepts }}{{ t.term | renderer.tag }}{{ end }}\n"));
        var bullets = ConceptRenderers.Bullets(ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, ConceptSamples.Source));
        var quoted = "> " + bullets;

        var composition = Compose(plan, "node.summary", Scope(("node.terms.value", Concepts())));

        Assert.Equal($"{quoted}\n[Diabetes mellitus][Family support strong]\n", Composed(composition));
        var renders = composition.Renders;
        Assert.Equal([1, 2, 3, 3], renders.Select(r => r.N));
        Assert.Equal(["renderer.concept_bullets", "renderer.quote", "renderer.tag", "renderer.tag"], renders.Select(r => r.Renderer));
        Assert.Equal(["node.terms.value", "render:1", "t.term", "t.term"], renders.Select(r => r.Source));
        Assert.Equal(CanonicalJson.HashText(CanonicalJson.Write(Concepts())), renders[0].InputHash);
        Assert.Equal(CanonicalJson.HashText(bullets), renders[0].OutputHash);
        Assert.Equal(CanonicalJson.HashText(bullets), renders[1].InputHash);
        Assert.Equal(CanonicalJson.HashText(quoted), renders[1].OutputHash);
        Assert.Equal(quoted.EnumerateRunes().Count(), renders[1].OutputLength);
        Assert.Null(renders[1].InputSize);
        Assert.Equal(CanonicalJson.HashText("Diabetes mellitus"), renders[2].InputHash);
        Assert.Equal(CanonicalJson.HashText("[Family support strong]"), renders[3].OutputHash);
        Assert.Equal((string)plan.Document["renderers"]!["renderer.quote"]!["bodyHash"]!, renders[1].TemplateHash);
        Assert.All(renders, r => Assert.False(r.RunDependent));
    }

    [Fact]
    public void Renders_AreNumberedAsTheEntrysRenders()
    {
        // The entry's renders count the steps of every unit it places, in order of appearance (Task 7): 1, the snippet's 2,
        // then 3. A unit not placed renders nothing and keeps its number, so the body's later step is still 3.
        var plan = RunFixture.Plan(WithTerms(Package())
            .WithSnippet("context", "{{ node.terms.value | renderer.concept_context }}", "markdown_block")
            .WithAgent("summary",
                "Before:\n{{ node.terms.value | renderer.concept_bullets }}\n{{~ snippet.context.content }}\nAfter:\n"
                + "{{ node.terms.value | renderer.concept_bullets }}\n"));
        var scope = Scope(("node.terms.value", Concepts()));
        var entry = plan.Entries["node.summary"].Json["renders"]!.AsArray()
            .Select(r => ((int)r!["n"]!, "renderer." + (string)r["renderer"]!, (string)r["source"]!)).ToList();

        var placed = Compose(plan, "node.summary", scope, Placed("snippet.context"));
        var not = Compose(plan, "node.summary", scope);

        Assert.Equal(entry, placed.Renders.Select(r => (r.N, r.Renderer, r.Source)));
        Assert.Equal([1, 3], not.Renders.Select(r => r.N));
        var context = ConceptRenderers.Context(ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, ConceptSamples.Source));
        Assert.Equal([new ComposedUnit("snippet.context", CanonicalJson.HashText(context))], placed.Units);
    }

    [Fact]
    public void AMacroPlacement_RendersNothingYet_AndItsInstructionKeepsItsNumber()
    {
        // The macros' task renders a placed macro's instruction; here macro.<…> renders nothing (§ 2.6's empty line), and a
        // renderer step in an instruction keeps its number in the entry's renders: the body's later step is 3.
        var plan = RunFixture.Plan(WithTerms(Package())
            .Listed("macros/index.json", "m")
            .With("macros/m.md", Md(
                "{ \"label\": \"Terms\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place {{ macro.m.content }} after {{ node.terms.value | renderer.concept_bullets }}.\"] }",
                "Reviewed with Dr A. Sample.\n"))
            .Listed("macros/index.json", "f")
            .With("macros/f/index.json", Index(["n"], extra: ", \"if\": null"))
            .With("macros/f/n.md", Md(
                "{ \"label\": \"Follow-up\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the follow-up line alone after the plan:\", \"{{~ macro.f.n.content }}\"] }",
                "Follow up with Dr A. Sample in six weeks.\n"))
            .WithAgent("draft",
                "A {{ node.terms.value | renderer.concept_bullets }}\n{{~ macro.m }}\n{{~ macro.f.n }}\nB {{ node.terms.value | renderer.concept_bullets }}\n"));
        var bullets = ConceptRenderers.Bullets(ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, ConceptSamples.Source));
        Assert.Equal([1, 2, 3], plan.Entries["node.draft"].Json["renders"]!.AsArray().Select(r => (int)r!["n"]!));

        var composition = Compose(plan, "node.draft", Scope(("node.terms.value", Concepts())),
            Placed("macro.m", "macro-folder.f", "macro.f.n"));

        Assert.Equal($"A {bullets}\n\n\nB {bullets}\n", Composed(composition));
        Assert.Empty(composition.Macros);
        Assert.Empty(composition.Units);
        Assert.Equal([1, 3], composition.Renders.Select(r => r.N));
    }

    [Fact]
    public void ASnippetHoldingARawBlock_IsSplicedByteForByte()
    {
        var plan = RunFixture.Plan(Package()
            .WithSnippet("literal", "Keep {%{ {{ node.x.content }} }%} as written.")
            .WithAgent("draft", "Draft:\n{{~ snippet.literal.content }}\n"));

        Assert.Equal("Draft:\nKeep  {{ node.x.content }}  as written.\n",
            Composed(Compose(plan, "node.draft", new JsonObject(), Placed("snippet.literal"))));
    }

    [Fact]
    public void ARendererThatLoops_PipedInAFor_SpendsTheSharedBudget()
    {
        // 200 drafts, each piping 600 concepts into a renderer that loops over them: no for passes 1,000, but the
        // composition's 120,200 iterations pass the budget of 100,000 (its loop writes nothing, so the output limit cannot
        // stop it first).
        var plan = RunFixture.Plan(WithTerms(Package())
            .WithLoop("drafts", "node.plan.value.sections", "draft", "Draft the section '{{ item.value.title }}'.\n")
            .WithRenderer("terms", "\"concept_list\"", "Terms:{{ for c in value.concepts }}{{ end }}", "plaintext")
            .WithAgent("summary", "{{ for d in node.drafts.value }}{{ node.terms.value | renderer.terms }}{{ end }}\n"));
        var concept = Concepts()["concepts"]![0]!;
        var many = new JsonObject { ["concepts"] = new JsonArray(Enumerable.Range(0, 600).Select(_ => concept.DeepClone()).ToArray()) };
        JsonObject ScopeOf(int drafts) => Scope(
            ("node.drafts.value", new JsonArray(Enumerable.Range(0, drafts).Select(_ => (JsonNode?)JsonValue.Create("x")).ToArray())),
            ("node.terms.value", many.DeepClone()));

        var stopped = Compose(plan, "node.summary", ScopeOf(200));
        Assert.True(stopped.TooManySteps, stopped.Failure);
        Assert.Null(stopped.Text);
        Assert.Null(stopped.Failure);
        Assert.Equal(2, Compose(plan, "node.summary", ScopeOf(2)).Renders.Count);
    }

    [Fact]
    public void TheContext_HoldsTheSevenBuiltIns_AndNoOther()
    {
        // A second line behind TemplateSyntax: the run-time context has only the dialect's built-ins (part 05 § 2.2).
        var builtins = TemplateValues.BuiltIns();
        var names = builtins.SelectMany(root => ((Scriban.Runtime.ScriptObject)root.Value!).Select(f => $"{root.Key}.{f.Key}")).Order(StringComparer.Ordinal);
        Assert.Equal(
            ["array.join", "array.size", "date.to_string", "string.capitalize", "string.downcase", "string.strip", "string.upcase"],
            names);
    }

    [Fact]
    public void ARendererInARunSpot_IsRunDependent()
    {
        var plan = RunFixture.Plan(Package()
            .WithRenderer("long_date", "\"date\"", "{{ value | date.to_string \"%A %d %B %Y\" }}", "plaintext")
            .WithAgent("draft", "Dated {{ run.date | renderer.long_date }}.\n")
            .RunDependent("nodes/draft.md"));

        var composition = Compose(plan, "node.draft", Scope(("run.date", "2026-11-21")));

        Assert.Equal("Dated Saturday 21 November 2026.\n", Composed(composition));
        var render = Assert.Single(composition.Renders);
        Assert.True(render.RunDependent);
        Assert.Equal("run.date", render.Source);
        Assert.Null(render.InputSize);
    }

    [Fact]
    public void AFunctionsOwnSnippet_IsFoundBeforeThePackages()
    {
        var files = ResolveFixture.Functions()
            .WithSnippet("tone", "Write for Dr A. Sample.")
            .With("functions/draft_section/snippets/index.json", Index(["tone"]))
            .With("functions/draft_section/snippets/tone.md", Md(
                "{ \"label\": \"Tone\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }", "Write for Margaret Okafor."))
            .With("functions/draft_section/patient_draft.md", Agent("Drafting from the patient's facts", Plain,
                "Using the consult draft below, write for this section.\n{{~ snippet.tone.content }}\n\n{{ param.draft.content }}\n"));
        var plan = RunFixture.Plan(files);

        var composition = Compose(plan, "node.patient_draft", Scope(("param.draft.content", Draft)), Placed("snippet.tone"), "draft_section");

        Assert.Equal($"Using the consult draft below, write for this section.\nWrite for Margaret Okafor.\n\n{Draft}\n", Composed(composition));
    }

    // ---- the dialect's values -----------------------------------------------------------------

    /// <summary>
    /// The loop <c>drafts</c> over the plan's sections (a list of <c>plaintext</c>), a number and a boolean, and the
    /// agent <c>summary</c> with <paramref name="body"/>, declared run-dependent when it reads a run fact.
    /// </summary>
    private static RunPlan Values(string body)
    {
        var files = Package()
            .WithLoop("drafts", "node.plan.value.sections", "draft", "Draft the section '{{ item.value.title }}'.\n")
            .WithComputed("count", "\"number\"", "{ \"size\": [{ \"var\": \"input.consult_draft.content\" }] }")
            .WithComputed("flag", "\"boolean\"", "{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }")
            .WithAgent("summary", body);
        return RunFixture.Plan(body.Contains("run.", StringComparison.Ordinal) ? files.RunDependent("nodes/summary.md") : files);
    }

    [Fact]
    public void Nulls_FollowTheDialect()
    {
        var plan = Values(
            "A{{ node.plan.content }}B{{ node.plan.content | string.upcase }}C {{ run.date }} {{ run.date | date.to_string \"%d %B %Y\" }} "
            + "{{ node.count.value }} {{ node.flag.value }}\n{{ node.drafts.value | array.join \", \" }}\n");
        JsonObject ScopeWith(JsonNode? drafts) => Scope(
            ("node.plan.content", null), ("run.date", "2026-11-21"), ("node.count.value", JsonNode.Parse("2.50")),
            ("node.flag.value", false), ("node.drafts.value", drafts));

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("ABC 2026-11-21 21 November 2026 2.5 false\nHistory, Plan\n",
                Composed(Compose(plan, "node.summary", ScopeWith(new JsonArray("History", "Plan")))));
            Assert.Equal("ABC 2026-11-21 21 November 2026 2.5 false\n\n", Composed(Compose(plan, "node.summary", ScopeWith(null))));
            AssertFailed(Compose(plan, "node.summary", ScopeWith(new JsonArray("History", null))),
                "A07-006", "Node 'summary' stopped rendering: array.join met a list with an empty element.");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void EachLimit_StopsTheRenderWithItsSentence()
    {
        static JsonArray Drafts(int count) => new(Enumerable.Range(0, count).Select(_ => (JsonNode?)JsonValue.Create("x")).ToArray());

        // 1,000 iterations per for (S05-012, with the for's line).
        var loop = Values("Drafts:\n{{ for d in node.drafts.value }}{{ d }}{{ end }}\n");
        Assert.Equal($"Drafts:\n{new string('x', 1000)}\n", Composed(Compose(loop, "node.summary", Scope(("node.drafts.value", Drafts(1000))))));
        AssertFailed(Compose(loop, "node.summary", Scope(("node.drafts.value", Drafts(1001)))),
            "S05-012", "Node 'summary' stopped rendering: the for at line 2 reached 1,000 iterations.");

        // The step budget, across the whole composition: three nested fors over 200 items.
        var nested = Values(
            "{{ for a in node.drafts.value }}{{ for b in node.drafts.value }}{{ for c in node.drafts.value }}{{ end }}{{ end }}{{ end }}\n");
        var stopped = Compose(nested, "node.summary", Scope(("node.drafts.value", Drafts(200))), steps: 100_000);
        Assert.True(stopped.TooManySteps);
        Assert.Null(stopped.Text);
        Assert.Null(stopped.Failure);
        Assert.Equal("\n", Composed(Compose(nested, "node.summary", Scope(("node.drafts.value", Drafts(40))), steps: 100_000)));

        // 1 MiB of output, UTF-8, in three-byte characters: exactly 1 MiB passes, one byte more is S05-014.
        const string body = "Draft:\n{{ input.consult_draft.content }}\n";
        var output = RunFixture.Plan(Package().WithAgent("draft", body));
        var literal = Encoding.UTF8.GetByteCount(body.Replace("{{ input.consult_draft.content }}", "", StringComparison.Ordinal));
        var room = 1_048_576 - literal;
        var exactly = new string('€', room / 3) + new string('x', room % 3);
        Assert.Equal(1_048_576, Encoding.UTF8.GetByteCount(Composed(Compose(output, "node.draft", Scope(("input.consult_draft.content", exactly))))));
        AssertFailed(Compose(output, "node.draft", Scope(("input.consult_draft.content", exactly + "x"))),
            "S05-014", "Node 'draft' stopped rendering: its output passed 1 MiB.");

        // A three-byte character straddling the limit: 1 MiB − 1 bytes, then '€'.
        AssertFailed(Compose(output, "node.draft", Scope(("input.consult_draft.content", new string('x', room - 1) + "€"))),
            "S05-014", "Node 'draft' stopped rendering: its output passed 1 MiB.");
    }

    [Fact]
    public void AValueHoldingBraces_IsNotRendered()
    {
        var plan = RunFixture.Plan(Package().WithAgent("draft", "Draft:\n{{ input.consult_draft.content }}\n"));
        const string value = "Margaret wrote {{ node.x.content }}, {%{ a raw }%} and {%{ alone, then {{run:1}} and {{ include 'a' }}.";

        Assert.Equal($"Draft:\n{value}\n", Composed(Compose(plan, "node.draft", Scope(("input.consult_draft.content", value)))));
    }

    // ---- what publish did not write -----------------------------------------------------------

    [Fact]
    public void AnEntryThePlanLacks_IsS07006()
    {
        var plan = RunFixture.Plan(Package());
        var sentence = ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence;

        AssertFailed(Compose(plan, "node.nowhere", new JsonObject()), "S07-006", sentence);
        AssertFailed(Compose(plan, "node.plan", new JsonObject(), function: "no_such_function"), "S07-006", sentence);
    }

    [Fact]
    public void Compose_NeverThrows()
    {
        var files = Package().WithAgent("draft", "Draft:\n{{ input.consult_draft.content }}\n");
        var plan = RunFixture.Plan(files);
        var couldNot = RunSentences.CouldNotRender("Node 'draft'");

        // A scope missing every path, and a value of the wrong kind written directly: A06-008, never Scriban's message.
        AssertFailed(Compose(plan, "node.draft", new JsonObject()), "A06-008", couldNot);
        AssertFailed(Compose(plan, "node.draft", Scope(("input.consult_draft.content", new JsonObject { ["a"] = 1 }))), "A06-008", couldNot);

        // A member the data does not hold, under an object that is there (strict variables, part 05 § 2.3).
        AssertFailed(Compose(plan, "node.draft", Scope(("input.consult_draft.title", "Draft"))), "A06-008", couldNot);

        // A body edited after publish: one that holds what the dialect refuses, and one that does not parse.
        var (_, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        foreach (var edited in new[] { "{{ include 'x' }}", "{{ x = 1 }}{{ x }}", "{{ for a in b }}", "{{ node.x.content | object.default 'y' }}" })
        {
            var document = JsonNode.Parse(compiled!.Resolve)!;
            document["nodes"]!["node.draft"]!["body"] = edited;
            var (changed, _) = RunPlan.Build(document.ToJsonString(), compiled.ResolveHash, compiled.LockHash, null);
            var composition = Compose(changed!, "node.draft", Scope(("input.consult_draft.content", Draft)));
            AssertFailed(composition, "S07-006", ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
        }
    }
}
