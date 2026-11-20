using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): <see cref="AgentBindings"/> (part 02 § 7, D21) and the sentences of a missing binding.</summary>
public class AgentBindingsTests
{
    private static readonly TypeRegistry Registry = TypeFixture.Published();

    private static AgentBindings Published() => AgentBindings.Read(Registry.Bindings, Registry.Agents);

    [Fact]
    public void ABuiltInFormat_IsBoundByFormat()
    {
        var bindings = Published();
        var binding = bindings.Find("plaintext", FormatOrigin.BuiltIn, "generated")!;
        Assert.Equal("test-json", binding.AgentName);
        Assert.Equal(48, binding.AgentVersion);
        Assert.Equal("format", binding.Via);
        Assert.Equal(["format"], binding.StrictRejects);
        Assert.Equal("v2026.12.1", bindings.Version);
        Assert.Equal("judgement", bindings.Find("judgement", FormatOrigin.BuiltIn, "generated")!.AgentName);
        Assert.Equal("concept-extraction", bindings.Find("concept_list", FormatOrigin.Catalogue, "generated")!.AgentName);
    }

    [Fact]
    public void APackageFormat_IsBoundThroughCustom()
    {
        var bindings = Published();
        foreach (var origin in new[] { FormatOrigin.Package, FormatOrigin.Function })
        {
            var binding = bindings.Find("verdict", origin, "generated")!;
            Assert.Equal("custom-json", binding.AgentName);
            Assert.Equal(1, binding.AgentVersion);
            Assert.Equal("custom", binding.Via);
        }

        // A package format named like a built-in is still its own format: custom, never formats.
        Assert.Equal("custom", bindings.Find("plaintext", FormatOrigin.Package, "generated")!.Via);
    }

    [Fact]
    public void AnUnboundFormat_HasNoBinding()
    {
        var bindings = Published();
        Assert.Null(bindings.Find("check_result", FormatOrigin.BuiltIn, "generated"));
        Assert.Null(bindings.Find("plaintext", FormatOrigin.BuiltIn, "grounded"));
        Assert.Null(bindings.Find("verdict", FormatOrigin.Package, "computed"));
    }

    [Fact]
    public void AMalformedRegistryFile_Throws()
    {
        JsonObject Bindings() => (JsonObject)Registry.Bindings.DeepClone();
        JsonObject Agents() => (JsonObject)Registry.Agents.DeepClone();

        var noCustom = Bindings();
        noCustom.Remove("custom");
        Assert.Contains("custom", Assert.Throws<FormatException>(() => AgentBindings.Read(noCustom, Agents())).Message);

        var missingVersion = Bindings();
        missingVersion["formats"]!["plaintext"]!["generated"]!["agentVersion"] = 49;
        Assert.Contains("version 49", Assert.Throws<FormatException>(() => AgentBindings.Read(missingVersion, Agents())).Message);

        var otherVersion = Agents();
        otherVersion["version"] = "v1";
        Assert.Throws<FormatException>(() => AgentBindings.Read(Bindings(), otherVersion));

        var extra = Bindings();
        extra["surprise"] = 1;
        Assert.Throws<FormatException>(() => AgentBindings.Read(extra, Agents()));

        var badRejects = Agents();
        badRejects["agents"]!["test-json"]!["48"]!["strictRejects"] = "format";
        Assert.Throws<FormatException>(() => AgentBindings.Read(Bindings(), badRejects));

        var otherProducer = Bindings();
        otherProducer["custom"] = new JsonObject { ["computed"] = otherProducer["custom"]!["generated"]!.DeepClone() };
        Assert.Throws<FormatException>(() => AgentBindings.Read(otherProducer, Agents()));
    }

    [Fact]
    public void ANodeWithoutABinding_SaysS02012()
    {
        var refusal = FormatSentences.NoBinding("verdict", "check_result", "generated", "workflow.json");
        Assert.Equal("S02-012", refusal.Id);
        Assert.Equal("Node 'verdict' is kind agent and returns check_result, which has no binding for producer generated in agents/bindings.json.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AnAnyOfAtTheRoot_SaysS02027()
    {
        var refusal = FormatSentences.AnyOfAtRoot("pick", "string | number", "workflow.json");
        Assert.Equal("S02-027", refusal.Id);
        Assert.Equal("Node 'pick' returns string | number, an anyOf at its root, which strict mode can't send to a model; wrap it in an object.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }
}
