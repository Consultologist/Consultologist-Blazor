using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 11; part 09 §§ 3.7, 6; D10, D13, D16; Review Focus 4): a 31 job's record
/// (storage 12), served beside the job routes and authorised as <c>GetConsultGenerationJobLinks</c> is: an account that can
/// use the app (401 without one, 403 otherwise), the job read from its entity, and someone else's job, a job that does not
/// exist and a job below storage 12 not found (404, never 403). GET and OPTIONS; <see cref="FunctionCors"/> on every answer.
/// <list type="bullet">
/// <item><c>GET ConsultGenerationJobs/{jobId}/Rows?key=&lt;row key&gt;</c>: one row, <c>{ key, section, row, text, textDeleted }</c>
/// (<c>row</c> the stored row's JSON object; <c>text</c> the retained text it is still owed, <see cref="OfferedText"/>; 404
/// for a key the job has no row for). Without <c>key</c>: <c>{ rows: [ { key, section, row } ] }</c>, every row in key
/// order, decisions included, no text.</item>
/// <item><c>GET ConsultGenerationJobs/{jobId}/Record</c>: the canonical record document's bytes as stored, sealing a terminal
/// job that is not sealed yet (D13), with <c>X-Record-Hash</c>; 409 for a job not terminal. A document the text account's
/// lifecycle policy deleted is restored from the rows, checked against <c>recordHash</c> (<see cref="IRecordSealer.ReadSealedAsync"/>).</item>
/// </list>
/// Logs carry the job id only, never a key's text, a row or retained text.
/// </summary>
public sealed class RecordRoutes(
    IAccountAuthorizer authorizer,
    IRecordRowStore rows,
    IRecordBlobStore blobs,
    RecordSealers sealers,
    IAccountStore accounts,
    ILogger<RecordRoutes> logger)
{
    internal const string NotFound = "Consult generation job was not found.";
    internal const string NoSuchRow = "The job has no row with that key.";
    internal const string NotFinished = "The job hasn't finished.";
    internal const string NotAvailable = "The record isn't available yet. Try again.";

    /// <summary>The members of a row's text vouched for by its content's check (part 08 § 11's right-hand column, D2).</summary>
    private static readonly string[] VouchedMembers =
        ["value", "itemName", "spots", "uncovered", "untested", "neutral", "renders", "macros", "substitutions"];

    [Function("GetConsultGenerationJobRows")]
    public async Task<HttpResponseData> GetRowsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "ConsultGenerationJobs/{jobId}/Rows")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var (refused, job, _) = await OpenAsync(req, client, jobId, cancellationToken);
        if (refused != null)
        {
            return refused;
        }

        var key = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["key"];
        if (key == null)
        {
            var listed = await rows.ListAsync(jobId, cancellationToken);
            var array = new JsonArray();
            foreach (var row in listed.OrderBy(row => row.Key, StringComparer.Ordinal))
            {
                array.Add(new JsonObject { ["key"] = row.Key, ["section"] = row.Section, ["row"] = JsonNode.Parse(row.Json) });
            }

            return await JsonAsync(req, HttpStatusCode.OK, new JsonObject { ["rows"] = array }, cancellationToken);
        }

        var stored = await rows.ReadAsync(jobId, key, cancellationToken);
        if (stored == null)
        {
            return await JsonAsync(req, HttpStatusCode.NotFound, new JsonObject { ["error"] = NoSuchRow }, cancellationToken);
        }

        var rowJson = JsonNode.Parse(stored.Json) as JsonObject;
        var textDeleted = job!.TextDroppedAtUtc != null;
        JsonObject? text = null;
        if (!textDeleted && rowJson != null && HasText(stored.Section))
        {
            var kind = await new DurableRecordJobs(client, accounts).AccountKindAsync(job, cancellationToken);
            text = OfferedText(stored.Section, rowJson, await blobs.ReadTextAsync(kind, job.AppUserId, jobId, key, cancellationToken));
        }

        return await JsonAsync(
            req,
            HttpStatusCode.OK,
            new JsonObject
            {
                ["key"] = stored.Key,
                ["section"] = stored.Section,
                ["row"] = rowJson,
                ["text"] = text,
                ["textDeleted"] = textDeleted,
            },
            cancellationToken);
    }

    [Function("GetConsultGenerationJobRecord")]
    public async Task<HttpResponseData> GetRecordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "ConsultGenerationJobs/{jobId}/Record")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var (refused, job, _) = await OpenAsync(req, client, jobId, cancellationToken);
        if (refused != null)
        {
            return refused;
        }

        // the sealer's own test of terminal: Delivering is not
        if (!ConsultGenerationJobEntity.IsTerminal(job!.Status))
        {
            return await JsonAsync(req, HttpStatusCode.Conflict, new JsonObject { ["error"] = NotFinished }, cancellationToken);
        }

        // #1506 (Task 15): sealed if it is not yet; a document the lifecycle policy deleted is restored from the rows and
        // checked against recordHash (a restore that does not hash to it is a fault, answered below)
        string? document;
        try
        {
            document = await sealers.For(client).ReadSealedAsync(jobId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // the sealer logged an engine fault itself; the page still gets an answer it can read (CORS applied)
            logger.LogWarning("V31 record not available. JobId={JobId}, ExceptionType={ExceptionType}", jobId, ex.GetType().FullName);
            return await JsonAsync(req, HttpStatusCode.InternalServerError, new JsonObject { ["error"] = NotAvailable }, cancellationToken);
        }

        if (document == null)
        {
            logger.LogWarning("V31 record not available. JobId={JobId}", jobId);
            return await JsonAsync(req, HttpStatusCode.InternalServerError, new JsonObject { ["error"] = NotAvailable }, cancellationToken);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        ApplyCors(req, response);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"record-{jobId}.json\"");
        response.Headers.Add("X-Record-Hash", Consultologist.PackageFormat.V31.Run.RecordDocument.Hash(document));
        await response.Body.WriteAsync(new UTF8Encoding(false).GetBytes(document), cancellationToken);
        return response;
    }

    /// <summary>
    /// The retained text a row is still owed (part 08 §§ 1.1, 11; D2), from its merged text blob: <c>content</c> only when it
    /// hashes to the row's <c>outputHash</c> (a node or check) or <c>composedHash</c> (a deliverable); <c>sent</c> only when
    /// it hashes to <c>inputHash</c>; the other members only when <c>content</c> passed and the row completed (a deliverable:
    /// was composed, which its <c>composedHash</c> says). So a text written after its row settled (a late write after a
    /// cancel) is never offered as the row's. Null when nothing is offered, and for a decision or a delivery.
    /// </summary>
    internal static JsonObject? OfferedText(string section, JsonObject row, JsonObject? text)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(row);
        if (text == null || !HasText(section))
        {
            return null;
        }

        var deliverable = string.Equals(section, "deliverable", StringComparison.Ordinal);
        var offered = new JsonObject();
        var contentPassed = Hashes(text["content"], row[deliverable ? "composedHash" : "outputHash"]);
        if (contentPassed)
        {
            offered["content"] = text["content"]!.DeepClone();
        }

        if (Hashes(text["sent"], row["inputHash"]))
        {
            offered["sent"] = text["sent"]!.DeepClone();
        }

        var settled = deliverable || (row["status"] is JsonValue status && status.TryGetValue<string>(out var word)
            && string.Equals(word, "Completed", StringComparison.Ordinal));
        if (contentPassed && settled)
        {
            foreach (var member in VouchedMembers.Where(text.ContainsKey))
            {
                offered[member] = text[member]?.DeepClone();
            }
        }

        return offered.Count == 0 ? null : offered;
    }

    private static bool HasText(string section) => section is "node" or "check" or "deliverable";

    private static bool Hashes(JsonNode? text, JsonNode? hash) =>
        text is JsonValue textValue && textValue.TryGetValue<string>(out var value)
        && hash is JsonValue hashValue && hashValue.TryGetValue<string>(out var expected)
        && string.Equals(CanonicalJson.HashText(value), expected, StringComparison.Ordinal);

    /// <summary>
    /// The preflight, the account and the job, as the Links route checks them: the refusal to answer, or the job (its
    /// entity's state, storage 12 and the caller's) and its view for the sealer.
    /// </summary>
    private async Task<(HttpResponseData? Refused, ConsultGenerationJobState? Job, DurableRecordJobs? Jobs)> OpenAsync(
        HttpRequestData req, DurableTaskClient client, string jobId, CancellationToken cancellationToken)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var preflight = req.CreateResponse(HttpStatusCode.OK);
            ApplyCors(req, preflight);
            return (preflight, null, null);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return (await JsonAsync(req, HttpStatusCode.BadRequest, new JsonObject { ["error"] = "JobId is required." }, cancellationToken), null, null);
        }

        var account = await authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return (AccountAuthorizer.CreateUnauthorizedResponse(req), null, null);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return (AccountAuthorizer.CreateForbiddenResponse(req), null, null);
        }

        var jobs = new DurableRecordJobs(client, accounts);
        var job = await jobs.ReadAsync(jobId, cancellationToken);
        if (job == null
            || !string.Equals(job.AppUserId, account.AppUserId, StringComparison.Ordinal)
            || job.SchemaVersion < RecordSealer.SealedFrom)
        {
            return (await JsonAsync(req, HttpStatusCode.NotFound, new JsonObject { ["error"] = NotFound }, cancellationToken), null, null);
        }

        return (null, job, jobs);
    }

    /// <summary><see cref="FunctionCors.Apply(HttpRequestData, HttpResponseData)"/>, and the record's hash header readable by the page that asked.</summary>
    private static void ApplyCors(HttpRequestData req, HttpResponseData response)
    {
        FunctionCors.Apply(req, response);
        if (response.Headers.Contains("Access-Control-Allow-Origin"))
        {
            response.Headers.Add("Access-Control-Expose-Headers", "X-Record-Hash, Content-Disposition");
        }
    }

    private static async Task<HttpResponseData> JsonAsync(HttpRequestData req, HttpStatusCode status, JsonObject body, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        ApplyCors(req, response);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.Body.WriteAsync(new UTF8Encoding(false).GetBytes(body.ToJsonString()), cancellationToken);
        return response;
    }
}
