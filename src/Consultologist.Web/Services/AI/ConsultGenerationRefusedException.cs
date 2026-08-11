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
    public ConsultGenerationRefusedException(HttpStatusCode status, string detail)
        : base(detail)
    {
        Status = status;
    }

    public HttpStatusCode Status { get; }
}
