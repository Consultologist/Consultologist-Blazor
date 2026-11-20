using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): why a rule run could not give a value: the rule was declined (A04-011),
/// <c>matches</c> was given text over 1 MiB (S03-040), a condition yielded no boolean (S03-041), the evaluator failed
/// (A04-011), or <c>plain</c> was given Markdown nested too deeply to convert (A04-011).
/// </summary>
public enum JsonLogicFailureKind { Declined, MatchesTextTooLong, NotABoolean, Fault, PlainNotConverted }

/// <summary>#1501 (specVersion 31, area 04): a failure of a rule run; <paramref name="Detail"/> never holds a value from the data.</summary>
public sealed record JsonLogicFailure(JsonLogicFailureKind Kind, string Detail);

/// <summary>
/// #1501 (specVersion 31, area 04): thrown by an operation that fails the rule (S03-040 for <c>matches</c>, A04-011 for a
/// <c>plain</c> that cannot convert), the only failure <see cref="JsonLogicFunctions.Apply"/> throws on run data.
/// </summary>
public sealed class JsonLogicFailureException(JsonLogicFailure failure) : Exception(failure.Detail)
{
    /// <summary>The failure.</summary>
    public JsonLogicFailure Failure { get; } = failure;
}

/// <summary>
/// #1501 (specVersion 31, area 04): what each operation of part 03 §§ 5.1 to 5.5 computes, as plain C# over
/// <see cref="JsonNode"/> values, with D18 to D21: an operand of a kind its operation does not take reads as <c>null</c>,
/// nothing is coerced, and nothing throws on run data.
/// </summary>
public static class JsonLogicFunctions
{
    private const int MaxAddDays = 3_652_059; // days from 0001-01-01 to 9999-12-31

    private static readonly JsonLogicFailure PlainNotConverted =
        new(JsonLogicFailureKind.PlainNotConverted, "plain was given Markdown nested too deeply to convert");

    /// <summary>
    /// The value of <paramref name="operation"/> over already-evaluated <paramref name="operands"/>; a new node, never an operand.
    /// Not here: <c>var</c>, <c>missing</c>, <c>if</c>, <c>and</c>, <c>or</c>, <c>!</c>, <c>===</c>, <c>!==</c>, <c>object</c>.
    /// </summary>
    /// <exception cref="JsonLogicFailureException"><c>matches</c> over text longer than 1 MiB; <c>plain</c>, told to convert, over
    /// Markdown nested too deeply for <see cref="PlainConversion.ToPlain"/> to convert.</exception>
    /// <exception cref="ArgumentException">A name this class does not compute.</exception>
    public static JsonNode? Apply(string operation, IReadOnlyList<JsonNode?> operands, bool plainConverts = true)
    {
        operands ??= [];
        JsonNode? At(int i) => i < operands.Count ? operands[i] : null;

        switch (operation)
        {
            case "+": return Fold(operands, (a, b) => a + b);
            case "*": return Fold(operands, (a, b) => a * b);
            case "-":
                if (operands.Count == 1)
                    return Number(Num(At(0)) is { } n ? -n : null);
                return operands.Count == 2 ? Number(Num(At(0)) - Num(At(1))) : null;
            case "/": return Binary(At(0), At(1), (a, b) => b == 0 ? null : a / b);
            case "%": return Binary(At(0), At(1), (a, b) => b == 0 ? null : a % b);
            case "min": return Fold(operands, Math.Min);
            case "max": return Fold(operands, Math.Max);
            case "<": return Compare(At(0), At(1), c => c < 0);
            case "<=": return Compare(At(0), At(1), c => c <= 0);
            case ">": return Compare(At(0), At(1), c => c > 0);
            case ">=": return Compare(At(0), At(1), c => c >= 0);
            case "in": return JsonValue.Create(In(At(0), At(1)));
            case "cat": return JsonValue.Create(Cat(operands));

            case "size":
                if (Text(At(0)) is { } sized)
                    return Number(sized.EnumerateRunes().Count());
                return At(0) is JsonArray list ? Number(list.Count) : null;
            case "blank":
                return JsonValue.Create(Text(At(0)) is not { } blank || blank.EnumerateRunes().All(Rune.IsWhiteSpace));
            case "words": return Text(At(0)) is { } w ? Number(Words(w)) : null;
            case "lines": return Text(At(0)) is { } l ? Number(Lines(l)) : null;
            case "lower": return Text(At(0)) is { } lo ? JsonValue.Create(lo.ToLowerInvariant()) : null;
            case "trim": return Text(At(0)) is { } t ? JsonValue.Create(Trim(t)) : null;
            case "starts_with":
                return JsonValue.Create(Text(At(0)) is { } s && Text(At(1)) is { } p && s.StartsWith(p, StringComparison.Ordinal));
            case "ends_with":
                return JsonValue.Create(Text(At(0)) is { } e && Text(At(1)) is { } q && e.EndsWith(q, StringComparison.Ordinal));
            case "plain":
                if (Text(At(0)) is not { } md)
                    return null;
                if (!plainConverts)
                    return JsonValue.Create(md);
                return JsonValue.Create(PlainConversion.ToPlain(md) ?? throw new JsonLogicFailureException(PlainNotConverted));
            case "matches": return JsonValue.Create(Matches(At(0), At(1)));
            case "has_word":
                return JsonValue.Create(Text(At(0)) is { } hay && Text(At(1)) is { } word && HasWord(hay, word));

            case "days_between":
                return Date(At(0)) is { } d1 && Date(At(1)) is { } d2 ? Number(d2.DayNumber - d1.DayNumber) : null;
            case "years_between":
                return Date(At(0)) is { } y1 && Date(At(1)) is { } y2 ? Number(YearsBetween(y1, y2)) : null;
            case "add_days": return AddDays(At(0), At(1));

            case "terms_subset": return TermsSubset(At(0), At(1), At(2));
            case "all_pass": return Aggregate(At(0), null, At(1), all: true);
            case "count_pass": return Aggregate(At(0), At(1), At(2), all: false);

            default:
                throw new ArgumentException($"'{operation}' is not an operation this class computes.", nameof(operation));
        }
    }

