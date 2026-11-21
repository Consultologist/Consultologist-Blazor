using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.V31;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (Task 13's review; D9): the host's own registrations (<see cref="ApiServices.AddApiServices"/>, which
/// <c>Program.cs</c> calls) give the job starter its 31 branch and its drain, the 31 starter the same drain, and the
/// drain's endpoints and activity what they need. The starter takes the two through optional constructor parameters, so
/// a registration gone missing would not fail the host: 31 packages would silently take the 30 path, and no start would
/// ever park. Nothing here connects to anything: the stores are built over a local connection string, never called.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void TheHostsRegistrations_WireThe31StarterAndTheDrain()
    {
        using var provider = HostServices();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        var drain = services.GetRequiredService<IEngineDrain>();
        Assert.IsType<EngineDrain>(drain);
        Assert.IsType<TextDrainBlobs>(services.GetRequiredService<IDrainBlobs>());

        var starter = Assert.IsType<ConsultGenerationJobStarter>(services.GetRequiredService<IConsultGenerationJobStarter>());
        var v31 = Assert.IsType<V31JobStarter>(FieldOf<V31JobStarter>(starter));
        Assert.Same(services.GetRequiredService<V31JobStarter>(), v31);
        Assert.Same(drain, FieldOf<IEngineDrain>(starter));
        Assert.Same(drain, FieldOf<IEngineDrain>(v31));

        // the drain's endpoints read Operators__AppUserIds (no operator set of their own), and its activity resolves
        var admin = services.GetRequiredService<AdminDrain>();
        Assert.Same(drain, FieldOf<IEngineDrain>(admin));
        Assert.Null(FieldOf<IReadOnlySet<string>>(admin));
        Assert.Same(drain, FieldOf<IEngineDrain>(ActivatorUtilities.CreateInstance<CheckDrainActivity>(services)));
    }

    /// <summary>The host's registrations over a configuration a test can give: a local storage connection string.</summary>
    private static ServiceProvider HostServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["Auth:Authority"] = "https://login.example.test/",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApiServices();
        return services.BuildServiceProvider();
    }

    /// <summary>The one instance field of <paramref name="owner"/> typed <typeparamref name="T"/> (a primary constructor's capture included).</summary>
    private static T? FieldOf<T>(object owner) =>
        (T?)owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(T)).GetValue(owner);
}
