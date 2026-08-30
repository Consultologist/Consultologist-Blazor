using Consultologist.Api.Models;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v11 #516 (package-format-v11-design.md § 5): the signature block, last —
/// after every macro, before CompleteResultDocument, inside documentHash.
/// Pure. Not signed returns the same references (§ 7's control: a package
/// using nothing of v11 writes the bytes it always wrote). Signed with a
/// snapshot appends the block's text verbatim, blank-line separated, no
/// invented heading, and names it in appended[] with its as-of date. Signed
/// with no chosen block changes nothing and reports the
/// unsigned-although-requested state — explicit initialisation: never a
/// silent hold, never a refusal; the document is the work, the signature is
/// a block on it.
/// </summary>
internal static class ConsultSignatureAppend
{
    public static (string Text, IReadOnlyList<ConsultAppendedEntry>? Appended, bool? Unsigned) Apply(
        string text,
        IReadOnlyList<ConsultAppendedEntry>? appended,
        bool signed,
        ConsultSignatureSnapshot? snapshot)
    {
        if (!signed)
        {
            return (text, appended, null);
        }

        if (snapshot == null)
        {
            return (text, appended, true);
        }

        var entries = appended is { Count: > 0 }
            ? new List<ConsultAppendedEntry>(appended)
            : new List<ConsultAppendedEntry>();
        entries.Add(new ConsultAppendedEntry(ConsultAppendedKinds.Signature, snapshot.Id, snapshot.AsOf));

        return (text + "\n\n" + snapshot.Text, entries, null);
    }
}
