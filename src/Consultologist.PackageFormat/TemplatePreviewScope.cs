using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1071: the scope a template is PREVIEWED over, at edit time, when no run
/// exists — every value a run would supply is a token naming its read
/// (<c>⟨input.notes⟩</c>, <c>⟨node.extract.content⟩</c>, <c>⟨profile.name⟩</c>),
/// while the package's own texts — snippets, folder items, macro texts and
/// instructions — render for real over the same scope. Rendering
/// <c>{{ macro.&lt;id&gt;.prompt }}</c> over it through <see cref="TemplateRenderer"/>
/// gives exactly what the engine composes for an inline macro: the instruction
/// rendered, a blank line, the mechanics sentence (<see cref="WorkflowMacroMechanics"/>)
/// — the editor's "what the model is told".
///
/// Not the publish desk's render probe (WorkflowPackageValidator.RenderProbeAt25),
/// which fills every value with the word "placeholder" to prove a template
/// renders strictly and reports only failures; this scope is for reading. A
/// structured value (an object or array input, an object or array format's
/// <c>.value</c>) is a JSON carrier whose leaves are tokens, so a path read
/// renders the path's token; a date, number or boolean input renders as its
/// token too (the renderer types a string that does not parse as the string).
/// </summary>
public static class TemplatePreviewScope
{
    /// <summary>The token for a read: <c>⟨path⟩</c>.</summary>
    public static string Token(string path) => $"⟨{path}⟩";

    /// <summary>
    /// Every renderer pipe: over a token operand (the node's text as this scope makes
    /// it) it renders a token naming the pipe and the operand's token; over real text
    /// (#1138: a preview pick put the sample concept list in the node's place) it renders
    /// the engine's bytes through the spec's own render function, with the concept source
    /// mapped as the engine maps it (<see cref="WorkflowNodeDefaults.WellKnownConceptSources"/>;
    /// the token keeps the raw node id, since it names a path).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>> Pipes =
        TemplatePipes.Specs.ToDictionary(
            spec => spec.Name,
            spec => (Func<TemplateNodeOutput, string>)(output =>
                output.Text.StartsWith('⟨')
                    ? Token($"{spec.Name} of {TemplateObjects.Node}.{output.ConceptSource}.{TemplateMembers.Content}")
                    : spec.Render(output.Text, WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(output.ConceptSource ?? "node", output.ConceptSource ?? "node"))),
            StringComparer.Ordinal);

