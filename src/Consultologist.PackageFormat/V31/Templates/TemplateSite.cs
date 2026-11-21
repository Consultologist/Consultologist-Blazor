namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 3.1): the eight places a template is written in a package, each a row of
/// § 3.1's table of what it reads: an agent's prompt, a <c>rendered</c> node's body, a renderer's body, a snippet's
/// body, a part's text, a macro's instruction, a macro's body and a deliverable's body.
/// </summary>
public enum TemplateSite
{
    Prompt,
    Rendered,
    Renderer,
    SnippetBody,
    PartText,
    MacroInstruction,
    MacroBody,
    Deliverable,
}
