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
/// The fan item an instance runs for: a snippet fan carries <c>Text</c> (a
/// template, rendered when read); an input fan carries <c>Value</c> (the
/// element's canonical form or carrier JSON) typed by <c>Element</c>.
/// </summary>
public sealed record TemplateFanItem(string Id, string Name, string? Text = null, string? Value = null, WorkflowDeclarationNode? Element = null);

/// <summary>A macro as the template reads it: its text (a template, rendered when read) and its instruction (the <c>.prompt</c> half the mechanics sentence completes).</summary>
public sealed record TemplateMacroSource(string Id, string Name, string Text, string Instruction);

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
    IReadOnlySet<string>? InertSnippets = null);

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
        foreach (var (id, output) in scope.Nodes)
        {
            node[id] = new LazyMembersObject(subject, $"{TemplateObjects.Node}.{id}",
                new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
                {
                    [TemplateMembers.Content] = () => output.Text,
                    [TemplateMembers.Value] = () => NodeValue(subject, id, output),
                });
        }

        globals[TemplateObjects.Node] = node;

        var snippet = new ClosedObject(subject, TemplateObjects.Snippet);
        foreach (var (id, text) in scope.Snippets)
        {
            // #1094: an inert snippet's content is empty where read; its id as ever.
            var inert = scope.InertSnippets?.Contains(id) == true;
            snippet[id] = Rendered(subject, $"{TemplateObjects.Snippet}.{id}", id, id, inert ? string.Empty : text, $"Snippet '{id}'", globals, trace);
        }

        foreach (var (folder, items) in scope.Folders)
        {
            var array = new ScriptArray();
            foreach (var item in items)
            {
                array.Add(Rendered(subject, $"{TemplateObjects.Snippet}.{folder}[{item.Id}]", item.Id, item.Name, item.Text, $"Snippet '{folder}/{item.Id}'", globals, trace));
            }

            snippet[folder] = array;
        }

        globals[TemplateObjects.Snippet] = snippet;

        if (scope.Item is { } fanItem)
        {
            globals[TemplateObjects.Item] = fanItem.Text is not null
                ? Rendered(subject, TemplateObjects.Item, fanItem.Id, fanItem.Name, fanItem.Text, $"{subject} item '{fanItem.Id}'", globals, trace)
                : new ClosedObject(subject, TemplateObjects.Item)
                {
                    [TemplateMembers.Id] = fanItem.Id,
                    [TemplateMembers.Name] = fanItem.Name,
                    [TemplateMembers.Value] = PromptTemplateRenderer.MaterialiseElement(fanItem.Value ?? string.Empty, fanItem.Element),
                };
        }

        var macro = new ClosedObject(subject, TemplateObjects.Macro);
        foreach (var (id, source) in scope.Macros)
        {
            var members = new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
            {
                [TemplateMembers.Id] = () => source.Id,
                [TemplateMembers.Name] = () => source.Name,
                // #1044: the instruction is a template, rendered over the same
                // scope FIRST; the mechanics sentence — carrying the literal read
                // the model is to write — is appended after, never rendered.
                [TemplateMembers.Prompt] = () =>
                {
                    // #1089 (v26 design § 2.3): an inline read the running node's
                    // instance gate suppresses renders empty — no instruction, no
                    // mechanics sentence, no read recorded (the model is told nothing).
                    if (scope.SuppressedPrompts?.Contains(source.Id) == true || scope.InertMacros?.Contains(source.Id) == true)
                    {
                        return string.Empty;
                    }

                    trace?.PromptReads.Add(source.Id);
                    return WorkflowMacroMechanics.Prompt(source.Id, RenderWith($"Macro '{id}' instruction", source.Instruction, globals, trace, nested: true));
                },
            };
            var text = new Lazy<string>(() => RenderWith($"Macro '{id}'", source.Text, globals, trace, nested: true));
            members[TemplateMembers.Content] = () =>
            {
                // #1090: an inert macro's content is empty where read — no blank
                // block, no read recorded (a placed instance composes nothing).
                if (scope.InertMacros?.Contains(source.Id) == true)
                {
                    return string.Empty;
                }

                trace?.ContentRead(source.Id);
                return text.Value;
            };
            macro[id] = new LazyMembersObject(subject, $"{TemplateObjects.Macro}.{id}", members, UncachedMacroMembers);
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

        foreach (var name in TemplatePipes.All)
        {
            var pipe = pipes is not null && pipes.TryGetValue(name, out var found) ? found : null;
            globals.Import(name, (Func<object?, string>)(argument => ApplyPipe(subject, name, pipe, argument, scope.Nodes)));
        }

        return globals;
    }

    /// <summary>
    /// A pipe renders a node's text and nothing else (design § 1): the argument
    /// must be the very string a <c>node.&lt;id&gt;.content</c> read produced, so
    /// the pipe receives the node's output whole — its concept source with it.
    /// </summary>
    private static string ApplyPipe(string subject, string name, Func<TemplateNodeOutput, string>? pipe, object? argument, IReadOnlyDictionary<string, TemplateNodeOutput> nodes)
    {
        if (pipe is null)
        {
            throw new InvalidOperationException($"{subject} pipes through {name}, which this renderer does not provide.");
        }

        if (argument is not string text)
        {
            throw new InvalidOperationException($"{subject} pipes a {Describe(argument)} through {name}; the pipe renders a node's text (node.<id>.content).");
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

    private static LazyMembersObject Rendered(string subject, string path, string id, string name, string text, string templateSubject, ScriptObject globals, TemplateRenderTrace? trace)
    {
        var rendered = new Lazy<string>(() => RenderWith(templateSubject, text, globals, trace, nested: true));
        return new LazyMembersObject(subject, path, new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
        {
            [TemplateMembers.Id] = () => id,
            [TemplateMembers.Name] = () => name,
            [TemplateMembers.Content] = () => rendered.Value,
        });
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
    private sealed class LazyMembersObject : ClosedObject
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
