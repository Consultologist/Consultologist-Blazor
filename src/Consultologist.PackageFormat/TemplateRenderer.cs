using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// A renderer pipe: its name, the format whose node text it renders (design
/// § 1, #1058: a pipe renders <c>node.&lt;id&gt;.content</c>, and only of a node
/// of this format — the walker refuses any other operand at publish), and
/// (#1138) <see cref="Render"/> — its bytes over a node's concept-list JSON and
/// the concept source the engine stamps (<see cref="TemplateNodeOutput.ConceptSource"/>).
/// </summary>
public sealed record TemplatePipeSpec(string Name, string Format, Func<string, string, string> Render);

/// <summary>The renderers a 25 template applies as pipes (design § 1: "renderers are pipes"; the closed set bindings' <c>as:</c> had), each typed by the format it renders (#1058).</summary>
public static class TemplatePipes
{
    /// <summary>The attested, byte-stable context rendering <c>as: concept-context</c> gave.</summary>
    public const string ConceptContext = "concept_context";

    /// <summary>The bullet rendering <c>as: concept-bullets</c> gave.</summary>
    public const string ConceptBullets = "concept_bullets";

    /// <summary>The closed set, each with the format it renders. Declared before <see cref="All"/>, which derives from it.</summary>
    public static readonly IReadOnlyList<TemplatePipeSpec> Specs = new[]
    {
        new TemplatePipeSpec(ConceptContext, WorkflowFormatIds.ConceptList, (json, source) => ConceptRenderers.Context(ConceptOutputContract.Deserialize(json, source))),
        new TemplatePipeSpec(ConceptBullets, WorkflowFormatIds.ConceptList, (json, source) => ConceptRenderers.Bullets(ConceptOutputContract.Deserialize(json, source))),
    };

    /// <summary>The names — for the renderer's globals, the validator's probe and the editor's select.</summary>
    public static readonly IReadOnlyList<string> All = Specs.Select(spec => spec.Name).ToList();

    /// <summary>
    /// #1138: the engine's renderers as <see cref="TemplateRenderer.Render"/> takes them (was the
    /// Api's ConceptPipes.All): each spec's <see cref="TemplatePipeSpec.Render"/> over the node's
    /// text and its concept source — "node" when the scope stamped none. Declared after <see cref="Specs"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>> Engine =
        Specs.ToDictionary(spec => spec.Name, spec => (Func<TemplateNodeOutput, string>)(output => spec.Render(output.Text, output.ConceptSource ?? "node")), StringComparer.Ordinal);

    /// <summary>The spec of a pipe; null when the name is not a renderer pipe.</summary>
    public static TemplatePipeSpec? SpecOf(string name) =>
        Specs.FirstOrDefault(spec => string.Equals(spec.Name, name, StringComparison.Ordinal));

    /// <summary>The format a pipe renders; null when the name is not a renderer pipe.</summary>
    public static string? FormatOf(string name) => SpecOf(name)?.Format;
}

/// <summary>
/// An upstream node's output as the template reads it: its composed text, the
/// format type that says what <c>.value</c> parses to, and the concept source
/// the engine stamps when a pipe renders its concepts (null when none).
/// </summary>
public sealed record TemplateNodeOutput(
    string Text,
    string Type,
    string? ConceptSource = null,
    // #1071: what .value parses when it is not the text — the editor's preview
    // scope keeps .content a token and .value a carrier; a run leaves it null.
    string? ValueJson = null);

/// <summary>One item of a snippet folder: id, name and its template text (rendered when read).</summary>
public sealed record TemplateFolderItem(string Id, string Name, string Text);

