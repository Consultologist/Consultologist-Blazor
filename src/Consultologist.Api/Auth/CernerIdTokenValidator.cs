using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Auth;

public interface ICernerIdTokenValidator
{
    Task<SmartIdentityClaims?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>
/// The Cerner (Oracle Health) binding of <see cref="SmartIdTokenValidator"/>
/// (#662): the config prefix is <c>Cerner</c> (<c>Cerner:ClientId</c>,
/// <c>Cerner:AllowedIssuers</c>). Cerner's OIDC issuer is per-tenant
/// (<c>https://authorization.cerner.com/tenants/{id}/oidc/idsps/{id}/</c>), so
/// the base's per-installation allowlist applies unchanged — only the prefix
/// and the injected interface differ from Epic.
/// </summary>
public sealed class CernerIdTokenValidator : SmartIdTokenValidator, ICernerIdTokenValidator
{
    public CernerIdTokenValidator(IConfiguration configuration, ILogger<CernerIdTokenValidator> logger)
        : base("Cerner", configuration, logger, signingKeys: null)
    {
    }

    // The signing-keys seam, so a test can assert the allowlist refuses an
    // unlisted issuer BEFORE any key fetch.
    internal CernerIdTokenValidator(
        IConfiguration configuration,
        ILogger<CernerIdTokenValidator> logger,
        Func<string, CancellationToken, Task<ICollection<SecurityKey>>>? signingKeys)
        : base("Cerner", configuration, logger, signingKeys)
    {
    }
}
