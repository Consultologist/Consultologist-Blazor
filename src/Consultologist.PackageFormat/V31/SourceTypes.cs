using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): what the type rules learned of a package, for the areas that
/// follow: the formats the package's own scope can name, each function's (by function id), and,
/// by each node's <see cref="SourceNode.Path"/> (a key is not unique across functions), its
/// declared format and, for an agent, its binding.
/// </summary>
public sealed record PackageTypes(
    FormatSet Formats,
    IReadOnlyDictionary<string, FormatSet> FunctionFormats,
    IReadOnlyDictionary<string, JsonNode> NodeFormats,
    IReadOnlyDictionary<string, AgentBinding> Bindings);

/// <summary>
/// #1500: what <see cref="SourceTypes.Check"/> made of a package. <see cref="Types"/> is null exactly
/// when <see cref="Refusals"/> is not empty; the refusals are said once each and ordered as the
/// reader's are: by path (ordinal), then in the order found.
/// </summary>
public sealed record TypeCheckResult(IReadOnlyList<SourceRefusal> Refusals, PackageTypes? Types);

/// <summary>
/// #1500 (specVersion 31, area 03): the type rules of a package source that need no expression or
/// template (part 02), run on what <see cref="SourceParser.Parse"/> accepted. This file holds the
/// format files (§§ 3–6, D30), every <c>format</c> member in the scope that owns it (§ 2, D11) and
/// the inputs (§ 8, D17–D19); the nodes' rules (§§ 1–2, § 7) are <c>SourceTypes.Nodes.cs</c>'s. Never throws on
/// package content.
/// </summary>
public static partial class SourceTypes
{
    private const string ManifestPath = "manifest.json";

    /// <summary>The channels an input may declare (part 02 § 8.3); the first three take text.</summary>
    private static readonly string[] TextChannels = ["transcript", "image", "ambient-note"];

    /// <summary>
    /// Holds <paramref name="package"/> to the type rules against <paramref name="registry"/>. A null
    /// argument is the caller's error (<see cref="ArgumentNullException"/>).
    /// </summary>
    public static TypeCheckResult Check(SourcePackage package, TypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(registry);

        var refusals = new List<SourceRefusal>();
        var files = new Dictionary<JsonObject, SourceFormat>(ReferenceEqualityComparer.Instance);

        var formats = FormatSet.ForPackage(registry, package.Definitions.Formats.Items);
        CheckFiles(formats, package.Definitions.Formats.Items, files, refusals);

        var functionFormats = new Dictionary<string, FormatSet>(StringComparer.Ordinal);
        foreach (var function in package.Functions.Items)
        {
            var path = SourceLayout.Join(function.Path, "index.json");
            var uses = SourceLayout.Lines((function.Index["uses"] as JsonObject)?["formats"]) ?? [];

            // A name no scope knows; a built-in or catalogue name needs no listing, and is another area's to judge.
            foreach (var name in uses.Distinct(StringComparer.Ordinal).Where(n => formats.Find(n) == null))
                refusals.Add(FormatSentences.UnknownFormat(name, path));

            var scope = formats.ForFunction(function.Definitions.Formats.Items, uses);
            functionFormats.TryAdd(function.Id, scope);
            CheckFiles(scope, function.Definitions.Formats.Items, files, refusals);
        }

        // Every format member, in its scope; then the inputs whose format passed and reaches no refused format file.
        var refused = RefusedPaths(refusals);
        foreach (var input in package.Manifest.Inputs)
        {
            if (Member(formats, input.Json["format"], $"Input '{input.Id}'", input.Id, ManifestPath, refusals)
                && !ReachesRefused(formats, input.Json["format"], files, refused))
            {
                CheckInput(formats, input, refusals);
            }
        }

        var passed = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
        Nodes(formats, package.Nodes.Nodes, passed, refusals);
        Nodes(formats, package.Checks.Nodes, passed, refusals);
        Definitions(formats, package.Definitions, refusals);
        Deliverables(formats, package.Deliverables, refusals);
        foreach (var function in package.Functions.Items)
        {
            var scope = functionFormats[function.Id];
            var path = SourceLayout.Join(function.Path, "index.json");
            var name = $"Function '{function.Id}'";
            Member(scope, function.Index["format"], name, function.Id, path, refusals);
            foreach (var parameter in function.Inputs)
                Member(scope, parameter.Json["format"], $"{name} parameter '{parameter.Id}'", parameter.Id, path, refusals);
            Nodes(scope, function.Body.Nodes, passed, refusals);
            Definitions(scope, function.Definitions, refusals);
        }

        // The nodes' rules, each node judged only when its format passed all of the above.
        var judge = new NodeJudge(registry, files, RefusedPaths(refusals), passed, refusals);
        judge.Body(formats, package.Nodes.Nodes);
        judge.Body(formats, package.Checks.Nodes);
        foreach (var function in package.Functions.Items)
            judge.Body(functionFormats[function.Id], function.Body.Nodes);

        var ordered = refusals.Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
        var types = ordered.Count == 0
            ? new PackageTypes(formats, functionFormats, judge.NodeFormats, judge.Bindings)
            : null;
        return new TypeCheckResult(ordered, types);
    }

