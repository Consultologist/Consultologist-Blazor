namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #543: what the intake door does with a pushed form response. Rides the
/// generic settings routes; mirrors the Api's
/// AccountSettingKeys.FormResponseMode / FormResponseModes and their
/// tolerant reading.
///
/// Empty until chosen: null means "not chosen", and a pushed response is
/// then held for review, as before the option existed. Only the account's
/// own exact word runs anything.
/// </summary>
public static class FormResponsePreference
{
    public const string SettingKey = "forms.responseMode";

    public const string ContentType = "text/plain";

    public const string Hold = "hold";
    public const string RunAtOnce = "runAtOnce";

    public static string? Parse(string? value) => value?.Trim() switch
    {
        { } word when string.Equals(word, Hold, StringComparison.OrdinalIgnoreCase) => Hold,
        { } word when string.Equals(word, RunAtOnce, StringComparison.OrdinalIgnoreCase) => RunAtOnce,
        _ => null,
    };

    /// <summary>The state line on the profile card — what unset means is said, not left silent.</summary>
    public static string Describe(string? choice) => choice switch
    {
        RunAtOnce => "Run at once — each pushed response starts a consult on your pinned package",
        Hold => "Hold for review — responses wait for the setup form's picker",
        _ => "Not chosen — responses are held for review, as today"
    };
}
