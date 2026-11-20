using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): canonical JSON, RFC 8785, and the hash over it (part 07 § 1).
/// Members are sorted by UTF-16 code units, a number prints as ECMAScript prints a double, a
/// string escapes only what it must, and nothing is padded. Two documents that mean the same
/// write the same text, so the same hash.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The deepest nesting written; anything deeper is written as <c>null</c>, so no document can overflow the stack.</summary>
    private const int MaxDepth = 256;

    /// <summary>
    /// The canonical text of <paramref name="node"/> (<c>null</c> is JSON null). A number no finite
    /// double holds (<c>1e400</c>) is written as <c>null</c>, part 03 § 7's rule for non-finite
    /// numbers; so is anything nested deeper than 256 levels. Never throws on package content.
    /// </summary>
    public static string Write(JsonNode? node)
    {
        var text = new StringBuilder();
        Append(text, node, 0);
        return text.ToString();
    }

    /// <summary><c>sha256:</c> and 64 lowercase hex digits over the UTF-8 of <see cref="Write"/>.</summary>
    public static string Hash(JsonNode? node) => HashText(Write(node));

    /// <summary>The same hash over <paramref name="text"/> as written.</summary>
    public static string HashText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static void Append(StringBuilder text, JsonNode? node, int depth)
    {
        if (depth > MaxDepth)
        {
            text.Append("null");
            return;
        }

        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject obj:
                text.Append('{');
                var first = true;
                foreach (var member in obj.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    first = false;
                    AppendString(text, member.Key);
                    text.Append(':');
                    Append(text, member.Value, depth + 1);
                }

                text.Append('}');
                break;
            case JsonArray array:
                text.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    Append(text, array[i], depth + 1);
                }

                text.Append(']');
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        AppendString(text, value.GetValue<string>());
                        break;
                    case JsonValueKind.True:
                        text.Append("true");
                        break;
                    case JsonValueKind.False:
                        text.Append("false");
                        break;
                    case JsonValueKind.Number:
                        text.Append(Number(double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture)));
                        break;
                    default:
                        text.Append("null");
                        break;
                }

                break;
        }
    }

    /// <summary>RFC 8785 § 3.2.2.2: <c>\b \t \n \f \r \" \\</c> short, other controls as <c>\u00xx</c>, everything else as written.</summary>
    private static void AppendString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\t': text.Append("\\t"); break;
                case '\n': text.Append("\\n"); break;
                case '\f': text.Append("\\f"); break;
                case '\r': text.Append("\\r"); break;
                case < ' ': text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: text.Append(c); break;
            }
        }

        text.Append('"');
    }

    /// <summary>
    /// A double as ECMAScript's Number::toString prints it: the shortest digits that round-trip,
    /// in exponent form only below 1e-6 and from 1e21.
    /// </summary>
    private static string Number(double number)
    {
        if (number == 0)
        {
            return "0";
        }

        if (!double.IsFinite(number))
        {
            return "null";
        }

        // "R" is the shortest round-trip text; take its digits and decimal exponent, then lay them out.
        var r = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture);
            r = r[..e];
        }

        var dot = r.IndexOf('.');
        var digits = dot >= 0 ? r.Remove(dot, 1) : r;
        var pointAt = (dot >= 0 ? dot : r.Length) + exponent;
        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0').TrimEnd('0');
        pointAt -= leading;

        // value = 0.<digits> * 10^n, as the ECMAScript algorithm names them (k digits, n the point).
        var k = digits.Length;
        var n = pointAt;
        var sign = number < 0 ? "-" : "";
        if (k <= n && n <= 21)
        {
            return sign + digits + new string('0', n - k);
        }

        if (0 < n && n <= 21)
        {
            return sign + digits[..n] + "." + digits[n..];
        }

        if (-6 < n && n <= 0)
        {
            return sign + "0." + new string('0', -n) + digits;
        }

        var exp = n - 1;
        var expText = (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        return sign + (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + expText;
    }
}
