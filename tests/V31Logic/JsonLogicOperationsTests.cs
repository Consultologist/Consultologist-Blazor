using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501: the operations of part 03 § 5 and their versions, held to <c>v31/constants.json</c>.</summary>
public class JsonLogicOperationsTests
{
    [Fact]
    public void Names_AreTheContractsNames()
    {
        var constants = V31Contract.Json("constants.json");

        Assert.Equal(
            constants["standardOperations"]!.AsArray().Select(n => n!.GetValue<string>()),
            JsonLogicOperations.All.Where(o => !o.Custom).Select(o => o.Name));

        var custom = constants["operations"]!.AsObject();
        Assert.Equal(custom.Select(m => m.Key), JsonLogicOperations.All.Where(o => o.Custom).Select(o => o.Name));
        Assert.All(custom, m => Assert.Equal(JsonLogicOperations.OperationVersion, m.Value!.GetValue<string>()));

        Assert.Equal(JsonLogicOperations.EvaluatorVersion, constants["evaluator"]!.GetValue<string>());
        Assert.Equal(JsonLogicOperations.PrinterVersion, constants["builtins"]!["printer"]!.GetValue<string>());
        Assert.Equal(
            constants["runFacts"]!.AsArray().Select(n => n!.GetValue<string>()),
            JsonLogicOperations.RunFacts);

        var limits = constants["limits"]!;
        Assert.Equal(IRegexp.MaxCodePoints, limits["matchesPatternCodePoints"]!.GetValue<int>());
        Assert.Equal(IRegexp.MaxRepetition, limits["matchesRepetition"]!.GetValue<int>());
        Assert.Equal(IRegexp.MaxTextBytes, limits["matchesTextBytes"]!.GetValue<int>());
    }

    [Fact]
    public void AllowedList_IsEveryName_InOrder()
    {
        Assert.Equal(
            string.Join(", ", JsonLogicOperations.All.Select(o => o.Name)),
            JsonLogicOperations.AllowedList);
        Assert.StartsWith("var, missing, if, ===", JsonLogicOperations.AllowedList, StringComparison.Ordinal);
        Assert.EndsWith("years_between, add_days", JsonLogicOperations.AllowedList, StringComparison.Ordinal);
        Assert.Null(JsonLogicOperations.Find("map"));
        Assert.Null(JsonLogicOperations.Find("VAR"));
    }

    [Theory]
    [InlineData("!", "1")]
    [InlineData("-", "1 or 2")]
    [InlineData("+", "2 or more")]
    [InlineData("min", "1 or more")]
    [InlineData("if", "3 or more")]
    [InlineData("count_pass", "3")]
    [InlineData("var", "1")]
    [InlineData("object", "1")]
    [InlineData("missing", "1 or more")]
    [InlineData("matches", "2")]
    public void Takes_SaysTheCount(string name, string expected)
    {
        Assert.Equal(expected, JsonLogicOperations.Takes(JsonLogicOperations.Find(name)!));
    }
}
