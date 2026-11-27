using System.Net;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1509 (specVersion 31, area 12; D1; part 09 § 5.3): <c>GET WorkflowPackages/Current/StartChecks?ref=&amp;signatureBlockId=</c>
/// answers, before a start, the account checks the start would refuse with, on the code the start runs
/// (<see cref="V31JobStarter"/>'s account reading and <see cref="V31StartChecks"/>): the signature block's refusal (A08-004),
/// then <see cref="V31StartChecks.Account"/>'s sentences, and <c>needs.profile</c>. The ref is resolved, and the package loaded,
/// as <c>WorkflowPackages/Current</c> does (ownership included); a load refusal is the same 400 <c>{ error }</c>, S09-001 when
/// 31 is not admitted. Below 31 both lists are empty. Input refusals are not here: the page's own checks cover them and the
/// start repeats them. No value of the profile is in the answer.
/// </summary>
public sealed class V31StartChecksRoute(
    IAccountAuthorizer authorizer,
    IWorkflowPackagePinResolver pins,
    IWorkflowPackageOwnership ownership,
    IWorkflowPackageStore packages,
    IAccountStore accounts,
    IAccountSettingsStore settings,
    ILogger<V31StartChecksRoute> logger,
    ICompiledPackageStore? compiled = null,
    SpecVersionAdmission? admission = null)
{
    [Function("WorkflowPackageStartChecks")]
    public async Task<HttpResponseData> GetStartChecksAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "WorkflowPackages/Current/StartChecks")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        if (!AccountAuthorizer.CanUseApp(account))
            return AccountAuthorizer.CreateForbiddenResponse(req);

        var requested = WorkflowPackages.ParseRequestedPackage(req.Url, account.AppUserId);
        if (requested.Kind == WorkflowPackages.RequestedPackageKind.Malformed)
        {
            return await JsonAsync(req, HttpStatusCode.BadRequest,
                new { error = "ref must be a package reference (name@vYYYY.MM.N or name@latest)." }, cancellationToken);
        }

        if (requested.Kind == WorkflowPackages.RequestedPackageKind.AccountPackage
            && !await ownership.CanAccessAsync(requested.Ref!.Name, account.AppUserId, cancellationToken))
        {
            return await JsonAsync(req, HttpStatusCode.Forbidden,
                new { error = "Workflow package is not accessible from this account." }, cancellationToken);
        }

        var packageRef = requested.Ref ?? await pins.ResolvePinAsync(account.AppUserId, cancellationToken);

        var load = compiled != null ? await compiled.TryLoadAsync(packageRef, cancellationToken) : null;
        if (load is { Unreadable: false })
        {
            if (load.Plan is not { } plan)
            {
                logger.LogWarning(
                    "Start checks could not load the package at 31. Ref={Ref}, RefusalId={RefusalId}, Detail={Detail}",
                    packageRef, load.RefusalId, load.OperatorDetail ?? load.Refusal);
                return await JsonAsync(req, HttpStatusCode.BadRequest, new { error = load.Refusal }, cancellationToken);
            }

            var signatureBlockId = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["signatureBlockId"];
            if (string.IsNullOrWhiteSpace(signatureBlockId))
                signatureBlockId = null;
            var (profile, signature) = await V31JobStarter.ProfileAsync(settings, accounts, plan, account.AppUserId, signatureBlockId, cancellationToken);
            var timeZone = await V31JobStarter.TimeZoneAsync(settings, account.AppUserId, cancellationToken);
            var refusals = V31JobStarter.AccountRefusals(plan, profile, timeZone, signature.Refusal);

            return await JsonAsync(req, HttpStatusCode.OK,
                new WorkflowPackageStartChecksResponse(
                    refusals.Select(r => r.Sentence).ToList(),
                    plan.NeedsProfile.Distinct(StringComparer.Ordinal).ToList()),
                cancellationToken);
        }

        // As Current: today's reader meets the version. Below 31 it resolves (nothing to check); a 31 version it
        // refuses is S09-001 with the preview unset.
        try
        {
            await packages.ResolveAsync(packageRef, cancellationToken);
        }
        catch (WorkflowPackageSpecVersionException ex) when (ex.SpecVersion == PublishedStamp.SpecVersion && admission != null)
        {
            var (status, error) = WorkflowPackages.NotLoadedAt31(admission, packageRef, load, logger);
            return await JsonAsync(req, status, new { error }, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found", StringComparison.Ordinal))
        {
            logger.LogWarning(ex, "Start checks: workflow package not found. Ref={Ref}", packageRef);
            return await JsonAsync(req, HttpStatusCode.NotFound, new { error = $"Workflow package {packageRef} was not found." }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Start checks: workflow package resolution failed. Ref={Ref}", packageRef);
            return await JsonAsync(req, HttpStatusCode.ServiceUnavailable, new { error = "Workflow package registry is unavailable." }, cancellationToken);
        }

        return await JsonAsync(req, HttpStatusCode.OK, new WorkflowPackageStartChecksResponse([], []), cancellationToken);
    }

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, HttpStatusCode status, T payload, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(payload, cancellationToken);
        return response;
    }
}
