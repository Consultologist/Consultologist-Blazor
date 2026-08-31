using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests;

public class ConsultGenerationStateSourceTests
{
    private static ConsultGenerationJobState CreateState(string? source)
    {
        var state = ConsultGenerationJobState.Create(
            "job-1",
            "user-1",
            new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" }
            });
        state.Source = source;
        return state;
    }

    [Fact]
    public void ToIndexEntry_CarriesSource()
    {
        Assert.Equal("email", CreateState("email").ToIndexEntry().Source);
    }

    [Fact]
    public void ToResponse_CarriesSource()
    {
        Assert.Equal("email", CreateState("email").ToResponse().Source);
    }

    [Fact]
    public void NullSource_RoundTripsForLegacyRecords()
    {
        var state = CreateState(null);

        Assert.Null(state.ToIndexEntry().Source);
        Assert.Null(state.ToResponse().Source);
    }

    [Fact]
    public async Task Initialize_StampsTheHostAndTheEngineOnce()
    {
        // #514: where the job ran and what ran it, write-once like Source — a
        // re-Initialize from the orchestrator never moves them, and a record
        // born without them stays without them.
        var stateProperty = typeof(ConsultGenerationJobEntity)
            .GetProperty("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var entity = new ConsultGenerationJobEntity(NSubstitute.Substitute.For<IConsultGenerationJobIndexStore>(), NSubstitute.Substitute.For<IJobOutputsBlobStore>());
        var items = new List<IReadOnlyDictionary<string, string>> { new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" } };

        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", items, ApiHost: "east.ca.api.consultologist.ai", EngineCommit: new string('a', 40)));
        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", items, ApiHost: "west.ca.api.consultologist.ai", EngineCommit: new string('b', 40)));

        var state = (ConsultGenerationJobState)stateProperty.GetValue(entity)!;
        Assert.Equal("east.ca.api.consultologist.ai", state.ApiHost);
        Assert.Equal(new string('a', 40), state.EngineCommit);
        Assert.Equal("east.ca.api.consultologist.ai", state.ToResponse().ApiHost);
        Assert.Equal(new string('a', 40), state.ToResponse().EngineCommit);
    }

    [Fact]
    public async Task Initialize_StampsSourceOnceAndNeverOverwrites()
    {
        var stateProperty = typeof(ConsultGenerationJobEntity)
            .GetProperty("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var indexStore = NSubstitute.Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(indexStore, NSubstitute.Substitute.For<IJobOutputsBlobStore>());

        var items = new List<IReadOnlyDictionary<string, string>>
        {
            new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" }
        };

        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", items, Source: "email"));
        Assert.Equal("email", ((ConsultGenerationJobState)stateProperty.GetValue(entity)!).Source);

        // The orchestrator's defensive re-Initialize must not overwrite.
        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", items, Source: "app"));
        Assert.Equal("email", ((ConsultGenerationJobState)stateProperty.GetValue(entity)!).Source);
    }
}
