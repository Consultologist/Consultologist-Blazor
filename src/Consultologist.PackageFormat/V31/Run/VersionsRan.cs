using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1506 (specVersion 31, area 09; R4; part 08 § 3): the versions a run ran under.</summary>
public static class VersionsRan
{
    /// <summary>
    /// The lock's nested shape without <c>documentVersion</c>, <c>package</c> and <c>resolve</c>: <c>resolver</c>,
    /// <c>libraries</c>, <c>evaluator</c>, <c>operations</c>, <c>builtins</c>, <c>culture</c>, <c>runtime</c>, <c>catalogues</c>.
    /// What ran is this process's <paramref name="toolchain"/> and this engine's operations and built-ins, the ones the
    /// <paramref name="lockDocument"/> names (its keys), each at the version this engine holds.
    /// </summary>
    public static JsonObject Of(Toolchain toolchain, JsonObject lockDocument)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(lockDocument);

        var libraries = new JsonObject();
        foreach (var (name, version) in toolchain.Libraries.OrderBy(l => l.Key, StringComparer.Ordinal))
            libraries[name] = version;

        var operations = new JsonObject();
        if (lockDocument["operations"] is JsonObject locked)
        {
            foreach (var name in locked.Select(p => p.Key))
                operations[name] = JsonLogicOperations.OperationVersion;
        }

        var builtins = new JsonObject();
        if (lockDocument["builtins"] is JsonObject lockedBuiltins)
        {
            foreach (var (name, value) in lockedBuiltins)
                builtins[name] = LockCompiler.BuiltinVersions.TryGetValue(name, out var version) ? version : value?.GetValue<string>();
        }

        return new JsonObject
        {
            ["resolver"] = new JsonObject { ["packageFormat"] = toolchain.PackageFormat, ["dialect"] = toolchain.Dialect },
            ["libraries"] = libraries,
            ["evaluator"] = toolchain.Evaluator,
            ["operations"] = operations,
            ["builtins"] = builtins,
            ["culture"] = LockCompiler.Culture,
            ["runtime"] = new JsonObject { ["dotnet"] = toolchain.DotNet, ["unicode"] = toolchain.Unicode },
            ["catalogues"] = new JsonObject
            {
                ["agents"] = toolchain.AgentsCatalogue,
                ["formats"] = toolchain.FormatsCatalogue,
                ["outputContracts"] = toolchain.OutputContracts,
            },
        };
    }
}
