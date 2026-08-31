using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #582: the rerun verdict. The baseline is seeded once at Initialize; the
/// verdict is computed at completion over the package's own reproducible
/// claims and stamped on the record.
/// </summary>
public class RerunVerdictTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Entity()
    {
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(),
            Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>());
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static ConsultGenerationJobInitialize Init(ConsultRerunBaseline? baseline = null) =>
        new("job-1", "user-1",
            new[] { new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" } },
            RerunBaseline: baseline);

    private static ConsultRerunBaseline Baseline(params (string Key, string In, string Out)[] nodes) =>
        new("source-job-1", "aaaa", 5,
            nodes.ToDictionary(n => n.Key, n => new ConsultRerunBaselineNode(n.In, n.Out, 5), StringComparer.Ordinal));

    [Fact]
    public async Task TheBaseline_SeedsOnce_AndALaterInitializeCannotReplaceIt()
    {
        var (entity, state) = Entity();
        var baseline = Baseline(("extract", "in1", "out1"));

        await entity.Initialize(Init(baseline));
        Assert.Same(baseline, state().RerunBaseline);

        // The engine's replay-safe second Initialize carries no baseline —
        // the ??= keeps the seeded one, exactly as InputsBlob works.
        await entity.Initialize(Init());
        Assert.Same(baseline, state().RerunBaseline);
    }

    [Fact]
    public async Task AnOrdinaryInitialize_SeedsNoBaseline()
    {
        var (entity, state) = Entity();

        await entity.Initialize(Init());

        Assert.Null(state().RerunBaseline);
        Assert.Null(state().RerunVerdict);
    }
}
