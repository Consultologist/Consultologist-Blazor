using System.Text.RegularExpressions;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The node vocabulary: binding-reference parsing, the closed concept-renderer
/// set, and the engine defaults. Parsing is namespace-syntactic; vocabulary
/// closures (collection-declared item fields, declared data entries, declared
/// input slots) belong to the validator. Pre-v5 vocabularies (map kinds,
/// previous_step_output, input:sections, the synthesis shims) were retired by
/// the v5-only rebase — see external/consultologist-package-format/package-format-v5.md.
/// </summary>
public abstract record WorkflowNodeBindingSource
{
    public sealed record Input(string Name) : WorkflowNodeBindingSource;
    public sealed record NodeOutput(string NodeId) : WorkflowNodeBindingSource;
    public sealed record Item(string Field) : WorkflowNodeBindingSource;
    public sealed record Data(string Id) : WorkflowNodeBindingSource;
}

public static class WorkflowNodeBindingSources
{
    public const string InputConsultDraft = "input:consult_draft";
    public const string ItemName = "item:name";
    public const string InputPrefix = "input:";
    public const string NodePrefix = "node:";
    public const string DataPrefix = "data:";

    public static bool TryParse(string raw, out WorkflowNodeBindingSource? source, out string? error)
    {
        source = null;
        error = null;

        var separator = raw.IndexOf(':');
        if (separator <= 0 || separator == raw.Length - 1)
        {
            error = $"unrecognized source '{raw}'";
            return false;
        }

        var ns = raw[..separator];
        var name = raw[(separator + 1)..];

        switch (ns)
        {
            case "input":
                source = new WorkflowNodeBindingSource.Input(name);
                return true;
            case "item":
                source = new WorkflowNodeBindingSource.Item(name);
                return true;
            case "data":
                source = new WorkflowNodeBindingSource.Data(name);
                return true;
            case "node":
                source = new WorkflowNodeBindingSource.NodeOutput(name);
                return true;
            default:
                error = $"unrecognized source '{raw}'";
                return false;
        }
    }
}

/// <summary>
/// The id grammar shared by specVersion-7 declared inputs and results:
/// snake_case, letter-first (package-format-v7.md § 2). One rule for both
/// declared-vocabulary sections; result ids additionally feed delivery
/// filenames, where the absence of '-' keeps "{resultId}-{jobId8}" unambiguous.
/// </summary>
public static class WorkflowDeclaredIds
{
    private static readonly Regex Pattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public static bool IsValid(string? id) => id != null && Pattern.IsMatch(id);
}

public static class WorkflowConceptRenderers
{
    public const string ConceptBullets = "concept-bullets";
    public const string ConceptContext = "concept-context";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ConceptBullets,
        ConceptContext
    };
}

public static class WorkflowNodeDefaults
{
    /// <summary>
    /// Legacy concept source stamps for the four canonical analysis node ids: the
    /// concept-context rendering embeds "source:", and these strings predate node
    /// ids — byte parity for the canonical workflow requires preserving them.
    /// Custom node ids fall back to the node id itself.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WellKnownConceptSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["extract-patient-concepts"] = "patient",
        ["identify-problem"] = "problem",
        ["create-typical-trajectory"] = "typical-trajectory",
        ["create-patient-trajectory"] = "patient-trajectory"
    };

    public const string ConceptListSchemaId = "concept-list";
}
