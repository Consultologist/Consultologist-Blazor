namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #1509 (specVersion 31, part 09 § 7.1; D13): the three Letter details a 31 package reads as <c>profile.*</c>, with the
/// limits the Api holds them to (<c>AccountSettingKeys</c> and <c>Account.ValidateProfileSetting</c>), mirrored by hand
/// and pinned by <c>LetterDetailsProfileTests.TheLimits_MirrorTheApis</c>. A blank value is not set: saving one deletes
/// the setting.
/// </summary>
public static class LetterDetails
{
    public const string CredentialsKey = "profile.credentials";
    public const string OrganisationKey = "profile.organisation";
    public const string ClosingKey = "profile.closing";

    public const int MaxCredentialsLength = 120;
    public const int MaxOrganisationLength = 200;
    public const int MaxClosingLength = 4_000;

    public const string ContentType = "text/plain";

    /// <summary>What is stored for a typed value: its trimmed text, or null (delete the setting) when it is blank.</summary>
    public static string? Store(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    /// <summary>The state line: the saved text, or that the field is not set.</summary>
    public static string Describe(string? saved) =>
        string.IsNullOrWhiteSpace(saved) ? "Not set" : saved;
}