    // Tokens stay readable inside a JSON carrier (no \u27E8 escapes).
    private static readonly JsonSerializerOptions CarrierJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The token scope of a package: its declarations, formats, snippets and macros, with every run value a token.</summary>
    public static TemplateRenderScope Tokens(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, WorkflowFormatCatalog? formats)
    {
        // Every declared input (WorkflowVariableDeclarations.For keeps only the
        // typed ones — the renderer's conversions; a text input needs none).
        var declared = (manifest.Inputs ?? new List<WorkflowInputSpec>()).DistinctBy(i => i.Id, StringComparer.Ordinal).ToList();
        var declarations = WorkflowVariableDeclarations.For(manifest);
        var inputTypes = declared.ToDictionary(input => input.Id, WorkflowInputTypes.Of, StringComparer.Ordinal);
        var inputs = declared.ToDictionary(
            input => input.Id,
            input => InputCarrier($"{TemplateObjects.Input}.{input.Id}", WorkflowDeclarationNode.Of(input)),
            StringComparer.Ordinal);

        var localSpecs = new Dictionary<string, WorkflowFormatSpec>(StringComparer.Ordinal);
        var discard = new List<string>();
        foreach (var format in manifest.Formats ?? new List<WorkflowFormatReference>())
        {
            if (format.File is not null && files.TryGetValue(format.File, out var json)
                && WorkflowFormatSpecJson.TryRead(json, $"Format '{format.Id}'", discard, out var spec))
            {
                localSpecs[format.Id] = spec!;
            }
        }

        WorkflowFormatSpec? SpecOf(string? formatId) =>
            formatId is null ? null
            : localSpecs.TryGetValue(formatId, out var local) ? local
            : formats is not null && formats.TryGet(formatId, out var catalogue) ? catalogue!.Spec
            : null;

        var nodes = new Dictionary<string, TemplateNodeOutput>(StringComparer.Ordinal);
        foreach (var node in (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal))
        {
            var path = $"{TemplateObjects.Node}.{node.Id}";
            var spec = node.Aggregate is { Count: > 0 } ? null : SpecOf(node.Format);
            var type = spec?.Type ?? WorkflowInputTypes.Text;
            // .content is its token; .value, on an object or array format, a carrier
            // with token leaves; an enum node's content IS its value, one token for both.
            var valueJson = type is WorkflowInputTypes.Object or WorkflowInputTypes.Array
                ? ShapeCarrier($"{path}.{TemplateMembers.Value}", TemplateValueShape.FromSchema(spec!.Schema)).ToJsonString(CarrierJson)
                : null;
            var text = Token($"{path}.{(type == WorkflowInputTypes.Enum ? TemplateMembers.Value : TemplateMembers.Content)}");
            nodes[node.Id] = new TemplateNodeOutput(text, type, node.Id, valueJson);
        }

        // #1105: at 26 the texts carry headers — the scope reads bodies and the
        // header's label (the renderer's own 26 members are #1157's).
        var texts = manifest.SpecVersion >= 26 ? WorkflowTextHeaders.Resolve(manifest, files, discard) : null;
        if (texts is not null)
        {
            manifest = texts.Manifest;
            files = texts.Files;
        }
        var content = texts is null
            ? WorkflowDataResolver.ResolveContent(manifest, files, discard)
            : WorkflowDataResolver.ResolveContent(manifest, texts, discard);
        var folders = content.Collections.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<TemplateFolderItem>)entry.Value.Items
                .Select(item => new TemplateFolderItem(
                    item.Id,
                    item.Fields.GetValueOrDefault(TemplateMembers.Name) ?? item.Fields.GetValueOrDefault(TemplateMembers.Label, item.Id),
                    item.Fields.GetValueOrDefault(TemplateMembers.Content, string.Empty)))
                .ToList(),
            StringComparer.Ordinal);

        // #1099: a macro folder's items at 26, each with its header's label and instruction.
        var macroFolders = texts is null ? null : WorkflowTextHeaders.ResolveMacroFolders(manifest, texts, discard)?.Collections.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<TemplateMacroSource>)entry.Value.Items
                .Select(item => new TemplateMacroSource(item.Id, item.Header?.Label ?? item.Id, item.Fields.GetValueOrDefault(TemplateMembers.Content, string.Empty), item.Header?.Instruction ?? string.Empty))
                .ToList(),
            StringComparer.Ordinal);

        var macros = (manifest.Macros ?? new List<WorkflowMacroSpec>())
            .Where(macro => macro.File is not null && !macro.File.EndsWith('/'))
            .DistinctBy(macro => macro.Id, StringComparer.Ordinal)
            .ToDictionary(
                macro => macro.Id,
                macro => new TemplateMacroSource(macro.Id, macro.Label ?? macro.Id, files.GetValueOrDefault(macro.File!, string.Empty), macro.Instruction ?? string.Empty),
                StringComparer.Ordinal);

        return new TemplateRenderScope(
            inputs,
            inputTypes,
            declarations,
            nodes,
            content.Scalars,
            folders,
            // #1207: at 26 the fan item's name member is label — the token says so.
            new TemplateFanItem(Token($"{TemplateObjects.Item}.{TemplateMembers.Id}"), Token($"{TemplateObjects.Item}.{(manifest.SpecVersion >= 26 ? TemplateMembers.Label : TemplateMembers.Name)}"), Text: Token($"{TemplateObjects.Item}.{TemplateMembers.Content}")),
            macros,
            new TemplateRunFacts(Token("run.date"), Token("run.job"), Token("run.package"), Token("run.host")),
            new TemplateProfileFacts(Token("profile.name"), Token("profile.signature")),
            MacroFolders: macroFolders,
            // #1207 (v26 design § 5): the preview renders the 26 members — item.label, a folder
            // item's .label, a macro's .label/.instruction — as the engine's render does, and a
            // standalone snippet's label is its header's (the #1179 slice the preview lacked).
            Labelled: manifest.SpecVersion >= 26,
            SnippetLabels: texts is null
                ? null
                : (manifest.Snippets ?? new Dictionary<string, string>())
                    .Where(entry => !entry.Value.EndsWith('/') && texts.Headers.GetValueOrDefault(entry.Value)?.Label is not null)
                    .ToDictionary(entry => entry.Key, entry => texts.Headers[entry.Value].Label!, StringComparer.Ordinal));
    }

    /// <summary>What the model is told where <c>macro.&lt;id&gt;.prompt</c> is read — the instruction rendered over the scope, then the mechanics sentence; the renderer's own sentence when the instruction does not render.</summary>
    public static string MacroPrompt(string macroId, TemplateRenderScope scope) =>
        TemplateRenderer.Render($"Macro '{macroId}' prompt", "{{ macro." + macroId + "." + TemplateMembers.Prompt + " }}", scope, Pipes);

    // A scalar input is its token; a structured one a carrier whose leaves are tokens.
    private static string InputCarrier(string path, WorkflowDeclarationNode node) =>
        node.Type is WorkflowInputTypes.Object or WorkflowInputTypes.Array
            ? DeclarationCarrier(path, node).ToJsonString(CarrierJson)
            : Token(path);

    private static JsonNode DeclarationCarrier(string path, WorkflowDeclarationNode node) => node.Type switch
    {
        WorkflowInputTypes.Object => new JsonObject((node.Fields ?? new List<WorkflowDeclarationNode>())
            .Select(field => new KeyValuePair<string, JsonNode?>(field.Id, DeclarationCarrier($"{path}.{field.Id}", field)))),
        WorkflowInputTypes.Array => new JsonArray(node.Items is null ? JsonValue.Create(Token($"{path}[0]")) : DeclarationCarrier($"{path}[0]", node.Items)),
        _ => JsonValue.Create(Token(path)),
    };

    private static JsonNode ShapeCarrier(string path, TemplateValueShape shape) =>
        shape.IsObject
            ? new JsonObject((shape.Fields ?? new Dictionary<string, TemplateValueShape>())
                .Select(field => new KeyValuePair<string, JsonNode?>(field.Key, ShapeCarrier($"{path}.{field.Key}", field.Value))))
            : shape.IsArray
                ? new JsonArray(shape.Items is null ? JsonValue.Create(Token($"{path}[0]")) : ShapeCarrier($"{path}[0]", shape.Items))
                : JsonValue.Create(Token(path));
}
