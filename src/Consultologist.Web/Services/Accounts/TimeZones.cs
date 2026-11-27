namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #1509 (specVersion 31, part 09 § 7.2; D12): the account's time zone. An account setting, never a <c>profile.*</c>
/// read; it sets what <c>run.date</c> means. The key mirrors the Api's <c>AccountSettingKeys.TimeZone</c> (pinned by
/// <c>TimeZoneProfileTests.TheKey_MirrorsTheApis</c>).
/// </summary>
public static class TimeZones
{
    public const string SettingKey = "account.timeZone";

    public const string ContentType = "text/plain";

    /// <summary>S06-008, said until a zone is set (the Api's start checks say the same sentence).</summary>
    public const string NotSet = "Set your time zone in your account settings before running a workflow.";

    /// <summary>The IANA zone names the runtime knows, in ordinal order.</summary>
    public static IReadOnlyList<string> Iana() =>
        TimeZoneInfo.GetSystemTimeZones()
            .Where(zone => zone.HasIanaId)
            .Select(zone => zone.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
}
