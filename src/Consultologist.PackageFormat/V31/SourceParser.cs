using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): reads a package source (part 01) into a
/// <see cref="SourcePackage"/>, or says every way it is wrong. Intake first (D4: an intake refusal
/// ends the parse); then the root, the manifest and each of the nine folders through
/// <see cref="SourceLayout"/>; then the files nobody listed. It never throws, whatever the upload
/// holds: every JSON value is read through a guard, since a declaration the member rules refused is
/// still walked for its layout.
/// </summary>
/// <remarks>
/// This file reads the root, the manifest, the definitions, the deliverables and the tests, and
/// the top of <c>nodes/</c> and <c>checks/</c>. Their leaves are read in <c>SourceParser.Nodes.cs</c>,
/// their flow nodes in <c>SourceParser.Flow.cs</c>, and <c>functions/</c> in
/// <c>SourceParser.Functions.cs</c>.
/// </remarks>
public static partial class SourceParser
{
    /// <summary>The nine top-level folders, always present (part 01 § 2).</summary>
    private static readonly string[] TopFolders =
        ["nodes", "checks", "functions", "renderers", "snippets", "macros", "formats", "deliverables", "tests"];

    /// <summary>
    /// Reads <paramref name="upload"/> (path to bytes) against <paramref name="registry"/>'s names. A null
    /// argument is the caller's error (<see cref="ArgumentNullException"/>); nothing an upload holds throws.
    /// </summary>
    public static SourceParseResult Parse(IReadOnlyDictionary<string, byte[]> upload, SourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(registry);

        var intake = SourceIntake.Read(upload);
        if (intake.Refusals.Count > 0)
            return new SourceParseResult(null, Order(intake.Refusals), intake.Normalised);

        var walk = new SourceWalk(intake.Files, registry);
        ReadRoot(walk);
        var manifest = ReadManifest(walk);
        var nodes = ReadBodyFolder(walk, "nodes");
        var checks = ReadBodyFolder(walk, "checks");
        var functions = ReadFunctions(walk);
        var definitions = ReadDefinitions(walk, "");
        var deliverables = ReadDeliverables(walk);
        var tests = ReadTests(walk);
        SourceLayout.ReportStrays(walk);

        // The scope rules need the whole tree, so they run on what the walk built, partial or not:
        // a scope fault is reported together with independent faults elsewhere.
        var tree = new SourcePackage(
            manifest ?? new SourceManifest("", "", null, null, null, [], [], new JsonObject()),
            nodes, checks, functions, definitions, deliverables, tests, walk.Readmes);
        SourceScopes.Check(tree, walk.Refusals);

        var refusals = Order(walk.Refusals);
        return new SourceParseResult(refusals.Count == 0 ? tree : null, refusals, intake.Normalised);
    }

    /// <summary>Each refusal once (same id, sentence and path), ordered by path, then in the order found.</summary>
    private static IReadOnlyList<SourceRefusal> Order(IEnumerable<SourceRefusal> refusals) =>
        refusals.Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ToList();

    /// <summary>The root's own files (manifest.json, README.md, dag.mmd) and the presence of the nine folders (S01-008).</summary>
    private static void ReadRoot(SourceWalk walk)
    {
        if (walk.Files.TryGetValue("README.md", out var readme))
        {
            walk.Account("README.md");
            walk.Readmes["README.md"] = readme;
        }

        walk.Account("dag.mmd");
        foreach (var folder in TopFolders)
        {
            if (!walk.HasFolder(folder))
                walk.Refuse(SourceSentences.NoTopFolder(folder));
        }
    }

    /// <summary>
    /// A top-level folder's or a function's definition folder's index, or null. A top-level folder
    /// that is not there was named once by <see cref="ReadRoot"/> (S01-008); a function's is S01-007.
    /// </summary>
    private static ReadFolder? Open(SourceWalk walk, string folder, DeclarationShape shape, MemberForm form)
    {
        if (!folder.Contains('/') && !walk.HasFolder(folder))
            return null;
        return SourceLayout.Read(walk, folder, shape, form);
    }

    // ---- The manifest (part 01 § 3) --------------------------------------------------------------

    private const string ManifestPath = "manifest.json";

