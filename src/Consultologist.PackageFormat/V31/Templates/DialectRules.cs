using System.Globalization;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07): one dialect rule a body breaks, found from its syntax: the id of its sentence
/// (<see cref="DialectSentences"/>), the 1-based line in the body, and the fills its sentence needs after the
/// subject, in the sentence's order and without the line (S05-001 <c>[construct]</c>, S05-002 <c>[built-in]</c>,
/// S05-006 <c>[expr]</c>, S05-013 <c>[depth]</c>, S05-030 <c>[id, value]</c>, S05-063 <c>[path, index]</c>,
/// A07-004 <c>[form]</c>, none for the others).
/// </summary>
public sealed record DialectFault(string Id, int Line, IReadOnlyList<string> Fill);

/// <summary>#1504 (specVersion 31, area 07; part 05 § 2.5): what kind of recorded fact a body <c>if</c>'s test is, by its form.</summary>
internal enum BodyTest
{
    /// <summary>None of the three: S05-007.</summary>
    Refused,

    /// <summary>A loop position (<c>for.first</c>, <c>for.index == 0</c>).</summary>
    Position,

    /// <summary>A <c>.state</c> compared with a string literal.</summary>
    State,

    /// <summary><c>check.&lt;…&gt;.value.pass</c>.</summary>
    CheckPassed,
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.1, 2.2, 2.3, 2.6, 5.2; <c>scriban-dialect.md</c> § 10): the
/// statement-level rules of the dialect, each judged from the syntax alone. <see cref="TemplateSyntax.Parse"/>
/// asks them as it walks a body; what a body may read at its site, and what a body <c>if</c> may test, are the
/// later steps'.
/// </summary>
internal static class DialectRules
{
    /// <summary>Part 05 § 2.3: <c>if</c> and <c>for</c> nest at most this deep in one body.</summary>
    public const int NestingLimit = 15;

    /// <summary>Part 05 § 2.2: the seven allowed built-ins.</summary>
    public static readonly IReadOnlySet<string> AllowedBuiltIns = new HashSet<string>(StringComparer.Ordinal)
    {
        "string.upcase", "string.downcase", "string.capitalize", "string.strip", "array.join", "array.size", "date.to_string",
    };

    /// <summary>Part 05 § 2.1: the loop positions a template reads, as <c>for.&lt;name&gt;</c>.</summary>
    public static readonly IReadOnlySet<string> LoopPositions = new HashSet<string>(StringComparer.Ordinal)
    {
        "first", "last", "index", "even", "odd",
    };

    /// <summary>The names Scriban 7.2.5's built-in object holds (<c>TemplateContext.BuiltinObject</c>); a read or a call rooted at one is a built-in.</summary>
    private static readonly IReadOnlySet<string> ScribanBuiltIns = new HashSet<string>(StringComparer.Ordinal)
    {
        "array", "blank", "date", "empty", "html", "include", "include_join", "math", "object", "regex", "string", "timespan",
    };

    /// <summary>Whether <paramref name="chain"/> (dotted, as written) is rooted at a Scriban built-in: a name, not a read of the run.</summary>
    public static bool IsBuiltInName(string chain)
    {
        var dot = chain.IndexOf('.', StringComparison.Ordinal);
        return ScribanBuiltIns.Contains(dot < 0 ? chain : chain[..dot]);
    }

    /// <summary>The root every renderer is read under (part 05 § 5.2).</summary>
    public const string RendererRoot = "renderer";

    /// <summary>S05-001 for a statement the dialect refuses, its keyword as Scriban reads it; <c>null</c> for an allowed statement.</summary>
    public static string? RefusedStatement(ScriptNode node) => node switch
    {
        ScriptCaptureStatement => "capture",
        ScriptFunction => "func",
        ScriptImportStatement => "import",
        ScriptWithStatement => "with",
        ScriptWrapStatement => "wrap",
        ScriptWhileStatement => "while",
        ScriptBreakStatement => "break",
        ScriptContinueStatement => "continue",
        ScriptCaseStatement => "case",
        ScriptTableRowStatement => "tablerow",
        ScriptReturnStatement => "ret",
        ScriptReadOnlyStatement => "readonly",
        _ => null,
    };

