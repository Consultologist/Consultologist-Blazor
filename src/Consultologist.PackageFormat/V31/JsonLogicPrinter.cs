using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): the one printer of JSON Logic (part 03 § 8, D23) and the parser of
/// the text it writes. <see cref="Print"/> is total: any <see cref="JsonNode"/> prints, and a rule with no
/// fault of the written form (<see cref="JsonLogicShape"/>) prints text that <see cref="TryParse"/> reads
/// back to the same tree. Precedence, tightest first: 7 function forms, literals, paths, arrays and
/// <c>object</c>; 6 <c>not</c> and one-operand <c>-</c>; 5 <c>* / %</c>; 4 <c>+ -</c>; 3 the comparisons and
/// <c>in</c>; 2 <c>and</c>; 1 <c>or</c>; 0 <c>if</c>. A child operation is parenthesised when its level is
/// lower than or equal to its parent's; operands of a function form, elements of an array and fields of an
/// <c>object</c> are printed bare. The text is recorded, so the printer is versioned: any change to its
/// output is a major version.
/// </summary>
public static class JsonLogicPrinter
{
    /// <summary>The printer's version (<c>constants.json</c> <c>builtins.printer</c>).</summary>
    public const string Version = JsonLogicOperations.PrinterVersion;

    private const int Bare = -1;
    private const int Primary = 7;

    private static readonly string[] Words = ["true", "false", "null", "not", "and", "or", "in", "if", "then", "else"];

    private static readonly Dictionary<string, string> Comparisons = new(StringComparer.Ordinal)
    {
        ["==="] = "=", ["!=="] = "≠", ["<"] = "<", ["<="] = "≤", [">"] = ">", [">="] = "≥", ["in"] = "in",
    };

    /// <summary>The text of <paramref name="rule"/> (<c>null</c> is the JSON value <c>null</c>). Never throws.</summary>
    public static string Print(JsonNode? rule) => Write(rule, Bare, 1);

    /// <summary>
    /// S03-001's shape: <c>skipped: input.kind.value = "table" is false</c>, from the <paramref name="state"/>,
    /// the printed <paramref name="rule"/> and its <paramref name="result"/>.
    /// </summary>
    public static string Reason(string state, JsonNode? rule, bool result) =>
        $"{state}: {Print(rule)} is {(result ? "true" : "false")}";

    // ---- printing ----

    private enum Kind { Literal, Path, Infix, Not, Negate, If, Object, Missing, Call }

    private readonly record struct Form(Kind Kind, int Level, string Name, string Symbol, JsonNode? Operands);

    private static Form Classify(JsonNode? node)
    {
        if (node is not JsonObject obj || obj.Count != 1)
            return new Form(Kind.Literal, Primary, "", "", null);

        var (name, operands) = obj.First();
        var call = new Form(Kind.Call, Primary, name, "", operands);
        var count = (operands as JsonArray)?.Count ?? -1;

        if (Comparisons.TryGetValue(name, out var symbol))
            return count == 2 ? new Form(Kind.Infix, 3, name, symbol, operands) : call;

        switch (name)
        {
            case "and": return count >= 2 ? new Form(Kind.Infix, 2, name, "and", operands) : call;
            case "or": return count >= 2 ? new Form(Kind.Infix, 1, name, "or", operands) : call;
            case "+": return count >= 2 ? new Form(Kind.Infix, 4, name, "+", operands) : call;
            case "*": return count >= 2 ? new Form(Kind.Infix, 5, name, "*", operands) : call;
            case "/": return count == 2 ? new Form(Kind.Infix, 5, name, "/", operands) : call;
            case "%": return count == 2 ? new Form(Kind.Infix, 5, name, "%", operands) : call;
            case "-":
                return count == 2 ? new Form(Kind.Infix, 4, name, "-", operands)
                    : count == 1 ? new Form(Kind.Negate, 6, name, "-", operands)
                    : call;
            case "!": return count == 1 ? new Form(Kind.Not, 6, name, "not", operands) : call;
            case "if": return count >= 3 && count % 2 == 1 ? new Form(Kind.If, 0, name, "", operands) : call;
            case "object": return operands is JsonObject ? new Form(Kind.Object, Primary, name, "", operands) : call;
            case "missing": return count >= 0 ? new Form(Kind.Missing, Primary, name, "", operands) : call;
            case "var":
                return JsonLogicShape.TextOf(operands) is { } bare && IsBarePath(bare)
                    ? new Form(Kind.Path, Primary, name, "", operands)
                    : call;
            default: return call;
        }
    }