    private static SourceManifest? ReadManifest(SourceWalk walk)
    {
        if (!walk.HasFile(ManifestPath))
        {
            walk.Refuse(SourceSentences.NoManifest());
            return null;
        }

        walk.Account(ManifestPath);
        var json = SourceLayout.ReadJson(walk, ManifestPath);
        if (json == null)
            return null;

        SourceMembers.Check(json, SourceShapes.Manifest, "The manifest", ManifestPath, walk.Refusals);
        CheckSpecVersion(walk, json["specVersion"]);

        var inputs = new List<SourceInput>();
        if (json["inputs"] is JsonObject declared)
        {
            foreach (var (id, value) in declared)
            {
                if (SourceIds.Check(id, ManifestPath) is { } bad)
                    walk.Refuse(bad);
                if (value is JsonObject input)
                {
                    SourceMembers.Check(input, SourceShapes.Input, $"Input '{id}'", ManifestPath, walk.Refusals);
                    inputs.Add(new SourceInput(id, input));
                }
                else
                {
                    walk.Refuse(SourceSentences.MemberWrongType(
                        "The manifest", $"inputs.{id}", SourceMembers.Describe(value), "an object", ManifestPath));
                }
            }
        }

        return new SourceManifest(
            SourceLayout.Text(json["name"]) ?? "",
            SourceLayout.Text(json["version"]) ?? "",
            SourceLayout.Text(json["derivedFrom"]),
            SourceLayout.Text(json["title"]),
            SourceLayout.Lines(json["description"]),
            SourceLayout.Lines(json["tags"]) ?? [],
            inputs,
            json);
    }

    /// <summary>
    /// A02-004 (D8): a whole number other than 31. A <c>specVersion</c> that is missing, null or not
    /// a whole number was already refused by the member rules and is not named twice.
    /// </summary>
    private static void CheckSpecVersion(SourceWalk walk, JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
            return;
        var written = value.ToJsonString();
        if (value.TryGetValue<decimal>(out var number))
        {
            if (number != decimal.Truncate(number) || number == 31)
                return;
        }
        else if (written.IndexOfAny(['.', 'e', 'E']) >= 0)
        {
            return;
        }

        walk.Refuse(SourceSentences.SpecVersionNotRead(written));
    }

    // ---- nodes/ and checks/ ----------------------------------------------------------------------

    /// <summary><c>nodes/</c> or <c>checks/</c>: the index, then the top-level body.</summary>
    private static SourceBody ReadBodyFolder(SourceWalk walk, string folder)
    {
        var read = Open(walk, folder, SourceShapes.Index, MemberForm.FileOrFolder);
        if (read == null)
            return new SourceBody(folder, null, []);
        var root = folder == "nodes" ? "node" : "check";
        return new SourceBody(folder, SourceLayout.Lines(read.Index["description"]), ReadBody(walk, read.Members, root, ""));
    }

    /// <summary>
    /// The members of one body, in listed order: each listed leaf or flow folder is read and judged
    /// (<c>SourceParser.Nodes.cs</c>, <c>SourceParser.Flow.cs</c>); one that was refused is left out.
    /// <paramref name="root"/> is <c>node</c> or <c>check</c>; <paramref name="enclosing"/> the dotted
    /// ids of the flow folders around the body (<c>""</c> at the top, or at a function's top).
    /// </summary>
    private static IReadOnlyList<SourceNode> ReadBody(
        SourceWalk walk, IReadOnlyList<ListedMember> members, string root, string enclosing)
    {
        var nodes = new List<SourceNode>();
        foreach (var member in members)
        {
            if (ReadChild(walk, root, enclosing, member) is { } node)
                nodes.Add(node);
        }

        return nodes;
    }

    // ---- The definitions (part 01 § 2; part 05) ---------------------------------------------------

    /// <summary>The four definition folders under <paramref name="root"/> (<c>""</c> for the package, or a function's folder).</summary>
    private static SourceDefinitions ReadDefinitions(SourceWalk walk, string root) => new(
        ReadRenderers(walk, SourceLayout.Join(root, "renderers")),
        ReadTextEntries(walk, SourceLayout.Join(root, "snippets"), snippets: true),
        ReadTextEntries(walk, SourceLayout.Join(root, "macros"), snippets: false),
        ReadFormats(walk, SourceLayout.Join(root, "formats")));