    /// <summary>
    /// S05-001 for a binary operator outside the comparisons and <c>&amp;&amp;</c>/<c>||</c> (arithmetic, string
    /// concatenation by <c>+</c>, the coalescing operators, a range outside a <c>for</c>), as written; <c>null</c> when allowed.
    /// </summary>
    public static string? RefusedOperator(ScriptBinaryExpression node) => node.Operator switch
    {
        ScriptBinaryOperator.And or ScriptBinaryOperator.Or
            or ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual
            or ScriptBinaryOperator.CompareLess or ScriptBinaryOperator.CompareGreater
            or ScriptBinaryOperator.CompareLessOrEqual or ScriptBinaryOperator.CompareGreaterOrEqual => null,
        _ => node.OperatorToken?.Value ?? node.OperatorAsText,
    };

    /// <summary>S05-001 for a unary operator other than <c>!</c> (<c>-x</c>, <c>+x</c>, <c>++</c>, <c>@f</c>), as written; <c>null</c> for <c>!</c>.</summary>
    public static string? RefusedOperator(ScriptUnaryExpression node) =>
        node.Operator == ScriptUnaryOperator.Not ? null : node.OperatorToken?.Value ?? node.OperatorAsText;

    /// <summary>
    /// S05-006: a <c>for</c> iterates a range, a literal, or what a pipe or a call gives (every allowed built-in and every
    /// renderer gives text or a number), never a list read from the run.
    /// </summary>
    public static bool IsNoList(ScriptExpression iterator) => Unwrap(iterator) switch
    {
        ScriptBinaryExpression { Operator: ScriptBinaryOperator.RangeInclude or ScriptBinaryOperator.RangeExclude } => true,
        ScriptLiteral or ScriptArrayInitializerExpression or ScriptObjectInitializerExpression or ScriptInterpolatedStringExpression => true,
        ScriptPipeCall or ScriptFunctionCall => true,
        _ => false,
    };

    /// <summary>A read <paramref name="chain"/> (dotted, as written): a Scriban built-in named outside a pipe (S05-002), <c>include</c> (S05-001) or a loop position outside the five (A07-004).</summary>
    public static DialectFault? Read(string chain, int line)
    {
        var dot = chain.IndexOf('.', StringComparison.Ordinal);
        var root = dot < 0 ? chain : chain[..dot];

        if (root is "include" or "include_join")
        {
            return new DialectFault("S05-001", line, ["include"]);
        }

        if (ScribanBuiltIns.Contains(root))
        {
            return new DialectFault("S05-002", line, [chain]);
        }

        if (root == "for" && dot >= 0)
        {
            var rest = chain[(dot + 1)..];
            var member = rest.Split('.')[0];
            if (!LoopPositions.Contains(member))
            {
                return new DialectFault("A07-004", line, ["for." + member]);
            }
        }

        return null;
    }

    /// <summary>
    /// A pipe step to <paramref name="name"/> (<c>null</c> when the step is no name): a renderer or one of the seven
    /// passes; <c>date.to_string</c> needs a string literal for its pattern (S05-005); <c>truncate</c> (S05-003),
    /// <c>object.default</c> (S05-004) and everything else (S05-002) is refused.
    /// </summary>
    public static DialectFault? PipeStep(string? name, string written, IReadOnlyList<ScriptExpression> arguments, int line)
    {
        if (name is null)
        {
            return new DialectFault("S05-002", line, [written]);
        }

        if (name.StartsWith(RendererRoot + ".", StringComparison.Ordinal))
        {
            return null;
        }

        if (name == "date.to_string")
        {
            return arguments.Count == 1 && Unwrap(arguments[0]) is ScriptLiteral { Value: string }
                ? null
                : new DialectFault("S05-005", line, []);
        }

        if (AllowedBuiltIns.Contains(name))
        {
            return null;
        }

        var last = name[(name.LastIndexOf('.') + 1)..];
        if (last.StartsWith("truncate", StringComparison.Ordinal))
        {
            return new DialectFault("S05-003", line, []);
        }

        return name == "object.default"
            ? new DialectFault("S05-004", line, [])
            : new DialectFault("S05-002", line, [name]);
    }

