using System.Net;

namespace Consultologist.Web.Services.AI;

/// <summary>
/// The server declined to start (or serve) a job and said why (#348).
///
/// Distinct from a transport failure on purpose. Every refusal the job
/// endpoint returns carries a written reason naming the input, the file or
/// the wait — and the page's generic handler prefixed all of them with "Error
/// calling agent", which is untrue of a request no agent ever saw. A refusal
/// is the server's answer, not a fault: it is shown as written.
/// </summary>
public sealed class ConsultGenerationRefusedException : Exception
{
    public ConsultGenerationRefusedException(HttpStatusCode status, string detail, string? jobId = null)
        : base(detail)
    {
        Status = status;
        JobId = jobId;
    }

    public HttpStatusCode Status { get; }

    /// <summary>
    /// #434: the refusal that left a row — a well-formed request the package
    /// produced nothing for — says where the row is. Null for every other
    /// refusal, which left nothing.
    /// </summary>
    public string? JobId { get; }
}
