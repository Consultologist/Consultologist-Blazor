using Consultologist.Api.Models;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #863 (package-format v20): the assembly-time fill for the model-emitted
/// inline slot marker <c>[[slot:&lt;macro-id&gt;]]</c>. A section prompt's model
/// output may place the marker; here — after <see cref="ConsultMacroExpander.Compose"/>
/// has assembled the document and before the signature/hash — each marker is
/// replaced with its macro's verbatim expanded text.
///
/// Two provenance boundaries are load-bearing:
///  * The marker survives verbatim in the section node's <c>OutputHash</c> (it
///    IS model output). The fill runs over the COMPOSED document, never the node
///    output, so no package text ever folds into a node hash.
///  * The filled text is attributed exactly like an appended macro — a
///    <see cref="ConsultAppendedKinds.Slot"/> entry, outside node hashes, inside
///    the document hash — so nothing is added to the provenance record's shape.
///
/// The deliverable's <c>slot: true</c> macro references are the allow-list, and
/// they have ALREADY passed their <c>when</c>/<c>optional</c> gates (the starter
/// and the boundary filter drop an excluded macro's placement in lockstep). So a
/// marker whose id is not in the authorized set — unknown, never sanctioned by
/// this deliverable, or gate-excluded — is simply removed: emitting the marker is
/// the model's PLACEMENT choice; the author's gate still governs INCLUSION.
/// </summary>
internal static class ConsultSlotFiller
{
    public static (string Text, IReadOnlyList<ConsultAppendedEntry>? SlotEntries) Fill(
        string composed,
        IReadOnlySet<string> authorizedSlotIds,
        IReadOnlyDictionary<string, string>? macroTexts,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string>? dataScalars,
        IReadOnlyDictionary<string, string> classifications,
        ConsultMacroExpander.RunFacts facts)
    {
        // The control: a document with no marker is byte-identical and allocates
        // nothing — every pre-v20 deliverable writes the bytes it always wrote.
        if (!WorkflowSlotMarker.Pattern.IsMatch(composed))
        {
            return (composed, null);
        }

        var entries = new List<ConsultAppendedEntry>();

        var filled = WorkflowSlotMarker.Pattern.Replace(composed, match =>
        {
            var id = match.Groups[1].Value;

            if (!authorizedSlotIds.Contains(id))
            {
                // The model placed a marker this deliverable never sanctioned as a
                // slot — or one its gate excluded. Drop it: a stray [[slot:…]]
                // never leaks into the delivered document.
                return string.Empty;
            }

            if (macroTexts is null || !macroTexts.TryGetValue(id, out var template))
            {
                // Authorized but no snapshotted template is a broken invariant —
                // the starter snapshots every declared macro's text at start.
                throw new InvalidOperationException($"Slot macro '{id}' has no snapshotted template.");
            }

            entries.Add(new ConsultAppendedEntry(ConsultAppendedKinds.Slot, id));
            return ConsultMacroExpander.Expand(template, inputs, dataScalars, classifications, facts);
        });

        return (filled, entries.Count > 0 ? entries : null);
    }
}