/// <summary>
/// #1154 (v26 design § 1.3): a loop as a template reads it — iterable, each
/// element carrying the fan item, the returned node's output for that item and
/// every body node's by id; the list itself reading as the loop's composed
/// text (<see cref="Output"/>, the aggregator's bytes) and as the array of the
/// returned node's values. <see cref="Body"/> is body node id → item id → output.
/// </summary>
public sealed record TemplateLoopOutput(
    string RetId,
    IReadOnlyList<TemplateFanItem> Items,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, TemplateNodeOutput>> Body,
    TemplateNodeOutput Output,
    // (the two trailing optionals: #1235's skipped set; #1243's collection — the editor's preview names
    // the loop's collection so an inert folder item can leave the preview's elements as it leaves the run's)
    // #1235 (v27 design § 1.4): the items an inner if/case skipped by condition —
    // still elements (the loop is the collection), with `skipped` true, content
    // and value null, and only the body nodes that ran; absent from the values.
    IReadOnlySet<string>? Skipped = null,
    string? Collection = null);

/// <summary>
/// The fan item an instance runs for: a snippet fan carries <c>Text</c> (a
/// template, rendered when read); an input fan carries <c>Value</c> (the
/// element's canonical form or carrier JSON) typed by <c>Element</c>.
/// </summary>
public sealed record TemplateFanItem(string Id, string Name, string? Text = null, string? Value = null, WorkflowDeclarationNode? Element = null);

/// <summary>A macro as the template reads it: its text (a template, rendered when read) and its instruction (the <c>.prompt</c> half the mechanics sentence completes).</summary>
public sealed record TemplateMacroSource(
    string Id,
    string Name,
    string Text,
    string Instruction,
    // #1157 (v26 design § 2.2): the macro's own gate, its header's `if` — carried
    // beside the label and the instruction; judged by the engine (#1177). Null
    // below 26 and when the header declares none.
    string? If = null);

/// <summary>The run facts (<c>run.date</c> as yyyy-MM-dd, <c>run.job</c>, <c>run.package</c>, <c>run.host</c>), as the v11 tokens gave them.</summary>
public sealed record TemplateRunFacts(string Date, string Job, string Package, string Host);

/// <summary>The profile facts (<c>profile.name</c>, <c>profile.signature</c>), as the v11/v12 tokens gave them.</summary>
public sealed record TemplateProfileFacts(string Name, string Signature);

/// <summary>
/// Everything a 25 render can read (design § 1): the seven objects' sources.
/// Inputs are the effective map (every declared id, the carrier for a
/// structured value) typed as <see cref="PromptTemplateRenderer"/> types them;
/// nodes are the upstream outputs this template's edges name; snippets and
/// folders are the package's texts and the folder's start-time item set;
/// macros are the library's texts and instructions.
/// </summary>
public sealed record TemplateRenderScope(
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string>? InputTypes,
    IReadOnlyDictionary<string, WorkflowInputSpec>? Declarations,
    IReadOnlyDictionary<string, TemplateNodeOutput> Nodes,
    IReadOnlyDictionary<string, string> Snippets,
    IReadOnlyDictionary<string, IReadOnlyList<TemplateFolderItem>> Folders,
    TemplateFanItem? Item,
    IReadOnlyDictionary<string, TemplateMacroSource> Macros,
    TemplateRunFacts Run,
    TemplateProfileFacts Profile,
    // #1089 (v26 design § 2.3): the macros whose inline read this render must
    // leave empty — judged by the caller over this scope with
    // WorkflowInlineGates.Suppressed (the running node's instance gates); null
    // is the 25 render, nothing suppressed.
    IReadOnlySet<string>? SuppressedPrompts = null,
    // #1090 (v26 design § 2.2): the macros whose own gate does not hold for this
    // render — inert: .prompt and .content both empty, nothing recorded (judged
    // by the caller with WorkflowInlineGates.Inert); null is the 25 render.
    IReadOnlySet<string>? InertMacros = null,
    // #1094 (v26 design § 2.1, § 5.4): the standalone snippets whose own gate
    // does not hold — content empty where read (judged by the caller with
    // WorkflowInlineGates.InertSnippets); a folder item's gate is the fan
    // filter (the engine's) and the direct read's (#1116). Null is the 25 render.
    IReadOnlySet<string>? InertSnippets = null,
    // #1099 (v26 design § 5.3): the macro folders — macro.<folder> an array of
    // macro objects in the index's order; the inert and suppressed sets key an
    // item as <folder>/<item>. Null is the 25 render.
    IReadOnlyDictionary<string, IReadOnlyList<TemplateMacroSource>>? MacroFolders = null,
    // #1154 (v26 design § 1.3): the loops this render reads whole — node.<loop>
    // iterable, its elements the items with the body nodes' outputs; a loop
    // read as text alone needs no entry (Nodes carries its composed text).
    IReadOnlyDictionary<string, TemplateLoopOutput>? Loops = null,
    // #1154 (§ 1.3): the if/case nodes that took no branch — node.<id> reads
    // null, so {{ if node.<id> }} guards it. Null is the 25 render.
    IReadOnlySet<string>? SkippedNodes = null,
    // #1105/#1154: at 26 the item, snippet and macro objects carry `label`
    // (the header's) beside name. False is the 25 render.
    bool Labelled = false,
    // #1155 (v26 design § 3.1): the deliverables this render reads — id → the
    // composed text (the sources' sections, before the deliverable's macros and
    // the signature), null for one skipped by its `if` (the read is null; a
    // guard sees it). Null is a render that reads none.
    IReadOnlyDictionary<string, string?>? Deliverables = null,
    // #1179 (v26 design § 5.2): a standalone snippet's label — its header's — by
    // id, for `snippet.<id>.label`; a folder item's rides the folder item. Null
    // is the 25 render (the id names the snippet).
    IReadOnlyDictionary<string, string>? SnippetLabels = null);

