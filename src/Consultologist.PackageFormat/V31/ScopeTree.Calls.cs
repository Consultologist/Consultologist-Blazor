using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): one argument of a <c>call</c> (part 04 § 7.7), read where the call is.
/// <paramref name="Head"/> is the reference resolved (<see cref="ScopeHeadKind.Refused"/> with the sentence when it is
/// refused); <paramref name="Format"/> the type passed, <c>T | null</c> when the value read may be null, null when the
/// argument is refused or its head deferred (a loop whose own source is refused); <paramref name="Refusal"/> the one
/// sentence said of it.
/// </summary>
internal sealed record CallArgument(Resolved Head, JsonNode? Format, SourceRefusal? Refusal);

/// <summary>#1502 (specVersion 31, area 05): what a <c>call</c>'s argument names and its type (part 04 § 7.7; D13).</summary>
public sealed partial class ScopeTree
{
    /// <summary>
    /// The argument <paramref name="text"/> that <paramref name="call"/> passes as <paramref name="param"/>, a reference
    /// string resolved at the body the call is listed in (D13): not a reference, A05-001; the heads of § 7.7:
    /// <c>input.&lt;id&gt;.value</c> (bare, S02-018), <c>node.&lt;id&gt;</c>, <c>check.&lt;id&gt;</c>, <c>item</c>,
    /// <c>item.parent</c>, <c>param.&lt;id&gt;</c> (a whole one read through its <c>.value</c>),
    /// <c>snippet.&lt;folder&gt;.&lt;item&gt;</c> (of the folder's <c>format</c>; the function's own folder, else the
    /// package's), <c>run.&lt;fact&gt;</c> and <c>profile.&lt;field&gt;</c> (typed by <c>v31/profile.json</c>; inside a
    /// function S04-051, as <c>run.*</c> is), or a field path under one; <see cref="Resolve"/>'s refusals; S03-014 for
    /// anything else. A call not of this package is the caller's error (<see cref="ArgumentException"/>).
    /// </summary>
    internal CallArgument Argument(SourceLeaf call, string param, string text)
    {
        var frame = FrameOf(call);
        var path = call.Path;
        var subject = $"Call '{call.Id}'";
        if (ReferenceString.Parse(text) is not { } reference)
            return Refuse(ResolveSentences.NotAReference("Call", call.Id, $"with {param}", text, path));

        var s = reference.Segments;
        var head = s[0] switch
        {
            "snippet" => SnippetItem(frame, s),
            "profile" when frame.Function != null => Outside(new Read(reference, frame, subject, path, call)),
            "profile" => s.Count >= 2 && SourceResolve.ProfileFields.TryGetValue(s[1], out var field)
                ? Head(ScopeHeadKind.Value, null, 2, JsonValue.Create(field), false)
                : Unknown,
            _ => Resolve(reference, frame, subject, path, false, call),
        };
        var whole = s[0] is "node" or "check" or "item" or "param" or "snippet";
        var (_, read, refusal) = TypeHead(head, reference, frame, subject, path, whole);
        if (refusal != null)
            return Refuse(refusal);
        if (read?.Format is not { } format)
            return new CallArgument(head, null, null);

        var formats = FormatsOf(frame);
        var nullable = (head.Nullable || read.Nullable) && !FormatTypes.IsNullable(format, formats);
        return new CallArgument(head, nullable ? OrNull(format, formats) : format, null);

        static CallArgument Refuse(SourceRefusal refusal) => new(Refused(refusal), null, refusal);
    }

    /// <summary><c>snippet.&lt;folder&gt;.&lt;item&gt;</c>: a Format head of three of the folder's format; anything else under <c>snippet</c> names nothing.</summary>
    private Resolved SnippetItem(ScopeFrame frame, IReadOnlyList<string> s) =>
        s.Count >= 3 && SnippetEntry(frame.Function, s[1]) is SourceTextFolder folder
            && folder.Items.Any(i => i.Id == s[2]) && folder.Index["format"] is { } format
            ? Head(ScopeHeadKind.Format, null, 3, format, false)
            : Unknown;
}
