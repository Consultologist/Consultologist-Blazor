using System.Reflection;
using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #452: every published version in both registries against a candidate
/// catalog, with how many accounts are pinned to each version that would be
/// stranded. Reads only.
/// </summary>
public sealed class CatalogStrandSweeper
{
    private readonly IWorkflowPackageRegistryReader _registry;
    private readonly IAccountStore _accounts;
    private readonly IWorkflowPackagePinResolver _pins;

    public CatalogStrandSweeper(IWorkflowPackageRegistryReader registry, IAccountStore accounts, IWorkflowPackagePinResolver pins)
    {
        _registry = registry;
        _accounts = accounts;
        _pins = pins;
    }

    public async Task<CatalogStrandResponse> RunAsync(OutputContractCatalog candidate, CancellationToken cancellationToken)
    {
        var found = new HashSet<(string Name, string Version)>();

        foreach (var privateRegistry in new[] { false, true })
        {
            foreach (var blob in await _registry.ListBlobNamesAsync(privateRegistry, cancellationToken))
            {
                if (WorkflowPackageRef.TryParseManifestPath(blob, out var name, out var version))
                {
                    found.Add((name!, version!));
                }
            }
        }

        var versions = found.OrderBy(v => v.Name, StringComparer.Ordinal).ThenBy(v => v.Version, StringComparer.Ordinal).ToList();
        var pinnedBy = await CountPinsAsync(cancellationToken);
        var listed = new List<CatalogStrandVersion>();
        int checkedCount = 0, stampedCount = 0, unsupported = 0, noSchema = 0;

        foreach (var (name, version) in versions)
        {
            var packageRef = $"{name}@{version}";
            var manifestJson = await _registry.TryDownloadAsync(name, $"{name}/{version}/{WorkflowPackageRef.ManifestFileName}", cancellationToken);
            if (manifestJson == null)
            {
                listed.Add(new CatalogStrandVersion(packageRef, PinHealthStatuses.Unreadable,
                    new[] { new CatalogStrandSchema("(manifest)", "manifest.json listed but not readable") }, pinnedBy.GetValueOrDefault(packageRef)));
                continue;
            }

            var stampJson = await _registry.TryDownloadAsync(name, $"{name}/{version}/{WorkflowPackageStamp.FileName}", cancellationToken);
            var files = new Dictionary<string, string?>(StringComparer.Ordinal);

            var result = CatalogStrands.Check(
                packageRef,
                manifestJson,
                stampJson,
                path => files.TryGetValue(path, out var cached)
                    ? cached
                    : files[path] = _registry.TryDownloadAsync(name, $"{name}/{version}/{path}", cancellationToken).GetAwaiter().GetResult(),
                candidate,
                WorkflowPackageStore.SupportedSpecVersions,
                out var skip,
                out var stamped);

            if (skip == CatalogStrandSkips.UnsupportedSpec) { unsupported++; continue; }
            if (skip == CatalogStrandSkips.NoSchema) { noSchema++; continue; }

            checkedCount++;
            if (stamped) { stampedCount++; }
            if (result != null)
            {
                listed.Add(result with { PinnedBy = pinnedBy.GetValueOrDefault(packageRef) });
            }
        }

        return new CatalogStrandResponse(
            candidate.ResolvedRef,
            EngineAttestation.CommitOf(typeof(CatalogStrandSweeper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
            DateTimeOffset.UtcNow,
            new CatalogStrandCounts(versions.Count, checkedCount, stampedCount, unsupported, noSchema, _registry.HasPublicRegistry),
            listed);
    }

    /// <summary>Accounts per concrete version; an @latest pin counts against the version its pointer names today.</summary>
    private async Task<Dictionary<string, int>> CountPinsAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var latest = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var account in await _accounts.ListAsync(cancellationToken))
        {
            WorkflowPackageRef pin;
            try
            {
                pin = await _pins.ResolvePinAsync(account.AppUserId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            var version = pin.Version;
            if (pin.IsLatest)
            {
                if (!latest.TryGetValue(pin.Name, out version))
                {
                    var pointer = await _registry.TryDownloadAsync(pin.Name, $"{pin.Name}/latest.json", cancellationToken);
                    version = ReadLatestVersion(pointer);
                    latest[pin.Name] = version;
                }

                if (version == null)
                {
                    continue;
                }
            }

            var key = $"{pin.Name}@{version}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    private static string? ReadLatestVersion(string? pointerJson)
    {
        if (pointerJson == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(pointerJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("version") || property.NameEquals("Version"))
                {
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
