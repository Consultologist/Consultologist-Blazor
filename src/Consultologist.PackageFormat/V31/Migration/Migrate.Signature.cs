using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1510 (#1509 comment 7207709261, item 2; F-D4, F-D5′): the signature where 30 appended it. 30 left the signature and the
/// line before it out when the profile had none (its composer skipped an empty block; a deliverable macro's end was trimmed).
/// 31 tests no read in a body <c>if</c> (S05-007) and no JSON Logic reads the profile (S03-018), so the one accepted form
/// that tests the trimmed signature is a package renderer, whose body may test its own <c>value</c> (part 05 § 5.1): the
/// signature is piped to it in place of the line before it and the read. Its <c>input</c> and its <c>format</c> are the
/// signature's <c>markdown_block</c> (#1543 ruling 2, D4; a renderer's text is never escaped, D7, so the signature is 30's
/// bytes); each use shares its line, so it warns S05-061, which stays a warning, as the renderer's output begins with its
/// own line endings. Each renderer is written once,
/// into a package that needs it, listed in <c>renderers/index.json</c> and said (A10-089).
/// </summary>
internal static partial class Migrate
{
    /// <summary>The signature as 30 wrote it, read once.</summary>
    internal const string SignatureRead = "{{ profile.signature }}";

    /// <summary>
    /// The pipe that writes the signature after <paramref name="before"/> (<c>\n\n</c>, a blank line, or <c>\n</c>, a line
    /// ending) only when it is not blank, its renderer written when it is the first use.
    /// </summary>
    internal static string SignatureAfter(Migrated m, string before)
    {
        var (id, label) = before switch
        {
            "\n\n" => ("signature_after_blank_line", "Signature after a blank line"),
            "\n" => ("signature_after_line_end", "Signature after a line end"),
            _ => throw new ArgumentOutOfRangeException(nameof(before)),
        };

        // No 30 file becomes a package renderer (30's were the engine's), so the id is the migration's own once written.
        var file = $"renderers/{id}.md";
        if (!m.Sources.ContainsKey(file))
        {
            m.Put(file, new JsonObject
            {
                ["label"] = label,
                ["kind"] = "rendered",
                ["description"] = null,
                ["input"] = "markdown_block",
                ["format"] = "markdown_block",
            }, $"{{{{ if value != null && (value | string.strip) != \"\" }}}}{before}{{{{ value }}}}{{{{ end }}}}");
            m.Items("renderers").Add(id);
            m.Notice(MigrationSentences.SignatureThroughRenderer(id));
        }

        return $"{{{{ profile.signature | renderer.{id} }}}}";
    }

    /// <summary>
    /// A deliverable macro's body (its end trimmed as 30 trimmed it) whose last read is the signature after a blank line or
    /// one line ending: that line end and the read become <see cref="SignatureAfter"/>'s pipe. Any other body, a signature
    /// with text after it (which 30 kept) among them, is returned as it is.
    /// </summary>
    internal static string SignatureAtTheEnd(Migrated m, string body)
    {
        if (!body.EndsWith(SignatureRead, StringComparison.Ordinal))
            return body;
        var text = body[..^SignatureRead.Length];
        var before = text.EndsWith("\n\n", StringComparison.Ordinal) && !text.EndsWith("\n\n\n", StringComparison.Ordinal) ? "\n\n"
            : text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal) ? "\n"
            : null;
        return before is null ? body : text[..^before.Length] + SignatureAfter(m, before);
    }
}
