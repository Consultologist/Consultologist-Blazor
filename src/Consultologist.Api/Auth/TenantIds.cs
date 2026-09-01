namespace Consultologist.Api.Auth;

/// <summary>
/// #553: the tenant an account belongs to, read from what the record already
/// carries — the raw issuer of its Entra identity
/// (https://login.microsoftonline.com/&lt;tenant-id&gt;/v2.0). Personal Microsoft
/// accounts carry the consumers tenant there, so parsing is uniform and
/// personal accounts group under it by construction. Anything not shaped
/// like an Entra issuer (a LinkedIn issuer, junk) is null — never a guess.
/// </summary>
public static class TenantIds
{
    public static string? TenantIdOf(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer)
            || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 1 && Guid.TryParse(segments[0], out _) ? segments[0] : null;
    }
}
