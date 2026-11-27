namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 12, fix round 1; part 08 § 2.1): row keys in the order a person counts them. The rooted key (up to the
/// first <c>:</c> or <c>#</c>) compares as text, ordinally; what follows, the item path (<c>:2/10</c>) and the passes
/// (<c>#10</c>, <c>#2:3</c>), compares run by run: a run of digits as a number, anything else ordinally. So
/// <c>draft:2/10</c> follows <c>draft:2/9</c>, <c>judge#10</c> follows <c>judge#9</c>, and a key precedes its instances.
/// Shared by the Create page's rail and History.
/// </summary>
public sealed class RowKeyOrder31 : IComparer<string>
{
    public static readonly RowKeyOrder31 Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xTail = TailStart(x);
        var yTail = TailStart(y);
        var byRoot = string.CompareOrdinal(x[..xTail], y[..yTail]);
        return byRoot != 0 ? byRoot : CompareTails(x, xTail, y, yTail);
    }

    private static int TailStart(string key) => RowKey31.InstanceStart(key);

    private static int CompareTails(string x, int i, string y, int j)
    {
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var xEnd = i;
                while (xEnd < x.Length && char.IsAsciiDigit(x[xEnd]))
                {
                    xEnd++;
                }

                var yEnd = j;
                while (yEnd < y.Length && char.IsAsciiDigit(y[yEnd]))
                {
                    yEnd++;
                }

                // numbers of any length: fewer significant digits is smaller, then digit by digit
                var xDigits = x[i..xEnd].TrimStart('0');
                var yDigits = y[j..yEnd].TrimStart('0');
                var byNumber = xDigits.Length != yDigits.Length
                    ? xDigits.Length.CompareTo(yDigits.Length)
                    : string.CompareOrdinal(xDigits, yDigits);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                i = xEnd;
                j = yEnd;
                continue;
            }

            var byChar = x[i].CompareTo(y[j]);
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
