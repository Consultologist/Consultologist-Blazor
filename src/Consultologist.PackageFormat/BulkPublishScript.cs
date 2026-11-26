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
/// #1508 (specVersion 31, area 11, Task 36; part 10 § 1.5; R11, R18, R21; ruled on #1508, comments 5 to 8): the
/// operator's bulk publish, here so it is tested; the script reads its arguments and the token, and calls
/// <see cref="RunAsync"/>. It asks the engine for every account's active account package below 31
/// (<c>GET Admin/MigratePackage</c>), reads each one's <c>options.json</c> and <c>hand-edits/</c> from
/// <c>&lt;operator folder&gt;/&lt;owner&gt;/&lt;name&gt;/</c> (a package with no folder has neither: the defaults, no edits),
/// checks every folder before it sends anything, then sends each package (<c>POST Admin/MigratePackage</c>), one at a
/// time, in the engine's order. It writes <see cref="RecordFile"/> (one row per package: the engine's answer and the time)
/// and <see cref="OwnersFile"/> (per owner: the packages published, each with the notices the owner must see; the packages
/// not migrated, each with its reason, S10-005 and A11-015). Exits <see cref="Clean"/> when every package published or
/// would, <see cref="NotMigratedExit"/> when any is not migrated (or could not be read), <see cref="Fault"/> on a usage
/// error, a folder it cannot read, or an engine that does not answer as the route does (the files then hold what was done
/// before it). It talks only to the engine it is given.
/// </summary>
public static class BulkPublishScript
{
    /// <summary>Exit 0: every package published, or would.</summary>
    public const int Clean = 0;

    /// <summary>Exit 1: a package is not migrated, or an account's active package could not be read.</summary>
    public const int NotMigratedExit = 1;

    /// <summary>Exit 2: a usage error, a folder that does not read, or an engine fault.</summary>
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

        var route = new Uri(new Uri(arguments.Engine.AbsoluteUri.TrimEnd('/') + "/"), Route);

        // the packages, as the engine lists them
        var (listed, listFault) = await SendAsync(http, HttpMethod.Get, route, null, token, ct);
        if (listed is not { } list || list["packages"] is not JsonArray packages || list["notRead"] is not JsonValue notReadValue
            || !notReadValue.TryGetValue<int>(out var notRead))
        {
            error.WriteLine($"error: GET {route} did not answer the list: {listFault ?? "its answer is not { packages, notRead }"}");
            return Fault;
        }

        // every package's folder read before anything is sent
        var requests = new List<(string Owner, string Name, JsonObject Body)>();
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

            requests.Add((owner, name, body));
        }

        // each package sent, in the engine's order; a fault stops the run, the files holding what was done before it
        var rows = new JsonArray();
        var exit = notRead > 0 ? NotMigratedExit : Clean;
        foreach (var (owner, name, body) in requests)
        {
            var (answer, fault) = await SendAsync(http, HttpMethod.Post, route, body, token, ct);
            var outcome = Text(answer?["outcome"]);
            if (answer is null || outcome is null || !(outcome is "published" or "would publish" || outcome.StartsWith(NotMigratedPrefix, StringComparison.Ordinal)))
            {
                error.WriteLine($"error: POST {route} for {owner} {name}: {fault ?? "its answer has no outcome"}");
                exit = Fault;
                break;
            }

            answer["time"] = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            rows.Add(answer);
            output.WriteLine($"{owner} {name}@{Text(answer["oldVersion"]) ?? "?"}: {outcome}");
            if (outcome.StartsWith(NotMigratedPrefix, StringComparison.Ordinal) && exit == Clean)
                exit = NotMigratedExit;
        }

        try
        {
            Write(Path.Combine(root, RecordFile), new JsonObject
            {
                ["engine"] = arguments.Engine.AbsoluteUri,
                ["publish"] = arguments.Publish,
                ["notRead"] = notRead,
                ["rows"] = rows,
            });
            Write(Path.Combine(root, OwnersFile), Owners(rows));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: the record could not be written in {root}: {ex.Message}");
            return Fault;
        }

        if (notRead > 0)
            error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"warning: {notRead} of the accounts' active packages could not be read and are not in the record."));
        var counts = rows.Select(r => Text(r?["outcome"]) ?? "").ToList();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{counts.Count(o => o == "published")} published, {counts.Count(o => o == "would publish")} would publish, "
            + $"{counts.Count(o => o.StartsWith(NotMigratedPrefix, StringComparison.Ordinal))} not migrated"
            + $"{(arguments.Publish ? "" : " (dry run; pass --publish to publish)")}; written {RecordFile} and {OwnersFile}."));
        return exit;
    }

    /// <summary>
    /// <see cref="OwnersFile"/>'s content from the record's rows: per owner (ordinal order), the packages published (or, in
    /// a dry run, that would be) with the notices each owner must see (A10-013, <c>flag</c> becoming <c>retry</c>, among
    /// them), and the packages not migrated, each with its reason (the refusal's sentence), S10-005 and A11-015.
    /// </summary>
    public static JsonObject Owners(JsonArray rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var owners = new SortedDictionary<string, (JsonArray Published, JsonArray NotMigrated)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (Text(row?["owner"]) is not { } owner || Text(row?["name"]) is not { } name || Text(row?["outcome"]) is not { } outcome)
                continue;
            if (!owners.TryGetValue(owner, out var lists))
                owners[owner] = lists = ([], []);

            if (outcome.StartsWith(NotMigratedPrefix, StringComparison.Ordinal))
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
        foreach (var (owner, (published, notMigrated)) in owners)
            json[owner] = new JsonObject { ["published"] = published, ["notMigrated"] = notMigrated };
        return json;
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

    /// <summary>The engine's answer to one request, a JSON object, or null and why not (its status and its <c>error</c>, or the exception's type).</summary>
    private static async Task<(JsonObject? Answer, string? Fault)> SendAsync(
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
                return (null, string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} {Text(json?["error"]) ?? response.ReasonPhrase}"));
            return json is JsonObject answer ? (answer, null) : (null, "its answer is not a JSON object");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, $"the engine did not answer ({ex.GetType().Name})");
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

    private static void Write(string path, JsonObject json) =>
        File.WriteAllBytes(path, Utf8.GetBytes(json.ToJsonString(Indented) + "\n"));
}
