using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10): one file of the 31 set, held as a model until the end of the run: a plain JSON file
/// (<see cref="Body"/> null: an <c>index.json</c>, a format, a test) or a file with a header and a body (a node, a
/// snippet, a macro, a deliverable). A pass edits <see cref="Json"/> in place; <see cref="Migrated.Files"/> serialises it once.
/// </summary>
internal sealed class MigratedFile(JsonObject json, string? body)
{
    /// <summary>The whole file (a plain JSON file) or the header (a <c>.md</c> file).</summary>
    public JsonObject Json { get; } = json;

    /// <summary>The body after the header; null for a plain JSON file.</summary>
    public string? Body { get; set; } = body;
}

/// <summary>
/// #1507 (specVersion 31, area 10): where 31 reads a condition site from (Task 7 fills it as it writes the nodes): the
/// loop element's format, whether that element is a snippet folder's item, and whether <c>pass.number</c> is readable.
/// </summary>
internal sealed record SitePlace(JsonNode? Item, bool ItemIsFolderItem, bool InWhile);

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2, D1): the working set of one migration, which every pass after the chain
/// shares. It holds the 30 package as read and checked (<see cref="Manifest30"/>, <see cref="Root30"/>,
/// <see cref="Files30"/>, <see cref="Resolved30"/>, <see cref="Walk30"/>), the 31 set as a model (<see cref="Manifest"/>
/// and <see cref="Sources"/>, path to <see cref="MigratedFile"/>, the nine folder indexes among them from the start), and
/// what the run says (<see cref="Notice"/>, <see cref="Suggest"/>, <see cref="HandEdit"/>, the report's lists and
/// <see cref="Keys"/>). A pass reads the 30 model, adds or edits 31 files through <see cref="Put(string, JsonObject)"/>,
/// <see cref="Put(string, JsonObject, string)"/> and <see cref="Items"/>, and says what it did; nothing is serialised until
/// <see cref="Result"/>, so a later pass (a rename, a macro's input) still edits every file an earlier one wrote.
/// </summary>
internal sealed class Migrated
{
    /// <summary>The nine top-level folders, in part 01 § 2's order (part 10 § 1.2 row 1).</summary>
    public static readonly IReadOnlyList<string> TopFolders =
        ["nodes", "checks", "functions", "renderers", "snippets", "macros", "formats", "deliverables", "tests"];

