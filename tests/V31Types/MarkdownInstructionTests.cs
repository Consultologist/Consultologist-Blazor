using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1510 (specVersion 31, area 13, G-D12): the engine's Markdown instruction to the model is the built-in
/// format's description (part 02 § 3.5); it says how to write separate lines.
/// </summary>
public class MarkdownInstructionTests
{
    private static readonly FormatSet Formats = FormatSet.ForPackage(TypeFixture.Published(), []);

    private static string Description(string name) =>
        (string)Formats.Find(name)!.Schema["description"]!;

    [Fact]
    public void TheMarkdownFormats_TellTheModelHowToWriteSeparateLines()
    {
        Assert.EndsWith(
            "A single line ending is not a break: write separate lines as list items, or leave a blank line between them.",
            Description("markdown_block"));
        Assert.EndsWith(
            "It holds no line ending; separate lines are list items or paragraphs with a blank line between them, in Markdown that stands on its own lines.",
            Description("markdown_inline"));
    }
}
