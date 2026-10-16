namespace Consultologist.PackageFormat;

/// <summary>
/// The mechanics sentence of a macro's slot prompt (package-format-v25.md
/// § The mechanics sentence; design § 5.3): <c>macro.&lt;id&gt;.prompt</c> renders as
/// the macro's <c>instruction</c> — itself rendered, #1044 — followed by this
/// sentence, a constant of this format version — the same words for every
/// macro, published with the format and pinned by a test to the registry's
/// text, so the record can say exactly what the model was told. #1044: the
/// sentence names the read the model writes, <c>{{ macro.&lt;id&gt;.text }}</c>,
/// and the reply renders it (package-format-v25.md § The reply is a template);
/// it is appended AFTER the instruction renders, so the read reaches the model
/// as written.
/// </summary>
public static class WorkflowMacroMechanics
{
    /// <summary>The sentence as published, with <c>&lt;id&gt;</c> as the placeholder for the macro id.</summary>
    public const string Sentence =
        "If so, write {{ macro.<id>.text }} verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward.";

    /// <summary>The slot prompt of one macro: its instruction (already rendered), a blank line, the mechanics sentence naming its read.</summary>
    public static string Prompt(string macroId, string renderedInstruction) =>
        $"{renderedInstruction.TrimEnd()}\n\n{Sentence.Replace("<id>", macroId, StringComparison.Ordinal)}";
}
