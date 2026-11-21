using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 5.1 to 5.4; <c>scriban-dialect.md</c> § 6): renderers. A renderer's
/// <c>format</c> is a text format (S05-028) and its body pipes into no renderer (S05-033; that it reads only
/// <c>value</c>, S05-029, is <see cref="PathTyping"/>'s). At a use, every pipe step into a renderer is one
/// <see cref="TemplateRender"/> in source order: the renderer is the function's own, the package's or a built-in
/// (S05-031); the value piped is of its <c>input</c> by type equality (part 02 § 2.3; S05-026: a literal head is typed
/// by its built-in type, and a head no format says is of no renderer's <c>input</c>), and in a chain each step's
/// <c>format</c> is the next one's <c>input</c> (S05-034); a chain whose final value is a <c>markdown_block</c>
/// renderer's, written in a tag with text on its line, warns (S05-061).
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>Part 05 § 5.1: what a renderer returns.</summary>
    private static readonly IReadOnlySet<string> TextFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "plaintext", "markdown_block", "markdown_inline",
    };

    private sealed partial class BodyCheck
    {
        /// <summary>S05-028 for a renderer whose <c>format</c> is not one of the three text formats; nothing for other bodies.</summary>
        public IEnumerable<SourceRefusal> Header()
        {
            if (host is { Site: TemplateSite.Renderer, Declaration: SourceText renderer }
                && !(FormatSet.NameOf(renderer.Header["format"]) is { } name && TextFormats.Contains(name)))
            {
                yield return DialectSentences.RendererFormatNotText(renderer.Id, TypeStrings.Print(renderer.Header["format"], formats), host.Path);
            }
        }

        /// <summary>
        /// The pipe steps of <paramref name="output"/>, whose piped head was typed as <paramref name="head"/> (<c>null</c>
        /// when it is not one path or was not typed): in a renderer's body any renderer is S05-033; elsewhere a renderer
        /// given arguments is S05-030, and every other renderer step is typed and recorded.
        /// </summary>
        private void Pipes(TemplateOutput output, TypedRead? head)
        {
            if (host.Site == TemplateSite.Renderer)
            {
                RendererPipes(output.Pipes);
                return;
            }

            // The head as read: its path, else its literal or test as written (typed by its built-in type), else a
            // value no format says ([1, 2], an interpolated string), which no renderer's input is.
            var source = output.Path ?? output.Head;
            var written = source;
            var current = output.Path is null && output.HeadType is { } type ? new JsonObject { ["type"] = type } : head?.Format;
            var unknown = output.Path is null && output.HeadType is null;
            string? adjacent = null;
            int? last = null;
            (string Id, int Line)? final = null;
            foreach (var pipe in output.Pipes)
            {
                if (RendererOf(pipe) is not { } id)
                {
                    current = BuiltInResult(pipe.Name, current);
                    written += " | " + (pipe.Arguments.Count == 0 ? pipe.Name : pipe.Name + " " + string.Join(' ', pipe.Arguments));
                    adjacent = null;
                    continue;
                }

                if (pipe.Arguments.Count > 0)
                {
                    Refusals.Add(DialectSentences.CallsRenderer(host.Subject, id, source, pipe.Line, host.Path));
                    return;
                }

                if (Find(id) is not { } header)
                {
                    Refusals.Add(DialectSentences.PipesUndefinedRenderer(host.Subject, id, host.Path));
                    return;
                }

                var n = renders.Count + 1;
                renders.Add(new TemplateRender(n, last is { } before ? $"render:{before}" : source, id));

                var input = header["input"];
                if (unknown)
                {
                    Refusals.Add(DialectSentences.PipesWrongFormat(host.Subject, written, "?", id, TypeStrings.Print(input, formats), host.Path));
                    return;
                }

                if (current != null && FormatTypes.Shape(input, formats) != FormatShape.Unknown && !FormatTypes.Same(current, input, formats))
                {
                    var (shown, wanted) = (TypeStrings.Print(current, formats), TypeStrings.Print(input, formats));
                    Refusals.Add(adjacent is { } previous
                        ? DialectSentences.ChainWrongFormat(host.Subject, previous, shown, id, wanted, host.Path)
                        : DialectSentences.PipesWrongFormat(host.Subject, written, shown, id, wanted, host.Path));
                }

                current = header["format"];
                written = $"renderer.{id}";
                adjacent = id;
                last = n;
                final = (id, pipe.Line);
            }

            // Part 05 § 5.4: what the tag writes is the chain's final value; when that is a markdown_block renderer's
            // text (a built-in step after it keeps it) and the tag shares its line, it warns, naming that renderer.
            if (final is { } placed && current != null && FormatSet.NameOf(current) == "markdown_block" && (output.Tag.TextBefore || output.Tag.TextAfter))
                Warnings.Add(DialectSentences.BlockRendererInsideALine(host.Subject, placed.Id, placed.Line, host.Path));
        }

        /// <summary>S05-033 for each renderer among <paramref name="pipes"/>, in a renderer's body (part 05 § 5.1, R-N7).</summary>
        private void RendererPipes(IEnumerable<TemplatePipe> pipes)
        {
            var self = host.Declaration is SourceText renderer ? renderer.Id : host.Subject;
            foreach (var pipe in pipes)
            {
                if (RendererOf(pipe) is { } other)
                    Refusals.Add(DialectSentences.RendererPipesIntoRenderer(self, other, pipe.Line, host.Path));
            }
        }

        /// <summary>S05-033 for a renderer piped in a test of a renderer's own body (any test there is allowed, R2, but not a renderer).</summary>
        private void RendererPipesInTests()
        {
            if (host.Site != TemplateSite.Renderer)
                return;
            foreach (var chain in syntax.Ifs)
            {
                foreach (var test in chain.Tests)
                    RendererPipes(test.Pipes);
            }
        }

        /// <summary>The renderer id a pipe step names (<c>renderer.&lt;id&gt;</c>), or <c>null</c> for a built-in step.</summary>
        private static string? RendererOf(TemplatePipe pipe) =>
            pipe.Name.StartsWith(DialectRules.RendererRoot + ".", StringComparison.Ordinal) ? pipe.Name[(DialectRules.RendererRoot.Length + 1)..] : null;

        /// <summary>Renderer <paramref name="id"/>'s header: the function's own, else the package's, else a built-in's (part 05 § 5.3); <c>null</c> when none is.</summary>
        private JsonObject? Find(string id) =>
            host.Function?.Definitions.Renderers.Items.FirstOrDefault(r => r.Id == id)?.Header
            ?? package.Definitions.Renderers.Items.FirstOrDefault(r => r.Id == id)?.Header
            ?? registry.BuiltInRenderers.GetValueOrDefault(id)?.Header;

        /// <summary>
        /// What a built-in step gives for a value of <paramref name="format"/>: <c>array.size</c> an integer;
        /// <c>array.join</c> and <c>date.to_string</c> a string; the four <c>string.*</c> the text format or string they are
        /// given, else a string. <c>null</c> when it cannot be said (an untyped value, a step the dialect already refused).
        /// </summary>
        private JsonNode? BuiltInResult(string name, JsonNode? format) => name switch
        {
            "array.size" => new JsonObject { ["type"] = "integer" },
            "array.join" or "date.to_string" => JsonValue.Create("string"),
            "string.upcase" or "string.downcase" or "string.capitalize" or "string.strip" => format is null
                ? null
                : FormatTypes.Shape(format, formats) is FormatShape.Text or FormatShape.String ? format : JsonValue.Create("string"),
            _ => null,
        };
    }
}
