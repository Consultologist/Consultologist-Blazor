using System.Reflection;
using Microsoft.Azure.Functions.Worker;

namespace Consultologist.Api.Tests;

/// <summary>
/// #623's live demo found run-prompt-node registered against a helper with no
/// trigger: a doc comment slid a new method between [Function] and the method
/// it named, the source generator emitted an entry point with zero bindings,
/// and every consult run failed at dispatch — while the suite stayed green,
/// because tests call the method, not the registration. This walks what the
/// generator walks.
/// </summary>
public class FunctionRegistrationTests
{
    private static IEnumerable<MethodInfo> FunctionMethods() =>
        typeof(Api.Jobs.RunPromptNodeActivity).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() != null);

    [Fact]
    public void EveryFunction_HasExactlyOneTriggerParameter()
    {
        var offenders = FunctionMethods()
            .Where(m => m.GetParameters().Count(p => p.GetCustomAttributes()
                .Any(a => a.GetType().Name.EndsWith("TriggerAttribute", StringComparison.Ordinal))) != 1)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryFunctionName_IsRegisteredOnce()
    {
        var duplicates = FunctionMethods()
            .GroupBy(m => m.GetCustomAttribute<FunctionAttribute>()!.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void RunPromptNode_IsTheActivity_NotAHelper()
    {
        var method = FunctionMethods().Single(m =>
            m.GetCustomAttribute<FunctionAttribute>()!.Name == Api.Jobs.ConsultGenerationActivityNames.RunPromptNode);

        Assert.Equal("RunAsync", method.Name);
        Assert.Contains(method.GetParameters(), p => p.GetCustomAttribute<ActivityTriggerAttribute>() != null);
    }
}