    private static string Write(JsonNode? node, int parent, int depth)
    {
        if (node is JsonObject or JsonArray && depth > JsonLogicShape.MaxDepth)
            return "…";

        if (node is JsonArray array)
            return "[" + string.Join(", ", array.Select(e => Write(e, Bare, depth + 1))) + "]";

        if (node is not JsonObject obj)
            return CanonicalJson.Write(node);

        if (obj.Count != 1)
            return CanonicalJson.Write(obj);

        var form = Classify(obj);
        var text = Body(form, depth);
        return form.Level <= parent ? "(" + text + ")" : text;
    }

    private static string Body(Form form, int depth)
    {
        var operands = form.Operands;
        switch (form.Kind)
        {
            case Kind.Path:
                return JsonLogicShape.TextOf(operands)!;
            case Kind.Infix:
            {
                var list = (JsonArray)operands!;
                return string.Join($" {form.Symbol} ", list.Select(e => Write(e, form.Level, depth + 1)));
            }
            case Kind.Not:
                return "not " + Write(((JsonArray)operands!)[0], form.Level, depth + 1);
            case Kind.Negate:
            {
                var operand = ((JsonArray)operands!)[0];
                return operand is JsonValue value && value.GetValueKind() == JsonValueKind.Number
                    ? "-(" + CanonicalJson.Write(operand) + ")"
                    : "-" + Write(operand, form.Level, depth + 1);
            }
            case Kind.If:
            {
                var list = (JsonArray)operands!;
                var text = new StringBuilder();
                for (var i = 0; i + 1 < list.Count; i += 2)
                {
                    text.Append(i == 0 ? "if " : " else if ")
                        .Append(Write(list[i], 0, depth + 1)).Append(" then ")
                        .Append(Write(list[i + 1], 0, depth + 1));
                }

                return text.Append(" else ").Append(Write(list[^1], 0, depth + 1)).ToString();
            }
            case Kind.Object:
            {
                var fields = (JsonObject)operands!;
                if (fields.Count == 0)
                    return "{}";
                var parts = fields.Select(f => FieldName(f.Key) + ": " + Write(f.Value, Bare, depth + 1));
                return "{ " + string.Join(", ", parts) + " }";
            }
            case Kind.Missing:
                // the list of paths (always a list here) is no level of its own, as the shape counts
                return "missing(" + Write(operands, Bare, depth) + ")";
            default:
            {
                var args = operands is JsonArray list
                    ? list.Select(e => Write(e, Bare, depth + 1))
                    : [Write(operands, Bare, depth + 1)];
                return form.Name + "(" + string.Join(", ", args) + ")";
            }
        }
    }

    private static string FieldName(string name) =>
        IsIdentifier(name) ? name : CanonicalJson.Write(JsonValue.Create(name));

    private static bool IsIdentifier(string text) =>
        text.Length > 0 && IsLetter(text[0]) && text.All(c => IsLetter(c) || IsDigit(c));

    private static bool IsLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>A path the printer writes bare: an identifier, then dot-joined name or digit segments; no first segment a printer word.</summary>
    private static bool IsBarePath(string path)
    {
        var segments = path.Split('.');
        return IsIdentifier(segments[0])
            && Array.IndexOf(Words, segments[0]) < 0
            && segments.Skip(1).All(s => s.Length > 0 && s.All(c => IsLetter(c) || IsDigit(c)));
    }

