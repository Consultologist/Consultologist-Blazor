namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): one refusal of a package source: the id of its sentence
/// (<c>S01-006</c> from <c>v31/sentences.json</c>, or an area sentence <c>A02-NNN</c>), the
/// sentence itself, and the file it is about (<c>""</c> for the package as a whole).
/// </summary>
public sealed record SourceRefusal(string Id, string Sentence, string Path)
{
    /// <summary>
    /// #1505 ruling 6 (R14): for A08-006, the type of the exception the engine met reading the template (never its message
    /// or the text), for the caller to log; <c>null</c> for every other refusal. Not serialised, and not part of the sentence.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EngineFault { get; init; }
}
