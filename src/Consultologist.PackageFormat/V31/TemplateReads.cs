namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05, D1): what one Scriban body reads, as the dialect (area 07) finds it.
/// <paramref name="Reads"/> are the paths it reads, as written (<c>node.outline.value.points</c>, <c>item.value</c>,
/// <c>param.draft.content</c>, <c>snippet.standards.hpi.content</c>, <c>profile.signature</c>), snippet and macro bodies
/// it places included (part 07 § 2.5); the resolver resolves them where the body is (<see cref="ScopeTree.Resolve"/>).
/// <paramref name="Places"/> are the rooted ids of the macros it places, its parts' text included, in order of
/// appearance (<c>macro.letrozole</c>, <c>macro.tone.formal</c>); <paramref name="Renders"/> each pipe step into a
/// renderer; <paramref name="RunSpots"/> each run spot; <paramref name="PlacesProfileMacro"/> whether a macro it places
/// reads the profile (part 03 § 4.6: where profile-carrying starts).
/// </summary>
public sealed record TemplateReads(
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Places,
    IReadOnlyList<TemplateRender> Renders,
    IReadOnlyList<TemplateRunSpot> RunSpots,
    bool PlacesProfileMacro)
{
    /// <summary>A body that reads nothing, places nothing and renders nothing (an empty body's).</summary>
    public static TemplateReads None { get; } = new([], [], [], [], false);
}

/// <summary>#1502 (specVersion 31, area 05, D1): one pipe step into a renderer (part 07 § 2.5 <c>renders</c>): its 1-based position <paramref name="N"/>, the path it renders and the renderer's id.</summary>
public sealed record TemplateRender(int N, string Source, string Renderer);

/// <summary>#1502 (specVersion 31, area 05, D1): one run spot (part 07 § 2.5 <c>runSpots</c>, part 05 § 4): its 1-based position, where it is (<c>line:column</c>) and the path it reads.</summary>
public sealed record TemplateRunSpot(int Site, string At, string Reads);

/// <summary>
/// #1502 (specVersion 31, area 05, D1): the seam through which area 07 tells the resolver what each Scriban body reads.
/// Until a provider is supplied a non-empty body's reads are <i>unknown</i>, not empty: the flags that depend on it are
/// not judged, and <see cref="PackageDerived.Complete"/> is false.
/// </summary>
public interface ITemplateReads
{
    /// <summary>
    /// What the body of <paramref name="declaration"/> reads: a <see cref="SourceNode"/> (an agent's prompt, a rendered
    /// node's template), a <see cref="SourceDeliverable"/>, or a <see cref="SourceText"/> (a renderer, a snippet, a
    /// macro, a folder's item), as <see cref="SiteContext.Declaration"/> names them; <paramref name="function"/> is the
    /// function it is in, if any. Null: not known.
    /// </summary>
    TemplateReads? Of(object declaration, SourceFunction? function);
}
