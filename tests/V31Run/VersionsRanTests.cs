using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>#1506 (specVersion 31, area 09; R4; part 08 § 3): <c>versionsRan</c>.</summary>
public class VersionsRanTests
{
    [Fact]
    public void VersionsRan_IsTheLocksShapeWithoutThree()
    {
        var @lock = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(CompiledGoldenTests.GoldenFolder(), "nested-note.lock.json")))!;
        var toolchain = LockCompilerTests.Example;

        var ran = VersionsRan.Of(toolchain, @lock);

        Assert.Equal(
            ["builtins", "catalogues", "culture", "evaluator", "libraries", "operations", "resolver", "runtime"],
            ran.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            @lock.Select(p => p.Key).Where(k => k is not ("documentVersion" or "package" or "resolve")).Order(StringComparer.Ordinal),
            ran.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["packageFormat", "dialect"], ran["resolver"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["dotnet", "unicode"], ran["runtime"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["agents", "formats", "outputContracts"], ran["catalogues"]!.AsObject().Select(p => p.Key));
        Assert.Equal(@lock["operations"]!.AsObject().Select(p => p.Key), ran["operations"]!.AsObject().Select(p => p.Key));
        Assert.Equal(@lock["builtins"]!.AsObject().Select(p => p.Key), ran["builtins"]!.AsObject().Select(p => p.Key));
        Assert.All(ran["operations"]!.AsObject(), p => Assert.Equal(JsonLogicOperations.OperationVersion, (string?)p.Value));
        Assert.Equal(toolchain.Evaluator, (string?)ran["evaluator"]);
        Assert.Equal(toolchain.Scriban, (string?)ran["libraries"]!["Scriban"]);
        Assert.Equal(toolchain.DotNet, (string?)ran["runtime"]!["dotnet"]);
        Assert.Equal("invariant", (string?)ran["culture"]);
    }
}
