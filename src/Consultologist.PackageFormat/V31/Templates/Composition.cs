using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.3, 7.2, 8.3; D2, D3, D12): what one composition is asked: the entry
/// <paramref name="EntryKey"/> (local to <paramref name="Function"/> inside one), the data it reads (<paramref name="Scope"/>,
/// rooted keys as the run holds them), each decided site of the entry's <c>placements</c> (<c>part.&lt;id&gt;</c>,
/// <c>snippet.&lt;id&gt;</c>, <c>macro.&lt;id&gt;</c>, <c>macro-folder.&lt;id&gt;</c>) to its result in
/// <paramref name="Placed"/> (a site absent from it is not placed), and the step budget <paramref name="StepLimit"/>.
/// </summary>
public sealed record ComposeRequest(string EntryKey, string? Function, JsonObject Scope, IReadOnlyDictionary<string, bool> Placed, int StepLimit);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.3, 4, 5.5, 8.8; part 08 §§ 4.2, 4.3, 8.2, 9; D12): what composing a body gave.
/// <paramref name="Text"/> is the sent text, <c>null</c> exactly when <paramref name="Failure"/> (the sentence the node fails
/// with) is set or <paramref name="TooManySteps"/> (S05-062, said by the runner). <paramref name="NeutralText"/> is the
/// run-neutral render (each spot's output replaced at its place by its marker or the read node's run-neutral text), from
/// the same render, <c>null</c> when no run spot was filled; <paramref name="Spots"/> the spots filled, in the order of the
/// text (one per iteration of a <c>for</c> holding one);
/// <paramref name="Renders"/> one sub-row per renderer pipe step run, in the order run (an offered macro's body's steps
/// numbered after the entry's own); <paramref name="Macros"/> the macros offered, in prompt order; <paramref name="Units"/>
/// each snippet and part placed, in order of appearance.
/// </summary>
public sealed record Composition(
    string? Text,
    string? NeutralText,
    string? Failure,
    bool TooManySteps,
    IReadOnlyList<ComposedSpot> Spots,
    IReadOnlyList<ComposedRender> Renders,
    IReadOnlyList<OfferedMacro> Macros,
    IReadOnlyList<ComposedUnit> Units)
{
    /// <summary>
    /// #1505 ruling 6 (R14): the type of the exception the engine met reading one of the texts composed (A08-006), which
    /// failed the composition; never its message or the text. For the caller to log; not serialised.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EngineFault { get; init; }

    /// <summary>
    /// #1506 (area 09; part 08 §§ 4.2, 11; D2): each renderer step's input and output text, in the order of
    /// <see cref="Renders"/>: produced text, under the 7-day retention, which never enters a row. Not serialised.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<RenderedText> RenderTexts { get; init; } = [];

    /// <summary>
    /// #1506 (area 09; part 08 § 10; part 06 § 9): each output tag of the composed body, in source order (package text).
    /// A placed unit's tags are not sites of their own: they are substituted at the site of the tag that places them.
    /// </summary>
    public IReadOnlyList<ComposedSite> Sites { get; init; } = [];

    /// <summary>
    /// #1506 (area 09; part 08 §§ 9, 10; R10, D5, D6): each evaluation of a site that wrote, in the order of the text (so in
    /// order and never overlapping): where its text sits in <see cref="Text"/>, in runes. Each holds its
    /// <see cref="Substitution.Value"/>, produced text, so the list is not serialised; the row takes all but the value.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Substitution> Substitutions { get; init; } = [];
}

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 10): one site of a deliverable's template: its 1-based number in source order,
/// <paramref name="At"/> the tag's <c>&lt;line&gt;:&lt;column&gt;</c> (1-based) in the body, <paramref name="Expr"/> the
/// tag's expression as written, and <paramref name="Loop"/> the nearest template <c>for</c> around it
/// (<c>&lt;variable&gt; in &lt;source&gt;</c> as written), <c>null</c> in none. Package text.
/// </summary>
public sealed record ComposedSite(int Site, string At, string Expr, string? Loop);

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 9, 10; part 06 § 9; R10, D5, D6): one evaluation of a site.
/// <paramref name="Site"/> is the site's number (for a tag inside a placed unit, the placing tag's); <paramref name="Pass"/>
/// one 1-based number per template <c>for</c> around it, outermost first, <c>null</c> in none; <paramref name="Reads"/> the
/// path it reads (its head rooted through <c>names</c>; a run spot's what the spot reads); <paramref name="Hash"/> the hash
/// of the text it wrote; <paramref name="Start"/> and <paramref name="Size"/> where that text sits in the composed text, in
/// runes (<c>0</c> for a value that wrote nothing); <paramref name="Renders"/> the last renderer step of its chain as
/// <c>render:&lt;n&gt;</c> (the row names it by the row's key), <c>null</c> for none; and <paramref name="Value"/> the text
/// written, after escaping: produced text, which never enters a row.
/// </summary>
public sealed record Substitution(int Site, IReadOnlyList<int>? Pass, string Reads, string Hash, int Start, int Size, string? Renders, string Value)
{
    /// <inheritdoc/>
    public bool Equals(Substitution? other) =>
        other is not null && Site == other.Site && Reads == other.Reads && Hash == other.Hash && Start == other.Start && Size == other.Size
        && Renders == other.Renders && Value == other.Value
        && (Pass == null ? other.Pass == null : other.Pass != null && Pass.SequenceEqual(other.Pass));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Site, Reads, Hash, Start, Size, Renders, Value);
}