    // ---- Format files (part 02 §§ 3–6) ----------------------------------------------------------

    /// <summary>
    /// The format files of one scope (the package's <c>formats/</c>, or a function's): each against the
    /// dialect, then the scope's loops (a used package format's loop is said again by a function's
    /// scope, and de-duplicated at the end), then each file's names. Then D30: a file with no refusal,
    /// that reaches no file with one, and whose full form still cannot be built, is A03-011.
    /// </summary>
    private static void CheckFiles(
        FormatSet set, IReadOnlyList<SourceFormat> own, Dictionary<JsonObject, SourceFormat> files, List<SourceRefusal> refusals)
    {
        foreach (var format in own)
        {
            files.TryAdd(format.Schema, format);
            refusals.AddRange(FormatDialect.Check(format.Schema, SchemaSite.PackageFormat, $"Format '{format.Id}'", format.Id, format.Path));
        }

        refusals.AddRange(set.CheckCycles());
        foreach (var format in own)
            refusals.AddRange(set.CheckReferences(format.Schema, $"Format '{format.Id}'", format.Path));

        var refused = RefusedPaths(refusals);
        foreach (var format in own)
        {
            if (!refused.Contains(format.Path) && !ReachesRefused(set, format.Schema, files, refused)
                && !FormatTypes.Expands(format.Schema, set))
                refusals.Add(FormatSentences.FormatDoesNotExpand(format.Id, format.Path));
        }
    }

    private static HashSet<string> RefusedPaths(List<SourceRefusal> refusals) =>
        refusals.Select(r => r.Path).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a package or function format file that <paramref name="format"/> reaches through names, at any depth, has a refusal already.</summary>
    private static bool ReachesRefused(
        FormatSet set, JsonNode? format, Dictionary<JsonObject, SourceFormat> files, HashSet<string> refused)
    {
        var seen = new HashSet<JsonObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<JsonNode?>([format]);
        while (pending.Count > 0)
        {
            foreach (var (name, _) in FormatSet.References(pending.Pop()))
            {
                if (set.Find(name) is { Origin: FormatOrigin.Package or FormatOrigin.Function } found
                    && seen.Add(found.Schema) && files.TryGetValue(found.Schema, out var file))
                {
                    if (refused.Contains(file.Path))
                        return true;
                    pending.Push(file.Schema);
                }
            }
        }

        return false;
    }

    // ---- Format members (part 02 § 2, D11) -------------------------------------------------------

    /// <summary>
    /// One <c>format</c> member in <paramref name="set"/>'s scope: an inline schema against the dialect
    /// under its owner's name (<paramref name="subject"/>, D11), and every name it holds looked up.
    /// Any other value was the reader's to refuse. True when it added nothing.
    /// </summary>
    private static bool Member(FormatSet set, JsonNode? format, string subject, string id, string path, List<SourceRefusal> refusals)
    {
        var before = refusals.Count;
        if (format is JsonObject)
            refusals.AddRange(FormatDialect.Check(format, SchemaSite.Inline, subject, id, path));
        refusals.AddRange(set.CheckReferences(format, subject, path));
        return refusals.Count == before;
    }

