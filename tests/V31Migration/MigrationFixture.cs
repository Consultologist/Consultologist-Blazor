using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Logic;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10): what the migration tests share. The 30 expressions of
/// <c>conditions/expressions.json</c> (area 01's, read, never rewritten), and <see cref="ScopeOf"/>, the
/// <see cref="ConversionScope"/> an entry's <c>reads</c> describe at 31: each input's 30 type as part 02 § 11 maps
/// it, required when the read says <c>required: true</c> and nullable otherwise (<c>text</c> → <c>markdown_block</c>, <c>enum</c> → a <c>string</c> with a plain <c>enum</c>, <c>date</c> →
/// <c>date</c>, <c>fields</c> → <c>properties</c> with every field in <c>required</c> and an optional one nullable),
/// each 30 node <c>x</c> at the path <c>node.x</c> (<c>text</c> → <c>plaintext</c>, as <c>prose</c> becomes;
/// <c>classification</c> kept; an object schema made strict the same way), always valued when the read is
/// <c>settled</c> and nullable otherwise, the loop's item, and
/// <c>pass.number</c> readable when it is read.
/// </summary>
internal static class MigrationFixture
{
    /// <summary>The entries of <c>conditions/expressions.json</c>, in order.</summary>
    public static IReadOnlyList<JsonObject> Expressions() => ExpressionsFile.Value;

    /// <summary>The entry with <paramref name="id"/>.</summary>
    public static JsonObject Expression(string id) =>
        Expressions().Single(e => (string?)e["id"] == id);

    /// <summary>
    /// The published formats (<see cref="LogicFixture.Formats"/>), and the two package formats the § 2.2 <c>let</c>s
    /// declare (their <c>letType</c>), written strict as part 02 § 3.3 has them: <c>triage_summary</c>, the
    /// <c>priority</c> and <c>age</c> that <c>row.let-object</c> builds, and <c>reasons</c>, the list of plain text
    /// <c>row.let-array</c> builds.
    /// </summary>
    public static FormatSet Formats() => PublishedFormats.Value;

    /// <summary>How an entry is converted: a <c>condition</c> as a condition, a <c>case</c> subject and a <c>let</c> as a value.</summary>
    public static ConversionKind KindOf(JsonObject entry) =>
        (string?)entry["kind"] == "condition" ? ConversionKind.Condition : ConversionKind.Value;

    /// <summary>
    /// A <c>let</c>'s declared format at 31 (part 02 § 11: <c>text</c> is <c>markdown_block</c>, <c>number</c> and
    /// <c>boolean</c> are themselves, a format id is that format); <c>null</c> for a condition and a <c>case</c>.
    /// </summary>
    public static JsonNode? DeclaredOf(JsonObject entry) => (string?)entry["letType"] switch
    {
        null => null,
        "text" => JsonValue.Create("markdown_block"),
        var type => JsonValue.Create(type),
    };

