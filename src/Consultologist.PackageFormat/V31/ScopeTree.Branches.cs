using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what a <c>case</c> switches on (part 04 § 5.2), read outside the case.
/// <paramref name="Resolved"/> is its subject resolved (<see cref="ScopeHeadKind.Refused"/>, with the sentence, when
/// the subject is refused; <see cref="ScopeHeadKind.Deferred"/> when it reads a loop whose own source is refused), and
/// <paramref name="Reference"/> the subject as written. <paramref name="Format"/> is the type read and
/// <paramref name="Nullable"/> whether it can be null (a nullable format, an input not required, a node not
/// <see cref="ScopeTree.AlwaysValued"/>); both are unset when the subject is refused or deferred.
/// <paramref name="Values"/> are the enum's values in the format's order, null when the format is no enumerated string.
/// </summary>
internal sealed record CaseSubject(Resolved Resolved, string Reference, JsonNode? Format, bool Nullable, IReadOnlyList<string>? Values);

/// <summary>#1502 (specVersion 31, area 05): branches (part 04 § 5): what always has a value (§§ 5.3, 7.6; D14), and what a <c>case</c> switches on.</summary>
public sealed partial class ScopeTree
{
    /// <summary>
    /// Whether <paramref name="node"/> always has a value when it is reached (part 04 § 7.6's "always valued"): a leaf
    /// (a call included) with no <c>if</c>; a <c>for</c>; a <c>while</c> that runs its first pass unchecked
    /// (<c>checkFirst</c> false); an <c>if</c> with an <c>else</c> whose two <c>ret</c>s are always valued; a
    /// <c>case</c> with no empty branch whose <c>ret</c>s (its <c>else</c>'s too) are. A <c>ret</c> that names no node
    /// of its body is not. A node that is not may be skipped, and what it holds read as null (D14). Bounded by the
    /// reader's nesting limit. A null argument is the caller's error.
    /// </summary>
    public static bool AlwaysValued(SourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            SourceLeaf leaf => leaf.Header["if"] is null,
            SourceFor => true,
            SourceWhile loop => !loop.CheckFirst,
            SourceIf branch => branch.Else is { } otherwise && Returns(branch.Then) && Returns(otherwise),
            SourceCase choice => choice.Branches.All(b => Returns(b.Body)) && (choice.Else is not { } rest || Returns(rest)),
            _ => false,
        };

        static bool Returns(SourceBlock block) =>
            block.Ret is { } ret && block.Nodes.FirstOrDefault(n => n.Key == ret) is { } named && AlwaysValued(named);
    }

    /// <summary>
    /// The values of <paramref name="c"/>'s empty branches (<c>"nodes": []</c>, <c>"ret": null</c>), in written order:
    /// those for which the case reads as skipped (part 04 § 5.2, <i>"case '&lt;id&gt;': arm for &lt;value&gt; does
    /// nothing"</i>). A null argument is the caller's error.
    /// </summary>
    public static IReadOnlyList<string> EmptyValues(SourceCase c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.Branches.Where(b => b.Body.Nodes.Count == 0 && b.Body.Ret == null).SelectMany(b => b.When).ToList();
    }

    /// <summary>
    /// What <paramref name="c"/> switches on: its subject resolved at the body the case is listed in, as a loop's
    /// <c>in</c> is (D13): not a reference, A05-001; <see cref="Resolve"/>'s refusal; S03-014 for what names nothing or a
    /// member or field that does not read; S02-018 for an input read bare. A case not of this package is the caller's
    /// error (<see cref="ArgumentException"/>).
    /// </summary>
    internal CaseSubject Subject(SourceCase c)
    {
        var frame = FrameOf(c);
        var path = SourceLayout.Join(c.Path, "index.json");
        if (ReferenceString.Parse(c.Case) is not { } reference)
            return new CaseSubject(Refused(ResolveSentences.NotAReference("Case", c.Id, "case", c.Case, path)), c.Case, null, false, null);

        var (head, read, refusal) = Typed(reference, frame, $"Case '{c.Id}'", path, c);
        if (refusal != null)
            return new CaseSubject(Refused(refusal), c.Case, null, false, null);
        if (read == null)
            return new CaseSubject(head, c.Case, null, false, null);

        var formats = FormatsOf(frame);
        var nullable = head.Nullable || read.Nullable || FormatTypes.IsNullable(read.Format, formats);
        var values = FormatTypes.Shape(read.Format, formats) == FormatShape.Enum ? ExpressionChecker.ValuesOf(read.Format, formats) : [];
        return new CaseSubject(head, reference.Text, read.Format, nullable, values.Count == 0 ? null : values);
    }

    /// <summary>
    /// <paramref name="reference"/>, a reference string (an <c>in</c>, a <c>case</c>), resolved at <paramref name="frame"/>
    /// and typed (part 02 § 2.1): the head, and the read when it is typed. The refusal is <see cref="Resolve"/>'s, S03-014
    /// for a head that names nothing or a read that does not type, or S02-018 for an input read bare; with neither a read
    /// nor a refusal, the head is <see cref="ScopeHeadKind.Deferred"/>.
    /// </summary>
    private (Resolved Head, ReadResult? Read, SourceRefusal? Refusal) Typed(
        ReferenceString reference, ScopeFrame frame, string subject, string path, SourceNode reader)
    {
        var head = Resolve(reference, frame, subject, path, false, reader);
        switch (head.Kind)
        {
            case ScopeHeadKind.Refused:
                return (head, null, head.Refusal);
            case ScopeHeadKind.Deferred:
                return (head, null, null);
            case ScopeHeadKind.Format or ScopeHeadKind.Value:
                break;
            default:
                return (head, null, ResolveSentences.DoesNotExist(subject, reference.Text, path));
        }

        var s = reference.Segments;
        var rest = s.Skip(Math.Clamp(head.Length, 0, s.Count)).ToList();
        List<string> steps = head.Kind == ScopeHeadKind.Value ? ["value", .. rest] : rest;
        var read = FormatReads.Read(head.Format, steps, FormatsOf(frame));
        if (read.Fault == ReadFault.Bare && s[0] == "input")
            return (head, null, FormatReads.BareInput(s[1], path));
        if (read.Fault != ReadFault.None)
            return (head, null, ResolveSentences.DoesNotExist(subject, reference.Text, path));
        return (head, read, null);
    }
}
