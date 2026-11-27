namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1543 (ruling 2, D2): what a template tag is, for the rules that read the text beside it. <see cref="Statement"/> an
/// <c>if</c>, a <c>for</c>, an <c>else</c>, an <c>end</c> or an assignment (and a comment); <see cref="Placement"/> a unit of
/// part 05 § 2.6; <see cref="Renderer"/> an output whose pipe ends in a package or built-in renderer; <see cref="BlockRead"/>
/// an output typed <c>markdown_block</c> with no renderer at its end; <see cref="InlineRead"/> any other output.
/// </summary>
public enum TagRole
{
    Statement,
    Placement,
    Renderer,
    BlockRead,
    InlineRead,
}

/// <summary>
/// #1543 (ruling 2, D2): the one place a body's tags get their <see cref="TagRole"/>, from the checker's own typing of the
/// body (<see cref="CheckedBody.Writes"/>: the format each output writes and whether a renderer ends its pipe, and
/// <see cref="CheckedBody.Placements"/>), never from a second typing. What the checker did not type takes the role that
/// says least, <see cref="TagRole.Statement"/>: a tag of a file the checker refused or never typed (a snippet no template
/// places), and an output whose format it could not say. A file read at several places (a snippet placed twice) takes, per
/// tag, the role that says least of those it has there (two reads of different kinds are a <see cref="TagRole.BlockRead"/>;
/// any other two different roles a <see cref="TagRole.Statement"/>).
/// </summary>
internal static class TagRoles
{
    /// <summary>The role of each tag of <paramref name="body"/> (<see cref="TemplateSyntax.Tags"/>), by the offset of its <c>{{</c>.</summary>
    public static IReadOnlyDictionary<int, TagRole> Of(CheckedBody body, ScopeTree tree)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(tree);
        var formats = tree.FormatsOf(body.Host.Frame);
        var syntax = body.Syntax;
        var roles = syntax.Tags.ToDictionary(t => t.Start, _ => TagRole.Statement);
        var statements = syntax.Ifs.Select(i => i.Tag.Start).Concat(syntax.Fors.Select(f => f.Tag.Start)).ToHashSet();
        var outputs = new Dictionary<int, TagRole>();
        var placement = 0;
        for (var i = 0; i < syntax.Outputs.Count; i++)
        {
            var output = syntax.Outputs[i];
            TagRole role;
            if (placement < body.Placements.Count && PackageTemplates.Places(output, body.Placements[placement]))
            {
                placement++;
                role = TagRole.Placement;
            }
            else
            {
                role = i < body.Writes.Count ? Written(body.Writes[i], formats) : TagRole.Statement;
            }

            var start = output.Tag.Start;
            outputs[start] = outputs.TryGetValue(start, out var other) ? Merge(other, role) : role;
        }

        foreach (var (start, role) in outputs)
        {
            if (!statements.Contains(start))
                roles[start] = role;
        }

        return roles;
    }

    /// <summary>
    /// The roles of every file the checker read among <paramref name="bodies"/> (each top body and every body it places,
    /// <see cref="PackageTemplates.Placed"/>), built once for a whole check, a file of <paramref name="refused"/> typing none.
    /// </summary>
    public static SiteRoles Of(IEnumerable<CheckedBody> bodies, ScopeTree tree, IReadOnlySet<string> refused) => new(bodies, tree, refused);

    /// <summary>What an output that places nothing is, by what the checker says it writes.</summary>
    private static TagRole Written(WrittenOutput written, FormatSet formats)
    {
        // #1543 (D2, D5): a renderer's role is an output whose pipe ends in one; a built-in step after it gives the role
        // of the final value's type, as for any other output.
        if (written.Output.Pipes is [.., var last] && last.Name.StartsWith(DialectRules.RendererRoot + ".", StringComparison.Ordinal))
            return TagRole.Renderer;
        if (written.Format is not { } format)
            return TagRole.Statement;
        return FormatTypes.TextKind(format, formats) == "markdown_block" ? TagRole.BlockRead : TagRole.InlineRead;
    }

    /// <summary>The role that says least of two a tag has: two reads a block read, any other two different roles a statement.</summary>
    internal static TagRole Merge(TagRole a, TagRole b) =>
        a == b ? a
        : a is TagRole.BlockRead or TagRole.InlineRead && b is TagRole.BlockRead or TagRole.InlineRead ? TagRole.BlockRead
        : TagRole.Statement;
}

/// <summary>
/// #1543 (ruling 2, D2, D5): the path → roles map of one check, built once and read by each rule that judges the text beside
/// a tag (publish's S05-065, S05-066 and S05-067; the migration's hard breaks). For each file the checker read, with the
/// text it read, each tag's role as <see cref="TagRoles"/> gives it, merged over every place it read it, and what each
/// output reads as written (its piped head, without its pipes).
/// </summary>
internal sealed class SiteRoles
{
    private readonly Dictionary<(string Path, string Text), Dictionary<int, TagRole>> roles = [];
    private readonly Dictionary<(string Path, string Text), Dictionary<int, string>> reads = [];
    private readonly IReadOnlySet<string> refused;

    internal SiteRoles(IEnumerable<CheckedBody> bodies, ScopeTree tree, IReadOnlySet<string> refused)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(refused);
        this.refused = refused;
        var read = bodies.SelectMany(PackageTemplates.Placed).Select(p => p.Body)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<CheckedBody>();
        foreach (var body in read)
        {
            var key = (body.Host.Path, body.Syntax.Text);
            var typed = TagRoles.Of(body, tree);
            if (!roles.TryGetValue(key, out var merged))
            {
                roles[key] = new Dictionary<int, TagRole>(typed);
                reads[key] = body.Syntax.Outputs.GroupBy(o => o.Tag.Start).ToDictionary(g => g.Key, g => g.First().Path ?? g.First().Head);
                continue;
            }

            foreach (var (start, role) in typed)
                merged[start] = merged.TryGetValue(start, out var other) ? TagRoles.Merge(other, role) : TagRole.Statement;
        }
    }

    /// <summary>
    /// The role of each tag of <paramref name="text"/>, the body of the file <paramref name="path"/>; every tag
    /// <see cref="TagRole.Statement"/> when the file is refused or the checker read it nowhere with that text.
    /// </summary>
    public Func<TemplateTag, TagRole> Of(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        if (refused.Contains(path) || !roles.TryGetValue((path, text), out var merged))
            return _ => TagRole.Statement;
        return tag => merged.GetValueOrDefault(tag.Start, TagRole.Statement);
    }

    /// <summary>What the output in <paramref name="tag"/> of the file <paramref name="path"/> reads, as written without its pipes; <c>null</c> for no output the checker read.</summary>
    public string? ReadOf(string path, string text, TemplateTag tag) =>
        reads.TryGetValue((path, text), out var outputs) ? outputs.GetValueOrDefault(tag.Start) : null;
}
