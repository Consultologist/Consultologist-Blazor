using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>requires</c> (part 07 § 3.1; D8): derived, never authored, and minimal. The spec;
/// the evaluator's range (<c>^</c> and <see cref="JsonLogicOperations.EvaluatorVersion"/>); Scriban's (<c>^</c> and the
/// locked version); each custom operation any rule of the package calls, with its range; the node kinds used and the
/// features used, each in <c>constants.json</c>'s order; and each agent the bindings name, with its version.
/// #1504 (specVersion 31, area 07; ruling 4, R6): with its version as the integer the bindings carry (<c>{ "judgement": 1 }</c>);
/// the type rules hold a package to one version of each agent (A07-014), so each name has one.
/// </summary>
public static partial class ResolveCompiler
{
    /// <summary>The node kinds in <c>constants.json</c>'s order (<c>kinds</c>).</summary>
    private static readonly string[] KindOrder = ["agent", "rendered", "computed", "for", "while", "if", "case", "call"];

    /// <summary>The features in <c>constants.json</c>'s order (<c>features</c>).</summary>
    private static readonly string[] FeatureOrder = ["parallel", "macro-markers", "parts", "run-spots", "check-loops"];

    private static JsonObject Requires(Context context, string scribanVersion)
    {
        var package = context.Package;
        var all = context.Tree.All().ToList();
        var checks = ScopeTree.Walk(package.Checks.Nodes);

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rules(context))
            Operations(rule, used, 0);
        var operations = new JsonObject();
        foreach (var operation in JsonLogicOperations.All.Where(o => o.Custom && used.Contains(o.Name)))
            operations[operation.Name] = $"^{JsonLogicOperations.OperationVersion}";

        var kinds = all.Select(n => n.Kind).ToHashSet(StringComparer.Ordinal);
        var bodies = AllEntries(context.Derived).Select(e => e.Body).OfType<TemplateReads>().ToList();
        var agentBodies = AgentEntries(context).Select(e => e.Body).OfType<TemplateReads>().ToList();
        var features = new HashSet<string>(StringComparer.Ordinal);
        if (all.OfType<SourceFor>().Any(l => l.Parallel))
            features.Add("parallel");
        if (agentBodies.Any(b => b.Places.Count > 0))
            features.Add("macro-markers");
        if (all.OfType<SourceLeaf>().Any(l => l.Kind == "agent" && l.Header["parts"] is JsonObject { Count: > 0 }))
            features.Add("parts");
        if (bodies.Any(b => b.RunSpots.Count > 0))
            features.Add("run-spots");
        if (checks.OfType<SourceFor>().Any())
            features.Add("check-loops");

        var agents = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, producers) in context.Formats.Bindings)
        {
            foreach (var (_, binding) in producers?.AsObject() ?? [])
            {
                if (SourceLayout.Text(binding?["agentName"]) is { } name && binding?["agentVersion"] is JsonValue version
                    && version.TryGetValue<int>(out var number))
                {
                    agents.TryAdd(name, number);
                }
            }
        }

        var agentsObject = new JsonObject();
        foreach (var (name, version) in agents)
            agentsObject[name] = version;

        return new JsonObject
        {
            ["spec"] = SpecVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["evaluator"] = $"^{JsonLogicOperations.EvaluatorVersion}",
            ["scriban"] = $"^{scribanVersion}",
            ["operations"] = operations,
            ["kinds"] = Strings(KindOrder.Where(kinds.Contains)),
            ["features"] = Strings(FeatureOrder.Where(features.Contains)),
            ["agents"] = agentsObject,
        };
    }

    /// <summary>Every derived entry, the package's and each function's.</summary>
    private static IEnumerable<DerivedEntry> AllEntries(PackageDerived derived) =>
        derived.Entries.Values.Concat(derived.FunctionEntries.Values.SelectMany(f => f.Values));

    /// <summary>The derived entries of every agent node, the package's and each function's.</summary>
    private static IEnumerable<DerivedEntry> AgentEntries(Context context)
    {
        foreach (var node in context.Tree.All().OfType<SourceLeaf>().Where(l => l.Kind == "agent"))
        {
            if (context.Derive(node.Key, context.Tree.FrameOf(node).Function) is { } entry)
                yield return entry;
        }
    }

    /// <summary>Every JSON Logic rule of the package: conditions, expressions, parts' conditions, definitions' and deliverables' conditions.</summary>
    private static IEnumerable<JsonNode?> Rules(Context context)
    {
        foreach (var node in context.Tree.All())
        {
            switch (node)
            {
                case SourceLeaf leaf:
                    yield return leaf.Header["if"];
                    yield return leaf.Header["expression"];
                    if (leaf.Header["parts"] is JsonObject parts)
                    {
                        foreach (var (_, part) in parts)
                            yield return (part as JsonObject)?["if"];
                    }

                    break;
                case SourceFor loop:
                    yield return loop.Until;
                    break;
                case SourceWhile loop:
                    yield return loop.While;
                    yield return loop.Until;
                    break;
                case SourceIf branch:
                    yield return branch.If;
                    break;
            }
        }

        var definitions = new[] { context.Package.Definitions }.Concat(context.Package.Functions.Items.Select(f => f.Definitions));
        foreach (var set in definitions)
        {
            foreach (var entry in set.Snippets.Items.Concat(set.Macros.Items))
            {
                switch (entry)
                {
                    case SourceSingleText { Text: var text }:
                        yield return text.Header["if"];
                        break;
                    case SourceTextFolder folder:
                        yield return folder.Index["if"];
                        foreach (var item in folder.Items)
                            yield return item.Header["if"];
                        break;
                }
            }
        }

        foreach (var deliverable in Folders(context.Package.Deliverables).SelectMany(f => f.Deliverables))
            yield return deliverable.Header["if"];
    }

    private static IEnumerable<SourceDeliveryFolder> Folders(SourceDeliveryFolder folder) =>
        new[] { folder }.Concat(folder.Folders.SelectMany(Folders));

    /// <summary>The operation names <paramref name="node"/> calls; an <c>object</c>'s field names are not operations.</summary>
    private static void Operations(JsonNode? node, HashSet<string> used, int depth)
    {
        if (depth > RuleDepth)
            return;
        switch (node)
        {
            case JsonArray array:
                foreach (var element in array)
                    Operations(element, used, depth + 1);
                break;
            case JsonObject { Count: 1 } operation:
                var (name, operand) = operation.First();
                used.Add(name);
                if (name == "object" && operand is JsonObject fields)
                {
                    foreach (var (_, field) in fields)
                        Operations(field, used, depth + 1);
                }
                else
                    Operations(operand, used, depth + 1);
                break;
        }
    }
}
