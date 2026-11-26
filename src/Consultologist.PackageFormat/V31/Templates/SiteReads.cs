using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 3.1): one row of § 3.1's table: the site in the table's words, the roots
/// it reads, and its list of what it reads as the table writes it (the sentence S05-020 quotes).
/// </summary>
internal sealed record SiteRow(string Words, IReadOnlySet<string> Roots, string Allowed);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 3.1, § 2.1 strict variables; part 02 § 2.1; part 06 § 10.1): what each
/// template site reads, as data, and the typing of one path read in a body: a head resolved with
/// <see cref="ScopeTree.Resolve"/> at the body's frame, the rest with <see cref="FormatReads.Read"/>; a <c>for</c>
/// variable typed by its list; <c>run.*</c>, <c>profile.*</c>, <c>snippet.*</c> and the loop positions by their own
/// rules. Never throws on package content; every walk is over a path's segments.
/// </summary>
internal static class SiteReads
{
    /// <summary>Every root a template may name at some site (§ 3.1), <c>value</c> (a renderer's) and <c>for</c> apart.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "node", "input", "check", "item", "pass", "param", "snippet", "part", "macro", "renderer", "run", "profile", "deliverable",
    };

    /// <summary>The roots as § 3.1's table writes them in its lists.</summary>
    private static readonly IReadOnlyDictionary<string, string> Written = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["node"] = "node.*",
        ["input"] = "input.*",
        ["check"] = "check.*",
        ["item"] = "item",
        ["pass"] = "pass",
        ["param"] = "param",
        ["snippet"] = "snippet.*",
        ["part"] = "part.*",
        ["macro"] = "macro.*",
        ["renderer"] = "the renderer. pipe",
        ["run"] = "run.*",
        ["profile"] = "profile.*",
        ["deliverable"] = "deliverable.*",
    };

    /// <summary>Part 05 § 3.1, row by row. A snippet body reads what its placer reads (Task 5 judges it at the placer's row).</summary>
    private static readonly IReadOnlyDictionary<TemplateSite, SiteRow> Table = new Dictionary<TemplateSite, SiteRow>
    {
        [TemplateSite.Prompt] = Row("prompt",
            ["node", "input", "check", "item", "pass", "param", "snippet", "part", "macro", "renderer", "run"],
            "node.*, input.*, check.*, item, pass, param (in functions), snippet.*, part.* (its own node's), macro.*, the renderer. pipe, run.*"),
        [TemplateSite.Rendered] = Row("rendered node",
            ["node", "input", "check", "item", "pass", "param", "snippet", "renderer", "run"],
            "node.*, input.*, check.*, item, pass, param, snippet.*, the renderer. pipe, run.*"),
        [TemplateSite.Renderer] = Row("renderer", [], "its piped value only"),
        [TemplateSite.SnippetBody] = Row("snippet body",
            ["node", "input", "check", "item", "pass", "param", "snippet", "part", "macro", "renderer", "run", "profile"],
            "what the template that places it reads"),
        [TemplateSite.PartText] = Row("part text",
            ["node", "input", "check", "item", "pass", "param", "snippet", "macro", "renderer", "run"],
            "what its node's prompt reads, macro.* and snippet.* included"),
        [TemplateSite.MacroInstruction] = Row("macro instruction",
            ["node", "input", "check", "item", "pass", "param", "snippet", "renderer", "run"],
            "what the placing prompt reads, snippet.* included, except macro.* and part.*"),
        [TemplateSite.MacroBody] = Row("macro body",
            ["node", "input", "check", "item", "pass", "param", "snippet", "renderer", "run", "profile"],
            "node.*, input.*, check.*, item, pass, param, snippet.*, the renderer. pipe, run.*, plus profile.*"),
        [TemplateSite.Deliverable] = Row("deliverable",
            ["node", "input", "snippet", "check", "profile", "run", "renderer"],
            "node.*, input.*, snippet.*, check.* (not its own), profile.*, run.*, the renderer. pipe"),
    };

    /// <summary>The sites whose <c>profile.*</c> read is S05-021, said of the node, rather than S05-020.</summary>
    private static readonly IReadOnlySet<TemplateSite> SentToAModel = new HashSet<TemplateSite>
    {
        TemplateSite.Prompt, TemplateSite.Rendered, TemplateSite.PartText,
    };

    /// <summary>The row of <paramref name="site"/>.</summary>
    public static SiteRow Of(TemplateSite site) => Table[site];

    /// <summary><paramref name="root"/> as § 3.1's table writes it (<c>deliverable.*</c>, <c>item</c>).</summary>
    public static string AsWritten(string root) => Written.GetValueOrDefault(root, root);

    /// <summary>Whether <paramref name="site"/> says a <c>profile.*</c> read with S05-021 (a node's prompt, part or <c>rendered</c> body).</summary>
    public static bool SaysProfileOfTheNode(TemplateSite site) => SentToAModel.Contains(site);

    private static SiteRow Row(string words, string[] roots, string allowed) =>
        new(words, new HashSet<string>(roots, StringComparer.Ordinal), allowed);
}