    // ---- parsing ----

    /// <summary>
    /// Reads text the printer writes. A chain of one infix operator is one operation; mixed operators of one
    /// level without parentheses, or any text <see cref="Print"/> would not write for the tree read (other
    /// spacing, needless parentheses, <c>1.0</c>), is <c>false</c>. Never throws. A whole number that fits a
    /// <c>long</c> becomes a <c>long</c> node, any other number a <c>double</c> node.
    /// </summary>
    public static bool TryParse(string text, out JsonNode? rule)
    {
        rule = null;
        if (text == null)
            return false;

        try
        {
            var parser = new Parser(Tokenize(text));
            var parsed = parser.ParseAll();
            if (!string.Equals(Print(parsed), text, StringComparison.Ordinal))
                return false;
            rule = parsed;
            return true;
        }
        catch (Exception)
        {
            // Never throws, whatever the text: anything unexpected is "the printer did not write this".
            return false;
        }
    }

    private sealed class ParseFailure : Exception;

    private enum T { Number, String, Word, Symbol, End }

    private readonly record struct Token(T Type, string Text, JsonNode? Value = null);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '"')
            {
                var end = i + 1;
                while (end < text.Length && text[end] != '"')
                    end += text[end] == '\\' ? 2 : 1;
                if (end >= text.Length)
                    throw new ParseFailure();
                string? value;
                try
                {
                    value = JsonSerializer.Deserialize<string>(text.AsSpan(i, end - i + 1));
                }
                catch (JsonException)
                {
                    throw new ParseFailure();
                }

                tokens.Add(new Token(T.String, value ?? "", JsonValue.Create(value)));
                i = end + 1;
            }
            else if (IsDigit(c))
            {
                var start = i;
                while (i < text.Length && IsDigit(text[i]))
                    i++;
                var whole = true;
                if (i + 1 < text.Length && text[i] == '.' && IsDigit(text[i + 1]))
                {
                    whole = false;
                    i++;
                    while (i < text.Length && IsDigit(text[i]))
                        i++;
                }

                if (i < text.Length && text[i] is 'e' or 'E')
                {
                    var j = i + 1;
                    if (j < text.Length && text[j] is '+' or '-')
                        j++;
                    if (j >= text.Length || !IsDigit(text[j]))
                        throw new ParseFailure();
                    whole = false;
                    while (j < text.Length && IsDigit(text[j]))
                        j++;
                    i = j;
                }

                var literal = text[start..i];
                tokens.Add(new Token(T.Number, literal, Number(literal, whole)));
            }
            else if (IsLetter(c))
            {
                var start = i;
                while (i < text.Length && (IsLetter(text[i]) || IsDigit(text[i])
                    || (text[i] == '.' && i + 1 < text.Length && (IsLetter(text[i + 1]) || IsDigit(text[i + 1])))))
                    i++;
                tokens.Add(new Token(T.Word, text[start..i]));
            }
            else if ("()[]{},:=≠<>≤≥+-*/%".Contains(c))
            {
                tokens.Add(new Token(T.Symbol, c.ToString()));
                i++;
            }
            else
            {
                throw new ParseFailure();
            }
        }

        tokens.Add(new Token(T.End, ""));
        return tokens;
    }

    private static JsonNode Number(string literal, bool whole)
    {
        if (whole && long.TryParse(literal, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return JsonValue.Create(n);
        var d = double.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(d))
            throw new ParseFailure();
        return JsonValue.Create(d);
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _at;
        // Expressions nested in parentheses, lists, arguments and fields. The shape admits containers MaxDepth deep; a
        // literal inside the deepest one is one Expression more, and the path inside a missing's list two (the list
        // itself is one), so the bound admits every rule the shape does. Text that parses deeper prints "…" past the
        // limit, so TryParse still refuses it; the bound only keeps the recursion short.
        private const int MaxParseDepth = JsonLogicShape.MaxDepth + 2;

        private int _depth;

        private Token Peek => tokens[_at];

        private bool IsSymbol(string s) => Peek.Type == T.Symbol && Peek.Text == s;

        private bool IsWord(string s) => Peek.Type == T.Word && Peek.Text == s;

        private void Expect(string symbol)
        {
            if (!IsSymbol(symbol))
                throw new ParseFailure();
            _at++;
        }

        private void ExpectWord(string word)
        {
            if (!IsWord(word))
                throw new ParseFailure();
            _at++;
        }

        public JsonNode? ParseAll()
        {
            var node = Expression();
            if (Peek.Type != T.End)
                throw new ParseFailure();
            return node;
        }

        /// <summary>Level 0: an <c>if</c>, or anything tighter.</summary>
        private JsonNode? Expression()
        {
            if (++_depth > MaxParseDepth)
                throw new ParseFailure();
            JsonNode? result;
            if (IsWord("if"))
            {
                var operands = new JsonArray();
                _at++;
                while (true)
                {
                    operands.Add(Level(1));
                    ExpectWord("then");
                    operands.Add(Level(1));
                    ExpectWord("else");
                    if (IsWord("if"))
                    {
                        _at++;
                        continue;
                    }

                    operands.Add(Level(1));
                    break;
                }

                result = new JsonObject { ["if"] = operands };
            }
            else
            {
                result = Level(1);
            }

            _depth--;
            return result;
        }

        private JsonNode? Level(int level) => level switch
        {
            1 => Chain(2, "or"),
            2 => Chain(3, "and"),
            3 => Comparison(),
            4 => Additive(),
            5 => Multiplicative(),
            _ => Unary(),
        };

        private JsonNode? Chain(int tighter, string word)
        {
            var first = Level(tighter);
            if (!IsWord(word))
                return first;
            var operands = new JsonArray { first };
            while (IsWord(word))
            {
                _at++;
                operands.Add(Level(tighter));
            }

            return new JsonObject { [word] = operands };
        }

        private JsonNode? Comparison()
        {
            var left = Level(4);
            string? name = null;
            if (Peek.Type == T.Symbol)
            {
                name = Peek.Text switch
                {
                    "=" => "===", "≠" => "!==", "<" => "<", ">" => ">", "≤" => "<=", "≥" => ">=", _ => null,
                };
            }
            else if (IsWord("in"))
            {
                name = "in";
            }

            if (name == null)
                return left;
            _at++;
            return new JsonObject { [name] = new JsonArray { left, Level(4) } };
        }

        private JsonNode? Additive()
        {
            var first = Level(5);
            if (IsSymbol("+"))
            {
                var operands = new JsonArray { first };
                while (IsSymbol("+"))
                {
                    _at++;
                    operands.Add(Level(5));
                }

                return new JsonObject { ["+"] = operands };
            }

            if (IsSymbol("-"))
            {
                _at++;
                return new JsonObject { ["-"] = new JsonArray { first, Level(5) } };
            }

            return first;
        }

        private JsonNode? Multiplicative()
        {
            var first = Level(6);
            if (IsSymbol("*"))
            {
                var operands = new JsonArray { first };
                while (IsSymbol("*"))
                {
                    _at++;
                    operands.Add(Level(6));
                }

                return new JsonObject { ["*"] = operands };
            }

            foreach (var op in new[] { "/", "%" })
            {
                if (IsSymbol(op))
                {
                    _at++;
                    return new JsonObject { [op] = new JsonArray { first, Level(6) } };
                }
            }

            return first;
        }

        /// <summary>Level 6: <c>not</c> and one-operand <c>-</c> take one tighter operand; <c>-</c> before a number is a negative literal.</summary>
        private JsonNode? Unary()
        {
            if (IsWord("not"))
            {
                _at++;
                return new JsonObject { ["!"] = new JsonArray { Operand() } };
            }

            if (IsSymbol("-") && tokens[_at + 1].Type != T.Number)
            {
                _at++;
                return new JsonObject { ["-"] = new JsonArray { Atom() } };
            }

            return Operand();
        }

        private JsonNode? Operand()
        {
            if (IsSymbol("-") && tokens[_at + 1].Type == T.Number)
            {
                var number = tokens[_at + 1];
                _at += 2;
                return Negative(number);
            }

            return Atom();
        }

        private static JsonNode Negative(Token number) =>
            Number("-" + number.Text, !number.Text.Contains('.') && !number.Text.Contains('e') && !number.Text.Contains('E'));

        private JsonNode? Atom()
        {
            var token = Peek;
            switch (token.Type)
            {
                case T.Number:
                case T.String:
                    _at++;
                    return token.Value;
                case T.Word:
                    return Word();
                case T.Symbol when token.Text == "(":
                    _at++;
                    var inner = Expression();
                    Expect(")");
                    return inner;
                case T.Symbol when token.Text == "[":
                    _at++;
                    var items = new JsonArray();
                    foreach (var item in List("]"))
                        items.Add(item);
                    return items;
                case T.Symbol when token.Text == "{":
                    return ObjectForm();
                default:
                    throw new ParseFailure();
            }
        }

        /// <summary>Expressions separated by commas, up to and including <paramref name="close"/>; the opener is already read.</summary>
        private List<JsonNode?> List(string close)
        {
            var items = new List<JsonNode?>();
            if (IsSymbol(close))
            {
                _at++;
                return items;
            }

            while (true)
            {
                items.Add(Expression());
                if (IsSymbol(","))
                {
                    _at++;
                    continue;
                }

                Expect(close);
                return items;
            }
        }

        private JsonNode? Word()
        {
            var text = Peek.Text;
            _at++;
            var dotted = text.Contains('.');
            if (!dotted && IsSymbol("("))
                return Call(text);
            if (Array.IndexOf(Words, text.Split('.')[0]) >= 0)
            {
                if (dotted)
                    throw new ParseFailure();
                return text switch
                {
                    "true" => JsonValue.Create(true),
                    "false" => JsonValue.Create(false),
                    "null" => null,
                    _ => throw new ParseFailure(),
                };
            }

            return new JsonObject { ["var"] = text };
        }

        private JsonNode? Call(string name)
        {
            if (Array.IndexOf(Words, name) >= 0)
                throw new ParseFailure();
            _at++;
            var args = List(")");
            if (name == "var")
            {
                if (args is [JsonValue { } path] && JsonLogicShape.TextOf(path) != null)
                    return new JsonObject { ["var"] = path };
                throw new ParseFailure();
            }

            if (name == "missing" && args is [JsonArray paths])
                return new JsonObject { ["missing"] = paths };

            var operands = new JsonArray();
            foreach (var arg in args)
                operands.Add(arg);
            return new JsonObject { [name] = operands };
        }

        private JsonNode? ObjectForm()
        {
            _at++;
            var fields = new JsonObject();
            if (IsSymbol("}"))
            {
                _at++;
                return new JsonObject { ["object"] = fields };
            }

            while (true)
            {
                var key = Peek;
                if (key.Type == T.Word && !key.Text.Contains('.'))
                    _at++;
                else if (key.Type == T.String)
                    _at++;
                else
                    throw new ParseFailure();
                Expect(":");
                var value = Expression();
                if (fields.ContainsKey(key.Text))
                    throw new ParseFailure();
                fields[key.Text] = value;
                if (IsSymbol(","))
                {
                    _at++;
                    continue;
                }

                Expect("}");
                return new JsonObject { ["object"] = fields };
            }
        }
    }
}
