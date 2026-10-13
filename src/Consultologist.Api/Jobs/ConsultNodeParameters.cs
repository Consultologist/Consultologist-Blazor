using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B4 #1003, design § 3.6): a node's <c>with</c> resolved at job start
/// against the format's declared parameters — once, so every instance and
/// every replay sees the same operands. A <c>values</c> parameter becomes the
/// classifier's answer set (a list, or an enum input's declared values); a
/// <c>node</c> parameter the bare node id; a <c>text</c> parameter its
/// literal or the referenced input's effective value. The validator refused
/// anything else at publish, so an unexpected shape here is kept as its
/// literal rather than refused twice.
/// </summary>
internal static class ConsultNodeParameters
{
    private static readonly Regex Reference = new("^(?<object>node|input)\\.(?<id>[a-z][a-z0-9_]*)$", RegexOptions.Compiled);

    public sealed record Resolved(IReadOnlyDictionary<string, string>? With, IReadOnlyList<string>? Values);

    public static Resolved Resolve(
        WorkflowNodeSpec node,
        IReadOnlyDictionary<string, WorkflowFormatParameterSpec>? parameters,
        WorkflowPackageManifest? manifest,
        IReadOnlyDictionary<string, string>? effectiveInputs)
    {
        if (node.With is not { Count: > 0 } with || parameters is not { Count: > 0 })
        {
            return new Resolved(null, null);
        }

        var inputsById = (manifest?.Inputs ?? new List<WorkflowInputSpec>())
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<string>? values = null;

        foreach (var (name, value) in with)
        {
            if (!parameters.TryGetValue(name, out var parameter))
            {
                continue;
            }

            var text = value is JsonValue scalar && scalar.TryGetValue(out string? s) ? s : null;
            var reference = text is null ? null : Reference.Match(text);

            switch (parameter.Kind)
            {
                case WorkflowFormatParameterKinds.Node:
                    resolved[name] = reference is { Success: true } && reference.Groups["object"].Value == TemplateObjects.Node
                        ? reference.Groups["id"].Value
                        : text ?? value?.ToJsonString() ?? string.Empty;
                    break;

                case WorkflowFormatParameterKinds.Text:
                    resolved[name] = reference is { Success: true } && reference.Groups["object"].Value == TemplateObjects.Input
                        ? effectiveInputs?.GetValueOrDefault(reference.Groups["id"].Value) ?? string.Empty
                        : text ?? value?.ToJsonString() ?? string.Empty;
                    break;

                case WorkflowFormatParameterKinds.Values:
                    values = value is JsonArray array
                        ? array.Select(entry => entry is JsonValue v && v.TryGetValue(out string? member) ? member! : entry?.ToJsonString() ?? string.Empty).ToList()
                        : reference is { Success: true } && reference.Groups["object"].Value == TemplateObjects.Input
                            && inputsById.TryGetValue(reference.Groups["id"].Value, out var input)
                            ? input.Values?.ToList()
                            : null;
                    break;
            }
        }

        return new Resolved(resolved.Count > 0 ? resolved : null, values is { Count: > 0 } ? values : null);
    }
}
