using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1501 (specVersion 31, area 04): why a <c>matches</c> pattern is refused, in the order D10 checks.</summary>
public enum IRegexpFault
{
    /// <summary>The pattern is I-Regexp, within the bounds, and compiles.</summary>
    None,

    /// <summary>Longer than 200 code points (S03-032).</summary>
    TooLong,

    /// <summary>Not RFC 9485's grammar (S03-031).</summary>
    NotIRegexp,

    /// <summary>A counted repetition with a number over 100 (S03-042).</summary>
    Repeats,

    /// <summary>.NET's non-backtracking engine refuses the translation (S03-043).</summary>
    DoesNotCompile,
}

/// <summary>
/// #1501 (specVersion 31, area 04): what <see cref="IRegexp.Check"/> found. <paramref name="Reason"/>
/// is S03-031's or S03-043's <c>&lt;reason&gt;</c> (empty otherwise); <paramref name="Count"/> is the
/// pattern's code points for <see cref="IRegexpFault.TooLong"/> and the number over the limit for
/// <see cref="IRegexpFault.Repeats"/> (0 otherwise); <paramref name="Regex"/> is set exactly when
/// <paramref name="Fault"/> is <see cref="IRegexpFault.None"/>.
/// </summary>
public sealed record IRegexpCheck(IRegexpFault Fault, string Reason, int Count, Regex? Regex);

/// <summary>
/// #1501 (specVersion 31, area 04): the pattern of <c>matches</c> (part 03 § 5.3). A pattern is
/// checked against RFC 9485 § 4's grammar (transcribed by hand, over code points, D10), held to the
/// bounds, translated to .NET (D11) and compiled with the engine's exact options. The translation
/// matches whole code points: <c>.</c>, a class and its negation take a surrogate pair as one
/// character, and a literal outside the Basic Multilingual Plane is one atom. Unicode categories
/// are judged on Basic Multilingual Plane code points only (.NET tests them per UTF-16 unit): a code
/// point outside the plane is in no <c>\p{…}</c> and in every <c>\P{…}</c>.
/// </summary>
public static class IRegexp
{
    /// <summary>The longest pattern, in code points (<c>constants.json</c> <c>limits.matchesPatternCodePoints</c>).</summary>
    public const int MaxCodePoints = 200;

    /// <summary>The largest number of a counted repetition (<c>limits.matchesRepetition</c>).</summary>
    public const int MaxRepetition = 100;

    /// <summary>The longest text searched, in bytes of UTF-8 (<c>limits.matchesTextBytes</c>, D12).</summary>
    public const int MaxTextBytes = 1048576;

    /// <summary>The engine's options (part 03 § 5.3); no time-out, so a replay decides alike.</summary>
    private const RegexOptions Options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    private const int LastBmp = 0xFFFF;
    private const int FirstAstral = 0x10000;
    private const int LastCodePoint = 0x10FFFF;

    /// <summary>A .NET class item for every surrogate unit, so that no class matches half a pair.</summary>
    private const string Surrogates = "\\uD800-\\uDFFF";

    /// <summary>Any one character outside the Basic Multilingual Plane: a high surrogate, then a low one.</summary>
    private const string AnyAstral = "[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]";