    /// <summary>
    /// Each node of a body and the bodies inside it: a leaf by its file, a flow node by its
    /// <c>index.json</c>; a node whose <c>format</c> added nothing joins <paramref name="passed"/>.
    /// </summary>
    private static void Nodes(FormatSet set, IReadOnlyList<SourceNode> nodes, HashSet<SourceNode> passed, List<SourceRefusal> refusals)
    {
        foreach (var node in nodes)
        {
            if (Member(set, node.Header["format"], $"{Word(node)} '{node.Id}'", node.Id, FilePath(node), refusals))
                passed.Add(node);
            foreach (var body in Bodies(node))
                Nodes(set, body.Nodes, passed, refusals);
        }
    }

    /// <summary>How a sentence names a node (02-D9): <c>Loop</c>, <c>While</c>, <c>If</c>, <c>Case</c>, or <c>Check</c> or <c>Node</c> for a leaf.</summary>
    private static string Word(SourceNode node) => node switch
    {
        SourceFor => "Loop",
        SourceWhile => "While",
        SourceIf => "If",
        SourceCase => "Case",
        _ => node.Key.StartsWith("check.", StringComparison.Ordinal) ? "Check" : "Node",
    };

    /// <summary>The file a node's refusals carry: a leaf's own, a flow node's <c>index.json</c>.</summary>
    private static string FilePath(SourceNode node) => node is SourceLeaf ? node.Path : SourceLayout.Join(node.Path, "index.json");

    /// <summary>A node's bodies in order: a loop's one; an <c>if</c>'s <c>then</c>, then its <c>else</c>; a <c>case</c>'s branches, then its <c>else</c>.</summary>
    private static IEnumerable<SourceBlock> Bodies(SourceNode node)
    {
        switch (node)
        {
            case SourceFor loop:
                yield return loop.Body;
                break;
            case SourceWhile loop:
                yield return loop.Body;
                break;
            case SourceIf branch:
                yield return branch.Then;
                if (branch.Else != null)
                    yield return branch.Else;
                break;
            case SourceCase choice:
                foreach (var branch in choice.Branches)
                    yield return branch.Body;
                if (choice.Else != null)
                    yield return choice.Else;
                break;
        }
    }

    /// <summary>The renderers', snippets' and macros' format members (a snippet folder item has none, 02-D11).</summary>
    private static void Definitions(FormatSet set, SourceDefinitions definitions, List<SourceRefusal> refusals)
    {
        foreach (var renderer in definitions.Renderers.Items)
        {
            var name = $"Renderer '{renderer.Id}'";
            Member(set, renderer.Header["input"], name, renderer.Id, renderer.Path, refusals);
            Member(set, renderer.Header["format"], name, renderer.Id, renderer.Path, refusals);
        }

        foreach (var entry in definitions.Snippets.Items)
        {
            if (entry is SourceSingleText { Text: var snippet })
                Member(set, snippet.Header["format"], $"Snippet '{snippet.Id}'", snippet.Id, snippet.Path, refusals);
            else if (entry is SourceTextFolder folder)
                Member(set, folder.Index["format"], $"Snippet folder '{folder.Id}'", folder.Id, SourceLayout.Join(folder.Path, "index.json"), refusals);
        }

        foreach (var entry in definitions.Macros.Items)
        {
            IEnumerable<(SourceText Text, string Id)> macros = entry switch
            {
                SourceSingleText single => [(single.Text, single.Text.Id)],
                SourceTextFolder folder => folder.Items.Select(i => (i, $"{folder.Id}.{i.Id}")),
                _ => [],
            };
            foreach (var (macro, id) in macros)
                Member(set, macro.Header["format"], $"Macro '{id}'", macro.Id, macro.Path, refusals);
        }
    }

    private static void Deliverables(FormatSet set, SourceDeliveryFolder folder, List<SourceRefusal> refusals)
    {
        foreach (var deliverable in folder.Deliverables)
            Member(set, deliverable.Header["format"], $"Deliverable '{deliverable.Id}'", deliverable.Id, deliverable.Path, refusals);
        foreach (var inner in folder.Folders)
            Deliverables(set, inner, refusals);
    }

    // ---- Inputs (part 02 § 8, D17–D19) -----------------------------------------------------------

