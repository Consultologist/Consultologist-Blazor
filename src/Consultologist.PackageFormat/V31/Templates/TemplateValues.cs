using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Functions;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.2 to 2.4; D13): the values a composition reads and writes, and the seven
/// built-ins. Data is the run's JSON as Scriban sees it: an object is a <see cref="DataObject"/> (a member it does not hold
/// fails the render: strict variables, part 05 § 2.3), a list a <see cref="ScriptArray"/>, a number a <c>decimal</c> (else
/// a <c>double</c>), text a <c>string</c>. A value is written (<see cref="Written"/>) as D13 says: <c>null</c> as nothing,
/// a boolean <c>true</c>/<c>false</c>, a number in canonical JSON's invariant form, text as it is (a date is ISO text);
/// an object or a list fails. A built-in given <c>null</c> gives <c>null</c>; <c>array.join</c> over a list holding
/// <c>null</c> fails with A07-006. Nothing here parses a value as a template.
/// </summary>
internal static class TemplateValues
{
    /// <summary>The deepest JSON nesting turned into Scriban values; deeper reads <c>null</c> (a document publish wrote is far shallower).</summary>
    public const int MaxDepth = 256;

    /// <summary><paramref name="node"/> as Scriban reads it.</summary>
    public static object? FromJson(JsonNode? node, int depth = 0)
    {
        if (depth > MaxDepth)
            return null;
        switch (node)
        {
            case JsonObject obj:
                var data = new DataObject(obj);
                foreach (var (key, value) in obj)
                    data.SetValue(key, FromJson(value, depth + 1), false);
                return data;
            case JsonArray array:
                return new ScriptArray(array.Select(item => FromJson(item, depth + 1)));
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        return value.GetValue<string>();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Number:
                        var written = value.ToJsonString();
                        if (decimal.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                            return number;
                        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var wide) ? wide : null;
                    default:
                        return null;
                }

            default:
                return null;
        }
    }

    /// <summary>A Scriban value back as JSON (for its hash): the inverse of <see cref="FromJson"/>; <c>null</c> for anything else.</summary>
    public static JsonNode? ToJson(object? value, int depth = 0)
    {
        if (depth > MaxDepth)
            return null;
        return value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            decimal number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            DataObject data => data.Json.DeepClone(),
            BodyComposer.FolderItemObject item => item.Data.Json.DeepClone(),
            ScriptArray list => new JsonArray(list.Select(item => ToJson(item, depth + 1)).ToArray()),
            ScriptObject other => new JsonObject(other.Select(p => KeyValuePair.Create(p.Key, ToJson(p.Value, depth + 1)))),
            _ => null,
        };
    }

    /// <summary>D13: the text <paramref name="value"/> writes, or <c>null</c> when it is an object, a list or anything else no output writes.</summary>
    public static string? Written(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        decimal number => CanonicalJson.Write(JsonValue.Create(number)),
        double number => CanonicalJson.Write(JsonValue.Create(number)),
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>The seven built-ins (part 05 § 2.2), and nothing else, as the context's built-in object.</summary>
    public static ScriptObject BuiltIns()
    {
        var builtins = new ScriptObject();
        var text = new ScriptObject
        {
            ["upcase"] = new BuiltIn(1, (_, a) => Text(a[0]) is { } s ? s.ToUpperInvariant() : null),
            ["downcase"] = new BuiltIn(1, (_, a) => Text(a[0]) is { } s ? s.ToLowerInvariant() : null),
            ["capitalize"] = new BuiltIn(1, (_, a) => Text(a[0]) is { } s ? Capitalize(s) : null),
            ["strip"] = new BuiltIn(1, (_, a) => Text(a[0])?.Trim()),
        };
        var array = new ScriptObject
        {
            ["join"] = new BuiltIn(2, (context, a) => Join(context, a[0], a[1])),
            ["size"] = new BuiltIn(1, (_, a) => a[0] switch
            {
                null => null,
                ScriptArray list => list.Count,
                _ => throw new ComposeStop(null),
            }),
        };
        var date = new ScriptObject
        {
            ["to_string"] = new BuiltIn(2, (_, a) => a[0] is null ? null : DateText(a[0], a[1])),
        };
        builtins.SetValue("string", text, true);
        builtins.SetValue("array", array, true);
        builtins.SetValue("date", date, true);
        return builtins;
    }

    /// <summary>Text, or <c>null</c> for <c>null</c>; anything else is no text a built-in takes.</summary>
    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        _ => throw new ComposeStop(null),
    };

    /// <summary>The first character upper case in the invariant culture, the rest as it is (a surrogate pair is one character).</summary>
    private static string Capitalize(string text)
    {
        if (text.Length == 0 || !Rune.TryGetRuneAt(text, 0, out var first))
            return text;
        return Rune.ToUpperInvariant(first).ToString() + text[first.Utf16SequenceLength..];
    }

    /// <summary><c>array.join</c>: a <c>null</c> list gives <c>null</c>; a <c>null</c> element fails the node (A07-006).</summary>
    private static object? Join(TemplateContext context, object? list, object? separator)
    {
        if (list is null)
            return null;
        if (list is not ScriptArray items || separator is not string between)
            throw new ComposeStop(null);
        var texts = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (item is null)
                throw context is ComposeContext composing ? composing.Stopped(Run.RunSentences.JoinMetNull(composing.Subject)) : new ComposeStop(null);
            texts.Add(Written(item) ?? throw new ComposeStop(null));
        }

        return string.Join(between, texts);
    }

    /// <summary><c>date.to_string</c>: a date (<c>yyyy-MM-dd</c>, or an ISO date-time) formatted by Scriban's <c>%</c> pattern in the invariant culture.</summary>
    private static string DateText(object? value, object? pattern)
    {
        if (value is not string text || pattern is not string format)
            throw new ComposeStop(null);
        DateTime date;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            && !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date))
        {
            throw new ComposeStop(null);
        }

        return new DateTimeFunctions().ToString(date, format, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.3): a JSON object as a body reads it. A member it does not hold fails the
