using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the calls of a package (part 04 § 7.7; § 10.1's "Functions and calls" rows): each
/// <c>call</c> bound to the function it names, its <c>with</c> held to the function's parameters, each argument typed
/// against its parameter, and its <c>format</c> against the function's. A call's body is the reader's (S01-020, D2), and
/// a <c>with</c> value that is not text or <c>null</c> too (S04-017, A05-005, D13).
/// </summary>
public static partial class SourceResolve
{
    /// <summary>A call whose function exists, with its arguments (null for the literal <c>null</c>), by parameter id.</summary>
    private sealed record Bound(SourceLeaf Call, SourceFunction Function, IReadOnlyDictionary<string, Resolved?> Arguments);

    /// <summary>Every call, in source order: S04-064, then S04-065, S04-066, each argument's sentence, S04-070.</summary>
    private static List<Bound> Calls(ScopeTree tree, SourcePackage package, PackageTypes types, List<SourceRefusal> refusals)
    {
        var bound = new List<Bound>();
        foreach (var call in CallsIn(tree.All()))
        {
            var name = SourceLayout.Text(call.Header["function"]) ?? "";
            if (package.Functions.Items.FirstOrDefault(f => f.Id == name) is not { } function)
            {
                refusals.Add(ResolveSentences.UnknownFunction(call.Id, name, call.Path));
                continue;
            }

            bound.Add(new Bound(call, function, Bind(tree, types, call, function, refusals)));
        }

        return bound;
    }

    /// <summary>The <c>call</c> leaves among <paramref name="nodes"/>, in their order.</summary>
    private static IEnumerable<SourceLeaf> CallsIn(IEnumerable<SourceNode> nodes) =>
        nodes.OfType<SourceLeaf>().Where(n => n.Kind == "call");

    /// <summary>
    /// <paramref name="call"/>'s <c>with</c> against <paramref name="function"/>'s parameters (S04-065 for each one left
    /// out, S04-066 for each one it does not take; <c>&lt;params&gt;</c> in declared order, or <c>none</c>), each
    /// argument's own refusal or S04-068 (the literal <c>null</c> for a parameter that is not nullable; otherwise
    /// <see cref="FormatTypes.Fits"/>, in the caller's formats, where the argument's names mean what they say, the
    /// parameter's compared by name), and the call's <c>format</c> against the function's by
    /// <see cref="FormatTypes.Same"/> (S04-070).
    /// </summary>
    private static Dictionary<string, Resolved?> Bind(
        ScopeTree tree, PackageTypes types, SourceLeaf call, SourceFunction function, List<SourceRefusal> refusals)
    {
        var path = call.Path;
        var with = call.Header["with"] as JsonObject ?? new JsonObject();
        var parameters = function.Inputs.Count == 0 ? "none" : string.Join(", ", function.Inputs.Select(p => p.Id));
        foreach (var parameter in function.Inputs.Where(p => !with.ContainsKey(p.Id)))
            refusals.Add(ResolveSentences.ParameterNotPassed(call.Id, parameter.Id, function.Id, parameters, path));
        foreach (var (param, _) in with.Where(p => function.Inputs.All(i => i.Id != p.Key)))
            refusals.Add(ResolveSentences.ParameterNotTaken(call.Id, param, function.Id, parameters, path));

        var callerFormats = tree.FormatsOf(tree.FrameOf(call));
        var functionFormats = types.FunctionFormats.GetValueOrDefault(function.Id) ?? types.Formats;
        var arguments = new Dictionary<string, Resolved?>(StringComparer.Ordinal);
        foreach (var parameter in function.Inputs)
        {
            if (!with.TryGetPropertyValue(parameter.Id, out var value))
                continue;
            var expected = parameter.Json["format"];
            if (value is null)
            {
                arguments[parameter.Id] = null;
                if (!FormatTypes.IsNullable(expected, functionFormats))
                    refusals.Add(ResolveSentences.ArgumentDoesNotFit(call.Id, "null", "null", parameter.Id, function.Id, TypeStrings.Print(expected, functionFormats), path));
                continue;
            }

            if (SourceLayout.Text(value) is not { } text)
                continue;
            var argument = tree.Argument(call, parameter.Id, text);
            arguments[parameter.Id] = argument.Head;
            if (argument.Refusal is { } refusal)
                refusals.Add(refusal);
            else if (argument.Format is { } format && !FormatTypes.Fits(format, expected, callerFormats))
                refusals.Add(ResolveSentences.ArgumentDoesNotFit(call.Id, text, TypeStrings.Print(format, callerFormats), parameter.Id, function.Id, TypeStrings.Print(expected, functionFormats), path));
        }

        if (types.NodeFormats.GetValueOrDefault(call.Path) is { } declared && function.Index["format"] is { } returns
            && !FormatTypes.Same(declared, returns, callerFormats))
        {
            refusals.Add(ResolveSentences.CallFormatDisagrees(call.Id, TypeStrings.Print(declared, callerFormats), function.Id, TypeStrings.Print(returns, functionFormats), path));
        }

        return arguments;
    }
}
