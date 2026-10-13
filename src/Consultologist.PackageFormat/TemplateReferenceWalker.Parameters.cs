using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// The second edge source (package-format-v25-design.md §§ 2, 3.6): a node's
/// <c>with</c> supplies the parameters its format declares, each value an
/// object reference or a literal typed by the declaration. A <c>node</c>
/// parameter is a bare <c>node.&lt;id&gt;</c> (it names the node, so it is an
/// edge) whose format must match the declaration's when one is named; a
/// <c>text</c> parameter is a string literal or an <c>input.&lt;id&gt;</c>
/// reference to a text input; a <c>values</c> parameter is a list of
/// snake_case values or an <c>input.&lt;id&gt;</c> reference to an enum input.
/// A string that is exactly an object reference is read as one; anything
/// else is a literal.
/// </summary>
public static partial class TemplateReferenceWalker
{
    // object.id — the id in the manifest's own spelling (a node id may carry a
    // hyphen; `with` is JSON, not Scriban, so nothing needs escaping here).
    private static readonly Regex ObjectReference = new("^(?<object>[a-z]+)\\.(?<id>[a-z0-9][a-z0-9_-]*)(?<rest>(\\.[a-zA-Z0-9_]+)*)$", RegexOptions.Compiled);

    public static TemplateReferences WalkParameters(
        string subject,
        IReadOnlyDictionary<string, JsonNode?> with,
        IReadOnlyDictionary<string, WorkflowFormatParameterSpec> parameters,
        TemplateReferenceContext context)
    {
        var refs = new TemplateReferenceCollector();
        var errors = refs.Errors;
        var declared = string.Join(", ", parameters.Keys);

        foreach (var name in with.Keys.Where(n => !parameters.ContainsKey(n)))
        {
            errors.Add($"{subject} with '{name}' names no parameter of the format (parameters: {declared}).");
        }

        foreach (var name in parameters.Keys.Where(n => !with.ContainsKey(n)))
        {
            errors.Add($"{subject} with must supply parameter '{name}'.");
        }

        foreach (var (name, parameter) in parameters)
        {
            if (!with.TryGetValue(name, out var value))
            {
                continue;
            }

            var prefix = $"{subject} with '{name}'";

            switch (parameter.Kind)
            {
                case WorkflowFormatParameterKinds.Node:
                    WalkNodeParameter(prefix, value, parameter, context, refs);
                    break;
                case WorkflowFormatParameterKinds.Text:
                    WalkTextParameter(prefix, value, context, refs);
                    break;
                case WorkflowFormatParameterKinds.Values:
                    WalkValuesParameter(prefix, value, context, refs);
                    break;
                default:
                    errors.Add($"{prefix} has kind '{parameter.Kind}', which is not one of {string.Join(", ", WorkflowFormatParameterKinds.All)}.");
                    break;
            }
        }

        return refs.ToReferences();
    }

    private static void WalkNodeParameter(string prefix, JsonNode? value, WorkflowFormatParameterSpec parameter, TemplateReferenceContext context, TemplateReferenceCollector refs)
    {
        var text = AsString(value);
        var reference = text is null ? null : ObjectReference.Match(text);

        if (reference is not { Success: true } || reference.Groups["object"].Value != TemplateObjects.Node)
        {
            refs.Errors.Add($"{prefix} must be a bare node reference (node.<id>); got {Show(value)}.");
            return;
        }

        var id = reference.Groups["id"].Value;

        if (reference.Groups["rest"].Value.Length > 0)
        {
            refs.Errors.Add($"{prefix} must name the node bare (node.{id}), not {text}; the parameter reads the node's value.");
            return;
        }

        if (!context.Nodes.TryGetValue(id, out var shape))
        {
            refs.Errors.Add($"{prefix} names node '{id}', which the package does not declare.");
            return;
        }

        if (parameter.Format is not null && !string.Equals(shape.Format, parameter.Format, StringComparison.Ordinal))
        {
            refs.Errors.Add($"{prefix} names node '{id}' of format '{shape.Format}', but the parameter requires format '{parameter.Format}'.");
            return;
        }

        refs.ReadNode(id, text: false, value: true);
    }

