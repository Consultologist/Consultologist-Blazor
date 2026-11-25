using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2): the passes of the 30 → 31 rewrite, one file each, all on one
/// <see cref="Migrated"/>. This file is the first pass's layout (rows 1 to 5, the format rows, § 1.8): the manifest's
/// identity (part 01 § 3), the package's formats as their schemas alone (part 02 § 6.1, § 3.3), and the snippets and
/// snippet folders with their 31 headers (part 05 § 6, part 01 § 5.3). The nine folders and their indexes are the
/// working set's from the start (<see cref="Migrated.TopFolders"/>).
/// </summary>
internal static partial class Migrate
{
    /// <summary>The first pass's layout: the manifest's identity, the formats, then the snippets (whose folders add formats), and a root <c>README.md</c> as it was (part 01 § 2.3).</summary>
    public static void Layout(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        Manifest(m);
        Formats(m);
        Snippets(m);
        if (m.Files30.TryGetValue("README.md", out var readme))
            m.Texts["README.md"] = readme;
    }

    /// <summary>
    /// Part 01 § 3: identity and inputs only, every member written in <see cref="SourceShapes.Manifest"/>'s order:
    /// <c>specVersion</c> 31, <c>null</c> for no <c>derivedFrom</c>, <c>title</c> or description, the description as its
    /// lines (part 01 § 2.3; 30 holds it to 500 UTF-16 characters, so it is never past 31's 500 code points), <c>[]</c> for
    /// no tags. <c>inputs</c> is the
    /// inputs pass's (<see cref="Inputs"/>). <c>templating</c>, <c>nodes</c>, <c>macros</c>, <c>snippets</c>,
    /// <c>formats</c> and <c>deliverables</c> are not written: their folders list them.
    /// </summary>
    private static void Manifest(Migrated m)
    {
        var manifest = m.Manifest30;
        m.Manifest["name"] = manifest.Name;
        m.Manifest["version"] = manifest.Version;
        m.Manifest["specVersion"] = WorkflowPackageMigration30To31.Target;
        m.Manifest["derivedFrom"] = manifest.DerivedFrom;
        m.Manifest["title"] = manifest.Title;
        m.Manifest["description"] = Lines(manifest.Description);
        m.Manifest["tags"] = new JsonArray([.. (manifest.Tags ?? []).Select(t => (JsonNode?)JsonValue.Create(t))]);
        m.Manifest["inputs"] = new JsonObject();
    }

    /// <summary>A 30 description (one string) as 31's lines (part 01 § 2.3): split at <c>\n</c>; <c>null</c> for none or <c>""</c>.</summary>
    private static JsonArray? Lines(string? description) =>
        string.IsNullOrEmpty(description)
            ? null
            : new JsonArray([.. description.Split('\n').Select(l => (JsonNode?)JsonValue.Create(l))]);

    /// <summary>
    /// Part 02 § 6.1, § 11 (the wrapper row), § 3.3: each package format of 30's <c>formats</c> list becomes
    /// <c>formats/&lt;id&gt;.json</c>, the schema alone: <c>$schema</c> (the dialect, part 02 § 3.1), the schema's
    /// <c>type</c>, the wrapper's <c>description</c> as the schema's, then the schema's other members as written; the
    /// wrapper's <c>type</c>, <c>producer</c> and <c>outputMacros</c> are gone. Every property of every object is listed in
    /// <c>required</c>, the optional ones made nullable (§ 1.8, <see cref="Strict"/>). Each free-text <c>string</c> field is
    /// kept and a text format suggested (A10-021). Listed in <c>formats/index.json</c> in 30's order.
    /// </summary>
    private static void Formats(Migrated m)
    {
        foreach (var reference in m.Manifest30.Formats ?? [])
        {
            var wrapper = m.Files30.TryGetValue(reference.File, out var text) && StrictJson.Parse(text).Value is JsonObject read
                ? read
                : new JsonObject();
            var schema = wrapper["schema"] as JsonObject ?? new JsonObject();
            var written = new JsonObject { ["$schema"] = FormatDialect.DialectId };
            if (schema["type"] is { } type)
                written["type"] = type.DeepClone();
            if (wrapper["description"] is JsonValue description)
                written["description"] = description.DeepClone();
            else if (schema["description"] is { } own)
                written["description"] = own.DeepClone();
            foreach (var (member, value) in schema)
            {
                if (member is not ("$schema" or "type" or "description"))
                    written[member] = value?.DeepClone();
            }

            var path = $"formats/{reference.Id}.json";
            var types = TypesOf(written);
            var others = types.Where(t => t != "null").Distinct(StringComparer.Ordinal).ToList();
            if (HasNullType(written) && others.Count == 1)
            {
                // 30's nullable root (["object", "null"]): 31 writes it as an anyOf, which the root of a format a model
                // writes may not be (part 02 § 3.3). Written in that form, the checker refuses it: a hand edit.
                written = NullableRoot(written, others[0]);
                m.HandEdit(path, "type", MigrationSentences.NullableRoot);
            }
            else if ((types.Count > 0 && others.Count == 0) || (others.Count == 1 && others[0] is not ("object" or "array")))
            {
                // A root of null alone (["null", "null"] too) or of a scalar: a 31 package format is an object or a list
                // (part 02 § 3.3), so the checker refuses it as written: a hand edit.
                m.HandEdit(path, "type", MigrationSentences.PackageFormatRoot);
            }

            Strict(written, (pointer, _) => m.HandEdit(path, pointer, MigrationSentences.UnionField));
            SuggestTextFormats(m, reference.Id, written, "");
            m.Put($"formats/{reference.Id}.json", written);
            m.Items("formats").Add(reference.Id);
        }
    }

