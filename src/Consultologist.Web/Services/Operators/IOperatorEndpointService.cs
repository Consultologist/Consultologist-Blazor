namespace Consultologist.Web.Services.Operators;

public interface IOperatorEndpointService
{
    /// <summary>
    /// #553: usage per user for a window, from the derived store only.
    /// Throws OperatorAccessException on the allowlist's bodiless 403.
    /// </summary>
    Task<OperatorUsageResponse> GetUsageAsync(string from, string to);
}