/// <summary>#1506 (specVersion 31, area 09; part 08 § 4.2; D2): one renderer step's input (its text, or its canonical JSON for data) and its output, as produced text.</summary>
public sealed record RenderedText(string Input, string Output);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 5.5; part 08 § 4.2): one renderer pipe step run, a sub-row of the row that
/// composed it. <paramref name="N"/> is the step's number in the entry's <c>renders</c> (a step inside a <c>for</c> gives
/// one sub-row per iteration with the same number); <paramref name="Renderer"/> <c>renderer.&lt;id&gt;</c>;
/// <paramref name="TemplateHash"/> the renderer's <c>bodyHash</c>; <paramref name="Source"/> the path piped in, or
/// <c>render:&lt;n&gt;</c> of the step before it in a chain; <paramref name="InputHash"/> the hash of the piped value (of
/// its text for text, of its canonical JSON for data); <paramref name="InputSize"/> <c>"&lt;n&gt; items"</c> for a list,
/// <c>"&lt;n&gt; concepts"</c> for a <c>concept_list</c>, else <c>null</c>; <paramref name="OutputHash"/> and
/// <paramref name="OutputLength"/> (in runes, R10) of the rendered text; <paramref name="RunDependent"/> whether the step
/// is in a run spot's pipeline. #1506 (area 09; D5): <paramref name="Pass"/> is one 1-based number per template <c>for</c>
/// around the step, outermost first; <c>null</c> in none.
/// </summary>
public sealed record ComposedRender(
    int N,
    string Renderer,
    string TemplateHash,
    string Source,
    string InputHash,
    string? InputSize,
    string OutputHash,
    int OutputLength,
    bool RunDependent,
    IReadOnlyList<int>? Pass = null);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 9): a snippet or a part placed (<paramref name="Site"/>, as in <c>placements</c>),
/// with the hash of its rendered text. #1506 (area 09; R9): <paramref name="Sent"/> is <c>false</c> for a snippet
/// substituted into a macro's body, which is not sent to the model.
/// </summary>
public sealed record ComposedUnit(string Site, string TextHash, bool Sent = true);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.3, 8.4, 8.8; part 08 § 4.3): a macro offered in the prompt: its
/// <paramref name="Site"/>, the <paramref name="Marker"/> the model is to write, whether it is a <paramref name="Block"/>,
/// its rendered <paramref name="Body"/> as it is substituted (escaped as plain text for a <c>plaintext</c> macro in a
/// Markdown node, D14) and that text's <paramref name="ContentHash"/>, and the profile fields the body read
/// (<paramref name="ProfileReads"/>, each by the field's name, <c>signature</c> for <c>profile.signature</c>). #1506 (area 09;
/// R7; part 08 §§ 4.3, 8.2): a body's <c>run.*</c> is filled directly, so its run spots are its own: <paramref name="Spots"/>,
/// at their places in <paramref name="Body"/> (in runes), and <paramref name="NeutralText"/>, the body with each left as its
/// <c>{{run:&lt;n&gt;}}</c> marker, <c>null</c> when it has none.
/// </summary>
public sealed record OfferedMacro(
    string Site, string Marker, bool Block, string Body, string ContentHash, IReadOnlyList<ProfileRead> ProfileReads,
    string? NeutralText = null, IReadOnlyList<ComposedSpot>? Spots = null);

/// <summary>#1504 (specVersion 31, area 07; part 08 § 9): one <c>profile.*</c> field a macro body read, by hash.</summary>
public sealed record ProfileRead(string Field, string Hash);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; part 08 § 8.2; D4): one run spot filled: its 1-based
/// <paramref name="Site"/>, what it <paramref name="Reads"/>, where its output stands in the sent text
/// (<paramref name="Start"/>, <paramref name="Length"/>; #1506, R10: in runes), the output's hash, the output itself only
/// when the spot reads a raw <c>run.*</c> fact (else <c>null</c>: it follows 7-day retention), and the hash of the value
/// read (of its canonical JSON). #1506 (area 09; D5): <paramref name="Pass"/> is one 1-based number per template
/// <c>for</c> around the spot, outermost first; <c>null</c> in none.
/// </summary>
public sealed record ComposedSpot(int Site, string Reads, int Start, int Length, string OutputHash, string? Output, string ValueHash, IReadOnlyList<int>? Pass = null);
