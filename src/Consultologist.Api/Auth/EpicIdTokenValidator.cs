using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Auth;

public interface IEpicIdTokenValidator
{
    Task<SmartIdentityClaims?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>
/// The Epic binding of <see cref="SmartIdTokenValidator"/> (#654): the config
/// prefix is <c>Epic</c> (<c>Epic:ClientId</c>, <c>Epic:AllowedIssuers</c>).
/// All SMART/OIDC logic lives in the base; this only names the prefix and the
/// interface the Epic link endpoint injects.
/// </summary>
public sealed class EpicIdTokenValidator : SmartIdTokenValidator, IEpicIdTokenValidator
{
    public EpicIdTokenValidator(IConfiguration configuration, ILogger<EpicIdTokenValidator> logger)
        : base("Epic", configuration, logger, signingKeys: null)
    {
    }

    // The signing-keys seam, so a test can assert the allowlist refuses an
    // unlisted issuer BEFORE any key fetch.
    internal EpicIdTokenValidator(
        IConfiguration configuration,
        ILogger<EpicIdTokenValidator> logger,
        Func<string, CancellationToken, Task<ICollection<SecurityKey>>>? signingKeys)
        : base("Epic", configuration, logger, signingKeys)
    {
    }
}
