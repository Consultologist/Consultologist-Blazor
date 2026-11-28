namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1543 (ruling 2, D5, D6): a <c>markdown_block</c> read stands alone on its line. In every Markdown body site of
/// <see cref="Context.LineEndings"/> (deliverables, <c>rendered</c> nodes, macros, snippets and renderers of a Markdown
/// format, and every snippet folder's item; never a prompt or a part, which are not Markdown values), an output whose role
/// is <see cref="TagRole.BlockRead"/> (<see cref="TagRoles"/>: typed <c>markdown_block</c>, no renderer at its pipe's end)
/// with text before it on its line is refused (S05-066), and with text after it (S05-067). What is text is
/// <see cref="MarkdownLineEndings.TextBeside"/>'s: any character but a space or a tab outside every tag and comment, or an
/// inline read; a statement, a comment, a placement, a renderer-ended output or another block read beside it is not.
/// </summary>
public static partial class SourceTemplates
{
    private sealed partial class Context
    {
        /// <summary>
        /// S05-066 and S05-067 for each block read with text beside it in each site, in the order of the sites, then of the
        /// tags; the tags' roles from <paramref name="roles"/>, so a file refused before them is judged nowhere.
        /// </summary>
        public void BlockReads(SiteRoles roles)
        {
            foreach (var (subject, path, text) in Sites())
            {
                // The migration's separation (D7) reads the same finder, so what is refused here is what it separates.
                foreach (var (tag, before, after) in MarkdownLineEndings.BlockReadsBeside(text, roles.Of(path, text)))
                {
                    var read = roles.ReadOf(path, text, tag) ?? text[tag.Start..tag.End];
                    if (before)
                        Refusals.Add(DialectSentences.TextBeforeBlockRead(subject, read, tag.Line, path));
                    if (after)
                        Refusals.Add(DialectSentences.TextAfterBlockRead(subject, read, tag.Line, path));
                }
            }
        }
    }
}
