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
    IReadOnlyList<ComposedUnit> Units);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 5.5; part 08 § 4.2): one renderer pipe step run, a sub-row of the row that
/// composed it. <paramref name="N"/> is the step's number in the entry's <c>renders</c> (a step inside a <c>for</c> gives
/// one sub-row per iteration with the same number); <paramref name="Renderer"/> <c>renderer.&lt;id&gt;</c>;
/// <paramref name="TemplateHash"/> the renderer's <c>bodyHash</c>; <paramref name="Source"/> the path piped in, or
/// <c>render:&lt;n&gt;</c> of the step before it in a chain; <paramref name="InputHash"/> the hash of the piped value (of
/// its text for text, of its canonical JSON for data); <paramref name="InputSize"/> <c>"&lt;n&gt; items"</c> for a list,
/// <c>"&lt;n&gt; concepts"</c> for a <c>concept_list</c>, else <c>null</c>; <paramref name="OutputHash"/> and
/// <paramref name="OutputLength"/> (in characters) of the rendered text; <paramref name="RunDependent"/> whether the step
/// is in a run spot's pipeline.
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
    bool RunDependent);

/// <summary>#1504 (specVersion 31, area 07; part 05 § 9): a snippet or a part placed (<paramref name="Site"/>, as in <c>placements</c>), with the hash of its rendered text.</summary>
public sealed record ComposedUnit(string Site, string TextHash);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.3, 8.4, 8.8; part 08 § 4.3): a macro offered in the prompt: its
/// <paramref name="Site"/>, the <paramref name="Marker"/> the model is to write, whether it is a <paramref name="Block"/>,
/// its rendered <paramref name="Body"/> as it is substituted (escaped as plain text for a <c>plaintext</c> macro in a
/// Markdown node, D14) and that text's <paramref name="ContentHash"/>, and the profile fields the body read
/// (<paramref name="ProfileReads"/>, each by the field's name, <c>signature</c> for <c>profile.signature</c>).
/// </summary>
public sealed record OfferedMacro(string Site, string Marker, bool Block, string Body, string ContentHash, IReadOnlyList<ProfileRead> ProfileReads);

/// <summary>#1504 (specVersion 31, area 07; part 08 § 9): one <c>profile.*</c> field a macro body read, by hash.</summary>
public sealed record ProfileRead(string Field, string Hash);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; part 08 § 8.2; D4): one run spot filled: its 1-based
/// <paramref name="Site"/>, what it <paramref name="Reads"/>, where its output stands in the sent text
/// (<paramref name="Start"/>, <paramref name="Length"/>, in UTF-16 code units as <see cref="string"/> indexes it), the
/// output's hash, the output itself only when the spot reads a raw <c>run.*</c> fact (else <c>null</c>: it follows 7-day
/// retention), and the hash of the value read (of its canonical JSON).
/// </summary>
public sealed record ComposedSpot(int Site, string Reads, int Start, int Length, string OutputHash, string? Output, string ValueHash);
