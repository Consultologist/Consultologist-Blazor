using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1234 (package-format-v27-design.md): the 27 packages and the seam that let
/// them through while 27 was wired but not yet accepted (the bump, #1237, lists
/// 27; <see cref="Accepting27"/> is a harmless duplicate since), as
/// <see cref="V26Package.Accepting26"/> did for 26. <see cref="Minimal"/> is the
/// 26 minimal with its version raised — valid at 27 unchanged (§ 7: no
/// migration). The builders add what 27 opens: an if or case node inside the
/// loop's body, its branches' nodes running per item.
/// </summary>
public static class V27Package
{
    public static readonly IReadOnlyCollection<int> Accepting27 = WorkflowPackageValidator.AcceptedSpecVersions.Append(27).ToList();

    /// <summary>The 26 minimal at specVersion 27 — a version raise, nothing else.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (manifest, files) = V26Package.Minimal();
        return (manifest with { SpecVersion = 27 }, files);
    }

    public static WorkflowFlowBodySpec Body(string? ret, params string[] nodes) => new(nodes.ToList(), ret);

    /// <summary>
    /// The minimal with an if node `triage` inside the loop `sections`: its then
    /// runs `patient_draft` (reading the item and the loop's `section_notes` one
    /// level up), its else `table_draft`; the loop returns the flow node.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithTriageInLoop(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle,
        string condition = "item.id == \"hpi\"",
        WorkflowFlowBodySpec? then = null,
        WorkflowFlowBodySpec? @else = null,
        string ret = "node.triage")
    {
        var (m, files) = bundle;
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/patient.md"] = "Patient {{ item.label }}: {{ node.section_notes.content }}",
            ["prompts/table.md"] = "Table of {{ item.content }}.",
        };
        m = V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("patient_draft", "Patient draft", Prompt: "prompts/patient.md", Format: "prose"),
                new WorkflowNodeSpec("table_draft", "Table draft", Prompt: "prompts/table.md", Format: "prose"),
                new WorkflowNodeSpec("triage", "Triage", If: condition, Then: then ?? Body("node.patient_draft", "patient_draft"), Else: @else ?? Body("node.table_draft", "table_draft"))),
            "sections", n => n with { Nodes = new List<string> { "section_notes", "triage" }, Ret = ret });
        return (m, files);
    }

    /// <summary>
    /// The minimal with a second loop `topic_sections` over an array input
    /// `topics` of objects (a `title` text, a `kind` enum), whose body is a case
    /// node `route` switching on item.value.kind — `plan` for a narrative,
    /// `letter` for a table — returned by the loop and sourced.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithRouteInLoop(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle,
        string subject = "item.value.kind",
        params WorkflowCaseBranchSpec[] branches)
    {
        var (m, files) = bundle;
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/plan.md"] = "Plan for {{ item.value.title }}.",
            ["prompts/letter.md"] = "Letter about {{ item.value.title }}.",
        };
        m = V26Package.WithNodes(m with
            {
                Inputs = m.Inputs!.Append(new WorkflowInputSpec("topics", "Topics", false, "array",
                    Items: new WorkflowElementSpec("object"),
                    Fields: new List<WorkflowFieldSpec> { new("title", "Title", true, "text"), new("kind", "Kind", true, "enum", new List<string> { "narrative", "table" }) })).ToList(),
                Deliverables = m.Deliverables!.Select(d => d with { Sources = (d.Sources ?? new List<string>()).Append("node.topic_sections").ToList() }).ToList(),
            },
            new WorkflowNodeSpec("plan", "Plan", Prompt: "prompts/plan.md", Format: "prose"),
            new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose"),
            new WorkflowNodeSpec("route", "Route", Case: subject, Branches: branches.Length == 0
                ? new List<WorkflowCaseBranchSpec>
                {
                    new(new List<string> { "narrative" }, new List<string> { "plan" }, "node.plan"),
                    new(new List<string> { "table" }, new List<string> { "letter" }, "node.letter"),
                }
                : branches.ToList()),
            new WorkflowNodeSpec("topic_sections", "Topic sections", For: "item in input.topics", Nodes: new List<string> { "route" }, Ret: "node.route"));
        return (m, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting27);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;

    public static List<string> Errors(Func<(WorkflowPackageManifest Manifest, Dictionary<string, string> Files), (WorkflowPackageManifest Manifest, Dictionary<string, string> Files)> change) =>
        Errors(change(Minimal()));

    /// <summary>A change of the manifest alone, then of the files.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) With(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle,
        Func<WorkflowPackageManifest, WorkflowPackageManifest>? manifest = null,
        Action<Dictionary<string, string>>? files = null)
    {
        var copy = new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal);
        files?.Invoke(copy);
        return (manifest is null ? bundle.Manifest : manifest(bundle.Manifest), copy);
    }
}

public class WorkflowV27Tests
{
    [Fact]
    public void ThePublicEntry_Accepts27_SinceTheBump_AndRefuses28()
    {
        // #1237: the accepted set carries 27 (with the store's set and both of the
        // editor's ceilings). The seam and the public door agree; the version
        // after is what the set refuses now.
        Assert.Contains(27, WorkflowPackageValidator.AcceptedSpecVersions);

        var (manifest, files) = V27Package.Minimal();
        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Empty(result.Errors);
        Assert.Empty(V27Package.Errors(V27Package.Minimal()));

        var next = WorkflowPackageValidator.Validate(manifest with { SpecVersion = 28 }, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Contains(next.Errors, e => e.StartsWith("specVersion 28 is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMinimal_IsThe26Minimal_RaisedByVersion()
    {
        // § 7: 27 only permits what 26 refused — a 26 package is a 27 package by its version.
        var (manifest, files) = V27Package.Minimal();

        Assert.Equal(27, manifest.SpecVersion);
        Assert.Empty(V27Package.Errors((manifest, files)));
        Assert.Empty(V26Package.Errors((manifest with { SpecVersion = 26 }, files)));
    }
}
