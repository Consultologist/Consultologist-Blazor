using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1501 (specVersion 31, area 04): the kinds of place a JSON Logic rule is written (part 03 § 3; D5).</summary>
public enum ExpressionSiteKind
{
    /// <summary>A production leaf's <c>if</c>.</summary>
    NodeIf,

    /// <summary>An <c>if</c> flow node's <c>if</c>.</summary>
    FlowIf,

    /// <summary>A <c>while</c>'s <c>while</c>.</summary>
    While,

    /// <summary>A <c>for</c>'s or a <c>while</c>'s <c>until</c>.</summary>
    Until,

    /// <summary>A check's <c>if</c>.</summary>
    CheckIf,

    /// <summary>A deliverable's <c>if</c> (S03-020, S03-021).</summary>
    DeliverableIf,

    /// <summary>A part's <c>if</c>.</summary>
    PartIf,

    /// <summary>A macro's, or a macro folder's, <c>if</c> (S03-022).</summary>
    MacroIf,

    /// <summary>A single snippet's <c>if</c> (D5): read as a macro's, without the <c>param</c> rule.</summary>
    SnippetIf,

    /// <summary>A <c>computed</c> node's <c>expression</c>.</summary>
    Expression,
}

/// <summary>
/// #1501 (specVersion 31, area 04): one place a rule is written. <paramref name="Subject"/> is D4's <c>&lt;site&gt;</c>
/// (<c>Node 'gate'</c>, <c>Part 'p' of node 'x'</c>, <c>Computed node 'total'</c> (<c>Check 'x'</c> for a check), with <c> in function 'f'</c> inside a
/// function); <paramref name="Member"/> is <c>if</c>, <c>while</c>, <c>until</c> or <c>expression</c>; <paramref name="Path"/>
/// is the file a refusal names. <paramref name="ComputedId"/> and <paramref name="Declared"/> (the node's format as
/// written) are set for an <see cref="ExpressionSiteKind.Expression"/> only. #1505 (area 08; D1): <paramref name="InChecks"/>
/// is true for a rule of a node under <c>checks/</c>, the one place JSON Logic reads <c>deliverable.*</c>.
/// </summary>
public sealed record ExpressionSite(ExpressionSiteKind Kind, string Subject, string Member, string Path, string? ComputedId, JsonNode? Declared, bool InChecks = false);

/// <summary>#1501 (specVersion 31, area 04): what the start of a path names at a site (see <see cref="ScopeHead"/>).</summary>
public enum ScopeHeadKind
{
    /// <summary>A thing read through <c>.value</c>, <c>.content</c> or <c>.title</c> (an input, a node, a parameter): the rest of the path goes to <see cref="FormatReads.Read"/>.</summary>
    Format,

    /// <summary>Already a value of the format (a run fact): the rest of the path is its fields.</summary>
    Value,

    /// <summary>No type yet (D2): a read through it has no type, and fits any operand.</summary>
    Deferred,

    /// <summary>Nothing there (S03-014).</summary>
    Unknown,

    /// <summary>The scope refuses the read with its own sentence.</summary>
    Refused,
}

/// <summary>
/// #1501 (specVersion 31, area 04): what an <see cref="IExpressionScope"/> says of a path: the first
/// <paramref name="Length"/> segments are the head; <paramref name="Format"/> (as written) and
/// <paramref name="Nullable"/> are its type for <see cref="ScopeHeadKind.Format"/> and <see cref="ScopeHeadKind.Value"/>;
/// <paramref name="Refusal"/> is the sentence of a <see cref="ScopeHeadKind.Refused"/> one.
/// </summary>
public sealed record ScopeHead(ScopeHeadKind Kind, int Length, JsonNode? Format, bool Nullable, SourceRefusal? Refusal);

/// <summary>
/// #1501 (specVersion 31, area 04; D2): who can be read at one site, and their types. Area 05 writes the scope of
/// <c>node</c>, <c>check</c>, <c>item</c>, <c>pass</c> and <c>param</c>; the root and site rules of part 03
/// (S03-013, S03-015, S03-018 to S03-022) are judged before a scope is asked.
/// </summary>
public interface IExpressionScope
{
    /// <summary>What the start of the path <paramref name="segments"/> (split at the dots, never empty) names here.</summary>
    ScopeHead Head(IReadOnlyList<string> segments);
}

/// <summary>
/// #1501 (specVersion 31, area 04): a rule's type: its <paramref name="Format"/> (null: not known, D2), whether it
/// is declared nullable (D25), and, for a <c>.title</c> read, the titles a literal is held to.
/// </summary>
public sealed record ExpressionType(JsonNode? Format, bool Nullable, IReadOnlyList<string>? Titles = null);
