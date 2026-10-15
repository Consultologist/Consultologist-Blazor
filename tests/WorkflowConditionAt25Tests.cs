using Consultologist.PackageFormat;
using Scriban.Runtime;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1037 (design § 16): a <c>when</c> at 25 is a Scriban boolean expression over the
/// objects a template reads — walked (its reads are edges), typed (the ≤24 clause
/// rules re-spelled), evaluated to a boolean by the probe — and the ≤24 grammar is
/// refused by name with the spelling. The ≤24 parser reads both spellings through
/// ConditionScriban's inverse, so the evaluator, the diagram and the builder serve
/// either.
/// </summary>
public class WorkflowConditionAt25Tests
{
    private static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => V25Package.Errors(bundle);

    private static (WorkflowPackageManifest, Dictionary<string, string>) WithDeliverableWhen(string when)
    {
        var (manifest, files) = V25Package.Minimal();
        return (manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = when }).ToList() }, files);
    }

    private static (WorkflowPackageManifest, Dictionary<string, string>) WithNodeWhen(string nodeId, string when)
    {
        var (manifest, files) = V25Package.Minimal();
        return (V25Package.WithNode(manifest, nodeId, n => n with { When = when }), files);
    }

    private static (WorkflowPackageManifest, Dictionary<string, string>) WithInstanceWhen(string when)
    {
        var (manifest, files) = V25Package.Minimal();
        return (V25Package.WithNode(manifest, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: "after", When: when) } }), files);
    }

    // ----- the grammar -----------------------------------------------------------

    [Theory]
    [InlineData("node.scope.value == \"in_scope\"")]
    [InlineData("input.kind == \"new\"")]
    [InlineData("input.kind != \"new\" && node.scope.value == \"out_of_scope\"")]
    [InlineData("!(input.kind == \"new\") || node.scope.value == \"in_scope\"")]
    [InlineData("node.assessment.text == \"\"")]
    [InlineData("snippet.standards.text != \"\"")]
    [InlineData("run.date == \"2026-01-02\"")]
    public void AConditionOverTheObjects_IsAccepted(string when)
    {
        var errors = Errors(WithDeliverableWhen(when));

        Assert.DoesNotContain(errors, e => e.StartsWith("Deliverable 'consult' condition", StringComparison.Ordinal));
    }

    [Fact]
    public void ACountOfAnArray_OrdersAsANumber()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Append(new WorkflowInputSpec("docs", "Documents", false, "array", Items: new WorkflowElementSpec("text"))).ToList(),
            Deliverables = manifest.Deliverables!.Select(d => d with { When = "(input.docs | array.size) > 0" }).ToList(),
        };

        Assert.DoesNotContain(Errors((manifest, files)), e => e.StartsWith("Deliverable 'consult' condition", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{{ input.kind == \"new\" }}", "Deliverable 'consult' condition is a template, not a condition; write the expression bare (node.<id>.value == \"<value>\").")]
    [InlineData("input.kind == \"new\"; input.kind", "Deliverable 'consult' condition is not one expression; a condition is one boolean expression.")]
    [InlineData("   ", "Deliverable 'consult' condition is blank.")]
    public void NotOneExpression_IsRefused(string when, string sentence)
    {
        Assert.Contains(sentence, Errors(WithDeliverableWhen(when)));
    }

    [Fact]
    public void TheLegacyGrammar_IsRefusedByName_WithTheSpelling()
    {
        var errors = Errors(WithNodeWhen("assessment", "node:scope == in_scope and kind == new"));

        Assert.Contains("Node 'assessment' condition 'node:scope == in_scope and kind == new' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write (node.scope.value == \"in_scope\" && input.kind == \"new\").", errors);
    }

    // ----- typing: the ≤24 clause rules, re-spelled ------------------------------

    [Theory]
    [InlineData("node.scope.value == \"maybe\"", "compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope).")]
    [InlineData("\"maybe\" != node.scope.value", "compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope).")]
    [InlineData("input.kind == \"urgent\"", "compares input.kind to 'urgent', which it does not declare (values: new, follow_up).")]
    [InlineData("node.scope.value > \"in_scope\"", "compares node.scope.value with >; an enum's value is compared with == or != only.")]
    [InlineData("node.scope.value", "tests node.scope.value for truth; compare it to one of its values instead.")]
    [InlineData("input.notes", "tests input.notes for truth; a text cannot be tested — compare it to a value.")]
    [InlineData("input.notes < \"a\"", "orders input.notes, which is a text; ordering operators apply to a number or a date.")]
    [InlineData("input.kind == \"new\" && input.notes", "tests input.notes for truth; a text cannot be tested — compare it to a value.")]
    [InlineData("input.nope == \"x\"", "reads input 'nope', which the package does not declare.")]
    [InlineData("node.nope.value == \"x\"", "reads node 'nope', which the package does not declare.")]
    [InlineData("node.assessment.value == \"x\"", "reads node.assessment.value, but format 'prose' is text and has no value; read node.assessment.text.")]
    [InlineData("input.kind + 1 > 2", "computes with input.kind, which is an enum; arithmetic takes numbers.")]
    [InlineData("input.kind .. 3", "uses 'RangeInclude', which is not a condition operator (&&, ||, !, ==, !=, <, >, <=, >=, + - * /).")]
    public void AComparison_IsTyped(string when, string tail)
    {
        Assert.Contains($"Deliverable 'consult' condition {tail}", Errors(WithDeliverableWhen(when)));
    }

    [Fact]
    public void ANumberAndADate_AreOrdered_AndMismatchesAreNamed()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!
                .Append(new WorkflowInputSpec("age", "Age", true, "number"))
                .Append(new WorkflowInputSpec("seen", "Seen on", true, "date"))
                .ToList(),
        };

        (WorkflowPackageManifest, Dictionary<string, string>) With(string when) =>
            (manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = when }).ToList() }, files);

        Assert.DoesNotContain(Errors(With("input.age >= 65 && input.seen < date.parse \"2026-01-01\"")), e => e.Contains("condition", StringComparison.Ordinal));
        Assert.DoesNotContain(Errors(With("(input.age + 5) >= 70")), e => e.Contains("condition", StringComparison.Ordinal));
        Assert.Contains("Deliverable 'consult' condition compares input.age to 'sixty', which is not a number.", Errors(With("input.age == \"sixty\"")));
        Assert.Contains("Deliverable 'consult' condition compares input.seen to '2026', which is not a date; write date.parse \"YYYY-MM-DD\".", Errors(With("input.seen == \"2026\"")));
        Assert.Contains("Deliverable 'consult' condition compares input.age (a number) with input.seen (a date); both sides must be numbers or both dates.", Errors(With("input.age > input.seen")));
    }

    [Fact]
    public void AnItemId_IsTypedAgainstTheFoldersItems_AtAFan()
    {
        Assert.DoesNotContain(Errors(WithInstanceWhen("item.id == \"hpi\"")), e => e.Contains("condition", StringComparison.Ordinal));
        Assert.Contains("Node 'section_notes' macro 'closing' condition compares item.id to 'nope', which the folder does not contain (items: hpi, plan).", Errors(WithInstanceWhen("item.id == \"nope\"")));
        Assert.Contains("Node 'section_notes' macro 'closing' condition tests item.id for truth; compare it to one of the folder's item ids instead.", Errors(WithInstanceWhen("item.id")));
        Assert.Contains("Node 'section_notes' macro 'closing' condition compares item.id with <; an item id is compared with == or != only.", Errors(WithInstanceWhen("item.id < \"hpi\"")));
    }

    [Fact]
    public void AnItemRead_WhereNothingFans_IsRefused()
    {
        Assert.Contains("Deliverable 'consult' condition reads item.id, but the node does not fan.", Errors(WithDeliverableWhen("item.id == \"hpi\"")));
    }

    // ----- sites and edges -------------------------------------------------------

    [Fact]
    public void ANodesOwnWhen_IsValidated_AndItsReadsAreEdges()
    {
        // Validated at last (it was not at 25 before #1037)…
        Assert.Contains("Node 'assessment' condition compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope).", Errors(WithNodeWhen("assessment", "node.scope.value == \"maybe\"")));

        // …and its reads are the node's edges, as a prompt read would be.
        var (manifest, files) = WithNodeWhen("assessment", "node.scope.value == \"in_scope\"");
        var edges = WorkflowDerivedEdges.Derive(manifest, files, V25Package.Catalog());
        Assert.Contains("scope", edges["assessment"]);
    }

    [Fact]
    public void AnInstanceWhen_JoinsTheNodesEdges_AndADeliverableWhenIsReadApart()
    {
        var (manifest, files) = WithInstanceWhen("node.scope.value == \"in_scope\"");
        manifest = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { When = "node.scope.value == \"in_scope\"" }).ToList() };
        Assert.DoesNotContain(Errors((manifest, files)), e => e.Contains("condition", StringComparison.Ordinal));

        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        Assert.Contains("scope", walk.EdgesOf("section_notes"));
        Assert.Equal(new[] { "scope" }, walk.DeliverableConditionReads("consult"));
        // The instance's, the deliverable's, and the fixture's own folder-item when.
        Assert.Equal(3, walk.Conditions.Count(c => c.Walk.IsValid));
    }

    [Fact]
    public void AWhenThatReadsTheNodeItGates_IsACycle()
    {
        var errors = Errors(WithNodeWhen("scope", "node.scope.value == \"in_scope\""));

        Assert.Contains(errors, e => e.StartsWith("nodes contain a cycle involving: scope", StringComparison.Ordinal));
    }

    [Fact]
    public void AFolderItemWhen_ReadsInputsOnly_InTheObjectSpelling()
    {
        var (manifest, files) = V25Package.Minimal();
        files["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md", "when": "node.scope.value == \"in_scope\"" } ] }""";

        Assert.Contains("Snippet folder 'sections' item 'hpi' condition reads node.scope; a folder item's when reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).", Errors((manifest, files)));
    }

    // ----- the probe ---------------------------------------------------------------

    [Fact]
    public void TheProbe_RefusesAConditionThatYieldsNoBoolean()
    {
        // Typed as untyped by the walker (a snippet's text is a text, compared to nothing here).
        Assert.Contains("Deliverable 'consult' condition must be a boolean expression; it yields a text.", Errors(WithDeliverableWhen("snippet.standards.text | string.upcase")));
    }

    [Fact]
    public void Evaluate_ReturnsTheBoolean_OrNull()
    {
        var globals = new ScriptObject { ["input"] = new ScriptObject { ["kind"] = "new" } };
        var context = new TemplateContextForTests();
        context.PushGlobal(globals);

        Assert.True(WorkflowConditionAt25.TryParse("input.kind == \"new\"", out var yes, out _));
        Assert.True(WorkflowConditionAt25.Evaluate(yes!, context));
        Assert.True(WorkflowConditionAt25.TryParse("input.kind == \"follow_up\"", out var no, out _));
        Assert.False(WorkflowConditionAt25.Evaluate(no!, context));
        Assert.True(WorkflowConditionAt25.TryParse("input.kind", out var text, out _));
        Assert.Null(WorkflowConditionAt25.Evaluate(text!, context));
    }

    private sealed class TemplateContextForTests : Scriban.TemplateContext
    {
        public TemplateContextForTests() => StrictVariables = true;
    }

    // ----- both spellings through one parser ------------------------------------

    [Theory]
    [InlineData("node:scope == in_scope")]
    [InlineData("kind == follow_up")]
    [InlineData("billable")]
    [InlineData("not billable")]
    [InlineData("patient.age >= 65")]
    [InlineData("count(prior_notes) > 0")]
    [InlineData("item:id == hpi")]
    [InlineData("seen_on < 2026-01-01")]
    [InlineData("urgent == true")]
    [InlineData("node:scope == in_scope and kind == new")]
    [InlineData("kind == new or (billable and not urgent)")]
    [InlineData("patient.age + 5 >= 70")]
    [InlineData("count(prior_notes) - 1 > patient.age * 2")]
    public void TheObjectSpelling_ReadsBack_IntoTheSameTree(string legacy)
    {
        Assert.True(WorkflowResultConditions.TryParseLegacy(legacy, out var tree, out var error), error);
        string? TypeOf(string id) => id switch { "kind" => "enum", "billable" or "urgent" => "boolean", "seen_on" => "date", "patient" => "object", "prior_notes" => "array", _ => "text" };
        var spelled = ConditionScriban.Spell(tree!, TypeOf);

        Assert.True(ConditionScriban.LooksLikeObjectSpelling(spelled), spelled);
        Assert.True(WorkflowResultConditions.TryParseExpression(spelled, out var back, out var backError), $"{spelled}: {backError}");
        Assert.Equal(tree!.Text, back!.Text);
    }

    [Theory]
    [InlineData("node.assessment.text == \"\"", "reads node.assessment.text; the builder reads an enum node's value (node.assessment.value).")]
    [InlineData("snippet.standards.text == \"x\"", "reads snippet.standards.text; the builder reads input.<path>, node.<id>.value and item.id (snippet is beyond it).")]
    [InlineData("input.notes | string.upcase", "pipes a value; a condition counts an array as (input.<id> | array.size) and reads nothing else through a pipe.")]
    public void ARead_BeyondTheBuildersSubset_IsSaid(string when, string tail)
    {
        Assert.False(ConditionScriban.TryUnspell(when, out _, out var error));
        Assert.Equal(tail, error);
    }

    [Theory]
    [InlineData("consult_draft == \"urgent\"", false)]
    [InlineData("kind == new", false)]
    [InlineData("node:scope == in_scope", false)]
    [InlineData("input.kind == \"new\"", true)]
    [InlineData("node.scope.value == \"a\"", true)]
    [InlineData("a == \"x\" && b", true)]
    public void TheTell_IsAReservedObjectRead_OrAConnective(string when, bool objectSpelling)
    {
        Assert.Equal(objectSpelling, ConditionScriban.LooksLikeObjectSpelling(when));
    }
}
