using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>#1502 (specVersion 31, area 05): a reference string is a path with no position segment (D13).</summary>
public class ReferenceStringTests
{
    [Theory]
    [InlineData("node.plan.value.sections", 4)]
    [InlineData("item", 1)]
    [InlineData("snippet.standards", 2)]
    [InlineData("", null)]
    [InlineData("node..x", null)]
    [InlineData("node.x.value.0", null)]
    [InlineData(" node.x", null)]
    [InlineData("node.x ", null)]
    [InlineData(".node", null)]
    [InlineData(null, null)]
    public void Parse(string? text, int? segments)
    {
        var parsed = ReferenceString.Parse(text);

        if (segments == null)
        {
            Assert.Null(parsed);
            return;
        }

        Assert.NotNull(parsed);
        Assert.Equal(text, parsed.Text);
        Assert.Equal(segments, parsed.Segments.Count);
        Assert.Equal(text, string.Join('.', parsed.Segments));
    }
}
