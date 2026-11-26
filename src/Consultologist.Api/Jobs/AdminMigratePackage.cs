using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 36; part 10 § 1.5; R21): the operator's bulk publish, one account package a call,
/// driven by <c>scripts/bulk-publish-v31.cs</c>. Gated as <c>Admin/Drain</c> is (401 without an account; 403 unless the
/// account can use the app and is listed in <c>Operators__AppUserIds</c>), then, where the preview setting does not admit
/// 31, S09-001 as a 400 before anything is read. <c>GET Admin/MigratePackage</c> lists every account's active account
/// package below 31 (<c>{ "packages": [{ "owner", "name", "version", "specVersion" }], "notRead": n }</c>);
/// <c>POST Admin/MigratePackage</c> migrates and publishes one (<see cref="MigratePackage.RunAsync"/>; a body it cannot
/// read is a 400 with <c>error</c>). CORS is applied and each answer is <c>Cache-Control: no-store</c>.
/// </summary>
public sealed class AdminMigratePackage(
    IAccountAuthorizer authorizer,
    SpecVersionAdmission admission,
    MigratePackage migrate,
    ILogger<AdminMigratePackage> logger,
    // Tests name the operators; the engine reads Operators__AppUserIds, as Admin/Drain does.
    IReadOnlySet<string>? operators = null)
{
    [Function("AdminMigratePackage")]
    public async Task<HttpResponseData> MigrateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Admin/MigratePackage")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        if (await RefusalAsync(req, cancellationToken) is { } refused)
        {
            return refused;
        }

        var (request, error) = MigratePackageRequest.Read(await req.ReadAsStringAsync() ?? "");
        if (request is null)
        {
            logger.LogWarning("Admin/MigratePackage: the body does not read.");
            return await RespondAsync(req, HttpStatusCode.BadRequest, new JsonObject { ["error"] = error }, cancellationToken);
        }

        return await RespondAsync(req, HttpStatusCode.OK, await migrate.RunAsync(request, cancellationToken), cancellationToken);
    }

    [Function("AdminMigratePackageList")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Admin/MigratePackage")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        if (await RefusalAsync(req, cancellationToken) is { } refused)
        {
            return refused;
        }

        var (packages, notRead) = await migrate.ListAsync(cancellationToken);
        var rows = new JsonArray();
        foreach (var package in packages)
        {
            rows.Add(new JsonObject
            {
                ["owner"] = package.Owner,
                ["name"] = package.Name,
                ["version"] = package.Version,
                ["specVersion"] = package.SpecVersion,
            });
        }

        return await RespondAsync(req, HttpStatusCode.OK, new JsonObject { ["packages"] = rows, ["notRead"] = notRead }, cancellationToken);
    }

    /// <summary>The operator gate, then S09-001 as a 400 where 31 is not admitted; null when the call may go on.</summary>
    private async Task<HttpResponseData?> RefusalAsync(HttpRequestData req, CancellationToken cancellationToken)
    {
        if (await AdminDrain.OperatorRefusalAsync(authorizer, operators, req, cancellationToken) is { } refused)
        {
            return refused;
        }

        return admission.Runs31
            ? null
            : await RespondAsync(req, HttpStatusCode.BadRequest, new JsonObject { ["error"] = admission.NotSupported(31) }, cancellationToken);
    }

    private static async Task<HttpResponseData> RespondAsync(HttpRequestData req, HttpStatusCode status, JsonObject body, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.Body.WriteAsync(new UTF8Encoding(false).GetBytes(body.ToJsonString()), cancellationToken);
        return response;
    }
}
