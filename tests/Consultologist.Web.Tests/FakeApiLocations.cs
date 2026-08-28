using Consultologist.Web.Services.Locations;

namespace Consultologist.Web.Tests;

/// <summary>#515: a location list with no browser behind it — what the harness and the URL tests point services at.</summary>
public sealed class FakeApiLocations : IApiLocations
{
    public static readonly ApiLocation CanadaEast = new("ca-east", "Canada East", "https://east.ca.api.consultologist.ai/api");

    public FakeApiLocations(IReadOnlyList<ApiLocation>? all = null, string? chosenId = null)
    {
        All = all ?? new[] { CanadaEast };
        ChosenId = chosenId;
    }

    public string? ChosenId { get; private set; }

    public IReadOnlyList<ApiLocation> All { get; }

    public ApiLocation? Chosen => All.FirstOrDefault(l => l.Id == ChosenId);

    public ApiLocation Current => Chosen ?? All[0];

    public string ApiBase => Current.ApiBase.TrimEnd('/');

    public string Url(string route) => ApiBase + "/" + route.TrimStart('/');

    public Task ChooseAsync(string id)
    {
        ChosenId = id;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        ChosenId = null;
        return Task.CompletedTask;
    }
}