    private static void WalkTextParameter(string prefix, JsonNode? value, TemplateReferenceContext context, TemplateReferenceCollector refs)
    {
        var text = AsString(value);

        if (text is null)
        {
            refs.Errors.Add($"{prefix} must be a text literal or an input.<id> reference to a text input; got {Show(value)}.");
            return;
        }

        if (!TryReadInputReference(prefix, text, context, refs, out var declaration))
        {
            return;
        }

        if (declaration is null)
        {
            return; // a literal
        }

        if (declaration.Type != WorkflowInputTypes.Text)
        {
            refs.Errors.Add($"{prefix} names input '{declaration.Id}', which is {declaration.Describe()}, not a text.");
            return;
        }

        refs.ReadInput(declaration.Id);
    }

    private static void WalkValuesParameter(string prefix, JsonNode? value, TemplateReferenceContext context, TemplateReferenceCollector refs)
    {
        if (value is JsonArray array)
        {
            var values = new List<string>();

            foreach (var entry in array)
            {
                if (entry is JsonValue v && v.TryGetValue(out string? s))
                {
                    values.Add(s);
                }
                else
                {
                    refs.Errors.Add($"{prefix} must be a list of snake_case values; {Show(entry)} is not a value.");
                    return;
                }
            }

            WorkflowPackageValidator.ValidateEnumValues(prefix, values, refs.Errors);
            return;
        }

        var text = AsString(value);

        if (text is null || !TryReadInputReference(prefix, text, context, refs, out var declaration))
        {
            if (text is not null && !ObjectReference.IsMatch(text))
            {
                refs.Errors.Add($"{prefix} must be a list of snake_case values or an input.<id> reference to an enum input; got {Show(value)}.");
            }
            else if (text is null)
            {
                refs.Errors.Add($"{prefix} must be a list of snake_case values or an input.<id> reference to an enum input; got {Show(value)}.");
            }

            return;
        }

        if (declaration is null)
        {
            refs.Errors.Add($"{prefix} must be a list of snake_case values or an input.<id> reference to an enum input; got {Show(value)}.");
            return;
        }

        if (declaration.Type != WorkflowInputTypes.Enum)
        {
            refs.Errors.Add($"{prefix} names input '{declaration.Id}', which is {declaration.Describe()}, not an enum.");
            return;
        }

        refs.ReadInput(declaration.Id);
    }

    /// <summary>
    /// False when the string is a reference that does not resolve (a sentence
    /// was added); true with a null declaration when it is a literal; true with
    /// the input's declaration when it is <c>input.&lt;id&gt;</c>.
    /// </summary>
    private static bool TryReadInputReference(string prefix, string text, TemplateReferenceContext context, TemplateReferenceCollector refs, out WorkflowDeclarationNode? declaration)
    {
        declaration = null;
        var reference = ObjectReference.Match(text);

        if (!reference.Success || !TemplateObjects.IsReserved(reference.Groups["object"].Value))
        {
            return true;
        }

        var obj = reference.Groups["object"].Value;
        var id = reference.Groups["id"].Value;

        if (obj != TemplateObjects.Input)
        {
            refs.Errors.Add($"{prefix} must be a literal or an input.<id> reference; got {text} (a {obj} is not a parameter value here).");
            return false;
        }

        if (reference.Groups["rest"].Value.Length > 0)
        {
            refs.Errors.Add($"{prefix} must name the input bare (input.{id}), not {text}.");
            return false;
        }

        if (!context.Inputs.TryGetValue(id, out declaration))
        {
            refs.Errors.Add($"{prefix} names input '{id}', which the package does not declare.");
            return false;
        }

        return true;
    }

    private static string? AsString(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static string Show(JsonNode? value) =>
        value is null ? "null" : value.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
