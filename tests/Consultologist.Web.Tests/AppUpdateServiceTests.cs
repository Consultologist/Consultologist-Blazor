using Bunit;
using Consultologist.Web.Services.AppUpdate;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Consultologist.Web.Tests;

/// <summary>
/// #412: the Blazor face of the update watcher. Strict JS interop here — the
/// two calls into js/app-update.js are the whole contract, so an unexpected
/// call is a failure, not noise.
/// </summary>
public class AppUpdateServiceTests : BunitContext
{
    private AppUpdateService Create() => new(JSInterop.JSRuntime, NullLogger<AppUpdateService>.Instance);

    [Fact]
    public async Task Start_HandsTheWatcherAReferenceToItself()
    {
        var call = JSInterop.Setup<bool>("consultologistUpdate.start", _ => true).SetResult(true);
        var service = Create();

        await service.StartAsync();

        var argument = Assert.Single(Assert.Single(call.Invocations).Arguments);
        Assert.Same(service, Assert.IsType<DotNetObjectReference<AppUpdateService>>(argument).Value);
    }

    [Fact]
    public async Task Start_IsOnce()
    {
        var call = JSInterop.Setup<bool>("consultologistUpdate.start", _ => true).SetResult(true);
        var service = Create();

        await service.StartAsync();
        await service.StartAsync();

        Assert.Single(call.Invocations);
    }

    [Fact]
    public void NothingIsReadyUntilTheWatcherSaysSo()
    {
        var service = Create();
        var raised = 0;
        service.UpdateReadyChanged += () => raised++;

        Assert.False(service.UpdateReady);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void OnUpdateReady_FlipsTheFlagAndRaisesOnce()
    {
        var service = Create();
        var raised = 0;
        service.UpdateReadyChanged += () => raised++;

        service.OnUpdateReady();
        service.OnUpdateReady();

        Assert.True(service.UpdateReady);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Reload_AsksTheWatcherForTheHandOver()
    {
        var call = JSInterop.SetupVoid("consultologistUpdate.reload").SetVoidResult();

        await Create().ReloadAsync();

        Assert.Single(call.Invocations);
    }

    [Fact]
    public async Task Start_WithoutTheWatcher_CostsOnlyTheNotice()
    {
        JSInterop.Setup<bool>("consultologistUpdate.start", _ => true)
            .SetException(new JSException("consultologistUpdate is not defined"));
        var service = Create();

        await service.StartAsync();

        Assert.False(service.UpdateReady);
    }
}
