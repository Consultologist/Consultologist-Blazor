using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 07 § 4.4; part 09 § 1.2 step 4; D2): this engine's break points, each the version
/// of a locked component at which its output changed. Compiled in, not a setting: one is added only by an upgrade that
/// went through the corpus re-render, which is a code change. None yet.
/// </summary>
public static class EngineBreakPoints
{
    public static readonly IReadOnlyList<BreakPoint> All = [];
}