    /// <summary>The categories of RFC 9485's <c>charProp</c>.</summary>
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "L", "Ll", "Lm", "Lo", "Lt", "Lu",
        "M", "Mc", "Me", "Mn",
        "N", "Nd", "Nl", "No",
        "P", "Pc", "Pd", "Pe", "Pf", "Pi", "Po", "Ps",
        "Z", "Zl", "Zp", "Zs",
        "S", "Sc", "Sk", "Sm", "So",
        "C", "Cc", "Cf", "Cn", "Co",
    };

    private static readonly ConcurrentDictionary<string, IRegexpCheck> Checked = new(StringComparer.Ordinal);

    /// <summary>
    /// Checks <paramref name="pattern"/> in D10's order (length, grammar, repetition, compile) and stops
    /// at the first fault. The result is cached by pattern. Never throws; a <c>null</c> pattern is the
    /// empty one.
    /// </summary>
    public static IRegexpCheck Check(string pattern) => Checked.GetOrAdd(pattern ?? string.Empty, Run);

    /// <summary>
    /// The .NET pattern <paramref name="pattern"/> translates to (D11), or <c>null</c> when it is longer
    /// than 200 code points or not I-Regexp. A repetition over the limit is still translated.
    /// </summary>
    public static string? ToDotNet(string pattern)
    {
        var parsed = Parse(pattern ?? string.Empty);
        return parsed.Tree is { } tree ? Write(tree) : null;
    }

    /// <summary>Whether <paramref name="text"/> is over 1 MiB of UTF-8 (S03-040), so <c>matches</c> does not search it.</summary>
    public static bool TooLong(string text) =>
        text != null && Encoding.UTF8.GetByteCount(text) > MaxTextBytes;

    /// <summary>Whether <paramref name="regex"/> occurs anywhere in <paramref name="text"/> (search, not whole-string match).</summary>
    public static bool IsMatch(Regex regex, string text)
    {
        ArgumentNullException.ThrowIfNull(regex);
        return regex.IsMatch(text ?? string.Empty);
    }

    private static IRegexpCheck Run(string pattern)
    {
        var parsed = Parse(pattern);
        if (parsed.Fault != IRegexpFault.None)
        {
            return new IRegexpCheck(parsed.Fault, parsed.Reason, parsed.Count, null);
        }

        try
        {
            return new IRegexpCheck(IRegexpFault.None, "", 0, new Regex(Write(parsed.Tree!), Options, Regex.InfiniteMatchTimeout));
        }
        catch (NotSupportedException)
        {
            // The non-backtracking engine's refusal of a pattern whose automaton is over its size (D12).
            return new IRegexpCheck(IRegexpFault.DoesNotCompile, "it needs too large an automaton", 0, null);
        }
        catch (Exception)
        {
            // Never .NET's own message: it is localised (D12).
            return new IRegexpCheck(IRegexpFault.DoesNotCompile, ".NET does not accept it", 0, null);
        }
    }

    // ---- The tree -------------------------------------------------------------------------------

    private abstract record Node;

    /// <summary>Branches joined by <c>|</c>; a group is one of these in parentheses.</summary>
    private sealed record Alternation(IReadOnlyList<Sequence> Branches) : Node;

    private sealed record Sequence(IReadOnlyList<Node> Pieces) : Node;

    /// <summary>An atom with a quantifier; <paramref name="Max"/> <c>null</c> is no upper bound.</summary>
    private sealed record Repeat(Node Atom, int Min, int? Max) : Node;

    private sealed record Literal(int CodePoint) : Node;

    /// <summary><c>.</c>: any code point but <c>\n</c> and <c>\r</c>.</summary>
    private sealed record AnyChar : Node;

    /// <summary><c>\p{Name}</c>, or <c>\P{Name}</c> when <paramref name="Negated"/>.</summary>
    private sealed record Category(string Name, bool Negated) : Node;

    /// <summary>A bracketed class: code-point ranges (inclusive) and categories, negated by <c>^</c> or not.</summary>
    private sealed record CharClass(bool Negated, IReadOnlyList<(int Lo, int Hi)> Ranges, IReadOnlyList<Category> Categories) : Node;

    private sealed record Parsed(IRegexpFault Fault, string Reason, int Count, Node? Tree);

    // ---- The grammar (RFC 9485 § 4) -------------------------------------------------------------

    /// <summary>A fault of the grammar, with S03-031's reason; caught in <see cref="Parse"/>, never escapes.</summary>
    private sealed class GrammarFault(string reason) : Exception(reason);

    private static Parsed Parse(string pattern)
    {
        var codePoints = CodePoints(pattern);
        if (codePoints.Count > MaxCodePoints)
        {
            return new Parsed(IRegexpFault.TooLong, "", codePoints.Count, null);
        }

        var parser = new Parser(codePoints);
        Node tree;
        try
        {
            tree = parser.ParsePattern();
        }
        catch (GrammarFault fault)
        {
            return new Parsed(IRegexpFault.NotIRegexp, fault.Message, 0, null);
        }

        return parser.OverLimit is { } over
            ? new Parsed(IRegexpFault.Repeats, "", over, tree)
            : new Parsed(IRegexpFault.None, "", 0, tree);
    }

    /// <summary>The pattern's code points; a lone surrogate is one code point of its own (and never in the grammar).</summary>
    private static List<int> CodePoints(string text)
    {
        var codePoints = new List<int>(text.Length);
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var used) == System.Buffers.OperationStatus.Done)
            {
                codePoints.Add(rune.Value);
                rest = rest[used..];
            }
            else
            {
                codePoints.Add(rest[0]);
                rest = rest[1..];
            }
        }

        return codePoints;
    }

    /// <summary>
    /// A recursive-descent parser over the code points. Nesting is bounded by the 200 code points, so
    /// the recursion is too. Positions in reasons are 1-based code points, at the start of the
    /// offending construct.
    /// </summary>
    private sealed class Parser(List<int> text)
    {
        private int _at;

        /// <summary>The first number of a counted repetition over 100, if any (S03-042, checked after the grammar).</summary>
        public int? OverLimit { get; private set; }

        private bool AtEnd => _at >= text.Count;

        private int Current => text[_at];

        public Node ParsePattern()
        {
            var tree = ParseAlternation();
            if (!AtEnd)
            {
                // Only a ')' with no group open stops an alternation before the end.
                throw NotAllowed(_at);
            }

            return tree;
        }

        private Alternation ParseAlternation()
        {
            var branches = new List<Sequence> { ParseBranch() };
            while (!AtEnd && Current == '|')
            {
                _at++;
                branches.Add(ParseBranch());
            }

            return new Alternation(branches);
        }

        private Sequence ParseBranch()
        {
            var pieces = new List<Node>();
            while (!AtEnd && Current != '|' && Current != ')')
            {
                pieces.Add(ParsePiece());
            }

            return new Sequence(pieces);
        }

        private Node ParsePiece()
        {
            var atom = ParseAtom();
            if (AtEnd)
            {
                return atom;
            }

            switch (Current)
            {
                case '*':
                    _at++;
                    return new Repeat(atom, 0, null);
                case '+':
                    _at++;
                    return new Repeat(atom, 1, null);
                case '?':
                    _at++;
                    return new Repeat(atom, 0, 1);
                case '{':
                    return ParseCount(atom);
                default:
                    return atom;
            }
        }

        private Node ParseAtom()
        {
            var c = Current;
            switch (c)
            {
                case '(':
                    _at++;
                    var group = ParseAlternation();
                    if (AtEnd)
                    {
                        throw new GrammarFault("the pattern ends inside a group");
                    }

                    _at++; // ')'
                    return group;
                case '[':
                    return ParseClass();
                case '\\':
                    return ParseEscape();
                case '.':
                    _at++;
                    return new AnyChar();
                default:
                    if (!IsNormalChar(c))
                    {
                        throw NotAllowed(_at);
                    }

                    _at++;
                    return new Literal(c);
            }
        }

        /// <summary><c>{n}</c>, <c>{n,}</c>, <c>{n,m}</c>; <see cref="_at"/> is on the <c>{</c>.</summary>
        private Repeat ParseCount(Node atom)
        {
            var start = _at;
            _at++;
            var min = ParseNumber();
            BigInteger? max = min;
            if (AtEnd)
            {
                throw new GrammarFault("the pattern ends inside a repetition");
            }

            if (Current == ',')
            {
                _at++;
                if (AtEnd)
                {
                    throw new GrammarFault("the pattern ends inside a repetition");
                }

                max = IsDigit(Current) ? ParseNumber() : null;
                if (AtEnd)
                {
                    throw new GrammarFault("the pattern ends inside a repetition");
                }
            }

            if (Current != '}')
            {
                throw NotAllowed(_at);
            }

            _at++;
            if (max is { } upper && upper < min)
            {
                throw new GrammarFault($"the repetition at character {Position(start)} has its numbers backwards");
            }

            Note(min);
            if (max is { } bound)
            {
                Note(bound);
            }

            return new Repeat(atom, Small(min), max is { } m ? Small(m) : null);
        }

        /// <summary><c>1*DIGIT</c>, as a number of any size (a pattern may write 198 digits).</summary>
        private BigInteger ParseNumber()
        {
            if (AtEnd)
            {
                throw new GrammarFault("the pattern ends inside a repetition");
            }

            if (!IsDigit(Current))
            {
                throw NotAllowed(_at);
            }

            var number = BigInteger.Zero;
            while (!AtEnd && IsDigit(Current))
            {
                number = number * 10 + (Current - '0');
                _at++;
            }

            return number;
        }

        private void Note(BigInteger number)
        {
            if (OverLimit == null && number > MaxRepetition)
            {
                OverLimit = Small(number);
            }
        }

        /// <summary>A class, <c>[…]</c> or <c>[^…]</c>; <see cref="_at"/> is on the <c>[</c>.</summary>
        private CharClass ParseClass()
        {
            _at++;
            var negated = false;
            if (!AtEnd && Current == '^')
            {
                negated = true;
                _at++;
            }

            var ranges = new List<(int, int)>();
            var categories = new List<Category>();
            var first = true;
            while (true)
            {
                if (AtEnd)
                {
                    throw new GrammarFault("the pattern ends inside a class");
                }

                if (Current == ']' && !first)
                {
                    _at++;
                    return new CharClass(negated, ranges, categories);
                }

                if (Current == '-')
                {
                    // A '-' is a character of the class only first or last.
                    if (first || (_at + 1 < text.Count && text[_at + 1] == ']'))
                    {
                        ranges.Add(('-', '-'));
                        _at++;
                        first = false;
                        continue;
                    }

                    if (_at + 1 >= text.Count)
                    {
                        throw new GrammarFault("the pattern ends inside a class");
                    }

                    throw NotAllowed(_at);
                }

                first = false;
                var start = _at;
                var item = ParseClassChar();
                if (item is Category category)
                {
                    categories.Add(category);
                    continue;
                }

                var lo = ((Literal)item).CodePoint;
                if (AtEnd || Current != '-' || _at + 1 >= text.Count || text[_at + 1] == ']')
                {
                    // A single character; a '-' that follows is the class's last, or the class is unended.
                    ranges.Add((lo, lo));
                    continue;
                }

                var dash = _at;
                _at++;
                if (text[_at] == '-' || IsCategoryEscape(_at))
                {
                    // A range ends with a character: neither a second '-' nor a category.
                    throw NotAllowed(dash);
                }

                var hi = ((Literal)ParseClassChar()).CodePoint;
                if (hi < lo)
                {
                    throw new GrammarFault($"the range at character {Position(start)} is backwards");
                }

                ranges.Add((lo, hi));
            }
        }

        /// <summary><c>CCchar</c> or <c>charClassEsc</c>: a <see cref="Literal"/> or a <see cref="Category"/>.</summary>
        private Node ParseClassChar()
        {
            var c = Current;
            if (c == '\\')
            {
                return ParseEscape();
            }

            if (c == '[' || c == ']' || c == '-' || IsSurrogate(c))
            {
                throw NotAllowed(_at);
            }

            _at++;
            return new Literal(c);
        }

        private bool IsCategoryEscape(int at) =>
            text[at] == '\\' && at + 1 < text.Count && (text[at + 1] == 'p' || text[at + 1] == 'P');

        /// <summary><c>SingleCharEsc</c> or <c>charClassEsc</c>; <see cref="_at"/> is on the backslash.</summary>
        private Node ParseEscape()
        {
            var start = _at;
            _at++;
            if (AtEnd)
            {
                throw new GrammarFault("the pattern ends inside an escape");
            }

            var c = Current;
            _at++;
            switch (c)
            {
                case 'n':
                    return new Literal('\n');
                case 'r':
                    return new Literal('\r');
                case 't':
                    return new Literal('\t');
                case >= 0x28 and <= 0x2B: // ( ) * +
                case '-' or '.' or '?':
                case >= 0x5B and <= 0x5E: // [ \ ] ^
                case >= 0x7B and <= 0x7D: // { | }
                    return new Literal(c);
                case 'p' or 'P':
                    return ParseCategory(start, negated: c == 'P');
                default:
                    throw new GrammarFault($"'\\{Show(c)}' at character {Position(start)} is not an escape of I-Regexp");
            }
        }

        /// <summary>The <c>{Name}</c> of <c>\p</c> or <c>\P</c>; <see cref="_at"/> is after the letter.</summary>
        private Category ParseCategory(int start, bool negated)
        {
            if (AtEnd)
            {
                throw new GrammarFault("the pattern ends inside an escape");
            }

            if (Current != '{')
            {
                throw new GrammarFault($"'\\{(negated ? 'P' : 'p')}' at character {Position(start)} is not an escape of I-Regexp");
            }

            _at++;
            var name = new StringBuilder();
            while (!AtEnd && Current != '}')
            {
                name.Append(Show(Current));
                _at++;
            }

            if (AtEnd)
            {
                throw new GrammarFault("the pattern ends inside an escape");
            }

            _at++; // '}'
            if (!Categories.Contains(name.ToString()))
            {
                throw new GrammarFault($"'{name}' at character {Position(start)} is not a Unicode category of I-Regexp");
            }

            return new Category(name.ToString(), negated);
        }

        private GrammarFault NotAllowed(int at) =>
            new($"'{Show(text[at])}' at character {Position(at)} is not allowed there");

        private static string Position(int at) => (at + 1).ToString(CultureInfo.InvariantCulture);

        private static int Small(BigInteger number) => number > int.MaxValue ? int.MaxValue : (int)number;
    }

    /// <summary><c>NormalChar</c>: any code point but the metacharacters and the surrogates.</summary>
    private static bool IsNormalChar(int c) =>
        c is (>= 0x00 and <= 0x27) or ',' or '-' or (>= 0x2F and <= 0x3E) or (>= 0x40 and <= 0x5A)
            or (>= 0x5E and <= 0x7A) or (>= 0x7E and <= 0xD7FF) or (>= 0xE000 and <= LastCodePoint);

    private static bool IsDigit(int c) => c is >= '0' and <= '9';

    private static bool IsSurrogate(int c) => c is >= 0xD800 and <= 0xDFFF;

    /// <summary>A code point as a reason shows it; a lone surrogate, which no text can hold whole, as <c>U+D800</c>.</summary>
    private static string Show(int c) =>
        IsSurrogate(c) ? $"U+{c.ToString("X4", CultureInfo.InvariantCulture)}" : char.ConvertFromUtf32(c);

    // ---- The translation (D11) ------------------------------------------------------------------

    private static string Write(Node tree)
    {
        var net = new StringBuilder();
        Write(net, tree);
        return net.ToString();
    }

    private static void Write(StringBuilder net, Node node)
    {
        switch (node)
        {
            case Alternation alternation:
                for (var i = 0; i < alternation.Branches.Count; i++)
                {
                    if (i > 0)
                    {
                        net.Append('|');
                    }

                    Write(net, alternation.Branches[i]);
                }

                break;
            case Sequence sequence:
                foreach (var piece in sequence.Pieces)
                {
                    Write(net, piece);
                }

                break;
            case Repeat repeat:
                WriteAtom(net, repeat.Atom);
                net.Append(Quantifier(repeat.Min, repeat.Max));
                break;
            default:
                WriteAtom(net, node);
                break;
        }
    }

    /// <summary>An atom, written so that a quantifier after it applies to all of it.</summary>
    private static void WriteAtom(StringBuilder net, Node atom)
    {
        switch (atom)
        {
            case Alternation group:
                net.Append("(?:");
                Write(net, group);
                net.Append(')');
                break;
            case Literal literal when literal.CodePoint > LastBmp:
                net.Append("(?:").Append(Unit(literal.CodePoint)).Append(')');
                break;
            case Literal literal:
                net.Append(Unit(literal.CodePoint));
                break;
            case AnyChar:
                net.Append("(?:[^\\n\\r").Append(Surrogates).Append("]|").Append(AnyAstral).Append(')');
                break;
            case Category { Negated: false } category:
                net.Append('[').Append(Positive(category.Name)).Append(']');
                break;
            case Category category:
                net.Append("(?:[^").Append(Positive(category.Name)).Append(Surrogates).Append("]|").Append(AnyAstral).Append(')');
                break;
            case CharClass charClass:
                WriteClass(net, charClass);
                break;
        }
    }

    private static string Quantifier(int min, int? max) => (min, max) switch
    {
        (0, null) => "*",
        (1, null) => "+",
        (0, 1) => "?",
        (_, null) => FormattableString.Invariant($"{{{min},}}"),
        _ when min == max => FormattableString.Invariant($"{{{min}}}"),
        _ => FormattableString.Invariant($"{{{min},{max}}}"),
    };

    /// <summary>
    /// A class as alternatives that each match one whole code point. A code point of the Basic
    /// Multilingual Plane is judged by one .NET class (its ranges, split around the surrogates, and its
    /// categories); one outside the plane by surrogate-pair alternatives built from its ranges, or any
    /// such pair when the class holds a <c>\P{…}</c> (D11: every astral code point is in every
    /// <c>\P{…}</c>, in no <c>\p{…}</c>).
    /// </summary>
    private static void WriteClass(StringBuilder net, CharClass charClass)
    {
        var bmp = new StringBuilder();
        var astral = new List<(int Lo, int Hi)>();
        foreach (var (lo, hi) in charClass.Ranges)
        {
            AppendBmp(bmp, lo, Math.Min(hi, 0xD7FF));
            AppendBmp(bmp, Math.Max(lo, 0xE000), Math.Min(hi, LastBmp));
            if (hi >= FirstAstral)
            {
                astral.Add((Math.Max(lo, FirstAstral), hi));
            }
        }

        var negatives = charClass.Categories.Where(c => c.Negated).ToList();
        if (negatives.Count > 0)
        {
            astral = [(FirstAstral, LastCodePoint)];
        }

        var parts = new List<string>();
        if (!charClass.Negated)
        {
            foreach (var category in charClass.Categories.Where(c => !c.Negated))
            {
                bmp.Append(Positive(category.Name));
            }

            if (bmp.Length > 0)
            {
                parts.Add("[" + bmp + "]");
            }

            // A \P{X} among other items: the plane's code points outside X, as an alternative of its own.
            parts.AddRange(negatives.Select(c => "[^" + Positive(c.Name) + Surrogates + "]"));
            parts.AddRange(Pairs(Merge(astral)));
        }
        else
        {
            foreach (var category in charClass.Categories)
            {
                bmp.Append(category.Negated ? $"\\P{{{category.Name}}}" : Positive(category.Name));
            }

            parts.Add("[^" + bmp + Surrogates + "]");
            parts.AddRange(Pairs(Complement(Merge(astral))));
        }

        switch (parts.Count)
        {
            case 0:
                net.Append("[^\\u0000-\\uFFFF]"); // matches nothing; no class of the grammar is empty
                break;
            case 1 when parts[0].StartsWith('['):
                net.Append(parts[0]);
                break;
            default:
                net.Append("(?:").Append(string.Join('|', parts)).Append(')');
                break;
        }
    }

    /// <summary>
    /// <c>\p{X}</c> as .NET class items. .NET's <c>C</c> holds the surrogates (<c>Cs</c>), which
    /// RFC 9485's does not, so <c>C</c> is written as its four other members.
    /// </summary>
    private static string Positive(string name) =>
        name == "C" ? "\\p{Cc}\\p{Cf}\\p{Cn}\\p{Co}" : $"\\p{{{name}}}";

    private static void AppendBmp(StringBuilder bmp, int lo, int hi)
    {
        if (lo > hi)
        {
            return;
        }

        bmp.Append(Unit(lo));
        if (hi > lo)
        {
            bmp.Append('-').Append(Unit(hi));
        }
    }

    private static List<(int Lo, int Hi)> Merge(List<(int Lo, int Hi)> ranges)
    {
        var merged = new List<(int Lo, int Hi)>();
        foreach (var (lo, hi) in ranges.OrderBy(r => r.Lo))
        {
            if (merged.Count > 0 && lo <= merged[^1].Hi + 1)
            {
                merged[^1] = (merged[^1].Lo, Math.Max(merged[^1].Hi, hi));
            }
            else
            {
                merged.Add((lo, hi));
            }
        }

        return merged;
    }

    /// <summary>The code points outside the Basic Multilingual Plane that sorted, merged <paramref name="ranges"/> leave out.</summary>
    private static List<(int Lo, int Hi)> Complement(List<(int Lo, int Hi)> ranges)
    {
        var rest = new List<(int Lo, int Hi)>();
        var next = FirstAstral;
        foreach (var (lo, hi) in ranges)
        {
            if (lo > next)
            {
                rest.Add((next, lo - 1));
            }

            next = hi + 1;
        }

        if (next <= LastCodePoint)
        {
            rest.Add((next, LastCodePoint));
        }

        return rest;
    }

    /// <summary>Astral ranges as UTF-16 alternatives: a high surrogate (or a run of them) and a run of low ones.</summary>
    private static IEnumerable<string> Pairs(List<(int Lo, int Hi)> ranges)
    {
        foreach (var (lo, hi) in ranges)
        {
            var (loHigh, loLow) = Split(lo);
            var (hiHigh, hiLow) = Split(hi);
            if (loHigh == hiHigh)
            {
                yield return Unit(loHigh) + LowRun(loLow, hiLow);
                continue;
            }

            yield return Unit(loHigh) + LowRun(loLow, 0xDFFF);
            if (hiHigh - loHigh > 1)
            {
                yield return "[" + Unit(loHigh + 1) + "-" + Unit(hiHigh - 1) + "]" + LowRun(0xDC00, 0xDFFF);
            }

            yield return Unit(hiHigh) + LowRun(0xDC00, hiLow);
        }
    }

    private static string LowRun(int lo, int hi) => lo == hi ? Unit(lo) : "[" + Unit(lo) + "-" + Unit(hi) + "]";

    private static (int High, int Low) Split(int codePoint)
    {
        var offset = codePoint - FirstAstral;
        return (0xD800 + (offset >> 10), 0xDC00 + (offset & 0x3FF));
    }

    /// <summary>A code point as <c>\uXXXX</c> escapes: one for the plane, its surrogate pair outside it.</summary>
    private static string Unit(int codePoint)
    {
        if (codePoint <= LastBmp)
        {
            return "\\u" + codePoint.ToString("X4", CultureInfo.InvariantCulture);
        }

        var (high, low) = Split(codePoint);
        return Unit(high) + Unit(low);
    }
}
