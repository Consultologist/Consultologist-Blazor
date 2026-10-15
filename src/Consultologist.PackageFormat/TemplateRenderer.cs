using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>The renderers a 25 template applies as pipes (design § 1: "renderers are pipes"; the closed set bindings' <c>as:</c> had).</summary>
public static class TemplatePipes
{
    /// <summary>The attested, byte-stable context rendering <c>as: concept-context</c> gave.</summary>
    public const string ConceptContext = "concept_context";

    /// <summary>The bullet rendering <c>as: concept-bullets</c> gave.</summary>
    public const string ConceptBullets = "concept_bullets";

    public static readonly IReadOnlyList<string> All = new[] { ConceptContext, ConceptBullets };
}

/// <summary>
/// An upstream node's output as the template reads it: its composed text, the
/// format type that says what <c>.value</c> parses to, and the concept source
/// the engine stamps when a pipe renders its concepts (null when none).
/// </summary>
public sealed record TemplateNodeOutput(string Text, string Type, string? ConceptSource = null);

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
    TemplateProfileFacts Profile);

/// <summary>
/// The specVersion 25 render (design §§ 1, 4, 5.3; B3 #1002): one Scriban
/// render of a template over the seven reserved objects with closed members —
/// <c>input</c>, <c>node</c>, <c>snippet</c>, <c>item</c>, <c>macro</c>,
/// <c>run</c>, <c>profile</c> — and the renderer pipes. Snippets, folder items
/// and macro texts are templates themselves: their <c>.text</c> renders over
/// the same scope the first time it is read and is kept for the render (the
/// validator refused cycles at publish). <c>macro.&lt;id&gt;.prompt</c> is
/// the instruction composed with <see cref="WorkflowMacroMechanics"/>.
/// <c>node.&lt;id&gt;.value</c> is the output parsed as JSON on a structured
/// format, the text itself otherwise. Strict: an undeclared member or an
/// unknown pipe fails the render with the subject in the sentence.
/// </summary>
public static class TemplateRenderer
{
    /// <param name="promptReads">B5 #1004 (design § 7): when given, every
    /// <c>macro.&lt;id&gt;.prompt</c> this render evaluated is added — the slots
    /// the prompt that ran sanctions, exactly the instructions the model saw
    /// (a read behind a branch not taken is not one of them).</param>
    public static string Render(
        string subject,
        string templateText,
        TemplateRenderScope scope,
        IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null,
        ISet<string>? promptReads = null)
    {
        var globals = Globals(subject, scope, pipes, promptReads);
        return RenderWith(subject, templateText, globals);
    }

    /// <summary>The globals a probe or a renderer pushes: the seven objects and the pipes, over this scope.</summary>
    internal static ScriptObject Globals(string subject, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes, ISet<string>? promptReads = null)
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
                    [TemplateMembers.Text] = () => output.Text,
                    [TemplateMembers.Value] = () => NodeValue(subject, id, output),
                });
        }

        globals[TemplateObjects.Node] = node;

        var snippet = new ClosedObject(subject, TemplateObjects.Snippet);
        foreach (var (id, text) in scope.Snippets)
        {
            snippet[id] = Rendered(subject, $"{TemplateObjects.Snippet}.{id}", id, id, text, $"Snippet '{id}'", globals);
        }

        foreach (var (folder, items) in scope.Folders)
        {
            var array = new ScriptArray();
            foreach (var item in items)
            {
                array.Add(Rendered(subject, $"{TemplateObjects.Snippet}.{folder}[{item.Id}]", item.Id, item.Name, item.Text, $"Snippet '{folder}/{item.Id}'", globals));
            }

            snippet[folder] = array;
        }

        globals[TemplateObjects.Snippet] = snippet;

        if (scope.Item is { } fanItem)
        {
            globals[TemplateObjects.Item] = fanItem.Text is not null
                ? Rendered(subject, TemplateObjects.Item, fanItem.Id, fanItem.Name, fanItem.Text, $"{subject} item '{fanItem.Id}'", globals)
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
                [TemplateMembers.Prompt] = () =>
                {
                    promptReads?.Add(source.Id);
                    return WorkflowMacroMechanics.Prompt(source.Id, source.Instruction);
                },
            };
            var text = new Lazy<string>(() => RenderWith($"Macro '{id}'", source.Text, globals));
            members[TemplateMembers.Text] = () => text.Value;
            macro[id] = new LazyMembersObject(subject, $"{TemplateObjects.Macro}.{id}", members);
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
    /// must be the very string a <c>node.&lt;id&gt;.text</c> read produced, so
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
            throw new InvalidOperationException($"{subject} pipes a {Describe(argument)} through {name}; the pipe renders a node's text (node.<id>.text).");
        }

        var output = nodes.Values.FirstOrDefault(node => ReferenceEquals(node.Text, text));

        if (output is null)
        {
            throw new InvalidOperationException($"{subject} pipes text that is not a node's output through {name}; the pipe renders a node's text (node.<id>.text).");
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

    private static LazyMembersObject Rendered(string subject, string path, string id, string name, string text, string templateSubject, ScriptObject globals)
    {
        var rendered = new Lazy<string>(() => RenderWith(templateSubject, text, globals));
        return new LazyMembersObject(subject, path, new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
        {
            [TemplateMembers.Id] = () => id,
            [TemplateMembers.Name] = () => name,
            [TemplateMembers.Text] = () => rendered.Value,
        });
    }

    private static string RenderWith(string subject, string templateText, ScriptObject globals)
    {
        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            throw new InvalidOperationException(
                $"{subject} does not parse: {string.Join(" ", template.Messages.Select(m => m.Message))}");
        }

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
            parsed = JsonNode.Parse(output.Text);
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

        public LazyMembersObject(string subject, string path, Dictionary<string, Func<object?>> members)
            : base(subject, path)
        {
            _subject = subject;
            _path = path;
            _members = members;
        }

        public override bool Contains(string member) => _members.ContainsKey(member);

        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (!_members.TryGetValue(member, out var produce))
            {
                throw new InvalidOperationException($"{_subject} reads {_path}.{member}, which this render has no value for.");
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
