using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat.V31;
using Microsoft.Azure.Functions.Worker;
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

    [Fact]
    public void TheHostsRegistrations_GiveTheRecordsStores_AsSingletons_WithoutTouchingStorage()
    {
        // #1506 (Task 9): the record's rows (a table) and its text and document (blobs); built over a local connection
        // string and never called, so nothing is created
        using var provider = HostServices();
        using var scope = provider.CreateScope();

        Assert.IsType<TableRecordRowStore>(scope.ServiceProvider.GetRequiredService<IRecordRowStore>());
        Assert.IsType<BlobRecordStore>(scope.ServiceProvider.GetRequiredService<IRecordBlobStore>());
        Assert.Same(provider.GetRequiredService<IRecordRowStore>(), scope.ServiceProvider.GetRequiredService<IRecordRowStore>());
        Assert.Same(provider.GetRequiredService<IRecordBlobStore>(), scope.ServiceProvider.GetRequiredService<IRecordBlobStore>());
    }

    [Fact]
    public void TheHostsStartup_LoadsThe31AgentsCatalogue_SoADefectStopsTheHost()
    {
        // #1504 (D9): what Program.cs resolves before the host runs (ApiServices.ResolveAtStartup) includes the 31 agents
        // catalogue; resolved lazily, a missing or incoherent agents/v31 would instead fail every job start, 5-30 included.
        using (var provider = HostServices(preview: "31"))
        {
            ApiServices.ResolveAtStartup(provider);
            Assert.Contains(("judgement", 1), provider.GetRequiredService<V31AgentsCatalogue>().Agents);
        }

        using var broken = HostServices(services => services.AddSingleton<V31AgentsCatalogue>(
            _ => throw new InvalidOperationException("The 31 agents catalogue file 'agents/v31/agents.json' was not found.")), preview: "31");
        Assert.Contains("agents/v31", Assert.Throws<InvalidOperationException>(() => ApiServices.ResolveAtStartup(broken)).Message);
    }

    [Fact]
    public void WithPreviewUnset_StartupNeverReadsTheV31Catalogue()
    {
        // #1505 ruling 7 (R15): a 5-30 host does not touch agents/v31; a catalogue that throws when touched is never touched
        using var provider = HostServices(services => services.AddSingleton<V31AgentsCatalogue>(
            _ => throw new InvalidOperationException("The 31 agents catalogue was read.")));
        ApiServices.ResolveAtStartup(provider);
    }

    [Fact]
    public void WithPreviewUnset_NoRegistrationAndNoFunctionClass_ReadsTheV31Catalogue()
    {
        // #1505 ruling 7 (R15), the final review: not only the startup resolve. Every registration of the host's container
        // (the job starter, whose 31 branch's store holds the engine's capabilities, among them) and every function class,
        // built as the worker builds one, is built with a catalogue that throws when touched, and none touches it. Only the
        // catalogue's own seams are left out: the catalogue, the capabilities built from it, and their factory.
        var touched = 0;
        var collection = HostCollection(services => services.AddSingleton<V31AgentsCatalogue>(_ =>
        {
            Interlocked.Increment(ref touched);
            throw new InvalidOperationException(CatalogueRead);
        }));
        using var provider = collection.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        Type[] seams = [typeof(V31AgentsCatalogue), typeof(EngineCapabilities), typeof(EngineCapabilitiesFactory),
            typeof(Lazy<V31AgentsCatalogue>), typeof(Lazy<EngineCapabilities>)];

        var readers = new List<string>();
        var built = 0;
        void Build(string name, Func<object?> build)
        {
            try
            {
                build();
                built++;
            }
            catch (Exception ex) when (ReadsTheCatalogue(ex))
            {
                readers.Add(name);
            }
            catch (Exception)
            {
                // a registration this test's configuration cannot build (a setting it lacks); it did not read the catalogue
            }
        }

        foreach (var descriptor in collection.Where(d => !d.ServiceType.IsGenericTypeDefinition && !seams.Contains(d.ServiceType)))
            Build(descriptor.ServiceType.FullName!, () => services.GetServices(descriptor.ServiceType).ToList());

        var functionClasses = typeof(ApiServices).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.ContainsGenericParameters
                && t.GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => m.GetCustomAttribute<FunctionAttribute>() != null))
            .ToList();
        Assert.Contains(typeof(RunAgentNodeV31Activity), functionClasses);
        foreach (var type in functionClasses)
            Build(type.FullName!, () => ActivatorUtilities.CreateInstance(services, type));

        Assert.Empty(readers);
        Assert.Equal(0, touched);
        Assert.True(built > 100, $"only {built} registrations and function classes were built");
        Assert.IsType<ConsultGenerationJobStarter>(services.GetRequiredService<IConsultGenerationJobStarter>());
    }

    [Fact]
    public void WithPreviewAdmitting31_StartupResolvesIt()
    {
        using var provider = HostServices(preview: "31");
        ApiServices.ResolveAtStartup(provider);
        Assert.Contains(("judgement", 1), provider.GetRequiredService<V31AgentsCatalogue>().Agents);

        using var broken = HostServices(services => services.AddSingleton<V31AgentsCatalogue>(
            _ => throw new InvalidOperationException("The 31 agents catalogue file 'agents/v31/agents.json' was not found.")), preview: "31");
        Assert.Contains("agents/v31", Assert.Throws<InvalidOperationException>(() => ApiServices.ResolveAtStartup(broken)).Message);
    }

    private const string CatalogueRead = "The 31 agents catalogue was read.";

    private static bool ReadsTheCatalogue(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
        {
            if (ex.Message.Contains(CatalogueRead, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The host's registrations over a configuration a test can give: a local storage connection string; then <paramref name="change"/>.</summary>
    private static ServiceProvider HostServices(Action<IServiceCollection>? change = null, string? preview = null) =>
        HostCollection(change, preview).BuildServiceProvider();

    /// <summary>The collection <see cref="HostServices"/> builds.</summary>
    private static ServiceCollection HostCollection(Action<IServiceCollection>? change = null, string? preview = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["Auth:Authority"] = "https://login.example.test/",
                [SpecVersionAdmission.PreviewSetting] = preview,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApiServices();
        change?.Invoke(services);
        return services;
    }

    /// <summary>The one instance field of <paramref name="owner"/> typed <typeparamref name="T"/> (a primary constructor's capture included).</summary>
    private static T? FieldOf<T>(object owner) =>
        (T?)owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(T)).GetValue(owner);
}
