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
    /// The roles of the tags of <paramref name="text"/>, the body of the file <paramref name="path"/>, as the checker typed
    /// it wherever it read it among <paramref name="bodies"/> (each top body and every body it places,
    /// <see cref="PackageTemplates.Placed"/>); every tag <see cref="TagRole.Statement"/> when <paramref name="refused"/>
    /// holds the file or the checker read it nowhere.
    /// </summary>
    public static Func<TemplateTag, TagRole> Of(IEnumerable<CheckedBody> bodies, ScopeTree tree, string path, string text, IReadOnlySet<string> refused)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(refused);
        if (refused.Contains(path))
            return _ => TagRole.Statement;
        var read = bodies.SelectMany(PackageTemplates.Placed).Select(p => p.Body)
            .Where(b => b.Host.Path == path && b.Syntax.Text == text)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<CheckedBody>();
        Dictionary<int, TagRole>? merged = null;
        foreach (var body in read)
        {
            var roles = Of(body, tree);
            if (merged is null)
            {
                merged = new Dictionary<int, TagRole>(roles);
                continue;
            }

            foreach (var (start, role) in roles)
                merged[start] = merged.TryGetValue(start, out var other) ? Merge(other, role) : TagRole.Statement;
        }

        return merged is null ? _ => TagRole.Statement : tag => merged.GetValueOrDefault(tag.Start, TagRole.Statement);
    }

    /// <summary>What an output that places nothing is, by what the checker says it writes.</summary>
    private static TagRole Written(WrittenOutput written, FormatSet formats)
    {
        if (written.Rendered)
            return TagRole.Renderer;
        if (written.Format is not { } format)
            return TagRole.Statement;
        return FormatTypes.TextKind(format, formats) == "markdown_block" ? TagRole.BlockRead : TagRole.InlineRead;
    }

    /// <summary>The role that says least of two a tag has: two reads a block read, any other two different roles a statement.</summary>
    private static TagRole Merge(TagRole a, TagRole b) =>
        a == b ? a
        : a is TagRole.BlockRead or TagRole.InlineRead && b is TagRole.BlockRead or TagRole.InlineRead ? TagRole.BlockRead
        : TagRole.Statement;
}
