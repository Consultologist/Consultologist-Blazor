using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Http;

namespace Consultologist.Api;

internal static class FunctionCors
{
    // #612: a static class reads the literal __ env name once, the
    // Operators.cs shape — app-settings changes restart the workers, so
    // read-once is exactly when the platform re-reads anyway.
    internal const string SettingName = "Cors__AllowedOrigins";

    // Shared by both Apply overloads and by endpoints that need to validate a
    // browser Origin outside of CORS (the LinkedIn link flow derives its
    // redirect-back origin from this list, #133). The compiled entries are
    // the always-present baseline; Cors__AllowedOrigins EXTENDS them (#612).
    internal static readonly string[] BaselineOrigins =
    {
        "https://app.consultologist.ai",
        "https://gentle-desert-09697700f.3.azurestaticapps.net",
        "http://localhost:3000",
        "http://localhost:5000",
        "http://localhost:5173",
        "http://localhost:5174",
        "http://localhost:7071"
    };

    private static readonly Lazy<string[]> Effective =
        new(() => WithConfigured(Environment.GetEnvironmentVariable(SettingName)));

    internal static string[] AllowedOrigins => Effective.Value;

    /// <summary>
    /// #612: configuration EXTENDS the baseline, never replaces it — a
    /// missing or blank setting changes nothing, and no setting can remove
    /// a compiled origin (the LinkedIn redirect derivation stays stable for
    /// the baseline whatever an operator types). Semicolon-separated is the
    /// documented form; comma and whitespace are tolerated, the Operators
    /// precedent. Extracted so it can be asserted directly.
    /// </summary>
    internal static string[] WithConfigured(string? setting) =>
        BaselineOrigins
            .Concat(setting?.Split(
                new[] { ';', ',', ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static bool IsAllowedOrigin([NotNullWhen(true)] string? origin)
    {
        return !string.IsNullOrWhiteSpace(origin) && AllowedOrigins.Contains(origin);
    }

    /// <summary>
    /// Open CORS for the anonymous public-registry endpoints (#95): the data is
    /// public and the requests carry no credentials, so any origin — including
    /// the future marketing site — may read.
    /// </summary>
    public static void ApplyPublic(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
    }

    public static void Apply(HttpRequestData req, HttpResponseData response)
    {
        if (!req.Headers.TryGetValues("Origin", out var originValues))
        {
            return;
        }

        var origin = originValues.FirstOrDefault();

        if (!IsAllowedOrigin(origin))
        {
            return;
        }

        response.Headers.Add("Access-Control-Allow-Origin", origin);
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Last-Event-ID");
    }

    public static void Apply(HttpRequest req, HttpResponse response)
    {
        if (!req.Headers.TryGetValue("Origin", out var originValues))
        {
            return;
        }

        string? origin = originValues.FirstOrDefault();

        if (!IsAllowedOrigin(origin))
        {
            return;
        }

        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "Content-Type, Authorization, Last-Event-ID";
    }
}
