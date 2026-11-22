using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1506 (specVersion 31, area 09; D10; part 08 § 1.1): a row's key as a table's row key. Azure Tables refuse <c>/</c>,
/// <c>\</c>, <c>#</c>, <c>?</c> and control characters in a row key, and a row key is at most 1 KiB.
/// </summary>
public static class RecordKeys
{
    /// <summary>The longest escaped key kept as it is, in UTF-16 characters; a longer one is its hash.</summary>
    public const int MaxRowKey = 512;

    /// <summary>
    /// <paramref name="key"/> with <c>!</c>, <c>/</c>, <c>\</c>, <c>#</c>, <c>?</c>, <c>%</c> and each character below U+0020 or
    /// from U+007F to U+009F written as <c>!</c> and two hex digits; a result longer than <see cref="MaxRowKey"/> is <c>~</c>
    /// and the SHA-256 hex (lowercase) of the key's UTF-8. Two different keys never give the same result.
    /// </summary>
    public static string Escape(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var text = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (c is '!' or '/' or '\\' or '#' or '?' or '%' or < ' ' or (>= '\u007F' and <= '\u009F'))
                text.Append('!').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else
                text.Append(c);
        }

        return text.Length > MaxRowKey
            ? "~" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()
            : text.ToString();
    }
}
