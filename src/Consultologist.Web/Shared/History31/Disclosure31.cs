using Consultologist.Web.Services.Provenance.V31;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Consultologist.Web.Shared.History31;

/// <summary>
/// #1509 (Task 19 fix round 1): the disclosure of a section of History's record view, once: which rows are open, the rows'
/// parents, and following a link to a row (<c>#row-&lt;key&gt;</c>, on arrival, on <c>LocationChanged</c> or through
/// <see cref="RowLinks31"/>). A section (Nodes, Checks, and Deliverables to come) owns one, gives it its tree with
/// <see cref="SetTree"/> each time its record changes, hands it to its rows (<see cref="DisclosureRow31{TView}"/>) as their
/// <c>View</c>, calls <see cref="Arrive"/> once after the first tree, and disposes it. A link to a target the section does not
/// hold opens nothing and answers false. Pure but for the navigation subscription.
/// </summary>
public class Disclosure31 : IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly Action _changed;
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private bool _arrived;

    /// <param name="navigation">Watched for a <c>#row-…</c> fragment.</param>
    /// <param name="changed">Asks the section to draw again (<c>StateHasChanged</c> on the render thread).</param>
    public Disclosure31(NavigationManager navigation, Action changed)
    {
        _navigation = navigation;
        _changed = changed;
        _navigation.LocationChanged += OnLocationChanged;
    }

    public Record31 Record { get; private set; } = default!;

    public RowText31 Texts { get; set; } = default!;

    /// <summary>The row the page was sent to, focused once it is drawn; null when none is waiting.</summary>
    public string? FocusKey { get; set; }

    public bool IsOpen(string key) => _open.Contains(key);

    public void Toggle(string key)
    {
        if (!_open.Remove(key))
        {
            _open.Add(key);
        }

        _changed();
    }

    /// <summary>The section's tree for <paramref name="record"/>: every row's parent is read from it.</summary>
    public void SetTree(Record31 record, IReadOnlyList<TreeNode31> tree)
    {
        Record = record;
        _parents.Clear();
        Walk(tree, null);
    }

    /// <summary>Once, after the first tree: a page opened at a row opens its rows as it is first drawn.</summary>
    public void Arrive()
    {
        if (!_arrived)
        {
            _arrived = true;
            OpenAt(_navigation.Uri);
        }
    }

    /// <summary>Follows a link to a row, a decision or a sub-row of this section and draws; false when it holds none.</summary>
    public bool Reveal(string target)
    {
        if (!Open(target))
        {
            return false;
        }

        _changed();
        return true;
    }

    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    private void Walk(IReadOnlyList<TreeNode31> nodes, string? parent)
    {
        foreach (var node in nodes)
        {
            _parents[node.Key] = parent;
            Walk(node.Children, node.Key);
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (OpenAt(args.Location))
        {
            _changed();
        }
    }

    /// <summary>Opens the rows a <c>#row-&lt;key&gt;</c> fragment of <paramref name="uri"/> names; false when it names none of this section's.</summary>
    private bool OpenAt(string uri)
    {
        var hash = uri.IndexOf('#');
        if (hash < 0)
        {
            return false;
        }

        var fragment = Uri.UnescapeDataString(uri[(hash + 1)..]);
        return fragment.StartsWith("row-", StringComparison.Ordinal) && Open(fragment["row-".Length..]);
    }

    /// <summary>Opens the row holding <paramref name="target"/> and each row it sits in, and asks for it to be focused.</summary>
    private bool Open(string target)
    {
        if (RowHolding(target) is not { } key)
        {
            return false;
        }

        FocusKey = key;
        for (string? at = key; at is not null; at = _parents.GetValueOrDefault(at))
        {
            _open.Add(at);
        }

        return true;
    }

    /// <summary>
    /// The row of this section that holds <paramref name="target"/>: the row itself, a decision's owner, or the row a sub-row
    /// id is under (the longest leading part of the id, cut at a <c>/</c>, that is a row here); null for none.
    /// </summary>
    private string? RowHolding(string target)
    {
        if (_parents.ContainsKey(target))
        {
            return target;
        }

        if (Record.Find(target) is { Section: "decision" } decision)
        {
            var owner = Record31.OwnerOf(decision);
            return _parents.ContainsKey(owner) ? owner : null;
        }

        for (var cut = target.LastIndexOf('/'); cut > 0; cut = target.LastIndexOf('/', cut - 1))
        {
            if (_parents.ContainsKey(target[..cut]))
            {
                return target[..cut];
            }
        }

        return null;
    }
}
