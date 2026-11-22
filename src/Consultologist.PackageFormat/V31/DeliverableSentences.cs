namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § Refusals, Deliverables): the publish sentences of a deliverable's header,
/// its body's reads and its delivery folder, one method each, named for the rule it says, as <see cref="CheckSentences"/>
/// does for the checks. Each is the row of <c>v31/sentences.json</c> (<c>S06-NNN</c>) or the area's own <c>A08-NNN</c>
/// with its placeholders filled in, character for character. A deliverable is named by its id (its file name), a folder
/// by its path; a value is the package's text as written. No sentence carries a value of an input, a model or a profile.
/// </summary>
internal static class DeliverableSentences
{
    /// <summary>S06-018 (R-D2): a deliverable whose body (or a snippet it places) reads <c>check.&lt;check&gt;</c>, the check its own <c>check</c> names.</summary>
    public static SourceRefusal ReadsOwnCheck(string id, string check, string path) =>
        new("S06-018", $"Deliverable '{id}' reads check.{check}, which checks {id}: a deliverable can't read its own check.", path);

    /// <summary>S06-020 (R-D4): a deliverable whose <c>format</c> (<paramref name="format"/>, as written) is not <c>markdown_block</c>, <c>plaintext</c> or an object format.</summary>
    public static SourceRefusal HasFormat(string id, string format, string path) =>
        new("S06-020", $"Deliverable '{id}' has format {format}; a deliverable is markdown_block, plaintext or an object format.", path);

    /// <summary>S06-025 (R-D10): a deliverable whose <c>whenEmpty</c> (<paramref name="value"/>, as written) is not <c>release</c>, <c>hold</c> or <c>fail</c>.</summary>
    public static SourceRefusal HasWhenEmpty(string id, string value, string path) =>
        new("S06-025", $"Deliverable '{id}' has whenEmpty {value}; write release, hold or fail.", path);

    /// <summary>S06-026 (R-D11): a delivery folder (<paramref name="folderPath"/>, <c>deliverables</c> or <c>deliverables/&lt;id&gt;</c>) whose <c>delivery</c> lists <paramref name="kind"/>, which is no kind; said of its <c>index.json</c>.</summary>
    public static SourceRefusal HasDeliveryKind(string folderPath, string kind) =>
        new("S06-026", $"Deliverables folder '{folderPath}' has delivery kind {kind}; the kinds are email, pdf, emr and zip.",
            SourceLayout.Join(folderPath, "index.json"));

    /// <summary>A08-002 (part 06 § 8): a delivery folder whose <c>bundle</c> (<paramref name="value"/>, as written) is neither <c>"zip"</c> nor <c>null</c>; said of its <c>index.json</c>.</summary>
    public static SourceRefusal HasBundle(string folderPath, string value) =>
        new("A08-002", $"Deliverables folder '{folderPath}' has bundle {value}; write \"zip\" or null.", SourceLayout.Join(folderPath, "index.json"));
}
