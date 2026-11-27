using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Consultologist.Web.Shared.History31;

/// <summary>
/// #1509 (Task 19 fix round 1): what every row of a disclosure section shares (<see cref="Disclosure31"/>): the node, whether
/// it is open, its body's id, its text fetched once when it is first opened (a failed fetch is forgotten and said through
/// <see cref="FetchFailed"/>), and focus when a link sent the page to it. A row component inherits this
/// (<c>@inherits DisclosureRow31&lt;MyView&gt;</c>), puts <c>@ref="Element"</c>, <c>id="row-@Node.Key"</c> and
/// <c>tabindex="-1"</c> on its element, and draws its own line and body.
/// </summary>
public abstract class DisclosureRow31<TView> : ComponentBase where TView : Disclosure31
{
    private bool _loading;
    private bool _wasOpen;

    [Parameter, EditorRequired]
    public TreeNode31 Node { get; set; } = default!;

    [Parameter, EditorRequired]
    public TView View { get; set; } = default!;

    protected ElementReference Element;

    /// <summary>The row's text once fetched (opened); null before, when the job has none, or after a failure.</summary>
    protected RecordRowText? Fetched { get; private set; }

    protected bool FetchFailed { get; private set; }

    protected Row31? Row => Node.Row;

    protected bool IsOpen => View.IsOpen(Node.Key);

    protected string BodyId => $"row-{Node.Key}-body";

    /// <summary>The body's id for <c>aria-controls</c>: only while the body exists.</summary>
    protected string? ControlsId => IsOpen ? BodyId : null;

    protected override async Task OnParametersSetAsync()
    {
        var opened = IsOpen && !_wasOpen;
        _wasOpen = IsOpen;
        if (!opened || Row is null)
        {
            return;
        }

        await OnOpenedAsync();
        if (_loading || Fetched is not null)
        {
            return;
        }

        _loading = true;
        FetchFailed = false;
        try
        {
            Fetched = await View.Texts.Get(Node.Key);
        }
        catch (AccessTokenNotAvailableException exception)
        {
            exception.Redirect();
        }
        catch (Exception)
        {
            // not read: the hashes stay, and the next opening asks again
            FetchFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Each time the row is opened, before its own text is fetched; a row that needs another row's text asks for it here.</summary>
    protected virtual Task OnOpenedAsync() => Task.CompletedTask;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.Equals(View.FocusKey, Node.Key, StringComparison.Ordinal))
        {
            View.FocusKey = null;
            try
            {
                await Element.FocusAsync();
            }
            catch (JSException)
            {
                // focus is a convenience: the row is open all the same
            }
        }
    }
}