    private static SourceIndex<SourceText> ReadRenderers(SourceWalk walk, string folder)
    {
        var read = Open(walk, folder, SourceShapes.Index, MemberForm.File);
        if (read == null)
            return new SourceIndex<SourceText>(folder, null, []);

        var items = new List<SourceText>();
        foreach (var member in read.Members)
        {
            if (walk.Registry.BuiltInRenderers.Contains(member.Id))
                walk.Refuse(SourceSentences.RendererReusesName(member.Id, member.Path));
            if (ReadText(walk, member, SourceShapes.Renderer, $"Renderer '{member.Id}'", "template") is { } text)
                items.Add(text);
        }

        return new SourceIndex<SourceText>(folder, SourceLayout.Lines(read.Index["description"]), items);
    }

    /// <summary>
    /// <c>snippets/</c> or <c>macros/</c>: single files (<c>Snippet</c>, <c>Macro</c>) and folders of
    /// items (one level: items only). A snippet folder item's header is any JSON object and its body
    /// may be empty (D11); a macro folder item is a macro.
    /// </summary>
    private static SourceIndex<SourceTextEntry> ReadTextEntries(SourceWalk walk, string folder, bool snippets)
    {
        var read = Open(walk, folder, SourceShapes.Index, MemberForm.FileOrFolder);
        if (read == null)
            return new SourceIndex<SourceTextEntry>(folder, null, []);

        var shape = snippets ? SourceShapes.Snippet : SourceShapes.Macro;
        var word = snippets ? "Snippet" : "Macro";
        var entries = new List<SourceTextEntry>();
        foreach (var member in read.Members)
        {
            if (!member.IsFolder)
            {
                if (ReadText(walk, member, shape, $"{word} '{member.Id}'", "text") is { } text)
                    entries.Add(new SourceSingleText(text));
                continue;
            }

            var inner = SourceLayout.Read(
                walk, member.Path, snippets ? SourceShapes.SnippetFolderIndex : SourceShapes.MacroFolderIndex, MemberForm.File);
            if (inner == null)
                continue;
            var items = new List<SourceText>();
            foreach (var item in inner.Members)
            {
                var name = $"Item '{member.Id}.{item.Id}'";
                var text = snippets ? ReadText(walk, item, null, name, null) : ReadText(walk, item, SourceShapes.Macro, name, "text");
                if (text != null)
                    items.Add(text);
            }

            entries.Add(new SourceTextFolder(member.Id, member.Path, inner.Index, items));
        }

        return new SourceIndex<SourceTextEntry>(folder, SourceLayout.Lines(read.Index["description"]), entries);
    }

    /// <summary><c>formats/</c>: each a JSON object (A02-005), its name not a built-in's or a catalogue's (S01-034, built-in first).</summary>
    private static SourceIndex<SourceFormat> ReadFormats(SourceWalk walk, string folder)
    {
        var read = Open(walk, folder, SourceShapes.Index, MemberForm.Json);
        if (read == null)
            return new SourceIndex<SourceFormat>(folder, null, []);

        var formats = new List<SourceFormat>();
        foreach (var member in read.Members)
        {
            if (walk.Registry.BuiltInFormats.Contains(member.Id))
                walk.Refuse(SourceSentences.FormatReusesName(member.Id, "built-in", member.Path));
            else if (walk.Registry.CatalogueFormats.Contains(member.Id))
                walk.Refuse(SourceSentences.FormatReusesName(member.Id, "catalogue", member.Path));
            if (SourceLayout.ReadJson(walk, member.Path) is { } schema)
                formats.Add(new SourceFormat(member.Id, member.Path, schema));
        }

        return new SourceIndex<SourceFormat>(folder, SourceLayout.Lines(read.Index["description"]), formats);
    }

    // ---- deliverables/ and tests/ (parts 06 and 07) -----------------------------------------------

