using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A package as a v25 template sees it (design § 1): four inputs, five nodes
/// across the catalogue formats, a snippet and a folder, two macros and a
/// folder. Fan variants per test.
/// </summary>
public static class TemplateFixtures
{
    public static readonly JsonNode ConceptListSchema = JsonNode.Parse("""
        { "type": "object", "additionalProperties": false, "required": ["concepts"],
          "properties": { "concepts": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["term", "count"],
                           "properties": { "term": { "type": "string" }, "count": { "type": "integer" } } } } } }
        """)!;

    public static readonly JsonNode TermsSubsetSchema = JsonNode.Parse("""
        { "type": "object", "additionalProperties": false, "required": ["pass", "uncovered", "untested"],
          "properties": { "pass": { "type": "boolean" }, "uncovered": { "type": "array", "items": { "type": "string" } }, "untested": { "type": "array", "items": { "type": "string" } } } }
        """)!;

    public static IReadOnlyDictionary<string, WorkflowDeclarationNode> Inputs() => new Dictionary<string, WorkflowDeclarationNode>(StringComparer.Ordinal)
    {
        ["notes"] = WorkflowDeclarationNode.Of(new WorkflowInputSpec("notes", "Notes")),
        ["kind"] = WorkflowDeclarationNode.Of(new WorkflowInputSpec("kind", "Kind", true, "enum", new List<string> { "new", "follow_up" })),
        ["patient"] = WorkflowDeclarationNode.Of(new WorkflowInputSpec("patient", "Patient", true, "object",
            Fields: new List<WorkflowFieldSpec>
            {
                new("name", "Name", true, "text"),
                new("age", "Age", false, "number"),
                new("address", "Address", false, "object", Fields: new List<WorkflowFieldSpec> { new("city", "City", true, "text") })
            })),
        ["history"] = WorkflowDeclarationNode.Of(new WorkflowInputSpec("history", "History", true, "array",
            Items: new WorkflowElementSpec("object"),
            Fields: new List<WorkflowFieldSpec> { new("event", "Event", true, "text"), new("year", "Year", false, "number") })),
    };

    public static IReadOnlyDictionary<string, TemplateNodeShape> Nodes() => new Dictionary<string, TemplateNodeShape>(StringComparer.Ordinal)
    {
        ["assessment"] = new("prose", "text"),
        ["letter"] = new("rendered", "text"),
        ["extract"] = new("concept_list", "object", ConceptListSchema),
        ["scope"] = new("classification", "enum"),
        ["coverage"] = new("terms_subset", "object", TermsSubsetSchema),
        ["meds"] = new("medication_list", "object", JsonNode.Parse("""{ "type": "object", "properties": { "medications": { "type": "array", "items": { "type": "string" } } } }""")),
        ["all"] = TemplateNodeShape.Aggregator(), // #1065
    };

    public static TemplateReferenceContext Context(TemplateFan? fan = null) => new(
        Inputs(), Nodes(),
        new HashSet<string>(StringComparer.Ordinal) { "standards", "intro" },
        new HashSet<string>(StringComparer.Ordinal) { "sections" },
        new HashSet<string>(StringComparer.Ordinal) { "closing", "letrozole" },
        new HashSet<string>(StringComparer.Ordinal) { "addenda" },
        fan);

    public static TemplateReferences Walk(string template, TemplateFan? fan = null, string subject = "Prompt 'p'") =>
        TemplateReferenceWalker.Walk(subject, template, Context(fan));

    public static TemplateReferences Ok(string template, TemplateFan? fan = null)
    {
        var refs = Walk(template, fan);
        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        return refs;
    }

    public static void Refused(string template, string sentence, TemplateFan? fan = null)
    {
        var refs = Walk(template, fan);
        Assert.Contains(refs.Errors, e => e.StartsWith("Prompt 'p' ", StringComparison.Ordinal) && e.Contains(sentence));
    }

}

public class TemplateReferenceWalkerTests
{
    // ----- the five objects, happy paths ---------------------------------------

    [Fact]
    public void Inputs_AreReads_TypedAsDeclared()
    {
        var refs = TemplateFixtures.Ok("""
            {{ input.notes }} {{ input.kind }} {{ input.patient.name }} {{ input.patient.address.city }}
            {{ for h in input.history }}{{ h.event }} {{ h.year }}{{ end }}
            """);

        Assert.Equal(new[] { "history", "kind", "notes", "patient" }, refs.Inputs.Order());
        Assert.Empty(refs.Nodes);
    }

    // C4 (#1010) accepted the renderer pipes as the engine's closed set; #1058 types the
    // operand: a concept_list node's text passes without the bare-read warning, anything
    // else the walk can type is refused, anything it cannot is judged when it renders.
    // ----- #1058: renderer pipes typed -----

