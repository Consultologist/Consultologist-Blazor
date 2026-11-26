using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): evidence is publish-only (part 07 § 7): the compiled copies an upload holds are
/// dropped unread and named in <see cref="SourceParseResult.Dropped"/>; an import that held <c>resolve.json</c> or
/// <c>lock.json</c> is told once that they were ignored (S07-016); CI's warning for a committed copy is S07-015.
/// </summary>
public class EvidenceTests
{
    [Fact]
    public void AnUploadWithResolveJson_NamesItDropped_AndIsToldOnce()
    {
        var upload = SourceFixture.Minimal();
        upload["resolve.json"] = SourceFixture.Utf8("{}");
        upload["publish-report.json"] = SourceFixture.Utf8("{}");
        upload["nodes/lock.json"] = SourceFixture.Utf8("{}");

        var parsed = SourceParser.Parse(upload, SourceFixture.EmptyRegistry());

        Assert.Equal(["publish-report.json", "resolve.json"], parsed.Dropped);
        var refusal = ResolveSentences.ImportIgnored(parsed.Dropped);
        Assert.NotNull(refusal);
        Assert.Equal("S07-016", refusal!.Id);
        Assert.Equal("This package's resolve.json and lock.json were ignored; publishing writes new ones.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AnUploadWithLockJson_IsToldToo()
    {
        var upload = SourceFixture.Minimal();
        upload["lock.json"] = [0xFF];

        var dropped = SourceIntake.Read(upload).Dropped;

        Assert.Equal(["lock.json"], dropped);
        Assert.Equal("S07-016", ResolveSentences.ImportIgnored(dropped)?.Id);
    }

    [Fact]
    public void AnUploadedStamp_IsDroppedUnread_AndNamed()
    {
        // #1508 (area 11, Global Constraints): publish.json is evidence too; bytes that are not even text are never read.
        var upload = SourceFixture.Minimal();
        upload["publish.json"] = [0xFF];

        var intake = SourceIntake.Read(upload);

        Assert.Equal(["publish.json"], intake.Dropped);
        Assert.Empty(intake.Refusals);
        Assert.DoesNotContain(intake.Files, file => file.Key == "publish.json");
    }

    [Fact]
    public void AnUploadWithout_IsToldNothing()
    {
        var parsed = SourceParser.Parse(SourceFixture.Minimal(), SourceFixture.EmptyRegistry());
        Assert.Empty(parsed.Dropped);
        Assert.Null(ResolveSentences.ImportIgnored(parsed.Dropped));

        // A publish report alone is not a compiled document.
        Assert.Null(ResolveSentences.ImportIgnored(["publish-report.json"]));
    }

    [Fact]
    public void ARefusedUpload_StillNamesWhatWasDropped()
    {
        var upload = SourceFixture.Minimal();
        upload["lock.json"] = SourceFixture.Utf8("{}");
        upload["nodes/plan.md"] = [0xFF];

        var parsed = SourceParser.Parse(upload, SourceFixture.EmptyRegistry());

        Assert.NotEmpty(parsed.Refusals);
        Assert.Equal(["lock.json"], parsed.Dropped);
    }

    [Fact]
    public void ACommittedCopy_IsCIsWarning()
    {
        var warning = ResolveSentences.EvidenceCommitted("resolve.json");
        Assert.Equal("S07-015", warning.Id);
        Assert.Equal("resolve.json is evidence written only by publish; it is ignored and shouldn't be committed.", warning.Sentence);
        Assert.Equal("resolve.json", warning.Path);
        V31Contract.AssertSentence(warning);
    }
}
