using System.Text;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.6; part 08 § 4; Review Focus 1): <see cref="MacroReply.Process"/>, a model's
/// reply checked for the markers of the macros offered and replaced literally. One test per row of § 8.6's lists (V-1 to
/// V-7, each with its exact sentence), the two replacements, the near-misses, and replies that play with markers: the reply
/// is judged as literal text, never rendered, and what is substituted is never scanned again. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MacroReplyTests
{
    private const string Letrozole = "Discussed side effects including hot flashes. Advised to monitor side effects.";

    private static readonly string[] Package = ["letrozole", "follow_up", "section_notes.hpi", "unused"];

    private static OfferedMacro Offered(string id, bool block, string body) =>
        new($"macro.{id}", $"macro.{id}.content", block, body, CanonicalJson.HashText(body), []);

    private static readonly OfferedMacro Block = Offered("letrozole", true, Letrozole);

    private static readonly OfferedMacro Inline = Offered("follow_up", false, "in six weeks with Dr A. Sample");

    private static MacroReplyResult Process(string reply, bool ordered = false, params OfferedMacro[] offered) =>
        MacroReply.Process(reply, offered, Package, ordered);

    /// <summary>The one fault of <paramref name="result"/>: its id, macro, sentence (held to its row) and part 08's reason.</summary>
    private static void AssertFault(MacroReplyResult result, string id, string macro, string sentence, string reason)
    {
        Assert.Null(result.Text);
        Assert.Empty(result.Placed);
        var fault = Assert.Single(result.Faults);
        Assert.Equal(new ReplyFault(id, macro, sentence, reason), fault);
        V31Contract.AssertSentence(new SourceRefusal(fault.Id, fault.Sentence, ""));
    }

    private static string Valid(MacroReplyResult result)
    {
        Assert.True(result.Faults.Count == 0, string.Join(" | ", result.Faults.Select(f => f.Sentence)));
        Assert.NotNull(result.Text);
        return result.Text!;
    }

    // ---- § 8.6's rows ---------------------------------------------------------------------

    [Fact]
    public void V1_AMarkerMissing()
    {
        AssertFault(Process("Assessment: stable.\n", false, Block),
            "S05-053", "letrozole", "The reply has no marker for macro 'letrozole'.", "macro letrozole: missing");
    }

    [Fact]
    public void V2_AMarkerRepeated()
    {
        AssertFault(Process("{{~ macro.letrozole.content }}\nPlan.\n{{~ macro.letrozole.content }}\n", false, Block),
            "S05-054", "letrozole", "The reply has the marker for macro 'letrozole' 2 times.", "macro letrozole: repeated");
    }

    [Fact]
    public void V3_AMarkerOfAMacroNotOffered()
    {
        AssertFault(Process("{{~ macro.letrozole.content }}\nReview {{ macro.unused.content }}.\n", false, Block),
            "S05-055", "unused", "The reply has a marker for macro 'unused', which wasn't offered.", "macro unused: not offered");
    }

    [Fact]
    public void V4_AMarkerOfNoMacro()
    {
        AssertFault(Process("{{~ macro.letrozole.content }}\nAnd {{ macro.nothing_here.content }}.\n", false, Block),
            "S05-056", "nothing_here", "The reply has a marker that names no macro.", "macro nothing_here: unknown");
    }

    [Fact]
    public void AMarkerNamingNoMacro_IsSaidWithoutItsId()
    {
        // Ruled by the owner (#1506, ruling 7 of the third comment): a recorded sentence quotes nothing from the reply.
        var result = Process("{{~ macro.letrozole.content }}\nSee {{ macro.smith_john.content }}.\n", false, Block);

        var fault = Assert.Single(result.Faults);
        Assert.Equal("S05-056", fault.Id);
        Assert.Equal("The reply has a marker that names no macro.", fault.Sentence);
        Assert.DoesNotContain("smith_john", fault.Sentence);
    }

    [Fact]
    public void V5_AMarkerInTheOtherForm()
    {
        const string blockAsked = "The reply writes the marker for macro 'letrozole' inside a sentence, but it was asked for alone on a line.";
        const string inlineAsked = "The reply writes the marker for macro 'follow_up' alone on a line, but it was asked for inside a sentence.";

        // The inline form where a block was asked, inside a sentence and alone on its line; the block form with text before it.
        AssertFault(Process("Plan: {{ macro.letrozole.content }} today.\n", false, Block), "S05-057", "letrozole", blockAsked, "macro letrozole: altered");
        AssertFault(Process("Plan:\n{{ macro.letrozole.content }}\n", false, Block), "S05-057", "letrozole", blockAsked, "macro letrozole: altered");
        AssertFault(Process("Plan: {{~ macro.letrozole.content }}\n", false, Block), "S05-057", "letrozole", blockAsked, "macro letrozole: altered");

        // The block form where an inline one was asked, alone on its line and inside a sentence.
        AssertFault(Process("Follow up\n{{~ macro.follow_up.content }}\n", false, Inline), "S05-057", "follow_up", inlineAsked, "macro follow_up: altered");
        AssertFault(Process("Follow up {{~ macro.follow_up.content }}.\n", false, Inline), "S05-057", "follow_up", inlineAsked, "macro follow_up: altered");
    }

    [Fact]
    public void V6_TextAfterABlockMarker()
    {
        foreach (var after in new[] { " and more", ".", "\tx", " \t" })
        {
            AssertFault(Process($"Plan:\n  {{{{~ macro.letrozole.content }}}}{after}\nEnd.\n", false, Block),
                "S05-058", "letrozole", "The reply has text after the marker for macro 'letrozole' on its line.", "macro letrozole: altered");
        }
    }

    [Fact]
    public void V7_MarkersOutOfOrder()
    {
        const string reply = "Return {{ macro.follow_up.content }}.\n{{~ macro.letrozole.content }}\n";

        AssertFault(Process(reply, true, Block, Inline),
            "S05-059", "follow_up", "The reply has the markers out of order: 'follow_up' before 'letrozole'.", "macro follow_up: out of order");
    }

    [Fact]
    public void UnorderedAcceptsAnyOrder()
    {
        const string reply = "Return {{ macro.follow_up.content }}.\n{{~ macro.letrozole.content }}\n";

        Assert.Equal($"Return in six weeks with Dr A. Sample.\n{Letrozole}\n", Valid(Process(reply, false, Block, Inline)));
        Assert.Equal(["macro.letrozole", "macro.follow_up"], Process(reply, false, Block, Inline).Placed);
        Assert.Equal(
            $"{Letrozole}\nReturn in six weeks with Dr A. Sample.\n",
            Valid(Process("{{~ macro.letrozole.content }}\nReturn {{ macro.follow_up.content }}.\n", true, Block, Inline)));
    }

    [Fact]
    public void EveryFault_IsSaid_InOrder()
    {
        // Each occurrence first (unknown, not offered), then each offered macro in prompt order, then the order.
        var result = Process("{{ macro.nope.content }} {{ macro.unused.content }}\n{{ macro.letrozole.content }}\n", false, Block, Inline);

        Assert.Null(result.Text);
        Assert.Equal(["S05-056", "S05-055", "S05-057", "S05-053"], result.Faults.Select(f => f.Id));
        Assert.All(result.Faults, f => V31Contract.AssertSentence(new SourceRefusal(f.Id, f.Sentence, "")));
    }

    // ---- the replacement ----------------------------------------------------------------------

    [Fact]
    public void ABlockMarker_LosesIndentationAndTrailingSpaces_AndKeepsItsNewline()
    {
        Assert.Equal($"Plan:\n{Letrozole}\nEnd.\n", Valid(Process("Plan:\n\t  {{~ macro.letrozole.content }}   \nEnd.\n", false, Block)));
        Assert.Equal($"Plan:\r\n{Letrozole}\r\nEnd.", Valid(Process("Plan:\r\n {{~ macro.letrozole.content }} \r\nEnd.", false, Block)));
        Assert.Equal($"Plan:\n{Letrozole}", Valid(Process("Plan:\n    {{~ macro.letrozole.content }}  ", false, Block)));
        Assert.Equal(Letrozole, Valid(Process("{{~ macro.letrozole.content }}", false, Block)));
    }

    [Fact]
    public void AnInlineMarker_IsReplacedInPlace()
    {
        Assert.Equal("Return  in six weeks with Dr A. Sample .\n", Valid(Process("Return  {{ macro.follow_up.content }} .\n", false, Inline)));

        // Alone on its line is still in place when the inline form was asked: its indentation and spaces stay.
        Assert.Equal("Return\n  in six weeks with Dr A. Sample  \n", Valid(Process("Return\n  {{ macro.follow_up.content }}  \n", false, Inline)));
    }

    [Fact]
    public void AFolderItemsMarker_IsQualified()
    {
        var hpi = Offered("section_notes.hpi", true, "History noted: Dr A. Sample.");

        Assert.Equal("History noted: Dr A. Sample.\n", Valid(Process("{{~ macro.section_notes.hpi.content }}\n", false, hpi)));
        var bare = Process("{{~ macro.hpi.content }}\n", false, hpi);
        Assert.Equal(["S05-056", "S05-053"], bare.Faults.Select(f => f.Id));
        Assert.Equal("The reply has a marker that names no macro.", bare.Faults[0].Sentence);
        Assert.Equal("The reply has no marker for macro 'section_notes.hpi'.", bare.Faults[1].Sentence);
    }

    [Fact]
    public void NearMisses_AreNotMarkers()
    {
        var near = new[]
        {
            "{{macro.letrozole.content}}", "{{ macro.Letrozole.content }}", "{{  macro.letrozole.content }}",
            "{{ macro.letrozole.content  }}", "{{~macro.letrozole.content }}", "{{- macro.letrozole.content }}",
            "{{ macro.letrozole.content ~}}", "{{ macro.letrozole }}", "{{ macro.letrozole.contents }}",
            "{{ macro..content }}", "{{ macro.letrozole.content}}", "{ { macro.letrozole.content } }",
            "{{ macro.a.b.letrozole.content }}", $"{{{{ macro.{new string('a', 65)}.content }}}}",
        };
        foreach (var written in near)
        {
            AssertFault(Process($"Plan:\n{written}\n", false, Block),
                "S05-053", "letrozole", "The reply has no marker for macro 'letrozole'.", "macro letrozole: missing");
        }
    }

    [Fact]
    public void AnyReply_IsJudgedLiterally()
    {
        // A marker in a code fence is a marker.
        Assert.Equal($"```\n{Letrozole}\n```\n", Valid(Process("```\n{{~ macro.letrozole.content }}\n```\n", false, Block)));

        // A body holding another macro's marker text (and a run spot's) is substituted and never processed again.
        var holding = Offered("letrozole", true, "See {{ macro.follow_up.content }} and {{run:1}}.");
        Assert.Equal(
            "See {{ macro.follow_up.content }} and {{run:1}}.\nReturn in six weeks with Dr A. Sample.\n",
            Valid(Process("{{~ macro.letrozole.content }}\nReturn {{ macro.follow_up.content }}.\n", true, holding, Inline)));

        // A reply of only the marker.
        Assert.Equal("in six weeks with Dr A. Sample", Valid(Process("{{ macro.follow_up.content }}", false, Inline)));

        // Template syntax the model wrote comes back with its bytes.
        const string syntax = "{{ for x in y }}{{ x }}{{ end }} {{ include 'a' }} {%{ raw }%} {{ macro.follow_up.content }} {{ node.x.content }}";
        Assert.Equal(
            "{{ for x in y }}{{ x }}{{ end }} {{ include 'a' }} {%{ raw }%} in six weeks with Dr A. Sample {{ node.x.content }}",
            Valid(Process(syntax, false, Inline)));
        const string unmarked = "{{ for x in y }}{{ x }}{{ end }} {{ include 'a' }} {%{ raw }%} {{ node.x.content }}";
        Assert.Equal(unmarked, Valid(Process(unmarked, false)));

        // A reply of 2 MiB, the marker at its end.
        var large = new StringBuilder().Append('x', 2 * 1024 * 1024).Append("\n{{~ macro.letrozole.content }}\n").ToString();
        var replaced = Valid(Process(large, false, Block));
        Assert.Equal(2 * 1024 * 1024 + 1 + Letrozole.Length + 1, replaced.Length);
        Assert.EndsWith($"\n{Letrozole}\n", replaced, StringComparison.Ordinal);

        // An empty reply with a macro offered is V-1; with none, it is the reply.
        AssertFault(Process("", false, Block), "S05-053", "letrozole", "The reply has no marker for macro 'letrozole'.", "macro letrozole: missing");
        Assert.Equal("", Valid(Process("", false)));
    }

    [Fact]
    public void AOneLineReplyFullOfMarkers_IsScannedPromptly()
    {
        // Fix round 1 (M4): 2 MiB on one line, every marker on it; the line's start is never searched for again.
        const string marker = "{{ macro.follow_up.content }} {{~ macro.letrozole.content }} ";
        var reply = new StringBuilder();
        while (reply.Length < 2 * 1024 * 1024)
            reply.Append(marker);
        var count = reply.Length / marker.Length;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = Process(reply.ToString(), false, Block, Inline);

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.Equal(["S05-054", "S05-054"], result.Faults.Select(f => f.Id));
        Assert.Equal($"The reply has the marker for macro 'letrozole' {count} times.", result.Faults[0].Sentence);
    }

    [Fact]
    public void Process_NeverThrows()
    {
        foreach (var reply in new[] { "{{", "{{ ", "{{~", "{{ macro.", "{{ macro.x", "{{ macro.x.content", "{{ macro.x.content }", "}}{{", "\r", "\n\n{{~ macro.letrozole.content }}\r" })
        {
            var result = Process(reply, true, Block, Inline);
            Assert.True(result.Text != null || result.Faults.Count > 0);
        }

        Assert.Throws<ArgumentNullException>(() => MacroReply.Process(null!, [], Package, false));
    }
}