    [Fact]
    public void ARendererPipe_OnAConceptListNodesText_IsAccepted_WithoutTheWarning()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.content | concept_context }} {{ node.extract.content | concept_bullets }}");

        Assert.Equal(new[] { "extract" }, refs.Edges);
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["extract"]);
        Assert.Empty(refs.Warnings);
    }

    [Fact]
    public void ARendererPipe_OnATextNodesText_IsRefused_AndTheEdgeIsStillRecorded()
    {
        const string sentence = "pipes node.assessment.content through concept_bullets, but format 'prose' is text; concept_bullets renders a concept_list node's text (node.<id>.content).";
        TemplateFixtures.Refused("{{ node.assessment.content | concept_bullets }}", sentence);

        var refs = TemplateFixtures.Walk("{{ node.assessment.content | concept_bullets }}");
        Assert.Contains("assessment", refs.Edges);
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["assessment"]);
    }

    [Theory]
    [InlineData("{{ node.meds.content | concept_context }}", "pipes node.meds.content through concept_context, but format 'medication_list' (object) is not concept_list; concept_context renders a concept_list node's text (node.<id>.content).")]
    [InlineData("{{ node.scope.content | concept_bullets }}", "pipes node.scope.content through concept_bullets, but format 'classification' (enum) is not concept_list; concept_bullets renders a concept_list node's text (node.<id>.content).")]
    [InlineData("{{ node.coverage.content | concept_bullets }}", "pipes node.coverage.content through concept_bullets, but format 'terms_subset' (object) is not concept_list;")]
    public void ARendererPipe_OnAnotherShapedNodesText_IsRefused(string template, string sentence) =>
        TemplateFixtures.Refused(template, sentence);

    [Theory]
    [InlineData("{{ node.extract.value | concept_bullets }}", "pipes node.extract.value through concept_bullets; a renderer pipe renders a node's text — pipe node.extract.content.")]
    [InlineData("{{ node.scope.value | concept_context }}", "pipes node.scope.value through concept_context; a renderer pipe renders a node's text — pipe node.scope.content.")]
    [InlineData("{{ node.extract.value.concepts | concept_bullets }}", "pipes node.extract.value.concepts through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    public void ARendererPipe_OnANodesValue_IsRefused(string template, string sentence) =>
        TemplateFixtures.Refused(template, sentence);

    [Theory]
    [InlineData("{{ snippet.standards.content | concept_bullets }}", "pipes snippet.standards.content through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ input.notes | concept_context }}", "pipes input.notes through concept_context; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ input.patient | concept_bullets }}", "pipes input.patient through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ macro.closing.content | concept_bullets }}", "pipes macro.closing.content through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ run.date | concept_bullets }}", "pipes run.date through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ profile.name | concept_context }}", "pipes profile.name through concept_context; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ snippet.sections | concept_bullets }}", "pipes snippet.sections through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ 'free text' | concept_bullets }}", "through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ 42 | concept_bullets }}", "through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ for s in snippet.sections }}{{ s.content | concept_bullets }}{{ end }}", "through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ for c in node.extract.value.concepts }}{{ c | concept_bullets }}{{ end }}", "through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).")]
    [InlineData("{{ node.extract | concept_bullets }}", "reads node 'extract' bare; read node.extract.content (its composed output) or node.extract.value.")]
    public void ARendererPipe_OnAnythingButANodesText_IsRefused(string template, string sentence) =>
        TemplateFixtures.Refused(template, sentence);

    [Fact]
    public void ARendererPipe_OnAFanItemsText_IsRefused() =>
        TemplateFixtures.Refused("{{ item.content | concept_bullets }}", "pipes item.content through concept_bullets; a renderer pipe renders a node's text (node.<id>.content).", new SnippetFolderFan("sections"));

    [Fact]
    public void ARendererPipe_OnAnOperandTheWalkCannotType_IsAccepted()
    {
        // An assigned variable, a range's element, a builtin's result, a nested expression:
        // untyped here, judged when it renders (TemplateRenderer.ApplyPipe), as before.
        // (The assignment itself is a bare shaped read and warns as one — the walk cannot
        // see that the variable is piped later.)
        var refs = TemplateFixtures.Ok("""
            {{ x = node.extract.content }}{{ x | concept_bullets }}
            {{ for i in 1..3 }}{{ i | concept_bullets }}{{ end }}
            {{ for w in (input.notes | string.split " ") }}{{ w | concept_context }}{{ end }}
            {{ (node.assessment.content) | concept_bullets }}
            """);

        Assert.All(refs.Warnings, w => Assert.Contains("reads node.extract.content of format 'concept_list'", w));
    }

    [Fact]
    public void AnUnknownPipe_IsStillAnUnknownRoot()
    {
        var unknown = TemplateFixtures.Walk("{{ node.extract.content | frobnicate }}");

        Assert.Contains(unknown.Errors, e => e.Contains("reads 'frobnicate', which is not an object"));
    }

    [Fact]
    public void ARendererPipe_InsideWith_IsJudgedTheSame()
    {
        var ok = TemplateFixtures.Ok("{{ with node.extract }}{{ content | concept_bullets }}{{ end }}");
        Assert.Empty(ok.Warnings);
        Assert.Equal(new TemplateNodeRead(true, false), ok.Nodes["extract"]);

        TemplateFixtures.Refused("{{ with node.assessment }}{{ content | concept_bullets }}{{ end }}", "pipes node.assessment.content through concept_bullets, but format 'prose' is text");
        TemplateFixtures.Refused("{{ with node.extract }}{{ value | concept_bullets }}{{ end }}", "pipes node.extract.value through concept_bullets");
    }

    [Fact]
    public void ARendererPipe_InTheCallForm_IsJudgedTheSame()
    {
        Assert.Empty(TemplateFixtures.Ok("{{ node.extract.content | concept_bullets() }}").Warnings);
        TemplateFixtures.Refused("{{ node.assessment.content | concept_bullets() }}", "but format 'prose' is text");
    }

    [Fact]
    public void AChainedRendererPipe_RendersTheNodeThenPipesOn()
    {
        // Scriban nests pipes to the right — `a | r | g` is Pipe(a, Pipe(call r, call g)) —
        // so the renderer is the first destination and the rest is piped on.
        Assert.Empty(TemplateFixtures.Ok("{{ node.extract.content | concept_bullets | string.upcase }}").Warnings);
        TemplateFixtures.Refused("{{ node.assessment.content | concept_bullets | string.upcase }}", "pipes node.assessment.content through concept_bullets, but format 'prose' is text");
    }

    [Fact]
    public void ARendererPipe_AfterAnotherPipe_IsAccepted_AndTheBareReadWarns()
    {
        // The renderer's operand is a builtin's result (untyped); the .content itself was
        // read bare into string.upcase, so that read warns as any bare shaped read does.
        var refs = TemplateFixtures.Ok("{{ node.extract.content | string.upcase | concept_bullets }}");

        Assert.Contains(refs.Warnings, w => w.Contains("reads node.extract.content of format 'concept_list'"));
    }

    // ----- #1098: which pipe rendered which node -----
    // The walk records an ACCEPTED rendering as (node, pipe) — the editor's "Used by"
    // rows on a renderer's pane. A refused pipe records its edge and no rendering;
    // an operand the walk cannot type records nothing (judged when it renders).

    [Fact]
    public void ARendererPipe_OnAConceptListNodesText_IsRecorded_AsRenderingThatNode()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.content | concept_context }} {{ node.extract.content | concept_bullets }} {{ node.extract.content | concept_context }}");

        Assert.Equal(
            new[] { new TemplateRenderedRead("extract", "concept_bullets"), new TemplateRenderedRead("extract", "concept_context") },
            refs.Rendered.OrderBy(r => r.Pipe, StringComparer.Ordinal));
    }

    [Fact]
    public void ARefusedRendererPipe_RecordsTheEdge_ButNoRendering()
    {
        var refs = TemplateFixtures.Walk("{{ node.assessment.content | concept_bullets }}");

        Assert.Contains("assessment", refs.Edges);
        Assert.Empty(refs.Rendered);
    }

    [Fact]
    public void ARendererPipe_InsideWith_IsRecorded_AgainstTheNode()
    {
        var refs = TemplateFixtures.Ok("{{ with node.extract }}{{ content | concept_bullets }}{{ end }}");

        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets"), Assert.Single(refs.Rendered));
    }

    [Fact]
    public void ARendererPipe_InTheCallForm_IsRecorded()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.content | concept_bullets() }}");

        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets"), Assert.Single(refs.Rendered));
    }

    [Fact]
    public void AChainedRendererPipe_IsRecordedOnce_AndTheRestRecordsNothing()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.content | concept_bullets | string.upcase }}");

        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets"), Assert.Single(refs.Rendered));
    }

    [Fact]
    public void ARendererPipe_AfterAnotherPipe_RecordsNoRendering()
    {
        // The operand is a builtin's result: untyped, so judged when it renders — not here.
        var refs = TemplateFixtures.Ok("{{ node.extract.content | string.upcase | concept_bullets }}");

        Assert.Empty(refs.Rendered);
    }

    [Fact]
    public void ABareContentRead_RecordsNoRendering()
    {
        var refs = TemplateFixtures.Walk("{{ node.extract.content }} {{ node.assessment.content }} {{ node.extract.value.concepts | array.size }}");

        Assert.Empty(refs.Rendered);
    }

    [Fact]
    public void NonRendererPipes_AreUnchanged() =>
        Assert.Empty(TemplateFixtures.Ok("{{ snippet.sections | array.size }} {{ input.notes | string.upcase }} {{ node.assessment.content | string.truncate 10 }}").Warnings);

    [Fact]
    public void ABareShapedContentRead_StillWarns_Verbatim()
    {
        const string warning = "Prompt 'p' reads node.extract.content of format 'concept_list' (object); the JSON renders as text — read node.extract.value for the fields.";

        Assert.Equal(warning, Assert.Single(TemplateFixtures.Walk("{{ node.extract.content }}").Warnings));
        Assert.Equal(warning, Assert.Single(TemplateFixtures.Walk("{{ with node.extract }}{{ content }}{{ end }}").Warnings));
        Assert.Contains(TemplateFixtures.Walk("{{ if node.extract.content == \"x\" }}y{{ end }}").Warnings, w => w == warning);
        Assert.Empty(TemplateFixtures.Ok("{{ node.assessment.content }}").Warnings);
    }

    [Fact]
    public void AContentChainThatContinues_IsRefusedNotWarned()
    {
        var refs = TemplateFixtures.Walk("{{ node.extract.content.words }}");

        Assert.Contains(refs.Errors, e => e.Contains("which is a text, not an object"));
        Assert.Empty(refs.Warnings);
    }

    [Fact]
    public void ARendererPipe_InACondition_IsJudgedTheSame() =>
        TemplateFixtures.Refused("{{ if (node.assessment.content | concept_bullets) == \"x\" }}y{{ end }}", "pipes node.assessment.content through concept_bullets, but format 'prose' is text");

    // #1138: the rendered fact carries the if around the pipe — the renderer pane's
    // "included when" on a Used by row. Canonical only when the pipe's statement is the
    // if's whole content; a piped read is still no occurrence (the #1088 rows are for
    // reads a builder can wrap).
    [Fact]
    public void ARendererPipe_InsideAnIf_RecordsItsGuard_CanonicalWhenItIsTheIfsOnlyContent()
    {
        const string guard = "input.kind == \"new\"";

        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets", guard, true),
            Assert.Single(TemplateFixtures.Ok("{{ if " + guard + " }}{{ node.extract.content | concept_bullets }}{{ end }}").Rendered));
        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets", guard, false),
            Assert.Single(TemplateFixtures.Ok("{{ if " + guard + " }}Concepts:\n{{ node.extract.content | concept_bullets }}{{ end }}").Rendered));
        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets", guard, false),
            Assert.Single(TemplateFixtures.Ok("{{ if " + guard + " }}x{{ else }}{{ node.extract.content | concept_bullets }}{{ end }}").Rendered));
        Assert.Equal(new TemplateRenderedRead("extract", "concept_bullets"),
            Assert.Single(TemplateFixtures.Ok("{{ node.extract.content | concept_bullets }}").Rendered));
        Assert.Null(Assert.Single(TemplateFixtures.Ok("{{~ case input.kind ~}}{{~ when \"new\" ~}}{{ node.extract.content | concept_bullets }}{{~ end ~}}").Rendered).Guard);
        Assert.Empty(TemplateFixtures.Ok("{{ if " + guard + " }}{{ node.extract.content | concept_bullets }}{{ end }}").Occurrences);
    }

    [Fact]
    public void TemplatePipes_DeclareTheFormatTheyRender()
    {
        Assert.Equal(WorkflowFormatIds.ConceptList, TemplatePipes.FormatOf(TemplatePipes.ConceptBullets));
        Assert.Equal(WorkflowFormatIds.ConceptList, TemplatePipes.FormatOf(TemplatePipes.ConceptContext));
        Assert.Null(TemplatePipes.FormatOf("frobnicate"));
        Assert.Equal(TemplatePipes.Specs.Select(spec => spec.Name), TemplatePipes.All);

        // #1138: each spec renders through the library's own code, and Engine is the
        // pipes as TemplateRenderer takes them — ConceptPipes.All's semantics, "node" default included.
        Assert.Equal(TemplatePipes.All.Order(), TemplatePipes.Engine.Keys.Order());
        Assert.Null(TemplatePipes.SpecOf("frobnicate"));
        var concepts = ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, "extract");
        var output = new TemplateNodeOutput(ConceptSamples.ConceptListJson, "object", "extract");
        Assert.Equal(ConceptRenderers.Context(concepts), TemplatePipes.SpecOf(TemplatePipes.ConceptContext)!.Render(ConceptSamples.ConceptListJson, "extract"));
        Assert.Equal(ConceptRenderers.Context(concepts), TemplatePipes.Engine[TemplatePipes.ConceptContext](output));
        Assert.Equal(ConceptRenderers.Bullets(concepts), TemplatePipes.Engine[TemplatePipes.ConceptBullets](output));
        Assert.Contains("source: node", TemplatePipes.Engine[TemplatePipes.ConceptContext](new TemplateNodeOutput(ConceptSamples.ConceptListJson, "object")), StringComparison.Ordinal);
    }

    [Fact]
    public void NodeText_IsAnEdge_AndNodeValue_IsAnEdge()
    {
        var refs = TemplateFixtures.Ok("""
            {{ node.assessment.content }}
            {{ for c in node.extract.value.concepts }}{{ c.term }} ({{ c.count }}){{ end }}
            {{ if node.scope.value == "in_scope" }}yes{{ end }}
            {{ node.coverage.value.pass }} {{ node.extract.content }}
            """);

        Assert.Equal(new[] { "assessment", "coverage", "extract", "scope" }, refs.Edges.Order());
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["assessment"]);
        Assert.Equal(new TemplateNodeRead(true, true), refs.Nodes["extract"]);
        Assert.Equal(new TemplateNodeRead(false, true), refs.Nodes["scope"]);
        Assert.Contains(refs.Warnings, w => w.Contains("reads node.extract.content of format 'concept_list' (object); the JSON renders as text — read node.extract.value for the fields"));
    }

    [Fact]
    public void Snippets_AreReads_AndFoldersIterate()
    {
        var refs = TemplateFixtures.Ok("""
            {{ snippet.standards.content }} {{ snippet.intro.id }} {{ snippet.intro.name }}
            {{ for s in snippet.sections }}{{ s.id }}: {{ s.content }}{{ end }}
            {{ snippet.sections | array.size }}
            """);

        Assert.Equal(new[] { "intro", "standards" }, refs.Snippets.Order());
        Assert.Equal(new[] { "sections" }, refs.SnippetFolders.ToArray());
    }

    [Fact]
    public void Macros_TextIsARead_PromptIsAnInlineMacro()
    {
        var refs = TemplateFixtures.Ok("""
            {{ macro.closing.content }} {{ macro.letrozole.prompt }} {{ macro.closing.id }} {{ macro.closing.name }}
            {{ for m in macro.addenda }}{{ m.content }} {{ m.prompt }}{{ end }}
            """);

        Assert.Equal(new[] { "closing" }, refs.MacroContents.ToArray());
        Assert.Equal(new[] { "letrozole" }, refs.MacroPrompts.ToArray());
        Assert.Equal(new[] { "addenda" }, refs.MacroFolders.ToArray());
    }

    [Fact]
    public void Item_OnASnippetFan_CarriesIdNameText()
    {
        var refs = TemplateFixtures.Ok("{{ item.id }} {{ item.name }} {{ item.content }}", new SnippetFolderFan("sections"));

        Assert.Equal(new[] { "content", "id", "name" }, refs.ItemMembers.Order());
    }

    [Fact]
    public void Item_OnAnInputFan_CarriesIdNameValue_TypedByTheElement()
    {
        var element = TemplateFixtures.Inputs()["history"].Items!;
        var refs = TemplateFixtures.Ok("{{ item.id }} {{ item.value.event }} {{ item.value.year }}", new InputArrayFan(element));

        Assert.Equal(new[] { "id", "value" }, refs.ItemMembers.Order());
        TemplateFixtures.Refused("{{ item.value.date }}", "reads field 'date' of 'item.value', which it does not declare (fields: event, year)", new InputArrayFan(element));
    }

    [Fact]
    public void ReadsInsideBranches_CountTheSame()
    {
        var refs = TemplateFixtures.Ok("""
            {{ if input.kind == "new" }}{{ node.assessment.content }}{{ else }}{{ node.letter.content }}{{ end }}
            {{ for s in snippet.sections }}{{ if s.id == "a" }}{{ macro.closing.content }}{{ end }}{{ end }}
            """);

        Assert.Equal(new[] { "assessment", "letter" }, refs.Edges.Order());
        Assert.Contains("closing", refs.MacroContents);
    }

    [Fact]
    public void WithScopes_ResolveBareNamesAsMembers()
    {
        var refs = TemplateFixtures.Ok("{{ with macro.closing }}{{ content }} {{ prompt }}{{ end }}{{ with input.patient }}{{ name }}{{ end }}");

        Assert.Contains("closing", refs.MacroContents);
        Assert.Contains("closing", refs.MacroPrompts);
        TemplateFixtures.Refused("{{ with input.patient }}{{ nam }}{{ end }}", "reads 'nam', which is not an object");
    }

    [Fact]
    public void CaptureAndAssignment_BindLocals()
    {
        TemplateFixtures.Ok("{{ capture intro }}{{ node.assessment.content }}{{ end }}{{ intro }}{{ x = input.notes }}{{ x }}{{ x.anything }}");
    }

    [Fact]
    public void ScribanBuiltins_AreOpaque()
    {
        TemplateFixtures.Ok("""{{ date.now | date.to_string "%Y" }} {{ input.notes | string.upcase }} {{ for i in 1..3 }}{{ i }} {{ for.index }}{{ end }} {{ "x" | string.size }}""");
    }

    [Fact]
    public void RunAndProfileFacts_AreReads()
    {
        // A4 (#999): the v11/v12 {{run:…}}/{{profile:…}} namespaces as objects.
        var refs = TemplateFixtures.Ok("{{ run.date }} {{ run.job }} {{ run.package }} {{ run.host }} {{ profile.name }}\n{{ profile.signature }} {{ with profile }}{{ name }}{{ end }}");

        Assert.Equal(new[] { "date", "host", "job", "package" }, refs.RunFacts.Order());
        Assert.Equal(new[] { "name", "signature" }, refs.ProfileFacts.Order());
        Assert.Empty(refs.Inputs);
    }

    [Fact]
    public void TheFactMembers_AreTheV11AndV12Vocabularies()
    {
        Assert.Equal(WorkflowMacroPlaceholders.RunFacts.Order(), TemplateMembers.Run);
        Assert.Equal(WorkflowMacroPlaceholders.ProfileFactsFor(25).Order(), TemplateMembers.Profile);
        Assert.Contains("run", TemplateObjects.All);
        Assert.Contains("profile", TemplateObjects.All);
        Assert.True(TemplateObjects.IsReserved("profile"));
    }

    [Fact]
    public void AnEmptyTemplate_ReadsNothing()
    {
        var refs = TemplateFixtures.Ok("Plain prose with no references.");

        Assert.Empty(refs.Inputs);
        Assert.Empty(refs.Nodes);
        Assert.Empty(refs.Warnings);
    }

    // ----- loop typing ------------------------------------------------------------

    [Fact]
    public void ALoopVariable_OverASnippetFolder_HasTheSnippetMembers()
    {
        TemplateFixtures.Refused("{{ for s in snippet.sections }}{{ s.body }}{{ end }}", "reads s.body, which is not a member (a snippet has .id, .name and .content)");
    }

    [Fact]
    public void ALoopVariable_OverAMacroFolder_HasTheMacroMembers()
    {
        TemplateFixtures.Refused("{{ for m in macro.addenda }}{{ m.instruction }}{{ end }}", "reads m.instruction, which is not a member (a macro has .id, .name, .content and .prompt)");
    }

    [Fact]
    public void ALoopVariable_OverASchemaArray_IsTypedByItems()
    {
        TemplateFixtures.Refused("{{ for c in node.extract.value.concepts }}{{ c.label }}{{ end }}", "reads field 'label' of 'c', which it does not declare (fields: term, count)");
        TemplateFixtures.Refused("{{ for u in node.coverage.value.uncovered }}{{ u.term }}{{ end }}", "reads field 'term' of 'u', which is a text, not an object");
    }

    [Fact]
    public void ALoopVariable_OverAnInputArray_IsTypedByTheElement()
    {
        TemplateFixtures.Refused("{{ for h in input.history }}{{ h.date }}{{ end }}", "reads field 'date' of 'h', which it does not declare (fields: event, year)");
    }

    [Fact]
    public void ALoopVariable_OverAnythingElse_IsUntyped()
    {
        TemplateFixtures.Ok("{{ for i in 1..3 }}{{ i.whatever }}{{ end }}{{ for w in (input.notes | string.split \" \") }}{{ w.size }}{{ end }}");
    }

    [Fact]
    public void IteratingANonArray_IsRefused()
    {
        TemplateFixtures.Refused("{{ for x in input.patient }}{{ x }}{{ end }}", "iterates input.patient, which is an object, not an array");
        TemplateFixtures.Refused("{{ for x in node.coverage.value.pass }}{{ x }}{{ end }}", "iterates node.coverage.value.pass, which is a boolean, not an array");
    }

    [Fact]
    public void ALoopVariable_NamedAReservedObject_IsRefused()
    {
        TemplateFixtures.Refused("{{ for node in snippet.sections }}{{ node.content }}{{ end }}", "names a loop variable 'node', a reserved object");
        TemplateFixtures.Refused("{{ capture item }}x{{ end }}", "captures into 'item', a reserved object");
        TemplateFixtures.Refused("{{ snippet = 1 }}", "assigns to 'snippet', a reserved object");
        TemplateFixtures.Refused("{{ node.assessment.content = 1 }}", "assigns to 'node', a reserved object");
    }

    // ----- indexers ----------------------------------------------------------------

    [Fact]
    public void ALiteralIndex_OnAnArrayPath_IsStatic()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.value.concepts[0].term }} {{ input.history[1].event }} {{ snippet.sections[0].content }}");

        Assert.Contains("extract", refs.Edges);
        Assert.Contains("sections", refs.SnippetFolders);
    }

    [Fact]
    public void AStringLiteralIndex_IsAMember_TheOnlySpellingOfAHyphenatedId()
    {
        // Scriban reads node.identify-problem as a subtraction; the bracket form is the static spelling.
        var context = TemplateFixtures.Context();
        var nodes = new Dictionary<string, TemplateNodeShape>(context.Nodes, StringComparer.Ordinal) { ["identify-problem"] = new("prose", "text") };
        var refs = TemplateReferenceWalker.Walk("Prompt 'p'", """{{ node["identify-problem"].content }} {{ node["assessment"].content }} {{ input["patient"].name }}""", context with { Nodes = nodes });

        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Equal(new[] { "assessment", "identify-problem" }, refs.Edges.Order());
        Assert.Contains("patient", refs.Inputs);
    }

    [Fact]
    public void ALiteralIndex_OnANonArray_IsRefused()
    {
        TemplateFixtures.Refused("{{ node.extract.value[0] }}", "indexes node.extract.value, which is not an array");
        TemplateFixtures.Refused("{{ input.patient[0] }}", "indexes input.patient, which is not an array");
    }

    [Fact]
    public void ADynamicIndex_OnAReservedChain_IsRefused()
    {
        TemplateFixtures.Refused("{{ node.extract.value.concepts[input.notes] }}", "accesses node.extract.value.concepts dynamically (node.extract.value.concepts[…]); static member access only");
        TemplateFixtures.Refused("{{ node[input.notes].content }}", "accesses node dynamically (node[…]); static member access only — name the id: node.<id>");
        TemplateFixtures.Refused("{{ for s in snippet.sections }}{{ input.history[s.id] }}{{ end }}", "accesses input.history dynamically");
    }

    // ----- refusals by name ------------------------------------------------------

    [Theory]
    [InlineData("{{ node }}", "reads 'node' bare; name the object: node.<id>.content or node.<id>.value")]
    [InlineData("{{ input }}", "reads 'input' bare; name the object: input.<id>")]
    [InlineData("{{ node.assessment }}", "reads node 'assessment' bare; read node.assessment.content (its composed output) or node.assessment.value")]
    [InlineData("{{ node.nope.content }}", "reads node 'nope', which the package does not declare")]
    [InlineData("{{ node.assessment.output }}", "reads node.assessment.output, which is not a member (node.<id> has .content and .value)")]
    [InlineData("{{ node.assessment.value }}", "reads node.assessment.value, but format 'prose' is text and has no value; read node.assessment.content")]
    [InlineData("{{ node.letter.value }}", "reads node.letter.value, but format 'rendered' is text and has no value")]
    [InlineData("{{ node.all.value }}", "reads node.all.value, but node 'all' aggregates — its composed text has no value; read node.all.content")]
    [InlineData("{{ node.all.content | concept_bullets }}", "pipes node.all.content through concept_bullets, but node.all aggregates; concept_bullets renders a concept_list node's text (node.<id>.content).")]
    [InlineData("{{ node.extract.value.terms }}", "reads field 'terms' of 'node.extract.value', which it does not declare (fields: concepts)")]
    [InlineData("{{ node.extract.value.concepts.term }}", "reads field 'term' of 'node.extract.value.concepts', which is an array of object, not an object")]
    [InlineData("{{ node.scope.value.label }}", "reads field 'label' of 'node.scope.value', which is an enum, not an object")]
    [InlineData("{{ node.coverage.value.pass.x }}", "reads field 'x' of 'node.coverage.value.pass', which is a boolean, not an object")]
    [InlineData("{{ node.assessment.content.words }}", "reads field 'words' of 'node.assessment.content', which is a text, not an object")]
    [InlineData("{{ input.nope }}", "reads input 'nope', which the package does not declare")]
    [InlineData("{{ input.notes.label }}", "reads field 'label' of 'input.notes', which is a text, not an object")]
    [InlineData("{{ input.patient.dob }}", "reads field 'dob' of 'input.patient', which it does not declare (fields: name, age, address)")]
    [InlineData("{{ input.history.event }}", "reads field 'event' of 'input.history', which is an array of object, not an object")]
    [InlineData("{{ snippet.nope.content }}", "reads snippet 'nope', which the package does not declare")]
    [InlineData("{{ snippet.standards }}", "reads snippet 'standards' bare; read snippet.standards.content")]
    [InlineData("{{ snippet.standards.body }}", "reads snippet.standards.body, which is not a member (a snippet has .id, .name and .content)")]
    [InlineData("{{ snippet.sections }}", "reads snippet.sections as a value; a folder is iterated: {{ for s in snippet.sections }}")]
    [InlineData("{{ snippet.sections.content }}", "reads snippet.sections.content; a folder is iterated: {{ for s in snippet.sections }}")]
    [InlineData("{{ macro.nope.content }}", "reads macro 'nope', which the package does not declare")]
    [InlineData("{{ macro.closing }}", "reads macro 'closing' bare; read macro.closing.content or macro.closing.prompt")]
    [InlineData("{{ macro.closing.instruction }}", "reads macro.closing.instruction, which is not a member (a macro has .id, .name, .content and .prompt)")]
    [InlineData("{{ macro.addenda }}", "reads macro.addenda as a value; a folder is iterated: {{ for s in macro.addenda }}")]
    [InlineData("{{ item.content }}", "reads item.content, but the node does not fan")]
    [InlineData("{{ item }}", "reads item, but the node does not fan")]
    [InlineData("{{ data.standards }}", "reads 'data'; at specVersion 25 data is 'snippet' — read snippet.<id>.content")]
    [InlineData("{{ bindings.x }}", "reads 'bindings'; at specVersion 25 a template reads objects")]
    [InlineData("{{ consult_draft }}", "reads 'consult_draft', which is not an object (input, node, snippet, item, macro, run, profile), a loop variable, or a Scriban builtin")]
    [InlineData("{{ run }}", "reads 'run' bare; name the fact: run.date or run.host or run.job or run.package")]
    [InlineData("{{ profile }}", "reads 'profile' bare; name the fact: profile.name or profile.signature")]
    [InlineData("{{ run.user }}", "reads run.user, which is not a member (run has .date, .host, .job and .package)")]
    [InlineData("{{ profile.email }}", "reads profile.email, which is not a member (profile has .name and .signature)")]
    [InlineData("{{ run.date.year }}", "reads field 'year' of 'run.date', which is a text, not an object")]
    [InlineData("{{input:notes}}", "carries the placeholder '{{input:notes}}'; at specVersion 25 references are objects")]
    [InlineData("{{ classification:scope }}", "carries the placeholder '{{classification:scope}}'")]
    public void Refusals_NameTheRule(string template, string sentence)
    {
        TemplateFixtures.Refused(template, sentence);
    }

    [Fact]
    public void Item_OnTheWrongFanKind_IsRefused()
    {
        TemplateFixtures.Refused("{{ item.value }}", "reads item.value, which a snippet fan item does not carry (it carries: id, name, content)", new SnippetFolderFan("sections"));
        TemplateFixtures.Refused("{{ item.content }}", "reads item.content, which an input fan item does not carry (it carries: id, name, value)", new InputArrayFan(TemplateFixtures.Inputs()["history"].Items!));
        TemplateFixtures.Refused("{{ item }}", "reads item bare; read a member (item.id, item.name, item.content)", new SnippetFolderFan("sections"));
    }

    [Fact]
    public void ARefusedChain_IsRefusedOnce()
    {
        var refs = TemplateFixtures.Walk("{{ node.nope.value.a.b.c }}");

        Assert.Single(refs.Errors);
    }

    [Fact]
    public void EveryDefect_IsListed()
    {
        var refs = TemplateFixtures.Walk("{{ node.nope.content }} {{ input.nope }} {{ snippet.sections }}");

        Assert.Equal(3, refs.Errors.Count);
    }

    // ----- parsing --------------------------------------------------------------------

    [Fact]
    public void AParseError_NamesTheLine()
    {
        var refs = TemplateFixtures.Walk("line one\nline two {{ node.assessment. }}\nline three");

        Assert.Contains(refs.Errors, e => e.StartsWith("Prompt 'p' does not parse at line 2:", StringComparison.Ordinal));
    }

    [Fact]
    public void AStrayOpeningBrace_NamesTheLine()
    {
        // Scriban reads the rest of the file as code without an error of its own.
        var refs = TemplateFixtures.Walk("Use {{ input.notes }} here.\nA literal {{ in prose\nand more prose.");

        Assert.Contains(refs.Errors, e => e.Contains("does not parse at line 2: '{{' is not closed; escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}"));
    }

    [Fact]
    public void AnEscapedBrace_AndARawBlock_AreProse()
    {
        TemplateFixtures.Ok("""Write {{ "{{" }} literally, and {%{ {{ this }} too }%}.""");
    }

    [Fact]
    public void AnUnbalancedEndStatement_NamesTheLine()
    {
        var refs = TemplateFixtures.Walk("{{ if input.notes }}\nnever closed");

        Assert.Contains(refs.Errors, e => e.StartsWith("Prompt 'p' does not parse at line 1:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSubject_OpensEverySentence()
    {
        var refs = TemplateReferenceWalker.Walk("Snippet 'standards'", "{{ node.nope.content }}", TemplateFixtures.Context());

        Assert.All(refs.Errors, e => Assert.StartsWith("Snippet 'standards' ", e));
    }

    // ----- #1065: an aggregator's composed text is readable -----

    [Fact]
    public void AnAggregatorsText_IsReadable_AsAnEdge_WithoutAWarning()
    {
        var refs = TemplateFixtures.Ok("Letter from:\n{{ node.all.content }}");

        Assert.Equal(new[] { "all" }, refs.Edges);
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["all"]);
        Assert.Empty(refs.Warnings);
    }

    [Fact]
    public void ARendererPipe_OnAnAggregator_IsRefused_AndTheEdgeIsStillRecorded()
    {
        var refs = TemplateFixtures.Walk("{{ node.all.content | concept_bullets }}");

        Assert.Contains("all", refs.Edges);
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["all"]);
        Assert.Single(refs.Errors);
    }

    // ----- #1088: read occurrences and their guard -----

    private const string Read = "{{ macro.letrozole.prompt }}";

    private static TemplateReadOccurrence Occurrence(string text, string member = "macro.letrozole.prompt") =>
        Assert.Single(TemplateFixtures.Ok(text).Occurrences.Where(o => o.Member == member));

    private static string Block(string text, TemplateReadOccurrence o) => text[o.Start..o.End];

    private static string GuardBlock(string text, TemplateReadOccurrence o) => text[o.GuardStart..o.GuardEnd];

    [Fact]
    public void AnUnguardedRead_IsAnOccurrence_WithItsBlockSpan_AndNoGuard()
    {
        const string text = "Assess.\n" + Read + "\nAfter.";
        var o = Occurrence(text);

        Assert.Equal(Read, Block(text, o));
        Assert.Null(o.Guard);
        Assert.False(o.GuardCanonical);
        Assert.Equal((-1, -1), (o.GuardStart, o.GuardEnd));
    }

    [Fact]
    public void TheCanonicalOneLiner_IsGuarded_AndCanonical_WithTheWholeLineAsTheGuardBlock()
    {
        const string line = "{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}";
        const string text = "History: {{ input.notes }}\n" + line + "\n";
        var o = Occurrence(text);

        Assert.Equal(Read, Block(text, o));
        Assert.Equal("input.kind == \"new\"", o.Guard);
        Assert.True(o.GuardCanonical);
        Assert.Equal(line, GuardBlock(text, o));
    }

    [Fact]
    public void AHandWrittenThreeLineGuard_IsCanonical_AndItsBlockRunsFromIfToEnd()
    {
        const string guard = "{{ if input.kind == \"new\" }}\n" + Read + "\n{{ end }}";
        const string text = "History.\n" + guard + "\nAfter.";
        var o = Occurrence(text);

        Assert.Equal("input.kind == \"new\"", o.Guard);
        Assert.True(o.GuardCanonical);
        Assert.Equal(guard, GuardBlock(text, o));
    }

    [Fact]
    public void AnElseBranch_ProseInTheBlock_OrAReadInTheElse_IsGuardedButNotCanonical()
    {
        var withElse = Occurrence("{{ if input.kind == \"new\" }}" + Read + "{{ else }}x{{ end }}");
        Assert.Equal("input.kind == \"new\"", withElse.Guard);
        Assert.False(withElse.GuardCanonical);

        var withProse = Occurrence("{{ if input.kind == \"new\" }}" + Read + " extra{{ end }}");
        Assert.Equal("input.kind == \"new\"", withProse.Guard);
        Assert.False(withProse.GuardCanonical);

        var inElse = Occurrence("{{ if input.kind == \"new\" }}x{{ else }}" + Read + "{{ end }}");
        Assert.Equal("input.kind == \"new\"", inElse.Guard);
        Assert.False(inElse.GuardCanonical);
    }

    [Fact]
    public void ANestedIf_IsGuardedByTheInnerOne_AndAForBetween_IsNotCanonical()
    {
        const string nested = "{{ if input.kind == \"new\" }}{{ if input.kind != \"follow_up\" }}" + Read + "{{ end }}{{ end }}";
        var inner = Occurrence(nested);
        Assert.Equal("input.kind != \"follow_up\"", inner.Guard);
        Assert.True(inner.GuardCanonical);
        Assert.Equal("{{ if input.kind != \"follow_up\" }}" + Read + "{{ end }}", GuardBlock(nested, inner));

        var throughFor = Occurrence("{{ if input.kind == \"new\" }}{{ for s in snippet.sections }}" + Read + "{{ end }}{{ end }}");
        Assert.Equal("input.kind == \"new\"", throughFor.Guard);
        Assert.False(throughFor.GuardCanonical);
    }

    [Fact]
    public void ReadsAreRecorded_InTextOrder_ForEveryMember_AndAPipedReadIsNone()
    {
        const string text = Read + "\n{{ snippet.standards.content }}\n{{ node.extract.value }}\n" + Read + "\n{{ node.extract.content | concept_context }}";
        var refs = TemplateFixtures.Ok(text);

        Assert.Equal(
            new[] { "macro.letrozole.prompt", "snippet.standards.content", "node.extract.value", "macro.letrozole.prompt" },
            refs.Occurrences.Select(o => o.Member));
        Assert.All(refs.Occurrences, o => Assert.Equal("{{ " + o.Member + " }}", Block(text, o)));
        Assert.True(refs.Occurrences[0].Start < refs.Occurrences[3].Start);
    }

    // ----- #1091: case/when branch blocks -----

    private const string CaseLine = "{{~ case input.kind ~}}{{~ when \"new\" ~}}{{ macro.closing.prompt }}{{~ when \"follow_up\" ~}}" + Read + "{{~ else ~}}{{~ end ~}}";

    private static TemplateBranchBlock Case(string text) => Assert.Single(TemplateFixtures.Ok(text).Branches);

    [Fact]
    public void TheCanonicalCaseOneLiner_IsABranchBlock_WithItsBranches_AndTheWholeLineAsItsSpan()
    {
        const string text = "Assess.\n" + CaseLine + "\n{{ input.notes }}";
        var refs = TemplateFixtures.Ok(text);
        var block = Assert.Single(refs.Branches);

        Assert.Equal("input.kind", block.Subject);
        Assert.True(block.Canonical);
        Assert.Equal(
            new[] { new TemplateBranch("new", "macro.closing.prompt"), new TemplateBranch("follow_up", "macro.letrozole.prompt"), new TemplateBranch(null, null) },
            block.Branches);
        Assert.Equal(CaseLine, text[block.Start..block.End]);

        // The reads in the branches belong to the case: no guard, the block's index.
        var inBranches = refs.Occurrences.Where(o => o.Member.StartsWith("macro.", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, inBranches.Count);
        Assert.All(inBranches, o => { Assert.Equal(0, o.Branch); Assert.Null(o.Guard); });
        Assert.Equal(-1, Assert.Single(refs.Occurrences, o => o.Member == "input.notes").Branch);
    }

    /// <summary>#1120: the palette's empty block — what the editor's row table fills; the subject is itself a read.</summary>
    [Fact]
    public void AnEmptyCase_IsACanonicalBlock_WithNoBranches_AndReadsItsSubject()
    {
        const string text = "Before.\n{{~ case input.kind ~}}{{~ end ~}}\nAfter.";
        var refs = TemplateFixtures.Ok(text);
        var block = Assert.Single(refs.Branches);

        Assert.Equal("input.kind", block.Subject);
        Assert.True(block.Canonical);
        Assert.Empty(block.Branches);
        Assert.Equal("{{~ case input.kind ~}}{{~ end ~}}", text[block.Start..block.End]);
        Assert.Contains("kind", refs.Inputs);
        Assert.Empty(refs.Errors);
    }

    [Fact]
    public void AHandWrittenMultiLineCase_IsCanonical_AndItsBlockRunsFromCaseToEnd()
    {
        const string block = "{{ case input.kind }}\n{{ when \"new\" }}\n{{ macro.closing.prompt }}\n{{ when \"follow_up\" }}\n{{ end }}";
        const string text = "Before.\n" + block + "\nAfter.";
        var c = Case(text);

        Assert.True(c.Canonical);
        Assert.Equal(new[] { new TemplateBranch("new", "macro.closing.prompt"), new TemplateBranch("follow_up", null) }, c.Branches);
        Assert.Equal(block, text[c.Start..c.End]);
    }

    [Fact]
    public void ACase_IsNotCanonical_WhenALiteralIsUndeclared_Doubled_Prose_OrTheSubjectIsNoEnum()
    {
        Assert.False(Case("{{ case input.kind }}{{ when \"bogus\" }}{{ macro.closing.prompt }}{{ end }}").Canonical);
        Assert.False(Case("{{ case input.kind }}{{ when \"new\", \"follow_up\" }}{{ macro.closing.prompt }}{{ end }}").Canonical);
        Assert.False(Case("{{ case input.kind }}{{ when \"new\" }}{{ macro.closing.prompt }} extra{{ end }}").Canonical);
        Assert.False(Case("{{ case input.kind }}{{ when \"new\" }}{{ macro.closing.prompt }}{{ when \"new\" }}{{ end }}").Canonical);

        var noEnum = Case("{{ case input.notes }}{{ when \"x\" }}{{ macro.closing.prompt }}{{ end }}");
        Assert.False(noEnum.Canonical);
        Assert.Equal("input.notes", noEnum.Subject);
        Assert.Equal(new[] { new TemplateBranch("x", "macro.closing.prompt") }, noEnum.Branches);
    }

    [Fact]
    public void ACaseInsideAnIf_OwnsItsReads_AndAnEnumNodeSubject_IsCanonical()
    {
        var refs = TemplateFixtures.Ok("{{ if input.kind == \"new\" }}{{ case input.kind }}{{ when \"new\" }}" + Read + "{{ end }}{{ end }}");
        var read = Assert.Single(refs.Occurrences, o => o.Member == "macro.letrozole.prompt");
        Assert.Equal(0, read.Branch);
        Assert.Null(read.Guard);

        var nodes = new Dictionary<string, TemplateNodeShape>(TemplateFixtures.Nodes(), StringComparer.Ordinal)
        {
            ["scope"] = new("classification", "enum", Values: new[] { "in_scope", "out_of_scope" }),
        };
        var context = TemplateFixtures.Context() with { Nodes = nodes };
        var byNode = TemplateReferenceWalker.Walk("Prompt 'p'", "{{ case node.scope.value }}{{ when \"in_scope\" }}" + Read + "{{ end }}", context);
        Assert.True(byNode.IsValid, string.Join(" | ", byNode.Errors));
        var block = Assert.Single(byNode.Branches);
        Assert.Equal("node.scope.value", block.Subject);
        Assert.True(block.Canonical);
        Assert.Equal(new[] { new TemplateBranch("in_scope", "macro.letrozole.prompt") }, block.Branches);
    }

    // ----- #1121: if … else branch blocks -----

    private const string IfElseLine = "{{~ if input.kind == \"new\" ~}}{{ macro.closing.prompt }}{{~ else ~}}" + Read + "{{~ end ~}}";

    [Fact]
    public void TheCanonicalIfElse_IsAnIfBlock_WithBothReadsIndexingIt()
    {
        const string text = "Before.\n" + IfElseLine + "\nAfter.";
        var refs = TemplateFixtures.Ok(text);
        var block = Assert.Single(refs.Branches);

        Assert.Equal(TemplateBranchKind.If, block.Kind);
        Assert.Equal("input.kind == \"new\"", block.Subject);
        Assert.True(block.Canonical);
        Assert.Equal(
            new[] { new TemplateBranch("input.kind == \"new\"", "macro.closing.prompt"), new TemplateBranch(null, "macro.letrozole.prompt") },
            block.Branches);
        Assert.Equal(IfElseLine, text[block.Start..block.End]);

        // The then-read keeps the condition as a canonical guard over the whole block; the else-read has none; both index the block.
        var then = Assert.Single(refs.Occurrences, o => o.Member == "macro.closing.prompt");
        Assert.Equal("input.kind == \"new\"", then.Guard);
        Assert.True(then.GuardCanonical);
        Assert.Equal(IfElseLine, text[then.GuardStart..then.GuardEnd]);
        Assert.Equal(0, then.Branch);

        var otherwise = Assert.Single(refs.Occurrences, o => o.Member == "macro.letrozole.prompt");
        Assert.Null(otherwise.Guard);
        Assert.Equal(0, otherwise.Branch);
    }

    [Fact]
    public void AnElseOnlyIf_IsABlock_WithNoThenMember()
    {
        var block = Case("{{~ if input.kind == \"new\" ~}}{{~ else ~}}" + Read + "{{~ end ~}}");

        Assert.Equal(TemplateBranchKind.If, block.Kind);
        Assert.Equal(new[] { new TemplateBranch("input.kind == \"new\"", null), new TemplateBranch(null, "macro.letrozole.prompt") }, block.Branches);
    }

    [Fact]
    public void AnElseIf_ProseInTheElse_OrANestedStatement_IsNoBlock_AndTheReadsStayGuardedAsBefore()
    {
        var elseIf = TemplateFixtures.Ok("{{ if input.kind == \"new\" }}{{ macro.closing.prompt }}{{ else if input.kind == \"follow_up\" }}" + Read + "{{ end }}");
        Assert.Empty(elseIf.Branches);
        Assert.All(elseIf.Occurrences, o => { Assert.False(o.GuardCanonical); Assert.Equal(-1, o.Branch); });

        var prose = TemplateFixtures.Ok("{{ if input.kind == \"new\" }}" + Read + "{{ else }}x{{ end }}");
        Assert.Empty(prose.Branches);
        Assert.Equal("input.kind == \"new\"", Assert.Single(prose.Occurrences).Guard);
        Assert.False(Assert.Single(prose.Occurrences).GuardCanonical);

        var nested = TemplateFixtures.Ok("{{ if input.kind == \"new\" }}{{ macro.closing.prompt }}{{ else }}{{ for s in snippet.sections }}{{ s.content }}{{ end }}{{ end }}");
        Assert.Empty(nested.Branches);
    }

    [Fact]
    public void APlainGuard_StaysAGuard_NotABlock()
    {
        var refs = TemplateFixtures.Ok("{{~ if input.kind == \"new\" ~}}" + Read + "{{~ end ~}}");

        Assert.Empty(refs.Branches);
        var read = Assert.Single(refs.Occurrences);
        Assert.True(read.GuardCanonical);
        Assert.Equal(-1, read.Branch);
    }

    // ----- #1122: a literal outside the enum's declared values -----

    private const string Bogus = "Prompt 'p' compares input.kind to 'bogus', which it does not declare (values: new, follow_up); that branch never runs.";

    [Fact]
    public void AWhenLiteralOutsideTheEnum_Warns_ThatTheBranchNeverRuns_AndTheBlockIsNotCanonical()
    {
        var refs = TemplateFixtures.Ok("{{ case input.kind }}{{ when \"bogus\" }}" + Read + "{{ end }}");

        Assert.Equal(Bogus, Assert.Single(refs.Warnings));
        Assert.False(Assert.Single(refs.Branches).Canonical);
    }

    [Fact]
    public void AnEqualsLiteralOutsideTheEnum_Warns_EitherWayRound_AndNotEquals_SaysTheTestIsAlwaysTrue()
    {
        Assert.Equal(Bogus, Assert.Single(TemplateFixtures.Ok("{{ if input.kind == \"bogus\" }}x{{ end }}").Warnings));
        Assert.Equal(Bogus, Assert.Single(TemplateFixtures.Ok("{{ if \"bogus\" == input.kind }}x{{ end }}").Warnings));
        Assert.Equal(
            "Prompt 'p' compares input.kind to 'bogus', which it does not declare (values: new, follow_up); that test is always true.",
            Assert.Single(TemplateFixtures.Ok("{{ if input.kind != \"bogus\" }}x{{ end }}").Warnings));
    }

    [Fact]
    public void ADeclaredLiteral_ANonEnumSide_ANonLiteral_AndAValuelessEnumNode_WarnNothing()
    {
        Assert.Empty(TemplateFixtures.Ok("{{ if input.kind == \"new\" }}x{{ end }}").Warnings);
        Assert.Empty(TemplateFixtures.Ok("{{ if input.notes == \"bogus\" }}x{{ end }}").Warnings);
        Assert.Empty(TemplateFixtures.Ok("{{ if input.kind == input.notes }}x{{ end }}").Warnings);
        Assert.Empty(TemplateFixtures.Ok("{{ if node.scope.value == \"bogus\" }}x{{ end }}").Warnings);
        Assert.Empty(TemplateFixtures.Ok("{{ case input.kind }}{{ when \"new\" }}x{{ end }}").Warnings);
    }

    [Fact]
    public void AnEnumNodesValue_WarnsLikeAnInput_ForAnIfAndAWhen()
    {
        var nodes = new Dictionary<string, TemplateNodeShape>(TemplateFixtures.Nodes(), StringComparer.Ordinal)
        {
            ["scope"] = new("classification", "enum", Values: new[] { "in_scope", "out_of_scope" }),
        };
        var context = TemplateFixtures.Context() with { Nodes = nodes };
        const string sentence = "Prompt 'p' compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope); that branch never runs.";

        Assert.Equal(sentence, Assert.Single(TemplateReferenceWalker.Walk("Prompt 'p'", "{{ if node.scope.value == \"maybe\" }}x{{ end }}", context).Warnings));
        Assert.Equal(sentence, Assert.Single(TemplateReferenceWalker.Walk("Prompt 'p'", "{{ case node.scope.value }}{{ when \"maybe\" }}x{{ end }}", context).Warnings));
    }
}
