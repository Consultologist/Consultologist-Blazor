using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07): the packages the dialect's package tests read, built on
/// <see cref="SourceFixture.Minimal"/> with the formats of <see cref="ResolveFixture.WithFormats"/>: the agent
/// <c>plan</c> returns a <c>section_plan</c>, and helpers add an agent with a body, a <c>rendered</c> node, a computed
/// node, a snippet, a macro, a renderer, a loop over a list and a deliverable body. <see cref="Check"/> runs the steps
/// before the dialect (each asserted to accept) and then <see cref="SourceTemplates.Check"/>. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
internal static class TemplateFixture
{
    public const string Plain = "\"plaintext\"";

    /// <summary><see cref="SourceFixture.Minimal"/> with <c>section_plan</c>, <c>outline</c> and <c>verdict</c>, <c>plan</c> returning a <c>section_plan</c>.</summary>
    public static Dictionary<string, byte[]> Package() =>
        ResolveFixture.WithFormats(Minimal(), "section_plan", "outline", "verdict")
            .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
                "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"));

    /// <summary>Adds the agent <paramref name="id"/> at the top of <c>nodes/</c> with <paramref name="body"/> as its prompt.</summary>
    public static Dictionary<string, byte[]> WithAgent(this Dictionary<string, byte[]> files, string id, string body, string format = Plain) =>
        files.Listed("nodes/index.json", id).With($"nodes/{id}.md", Agent($"Agent {id}", format, body));

    /// <summary>Adds the <c>rendered</c> node <paramref name="id"/> at the top of <c>nodes/</c>.</summary>
    public static Dictionary<string, byte[]> WithRendered(this Dictionary<string, byte[]> files, string id, string body, string format = Plain) =>
        files.Listed("nodes/index.json", id)
            .With($"nodes/{id}.md", Md(SourceFixture.Node("rendered", $"Rendered {id}", format, true, false, "\"if\": null"), body));

    /// <summary>Adds the computed node <paramref name="id"/> (its <c>expression</c> as JSON text), declared run-dependent when asked.</summary>
    public static Dictionary<string, byte[]> WithComputed(
        this Dictionary<string, byte[]> files, string id, string format, string expression, bool runDependent = false)
    {
        files.Listed("nodes/index.json", id)
            .With($"nodes/{id}.md", Md(SourceFixture.Node("computed", $"Computed {id}", format, true, false,
                $"\"if\": null, \"expression\": {expression}"), ""));
        return runDependent ? files.RunDependent($"nodes/{id}.md") : files;
    }

    /// <summary>Declares the node of <paramref name="path"/> (a leaf's file or a flow node's <c>index.json</c>) run-dependent.</summary>
    public static Dictionary<string, byte[]> RunDependent(this Dictionary<string, byte[]> files, string path) =>
        files.WithJson(path, h => h["runDependent"] = true);

    /// <summary>Adds the single snippet <paramref name="id"/> of text format <paramref name="format"/>.</summary>
    public static Dictionary<string, byte[]> WithSnippet(this Dictionary<string, byte[]> files, string id, string body, string format = "plaintext") =>
        files.Listed("snippets/index.json", id).With($"snippets/{id}.md", Md(
            $"{{ \"label\": \"Snippet {id}\", \"description\": null, \"if\": null, \"format\": \"{format}\" }}", body));

    /// <summary>Adds the macro <paramref name="id"/>, its instruction one line holding its own marker inline.</summary>
    public static Dictionary<string, byte[]> WithMacro(this Dictionary<string, byte[]> files, string id, string body) =>
        files.Listed("macros/index.json", id).With($"macros/{id}.md", Md(
            $"{{ \"label\": \"Macro {id}\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
            + $"\"instruction\": [\"Place {{{{ macro.{id}.content }}}} at the end of the plan.\"] }}", body));

    /// <summary>Adds the renderer <paramref name="id"/> of <paramref name="input"/> (JSON text) returning <paramref name="format"/>.</summary>
    public static Dictionary<string, byte[]> WithRenderer(
        this Dictionary<string, byte[]> files, string id, string input, string body, string format = "markdown_block") =>
        files.Listed("renderers/index.json", id).With($"renderers/{id}.md", Md(
            $"{{ \"label\": \"Renderer {id}\", \"kind\": \"rendered\", \"description\": null, \"input\": {input}, \"format\": \"{format}\" }}", body));

    /// <summary>Adds the loop <paramref name="id"/> over <paramref name="source"/> whose body is the agent <paramref name="inner"/>.</summary>
    public static Dictionary<string, byte[]> WithLoop(this Dictionary<string, byte[]> files, string id, string source, string inner, string body) =>
        files.Listed("nodes/index.json", id)
            .With($"nodes/{id}/index.json", Json(SourceFixture.Node("for", $"Loop {id}", ArrayOf(Plain), false, true,
                $"\"in\": \"{source}\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + $"\"itemLabel\": null, \"nodes\": [\"{inner}\"], \"ret\": \"node.{id}.{inner}\"")))
            .With($"nodes/{id}/{inner}.md", Agent($"Agent {inner}", Plain, body));

    /// <summary>Adds the while <paramref name="id"/> (two passes) whose body is the agent <paramref name="inner"/>.</summary>
    public static Dictionary<string, byte[]> WithWhile(this Dictionary<string, byte[]> files, string id, string inner, string body) =>
        files.Listed("nodes/index.json", id)
            .With($"nodes/{id}/index.json", Json(SourceFixture.Node("while", $"While {id}", Plain, false, true,
                "\"while\": { \"<\": [{ \"var\": \"pass.number\" }, 2] }, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + $"\"nodes\": [\"{inner}\"], \"ret\": \"node.{id}.{inner}\"")))
            .With($"nodes/{id}/{inner}.md", Agent($"Agent {inner}", Plain, body));

    /// <summary>Adds two concept-extracting agents and the check <c>coverage</c> (<c>terms_subset</c> of one in the other).</summary>
    public static Dictionary<string, byte[]> WithCheck(this Dictionary<string, byte[]> files) =>
        files.WithAgent("extract_input_terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"")
            .WithAgent("extract_note_terms", "List the clinical terms in the plan.\n\n{{ node.plan.content }}\n", "\"concept_list\"")
            .Listed("checks/index.json", "coverage")
            .With("checks/coverage.md", Md(SourceFixture.Node("computed", "Terms covered", "\"check_result\"", true, true,
                "\"if\": null, \"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
                + "{ \"var\": \"node.extract_note_terms.value\" }, \"The plan does not cover every term of the draft.\"] }"), ""));

    /// <summary>Gives the deliverable <c>note</c> <paramref name="body"/>.</summary>
    public static Dictionary<string, byte[]> WithDeliverable(this Dictionary<string, byte[]> files, string body) =>
        files.With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"), body));

    /// <summary>The reader, the types, the resolver and the expressions (each asserted to accept), then the dialect.</summary>
    public static TemplateCheck Check(Dictionary<string, byte[]> files)
    {
        var registry = ResolveFixture.Published();
        var parsed = SourceParser.Parse(files, registry.Names);
        Assert.True(parsed.Package != null, "The reader refused: " + Said(parsed.Refusals));
        var types = SourceTypes.Check(parsed.Package!, registry);
        Assert.True(types.Types != null, "The type rules refused: " + Said(types.Refusals));
        var (refusals, resolution) = SourceResolve.Check(parsed.Package!, types.Types!);
        Assert.True(resolution != null, "The resolver refused: " + Said(refusals));
        var expressions = SourceExpressions.Check(parsed.Package!, types.Types!, resolution!.Scopes);
        Assert.True(expressions.Expressions != null, "The expressions were refused: " + Said(expressions.Refusals));
        return SourceTemplates.Check(parsed.Package!, types.Types!, resolution, registry);
    }

    /// <summary>The checked body of the file <paramref name="path"/>; the check is asserted to refuse nothing.</summary>
    public static CheckedBody Body(TemplateCheck check, string path)
    {
        Assert.True(check.Templates != null, "The dialect refused: " + Said(check.Refusals));
        return check.Templates!.Bodies.Single(b => b.Host.Path == path);
    }

    /// <summary>The one refusal of <paramref name="check"/>, its sentence held to its row.</summary>
    public static SourceRefusal Single(TemplateCheck check)
    {
        Assert.True(check.Refusals.Count == 1, "Expected one refusal: " + Said(check.Refusals));
        var refusal = check.Refusals[0];
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    /// <summary>The refusals, one line, for an assertion's message.</summary>
    public static string Said(IEnumerable<SourceRefusal> refusals) =>
        string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}"));

    /// <summary>Adds <paramref name="id"/> to the items of the index <paramref name="index"/>.</summary>
    public static Dictionary<string, byte[]> Listed(this Dictionary<string, byte[]> files, string index, string id) =>
        files.WithJson(index, i => i["items"]!.AsArray().Add((JsonNode)id));
}
