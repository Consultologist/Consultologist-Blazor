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
    /// <summary>A05-001 (D13): <paramref name="member"/> (<c>in</c>, <c>case</c>, <c>with &lt;param&gt;</c>) of <paramref name="kind"/> <paramref name="id"/> is text that is not a reference.</summary>
    public static SourceRefusal NotAReference(string kind, string id, string member, string text, string path) =>
        new("A05-001", $"{kind} '{id}' {member} '{text}' is not a reference; a reference is a root, then names joined by dots.", path);

    /// <summary>S03-014 (D13): a reference string whose head names nothing at <paramref name="site"/>, or whose member or field does not read.</summary>
    public static SourceRefusal DoesNotExist(string site, string reference, string path) =>
        new("S03-014", $"{site} reads {reference}, which doesn't exist here.", path);

    /// <summary>S04-017: <paramref name="member"/> (<c>in</c>, <c>case</c>, <c>with &lt;param&gt;</c>) written as an object or a list, where a reference is.</summary>
    public static SourceRefusal IsAnExpression(string kind, string id, string member, string path) =>
        new("S04-017", $"{kind} '{id}' {member} is an expression; it names one readable thing: compute the value in a computed node and name that.", path);

    /// <summary>S04-025: the loop <paramref name="id"/> iterates <paramref name="reference"/>, of the type string <paramref name="format"/>, which is no list.</summary>
    public static SourceRefusal IteratesNoList(string id, string reference, string format, string path) =>
        new("S04-025", $"Loop '{id}' iterates {reference}, which is {format}; only a list can be iterated.", path);

    /// <summary>S04-026: the loop <paramref name="id"/> iterates <c>item.value.&lt;field&gt;</c> with no loop around it.</summary>
    public static SourceRefusal IteratesItemOutsideLoop(string id, string field, string path) =>
        new("S04-026", $"Loop '{id}' iterates item.value.{field}, but it runs inside no loop; item.value names the enclosing loop's item.", path);

    /// <summary>S04-027: the loop <paramref name="id"/> iterates <c>node.&lt;node&gt;.value.&lt;field&gt;</c>, which runs inside the loop <paramref name="loop"/> it is not in.</summary>
    public static SourceRefusal IteratesNodeInsideLoop(string id, string node, string field, string loop, string path) =>
        new("S04-027", $"Loop '{id}' iterates node.{node}.value.{field}; {node} runs inside loop '{loop}' — a loop iterates a node outside it, or in an enclosing loop's body.", path);

    /// <summary>S04-028: <c>itemLabel</c> is no property of the element of <paramref name="reference"/>; <paramref name="fields"/> are its properties in written order (<c>none</c> for an element that is no object).</summary>
    public static SourceRefusal ItemLabelNotAField(string id, string field, string reference, string fields, string path) =>
        new("S04-028", $"Loop '{id}' itemLabel '{field}' is not a field of the elements of {reference} (fields: {fields}).", path);

    /// <summary>S04-029: <c>itemLabel</c> names a property the element does not require, or that may be null.</summary>
    public static SourceRefusal ItemLabelMayBeMissing(string id, string field, string path) =>
        new("S04-029", $"Loop '{id}' itemLabel '{field}' may be missing or null; itemLabel names a field every element has as a string.", path);

    /// <summary>S04-030: <c>itemLabel</c> names a property of the type string <paramref name="format"/>, which is no string.</summary>
    public static SourceRefusal ItemLabelNotAString(string id, string field, string format, string path) =>
        new("S04-030", $"Loop '{id}' itemLabel '{field}' is {format}; itemLabel names a string field.", path);

    /// <summary>S04-035: the body node <paramref name="id"/> of the while <paramref name="loop"/> is not its <c>ret</c> and nothing in the while reads it.</summary>
    public static SourceRefusal WhileNodeUnused(string id, string loop, string path) =>
        new("S04-035", $"Node '{id}' in while '{loop}' is neither returned nor read; name it with ret, read it from a body node or from the check.", path);

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
