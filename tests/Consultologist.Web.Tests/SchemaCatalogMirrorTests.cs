using System.Text.Json.Nodes;
using Consultologist.PackageFormat;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// #759: the Schemas pane's catalog-backed add writes a bundled contract body
/// because the client has no runtime catalog. This pins that bundle against the
/// engine catalog — the same discipline as SpecVersionMirrorTests — so a body
/// the editor writes always canonically matches what the server will accept.
/// </summary>
public class SchemaCatalogMirrorTests
{
    [Fact]
    public void EachBundledContract_CanonicallyMatchesTheCatalog()
    {
        foreach (var (id, body) in WorkflowOutputContracts.Declarable)
        {
            Assert.True(EditorCatalogSchemas.CatalogSchemas.ContainsKey(id),
                $"Bundled contract '{id}' is not in the engine catalog.");

            var bundled = WorkflowPackageValidator.CanonicalizeSchema(JsonNode.Parse(body));
            var catalog = WorkflowPackageValidator.CanonicalizeSchema(JsonNode.Parse(EditorCatalogSchemas.CatalogSchemas[id]));
            Assert.Equal(catalog, bundled);
        }
    }

    [Fact]
    public void TheDeclarableSet_IsConceptListOnly()
    {
        // classification is implied by a classifier's kind (refused when declared),
        // and text is the no-output default — so concept-list is the only contract
        // the pane may add today.
        Assert.Equal(new[] { WorkflowNodeDefaults.ConceptListSchemaId }, WorkflowOutputContracts.Declarable.Keys.ToArray());
    }
}
