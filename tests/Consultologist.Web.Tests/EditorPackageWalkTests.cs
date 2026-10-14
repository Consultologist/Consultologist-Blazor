using Consultologist.Web.Services.Workflow;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C2 (#1008): the editor runs the engine's own walk. The catalogue the API
/// describes binds back into the library's catalogue with the same ids, types,
/// producers and parameters; the walk of the 25 fixture names each snippet's readers.
/// </summary>
public class EditorPackageWalkTests
{
    [Fact]
    public void CatalogFrom_BindsTheDescribedCatalogue_BackToTheLibrarysShape()
    {
        var bound = EditorPackageWalk.CatalogFrom(EditorFormatsCatalog.View());

        Assert.NotNull(bound);
        Assert.Equal(EditorFormatsCatalog.Catalog.Version, bound!.Version);
        Assert.Equal(EditorFormatsCatalog.Catalog.Formats.Keys.Order(StringComparer.Ordinal), bound.Formats.Keys.Order(StringComparer.Ordinal));

        foreach (var (id, original) in EditorFormatsCatalog.Catalog.Formats)
        {
            var copy = bound.Formats[id];
            Assert.Equal(original.Spec.Type, copy.Spec.Type);
            Assert.Equal(original.Spec.Producer, copy.Spec.Producer);
            Assert.Equal(original.Spec.Values, copy.Spec.Values);
            Assert.Equal(
                original.Spec.Parameters?.ToDictionary(p => p.Key, p => (p.Value.Kind, p.Value.Format)),
                copy.Spec.Parameters?.ToDictionary(p => p.Key, p => (p.Value.Kind, p.Value.Format)));
            Assert.Equal(original.Spec.Schema is not null, copy.Spec.Schema is not null);
            Assert.Equal(original.Entry.AgentName, copy.Entry.AgentName);
            Assert.Equal(original.Entry.Op, copy.Entry.Op);
        }

        Assert.Equal(EditorFormatsCatalog.Catalog.CustomProducer.AgentName, bound.CustomProducer.AgentName);
        Assert.Null(EditorPackageWalk.CatalogFrom(null));
    }

    [Fact]
    public void Walk_NamesEachSnippetsReaders_FromTheFixture()
    {
        var package = EditorFixtures.V25();

        var walk = EditorPackageWalk.Walk(package.Manifest.GetRawText(), package.Files, EditorPackageWalk.CatalogFrom(EditorFormatsCatalog.View()));

        Assert.NotNull(walk);
        var readers = walk!.Templates.Where(t => t.References.Snippets.Contains("standards")).Select(t => t.Subject).ToList();
        Assert.Equal(new[] { "Prompt 'assessment'" }, readers);
        // The folder's items walk as templates of their own.
        Assert.Contains(walk.Templates, t => t.Kind == PF.TemplateKind.Snippet && t.Id == "sections/hpi");
        Assert.Contains(walk.Templates, t => t.Kind == PF.TemplateKind.Snippet && t.Id == "standards");
    }

    [Fact]
    public void Walk_WithoutTheCatalogue_StillWalksSnippetsAndLocalFormatNodes()
    {
        var package = EditorFixtures.V25();

        var walk = EditorPackageWalk.Walk(package.Manifest.GetRawText(), package.Files, null);

        Assert.NotNull(walk);
        // meds is on the package-local format, so its prompt walks; assessment (prose, catalogue) does not.
        Assert.Contains(walk!.Templates, t => t.Subject == "Prompt 'meds'");
        Assert.DoesNotContain(walk.Templates, t => t.Subject == "Prompt 'assessment'");
        Assert.Contains(walk.Templates, t => t.Id == "standards");
    }

    [Fact]
    public void Walk_IsNull_WhenTheManifestDoesNotRead()
    {
        Assert.Null(EditorPackageWalk.Walk("{ nope", new Dictionary<string, string>(), null));
    }
}