/// <summary>
/// The specVersion 25 render (design §§ 1, 4, 5.3; B3 #1002): one Scriban
/// render of a template over the seven reserved objects with closed members —
/// <c>input</c>, <c>node</c>, <c>snippet</c>, <c>item</c>, <c>macro</c>,
/// <c>run</c>, <c>profile</c> — and the renderer pipes. Snippets, folder items
/// and macro texts are templates themselves: their <c>.content</c> renders over
/// the same scope the first time it is read and is kept for the render (the
/// validator refused cycles at publish). <c>macro.&lt;id&gt;.prompt</c> is
/// the instruction composed with <see cref="WorkflowMacroMechanics"/>.
/// <c>node.&lt;id&gt;.value</c> is the output parsed as JSON on a structured
/// format, the text itself otherwise. Strict: an undeclared member or an
/// unknown pipe fails the render with the subject in the sentence.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>
    /// What a render read, where the engine needs to know (B5 #1004, #1044):
    /// <see cref="PromptReads"/> — every <c>macro.&lt;id&gt;.prompt</c> the render
    /// evaluated (the inline macros the prompt that ran instructed, exactly the
    /// instructions the model saw; a read behind a branch not taken is not one of
    /// them); <see cref="ContentReads"/> — every <c>macro.&lt;id&gt;.content</c> the
    /// TOP-LEVEL template evaluated, in order, once per read (#1044: a reply's
    /// inline macros, as the record names them; a read inside a filled text is that
    /// text's, not an entry).
    /// </summary>
    public sealed class TemplateRenderTrace
    {
        private int _depth;

        public ISet<string> PromptReads { get; } = new HashSet<string>(StringComparer.Ordinal);

        public IList<string> ContentReads { get; } = new List<string>();

        internal void ContentRead(string macroId)
        {
            if (_depth == 0)
            {
                ContentReads.Add(macroId);
            }
        }

        internal IDisposable Nested() => new Nesting(this);

        private sealed class Nesting : IDisposable
        {
            private readonly TemplateRenderTrace _trace;

            public Nesting(TemplateRenderTrace trace)
            {
                _trace = trace;
                _trace._depth++;
            }

            public void Dispose() => _trace._depth--;
        }
    }

    /// <param name="trace">When given, what this render read (<see cref="TemplateRenderTrace"/>).</param>
    public static string Render(
        string subject,
        string templateText,
        TemplateRenderScope scope,
        IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null,
        TemplateRenderTrace? trace = null)
    {
        var globals = Globals(subject, scope, pipes, trace);
        return RenderWith(subject, templateText, globals, trace, nested: false);
    }

    /// <summary>#1044: a macro's <c>.content</c> is produced on every read so the trace counts each one (the render itself is cached by its Lazy).</summary>
    private static readonly ISet<string> UncachedMacroMembers = new HashSet<string>(StringComparer.Ordinal) { TemplateMembers.Content };

    /// <summary>The globals a probe or a renderer pushes: the seven objects and the pipes, over this scope.</summary>
    internal static ScriptObject Globals(string subject, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes, TemplateRenderTrace? trace = null)
    {
        var globals = new ScriptObject();

        var input = new ClosedObject(subject, TemplateObjects.Input);
        foreach (var (id, value) in scope.Inputs)
        {
            input[id] = PromptTemplateRenderer.Materialise(id, value, scope.InputTypes, scope.Declarations);
        }

        globals[TemplateObjects.Input] = input;

        var node = new ClosedObject(subject, TemplateObjects.Node);
        NodeObject NodeObjectOf(string id, TemplateNodeOutput output, string path) =>
            // #1059: the node object carries its identity — a renderer pipe takes it whole.
            new(subject, id, output,
                new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
                {
                    [TemplateMembers.Content] = () => output.Text,
                    [TemplateMembers.Value] = () => NodeValue(subject, id, output),
                },
                path);

        foreach (var (id, output) in scope.Nodes)
        {
            if (scope.Loops?.ContainsKey(id) == true)
            {
                continue; // the loop's list below reads as this text too
            }

            node[id] = NodeObjectOf(id, output, $"{TemplateObjects.Node}.{id}");
        }

        // #1154 (v26 design § 1.3): a loop iterates — each element the fan item,
        // the returned node's content and value, and every body node by id (a
        // NodeObject, so a renderer pipe takes it); the list reads as the loop's
        // composed text and as the array of the returned node's values. One
        // builder with the validator's probe, so the two member sets are one.
        foreach (var (id, loop) in scope.Loops ?? new Dictionary<string, TemplateLoopOutput>())
        {
            var path = $"{TemplateObjects.Node}.{id}";
            // #1235 (v27 design § 1.4): a skipped item is an element with `skipped`
            // true, no content and no value, and only the body nodes that ran for it.
            var present = loop.Items.Where(item => loop.Skipped?.Contains(item.Id) != true).ToList();
            var elements = loop.Items.Select(item =>
            {
                var elementPath = $"{path}[{item.Id}]";
                var skipped = loop.Skipped?.Contains(item.Id) == true;
                var ret = skipped ? null : loop.Body[loop.RetId][item.Id];
                var body = loop.Body
                    .Where(pair => pair.Value.ContainsKey(item.Id))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => (object?)NodeObjectOf(pair.Key, pair.Value[item.Id], $"{elementPath}.{pair.Key}"),
                        StringComparer.Ordinal);
                return new LoopElement(
                    ItemObject(subject, $"{elementPath}.{TemplateMembers.Item}", item, globals, trace, scope.Labelled),
                    ret?.Text,
                    true,
                    ret is null ? null : NodeValue(subject, loop.RetId, ret),
                    body,
                    skipped);
            });
            var values = new ScriptArray(present.Select(item => NodeValue(subject, loop.RetId, loop.Body[loop.RetId][item.Id])));

            node[id] = LoopList(() => new ClosedObject(subject, $"{path}[]"), elements, loop.Output.Text, true, values);
        }

        // #1154 (§ 1.3): a flow node that took no branch reads null — a guard sees it.
        foreach (var id in scope.SkippedNodes ?? new HashSet<string>())
        {
            node[id] = null;
        }

        globals[TemplateObjects.Node] = node;

        var snippet = new ClosedObject(subject, TemplateObjects.Snippet);
        foreach (var (id, text) in scope.Snippets)
        {
            // #1094: an inert snippet's content is empty where read; its id as ever.
            // #1179: its label is the header's at 26.
            var inert = scope.InertSnippets?.Contains(id) == true;
            var label = scope.SnippetLabels?.GetValueOrDefault(id) ?? id;
            snippet[id] = Rendered(subject, $"{TemplateObjects.Snippet}.{id}", id, label, inert ? string.Empty : text, $"Snippet '{id}'", globals, trace, scope.Labelled);
        }

        foreach (var (folder, items) in scope.Folders)
        {
            var array = new ScriptArray();
            foreach (var item in items)
            {
                // #1116 (v26 design § 5.4): a declared item is a member of its folder,
                // read directly as snippet.<folder>.<item>; an item gated off (#1094,
                // the engine's key <folder>/<item>) reads empty there and — #1179 —
                // is left out of every loop over its folder, exactly as the fan
                // filter leaves it out: a member, never an element.
                var inert = scope.InertSnippets?.Contains($"{folder}/{item.Id}") == true;
                var rendered = Rendered(subject, $"{TemplateObjects.Snippet}.{folder}[{item.Id}]", item.Id, item.Name, inert ? string.Empty : item.Text, $"Snippet '{folder}/{item.Id}'", globals, trace, scope.Labelled);
                if (!inert)
                {
                    array.Add(rendered);
                }

                array.ScriptObject[item.Id] = rendered;
            }

            snippet[folder] = array;
        }

        globals[TemplateObjects.Snippet] = snippet;

        if (scope.Item is { } fanItem)
        {
            globals[TemplateObjects.Item] = ItemObject(subject, TemplateObjects.Item, fanItem, globals, trace, scope.Labelled);
        }

        var macro = new ClosedObject(subject, TemplateObjects.Macro);

        // #1099: one macro object — a flat macro keyed by its id, a folder item by <folder>/<item>.
        LazyMembersObject MacroObject(string key, string path, TemplateMacroSource source)
        {
            // #1044: the instruction is a template, rendered over the same scope
            // FIRST; the mechanics sentence — carrying the literal read the model
            // is to write — is appended after, never rendered. #1157: .instruction
            // is the same read without the sentence (what the model will be told,
            // as text). Null is an empty read: nothing is told, nothing recorded.
            string? RenderInstruction()
            {
                // #1089 (v26 design § 2.3): an inline read the running node's
                // instance gate suppresses renders empty — no instruction, no
                // mechanics sentence, no read recorded (the model is told nothing).
                if (scope.SuppressedPrompts?.Contains(key) == true || scope.InertMacros?.Contains(key) == true)
                {
                    return null;
                }

                // #1096 (§ 2.3, § 2.5): the instruction renders first; a blank result —
                // no instruction, or a template that renders to nothing — is an
                // empty read: no mechanics sentence, no read recorded.
                var rendered = RenderWith($"Macro '{key}' instruction", source.Instruction, globals, trace, nested: true);
                if (string.IsNullOrWhiteSpace(rendered))
                {
                    return null;
                }

                trace?.PromptReads.Add(key);
                return rendered;
            }

            var members = new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
            {
                [TemplateMembers.Id] = () => source.Id,
                [TemplateMembers.Name] = () => source.Name,
                [TemplateMembers.Prompt] = () => RenderInstruction() is { } rendered ? WorkflowMacroMechanics.Prompt(source.Id, rendered) : string.Empty,
            };
            if (scope.Labelled)
            {
                // #1105 (v26 design § 5): at 26 the member is label — the header's.
                members[TemplateMembers.Label] = () => source.Name;
                // #1157 (§ 5.2): the instruction readable as text — the same read as .prompt.
                members[TemplateMembers.Instruction] = () => RenderInstruction() ?? string.Empty;
            }

            var text = new Lazy<string>(() => RenderWith($"Macro '{key}'", source.Text, globals, trace, nested: true));
            members[TemplateMembers.Content] = () =>
            {
                // #1090: an inert macro's content is empty where read — no blank
                // block, no read recorded (a placed instance composes nothing).
                if (scope.InertMacros?.Contains(key) == true)
                {
                    return string.Empty;
                }

                trace?.ContentRead(key);
                return text.Value;
            };
            return new LazyMembersObject(subject, path, members, UncachedMacroMembers);
        }

        foreach (var (id, source) in scope.Macros)
        {
            macro[id] = MacroObject(id, $"{TemplateObjects.Macro}.{id}", source);
        }

        // #1099 (v26 design § 5.3): a macro folder iterates as an array, in the index's order.
        foreach (var (folder, items) in scope.MacroFolders ?? new Dictionary<string, IReadOnlyList<TemplateMacroSource>>())
        {
            var array = new ScriptArray();
            foreach (var item in items)
            {
                var rendered = MacroObject($"{folder}/{item.Id}", $"{TemplateObjects.Macro}.{folder}[{item.Id}]", item);
                array.Add(rendered);
                // #1116: macro.<folder>.<item> reads the item directly.
                array.ScriptObject[item.Id] = rendered;
            }

            macro[folder] = array;
        }

        globals[TemplateObjects.Macro] = macro;

        globals[TemplateObjects.Run] = new ClosedObject(subject, TemplateObjects.Run)
        {
            ["date"] = scope.Run.Date,
            ["job"] = scope.Run.Job,
            ["package"] = scope.Run.Package,
            ["host"] = scope.Run.Host,
        };

        globals[TemplateObjects.Profile] = new ClosedObject(subject, TemplateObjects.Profile)
        {
            ["name"] = scope.Profile.Name,
            [WorkflowMacroPlaceholders.SignatureFact] = scope.Profile.Signature,
        };

        // #1155 (v26 design § 3.1): the sixth object — deliverable.<id>.content,
        // the composed text; a skipped deliverable reads null. One member, the
        // probe's; a renderer pipe refuses it as it refuses any non-node.
        if (scope.Deliverables is { } composed)
        {
            var deliverable = new ClosedObject(subject, TemplateObjects.Deliverable);
            foreach (var (id, text) in composed)
            {
                deliverable[id] = text is null
                    ? null
                    : new LazyMembersObject(subject, $"{TemplateObjects.Deliverable}.{id}", new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
                    {
                        [TemplateMembers.Content] = () => text,
                    });
            }

            globals[TemplateObjects.Deliverable] = deliverable;
        }

        foreach (var name in TemplatePipes.All)
        {
            var pipe = pipes is not null && pipes.TryGetValue(name, out var found) ? found : null;
            globals.Import(name, (Func<object?, string>)(argument => ApplyPipe(subject, name, pipe, argument, scope.Nodes)));
        }

        return globals;
    }

    /// <summary>
    /// A pipe renders a node and nothing else. #1059 (v26 design § 7.1): at 26
    /// the operand is the node object, which carries its output whole — its
    /// concept source with it; below 26 the operand is the very string a
    /// <c>node.&lt;id&gt;.content</c> read produced, found by reference.
    /// </summary>
    private static string ApplyPipe(string subject, string name, Func<TemplateNodeOutput, string>? pipe, object? argument, IReadOnlyDictionary<string, TemplateNodeOutput> nodes)
    {
        if (pipe is null)
        {
            throw new InvalidOperationException($"{subject} pipes through {name}, which this renderer does not provide.");
        }

        if (argument is NodeObject node)
        {
            return pipe(node.Output);
        }

        if (argument is not string text)
        {
            throw new InvalidOperationException($"{subject} pipes a {Describe(argument)} through {name}; a renderer pipe takes a node (node.<id>) — or, below specVersion 26, its text (node.<id>.content).");
        }

        var output = nodes.Values.FirstOrDefault(node => ReferenceEquals(node.Text, text));

        if (output is null)
        {
            throw new InvalidOperationException($"{subject} pipes text that is not a node's output through {name}; the pipe renders a node's text (node.<id>.content).");
        }

        return pipe(output);
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        ScriptArray => "list",
        ScriptObject => "object",
        _ => value.GetType().Name.ToLowerInvariant(),
    };

    private static LazyMembersObject Rendered(string subject, string path, string id, string name, string text, string templateSubject, ScriptObject globals, TemplateRenderTrace? trace, bool labelled = false)
    {
        var rendered = new Lazy<string>(() => RenderWith(templateSubject, text, globals, trace, nested: true));
        var members = new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
        {
            [TemplateMembers.Id] = () => id,
            [TemplateMembers.Name] = () => name,
            [TemplateMembers.Content] = () => rendered.Value,
        };
        if (labelled)
        {
            // #1105 (v26 design § 5): at 26 the member is label — the header's.
            members[TemplateMembers.Label] = () => name;
        }

        return new LazyMembersObject(subject, path, members);
    }

    /// <summary>The fan item as a template reads it: a snippet item renders its text when read; an input element carries its materialised value.</summary>
    private static ScriptObject ItemObject(string subject, string path, TemplateFanItem fanItem, ScriptObject globals, TemplateRenderTrace? trace, bool labelled)
    {
        if (fanItem.Text is not null)
        {
            return Rendered(subject, path, fanItem.Id, fanItem.Name, fanItem.Text, $"{subject} item '{fanItem.Id}'", globals, trace, labelled);
        }

        var element = new ClosedObject(subject, path)
        {
            [TemplateMembers.Id] = fanItem.Id,
            [TemplateMembers.Name] = fanItem.Name,
            [TemplateMembers.Value] = PromptTemplateRenderer.MaterialiseElement(fanItem.Value ?? string.Empty, fanItem.Element),
        };
        if (labelled)
        {
            element[TemplateMembers.Label] = fanItem.Name;
        }

        return element;
    }

    /// <summary>#1154: one element of a loop's list — the fan item, the returned node's content and (when it has one) value, and the body nodes by id.</summary>
    internal sealed record LoopElement(object Item, object? Content, bool HasValue, object? Value, IReadOnlyDictionary<string, object?> Body, bool Skipped = false);

    /// <summary>
    /// #1154 (v26 design § 1.3): the loop's list, ONE shape for the validator's
    /// probe and the render — each element carries <c>item</c>, <c>content</c>,
    /// <c>value</c> (when the returned node has one) and every body node by id;
    /// the list itself carries <c>content</c> (the loop's composed text) and
    /// <c>value</c> (the array of the returned node's values). The element
    /// container is the caller's: the probe's plain object, the render's closed one.
    /// </summary>
    internal static ScriptArray LoopList(Func<ScriptObject> newElement, IEnumerable<LoopElement> elements, object? content, bool hasValue, object? value)
    {
        var list = new ScriptArray();

        foreach (var e in elements)
        {
            var element = newElement();
            element[TemplateMembers.Item] = e.Item;
            element[TemplateMembers.Content] = e.Content;
            if (e.HasValue)
            {
                element[TemplateMembers.Value] = e.Value;
            }

            // #1235 (v27 design § 1.4): every element says whether its item was
            // skipped by condition; the walker admits the member at 27 only.
            element[TemplateMembers.Skipped] = e.Skipped;

            foreach (var (id, member) in e.Body)
            {
                element[id] = member;
            }

            list.Add(element);
        }

        list.ScriptObject[TemplateMembers.Content] = content;
        if (hasValue)
        {
            list.ScriptObject[TemplateMembers.Value] = value;
        }

        return list;
    }

    private static string RenderWith(string subject, string templateText, ScriptObject globals, TemplateRenderTrace? trace, bool nested)
    {
        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            throw new InvalidOperationException(
                $"{subject} does not parse: {string.Join(" ", template.Messages.Select(m => m.Message))}");
        }

        using var nesting = nested ? trace?.Nested() : null;
        return Unwrap(subject, "render", () => template.Render(ContextWith(globals)));
    }

    /// <summary>The strict context a render — or (#1038) a condition's evaluation — runs in, with this scope's globals pushed.</summary>
    internal static TemplateContext ContextFor(string subject, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes) =>
        ContextWith(Globals(subject, scope, pipes));

    private static TemplateContext ContextWith(ScriptObject globals)
    {
        var context = new PromptTemplateRenderer.RenderingContext();
        context.PushGlobal(globals);
        return context;
    }

    /// <summary>
    /// Runs a Scriban evaluation and surfaces the sentence underneath: Scriban
    /// wraps what a member producer or a pipe throws, and that sentence already
    /// names its subject; anything else becomes "{subject} failed to {verb}: …".
    /// </summary>
    internal static T Unwrap<T>(string subject, string verb, Func<T> run)
    {
        try
        {
            return run();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is InvalidOperationException ours)
                {
                    ExceptionDispatchInfo.Capture(ours).Throw();
                }
            }

            throw new InvalidOperationException($"{subject} failed to {verb}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// <c>node.&lt;id&gt;.value</c>: the output parsed as JSON on an object or
    /// array format (a ScriptObject / ScriptArray at every depth, numbers as
    /// decimals); the text itself on a text or enum format.
    /// </summary>
    private static object? NodeValue(string subject, string nodeId, TemplateNodeOutput output)
    {
        if (output.Type is not (WorkflowInputTypes.Object or WorkflowInputTypes.Array))
        {
            return output.Text;
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(output.ValueJson ?? output.Text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{subject} reads node.{nodeId}.value, but the node's output is not JSON: {ex.Message}");
        }

        return FromJson(parsed);
    }

    /// <summary>JSON as Scriban sees it: objects as ScriptObjects, arrays as ScriptArrays, numbers as decimals.</summary>
    internal static object? FromJson(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var script = new ScriptObject();
                foreach (var (key, value) in obj)
                {
                    script[key] = FromJson(value);
                }

                return script;
            case JsonArray array:
                return new ScriptArray(array.Select(FromJson));
            case JsonValue value:
                var element = value.GetValue<JsonElement>();
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : decimal.Parse(element.GetRawText(), CultureInfo.InvariantCulture),
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>
    /// A reserved object with a closed member set: a read outside the set
    /// fails with the subject rather than rendering empty (Scriban's relaxed
    /// member access, which stays on for a node's parsed value, where an
    /// optional field may be absent).
    /// </summary>
    private class ClosedObject : ScriptObject
    {
        private readonly string _subject;
        private readonly string _path;

        public ClosedObject(string subject, string path)
        {
            _subject = subject;
            _path = path;
        }

        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (base.TryGetValue(context, span, member, out value))
            {
                return true;
            }

            throw new InvalidOperationException($"{_subject} reads {_path}.{member}, which this render has no value for.");
        }
    }

    /// <summary>
    /// A closed-member object whose members are produced on first read and
    /// kept: a snippet's or macro's rendered text, a node's parsed value. A
    /// member outside the set is absent, so a strict read of it fails as any
    /// undeclared member does.
    /// </summary>
    /// <summary>#1059: a node's object — its members, and the identity a renderer pipe takes.</summary>
    private sealed class NodeObject : LazyMembersObject
    {
        public NodeObject(string subject, string id, TemplateNodeOutput output, Dictionary<string, Func<object?>> members, string? path = null)
            : base(subject, path ?? $"{TemplateObjects.Node}.{id}", members)
        {
            Id = id;
            Output = output;
        }

        public string Id { get; }

        public TemplateNodeOutput Output { get; }
    }

    private class LazyMembersObject : ClosedObject
    {
        private readonly string _subject;
        private readonly string _path;
        private readonly Dictionary<string, Func<object?>> _members;
        private readonly Dictionary<string, object?> _read = new(StringComparer.Ordinal);
        private readonly ISet<string>? _uncached;

        /// <param name="uncached">#1044: members produced on EVERY read (the producer records the read; its value is cached underneath).</param>
        public LazyMembersObject(string subject, string path, Dictionary<string, Func<object?>> members, ISet<string>? uncached = null)
            : base(subject, path)
        {
            _subject = subject;
            _path = path;
            _members = members;
            _uncached = uncached;
        }

        public override bool Contains(string member) => _members.ContainsKey(member);

        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (!_members.TryGetValue(member, out var produce))
            {
                throw new InvalidOperationException($"{_subject} reads {_path}.{member}, which this render has no value for.");
            }

            if (_uncached?.Contains(member) == true)
            {
                value = produce();
                return true;
            }

            if (!_read.TryGetValue(member, out value))
            {
                value = produce();
                _read[member] = value;
            }

            return true;
        }
    }
}