/// <summary>
/// #1504 (specVersion 31, area 07): a <c>for</c> variable in scope at a place in a body: its name and the element of
/// the list it iterates (<c>null</c> when the list is not typed, so reads through it are not judged).
/// </summary>
internal sealed record ForVariable(string Name, JsonNode? Element);

/// <summary>
/// #1504 (specVersion 31, area 07): the typing of one body's reads at its host's place. Each method returns the read
/// (<see cref="TypedRead"/>, its format <c>null</c> when it cannot be typed yet) or a refusal, never both, never
/// throwing.
/// </summary>
internal sealed class PathTyping
{
    private static readonly JsonNode Boolean = JsonValue.Create("boolean");
    private static readonly IReadOnlySet<string> Positions = new HashSet<string>(StringComparer.Ordinal) { "first", "last", "even", "odd" };

    private readonly TemplateHost host;
    private readonly ScopeTree tree;
    private readonly FormatSet formats;
    private readonly SourcePackage package;
    private readonly JsonNode? rendererInput;
    private readonly TemplateSite reads;

    /// <summary>
    /// The typing of <paramref name="host"/>'s reads; <paramref name="reads"/> is the row of § 3.1 they are held to: the
    /// host's own site, or for a snippet's body the site of the template that places it (§ 3.2).
    /// </summary>
    public PathTyping(TemplateHost host, ScopeTree tree, FormatSet formats, SourcePackage package, TemplateSite reads)
    {
        this.host = host;
        this.tree = tree;
        this.formats = formats;
        this.package = package;
        this.reads = reads;
        // A renderer's input that is no format this package can read is refused by the type rules (S02-017); its reads stay untyped here.
        rendererInput = host.Declaration is SourceText { Header: var header } && host.Site == TemplateSite.Renderer
            && FormatTypes.Shape(header["input"], formats) != FormatShape.Unknown
            ? header["input"]
            : null;
    }