    private static SourceDeliveryFolder ReadDeliverables(SourceWalk walk)
    {
        const string folder = "deliverables";
        var read = Open(walk, folder, SourceShapes.DeliverablesIndex, MemberForm.FileOrFolder);
        if (read == null)
            return new SourceDeliveryFolder(null, folder, new JsonObject(), [], []);

        var deliverables = new List<SourceDeliverable>();
        var folders = new List<SourceDeliveryFolder>();
        foreach (var member in read.Members)
        {
            if (!member.IsFolder)
            {
                if (ReadDeliverable(walk, member, $"deliverable.{member.Id}") is { } deliverable)
                    deliverables.Add(deliverable);
            }
            else if (ReadDeliveryFolder(walk, member) is { } inner)
            {
                folders.Add(inner);
            }
        }

        return new SourceDeliveryFolder(null, folder, read.Index, deliverables, folders);
    }

    /// <summary>
    /// A delivery folder, one level deep: any folder inside it is S01-013, named once with its own
    /// path and its files not strays.
    /// </summary>
    private static SourceDeliveryFolder? ReadDeliveryFolder(SourceWalk walk, ListedMember folder)
    {
        var prefix = folder.Path + "/";
        var inner = walk.Files.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .Where(rest => rest.IndexOf('/') > 0)
            .Select(rest => prefix + rest[..rest.IndexOf('/')])
            .Distinct(StringComparer.Ordinal);
        foreach (var path in inner)
        {
            walk.Refuse(SourceSentences.NestedDeliveryFolder(path));
            walk.AccountTree(path);
        }

        var read = SourceLayout.Read(walk, folder.Path, SourceShapes.DeliverablesIndex, MemberForm.FileOrFolder);
        if (read == null)
            return null;

        var deliverables = new List<SourceDeliverable>();
        foreach (var member in read.Members.Where(m => !m.IsFolder))
        {
            if (ReadDeliverable(walk, member, $"deliverable.{folder.Id}.{member.Id}") is { } deliverable)
                deliverables.Add(deliverable);
        }

        return new SourceDeliveryFolder(folder.Id, folder.Path, read.Index, deliverables, []);
    }

    private static SourceDeliverable? ReadDeliverable(SourceWalk walk, ListedMember member, string key) =>
        ReadText(walk, member, SourceShapes.Deliverable, $"Deliverable '{member.Id}'", "template") is { } text
            ? new SourceDeliverable(member.Id, key, member.Path, text.Header, text.Body)
            : null;

    private static SourceIndex<SourceTest> ReadTests(SourceWalk walk)
    {
        const string folder = "tests";
        var read = Open(walk, folder, SourceShapes.Index, MemberForm.Json);
        if (read == null)
            return new SourceIndex<SourceTest>(folder, null, []);

        var tests = new List<SourceTest>();
        foreach (var member in read.Members)
        {
            if (SourceLayout.ReadJson(walk, member.Path) is not { } json)
                continue;
            SourceMembers.Check(json, SourceShapes.Test, $"Test '{member.Id}'", member.Path, walk.Refusals);
            tests.Add(new SourceTest(member.Id, member.Path, json));
        }

        return new SourceIndex<SourceTest>(folder, SourceLayout.Lines(read.Index["description"]), tests);
    }

    // ---- Files with front matter (part 01 § 4) ---------------------------------------------------

    /// <summary>
    /// A file with front matter: split and parsed, its header checked by <paramref name="shape"/>
    /// (none for a snippet folder item, D11) under the name <paramref name="declaration"/>, and its body
    /// required when <paramref name="bodyWord"/> names it (S01-019). Null when the file cannot be split
    /// or parsed.
    /// </summary>
    private static SourceText? ReadText(
        SourceWalk walk, ListedMember member, DeclarationShape? shape, string declaration, string? bodyWord)
    {
        if (SourceLayout.ReadHeaderFile(walk, member.Path) is not { } file)
            return null;
        var (header, body) = file;
        if (shape != null)
            SourceMembers.Check(header, shape, declaration, member.Path, walk.Refusals);
        if (bodyWord != null && FrontMatter.IsEmptyBody(body))
            walk.Refuse(SourceSentences.EmptyBody(declaration, bodyWord, member.Path));
        return new SourceText(member.Id, member.Path, header, body);
    }
}
