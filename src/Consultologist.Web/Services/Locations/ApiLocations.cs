using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace Consultologist.Web.Services.Locations;

/// <summary>One deployed region: a name for the card and the API base every client call is built on.</summary>
public sealed record ApiLocation(string Id, string Name, string ApiBase)
{
    /// <summary>The bare host — what the record says as apiHost (#514).</summary>
    public string Host => Uri.TryCreate(ApiBase, UriKind.Absolute, out var uri) ? uri.Host : ApiBase;
}

/// <summary>
/// #515: the location the app talks to — the data-residency choice, made on
/// the profile and kept on this device. Accounts, packages and History live
/// per region, so the choice has to be readable before the first API call:
/// localStorage is the authority; the account's consult.location is a record.
///
/// Empty until chosen (#518's rule): unchosen, the first listed location is
/// used and the card says so; a second location never chooses itself.
/// </summary>
public interface IApiLocations
{
    IReadOnlyList<ApiLocation> All { get; }

    /// <summary>What this device chose, or null — an id the list no longer has reads as unchosen.</summary>
    ApiLocation? Chosen { get; }

    /// <summary>The location in use: the choice, else the first listed.</summary>
    ApiLocation Current { get; }

    /// <summary>Current.ApiBase without a trailing slash — e.g. https://east.ca.api.consultologist.ai/api.</summary>
    string ApiBase { get; }

    /// <summary>ApiBase + "/" + route.</summary>
    string Url(string route);

    Task ChooseAsync(string id);

    Task ClearAsync();
}

public sealed class ApiLocations : IApiLocations
{
    public const string ConfigurationSection = "Locations";

    /// <summary>The localStorage key; the value is a location id.</summary>
    public const string StorageKey = "api-location";

    private readonly IJSRuntime _js;
    private string? _chosenId;

    public ApiLocations(IConfiguration configuration, IJSRuntime js)
        : this(configuration, js, ReadStored(js))
    {
    }

    internal ApiLocations(IConfiguration configuration, IJSRuntime js, string? storedId)
    {
        _js = js;
        All = Bind(configuration);
        _chosenId = storedId;
    }

    public IReadOnlyList<ApiLocation> All { get; }

    public ApiLocation? Chosen => Find(_chosenId);

    public ApiLocation Current => Chosen
        ?? (All.Count > 0 ? All[0] : throw new InvalidOperationException("No location is configured (Locations in appsettings.json)."));

    public string ApiBase => Current.ApiBase.TrimEnd('/');

    public string Url(string route) => ApiBase + "/" + route.TrimStart('/');

    public async Task ChooseAsync(string id)
    {
        if (Find(id) == null)
        {
            throw new ArgumentException($"'{id}' is not a configured location.", nameof(id));
        }

        _chosenId = id;
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, id);
        }
        catch
        {
            // Storage refused (private mode, quota): the choice holds for this tab.
        }
    }

    public async Task ClearAsync()
    {
        _chosenId = null;
        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch
        {
        }
    }

    private ApiLocation? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(l => string.Equals(l.Id, id.Trim(), StringComparison.Ordinal));

    internal static IReadOnlyList<ApiLocation> Bind(IConfiguration configuration) =>
        configuration.GetSection(ConfigurationSection).GetChildren()
            .Select(s => new ApiLocation(s["Id"] ?? "", s["Name"] ?? "", s["ApiBase"] ?? ""))
            .Where(l => l.Id.Length > 0 && l.ApiBase.Length > 0)
            .ToList();

    /// <summary>
    /// Read synchronously: the WebAssembly runtime is in-process, and the
    /// first service to build a URL cannot wait on an async read.
    /// </summary>
    private static string? ReadStored(IJSRuntime js)
    {
        try
        {
            return js is IJSInProcessRuntime inProcess
                ? inProcess.Invoke<string?>("localStorage.getItem", StorageKey)
                : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>The API routes, relative to a location's base — one place, so every service agrees.</summary>
public static class ApiRoutes
{
    public const string Account = "Account";
    public const string AccountMe = "Account/Me";
    public const string ConsultGenerationJobs = "ConsultGenerationJobs";
    public const string DocumentExtractions = "DocumentExtractions";
    public const string DiagnosticsSseExit = "Diagnostics/SseExit";
    public const string WorkflowPackageCurrent = "WorkflowPackages/Current";
    public const string WorkflowPackageContent = "WorkflowPackages/Current/Content";
    public const string WorkflowPackagePublish = "WorkflowPackages/Publish";
    public const string WorkflowPackageDiagram = "WorkflowPackages/Current/Diagram";
    public const string WorkflowPackageMinePackages = "WorkflowPackages/MinePackages";
    public const string WorkflowPackageDiagramPreview = "WorkflowPackages/Diagram";
    public const string WorkflowPackageLineage = "WorkflowPackages/Lineage";
    public const string OperatorUsage = "Operator/Usage";
    public const string PublicChain = "Public/Chain";
    public const string PublicEngine = "Public/Engine";
}