    /// <summary>
    /// A call outside a pipe (<c>renderer.x(v)</c>, <c>string.upcase v</c>, <c>include "a"</c>): the call form of a
    /// renderer is S05-030, <c>include</c> is S05-001, and any other is S05-002 — a built-in is only piped into.
    /// </summary>
    public static DialectFault Call(string? name, string written, IReadOnlyList<string> arguments, int line)
    {
        if (name is not null && name.StartsWith(RendererRoot + ".", StringComparison.Ordinal))
        {
            return new DialectFault("S05-030", line, [name[(RendererRoot.Length + 1)..], string.Join(" ", arguments)]);
        }

        return name is "include" or "include_join"
            ? new DialectFault("S05-001", line, ["include"])
            : new DialectFault("S05-002", line, [name ?? written]);
    }

    /// <summary>A07-004 for a <c>for</c> option, as written: <c>limit:</c> and <c>offset:</c> with a value, <c>reversed</c> without.</summary>
    public static DialectFault ForOption(ScriptNamedArgument option, int line) =>
        new("A07-004", line, [(option.Name?.Name ?? string.Empty) + (option.Value is null ? string.Empty : ":")]);

    /// <summary>S05-013: the deepest nesting of a body, when past <see cref="NestingLimit"/>, at the line it was first reached.</summary>
    public static DialectFault? Nesting(int depth, int line) =>
        depth > NestingLimit ? new DialectFault("S05-013", line, [depth.ToString(CultureInfo.InvariantCulture)]) : null;

    /// <summary>Part 05 § 2.5, part 06 § 6.2: the closed set of states, in its order.</summary>
    public static readonly IReadOnlyList<string> States = ["completed", "skipped", "not_run"];

    /// <summary>
    /// Part 05 § 2.5: whether <paramref name="test"/> is one a body <c>if</c> may make, judged from its form (that what it
    /// reads exists, and that a <c>.state</c> is a state, is the caller's): a loop position, bare or compared with a
    /// literal (<c>for.first</c>, <c>for.index == 0</c>); a <c>.state</c> compared with <c>==</c> or <c>!=</c> to a string
    /// literal; or <c>check.&lt;…&gt;.value.pass</c>, bare or compared with <c>true</c> or <c>false</c>. No pipe.
    /// </summary>
    public static BodyTest TestForm(TemplateTest test)
    {
        if (test.Path is not { } path || test.Pipes.Count > 0)
            return BodyTest.Refused;

        var s = path.Split('.');
        var equality = test.Operator is "==" or "!=";
        if (s is ["for", var position] && LoopPositions.Contains(position))
            return test.Operator == null ? (position == "index" ? BodyTest.Refused : BodyTest.Position) : test.Literal != null ? BodyTest.Position : BodyTest.Refused;
        if (s.Length >= 2 && s[^1] == "state" && equality && test.Literal is { } literal && literal.Length >= 2 && literal[0] is '"' or '\'')
            return BodyTest.State;
        if (s is ["check", _, .., "value", "pass"] && (test.Operator == null || (equality && test.Literal is "true" or "false")))
            return BodyTest.CheckPassed;
        return BodyTest.Refused;
    }

    /// <summary>The expression inside any parentheses.</summary>
    public static ScriptExpression Unwrap(ScriptExpression expression)
    {
        while (expression is ScriptNestedExpression { Expression: { } inner })
        {
            expression = inner;
        }

        return expression;
    }
}
