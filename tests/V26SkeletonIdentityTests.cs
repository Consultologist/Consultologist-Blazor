using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1154 (package-format-v26-design.md § 1.4, § 14): the same skeleton — for
/// each public package, the 25 package and its migrated 26 twin resolve the
/// same blocks (count, names, items; the id's middle segment the loop's where
/// it was the fanned node's), the same collections and rosters, and decide
/// the same fire set over the same inputs. The offline half of the issue's
/// done-when; the run with equal hashes is the bump's.
/// </summary>
public class V26SkeletonIdentityTests
{
    private static readonly string Packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Consultologist", "consultologist-workflows", "packages");

    private static readonly string[] Names = { "general", "test/general", "example-two-documents", "example-v12-constructs", "example-classifier-scope", "example-conditional-documents", "example-structured-intake" };

    private static Dictionary<string, string> FilesOf(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("manifest.json", StringComparison.Ordinal) && !p.EndsWith("dag.mmd", StringComparison.Ordinal) && !p.EndsWith(".stamp.json", StringComparison.Ordinal))
            .ToDictionary(p => Path.GetRelativePath(dir, p).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);

    private static WorkflowPackage Load(WorkflowPackageManifest manifest, Dictionary<string, string> files)
    {
        var errors = new List<string>();
        var texts = manifest.SpecVersion >= 26 ? WorkflowTextHeaders.Resolve(manifest, files, errors) : null;
        var data = texts is null
            ? WorkflowDataResolver.ResolveContent(manifest, files, errors)
            : WorkflowDataResolver.ResolveContent(texts.Manifest, texts, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            SourceFiles: files,
            Results: WorkflowPackageStore.ResolveResultSet(manifest),
            Formats: TestFormats.Catalog,
            FormatsRef: TestFormats.Catalog.ResolvedRef,
            Texts: texts);
    }

    /// <summary>One value per declared input, by type — enough for every condition to decide.</summary>
    private static Dictionary<string, ConsultInputValue> SuppliedFor(WorkflowPackageManifest manifest) =>
        (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .ToDictionary(input => input.Id, input => ValueFor(WorkflowDeclarationNode.Of(input)), StringComparer.Ordinal);

    private static ConsultInputValue ValueFor(WorkflowDeclarationNode node) => node.Type switch
    {
        WorkflowInputTypes.Enum => ConsultInputValue.OfText(node.Values![0]),
        WorkflowInputTypes.Boolean => ConsultInputValue.OfBoolean(true),
        WorkflowInputTypes.Number => ConsultInputValue.OfNumber("1"),
        WorkflowInputTypes.Date => ConsultInputValue.OfText("2026-01-02"),
        WorkflowInputTypes.Array => ConsultInputValue.OfArray(new[] { ValueFor(node.Items!), ValueFor(node.Items!) }),
        WorkflowInputTypes.Object => ConsultInputValue.OfObject(node.Fields!.Select(field => new ConsultInputEntry(field.Id, ValueFor(field)))),
        _ => ConsultInputValue.OfText("Referral text."),
    };

    private static ConsultConditionScope ScopeOf(WorkflowPackage package, Dictionary<string, ConsultInputValue> supplied) =>
        ConsultGenerationJobStarter.StartScope(package, V26JobStartTests.Effective(supplied), supplied, "job00000000", "host", new DateTime(2026, 1, 2));

    [Fact]
    public void ThePublicPackages_ResolveTheSameSkeletonAt26()
    {
        if (!Directory.Exists(Packages))
        {
            return;
        }

        foreach (var name in Names)
        {
            var dir = Path.Combine(Packages, name.Replace('/', Path.DirectorySeparatorChar));
            var files = FilesOf(dir);
            var json = File.ReadAllText(Path.Combine(dir, "manifest.json"));
            var at25 = Load(WorkflowPackageManifestJson.Read(json, name, WorkflowPackageValidator.AcceptedSpecVersions), files);

            var migrated = WorkflowPackageMigration25To26.Migrate(json, files);
            Assert.True(migrated.Succeeded, $"{name}: {string.Join(" | ", migrated.Refusals)}");
            var at26 = Load(WorkflowPackageManifestJson.Read(migrated.ManifestJson, name, V26Package.Accepting26.ToList()), migrated.Files);
            var loops = at26.Nodes!.Where(n => n.For is not null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var fanned = at25.Nodes!.Where(n => n.ForEach is not null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            var supplied = SuppliedFor(at25.Manifest);
            var gating25 = ConsultGenerationJobStarter.GatingReads(at25)!;
            var gating26 = ConsultGenerationJobStarter.GatingReads(at26)!;
            Assert.True(gating25.SetEquals(gating26), $"{name}: gating reads {string.Join(",", gating25.Order())} vs {string.Join(",", gating26.Order())}");

            var package25 = at25;
            var package26 = at26;
            if (gating25.Count == 0)
            {
                // Not deciding: the fire set is the start's, over the same inputs.
                var fire25 = ConsultGenerationJobStarter.DecideFireSet(at25, supplied, null, ScopeOf(at25, supplied));
                var fire26 = ConsultGenerationJobStarter.DecideFireSet(at26, supplied, null, ScopeOf(at26, supplied));
                Assert.Equal(fire25.Firing.Select(r => r.Id), fire26.Firing.Select(r => r.Id));
                Assert.Equal(fire25.Skipped.Select(s => (s.ResultId, s.Reason)), fire26.Skipped.Select(s => (s.ResultId, s.Reason)));
                package25 = fire25.Package;
                package26 = fire26.Package;
            }

            var skeleton25 = ConsultGenerationJobStarter.ResolveSkeleton(package25, supplied, ScopeOf(package25, supplied));
            var skeleton26 = ConsultGenerationJobStarter.ResolveSkeleton(package26, supplied, ScopeOf(package26, supplied));

            Assert.Equal(skeleton25.Items.Count, skeleton26.Items.Count);
            Assert.Equal(skeleton25.Items.Select(i => i["name"]), skeleton26.Items.Select(i => i["name"]));
            foreach (var (block25, block26) in skeleton25.Items.Zip(skeleton26.Items))
            {
                var parts25 = block25["id"].Split(':');
                var parts26 = block26["id"].Split(':');
                Assert.Equal(parts25.Length, parts26.Length);
                Assert.Equal(parts25[0], parts26[0]);
                if (parts25.Length == 3)
                {
                    // A fanned block: the 25 id named the fanned node, the 26 id names the loop; the item is the same.
                    Assert.Equal(parts25[2], parts26[2]);
                    Assert.True(fanned.Contains(parts25[1]), $"{name}: {block25["id"]} names no fanned node");
                    Assert.True(loops.Contains(parts26[1]), $"{name}: {block26["id"]} names no loop");
                }
                else
                {
                    Assert.Equal(block25["id"], block26["id"]);
                }
            }

            Assert.Equal(skeleton25.CollectionSets.Keys.Order(), skeleton26.CollectionSets.Keys.Order());
            foreach (var (key, items25) in skeleton25.CollectionSets)
            {
                Assert.Equal(items25.Select(i => i["id"]), skeleton26.CollectionSets[key].Select(i => i["id"]));
            }

            Assert.Equal(
                skeleton25.CollectionRosters.Select(r => (r.CollectionId, string.Join("|", r.Items.Select(i => $"{i.Id}={i.Name}")))),
                skeleton26.CollectionRosters.Select(r => (r.CollectionId, string.Join("|", r.Items.Select(i => $"{i.Id}={i.Name}")))));
            Assert.Equal(skeleton25.EmptyFanLabels, skeleton26.EmptyFanLabels);
        }
    }
}