/// render (strict variables: "a render that meets one fails"), and its JSON is kept for a renderer step's input hash.
/// </summary>
internal sealed class DataObject(JsonObject json) : ScriptObject
{
    /// <summary>The object as the run gave it. Read-only.</summary>
    public JsonObject Json { get; } = json;

    public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
    {
        if (base.TryGetValue(context, span, member, out value))
            return true;
        throw new ComposeStop(null);
    }
}

/// <summary>
/// #1504 (specVersion 31, area 07): why a composition stopped, thrown from inside the render and caught by
/// <see cref="BodyComposer"/>: <see cref="Sentence"/> the node's failure, or <c>null</c> for A06-008 (the render failed
/// otherwise); <see cref="TooManySteps"/> past the step budget. Never carries a value.
/// </summary>
internal sealed class ComposeStop(string? sentence, bool tooManySteps = false) : Exception(sentence ?? "The composition stopped.")
{
    public string? Sentence { get; } = sentence;

    public bool TooManySteps { get; } = tooManySteps;
}

/// <summary>
/// #1504 (specVersion 31, area 07): a function a body pipes into: a built-in, or a renderer step
/// (<see cref="BodyComposer"/>). <paramref name="arity"/> is how many values it takes, the piped value first; any other
/// count fails the render.
/// </summary>
internal sealed class BuiltIn(int arity, Func<TemplateContext, ScriptArray, object?> body) : IScriptCustomFunction
{
    public int RequiredParameterCount => arity;

    public int ParameterCount => arity;

    public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;

    public Type ReturnType => typeof(object);

    public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), index == 0 ? "value" : "argument");

    public object? Invoke(TemplateContext context, ScriptNode? callerContext, ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        if (arguments.Count != arity)
            throw new ComposeStop(null);
        return body(context, arguments);
    }

    public ValueTask<object?> InvokeAsync(TemplateContext context, ScriptNode? callerContext, ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));
}
