namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #515: the record of the location a device chose, written to the account
/// in that location. The device's own copy (ApiLocations) is the authority —
/// a setting cannot be read before a host is chosen to read it from.
/// </summary>
public static class LocationPreference
{
    public const string SettingKey = "consult.location";

    public const string ContentType = "text/plain";
}
