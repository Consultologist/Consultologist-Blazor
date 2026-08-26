namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #486: the words for a job's delivery outcome, shared by History and
/// Consults so the two never disagree. Mirrors the Api's DeliveryOutcomes.
/// </summary>
public static class DeliveryState
{
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string AddressNotSet = "address-not-set";
    public const string NotConfigured = "not-configured";

    /// <summary>Short badge text, or null when nothing was recorded (a job from before, or still running).</summary>
    public static string? Badge(string? outcome) => outcome switch
    {
        Sent => "delivered",
        null => null,
        _ => "not delivered"
    };

    /// <summary>The sentence a reader gets, or null when nothing was recorded.</summary>
    public static string? Describe(string? outcome, bool? documentAttached) => outcome switch
    {
        Sent when documentAttached == true => "Emailed to your delivery address, document attached",
        Sent when documentAttached == false => "Emailed to your delivery address, link only — no delivery password is set",
        Sent => "Emailed to your delivery address",
        AddressNotSet => "Not emailed — no delivery address is set on your profile",
        NotConfigured => "Not emailed — delivery is not configured on this deployment",
        Failed => "The email failed — the consult is in History",
        null => null,
        _ => "Not emailed"
    };
}