    /// <summary>
    /// JSON Logic's operand truthiness (part 03 § 6.4), the rule <c>if</c>, <c>and</c>, <c>or</c> and <c>!</c> apply to the
    /// data values they are given inside a rule: <c>false</c>, <c>null</c>, the empty list, <c>0</c> and <c>""</c> are false; anything else is true.
    /// It is not how a condition's result is tested: that is <see cref="IJsonLogicRule.Decide"/>, which is stricter and
    /// fails a result that is a number, text or an object (S03-041) where this reads <c>0</c> and <c>""</c> as false.
    /// </summary>
    public static bool Truthy(JsonNode? value) => value switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonObject => true,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.Number => Num(v) is { } n && n != 0,
            JsonValueKind.String => Text(v) is not "",
            _ => true,
        },
        _ => true,
    };

    /// <summary>The value at <paramref name="path"/> (names joined by dots), walking objects only; absent, or a step into a non-object, is <c>null</c>. Returns the node itself.</summary>
    public static JsonNode? Read(JsonNode? data, string path)
    {
        if (path is null)
            return null;
        var current = data;
        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }
        return current;
    }

    /// <summary>The paths whose read is <c>null</c> or <c>""</c>, in written order.</summary>
    public static JsonArray Missing(IReadOnlyList<string> paths, Func<string, JsonNode?> read)
    {
        var missing = new JsonArray();
        foreach (var path in paths ?? [])
        {
            var value = read(path);
            if (value is null || Text(value) is "" || (value is JsonValue v && v.GetValueKind() == JsonValueKind.Null))
                missing.Add(JsonValue.Create(path));
        }
        return missing;
    }

    // ---- reading operands ----

    private static string? Text(JsonNode? node) => JsonLogicShape.TextOf(node);

    private static double? Num(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        try
        {
            if (value.GetValueKind() != JsonValueKind.Number)
                return null;
            var number = double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return double.IsFinite(number) ? number : null;
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidOperationException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonNode? Number(double? value) =>
        value is { } v && double.IsFinite(v) ? JsonValue.Create(v == 0 ? 0d : v) : null;

    // ---- arithmetic and comparison ----

    private static JsonNode? Fold(IReadOnlyList<JsonNode?> operands, Func<double, double, double> step)
    {
        if (operands.Count == 0)
            return null;
        double? total = null;
        foreach (var operand in operands)
        {
            if (Num(operand) is not { } n)
                return null;
            total = total is { } t ? step(t, n) : n;
        }
        return Number(total);
    }

    private static JsonNode? Binary(JsonNode? a, JsonNode? b, Func<double, double, double?> step) =>
        Num(a) is { } x && Num(b) is { } y ? Number(step(x, y)) : null;

    private static JsonNode? Compare(JsonNode? a, JsonNode? b, Func<int, bool> test)
    {
        if (Num(a) is { } x && Num(b) is { } y)
            return JsonValue.Create(test(x.CompareTo(y)));
        if (Date(a) is { } da && Date(b) is { } db)
            return JsonValue.Create(test(da.CompareTo(db)));
        return JsonValue.Create(false);
    }

    private static bool In(JsonNode? needle, JsonNode? haystack)
    {
        if (haystack is JsonArray list)
            return list.Any(element => Same(needle, element));
        return Text(needle) is { } n && Text(haystack) is { } h && h.Contains(n, StringComparison.Ordinal);
    }

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (IsNull(a) || IsNull(b))
            return IsNull(a) && IsNull(b);
        if (Num(a) is { } x)
            return Num(b) is { } y && x == y;
        if (Text(a) is { } s)
            return Text(b) is { } t && string.Equals(s, t, StringComparison.Ordinal);
        if (a is JsonValue va && b is JsonValue vb)
        {
            var ka = va.GetValueKind();
            return ka is JsonValueKind.True or JsonValueKind.False && ka == vb.GetValueKind();
        }
        return false;
    }

    private static bool IsNull(JsonNode? node) => node is null || (node is JsonValue v && v.GetValueKind() == JsonValueKind.Null);

    private static string Cat(IReadOnlyList<JsonNode?> operands)
    {
        var text = new StringBuilder();
        foreach (var operand in operands)
        {
            if (Text(operand) is { } s)
                text.Append(s);
            else if (Num(operand) is not null)
                text.Append(CanonicalJson.Write(operand));
        }
        return text.ToString();
    }

    // ---- text ----

    private static int Words(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var space = Rune.IsWhiteSpace(rune);
            if (!space && !inWord)
                count++;
            inWord = !space;
        }
        return count;
    }

    private static int Lines(string text)
    {
        if (text.Length == 0)
            return 0;
        var breaks = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            else if (text[i] != '\r' && text[i] != '\n')
                continue;
            if (i < text.Length - 1)
                breaks++;
        }
        return 1 + breaks;
    }

    private static string Trim(string text)
    {
        var runes = text.EnumerateRunes().ToList();
        var start = 0;
        var end = runes.Count;
        while (start < end && Rune.IsWhiteSpace(runes[start]))
            start++;
        while (end > start && Rune.IsWhiteSpace(runes[end - 1]))
            end--;
        var result = new StringBuilder();
        for (var i = start; i < end; i++)
            result.Append(runes[i].ToString());
        return result.ToString();
    }

    private static bool Matches(JsonNode? x, JsonNode? pattern)
    {
        if (Text(x) is not { } text || Text(pattern) is not { } source)
            return false;
        var check = IRegexp.Check(source);
        if (check.Regex is not { } regex)
            return false;
        if (IRegexp.TooLong(text))
        {
            var mebibytes = Math.Ceiling(Encoding.UTF8.GetByteCount(text) / (double)IRegexp.MaxTextBytes * 10) / 10;
            throw new JsonLogicFailureException(new JsonLogicFailure(
                JsonLogicFailureKind.MatchesTextTooLong, mebibytes.ToString("0.0", CultureInfo.InvariantCulture)));
        }
        return IRegexp.IsMatch(regex, text);
    }

    private static bool HasWord(string text, string word)
    {
        if (word.Length == 0)
            return false;
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = at + 1 < text.Length ? text.IndexOf(word, at + 1, StringComparison.Ordinal) : -1)
        {
            var before = at == 0 ? (Rune?)null : LastRune(text.AsSpan(0, at));
            var afterAt = at + word.Length;
            var after = afterAt >= text.Length ? (Rune?)null : FirstRune(text.AsSpan(afterAt));
            if ((before is null || !IsWordRune(before.Value)) && (after is null || !IsWordRune(after.Value)))
                return true;
        }
        return false;
    }

    private static Rune LastRune(ReadOnlySpan<char> span)
    {
        Rune.DecodeLastFromUtf16(span, out var rune, out _);
        return rune;
    }

    private static Rune FirstRune(ReadOnlySpan<char> span)
    {
        Rune.DecodeFromUtf16(span, out var rune, out _);
        return rune;
    }

    private static bool IsWordRune(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
        or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
        or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;

    // ---- dates ----

    /// <summary>A text that is a date, <c>YYYY-MM-DD</c> in years 0001 to 9999 (D18); also the checker's test of a date literal (D24).</summary>
    internal static DateOnly? Date(JsonNode? node)
    {
        if (Text(node) is not { Length: 10 } s || s[4] != '-' || s[7] != '-')
            return null;
        for (var i = 0; i < 10; i++)
            if (i is not (4 or 7) && s[i] is < '0' or > '9')
                return null;
        var year = int.Parse(s.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var month = int.Parse(s.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return null;
        return new DateOnly(year, month, day);
    }

    private static int YearsBetween(DateOnly a, DateOnly b)
    {
        if (b < a)
            return -YearsBetween(b, a);
        var years = b.Year - a.Year;
        var anniversary = a.Month == 2 && a.Day == 29 && !DateTime.IsLeapYear(b.Year)
            ? new DateOnly(b.Year, 3, 1)
            : new DateOnly(b.Year, a.Month, a.Day);
        return b < anniversary ? years - 1 : years;
    }

    private static JsonNode? AddDays(JsonNode? date, JsonNode? days)
    {
        if (Date(date) is not { } d || Num(days) is not { } n || n != Math.Floor(n) || Math.Abs(n) > MaxAddDays)
            return null;
        var target = (long)d.DayNumber + (long)n;
        if (target < DateOnly.MinValue.DayNumber || target > DateOnly.MaxValue.DayNumber)
            return null;
        return JsonValue.Create(DateOnly.FromDayNumber((int)target).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    // ---- checks ----

    private static JsonObject CheckResult(bool pass, string? failWith, JsonArray? uncovered, JsonArray? untested, int? passed, int? total) => new()
    {
        ["pass"] = pass,
        ["message"] = pass ? null : JsonValue.Create(failWith ?? ""),
        ["uncovered"] = uncovered,
        ["untested"] = untested,
        ["passed"] = passed,
        ["total"] = total,
    };

    private sealed record Concept(string Term, string? Id, bool Codable);

    private static List<Concept> Concepts(JsonNode? list)
    {
        var result = new List<Concept>();
        if (list is not JsonObject obj || !obj.TryGetPropertyValue("concepts", out var concepts) || concepts is not JsonArray array)
            return result;
        foreach (var element in array)
        {
            // Every object is a concept (as at 30, nothing is dropped): one whose term is not text is named by its id
            // when that is text, else as "", so a codable concept without a term still counts against the check.
            if (element is not JsonObject concept)
                continue;
            var id = Text(concept["id"]);
            var term = Text(concept["term"]) ?? id ?? "";
            var codable = Flag(concept["isSnomedConcept"]) && Flag(concept["isActive"]) && !string.IsNullOrEmpty(id);
            result.Add(new Concept(term, id, codable));
        }
        return result;
    }

    private static bool Flag(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    private static JsonNode? TermsSubset(JsonNode? of, JsonNode? inDocument, JsonNode? failWith)
    {
        var ofConcepts = Concepts(of);
        var documentConcepts = Concepts(inDocument);

        var documentIds = documentConcepts.Where(c => c.Codable).Select(c => c.Id!).ToHashSet(StringComparer.Ordinal);
        var uncovered = ofConcepts
            .Where(c => c.Codable && !documentIds.Contains(c.Id!))
            .GroupBy(c => c.Id!, StringComparer.Ordinal)
            .Select(g => g.First().Term)
            .ToList();
        var untested = ofConcepts.Concat(documentConcepts)
            .Where(c => !c.Codable)
            .Select(c => c.Term)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return CheckResult(
            uncovered.Count == 0,
            Text(failWith),
            uncovered.Count > 0 ? new JsonArray(uncovered.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) : null,
            untested.Count > 0 ? new JsonArray(untested.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) : null,
            null,
            null);
    }

    private static JsonNode? Aggregate(JsonNode? list, JsonNode? min, JsonNode? failWith, bool all)
    {
        int passed = 0, total = 0;
        if (list is JsonArray items)
        {
            foreach (var item in items)
            {
                var judged = item;
                if (item is JsonObject element && element.ContainsKey("state"))
                    judged = Text(element["state"]) == "completed" ? element["value"] : null;
                if (judged is JsonObject result && result["pass"] is JsonValue p
                    && p.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                {
                    total++;
                    if (p.GetValueKind() == JsonValueKind.True)
                        passed++;
                }
            }
        }

        var needed = all ? 0 : (int)Math.Clamp(Math.Ceiling(Num(min) ?? 0), 0, int.MaxValue);
        var pass = all ? total > 0 && passed == total : passed >= needed;
        return CheckResult(pass, Text(failWith), null, null, passed, total);
    }
}