    /// <summary>One input whose format passed the dialect and the lookup: which formats it may take, its channel, and its fields' titles.</summary>
    private static void CheckInput(FormatSet formats, SourceInput input, List<SourceRefusal> refusals)
    {
        var id = input.Id;
        var format = input.Json["format"];
        // D31: a nullable object counts as an object for form, a nullable list of text as a list of text.
        var schema = formats.Resolve(format);

        if (FormatSet.NameOf(format) == "markdown_inline")
            refusals.Add(FormatSentences.InputMarkdownInline(id));
        if (schema != null && schema.ContainsKey("anyOf"))
            refusals.Add(FormatSentences.InputChoiceOfShapes(id));

        switch (SourceLayout.Text(input.Json["channel"]))
        {
            case null:
                break;
            case "form":
                if (!(TypeOf(schema) == "object" && schema!["properties"] is JsonObject { Count: > 0 }))
                    refusals.Add(FormatSentences.InputFormNeedsObject(id));
                break;
            case var channel when TextChannels.Contains(channel):
                if (!IsTextName(format) && !(TypeOf(schema) == "array" && IsTextItems(formats, schema!)))
                    refusals.Add(FormatSentences.InputChannelNeedsText(id, channel));
                break;
            case var channel:
                refusals.Add(FormatSentences.InputChannelUnknown(id, channel));
                break;
        }

        new FieldTitles(formats, id, refusals).Walk(format, FormatTypes.Defs(format), "", 0);
    }

    /// <summary>D17: a text input's format is <c>plaintext</c> or <c>markdown_block</c>, by name or <c>$ref</c>.</summary>
    private static bool IsTextName(JsonNode? format) => FormatSet.NameOf(format) is "plaintext" or "markdown_block";

    /// <summary>
    /// D17: a list's <c>items</c> is <c>plaintext</c> or <c>markdown_block</c>, written as the name or
    /// through a <c>#/$defs</c> site of the list's file (in the dialect, a package's own definition can
    /// only be text by referring to one of the two).
    /// </summary>
    private static bool IsTextItems(FormatSet formats, JsonObject list) =>
        FormatTypes.KindOf(FormatTypes.Follow(list["items"], FormatTypes.Defs(list), formats).Schema) is "plaintext" or "markdown_block";

    /// <summary>A schema's one type (D31: a nullable object or list counts as one): its <c>type</c>, or the type of a nullable type pair.</summary>
    private static string? TypeOf(JsonObject? schema) =>
        schema?["type"] is JsonArray pair ? FormatTypes.NullablePair(pair) : FormatTypes.Text(schema?["type"]);

    /// <summary>
    /// S02-045 (D18): every property of what an input's format resolves to, through names, <c>#/$defs</c>,
    /// lists and unions, at any depth, that has no <c>title</c> where it is written nor on what it
    /// refers to; once per dotted path. A loop, the depth cap or <see cref="FormatTypes.SchemaBudget"/>
    /// schemas stop the walk.
    /// </summary>
    private sealed class FieldTitles(FormatSet formats, string id, List<SourceRefusal> refusals)
    {
        private readonly HashSet<JsonObject> open = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> said = new(StringComparer.Ordinal);
        private int budget = FormatTypes.SchemaBudget;

        public void Walk(JsonNode? node, JsonObject? defs, string prefix, int depth)
        {
            if (depth > SourceBounds.JsonDepth || --budget < 0)
                return;
            var (schema, inner) = FormatTypes.Follow(node, defs, formats);
            if (schema == null || !open.Add(schema))
                return;

            if (schema["anyOf"] is JsonArray members)
            {
                foreach (var member in members)
                    Walk(member, inner, prefix, depth + 1);
            }

            if (schema["items"] is JsonObject items)
                Walk(items, inner, prefix, depth + 1);

            if (schema["properties"] is JsonObject properties)
            {
                foreach (var (name, property) in properties)
                {
                    var field = prefix + name;
                    if (!HasTitle(property, inner) && said.Add(field))
                        refusals.Add(FormatSentences.InputFieldWithoutTitle(id, field));
                    Walk(property, inner, field + ".", depth + 1);
                }
            }

            open.Remove(schema);
        }

        private bool HasTitle(JsonNode? property, JsonObject? defs) =>
            FormatTypes.Text((property as JsonObject)?["title"]) != null
            || FormatTypes.Text(FormatTypes.Follow(property, defs, formats).Schema?["title"]) != null;
    }
}
