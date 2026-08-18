using System.Net;
using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests;

/// <summary>
/// #374: the start-refusal status mapping, which was an inline switch inside
/// the endpoint that no test reached — and whose default is 500, so a missing
/// arm silently turns a refusal the engine understands into an apparent server
/// fault.
/// </summary>
public class ConsultGenerationStartStatusTests
{
    [Theory]
    // The split this issue is about: the registry being down is a 503, and a
    // package whose content the catalog no longer matches is not.
    [InlineData(ConsultGenerationJobStartError.RegistryUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ConsultGenerationJobStartError.PackageContentRejected, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.SpecVersionNotYetExecutable, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.MalformedPackageRef, HttpStatusCode.BadRequest)]
    [InlineData(ConsultGenerationJobStartError.ForeignPackageRef, HttpStatusCode.Forbidden)]
    [InlineData(ConsultGenerationJobStartError.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(ConsultGenerationJobStartError.InputsMismatch, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.NoApplicableDeliverable, HttpStatusCode.UnprocessableEntity)]
    public void EachRefusal_AnswersItsOwnStatus(ConsultGenerationJobStartError error, HttpStatusCode expected)
    {
        Assert.Equal(expected, ConsultGenerationJobs.StatusFor(error));
    }

    [Fact]
    public void NoRefusalFallsThroughToAServerError()
    {
        // The default is 500. Every kind the engine can return must be named
        // above it, or the caller is told the server broke when it did not.
        Assert.All(
            Enum.GetValues<ConsultGenerationJobStartError>(),
            error => Assert.NotEqual(HttpStatusCode.InternalServerError, ConsultGenerationJobs.StatusFor(error)));
    }
}
