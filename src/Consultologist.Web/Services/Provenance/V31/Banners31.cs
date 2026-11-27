using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// One banner at the top of History (part 09 § 6.3). <paramref name="Kind"/> is <c>blocked</c>, <c>held</c>, <c>failed</c>,
/// <c>not-sent</c> or <c>delivery-failed</c>; <paramref name="LinkKey"/> is the key of the row the banner links to.
/// </summary>
public sealed record Banner31(string Kind, string Text, string? LinkKey);

/// <summary>
/// #1509 (Task 14; part 09 § 6.3): the banners of a record, one per deliverable that is <c>blocked</c>, <c>held</c> or
/// <c>failed</c> and one per delivery that is <c>not sent</c> or <c>failed</c>, in deliverable key order and, under each
/// deliverable, in delivery key order. A deliverable is named by its stored label, by its key only when the row has none.
/// Every word but the label is the engine's, as stored. Pure.
/// </summary>
public static class Banners31
{
    public static IReadOnlyList<Banner31> Of(Record31 record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var banners = new List<Banner31>();
        foreach (var deliverable in record.Deliverables)
        {
            var label = LabelOf(deliverable);
            var reason = deliverable.Text("stateReason");
            switch (deliverable.Text("state"))
            {
                case "blocked":
                    banners.Add(new Banner31("blocked", HistorySentences31.Blocked(label, reason), deliverable.Key));
                    break;
                case "held":
                    banners.Add(new Banner31("held", HistorySentences31.Held(label, reason), deliverable.Key));
                    break;
                case "failed":
                    banners.Add(new Banner31("failed", HistorySentences31.Failed(label, reason), deliverable.Key));
                    break;
            }

            foreach (var delivery in record.DeliveriesOf(deliverable.Key))
            {
                switch (delivery.Text("result"))
                {
                    case "not sent":
                        banners.Add(new Banner31("not-sent", HistorySentences31.NotSent(label, delivery.Text("reason")), delivery.Key));
                        break;
                    case "failed":
                        var kind = delivery.Text("kind") ?? "";
                        banners.Add(new Banner31("delivery-failed", HistorySentences31.CouldNotSend(DeliveryPlan.KindWords(kind)), delivery.Key));
                        break;
                }
            }
        }

        return banners;
    }

    private static string LabelOf(Row31 deliverable) =>
        deliverable.Text("label") is { Length: > 0 } label ? label : deliverable.Key;
}
