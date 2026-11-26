using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1508 (Task 36): what <c>scripts/bulk-publish-v31.cs</c> was given: the operator's folder, the engine's URL (its base;
/// the script calls only <c>&lt;engine&gt;/api/Admin/MigratePackage</c>) and whether to publish (without
/// <c>--publish</c>, a dry run).
/// </summary>
public sealed record BulkPublishArguments(string Folder, Uri Engine, bool Publish);

/// <summary>
/// #1508 (specVersion 31, area 11, Task 36; part 10 § 1.5; R11, R18, R21; ruled on #1508, comments 5 to 8; the final
/// review of Part F): the operator's bulk publish, here so it is tested; the script reads its arguments and the token, and
/// calls <see cref="RunAsync"/>. It asks the engine for every account's active account package below 31
/// (<c>GET Admin/MigratePackage</c>), reads each one's <c>options.json</c> and <c>hand-edits/</c> from
/// <c>&lt;operator folder&gt;/&lt;owner&gt;/&lt;name&gt;/</c> (a package with no folder has neither: the defaults, no edits),
/// checks every folder before it sends anything (a folder holding either that names no listed package and no carried row is
/// <see cref="Fault"/>, nothing sent), then sends each package (<c>POST Admin/MigratePackage</c>), one at a time, in the
/// engine's order. After every row it writes <see cref="RecordFile"/> (one row per package: the engine's answer and the
/// time) and <see cref="OwnersFile"/> (per owner: the packages published, each with the notices the owner must see; the
/// packages not migrated, each with its reason, S10-005 and A11-015; the packages <c>unknown</c>, each with A11-017), each
/// through a temporary file moved over it, so an interruption loses at most the row of the package in flight. A package
/// the engine does not answer for (a fault, a timeout, a lost answer) is recorded <c>unknown: &lt;fault&gt;</c> and stops
/// the run: in a <c>--publish</c> run it may have been published. A rerun carries forward every row of the existing record
/// whose outcome is <c>published</c> or <c>unknown</c> and whose package it did not answer (the latest row for each), and
/// builds both files from the merged rows; an existing record it cannot read, or one written for another engine, is
/// <see cref="Fault"/>, nothing sent and neither file written. Exits <see cref="Clean"/> when every package published or
/// would, <see cref="NotMigratedExit"/> when any is not migrated (or could not be read), <see cref="Fault"/> on a usage
/// error, a folder it cannot read or that names no package, an engine that does not answer as the route does, or an
/// <c>unknown</c> row this run did not answer. It talks only to the engine it is given (<c>https</c>, or <c>http</c> to this
/// machine).
/// </summary>
public static class BulkPublishScript
{
    /// <summary>Exit 0: every package published, or would.</summary>
    public const int Clean = 0;

    /// <summary>Exit 1: a package is not migrated, or an account's active package could not be read.</summary>
    public const int NotMigratedExit = 1;

    /// <summary>Exit 2: a usage error, a folder that does not read or names no package, a record of another engine, an engine fault, or an <c>unknown</c> row.</summary>
    public const int Fault = 2;

    /// <summary>The record, written in the operator's folder.</summary>
    public const string RecordFile = "bulk-publish-record.json";

    /// <summary>The owners' list, for the before-and-after notification (#1447), written in the operator's folder.</summary>
    public const string OwnersFile = "owners.json";

    /// <summary>A package's hand edits, under its folder: each file replaces or adds its path; <c>&lt;path&gt;.deleted</c> removes <c>&lt;path&gt;</c>.</summary>
    public const string HandEditsFolder = "hand-edits";

    /// <summary>The suffix of a file that removes its path.</summary>
    public const string DeletedSuffix = ".deleted";

    /// <summary>The engine's route, below its base.</summary>
    public const string Route = "api/Admin/MigratePackage";

    /// <summary>The variable that holds the operator account's bearer token.</summary>
    public const string TokenVariable = "ACCESS_TOKEN";

    /// <summary>The usage line.</summary>
    public const string Usage = "usage: dotnet run -v q --file scripts/bulk-publish-v31.cs -- <operator folder> --engine <url> [--publish]";

    private const string NotMigratedPrefix = "not migrated: ";

    /// <summary>The outcome of a row whose package the engine did not answer for (it may have been published): <c>unknown: &lt;fault&gt;</c>.</summary>
    private const string UnknownPrefix = "unknown: ";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The arguments of <paramref name="args"/>, or null and why not.</summary>
    public static (BulkPublishArguments? Arguments, string? Usage) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? folder = null;
        Uri? engine = null;
        var publish = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--publish":
                    publish = true;
                    break;
                case "--engine":
                    if (engine != null)
                        return (null, "--engine is given twice.");
                    if (i + 1 >= args.Count
                        || !Uri.TryCreate(args[++i], UriKind.Absolute, out var url)
                        || url.Scheme is not ("https" or "http")
                        || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
                        return (null, "--engine takes the engine's URL (https://<host>), with no query.");
                    // the bearer token is sent to it: http:// only to this machine
                    if (url.Scheme == "http" && url.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
                        return (null, "--engine takes https:// (http:// only for localhost, 127.0.0.1 or [::1]): the token is sent to it.");
                    engine = url;
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                        return (null, $"{args[i]} is not an option of the bulk publish.");
                    if (folder != null)
                        return (null, $"'{args[i]}' is a second operator folder.");
                    folder = args[i];
                    break;
            }
        }

        if (folder == null)
            return (null, "The operator folder is not given.");
        if (engine == null)
            return (null, "--engine is not given.");
        return (new BulkPublishArguments(folder, engine, publish), null);
    }

    /// <summary>
    /// Runs the bulk publish of <paramref name="arguments"/> through <paramref name="http"/> (each request with
    /// <paramref name="token"/> as its bearer token when given), the time each row was answered from <paramref name="clock"/>,
    /// one line per package and the summary to <paramref name="output"/>, faults to <paramref name="error"/>; the exit code.
    /// </summary>
    public static async Task<int> RunAsync(
        BulkPublishArguments arguments, HttpClient http, string? token, TimeProvider clock, TextWriter output, TextWriter error,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = Path.GetFullPath(arguments.Folder);
        if (!Directory.Exists(root))
        {
            error.WriteLine($"error: no folder {root}");
            return Fault;
        }

        // an earlier run's record: its published and unknown rows are carried forward, so a rerun never drops a package that
        // was or may have been published; a record of another engine is never merged into this one's
        var recordPath = Path.Combine(root, RecordFile);
        var (earlier, earlierEngine, earlierFault) = ReadRecord(recordPath);
        if (earlier is null)
        {
            error.WriteLine($"error: {earlierFault}; nothing is sent, neither file is written.");
            return Fault;
        }

        var engine = arguments.Engine.AbsoluteUri;
        if (earlierEngine is not null && !string.Equals(earlierEngine.TrimEnd('/'), engine.TrimEnd('/'), StringComparison.Ordinal))
        {
            error.WriteLine($"error: {recordPath} was written for the engine {earlierEngine}, not {engine}; use one operator folder for each engine. Nothing is sent, neither file is written.");
            return Fault;
        }

        var route = new Uri(new Uri(engine.TrimEnd('/') + "/"), Route);

        // the packages, as the engine lists them
        var (listed, listFault, _) = await SendAsync(http, HttpMethod.Get, route, null, token, ct);
        if (listed is not { } list || list["packages"] is not JsonArray packages || list["notRead"] is not JsonValue notReadValue
            || !notReadValue.TryGetValue<int>(out var notRead))
        {
            error.WriteLine($"error: GET {route} did not answer the list: {listFault ?? "its answer is not { packages, notRead }"}");
            return Fault;
        }

        // every package's folder read before anything is sent
        var requests = new List<(string Owner, string Name, string? Version, JsonObject Body)>();
        foreach (var package in packages)
        {
            if (Text(package?["owner"]) is not { } owner || Text(package?["name"]) is not { } name
                || !IsSegments(owner, nested: false) || !IsSegments(name, nested: true))
            {
                error.WriteLine($"error: GET {route} listed a package without a usable owner and name.");
                return Fault;
            }

            var folder = Path.Combine([root, owner, .. name.Split('/')]);
            var (body, fault) = Read(folder, owner, name, arguments.Publish);
            if (body is null)
            {
                error.WriteLine($"error: {fault}");
                return Fault;
            }

            requests.Add((owner, name, Text(package?["version"]), body));
        }

        // a folder of choices or edits that names no package: a misspelt one would leave its package at the defaults
        var known = requests.Select(r => r.Owner + "/" + r.Name)
            .Concat(earlier.Where(r => IsCarried(Text(r?["outcome"]))).Select(r => Text(r?["owner"]) + "/" + Text(r?["name"])))
            .ToHashSet(StringComparer.Ordinal);
        var (unmatched, walkFault) = Unmatched(root, known);
        if (unmatched is null)
        {
            error.WriteLine($"error: {walkFault}; nothing is sent, neither file is written.");
            return Fault;
        }

        if (unmatched.Count > 0)
        {
            foreach (var path in unmatched)
                error.WriteLine($"error: {path} matches no package: it holds {MigrationOptionsFile.FileName} or {HandEditsFolder}/, but names no package the engine listed and none the record carries.");
            error.WriteLine(notRead > 0
                ? string.Create(CultureInfo.InvariantCulture, $"error: rename or remove each (the engine could not read {notRead} of the accounts' active packages; a folder of one of them is set aside until it can). Nothing is sent, neither file is written.")
                : "error: rename or remove each. Nothing is sent, neither file is written.");
            return Fault;
        }

        // each package sent, in the engine's order; both files written after every row, so an interruption loses at most the
        // package in flight; a fault records that package unknown (it may have been published) and stops the run
        var rows = new JsonArray();
        var carried = new List<JsonNode?>();
        var exit = notRead > 0 ? NotMigratedExit : Clean;
        string? Persist()
        {
            var answered = rows.Select(r => (Text(r?["owner"]), Text(r?["name"]))).ToHashSet();
            carried = Latest(earlier.Where(r => IsCarried(Text(r?["outcome"])) && !answered.Contains((Text(r?["owner"]), Text(r?["name"])))));
            var merged = new JsonArray([.. carried.Select(r => r!.DeepClone()), .. rows.Select(r => r!.DeepClone())]);
            try
            {
                Write(recordPath, new JsonObject
                {
                    ["engine"] = engine,
                    ["publish"] = arguments.Publish,
                    ["notRead"] = notRead,
                    ["rows"] = merged,
                });
                Write(Path.Combine(root, OwnersFile), Owners(merged));
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"the record could not be written in {root}: {ex.Message}";
            }
        }

        foreach (var (owner, name, version, body) in requests)
        {
            var (answer, fault, rowFault) = await SendAsync(http, HttpMethod.Post, route, body, token, ct);
            var outcome = Text(answer?["outcome"]);
            var answeredRow = answer is not null && outcome is not null
                && (outcome is "published" or "would publish" || outcome.StartsWith(NotMigratedPrefix, StringComparison.Ordinal));
            if (!answeredRow)
            {
                error.WriteLine($"error: POST {route} for {owner} {name}: {fault ?? "its answer has no outcome"}");
                answer = new JsonObject
                {
                    ["owner"] = owner,
                    ["name"] = name,
                    ["oldVersion"] = version,
                    ["outcome"] = UnknownPrefix + (rowFault ?? "its answer has no outcome"),
                };
                outcome = Text(answer["outcome"])!;
                exit = Fault;
            }

            answer!["time"] = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            rows.Add(answer);
            if (answeredRow)
                output.WriteLine($"{owner} {name}@{Text(answer["oldVersion"]) ?? "?"}: {outcome}");
            if (outcome!.StartsWith(NotMigratedPrefix, StringComparison.Ordinal) && exit == Clean)
                exit = NotMigratedExit;

            if (Persist() is { } writeFault)
            {
                error.WriteLine($"error: {writeFault}");
                return Fault;
            }

            if (!answeredRow)
                break;
        }

        if (rows.Count == 0 && Persist() is { } emptyFault)
        {
            error.WriteLine($"error: {emptyFault}");
            return Fault;
        }

        // an unknown row carried from an earlier run: its package was not answered again, so the list is not yet sendable
        foreach (var row in carried.Where(r => Text(r?["outcome"])!.StartsWith(UnknownPrefix, StringComparison.Ordinal)))
        {
            error.WriteLine($"error: {Text(row?["owner"])} {Text(row?["name"])} is unknown in the record: the engine did not answer for it. Check its version on the engine before sending {OwnersFile}.");
            exit = Fault;
        }

        if (notRead > 0)
            error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"warning: {notRead} of the accounts' active packages could not be read and are not in the record."));
        var counts = rows.Select(r => Text(r?["outcome"]) ?? "").ToList();
        var carriedPublished = carried.Count(r => Text(r?["outcome"]) == "published");
        var carriedUnknown = carried.Count - carriedPublished;
        var unknownNow = counts.Count(o => o.StartsWith(UnknownPrefix, StringComparison.Ordinal));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{counts.Count(o => o == "published")} published, {counts.Count(o => o == "would publish")} would publish, "
            + $"{counts.Count(o => o.StartsWith(NotMigratedPrefix, StringComparison.Ordinal))} not migrated"
            + $"{(unknownNow > 0 ? $", {unknownNow} unknown" : "")}"
            + $"{(arguments.Publish ? "" : " (dry run; pass --publish to publish)")}"
            + $"{(carriedPublished > 0 ? $"; {carriedPublished} published by an earlier run carried forward" : "")}"
            + $"{(carriedUnknown > 0 ? $"; {carriedUnknown} unknown carried forward" : "")}; written {RecordFile} and {OwnersFile}."));
        return exit;
    }

    /// <summary>
    /// <see cref="OwnersFile"/>'s content from the record's rows: per owner (ordinal order), the packages published (or, in
    /// a dry run, that would be) with the notices each owner must see (A10-013, <c>flag</c> becoming <c>retry</c>, among
    /// them), the packages not migrated, each with its reason (the refusal's sentence), S10-005 and A11-015, and the packages
    /// whose row is <c>unknown</c> (the engine did not answer for them, so a <c>--publish</c> run may have published them),
    /// each with A11-017 for the operator, who settles it before the list is sent.
    /// </summary>
    public static JsonObject Owners(JsonArray rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var owners = new SortedDictionary<string, (JsonArray Published, JsonArray NotMigrated, JsonArray Unknown)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (Text(row?["owner"]) is not { } owner || Text(row?["name"]) is not { } name || Text(row?["outcome"]) is not { } outcome)
                continue;
            if (!owners.TryGetValue(owner, out var lists))
                owners[owner] = lists = ([], [], []);

            if (outcome.StartsWith(UnknownPrefix, StringComparison.Ordinal))
            {
                lists.Unknown.Add(new JsonObject { ["name"] = name, ["notice"] = PackagePublishSentences.NotAnswered });
            }
            else if (outcome.StartsWith(NotMigratedPrefix, StringComparison.Ordinal))
            {
                var reason = outcome[NotMigratedPrefix.Length..];
                lists.NotMigrated.Add(new JsonObject
                {
                    ["name"] = name,
                    ["version"] = row!["oldVersion"]?.DeepClone(),
                    ["reason"] = reason,
                    ["notice"] = PackagePublishSentences.StaysAt30(name, reason.TrimEnd('.')).Sentence,
                    ["deadline"] = PackagePublishSentences.StopsWhen30Retired,
                });
            }
            else
            {
                lists.Published.Add(new JsonObject
                {
                    ["name"] = name,
                    ["outcome"] = outcome,
                    ["oldVersion"] = row!["oldVersion"]?.DeepClone(),
                    ["newVersion"] = row["newVersion"]?.DeepClone(),
                    ["notices"] = row["notices"]?.DeepClone() ?? new JsonArray(),
                });
            }
        }

        var json = new JsonObject();
        foreach (var (owner, (published, notMigrated, unknown)) in owners)
            json[owner] = new JsonObject { ["published"] = published, ["notMigrated"] = notMigrated, ["unknown"] = unknown };
        return json;
    }

    /// <summary>
    /// The rows of the record at <paramref name="path"/> and the engine it was written for: none (and no engine) when there
    /// is no file; null and why not when it is not strict UTF-8 JSON, an object whose <c>engine</c> is text and whose
    /// <c>rows</c> is an array of objects.
    /// </summary>
    private static (IReadOnlyList<JsonNode?>? Rows, string? Engine, string? Fault) ReadRecord(string path)
    {
        if (!File.Exists(path))
            return ([], null, null);
        try
        {
            if (V31.StrictJson.Parse(Utf8.GetString(File.ReadAllBytes(path))) is { Fault: null, Value: JsonObject record }
                && Text(record["engine"]) is { } engine
                && record["rows"] is JsonArray rows && rows.All(r => r is JsonObject))
                return ([.. rows], engine, null);
            return (null, null, $"{path} is not a bulk publish record ({{ engine, publish, notRead, rows }}, each row an object)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return (null, null, $"{path} could not be read: {ex.Message}");
        }
    }

    /// <summary>Whether a row of <paramref name="outcome"/> is carried forward until its package is answered again: <c>published</c>, or <c>unknown: …</c>.</summary>
    private static bool IsCarried(string? outcome) =>
        outcome is not null && (outcome == "published" || outcome.StartsWith(UnknownPrefix, StringComparison.Ordinal));

    /// <summary><paramref name="rows"/> with one row for each <c>(owner, name)</c>, the latest, in the order of those latest rows.</summary>
    private static List<JsonNode?> Latest(IEnumerable<JsonNode?> rows)
    {
        var seen = new HashSet<(string?, string?)>();
        var latest = new List<JsonNode?>();
        foreach (var row in rows.Reverse())
            if (seen.Add((Text(row?["owner"]), Text(row?["name"]))))
                latest.Add(row);
        latest.Reverse();
        return latest;
    }

    /// <summary>
    /// Every folder under <paramref name="root"/> (outside a <c>hand-edits/</c>) holding <c>options.json</c> or
    /// <c>hand-edits/</c> whose <c>&lt;owner&gt;/&lt;name&gt;</c> is not in <paramref name="known"/>, in ordinal order; or
    /// null and why not when the folder cannot be walked.
    /// </summary>
    private static (IReadOnlyList<string>? Paths, string? Fault) Unmatched(string root, IReadOnlySet<string> known)
    {
        var unmatched = new List<string>();
        try
        {
            var pending = new Stack<string>([root]);
            while (pending.Count > 0)
            {
                var folder = pending.Pop();
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    if (Path.GetFileName(child) == HandEditsFolder)
                        continue;
                    pending.Push(child);
                }

                if (folder == root)
                    continue;
                if (!File.Exists(Path.Combine(folder, MigrationOptionsFile.FileName)) && !Directory.Exists(Path.Combine(folder, HandEditsFolder)))
                    continue;
                if (!known.Contains(Path.GetRelativePath(root, folder).Replace(Path.DirectorySeparatorChar, '/')))
                    unmatched.Add(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"{root} could not be walked: {ex.Message}");
        }

        unmatched.Sort(StringComparer.Ordinal);
        return (unmatched, null);
    }

    /// <summary>
    /// The request for the package whose folder is <paramref name="folder"/>: its <c>options.json</c> (null when the file
    /// is absent; <see cref="MigrationOptionsFile.Parse"/>) and every file under <c>hand-edits/</c> by its path (a
    /// <c>.deleted</c> file as null at its path), or null and why not.
    /// </summary>
    private static (JsonObject? Body, string? Fault) Read(string folder, string owner, string name, bool publish)
    {
        JsonObject? options = null;
        var edits = new JsonObject();
        try
        {
            var optionsPath = Path.Combine(folder, MigrationOptionsFile.FileName);
            if (File.Exists(optionsPath))
            {
                var (json, fault) = MigrationOptionsFile.Parse(Utf8.GetString(File.ReadAllBytes(optionsPath)));
                if (json is null)
                    return (null, $"{optionsPath}: {fault}");
                options = json;
            }

            var handEdits = Path.Combine(folder, HandEditsFolder);
            if (Directory.Exists(handEdits))
            {
                foreach (var file in Directory.EnumerateFiles(handEdits, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var path = Path.GetRelativePath(handEdits, file).Replace(Path.DirectorySeparatorChar, '/');
                    var deleted = path.EndsWith(DeletedSuffix, StringComparison.Ordinal);
                    var target = deleted ? path[..^DeletedSuffix.Length] : path;
                    if (edits.ContainsKey(target))
                        return (null, $"{handEdits}: '{target}' is both edited and deleted.");
                    edits[target] = deleted ? null : Utf8.GetString(File.ReadAllBytes(file));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return (null, $"{folder} could not be read: {ex.Message}");
        }

        return (new JsonObject
        {
            ["owner"] = owner,
            ["name"] = name,
            ["options"] = options,
            ["handEdits"] = edits,
            ["publish"] = publish,
        }, null);
    }

    /// <summary>
    /// The engine's answer to one request, a JSON object, or null and why not: for the operator's screen, its status and its
    /// <c>error</c>, or the exception's type and message; for the record, the same without the answer's text (its status
    /// alone).
    /// </summary>
    private static async Task<(JsonObject? Answer, string? Fault, string? RowFault)> SendAsync(
        HttpClient http, HttpMethod method, Uri route, JsonObject? body, string? token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, route);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
                request.Content = new StringContent(body.ToJsonString(), Utf8, "application/json");
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            try
            {
                json = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
            }

            if (!response.IsSuccessStatusCode)
                return (null, string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} {Text(json?["error"]) ?? response.ReasonPhrase}"),
                    string.Create(CultureInfo.InvariantCulture, $"the engine answered {(int)response.StatusCode}"));
            return json is JsonObject answer ? (answer, null, null) : (null, "its answer is not a JSON object", "its answer is not a JSON object");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            var fault = $"the engine did not answer ({ex.GetType().Name}: {ex.Message})";
            return (null, fault, fault);
        }
    }

    /// <summary>Whether <paramref name="text"/> is a path segment (or, <paramref name="nested"/>, segments joined by <c>/</c>) that stays inside its folder.</summary>
    private static bool IsSegments(string text, bool nested)
    {
        var segments = text.Split('/');
        return (nested || segments.Length == 1)
            && segments.All(s => s.Length > 0 && s is not ("." or "..") && s.IndexOfAny(['\\', ':', '\0']) < 0);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>Writes <paramref name="json"/> to a temporary file beside <paramref name="path"/>, then moves it over, so an interruption leaves the earlier file or the new one, never part of one.</summary>
    private static void Write(string path, JsonObject json)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, Utf8.GetBytes(json.ToJsonString(Indented) + "\n"));
        File.Move(temporary, path, overwrite: true);
    }
}
