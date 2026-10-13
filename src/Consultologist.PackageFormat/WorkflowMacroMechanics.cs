namespace Consultologist.PackageFormat;

/// <summary>
/// The mechanics sentence of a macro's slot prompt (package-format-v25.md
/// § The mechanics sentence; design § 5.3): <c>macro.&lt;id&gt;.prompt</c> renders as
/// the macro's <c>instruction</c> followed by this sentence, a constant of
/// this format version — the same words for every macro, published with the
/// format and pinned by a test to the registry's text, so the record can say
/// exactly what the model was told.
/// </summary>
public static class WorkflowMacroMechanics
{
    /// <summary>The sentence as published, with <c>&lt;id&gt;</c> as the placeholder for the macro id.</summary>
    public const string Sentence =
        "If so, emit [[slot:<id>]] verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward, and only when it applies.";

    /// <summary>The slot prompt of one macro: its instruction, a blank line, the mechanics sentence naming its marker.</summary>
    public static string Prompt(string macroId, string instruction) =>
        $"{instruction.TrimEnd()}\n\n{Sentence.Replace("<id>", macroId, StringComparison.Ordinal)}";
}
