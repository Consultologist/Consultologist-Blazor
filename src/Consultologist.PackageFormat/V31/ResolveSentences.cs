namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): every sentence the resolver says, one method each, named after what it says, as
/// <see cref="ExpressionSentences"/> does for area 04. Each is the row of <c>v31/sentences.json</c> (part 04 § 10.1,
/// part 07 § 10) or the area's own <c>A05-NNN</c> with its placeholders filled in, character for character.
/// <c>&lt;site&gt;</c> is the subject of the place that reads (<see cref="ExpressionSite.Subject"/>, or D13's
/// <c>Loop 'x'</c>, <c>Case 'x'</c>, <c>Call 'x'</c>), and every refusal names the file it is about. No sentence
/// carries a value from an input or a model: only ids, keys, references, member names and type strings.
/// </summary>
internal static class ResolveSentences
{
    /// <summary>S04-036: <c>pass.number</c> read where no <c>while</c> encloses the reader.</summary>
    public static SourceRefusal PassOutsideWhile(string site, string path) =>
        new("S04-036", $"{site} reads pass.number, but it runs inside no while; pass is a while's pass.", path);

    /// <summary>S04-037: <c>pass.parent</c> read in an outermost <c>while</c>.</summary>
    public static SourceRefusal PassParentOutsideWhile(string site, string path) =>
        new("S04-037", $"{site} reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's pass.", path);

    /// <summary>S04-038: <c>pass.previous.node.&lt;id&gt;</c> naming a node that is not in the <c>while</c> <paramref name="loop"/>.</summary>
    public static SourceRefusal PassPreviousNotInWhile(string site, string id, string loop, string path) =>
        new("S04-038", $"{site} reads pass.previous.node.{id}, which is not in while '{loop}'; a pass reads the while's own nodes.", path);

    /// <summary>S04-039: a node <paramref name="id"/> read from outside the loop <paramref name="loop"/> it runs inside.</summary>
    public static SourceRefusal NodeInsideLoop(string site, string id, string loop, string path) =>
        new("S04-039", $"{site} reads node '{id}', which runs inside loop '{loop}'; read node.{loop}.", path);

    /// <summary>S04-040: <c>item.parent</c> read in an outermost loop.</summary>
    public static SourceRefusal ItemParentOutsideLoop(string site, string path) =>
        new("S04-040", $"{site} reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's item.", path);

    /// <summary>S04-050: the node <paramref name="id"/> of the function <paramref name="function"/> reads the input <paramref name="input"/>.</summary>
    public static SourceRefusal FunctionReadsInput(string id, string function, string input, string path) =>
        new("S04-050", $"Node '{id}' in function '{function}' reads input.{input}; a function reads only its parameters: add a parameter and pass it in with.", path);

    /// <summary>S04-051: the node <paramref name="id"/> of the function <paramref name="function"/> reads <paramref name="reference"/>, from outside it.</summary>
    public static SourceRefusal FunctionReadsOutside(string id, string function, string reference, string path) =>
        new("S04-051", $"Node '{id}' in function '{function}' reads {reference}; a function reads only its parameters: add a parameter and pass it in with.", path);
}
