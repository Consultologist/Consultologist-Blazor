using System.Net;
using System.Text.Json;
using Consultologist.Api.Auth;
using Consultologist.Api.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1503 (part 09 § 3.6; D9): the drain's three endpoints, for the release workflow, gated as the <c>Operator/*</c>
/// endpoints are (an account listed in <c>Operators__AppUserIds</c> that can use the app; 401 without an account, 403
/// otherwise). <c>POST Admin/Drain</c> begins a drain and says where it stands; <c>GET Admin/Drain</c> says where it
/// stands (<c>{ "draining", "producing", "parked" }</c>), polled until <c>producing</c> is 0; <c>POST Admin/Resume</c> ends
/// it, schedules every parked job oldest first, once, and wakes every orchestration waiting behind it
/// (<c>{ "draining": false, "started" }</c>). The 60-minute cap is the workflow's.
/// </summary>
public sealed class AdminDrain(
    IAccountAuthorizer authorizer,
    IEngineDrain drain,
    ILogger<AdminDrain> logger,
    // Tests name the operators; the engine reads Operators__AppUserIds, as Operator/* does.
    IReadOnlySet<string>? operators = null)
{
    /// <summary>This engine's orchestrators: the only orchestrations a drain counts or wakes.</summary>
    private static readonly HashSet<string> Orchestrators = new(StringComparer.Ordinal)
    {
        nameof(ConsultGenerationOrchestrator),
        nameof(ConsultGenerationOrchestratorV31),
    };

    [Function("AdminDrain")]
    public async Task<HttpResponseData> DrainAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Admin/Drain")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        if (await RefusalAsync(req, cancellationToken) is { } refused)
        {
            return refused;
        }

        await drain.BeginAsync(cancellationToken);
        var status = await StatusOfAsync(client, cancellationToken);
        logger.LogWarning(
            "Engine drain begun: new jobs are parked until Admin/Resume. Producing={Producing}, Parked={Parked}",
            status.producing, status.parked);
        return await RespondAsync(req, status, cancellationToken);
    }

    [Function("AdminDrainStatus")]
    public async Task<HttpResponseData> StatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Admin/Drain")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        if (await RefusalAsync(req, cancellationToken) is { } refused)
        {
            return refused;
        }

        return await RespondAsync(req, await StatusOfAsync(client, cancellationToken), cancellationToken);
    }

    [Function("AdminResume")]
    public async Task<HttpResponseData> ResumeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Admin/Resume")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        if (await RefusalAsync(req, cancellationToken) is { } refused)
        {
            return refused;
        }

        var parked = await drain.EndAsync(cancellationToken);
        var started = 0;
        foreach (var job in parked)
        {
            if (await EngineDrainJobs.StartParkedAsync(client, drain, job, cancellationToken))
            {
                started++;
            }
        }

        var woken = 0;
        await foreach (var instance in client.GetAllInstancesAsync(new OrchestrationQuery(Statuses: [OrchestrationRuntimeStatus.Running])))
        {
            if (Orchestrators.Contains(instance.Name) && StatusOf(instance) == ConsultGenerationJobStatuses.WaitingForUpdate)
            {
                await client.RaiseEventAsync(instance.InstanceId, EngineDrain.ResumedEvent, null, cancellationToken);
                woken++;
            }
        }

        logger.LogWarning("Engine drain ended. Parked={Parked}, Started={Started}, Woken={Woken}", parked.Count, started, woken);
        return await RespondAsync(req, new { draining = false, started }, cancellationToken);
    }

    /// <summary>
    /// Where the drain stands: whether it is on, how many of this engine's orchestrations are producing or delivering
    /// (pending, or running and neither waiting for an update nor asleep on a schedule, by their custom status), and how
    /// many jobs are parked.
    /// </summary>
    private async Task<(bool draining, int producing, int parked)> StatusOfAsync(DurableTaskClient client, CancellationToken cancellationToken)
    {
        var producing = 0;
        await foreach (var instance in client.GetAllInstancesAsync(
            new OrchestrationQuery(Statuses: [OrchestrationRuntimeStatus.Running, OrchestrationRuntimeStatus.Pending])))
        {
            if (!Orchestrators.Contains(instance.Name))
            {
                continue;
            }

            if (instance.RuntimeStatus == OrchestrationRuntimeStatus.Pending
                || (instance.RuntimeStatus == OrchestrationRuntimeStatus.Running
                    && StatusOf(instance) is not (ConsultGenerationJobStatuses.WaitingForUpdate or ConsultGenerationJobStatuses.Scheduled)))
            {
                producing++;
            }
        }

        return (await drain.IsDrainingAsync(cancellationToken), producing, await drain.ParkedCountAsync(cancellationToken));
    }

    /// <summary>The <c>status</c> an orchestration published (its custom status), or null.</summary>
    private static string? StatusOf(OrchestrationMetadata instance)
    {
        if (string.IsNullOrWhiteSpace(instance.SerializedCustomStatus))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(instance.SerializedCustomStatus);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (string.Equals(member.Name, "status", StringComparison.OrdinalIgnoreCase) && member.Value.ValueKind == JsonValueKind.String)
                {
                    return member.Value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The refusal for a caller who is not an operator, as <c>Operator/*</c> answers it; null for an operator.</summary>
    private async Task<HttpResponseData?> RefusalAsync(HttpRequestData req, CancellationToken cancellationToken)
    {
        var account = await authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        var isOperator = operators == null ? Operators.IsOperator(account) : Operators.IsOperator(account, operators);
        return AccountAuthorizer.CanUseApp(account) && isOperator ? null : AccountAuthorizer.CreateForbiddenResponse(req);
    }

    private static Task<HttpResponseData> RespondAsync(HttpRequestData req, (bool draining, int producing, int parked) status, CancellationToken cancellationToken) =>
        RespondAsync(req, new { status.draining, status.producing, status.parked }, cancellationToken);

    private static async Task<HttpResponseData> RespondAsync<T>(HttpRequestData req, T body, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(body, cancellationToken);
        return response;
    }
}
