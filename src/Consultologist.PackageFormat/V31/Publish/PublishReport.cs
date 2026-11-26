using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 9; D10): the publish report, <see cref="FileName"/> in the version's folder and
/// the publish endpoint's answer, published or refused. Not evidence: records never pin it. A refused report has no time and
/// no <c>resolveHash</c> or <c>lockHash</c> (publish stopped before step 4). A refusal's <see cref="SourceRefusal.EngineFault"/>
/// is for the caller's log and is never written here, and no sentence carries a value from an input or a test.
/// </summary>
public sealed record PublishReport(
    string Name,
    string Version,
    bool Published,
    DateTimeOffset? PublishedAtUtc,
    string PublishedBy,
    string SourceHash,
    string? ResolveHash,
    string? LockHash,
    IReadOnlyList<string> Normalised,
    IReadOnlyList<SourceRefusal> Refusals,
    IReadOnlyList<SourceRefusal> Warnings,
    IReadOnlyList<TestResult> Tests)
{
    /// <summary>The report's file name in a version's folder.</summary>
    public const string FileName = "publish-report.json";

    /// <summary>The report's media type.</summary>
    public const string MediaType = "application/vnd.consultologist.publish-report+json";

    /// <summary>The report as part 07 § 9 gives it: every member present, <c>documentVersion</c> 1, <c>outcome</c> <c>"published"</c> or <c>"refused"</c>.</summary>
    public JsonObject ToJson()
    {
        ArgumentNullException.ThrowIfNull(Normalised);
        ArgumentNullException.ThrowIfNull(Refusals);
        ArgumentNullException.ThrowIfNull(Warnings);
        ArgumentNullException.ThrowIfNull(Tests);

        return new JsonObject
        {
            ["documentVersion"] = 1,
            ["package"] = new JsonObject { ["name"] = Name, ["version"] = Version },
            ["outcome"] = Published ? "published" : "refused",
            ["publishedAtUtc"] = PublishedAtUtc is { } at
                ? at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : null,
            ["publishedBy"] = PublishedBy,
            ["sourceHash"] = SourceHash,
            ["resolveHash"] = ResolveHash,
            ["lockHash"] = LockHash,
            ["normalised"] = new JsonArray([.. Normalised.Select(p => (JsonNode?)p)]),
            ["refusals"] = Sentences(Refusals),
            ["warnings"] = Sentences(Warnings),
            ["tests"] = new JsonArray([.. Tests.Select(t => (JsonNode?)t.ToJson())]),
        };
    }

    /// <summary>The report's canonical text (RFC 8785), the bytes that are written and hashed.</summary>
    public string ToCanonical() => CanonicalJson.Write(ToJson());

    /// <summary><c>{ sentence, at }</c> as <c>resolve.json</c>'s warnings write it: <c>at</c> null for the package as a whole.</summary>
    private static JsonArray Sentences(IReadOnlyList<SourceRefusal> items) =>
        new([.. items.Select(i => (JsonNode?)new JsonObject { ["sentence"] = i.Sentence, ["at"] = i.Path.Length == 0 ? null : i.Path })]);
}