    private static readonly Lazy<IReadOnlyList<JsonObject>> ExpressionsFile = new(() =>
        JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "expressions.json")))!
            ["expressions"]!.AsArray().Select(e => e!.AsObject()).ToList());

    private static readonly Lazy<FormatSet> PublishedFormats = new(() => LogicFixture.Formats(
        ("triage_summary", """
            {"type":"object","additionalProperties":false,"required":["priority","age"],
             "properties":{"priority":{"type":["string","null"],"enum":["low","high",null]},"age":{"type":"number"}}}
            """),
        ("reasons", """
            {"type":"object","additionalProperties":false,"required":["reasons"],
             "properties":{"reasons":{"type":"array","items":{"$ref":"plaintext"}}}}
            """)));

    /// <summary>The 31 scope an entry's <c>reads</c> describe (see the class).</summary>
    public static ConversionScope ScopeOf(JsonObject reads)
    {
        var inputs = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, ConversionRead>(StringComparer.Ordinal);
        JsonNode? item = null;
        var folderItem = false;
        var inWhile = false;

        foreach (var (read, type) in reads)
        {
            if (read.StartsWith("input.", StringComparison.Ordinal))
            {
                inputs[read["input.".Length..]] = Format(type!, nullable: false);
                if (type is JsonObject { } declared && declared["required"]?.GetValue<bool>() == true)
                {
                    required.Add(read["input.".Length..]);
                }
            }
            else if (read.StartsWith("node.", StringComparison.Ordinal))
            {
                nodes[read["node.".Length..]] = new ConversionRead(read, NodeFormat(type!))
                {
                    AlwaysValued = type is JsonObject { } settled && settled["settled"]?.GetValue<bool>() == true,
                };
            }
            else if (read == "item")
            {
                if (type!["value"] is { } value)
                {
                    item = Format(value, nullable: false);
                }
                else
                {
                    folderItem = true;
                }
            }
            else if (read == "pass.number")
            {
                inWhile = true;
            }
        }

        return new ConversionScope(inputs, nodes, item, folderItem, inWhile, Formats()) { RequiredInputs = required };
    }

    /// <summary>
    /// The migration's options for <paramref name="package"/> (a folder of <c>vendored/</c> or <c>fixture-packages/</c>):
    /// the published registry (<see cref="V31Contract.PublishedTypes"/>), the goldens' toolchain
    /// (<see cref="CompiledGoldenTests.Fixed"/>), 30's formats catalogue (<see cref="TestFormats.Catalog"/>), no contract
    /// resolver (every fixture is at 30), and the package's hand choices, read from its <c>options.json</c>
    /// (<see cref="ExampleChoices"/>, R18). A package with no such file, or one that does not read, fails.
    /// </summary>
    public static Migration31Options Options(string package)
    {
        var (options, refusal) = Migration31Options.FromJson(
            ExampleChoices.Json(package), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog);
        Assert.True(refusal is null, $"{HandEditsFolder}/{package}/{MigrationOptionsFile.FileName}: {refusal}");
        return options!;
    }

    /// <summary>
    /// <see cref="Options"/> with no hand choice, for a package that is no fixture (a test's own, or the vectors' hand-built
    /// one): an account package's (§ 1.9, "the kind is added as a suffix"; § 1.10, <c>release</c>).
    /// </summary>
    public static Migration31Options NoChoices => new(V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog);

    /// <summary><paramref name="package"/> read from the fixtures (<see cref="ByteEquivalenceFixtures.ReadPackage"/>) and migrated with its <see cref="Options"/>.</summary>
    public static Migration31Result Migrate(string package)
    {
        var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage(package);
        return WorkflowPackageMigration30To31.Migrate(manifestJson, files, Options(package));
    }

    /// <summary>The folder of the operator's hand edits, beside <c>migrated/</c> (Task 16).</summary>
    public const string HandEditsFolder = "hand-edits";

    /// <summary>What a hand-edit folder holds besides the files it lays over the migration: its account of the edits.</summary>
    public const string HandEditsReadme = "README.md";

    /// <summary>
    /// What a hand-edit folder may hold besides package files when an edit moves a node: each 30 node id whose 31 rooted key
    /// the edit moved, to its new key (an operator's fact, not a package file), laid over <see cref="Migration31Result.Keys"/>
    /// so the comparison maps 30's instance keys to the moved rows.
    /// </summary>
    public const string HandEditsKeys = "keys.json";

    /// <summary>
    /// #1510 (item 1): what a hand-edit folder may hold besides package files when an edit writes its own node to strip a
    /// loop's items in place of the migration's: each such node's rooted key to the rooted key of the <c>ret</c> it strips
    /// (an operator's fact, not a package file), laid over <see cref="Migration31Result.StripNodes"/> so the comparison credits
    /// its texts as the migration's (<c>loop-item-strip-node</c>). A stripping node whose file the folder deletes leaves it.
    /// </summary>
    public const string HandEditsStripNodes = "strip-nodes.json";

    /// <summary>The suffix of a hand-edit file that removes the migrated file of its path without the suffix.</summary>
    public const string DeletedSuffix = ".deleted";

    /// <summary>
    /// #1507 (Task 16; part 10 §§ 1.4, 1.5): <paramref name="package"/> migrated (<see cref="MigratedPackagesTests.Migrated"/>)
    /// with the operator's hand edits made: every file under <c>hand-edits/&lt;package&gt;/</c> but its <c>README.md</c>
    /// laid over the migrated files (<see cref="Overlay"/>). The result keeps the migration's notices, hand edits, report
    /// notes and macro bodies: the edits change files, not what the migration said; its keys change only where the folder's
    /// <c>keys.json</c> says an edit moved a node.
    /// </summary>
    public static Migration31Result Edited(string package) => EditedCache.GetOrAdd(package, name =>
        Overlay(MigratedPackagesTests.Migrated(name), HandEditsOf(name)));

    /// <summary>The folder of <paramref name="package"/>'s hand edits, <c>hand-edits/&lt;package&gt;/</c>.</summary>
    public static string HandEditsOf(string package) =>
        Path.Combine([ByteEquivalenceFixtures.Root, HandEditsFolder, .. package.Split('/')]);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Migration31Result> EditedCache = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="migrated"/> with the files under <paramref name="folder"/> laid over it, each at its path relative to
    /// the folder (<c>manifest.json</c> the manifest): a file named <c>&lt;path&gt;.deleted</c> removes <c>&lt;path&gt;</c>, any
    /// other replaces or adds its path. The folder's own <c>README.md</c> is not a package file, nor is its <c>options.json</c>
    /// (<see cref="MigrationOptionsFile"/>, the choices the package was migrated with), nor its <c>keys.json</c>
    /// (<see cref="HandEditsKeys"/>), laid over the keys instead, nor its <c>strip-nodes.json</c> (<see cref="HandEditsStripNodes"/>),
    /// laid over the stripping nodes. A folder that does not exist
    /// changes nothing.
    /// </summary>
    public static Migration31Result Overlay(Migration31Result migrated, string folder)
    {
        if (!Directory.Exists(folder))
            return migrated;

        var manifest = migrated.ManifestJson;
        var files = new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal);
        var keys = new Dictionary<string, string>(migrated.Keys, StringComparer.Ordinal);
        var strip = new Dictionary<string, string>(migrated.StripNodes, StringComparer.Ordinal);
        var breaks = new Dictionary<string, int>(migrated.HardBreaks, StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative is HandEditsReadme or MigrationOptionsFile.FileName)
                continue;
            if (relative == HandEditsKeys)
            {
                foreach (var (id, key) in JsonNode.Parse(ByteEquivalenceFixtures.ReadText(path))!.AsObject())
                {
                    Assert.True(keys.ContainsKey(id), $"{relative} moves '{id}', which the migration did not key.");
                    keys[id] = (string)key!;
                }

                continue;
            }
            if (relative == HandEditsStripNodes)
            {
                foreach (var (node, ret) in JsonNode.Parse(ByteEquivalenceFixtures.ReadText(path))!.AsObject())
                    strip[node] = (string)ret!;
                continue;
            }
            if (relative.EndsWith(DeletedSuffix, StringComparison.Ordinal))
            {
                var removed = relative[..^DeletedSuffix.Length];
                Assert.True(files.Remove(removed), $"{relative} removes {removed}, which the migration did not write.");
                breaks.Remove(removed);
                foreach (var node in strip.Keys.Where(k => StripNodeFile(k) == removed).ToList())
                    strip.Remove(node);
                continue;
            }

            var text = ByteEquivalenceFixtures.ReadText(path);
            if (relative == "manifest.json")
                manifest = text;
            else
                files[relative] = text;

            // #1510 (item 4; G-D9): an edited body holds the hard breaks the operator kept or wrote, counted again.
            if (relative.EndsWith(".md", StringComparison.Ordinal) && HardBreaksIn(text) is var count and > 0)
                breaks[relative] = count;
            else
                breaks.Remove(relative);
        }

        Assert.All(strip.Keys, k => Assert.True(files.ContainsKey(StripNodeFile(k)), $"{HandEditsStripNodes} names {k}, which the edited package does not hold."));
        return migrated with { ManifestJson = manifest, Files = files, Keys = keys, StripNodes = strip, HardBreaks = breaks };
    }

    /// <summary>
    /// The explicit hard breaks of a file with a header: the line endings of its body (<c>\n</c> or <c>\r\n</c>, as the rule
    /// <c>explicit-hard-break</c> reads them) after an odd number of <c>\</c> (an even number is escaped backslashes, which
    /// break nothing).
    /// </summary>
    private static int HardBreaksIn(string text) =>
        FrontMatter.Split(text) is { } split ? System.Text.RegularExpressions.Regex.Count(split.Body, @"(?<!\\)(?:\\\\)*\\(?=\r?\n)") : 0;

    /// <summary>The file of the stripping node at rooted key <paramref name="key"/> (<c>node.a.b</c> is <c>nodes/a/b.md</c>).</summary>
    private static string StripNodeFile(string key) => "nodes/" + key["node.".Length..].Replace('.', '/') + ".md";

    /// <summary>The value <c>vectors.json</c> writes for a read that has none: an absent input, a node with no output.</summary>
    public const string Absent = "<absent>";

    /// <summary>
    /// The 31 data for one case of <c>vectors.json</c> (Task 4; part 03 § 6.1): <paramref name="scope"/> (read to value,
    /// as supplied) placed where 31 reads it, the read types from <paramref name="reads"/> (the entry's). An input passes
    /// through 31's start rules (<c>V31StartChecks.Inputs</c>, part 02 § 8): absent, <c>null</c> or text that is blank as
    /// <c>blank()</c> judges is not given, so it is left out and <c>input.x.value</c> and <c>.content</c> read <c>null</c>;
    /// otherwise it is <c>{ value, content }</c>, <c>content</c> the text for a text input (30's <c>text</c>, at 31
    /// <c>markdown_block</c>) and <c>null</c> for any other. A node is placed at its 31 path (<see cref="ScopeOf"/>'s): one
    /// absent was skipped, so <c>value</c> and <c>content</c> are <c>null</c>; else <c>value</c> is its value (a
    /// classifier's <c>{ value }</c>) and <c>content</c> its text for a text format, else the value's canonical JSON (D11
    /// of area 06). <c>item</c> is the case's item as written (<c>{ id }</c> or <c>{ value }</c>), <c>pass.number</c> and
    /// <c>run.date</c> their values, and <c>profile.name</c> the profile's field.
    /// </summary>
    public static JsonObject DataOf(JsonObject scope, JsonObject reads)
    {
        var paths = ScopeOf(reads).Nodes;
        var data = new JsonObject();
        foreach (var (read, value) in scope)
        {
            var type = reads[read];
            if (read.StartsWith("input.", StringComparison.Ordinal))
            {
                if (NotGiven(value))
                {
                    continue;
                }

                var text = TypeName(type) == "text";
                Place(data, read, new JsonObject { ["value"] = value!.DeepClone(), ["content"] = text ? value.DeepClone() : null });
            }
            else if (read.StartsWith("node.", StringComparison.Ordinal))
            {
                var path = paths[read["node.".Length..]].Path;
                if (value is JsonValue absent && absent.TryGetValue<string>(out var marker) && marker == Absent)
                {
                    Place(data, path, new JsonObject { ["value"] = null, ["content"] = null });
                    continue;
                }

                var name = TypeName(type);
                var nodeValue = name == "classification" ? new JsonObject { ["value"] = value!.DeepClone() } : value!.DeepClone();
                var content = name == "text" ? (string?)value : CanonicalJson.Write(nodeValue);
                Place(data, path, new JsonObject { ["value"] = nodeValue, ["content"] = content });
            }
            else
            {
                Place(data, read, value?.DeepClone());
            }
        }

        return data;
    }

    /// <summary>Whether 31's start leaves a supplied value not given: absent, <c>null</c>, or text of nothing but white space.</summary>
    private static bool NotGiven(JsonNode? value) =>
        value is null
        || (value is JsonValue text && text.TryGetValue<string>(out var s) && (s == Absent || s.EnumerateRunes().All(System.Text.Rune.IsWhiteSpace)));

    /// <summary>A read type's name: the name itself, or an object's <c>type</c>.</summary>
    private static string? TypeName(JsonNode? type) => type is JsonValue ? (string?)type : (string?)type?["type"];

    /// <summary>Sets <paramref name="value"/> at the dotted <paramref name="path"/> under <paramref name="data"/>, making the objects on the way.</summary>
    private static void Place(JsonObject data, string path, JsonNode? value)
    {
        var segments = path.Split('.');
        var at = data;
        foreach (var segment in segments[..^1])
        {
            if (at[segment] is not JsonObject next)
            {
                next = new JsonObject();
                at[segment] = next;
            }

            at = next;
        }

        at[segments[^1]] = value;
    }

    /// <summary>A 30 node's format at 31: <c>text</c> is <c>plaintext</c>, a classifier keeps <c>classification</c>, an object its schema made strict.</summary>
    private static JsonNode NodeFormat(JsonNode type)
    {
        if (type is JsonValue)
        {
            return (string)type! == "text"
                ? JsonValue.Create("plaintext")
                : throw new InvalidOperationException($"A node read of type '{type}' has no 31 format here.");
        }

        return (string?)type["type"] switch
        {
            "text" => JsonValue.Create("plaintext"),
            "classification" => JsonValue.Create("classification"),
            "object" when type["schema"] is JsonObject schema => Strict(schema, nullable: false),
            var other => throw new InvalidOperationException($"A node read of type '{other}' has no 31 format here."),
        };
    }

    /// <summary>A 30 read type (<c>text</c>, <c>enum</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>array</c>, <c>object</c>) as its 31 format.</summary>
    private static JsonNode Format(JsonNode type, bool nullable)
    {
        var name = type is JsonValue ? (string)type! : (string)type["type"]!;
        switch (name)
        {
            case "text":
                return nullable ? NullableReference("markdown_block") : JsonValue.Create("markdown_block");
            case "number":
            case "boolean":
                return nullable ? new JsonObject { ["type"] = new JsonArray(name, "null") } : JsonValue.Create(name);
            case "date":
                return nullable
                    ? new JsonObject { ["type"] = new JsonArray("string", "null"), ["format"] = "date" }
                    : JsonValue.Create("date");
            case "enum":
                var values = new JsonArray(type["values"]!.AsArray().Select(v => (JsonNode?)JsonValue.Create((string)v!)).ToArray());
                if (nullable)
                {
                    values.Add(null);
                }

                return new JsonObject
                {
                    ["type"] = nullable ? new JsonArray("string", "null") : "string",
                    ["enum"] = values,
                };
            case "array":
                var list = new JsonObject { ["type"] = "array", ["items"] = Format(type["items"]!, nullable: false) };
                return nullable ? NullableSchema(list) : list;
            case "object":
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var (field, fieldType) in type["fields"]!.AsObject())
                {
                    var fieldRequired = fieldType is JsonObject declared && declared["required"]?.GetValue<bool>() == true;
                    properties[field] = Format(fieldType!, nullable: !fieldRequired);
                    required.Add(field);
                }

                var obj = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = required,
                    ["properties"] = properties,
                };
                return nullable ? NullableSchema(obj) : obj;
            default:
                throw new InvalidOperationException($"A read type '{name}' has no 31 format here.");
        }
    }

    /// <summary>A 30 JSON Schema made strict (part 02 § 3.3): every property in <c>required</c>, an optional one nullable.</summary>
    private static JsonNode Strict(JsonObject schema, bool nullable)
    {
        var type = (string?)schema["type"];
        switch (type)
        {
            case "object":
                var listed = schema["required"]?.AsArray().Select(r => (string)r!).ToHashSet(StringComparer.Ordinal) ?? [];
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var (name, property) in schema["properties"]?.AsObject() ?? new JsonObject())
                {
                    properties[name] = Strict(property!.AsObject(), nullable: !listed.Contains(name));
                    required.Add(name);
                }

                var obj = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = required,
                    ["properties"] = properties,
                };
                return nullable ? NullableSchema(obj) : obj;
            case "array":
                var list = new JsonObject { ["type"] = "array", ["items"] = Strict(schema["items"]!.AsObject(), nullable: false) };
                return nullable ? NullableSchema(list) : list;
            case "string" when schema["enum"] is JsonArray values:
                var enumValues = values.DeepClone().AsArray();
                if (nullable)
                {
                    enumValues.Add(null);
                }

                return new JsonObject { ["type"] = nullable ? new JsonArray("string", "null") : "string", ["enum"] = enumValues };
            case "string" or "number" or "integer" or "boolean":
                return new JsonObject { ["type"] = nullable ? new JsonArray(type, "null") : type };
            default:
                throw new InvalidOperationException($"A node schema of type '{type}' has no 31 format here.");
        }
    }

    private static JsonObject NullableReference(string name) =>
        new() { ["anyOf"] = new JsonArray(new JsonObject { ["$ref"] = name }, new JsonObject { ["type"] = "null" }) };

    private static JsonObject NullableSchema(JsonObject schema) =>
        new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
}