    private static readonly JsonSerializerOptions Indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        NewLine = "\n",
    };

    private static readonly JsonSerializerOptions OneLine = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly IReadOnlyDictionary<string, string> NoCatalogSchemas = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly List<string> _notices = [];
    private readonly List<string> _handEdits = [];

    private Migrated(
        WorkflowPackageManifest manifest30,
        JsonObject root30,
        IReadOnlyDictionary<string, string> files30,
        WorkflowResolvedTexts resolved30,
        WorkflowDerivedEdges.Walk walk30,
        Migration31Options options)
    {
        Manifest30 = manifest30;
        Root30 = root30;
        Files30 = files30;
        Resolved30 = resolved30;
        Walk30 = walk30;
        Options = options;

        foreach (var folder in TopFolders)
        {
            var index = new JsonObject { ["description"] = null, ["items"] = new JsonArray() };
            if (folder == "deliverables")
            {
                index["delivery"] = new JsonArray();
                index["bundle"] = null;
            }

            Put($"{folder}/{WorkflowDataResolver.IndexFileName}", index);
        }
    }

    /// <summary>The 30 manifest as <see cref="WorkflowPackageManifestJson.Read"/> reads it, its macros' header facts filled in (<see cref="Resolved30"/>).</summary>
    public WorkflowPackageManifest Manifest30 { get; }

    /// <summary>The 30 manifest as strict JSON, member order as written. Read-only: <c>DeepClone</c> before placing a node in a 31 tree.</summary>
    public JsonObject Root30 { get; }

    /// <summary>The 30 files (without <c>manifest.json</c>), <c>\n</c>-normalised and without a byte-order mark, headers included.</summary>
    public IReadOnlyDictionary<string, string> Files30 { get; }

    /// <summary>The 30 texts resolved: the bodies with their headers stripped, and each snippet and macro header by path.</summary>
    public WorkflowResolvedTexts Resolved30 { get; }

    /// <summary>The 30 walk: every template and every condition site, with the context 30 typed it in.</summary>
    public WorkflowDerivedEdges.Walk Walk30 { get; }

    public Migration31Options Options { get; }

    /// <summary>The 31 manifest (identity and inputs, part 01 § 3); the layout pass writes it, the inputs pass its <c>inputs</c>.</summary>
    public JsonObject Manifest { get; } = new();

    /// <summary>The 31 files kept as text, path to text: a root <c>README.md</c> (part 01 § 2.3), copied as 30 had it.</summary>
    public SortedDictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

    /// <summary>The 31 files, path to model, in ordinal path order (the manifest apart).</summary>
    public SortedDictionary<string, MigratedFile> Sources { get; } = new(StringComparer.Ordinal);

    /// <summary>Each 30 node id to its 31 rooted key, each deliverable id to <c>deliverable.&lt;id&gt;</c> (Tasks 7, 11).</summary>
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);

    /// <summary>Where 31 reads each 30 node, by 30 id (Task 7), for <see cref="Scope"/>.</summary>
    public Dictionary<string, ConversionRead> NodeReads { get; } = new(StringComparer.Ordinal);

    /// <summary>Each condition site's place, by the site's 30 owner id (Task 7), for <see cref="Scope"/>; none is the package's top level.</summary>
    public Dictionary<string, SitePlace> Places { get; } = new(StringComparer.Ordinal);

    /// <summary>The report's lists (D9), each also in <see cref="Migration31Result.Notices"/>.</summary>
    public List<string> Renames { get; } = [];

    public List<string> MacroInputs { get; } = [];

    public List<string> BlankConversions { get; } = [];

    public List<string> Suggestions { get; } = [];

    public List<string> Reproducible { get; } = [];

    /// <summary>
    /// The 30 package <paramref name="manifestJson"/> and <paramref name="files"/> (the chain's output) read for the
    /// passes, or the refusals that stop the run. Every file is normalised first (<c>\r\n</c> and a lone <c>\r</c> to
    /// <c>\n</c>, no byte-order mark: part 01 § 2.4), then every plain JSON file must be strict JSON (A10-019: .NET reads a
    /// member written twice and throws on its first access, so the hazard is refused here), then the manifest is read
    /// (<see cref="WorkflowPackageManifestJson.Read"/>), its texts resolved (<see cref="WorkflowTextHeaders.Resolve"/>),
    /// whose snippet and macro headers must be strict JSON too (A10-019), and the package validated at 30 against <see cref="Migration31Options.Formats30"/>,
    /// each error a refusal naming the version (A10-017); then it is walked. Package content never throws here (A10-020).
    /// </summary>
    public static (Migrated? Migrated, IReadOnlyList<string> Refusals) Read(
        string manifestJson, IReadOnlyDictionary<string, string> files, Migration31Options options)
    {
        var json = Normalise(manifestJson);
        var normalised = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in files)
            normalised[path] = Normalise(text);

        var unread = new List<string>();
        if (StrictJson.Parse(json) is not { Fault: null, Value: JsonObject root30 })
            return (null, [MigrationSentences.ManifestDoesNotRead]);
        foreach (var (path, text) in normalised.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path.EndsWith(".json", StringComparison.Ordinal))
            {
                if (StrictJson.Parse(text) is { Fault: { } fault } read)
                    unread.Add(MigrationSentences.FileDoesNotRead(path, header: false, fault, read.Detail));
            }
        }

        if (unread.Count > 0)
            return (null, unread);

        try
        {
            WorkflowPackageManifest manifest;
            try
            {
                manifest = WorkflowPackageManifestJson.Read(json, "manifest.json", [WorkflowPackageMigrationChain.Target]);
            }
            catch (Exception ex) when (ex is WorkflowPackageContentException or InvalidOperationException or WorkflowPackageSpecVersionException)
            {
                return (null, [MigrationSentences.AtRung(WorkflowPackageMigrationChain.Target, ex.Message)]);
            }

            var errors = new List<string>();
            var resolved = WorkflowTextHeaders.Resolve(manifest, normalised, errors);

            // The headers of the snippet and macro files the manifest lists (Resolve's, which reads them without
            // JsonNode), never a prompt's: a 30 prompt has no header, and one that opens like one is 30's to refuse.
            foreach (var path in resolved.Headers.Keys.Order(StringComparer.Ordinal))
            {
                if (WorkflowTextHeaders.Split(normalised[path]).HeaderJson is { } header
                    && StrictJson.Parse(header) is { Fault: { } fault } read)
                    unread.Add(MigrationSentences.FileDoesNotRead(path, header: true, fault, read.Detail));
            }

            if (unread.Count > 0)
                return (null, unread);
            if (errors.Count == 0)
                errors.AddRange(WorkflowPackageValidator.Validate(manifest, normalised, NoCatalogSchemas, formats: options.Formats30).Errors);
            if (errors.Count > 0)
                return (null, errors.Select(e => MigrationSentences.AtRung(WorkflowPackageMigrationChain.Target, e)).ToList());

            var walk = WorkflowDerivedEdges.WalkOf(manifest, normalised, options.Formats30);
            return (new Migrated(resolved.Manifest, root30, normalised, resolved, walk, options), []);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The 30 reader is the engine's and the Editor's, called as they call it; on content it was not written for
            // it may throw. The migration never throws on package content: the run is refused.
            return (null, [MigrationSentences.PackageDoesNotRead]);
        }
    }

    /// <summary>Part 01 § 2.4: <c>\r\n</c> and a lone <c>\r</c> become <c>\n</c>; a leading byte-order mark is dropped.</summary>
    public static string Normalise(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];
        return text.Contains('\r', StringComparison.Ordinal) ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
    }

    /// <summary>Whether <paramref name="text"/> holds a <c>\r</c> that is not part of a <c>\r\n</c> (A10-026 when normalised).</summary>
    public static bool HasLoneCarriageReturn(string text) =>
        text.Contains('\r', StringComparison.Ordinal) && text.Replace("\r\n", "\n", StringComparison.Ordinal).Contains('\r', StringComparison.Ordinal);

    /// <summary>One plain JSON file's text: indented, every member as the model holds it, <c>\n</c> line endings and a final <c>\n</c>.</summary>
    public static string Json(JsonObject json) => json.ToJsonString(Indented) + "\n";

    /// <summary>A file with a header (part 01 § 4): <c>---</c>, the header on one line, <c>---</c>, then <paramref name="body"/> as it is.</summary>
    public static string Md(JsonObject header, string body) => "---\n" + header.ToJsonString(OneLine) + "\n---\n" + body;

    /// <summary>Adds or replaces the plain JSON file at <paramref name="path"/>.</summary>
    public MigratedFile Put(string path, JsonObject json) => Sources[path] = new MigratedFile(json, null);

    /// <summary>Adds or replaces the file with a header at <paramref name="path"/>.</summary>
    public MigratedFile Put(string path, JsonObject header, string body) => Sources[path] = new MigratedFile(header, body);

    /// <summary>The <c>items</c> of <paramref name="folder"/>'s <c>index.json</c> (<c>nodes</c>, <c>snippets/standards</c>), which must have been put.</summary>
    public JsonArray Items(string folder) => Sources[$"{folder}/{WorkflowDataResolver.IndexFileName}"].Json["items"]!.AsArray();

    /// <summary>A notice that needs no edit (a rename, a conversion): it goes into <see cref="Migration31Result.Notices"/>.</summary>
    public void Notice(string notice) => _notices.Add(notice);

    /// <summary>A suggestion (A10-021, part 02 § 11): a notice, listed in the report's suggestions.</summary>
    public void Suggest(string suggestion)
    {
        _notices.Add(suggestion);
        Suggestions.Add(suggestion);
    }

    /// <summary>A <c>!x</c> on text written <c>blank(x)</c> (A10-008, § 2.3): a notice, listed in the report's blank conversions.</summary>
    public void Blank(string notice)
    {
        _notices.Add(notice);
        BlankConversions.Add(notice);
    }

    /// <summary>
    /// A hand edit (S10-002, part 10 § 1.4): what could not be decided in <paramref name="file"/>'s
    /// <paramref name="member"/>, which the pass has written in a form the 31 checker refuses. Exactly one notice per edit.
    /// </summary>
    public void HandEdit(string file, string member, string reason)
    {
        var notice = MigrationSentences.HandEdit(file, member, reason);
        _notices.Add(notice);
        _handEdits.Add(notice);
    }

    /// <summary>The package's own 31 formats as written so far (<c>formats/&lt;id&gt;.json</c>), in <c>formats/index.json</c>'s order.</summary>
    public IReadOnlyList<SourceFormat> PackageFormats() =>
        Items("formats")
            .Select(item => (string)item!)
            .Where(id => Sources.ContainsKey($"formats/{id}.json"))
            .Select(id => new SourceFormat(id, $"formats/{id}.json", Sources[$"formats/{id}.json"].Json))
            .ToList();

    /// <summary>
    /// What a 30 condition at <paramref name="site"/> reads at 31, for the converter (part 10 § 2.1): every input's 31
    /// format, required exactly when its <c>required</c> is <c>true</c> (unsaid is nullable); every node a pass has placed
    /// (<see cref="NodeReads"/>, Task 7, each <see cref="ConversionRead.AlwaysValued"/> as that pass says); the site's
    /// place (<see cref="Places"/>, keyed by the site's owner); and the package's formats with the registry's.
    /// </summary>
    public ConversionScope Scope(WorkflowDerivedEdges.WalkedCondition site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return Scope(site.OwnerId);
    }

    /// <summary>
    /// <see cref="Scope(WorkflowDerivedEdges.WalkedCondition)"/> at the place of <paramref name="ownerId"/> (a node, a
    /// snippet; none is the package's top level): what a condition owned by it reads.
    /// </summary>
    public ConversionScope Scope(string? ownerId)
    {
        var inputs = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (Manifest["inputs"] is JsonObject declared)
        {
            foreach (var (id, input) in declared)
            {
                if (input?["format"] is { } format)
                    inputs[id] = format.DeepClone();
                if (input?["required"] is JsonValue value && value.TryGetValue<bool>(out var isRequired) && isRequired)
                    required.Add(id);
            }
        }

        var place = ownerId is null ? null : Places.GetValueOrDefault(ownerId);
        return new ConversionScope(
            inputs,
            new Dictionary<string, ConversionRead>(NodeReads, StringComparer.Ordinal),
            place?.Item?.DeepClone(),
            place?.ItemIsFolderItem ?? false,
            place?.InWhile ?? false,
            FormatSet.ForPackage(Options.Registry, PackageFormats())) { RequiredInputs = required };
    }

    /// <summary>The 31 files serialised (without <c>manifest.json</c>): each of <see cref="Texts"/> as it is, each plain file by <see cref="Json"/>, each other by <see cref="Md"/>.</summary>
    public Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in Texts)
            files[path] = text;
        foreach (var (path, file) in Sources)
            files[path] = file.Body is null ? Json(file.Json) : Md(file.Json, file.Body);
        return files;
    }

    /// <summary>The run's result: the 31 manifest and files, <paramref name="chainNotices"/> then this run's notices, and the report.</summary>
    public Migration31Result Result(IReadOnlyList<string> chainNotices) =>
        new(Json(Manifest), Files(), [.. chainNotices, .. _notices], [])
        {
            HandEdits = _handEdits.ToList(),
            Report = new MigrationNotes(Renames.ToList(), MacroInputs.ToList(), BlankConversions.ToList(), Suggestions.ToList(), Reproducible.ToList()),
            Keys = new Dictionary<string, string>(Keys, StringComparer.Ordinal),
        };
}
