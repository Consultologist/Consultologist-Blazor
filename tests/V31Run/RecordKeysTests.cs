using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>#1506 (specVersion 31, area 09; D10; Review Focus 2): a row key as a table's row key.</summary>
public class RecordKeysTests
{
    [Theory]
    [InlineData("node.a.b#2:3/1/x")]
    [InlineData("delivery:deliverable.emr.note/email")]
    [InlineData("node.x/if:macro.y")]
    [InlineData("a?b\\c%d!e\u0001f\u007Fg\u009Fh")]
    public void Escape_RoundTripsThroughTables(string key)
    {
        var escaped = RecordKeys.Escape(key);
        Assert.DoesNotContain(escaped, c => c is '/' or '\\' or '#' or '?' || char.IsControl(c));
        Assert.Equal(key, Unescape(escaped));
    }

    [Fact]
    public void Escape_KeepsDifferentKeysApart()
    {
        Assert.NotEqual(RecordKeys.Escape("a/b"), RecordKeys.Escape("a!2Fb"));
        Assert.Equal("a!2Fb", RecordKeys.Escape("a/b"));
        Assert.Equal("a!21!2F", RecordKeys.Escape("a!/"));
        Assert.Equal("node.plain:1", RecordKeys.Escape("node.plain:1"));
    }

    [Fact]
    public void Escape_HashesAKeyPastTheLimit()
    {
        var key = new string('k', 600);
        var escaped = RecordKeys.Escape(key);
        Assert.Matches("^~[0-9a-f]{64}$", escaped);
        Assert.NotEqual(escaped, RecordKeys.Escape(new string('k', 599) + "j"));

        // the limit counts the escaped result: 171 slashes escape to 513 characters
        Assert.StartsWith("~", RecordKeys.Escape(new string('/', 171)));
        Assert.Equal(510, RecordKeys.Escape(new string('/', 170)).Length);
        Assert.Equal(RecordKeys.MaxRowKey, RecordKeys.Escape(new string('k', RecordKeys.MaxRowKey)).Length);
    }

    [Fact]
    public void Escape_NeverGivesAHashedFormToAnotherKey()
    {
        var long_ = new string('k', 600);
        var hashed = RecordKeys.Escape(long_);
        Assert.NotEqual(hashed, RecordKeys.Escape(hashed));
        Assert.DoesNotContain('~', RecordKeys.Escape("a~b"));
        Assert.Equal("a!7Eb", RecordKeys.Escape("a~b"));
    }

    private static string Unescape(string escaped)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] == '!')
            {
                text.Append((char)Convert.ToInt32(escaped.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
                text.Append(escaped[i]);
        }

        return text.ToString();
    }
}
