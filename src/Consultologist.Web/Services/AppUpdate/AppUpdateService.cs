using Microsoft.JSInterop;

namespace Consultologist.Web.Services.AppUpdate;

/// <summary>
/// #412: whether a newer build of the client is installed and waiting, and the
/// one action that lets it take over. The detection lives in
/// wwwroot/js/app-update.js; this is its Blazor face.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>True once the worker for a newer build is waiting.</summary>
    bool UpdateReady { get; }

    /// <summary>Raised once, when <see cref="UpdateReady"/> becomes true.</summary>
    event Action? UpdateReadyChanged;

    /// <summary>Registers the service worker and starts watching for a newer build.</summary>
    Task StartAsync();

    /// <summary>Hands the page to the waiting worker and reloads. The user's click, never automatic.</summary>
    Task ReloadAsync();
}

public sealed class AppUpdateService : IAppUpdateService, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AppUpdateService> _logger;
    private DotNetObjectReference<AppUpdateService>? _reference;
    private bool _started;

    public AppUpdateService(IJSRuntime jsRuntime, ILogger<AppUpdateService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public bool UpdateReady { get; private set; }

    public event Action? UpdateReadyChanged;

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _reference = DotNetObjectReference.Create(this);

        try
        {
            await _jsRuntime.InvokeAsync<bool>("consultologistUpdate.start", _reference);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            // No service worker (or no script) only costs the notice, never the page.
            _logger.LogWarning(exception, "Update watcher could not start");
        }
    }

    [JSInvokable]
    public void OnUpdateReady()
    {
        if (UpdateReady)
        {
            return;
        }

        UpdateReady = true;
        UpdateReadyChanged?.Invoke();
    }

    public async Task ReloadAsync()
    {
        await _jsRuntime.InvokeVoidAsync("consultologistUpdate.reload");
    }

    public ValueTask DisposeAsync()
    {
        _reference?.Dispose();
        return ValueTask.CompletedTask;
    }
}
