namespace Consultologist.PackageFormat;

/// <summary>
/// The custom tier's prompt (package-format-v25-design.md § 3.4): the rendered
/// prompt, a blank line, the catalogue's trailer sentence, a newline, the
/// format's schema as canonical JSON — one layout, so the bytes sent (and
/// hashed as the node's input) are the same for the same prompt and shape.
/// The classification-trailer discipline, one producer over.
/// </summary>
public static class WorkflowCustomPrompt
{
    public static string Compose(string rendered, string trailer, string schemaCanonicalJson) =>
        $"{rendered.TrimEnd()}\n\n{trailer}\n{schemaCanonicalJson}";
}
