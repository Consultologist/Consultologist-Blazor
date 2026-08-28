namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #518: whether app-initiated runs email the PDF at all. Rides the generic
/// settings routes like the schedule default (consult.scheduleTime); mirrors
/// the Api's AccountSettingKeys.EmailPdf and its tolerant reading.
///
/// Empty until chosen: null means "not chosen", and a run then sends, as it
/// did before the option existed. Only a stored "false" turns the email off.
/// </summary>
public static class EmailPdfPreference
{
    public const string SettingKey = "delivery.emailPdf";

    public const string ContentType = "text/plain";

    public static bool? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var word = value.Trim();
        if (string.Equals(word, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(word, "true", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    /// <summary>The state line on the profile card — what unset means is said, not left silent.</summary>
    public static string Describe(bool? choice) => choice switch
    {
        true => "Yes — each run started from the app is emailed to your delivery address",
        false => "No — runs started from the app are not emailed; they appear in History",
        null => "Not chosen — PDFs are sent, as today"
    };
}
