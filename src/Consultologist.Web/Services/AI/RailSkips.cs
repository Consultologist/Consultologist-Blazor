namespace Consultologist.Web.Services.AI;

/// <summary>
/// #1330: a folder item skipped by its node-gated if (v29, #1279) is skipped by its
/// LOOP — the record keys it <c>&lt;loop&gt;:&lt;item&gt;</c> — and the live
/// block-skipped event names the block, <c>&lt;result&gt;:&lt;loop&gt;:&lt;item&gt;</c>.
/// The rail's skip set is keyed the record's way; this reads a block id into it.
/// </summary>
public static class RailSkips
{
    /// <summary><c>loop:item</c> from a block id's last two segments, or null when it has fewer than two.</summary>
    public static string? LoopItemKeyOf(string blockId) => Consultologist.PackageFormat.WorkflowItemPath.LoopItemOfBlock(blockId);
}
