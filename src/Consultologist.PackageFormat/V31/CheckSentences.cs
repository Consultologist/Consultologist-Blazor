namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § Refusals, Checks): the publish sentences of <c>checks/</c> and of what gates
/// a deliverable, one method each, named for the rule it says, as <see cref="ResolveSentences"/> does for area 05. Each is
/// the row of <c>v31/sentences.json</c> (<c>S06-NNN</c>) or the area's own <c>A08-NNN</c> with its placeholders filled in,
/// character for character. No sentence carries a value of an input, a model or a profile: only package text, names and
/// paths.
/// </summary>
internal static class CheckSentences
{
    /// <summary>S06-009 (R-C2): a check under <c>checks/</c> whose <c>format</c> is not <c>check_result</c>; <paramref name="format"/> as written.</summary>
    public static SourceRefusal HasFormat(string id, string format, string path) =>
        new("S06-009", $"Check '{id}' has format {format}; a check returns check_result.", path);

    /// <summary>S06-010 (R-C3): a leaf under <c>checks/</c> of a kind other than <c>computed</c> (<c>agent</c>, <c>rendered</c>).</summary>
    public static SourceRefusal HasKind(string id, string kind, string path) =>
        new("S06-010", $"Check '{id}' has kind {kind}; a check is computed.", path);

    /// <summary>S06-011 (R-C4, D17): a <c>while</c> (its <c>index.json</c>) or a <c>call</c> (its file) under <c>checks/</c>; <paramref name="path"/> is that file, the sentence's <c>&lt;Path&gt;</c>.</summary>
    public static SourceRefusal HoldsKind(string path, string kind) =>
        new("S06-011", $"{path} in checks/ is {kind}; checks/ holds checks and the loops over them.", path);

    /// <summary>S06-014 (R-C7, D17): a <c>for</c> under <c>checks/</c> whose <c>in</c> (<paramref name="source"/>, as written) is not a loop's <c>.items</c>.</summary>
    public static SourceRefusal LoopRunsOver(string id, string source, string path) =>
        new("S06-014", $"Check loop '{id}' runs over {source}; a check loop runs over a production loop's .items.", path);

    /// <summary>S06-035 (R-C10, D18): a check that <paramref name="deliverable"/>'s <c>check</c> names, whose <c>if</c> reads <paramref name="readPath"/>, which can be null.</summary>
    public static SourceRefusal GateIfReadsNullable(string id, string deliverable, string readPath, string path) =>
        new("S06-035", $"Check '{id}' gates deliverable '{deliverable}', and its if reads {readPath}, which can be null; a gate check's if reads values that are always set.", path);

    /// <summary>A08-001 (R-C1): a deliverable's <c>check</c> (<paramref name="reference"/>, as written) that names no check at the top of <c>checks/</c>.</summary>
    public static SourceRefusal CheckedBy(string deliverable, string reference, string path) =>
        new("A08-001", $"Deliverable '{deliverable}' is checked by {reference}; a deliverable's check names a check.*.", path);
}
