using System.Globalization;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 5.2, 5.3, 5.5; part 08 § 4.2; D6): a pipe into a renderer. The renderer
/// (the function's own, else the package's, where publish embedded the built-ins it uses) renders its body with the
/// piped value as <c>value</c> and nothing else, in the composition's budget; each step gives one
/// <see cref="ComposedRender"/>, numbered by the step's place (<see cref="BodyComposer.Compose"/>), so a step inside a
/// <c>for</c> gives one per iteration with the same number.
/// </summary>
public static partial class BodyComposer
{
    internal sealed partial class Composing
    {
        private readonly Dictionary<string, TextPlan?> renderers = new(StringComparer.Ordinal);

        /// <summary>The renderer <paramref name="key"/> (<c>renderer.&lt;id&gt;</c>): the function's own, else the package's.</summary>
        public JsonObject? Renderer(string key) =>
            (function?.Json["renderers"] as JsonObject)?[key] as JsonObject ?? (RunPlan.Document["renderers"] as JsonObject)?[key] as JsonObject;

        /// <summary>
        /// Runs the pipe step <paramref name="step"/> into renderer <paramref name="id"/> on <paramref name="value"/> and
        /// records its sub-row. A renderer the plan lacks, or whose body is not a dialect template, is not what publish wrote
        /// (S07-006).
        /// </summary>
        public string RunStep(RenderStep step, string id, object? value)
        {
            var key = $"{DialectRules.RendererRoot}.{id}";
            var renderer = Renderer(key);
            if (!renderers.TryGetValue(key, out var plan))
            {
                plan = SourceLayout.Text(renderer?["body"]) is { } body ? RendererPlan(body, key) : null;
                renderers[key] = plan;
            }

            if (renderer == null || plan == null)
                throw Stopped(new ComposeStop(NotPublished));

            var globals = new ScriptObject();
            globals.SetValue("value", value, false);
            var pass = Pass();
            var output = Render(plan, globals);
            if (InNeutralPass)
                return output; // a text spot's pipeline over the run-neutral text: no sub-row of the record
            var input = value is string piped ? piped : CanonicalJson.Write(TemplateValues.ToJson(value));
            RenderTexts.Add(new RenderedText(input, output));
            Renders.Add(new ComposedRender(
                step.N,
                key,
                SourceLayout.Text(renderer["bodyHash"]) ?? string.Empty,
                step.Source,
                value is string text ? CanonicalJson.HashText(text) : CanonicalJson.Hash(TemplateValues.ToJson(value)),
                InputSize(value, renderer["input"]),
                CanonicalJson.HashText(output),
                output.EnumerateRunes().Count(),
                step.RunDependent,
                pass));
            return output;
        }

        /// <summary>Part 08 § 4.2's size summary: <c>"&lt;n&gt; concepts"</c> for a <c>concept_list</c>, <c>"&lt;n&gt; items"</c> for a list, else <c>null</c>.</summary>
        private static string? InputSize(object? value, JsonNode? input)
        {
            if (value is ScriptArray list)
                return string.Create(CultureInfo.InvariantCulture, $"{list.Count} items");
            if (value is DataObject { Json: var json } && SourceLayout.Text(input?["$ref"]) == "#/$defs/concept_list" && json["concepts"] is JsonArray concepts)
                return string.Create(CultureInfo.InvariantCulture, $"{concepts.Count} concepts");
            return null;
        }
    }

    /// <summary><c>renderer</c>: each renderer the entry can pipe into, as the step function a pipe calls. A renderer the plan lacks fails the render.</summary>
    internal sealed class RendererRoot(Composing composing) : ScriptObject
    {
        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (composing.Renderer($"{DialectRules.RendererRoot}.{member}") == null)
                throw composing.Stopped(new ComposeStop(null));
            value = new RendererStep(composing, member);
            return true;
        }
    }

    /// <summary>
    /// One renderer as a pipe calls it: the step is the one planned at the call's offset in the text being rendered (a
    /// call anywhere else, as a call form or inside a test, is no step publish numbered and fails the render).
    /// </summary>
    internal sealed class RendererStep(Composing composing, string id) : IScriptCustomFunction
    {
        public int RequiredParameterCount => 1;

        public int ParameterCount => 1;

        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;

        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), "value");

        public object? Invoke(TemplateContext context, ScriptNode? callerContext, ScriptArray arguments, ScriptBlockStatement? blockStatement)
        {
            if (context is not ComposeContext composer || callerContext == null || arguments.Count != 1
                || !composer.Text.Renders.TryGetValue(Offset(callerContext), out var step) || step.Renderer != id)
            {
                throw composing.Stopped(new ComposeStop(null));
            }

            return composing.RunStep(step, id, arguments[0]);
        }

        public ValueTask<object?> InvokeAsync(TemplateContext context, ScriptNode? callerContext, ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));
    }
}