    /// <summary>
    /// Part 02 § 3.3, part 10 § 1.8: every property of <paramref name="schema"/>'s objects, at every depth (nested objects,
    /// array items, the arms of an <c>anyOf</c>), listed in <c>required</c> in property order, and each one 30 left out of
    /// <c>required</c>, or wrote nullable in 30's form (a <c>type</c> array with <c>"null"</c>), made nullable in 31's
    /// (<see cref="Nullable"/>). A <c>required</c> the schema has keeps its place; one it lacks is added after its other
    /// members. A <c>type</c> array of more than one type besides <c>"null"</c> (30's union) has no 31 form (part 02 § 3.1,
    /// § 3.2): it is kept as written, which the checker refuses, and <paramref name="union"/> is told its JSON pointer in
    /// the written file (once each; <paramref name="pointer"/> is <paramref name="schema"/>'s own).
    /// </summary>
    internal static void Strict(JsonObject schema, Action<string, JsonObject>? union = null, string pointer = "")
    {
        if (IsUnion(schema))
            union?.Invoke(pointer + "/type", schema);

        if (schema["properties"] is JsonObject properties)
        {
            var required = (schema["required"] as JsonArray ?? [])
                .Select(r => r is JsonValue v && v.TryGetValue<string>(out var name) ? name : null)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in properties.Select(p => p.Key).ToList())
            {
                if (properties[name] is not JsonObject property)
                    continue;
                if (!required.Contains(name) || HasNullType(property))
                {
                    properties[name] = null;
                    properties[name] = property = Nullable(property);
                }

                Strict(property, union, $"{pointer}/properties/{FormatDialect.Escape(name)}");
            }

            schema["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
        }

        if (schema["items"] is JsonObject items)
        {
            if (HasNullType(items))
            {
                schema["items"] = null;
                schema["items"] = items = Nullable(items);
            }

            Strict(items, union, pointer + "/items");
        }

        if (schema["anyOf"] is JsonArray arms)
        {
            for (var i = 0; i < arms.Count; i++)
            {
                if (arms[i] is JsonObject arm)
                    Strict(arm, union, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{pointer}/anyOf/{i}"));
            }
        }
    }

    /// <summary>The type names of <paramref name="schema"/>'s <c>type</c> (one, or the array's), none when it has none.</summary>
    private static List<string> TypesOf(JsonObject schema) => schema["type"] switch
    {
        JsonValue value when value.TryGetValue<string>(out var single) => [single],
        JsonArray array => array.Select(t => t is JsonValue v && v.TryGetValue<string>(out var n) ? n : null).OfType<string>().ToList(),
        _ => [],
    };

    /// <summary>Whether <paramref name="schema"/>'s <c>type</c> is 30's union: an array of more than one type besides <c>"null"</c>.</summary>
    private static bool IsUnion(JsonObject schema) => TypesOf(schema).Count(t => t != "null") > 1;

    /// <summary>
    /// A format whose root 30 wrote nullable (<c>["object", "null"]</c>) in 31's form (part 02 § 3.2): <c>$schema</c> and
    /// the <c>description</c> stay at the root, beside an <c>anyOf</c> of the schema with its one <paramref name="type"/>
    /// and <c>null</c>.
    /// </summary>
    private static JsonObject NullableRoot(JsonObject written, string type)
    {
        var root = new JsonObject { ["$schema"] = written["$schema"]!.DeepClone() };
        if (written["description"] is { } description)
            root["description"] = description.DeepClone();
        var arm = new JsonObject();
        foreach (var (member, value) in written)
        {
            if (member is not ("$schema" or "description"))
                arm[member] = value?.DeepClone();
        }

        arm["type"] = type;
        root["anyOf"] = new JsonArray(arm, new JsonObject { ["type"] = "null" });
        return root;
    }

    /// <summary>
    /// Part 02 § 3.2's one way to write "nullable", for <paramref name="schema"/> (detached): a scalar's <c>type</c> becomes
    /// the array of it and <c>"null"</c>, an enum listing <c>null</c> too; an object, an array, a reference or anything
    /// with no <c>type</c> is wrapped as <c>anyOf</c> with <c>{ "type": "null" }</c>. A schema already nullable is kept, and
    /// so is 30's union of several types (<see cref="Strict"/> makes it a hand edit).
    /// </summary>
    internal static JsonObject Nullable(JsonObject schema)
    {
        var types = TypesOf(schema);
        var others = types.Where(t => t != "null").ToList();
        if ((types.Count > 0 && others.Count == 0) || others.Count > 1)
            return schema;

        if (others.Count == 1 && others[0] is not ("object" or "array"))
        {
            schema["type"] = new JsonArray(others[0], "null");
            if (schema["enum"] is JsonArray values && !values.Any(v => v is null))
                values.Add(null);
            return schema;
        }

        if (schema["anyOf"] is JsonArray arms && arms.Any(a => a is JsonObject o && o.Count == 1 && (string?)o["type"] == "null"))
            return schema;

        // 30's nullable object or array (["object", "null"]) is written as 31 writes it: the type alone, then anyOf.
        if (others.Count == 1)
            schema["type"] = others[0];
        return new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    }

    /// <summary>Whether <paramref name="schema"/>'s <c>type</c> is 30's nullable form: an array holding <c>"null"</c> and another type.</summary>
    private static bool HasNullType(JsonObject schema) =>
        schema["type"] is JsonArray types
        && types.Count > 1
        && types.Any(t => t is JsonValue v && v.TryGetValue<string>(out var n) && n == "null");

    /// <summary>
    /// A10-021 (part 02 § 11): each field of <paramref name="schema"/> that is free text (a <c>string</c> with no
    /// <c>enum</c>, <c>const</c> or <c>format</c>), named by its path of property names, depth first in property order.
    /// </summary>
    private static void SuggestTextFormats(Migrated m, string format, JsonObject schema, string path)
    {
        if (schema["anyOf"] is JsonArray arms)
        {
            foreach (var arm in arms.OfType<JsonObject>())
                SuggestTextFormats(m, format, arm, path);
            return;
        }

        if (path.Length > 0 && IsFreeText(schema))
            m.Suggest(MigrationSentences.FreeTextField(format, path));
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, property) in properties)
            {
                if (property is JsonObject field)
                    SuggestTextFormats(m, format, field, path.Length == 0 ? name : $"{path}.{name}");
            }
        }

        if (schema["items"] is JsonObject items)
            SuggestTextFormats(m, format, items, path);
    }

    private static bool IsFreeText(JsonObject schema)
    {
        var isString = schema["type"] switch
        {
            JsonValue value => value.TryGetValue<string>(out var type) && type == "string",
            JsonArray types => types.Any(t => t is JsonValue v && v.TryGetValue<string>(out var n) && n == "string"),
            _ => false,
        };
        return isString && !schema.ContainsKey("enum") && !schema.ContainsKey("const") && !schema.ContainsKey("format");
    }

    /// <summary>
    /// Part 05 § 6, part 01 § 5.3: each 30 snippet in its table's order. A single snippet becomes
    /// <c>snippets/&lt;id&gt;.md</c> with <c>label</c>, <c>description: null</c>, <c>if</c> (<c>null</c>, or 30's Scriban
    /// condition as a JSON string, D3's form, which the conditions pass converts) and <c>format: "markdown_block"</c> (as 30's bodies
    /// are, part 05 § 6.2), its body as it was. A folder becomes <c>snippets/&lt;folder&gt;/index.json</c> (its 28
    /// <c>label</c>, if any, as its description) and one file per item, whose header is a value of the folder's format:
    /// 30's item <c>label</c> as the member <c>title</c> of the package format the hand choice names
    /// (<see cref="Migration31Options.FolderFormats"/>) or <c>&lt;folder&gt;_item</c> (<see cref="FolderFormat"/>). A 30
    /// item's <c>if</c> has no 31 place: it is kept in the header, which the checker refuses, as a hand edit (A10-022).
    /// </summary>
    private static void Snippets(Migrated m)
    {
        foreach (var (id, path) in m.Manifest30.Snippets ?? [])
        {
            if (path.EndsWith('/'))
                SnippetFolder(m, id, path);
            else
                m.Put($"snippets/{id}.md", SnippetHeader(m, path, id), BodyOf(m, path));
            m.Items("snippets").Add(id);
        }
    }

    private static JsonObject SnippetHeader(Migrated m, string path, string id) => new()
    {
        ["label"] = m.Resolved30.Headers.GetValueOrDefault(path)?.Label ?? id,
        ["description"] = null,
        ["if"] = m.Resolved30.Headers.GetValueOrDefault(path)?.If,
        ["format"] = "markdown_block",
    };

    private static string BodyOf(Migrated m, string path) => m.Resolved30.Files.GetValueOrDefault(path) ?? "";

    private static void SnippetFolder(Migrated m, string id, string path)
    {
        var index = m.Files30.TryGetValue(path + WorkflowDataResolver.IndexFileName, out var indexJson)
            ? WorkflowTextHeaders.ReadIndex(indexJson, id, "Snippet", m.Manifest30.SpecVersion, [])
            : null;
        var format = FolderFormat(m, id);
        var folder = new JsonObject
        {
            ["description"] = index?.Label is { } label ? new JsonArray(label) : null,
            ["items"] = new JsonArray(),
            ["format"] = format,
        };
        m.Put($"snippets/{id}/{WorkflowDataResolver.IndexFileName}", folder);

        foreach (var (item, file) in index?.Items ?? [])
        {
            var header30 = m.Resolved30.Headers.GetValueOrDefault(path + file);
            var header = new JsonObject { ["title"] = header30?.Label ?? item };
            var written = $"snippets/{id}/{item}.md";
            if (header30?.If is { } condition)
            {
                header["if"] = condition;
                m.HandEdit(written, "if", MigrationSentences.FolderItemIf);
            }

            m.Put(written, header, BodyOf(m, path + file));
            m.Items($"snippets/{id}").Add(item);
        }
    }

    /// <summary>
    /// The package format a snippet folder's items are values of: <c>{ title: string }</c>, every property required (part
    /// 01 § 5.3), named as the hand choice says or <c>&lt;folder&gt;_item</c>. Written to <c>formats/</c> and listed after
    /// the package's own. A hand choice the renames pass did not refuse (A10-069: no 31 id, or a format of the package or
    /// the registry) is used as given; one an earlier folder chose is that folder's format, not written again. The
    /// generated name is cut to 64 characters as every generated id is (A10-034, said), and takes a numeric suffix
    /// (<c>_2</c>, …) when a format of the package already has it (final review A6).
    /// </summary>
    private static string FolderFormat(Migrated m, string folder)
    {
        var taken = m.Items("formats").Select(i => (string?)i).ToHashSet(StringComparer.Ordinal);
        string id;
        if (m.Options.FolderFormats?.GetValueOrDefault(folder) is { } chosen)
        {
            if (taken.Contains(chosen))
                return chosen;
            id = chosen;
        }
        else
        {
            var wanted = $"{folder}_item";
            id = Renames.Fit(folder, "_item");
            for (var n = 2; taken.Contains(id); n++)
                id = Renames.Fit(folder, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"_item_{n}"));
            if (wanted.Length > SourceBounds.IdLength)
                m.Notice(MigrationSentences.RenamedTooLong(Renames.Format, wanted, id));
        }

        m.Put($"formats/{id}.json", new JsonObject
        {
            ["$schema"] = FormatDialect.DialectId,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("title"),
            ["properties"] = new JsonObject { ["title"] = new JsonObject { ["type"] = "string" } },
        });
        m.Items("formats").Add(id);
        return id;
    }
}
