using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1505 (specVersion 31, area 08): the publish rules of a deliverable's header and of its delivery folder (part 06
/// §§ 6.1, 6.3, 8). The header rules need the package's formats, and run with the type step
/// (<see cref="SourceCheck.Run"/>): a deliverable's <c>format</c> is <c>markdown_block</c>, <c>plaintext</c> or a format
/// whose shape is an object (S06-020; an unknown name is S02-017's, said before), its <c>whenEmpty</c> is
/// <c>release</c>, <c>hold</c> or <c>fail</c> (S06-025), and each folder's <c>delivery</c> lists only <c>email</c>,
/// <c>pdf</c>, <c>emr</c> and <c>zip</c> (S06-026) and its <c>bundle</c> is <c>"zip"</c> or <c>null</c> (A08-002). The
/// rule that reads a body runs once the dialect has typed it (<see cref="PackageTemplates"/>): a deliverable reads no
/// <c>check.&lt;id&gt;</c> its own <c>check</c> names, in its body or in a snippet it places (S06-018, R-D2). Never throws
/// on package content.
/// </summary>
internal static class SourceDeliverables
{
    private static readonly IReadOnlySet<string> TextFormats = new HashSet<string>(StringComparer.Ordinal) { "markdown_block", "plaintext" };

    private static readonly IReadOnlySet<string> WhenEmpty = new HashSet<string>(StringComparer.Ordinal) { "release", "hold", "fail" };

    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "email", "pdf", "emr", "zip" };

    /// <summary>
    /// The refusals of <paramref name="package"/>'s deliverables and delivery folders, ordered by path (ordinal), then in
    /// the order found: the header and folder rules, each format read in <paramref name="formats"/> (the package's), and,
    /// with <paramref name="templates"/> (the dialect's, of this package), the rule that reads the bodies. A null package
    /// or format set is the caller's error.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Check(SourcePackage package, FormatSet formats, PackageTemplates? templates)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(formats);

        var refusals = new List<SourceRefusal>();
        foreach (var folder in Folders(package.Deliverables))
        {
            Folder(folder, refusals);
            foreach (var deliverable in folder.Deliverables)
            {
                Header(deliverable, formats, refusals);
                if (templates != null)
                    OwnCheck(deliverable, templates, refusals);
            }
        }

        return refusals.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary><c>deliverables/</c>, then each delivery folder under it (one level deep, part 06 § 8).</summary>
    private static IEnumerable<SourceDeliveryFolder> Folders(SourceDeliveryFolder root) => root.Folders.Prepend(root);

    /// <summary>S06-026, once per kind as first written, and A08-002 (part 06 § 8): a folder's <c>delivery</c> and <c>bundle</c>.</summary>
    private static void Folder(SourceDeliveryFolder folder, List<SourceRefusal> refusals)
    {
        if (folder.Index["delivery"] is JsonArray delivery)
        {
            var said = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kind in delivery.Select(SourceLayout.Text).OfType<string>())
            {
                if (!Kinds.Contains(kind) && said.Add(kind))
                    refusals.Add(DeliverableSentences.HasDeliveryKind(folder.Path, kind));
            }
        }

        if (SourceLayout.Text(folder.Index["bundle"]) is { } bundle && bundle != "zip")
            refusals.Add(DeliverableSentences.HasBundle(folder.Path, bundle));
    }

    /// <summary>
    /// S06-020 and S06-025 (part 06 § 6.1): the format is one of the two text names or a format whose shape is an object
    /// (<see cref="FormatTypes.Shape"/>: a package, catalogue or built-in object format such as <c>check_result</c>);
    /// <c>markdown_inline</c>, a list, a string, a number or any other is refused.
    /// </summary>
    private static void Header(SourceDeliverable deliverable, FormatSet formats, List<SourceRefusal> refusals)
    {
        if (SourceLayout.Text(deliverable.Header["format"]) is { } format
            && !TextFormats.Contains(format)
            && FormatTypes.Shape(deliverable.Header["format"], formats) != FormatShape.Object)
            refusals.Add(DeliverableSentences.HasFormat(deliverable.Id, format, deliverable.Path));

        if (SourceLayout.Text(deliverable.Header["whenEmpty"]) is { } whenEmpty && !WhenEmpty.Contains(whenEmpty))
            refusals.Add(DeliverableSentences.HasWhenEmpty(deliverable.Id, whenEmpty, deliverable.Path));
    }

    /// <summary>
    /// S06-018 (part 06 § 6.3, R-D2): the body, or a unit it places (their reads are the deliverable's), reads the check
    /// the deliverable's <c>check</c> names; said once. That the <c>check</c> names a check at the top of <c>checks/</c>
    /// is A08-001's, said before.
    /// </summary>
    private static void OwnCheck(SourceDeliverable deliverable, PackageTemplates templates, List<SourceRefusal> refusals)
    {
        if (SourceLayout.Text(deliverable.Header["check"]) is not { } reference
            || ReferenceString.Parse(reference) is not { Segments: ["check", var id] })
            return;

        if (templates.TypedReads(deliverable).Any(r => (r.RootedHead ?? r.Head) == reference))
            refusals.Add(DeliverableSentences.ReadsOwnCheck(deliverable.Id, id, deliverable.Path));
    }
}
