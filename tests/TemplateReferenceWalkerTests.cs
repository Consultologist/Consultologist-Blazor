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
        ["extract"] = new("concept-list", "object", ConceptListSchema),
        ["scope"] = new("classification", "enum"),
        ["coverage"] = new("terms-subset", "object", TermsSubsetSchema),
        ["meds"] = new("medication_list", "object", JsonNode.Parse("""{ "type": "object", "properties": { "medications": { "type": "array", "items": { "type": "string" } } } }""")),
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

    // C4 (#1010): the renderer pipes of § 1 are the engine's closed set — accepted by the
    // walk, judged when they render. An unknown pipe name is still an unknown root.
    [Fact]
    public void ARendererPipe_OnANodesText_IsAccepted_AnUnknownPipeIsNot()
    {
        var piped = TemplateFixtures.Ok("{{ node.extract.text | concept_context }} {{ node.assessment.text | concept_bullets }}");
        Assert.Equal(new[] { "assessment", "extract" }, piped.Edges.Order());
        Assert.Equal(new TemplateNodeRead(true, false), piped.Nodes["extract"]);

        var unknown = TemplateFixtures.Walk("{{ node.extract.text | frobnicate }}");
        Assert.Contains(unknown.Errors, e => e.Contains("reads 'frobnicate', which is not an object"));
    }

    [Fact]
    public void NodeText_IsAnEdge_AndNodeValue_IsAnEdge()
    {
        var refs = TemplateFixtures.Ok("""
            {{ node.assessment.text }}
            {{ for c in node.extract.value.concepts }}{{ c.term }} ({{ c.count }}){{ end }}
            {{ if node.scope.value == "in_scope" }}yes{{ end }}
            {{ node.coverage.value.pass }} {{ node.extract.text }}
            """);

        Assert.Equal(new[] { "assessment", "coverage", "extract", "scope" }, refs.Edges.Order());
        Assert.Equal(new TemplateNodeRead(true, false), refs.Nodes["assessment"]);
        Assert.Equal(new TemplateNodeRead(true, true), refs.Nodes["extract"]);
        Assert.Equal(new TemplateNodeRead(false, true), refs.Nodes["scope"]);
        Assert.Contains(refs.Warnings, w => w.Contains("reads node.extract.text of format 'concept-list' (object); the JSON renders as text — read node.extract.value for the fields"));
    }

    [Fact]
    public void Snippets_AreReads_AndFoldersIterate()
    {
        var refs = TemplateFixtures.Ok("""
            {{ snippet.standards.text }} {{ snippet.intro.id }} {{ snippet.intro.name }}
            {{ for s in snippet.sections }}{{ s.id }}: {{ s.text }}{{ end }}
            {{ snippet.sections | array.size }}
            """);

        Assert.Equal(new[] { "intro", "standards" }, refs.Snippets.Order());
        Assert.Equal(new[] { "sections" }, refs.SnippetFolders.ToArray());
    }

    [Fact]
    public void Macros_TextIsARead_PromptIsAnInlineSlot()
    {
        var refs = TemplateFixtures.Ok("""
            {{ macro.closing.text }} {{ macro.letrozole.prompt }} {{ macro.closing.id }} {{ macro.closing.name }}
            {{ for m in macro.addenda }}{{ m.text }} {{ m.prompt }}{{ end }}
            """);

        Assert.Equal(new[] { "closing" }, refs.MacroTexts.ToArray());
        Assert.Equal(new[] { "letrozole" }, refs.MacroPrompts.ToArray());
        Assert.Equal(new[] { "addenda" }, refs.MacroFolders.ToArray());
    }

    [Fact]
    public void Item_OnASnippetFan_CarriesIdNameText()
    {
        var refs = TemplateFixtures.Ok("{{ item.id }} {{ item.name }} {{ item.text }}", new SnippetFolderFan("sections"));

        Assert.Equal(new[] { "id", "name", "text" }, refs.ItemMembers.Order());
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
            {{ if input.kind == "new" }}{{ node.assessment.text }}{{ else }}{{ node.letter.text }}{{ end }}
            {{ for s in snippet.sections }}{{ if s.id == "a" }}{{ macro.closing.text }}{{ end }}{{ end }}
            """);

        Assert.Equal(new[] { "assessment", "letter" }, refs.Edges.Order());
        Assert.Contains("closing", refs.MacroTexts);
    }

    [Fact]
    public void WithScopes_ResolveBareNamesAsMembers()
    {
        var refs = TemplateFixtures.Ok("{{ with macro.closing }}{{ text }} {{ prompt }}{{ end }}{{ with input.patient }}{{ name }}{{ end }}");

        Assert.Contains("closing", refs.MacroTexts);
        Assert.Contains("closing", refs.MacroPrompts);
        TemplateFixtures.Refused("{{ with input.patient }}{{ nam }}{{ end }}", "reads 'nam', which is not an object");
    }

    [Fact]
    public void CaptureAndAssignment_BindLocals()
    {
        TemplateFixtures.Ok("{{ capture intro }}{{ node.assessment.text }}{{ end }}{{ intro }}{{ x = input.notes }}{{ x }}{{ x.anything }}");
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
        TemplateFixtures.Refused("{{ for s in snippet.sections }}{{ s.body }}{{ end }}", "reads s.body, which is not a member (a snippet has .id, .name and .text)");
    }

    [Fact]
    public void ALoopVariable_OverAMacroFolder_HasTheMacroMembers()
    {
        TemplateFixtures.Refused("{{ for m in macro.addenda }}{{ m.instruction }}{{ end }}", "reads m.instruction, which is not a member (a macro has .id, .name, .text and .prompt)");
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
        TemplateFixtures.Refused("{{ for node in snippet.sections }}{{ node.text }}{{ end }}", "names a loop variable 'node', a reserved object");
        TemplateFixtures.Refused("{{ capture item }}x{{ end }}", "captures into 'item', a reserved object");
        TemplateFixtures.Refused("{{ snippet = 1 }}", "assigns to 'snippet', a reserved object");
        TemplateFixtures.Refused("{{ node.assessment.text = 1 }}", "assigns to 'node', a reserved object");
    }

    // ----- indexers ----------------------------------------------------------------

    [Fact]
    public void ALiteralIndex_OnAnArrayPath_IsStatic()
    {
        var refs = TemplateFixtures.Ok("{{ node.extract.value.concepts[0].term }} {{ input.history[1].event }} {{ snippet.sections[0].text }}");

        Assert.Contains("extract", refs.Edges);
        Assert.Contains("sections", refs.SnippetFolders);
    }

    [Fact]
    public void AStringLiteralIndex_IsAMember_TheOnlySpellingOfAHyphenatedId()
    {
        // Scriban reads node.identify-problem as a subtraction; the bracket form is the static spelling.
        var context = TemplateFixtures.Context();
        var nodes = new Dictionary<string, TemplateNodeShape>(context.Nodes, StringComparer.Ordinal) { ["identify-problem"] = new("prose", "text") };
        var refs = TemplateReferenceWalker.Walk("Prompt 'p'", """{{ node["identify-problem"].text }} {{ node["assessment"].text }} {{ input["patient"].name }}""", context with { Nodes = nodes });

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
        TemplateFixtures.Refused("{{ node[input.notes].text }}", "accesses node dynamically (node[…]); static member access only — name the id: node.<id>");
        TemplateFixtures.Refused("{{ for s in snippet.sections }}{{ input.history[s.id] }}{{ end }}", "accesses input.history dynamically");
    }

    // ----- refusals by name ------------------------------------------------------

    [Theory]
    [InlineData("{{ node }}", "reads 'node' bare; name the object: node.<id>.text or node.<id>.value")]
    [InlineData("{{ input }}", "reads 'input' bare; name the object: input.<id>")]
    [InlineData("{{ node.assessment }}", "reads node 'assessment' bare; read node.assessment.text (its composed output) or node.assessment.value")]
    [InlineData("{{ node.nope.text }}", "reads node 'nope', which the package does not declare")]
    [InlineData("{{ node.assessment.output }}", "reads node.assessment.output, which is not a member (node.<id> has .text and .value)")]
    [InlineData("{{ node.assessment.value }}", "reads node.assessment.value, but format 'prose' is text and has no value; read node.assessment.text")]
    [InlineData("{{ node.letter.value }}", "reads node.letter.value, but format 'rendered' is text and has no value")]
    [InlineData("{{ node.extract.value.terms }}", "reads field 'terms' of 'node.extract.value', which it does not declare (fields: concepts)")]
    [InlineData("{{ node.extract.value.concepts.term }}", "reads field 'term' of 'node.extract.value.concepts', which is an array of object, not an object")]
    [InlineData("{{ node.scope.value.label }}", "reads field 'label' of 'node.scope.value', which is an enum, not an object")]
    [InlineData("{{ node.coverage.value.pass.x }}", "reads field 'x' of 'node.coverage.value.pass', which is a boolean, not an object")]
    [InlineData("{{ node.assessment.text.words }}", "reads field 'words' of 'node.assessment.text', which is a text, not an object")]
    [InlineData("{{ input.nope }}", "reads input 'nope', which the package does not declare")]
    [InlineData("{{ input.notes.label }}", "reads field 'label' of 'input.notes', which is a text, not an object")]
    [InlineData("{{ input.patient.dob }}", "reads field 'dob' of 'input.patient', which it does not declare (fields: name, age, address)")]
    [InlineData("{{ input.history.event }}", "reads field 'event' of 'input.history', which is an array of object, not an object")]
    [InlineData("{{ snippet.nope.text }}", "reads snippet 'nope', which the package does not declare")]
    [InlineData("{{ snippet.standards }}", "reads snippet 'standards' bare; read snippet.standards.text")]
    [InlineData("{{ snippet.standards.body }}", "reads snippet.standards.body, which is not a member (a snippet has .id, .name and .text)")]
    [InlineData("{{ snippet.sections }}", "reads snippet.sections as a value; a folder is iterated: {{ for s in snippet.sections }}")]
    [InlineData("{{ snippet.sections.text }}", "reads snippet.sections.text; a folder is iterated: {{ for s in snippet.sections }}")]
    [InlineData("{{ macro.nope.text }}", "reads macro 'nope', which the package does not declare")]
    [InlineData("{{ macro.closing }}", "reads macro 'closing' bare; read macro.closing.text or macro.closing.prompt")]
    [InlineData("{{ macro.closing.instruction }}", "reads macro.closing.instruction, which is not a member (a macro has .id, .name, .text and .prompt)")]
    [InlineData("{{ macro.addenda }}", "reads macro.addenda as a value; a folder is iterated: {{ for s in macro.addenda }}")]
    [InlineData("{{ item.text }}", "reads item.text, but the node does not fan")]
    [InlineData("{{ item }}", "reads item, but the node does not fan")]
    [InlineData("{{ data.standards }}", "reads 'data'; at specVersion 25 data is 'snippet' — read snippet.<id>.text")]
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
        TemplateFixtures.Refused("{{ item.value }}", "reads item.value, which a snippet fan item does not carry (it carries: id, name, text)", new SnippetFolderFan("sections"));
        TemplateFixtures.Refused("{{ item.text }}", "reads item.text, which an input fan item does not carry (it carries: id, name, value)", new InputArrayFan(TemplateFixtures.Inputs()["history"].Items!));
        TemplateFixtures.Refused("{{ item }}", "reads item bare; read a member (item.id, item.name, item.text)", new SnippetFolderFan("sections"));
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
        var refs = TemplateFixtures.Walk("{{ node.nope.text }} {{ input.nope }} {{ snippet.sections }}");

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
        var refs = TemplateReferenceWalker.Walk("Snippet 'standards'", "{{ node.nope.text }}", TemplateFixtures.Context());

        Assert.All(refs.Errors, e => Assert.StartsWith("Snippet 'standards' ", e));
    }
}