    /// <summary>
    /// Types <paramref name="written"/> (a dotted access path as written) read at <paramref name="line"/> with
    /// <paramref name="scope"/>'s <c>for</c> variables (innermost last). Null with no refusal: a read this task leaves to
    /// another (a placed <c>part.*</c> or <c>macro.*</c>). In a renderer anything but <c>value</c> and its <c>for</c>
    /// variables is S05-029.
    /// </summary>
    public (TypedRead? Read, SourceRefusal? Refusal) Type(string written, int line, IReadOnlyList<ForVariable> scope)
    {
        var s = written.Split('.');
        var root = s[0];

        for (var i = scope.Count - 1; i >= 0; i--)
        {
            if (scope[i].Name == root)
                return Steps(written, line, root, null, scope[i].Element, s.Skip(1).ToList(), nullable: false);
        }

        if (root == "for")
            return LoopPosition(written, line, s, scope.Count > 0);

        if (host.Site == TemplateSite.Renderer)
        {
            // Part 05 § 5.1: a renderer reads only `value` (S05-029); a renderer named outside a pipe is still the call form.
            return root switch
            {
                "value" => Steps(written, line, root, null, rendererInput, s.Skip(1).ToList(), nullable: false),
                "renderer" => Refuse(DialectSentences.CallsRenderer(host.Subject, string.Join('.', s.Skip(1)), "value", line, host.Path)),
                _ => Refuse(DialectSentences.RendererReadsRoot(RendererId, SiteReads.AsWritten(root), host.Path)),
            };
        }

        var row = SiteReads.Of(reads);
        // An unknown root, or a root alone that names nothing by itself (`{{ profile }}`; `item` and `pass` do).
        if (!SiteReads.Known.Contains(root) || (s.Length == 1 && root is not ("item" or "pass")))
            return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, row.Words, host.Path));
        if (!row.Roots.Contains(root))
        {
            if (root == "profile" && SiteReads.SaysProfileOfTheNode(reads) && host.Declaration is SourceNode node)
                return Refuse(DialectSentences.ReadsProfileInPrompt(node.Id, s.Length > 1 ? s[1] : string.Empty, host.Path));
            return Refuse(DialectSentences.ReadsRoot(host.Subject, SiteReads.AsWritten(root), line, row.Words, row.Allowed, host.Path));
        }

        return root switch
        {
            // A renderer named outside a pipe (`{{ renderer.x }}`, or as a pipe's argument): only the pipe calls one.
            "renderer" => Refuse(DialectSentences.CallsRenderer(host.Subject, string.Join('.', s.Skip(1)), "value", line, host.Path)),
            "part" or "macro" => Unit(written, line, s),
            "snippet" => Snippet(written, line, s),
            "profile" => Profile(written, line, s),
            _ => Resolved(written, line, s),
        };
    }

    /// <summary>The id of the renderer whose body this is.</summary>
    private string RendererId => host.Declaration is SourceText text ? text.Id : host.Subject;

    // ---- by root ---------------------------------------------------------------------------------

    /// <summary><c>for.first</c>, <c>for.last</c>, <c>for.even</c>, <c>for.odd</c> (boolean) and <c>for.index</c> (a number), only inside a <c>for</c>.</summary>
    private (TypedRead?, SourceRefusal?) LoopPosition(string written, int line, string[] s, bool inFor)
    {
        if (!inFor || s.Length < 2)
            return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, "for", host.Path));
        if (!Positions.Contains(s[1]) && s[1] != "index")
            return (null, null); // A07-004, Task 2's: a body with that fault is not typed.
        JsonNode format = s[1] == "index" ? new JsonObject { ["type"] = "integer" } : Boolean.DeepClone();
        return Steps(written, line, "for." + s[1], null, format, s.Skip(2).ToList(), nullable: false);
    }

    /// <summary>
    /// <c>snippet.&lt;id&gt;.content</c> (text of the snippet's <c>format</c>), <c>snippet.&lt;folder&gt;.&lt;id&gt;.content</c>
    /// (<c>markdown_block</c>, as a folder item's <c>.content</c> is in a loop) and <c>.value</c> of a folder item (its
    /// header, of the folder's <c>format</c>); a bare snippet is S05-037, and one neither the function nor the package
    /// defines S05-035.
    /// </summary>
    private (TypedRead?, SourceRefusal?) Snippet(string written, int line, string[] s)
    {
        if (s.Length < 2)
            return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, SiteReads.Of(reads).Words, host.Path));

        var entry = host.Function?.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == s[1])
            ?? package.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == s[1]);
        switch (entry)
        {
            case null:
                return Refuse(DialectSentences.PlacesUndefinedSnippet(host.Subject, s[1], host.Path));
            case SourceSingleText single:
                if (s.Length == 2)
                    return Refuse(DialectSentences.ReadsBareSnippet(host.Subject, s[1], line, host.Path));
                if (s is [_, _, "content"])
                    return (Read(written, line, $"snippet.{s[1]}", null, single.Text.Header["format"]?.DeepClone(), false), null);
                return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, $"snippet.{s[1]}", host.Path));
            case SourceTextFolder folder:
                if (s.Length == 2 || (s.Length == 3 && folder.Items.Any(i => i.Id == s[2])))
                    return Refuse(DialectSentences.ReadsBareSnippet(host.Subject, string.Join('.', s.Skip(1)), line, host.Path));
                if (s.Length == 3 || folder.Items.All(i => i.Id != s[2]))
                    return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, $"snippet.{s[1]}", host.Path));
                var head = $"snippet.{s[1]}.{s[2]}";
                if (s is [_, _, _, "content"])
                    return (Read(written, line, head, null, JsonValue.Create("markdown_block"), false), null);
                if (s[3] == "value")
                    return Steps(written, line, head, null, folder.Index["format"], s.Skip(4).ToList(), nullable: false);
                return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, head, host.Path));
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// #1508 (ruled by the owner, ruling 12 of 2026-11-25): <c>part.&lt;id&gt;</c>, <c>macro.&lt;id&gt;</c> and
    /// <c>macro.&lt;folder&gt;.&lt;id&gt;</c> are placements, judged where they are placed (null with no refusal; one that
    /// names no unit is said there). A unit has no members, so any longer path is S05-011 (strict variables), naming the
    /// longest part of it that names a unit (<c>part.brief</c>, <c>macro.follow_up</c>, <c>macro.notes.hpi</c>,
    /// <c>macro.notes</c>), else the root (<c>part.*</c>, <c>macro.*</c>). A macro's marker outside its own instruction
    /// is such a path (its instruction's are the macro's to judge, <c>BodyCheck.MacroPath</c>).
    /// </summary>
    private (TypedRead?, SourceRefusal?) Unit(string written, int line, string[] s)
    {
        string what;
        if (s[0] == "part")
        {
            if (s.Length == 2)
                return (null, null);
            var declared = host.Declaration is SourceLeaf { Header: var header } && header["parts"] is JsonObject parts && parts.ContainsKey(s[1]);
            what = declared ? $"part.{s[1]}" : SiteReads.AsWritten("part");
        }
        else
        {
            var entry = host.Function?.Definitions.Macros.Items.FirstOrDefault(e => e.Id == s[1])
                ?? package.Definitions.Macros.Items.FirstOrDefault(e => e.Id == s[1]);
            var item = entry is SourceTextFolder folder && s.Length > 2 && folder.Items.Any(i => i.Id == s[2]);
            if ((s.Length == 2 && entry is SourceSingleText) || (s.Length == 3 && item))
                return (null, null);
            // A placement's form that names no macro is said as the placement says it (Context.PlaceMacro), so once.
            var placement = s.Length == 2 || (s.Length == 3 && s[2] != "content");
            what = (placement, entry) switch
            {
                (true, _) or (_, null) => SiteReads.AsWritten("macro"),
                (_, SourceTextFolder) when item => $"macro.{s[1]}.{s[2]}",
                _ => $"macro.{s[1]}",
            };
        }

        return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, what, host.Path));
    }

    /// <summary><c>profile.&lt;field&gt;</c>, one of part 06 § 10.1's five (else S06-028), typed by its format.</summary>
    private (TypedRead?, SourceRefusal?) Profile(string written, int line, string[] s)
    {
        if (s.Length < 2)
            return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, SiteReads.Of(reads).Words, host.Path));
        if (!SourceResolve.ProfileFields.TryGetValue(s[1], out var format))
            return Refuse(DialectSentences.ReadsNoProfileField(host.Subject, s[1], host.Path));
        return Steps(written, line, $"profile.{s[1]}", null, JsonValue.Create(format), s.Skip(2).ToList(), nullable: false);
    }

    /// <summary><c>node</c>, <c>check</c>, <c>input</c>, <c>item</c>, <c>pass</c>, <c>param</c> and <c>run</c>: the head as <see cref="ScopeTree.Resolve"/> gives it at the host's frame.</summary>
    private (TypedRead?, SourceRefusal?) Resolved(string written, int line, string[] s)
    {
        var resolved = tree.Resolve(new ReferenceString(written, s), host.Frame, host.Subject, host.Path, condition: false, host.Declaration as SourceNode);
        switch (resolved.Kind)
        {
            case ScopeHeadKind.Refused:
                return Refuse(resolved.Refusal ?? DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, s[0], host.Path));
            case ScopeHeadKind.Deferred:
                return (Read(written, line, s[0], null, null, false), null);
            case ScopeHeadKind.Format or ScopeHeadKind.Value:
                break;
            default:
                return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, s[0], host.Path));
        }

        var length = Math.Clamp(resolved.Length, 1, s.Length);
        var named = s[0] is "node" or "check" && s.Length >= 2 ? 2 : length;
        var head = string.Join('.', s.Take(named));
        var rest = s.Skip(length).ToList();
        if (resolved.Kind == ScopeHeadKind.Format && rest.Count == 0)
        {
            var member = FormatTypes.TextKind(resolved.Format, formats) != null ? ".content" : ".value";
            return Refuse(DialectSentences.ReadsBare(host.Subject, written, line, member, host.Path));
        }

        return resolved.Kind == ScopeHeadKind.Value
            ? Steps(written, line, head, resolved.Key, resolved.Format, rest, resolved.Nullable)
            : Read(written, line, head, resolved.Key, resolved.Format, rest, resolved.Nullable);
    }

    // ---- steps -----------------------------------------------------------------------------------

    /// <summary>Fields of a value already of <paramref name="format"/> (a run fact, a <c>for</c> variable, a renderer's <c>value</c>).</summary>
    private (TypedRead?, SourceRefusal?) Steps(string written, int line, string head, string? rooted, JsonNode? format, List<string> rest, bool nullable) =>
        Read(written, line, head, rooted, format, ["value", .. rest], nullable);

    /// <summary>
    /// <paramref name="steps"/> read on <paramref name="format"/> with <see cref="FormatReads.Read"/>; in a renderer a
    /// list's <c>.size</c> is a number (as the built-in renderers test it). A format not known yet (<c>null</c>) gives
    /// the read untyped.
    /// </summary>
    private (TypedRead?, SourceRefusal?) Read(string written, int line, string head, string? rooted, JsonNode? format, List<string> steps, bool nullable)
    {
        if (format is null)
            return (Read(written, line, head, rooted, null, nullable), null);

        if (host.Site == TemplateSite.Renderer && steps.Count > 1 && steps[^1] == "size"
            && FormatReads.Read(format, steps[..^1], formats) is { Fault: ReadFault.None } list
            && FormatTypes.Shape(list.Format, formats) == FormatShape.List)
        {
            return (Read(written, line, head, rooted, new JsonObject { ["type"] = "integer" }, nullable || list.Nullable), null);
        }

        var read = FormatReads.Read(format, steps, formats);
        switch (read.Fault)
        {
            case ReadFault.None:
                return (Read(written, line, head, rooted, read.Format, nullable || read.Nullable), null);
            case ReadFault.TitleOfPlainEnum:
                return Refuse(FormatReads.TitleOfPlainEnum(written.EndsWith(".title", StringComparison.Ordinal) ? written[..^".title".Length] : written, host.Path));
            default:
                return Refuse(DialectSentences.ReadsWhatIsNotThere(host.Subject, written, line, TypeStrings.Print(Lacking(format, steps), formats), host.Path));
        }
    }

    /// <summary>The format the failing step is read on: what the longest run of <paramref name="steps"/> that reads gives, else <paramref name="format"/>.</summary>
    private JsonNode? Lacking(JsonNode format, List<string> steps)
    {
        for (var count = steps.Count - 1; count >= 1; count--)
        {
            if (FormatReads.Read(format, steps[..count], formats) is { Fault: ReadFault.None } prefix)
                return prefix.Format;
        }

        return format;
    }

    private static TypedRead Read(string written, int line, string? head, string? rooted, JsonNode? format, bool nullable)
    {
        var canonical = head != null && rooted != null && rooted != head && written.StartsWith(head, StringComparison.Ordinal)
            ? rooted + written[head.Length..]
            : written;
        return new TypedRead(written, canonical, head, rooted, format, nullable, line);
    }

    private static (TypedRead?, SourceRefusal?) Refuse(SourceRefusal refusal) => (null, refusal);
}
