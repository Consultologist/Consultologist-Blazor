using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.2): a parsed 30 Scriban condition or <c>let</c> written as JSON Logic,
/// row by row of § 2.2's table and nothing beyond it.
/// <list type="bullet">
/// <item>A read (<c>node.x.value.y</c>, <c>node.x.content</c>, <c>item.id</c>, <c>item.value.y</c>, <c>pass.number</c>,
/// <c>run.x</c>) is <c>{ "var": … }</c> of the same path with its root; a node's root is where 31 reads it
/// (<see cref="ConversionRead.Path"/>), and a classifier's <c>.value</c> is <c>.value.value</c> (its
/// <c>classification</c> format is kept, § 2.2's second bullet).</item>
/// <item>An input read bare is <c>input.x.value</c>, or <c>input.x.content</c> for a text format; <c>input.x.y</c> is
/// <c>input.x.value.y</c>.</item>
/// <item><c>==</c> and <c>!=</c> are <c>===</c> and <c>!==</c>, always; the orderings and <c>+ - * / %</c> keep their
/// operator; a chain of <c>&amp;&amp;</c> (or of <c>||</c>) is one <c>and</c> (one <c>or</c>); <c>!a</c> is
/// <c>{ "!": [a] }</c> (Task 3 turns it into <c>blank</c> on text, § 2.3).</item>
/// <item><c>(x | array.size)</c> and <c>x.size</c> on a list are <c>size</c>; <c>string.downcase</c>, <c>string.strip</c>,
/// <c>string.starts_with</c> and <c>string.ends_with</c> are <c>lower</c>, <c>trim</c>, <c>starts_with</c> and
/// <c>ends_with</c>, called or piped; <c>string.contains x "p"</c> is <c>{ "in": ["p", x] }</c>; <c>date.parse</c> of a
/// <c>YYYY-MM-DD</c> literal is that literal.</item>
/// <item>A number, <c>true</c>, <c>false</c> and a quoted string are the JSON literal; a <c>let</c>'s object is the
/// <c>object</c> operation, its fields in canonical (ordinal) order; an array is a JSON array.</item>
/// </list>
/// Anything else adds one S10-003 notice per construct (a call's arguments are walked too, so each reason is said) and
/// gives <c>null</c>; so does a read of an input, a node, an item or a pass the scope does not hold. A read of
/// <c>profile.*</c> is written as read, for the checker to refuse (S03-018), and so is any member of a node, an item or
/// a pass: the checker holds it to what 31 reads there. Never throws on package text; operations nested past
/// <see cref="SourceBounds.JsonDepth"/> (each writes at least one JSON level; parentheses write none) are a notice,
/// and the converter measures the written depth itself.
/// </summary>
internal static class ConditionTree
{
    private static readonly Regex IsoDate = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// <paramref name="expression"/>, parsed from <paramref name="text"/> (the author's text, which the notices quote),
    /// as JSON Logic over <paramref name="scope"/>; <c>null</c> with a notice in <paramref name="notices"/> for each
    /// construct § 2.2 has no row for.
    /// </summary>
    public static JsonNode? Write(ScriptExpression expression, string text, ConversionScope scope, List<string> notices)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(notices);

        var before = notices.Count;
        var logic = new Writer(text, scope, notices).Rule(expression, 0);
        return notices.Count > before ? null : logic;
    }

    private sealed class Writer(string text, ConversionScope scope, List<string> notices)
    {
        public JsonNode? Rule(ScriptExpression? expression, int depth)
        {
            if (depth > SourceBounds.JsonDepth)
            {
                return Refuse($"nesting deeper than {SourceBounds.JsonDepth.ToString(CultureInfo.InvariantCulture)} levels");
            }

            switch (expression)
            {
                case ScriptNestedExpression nested:
                    // Parentheses write nothing, so they add no depth.
                    return Rule(nested.Expression, depth);
                case ScriptBinaryExpression binary:
                    return Binary(binary, depth);
                case ScriptUnaryExpression unary:
                    return Unary(unary, depth);
                case ScriptLiteral literal:
                    return Literal(literal.Value);
                case ScriptFunctionCall call:
                    return Builtin(call.Target, [.. call.Arguments], depth);
                case ScriptPipeCall pipe:
                    return pipe.To is ScriptFunctionCall piped
                        ? Builtin(piped.Target, [pipe.From, .. piped.Arguments], depth)
                        : Builtin(pipe.To, [pipe.From], depth);
                case ScriptObjectInitializerExpression obj:
                    return Object(obj, depth);
                case ScriptArrayInitializerExpression array:
                    return new JsonArray(array.Values.Select(v => Rule(v, depth + 1)).ToArray());
                case ScriptAssignExpression:
                    return Refuse("an assignment");
                case ScriptMemberExpression or ScriptVariable or ScriptIndexerExpression:
                    return Read(expression);
                case null:
                    return Refuse("nothing");
                default:
                    return Refuse($"the Scriban form '{expression}'");
            }
        }

        // ---- operators ----

        private JsonNode? Binary(ScriptBinaryExpression binary, int depth)
        {
            var op = binary.Operator switch
            {
                ScriptBinaryOperator.And => "and",
                ScriptBinaryOperator.Or => "or",
                ScriptBinaryOperator.CompareEqual => "===",
                ScriptBinaryOperator.CompareNotEqual => "!==",
                ScriptBinaryOperator.CompareLess => "<",
                ScriptBinaryOperator.CompareLessOrEqual => "<=",
                ScriptBinaryOperator.CompareGreater => ">",
                ScriptBinaryOperator.CompareGreaterOrEqual => ">=",
                ScriptBinaryOperator.Add => "+",
                ScriptBinaryOperator.Subtract => "-",
                ScriptBinaryOperator.Multiply => "*",
                ScriptBinaryOperator.Divide => "/",
                ScriptBinaryOperator.Modulus => "%",
                _ => null,
            };

            if (op is null)
            {
                return Refuse($"the operator '{binary.Operator.ToText()}'");
            }

            if (op is "and" or "or")
            {
                // a && b && c is one and (§ 2.2): Scriban nests it to the left; a parenthesised group stays one operand.
                var operands = new List<ScriptExpression?>();
                Flatten(binary, binary.Operator, operands);
                return Operation(op, new JsonArray(operands.Select(o => Rule(o, depth + 1)).ToArray()));
            }

            return Operation(op, new JsonArray(Rule(binary.Left, depth + 1), Rule(binary.Right, depth + 1)));
        }

        private static void Flatten(ScriptExpression? expression, ScriptBinaryOperator op, List<ScriptExpression?> into)
        {
            if (expression is ScriptBinaryExpression binary && binary.Operator == op)
            {
                Flatten(binary.Left, op, into);
                Flatten(binary.Right, op, into);
                return;
            }

            into.Add(expression);
        }

        private JsonNode? Unary(ScriptUnaryExpression unary, int depth)
        {
            switch (unary.Operator)
            {
                case ScriptUnaryOperator.Not:
                    return Operation("!", new JsonArray(Rule(unary.Right, depth + 1)));
                case ScriptUnaryOperator.Negate when unary.Right is ScriptLiteral { Value: not (null or string or bool) } literal:
                    // -1 is the number -1, as Scriban reads it.
                    return Literal(literal.Value, negate: true);
                case ScriptUnaryOperator.Negate:
                    return Operation("-", new JsonArray(Rule(unary.Right, depth + 1)));
                default:
                    return Refuse($"the operator '{unary.Operator.ToText()}'");
            }
        }

        // ---- literals, objects ----

        private JsonNode? Literal(object? value, bool negate = false)
        {
            switch (value)
            {
                case string s:
                    return JsonValue.Create(s);
                case bool b:
                    return JsonValue.Create(b);
                case int or long or short or byte or sbyte or uint or ushort:
                    var whole = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    return JsonValue.Create(negate ? -whole : whole);
                case double or float or decimal:
                    var number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        return Refuse("a number no JSON number holds");
                    }

                    number = negate ? -number : number;
                    return Math.Abs(number) < 9e15 && number == Math.Floor(number)
                        ? JsonValue.Create((long)number)
                        : JsonValue.Create(number);
                case null:
                    return Refuse("null");
                default:
                    return Refuse($"the literal '{System.Convert.ToString(value, CultureInfo.InvariantCulture)}'");
            }
        }

        private JsonNode? Object(ScriptObjectInitializerExpression obj, int depth)
        {
            var fields = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var member in obj.Members)
            {
                var name = member.Name switch
                {
                    ScriptVariable variable => variable.Name,
                    ScriptLiteral { Value: string literal } => literal,
                    _ => null,
                };

                if (name is null)
                {
                    Refuse("an object field whose name is computed");
                    continue;
                }

                if (!fields.TryAdd(name, Rule(member.Value, depth + 1)))
                {
                    Refuse($"the object field '{name}' twice");
                }
            }

            var map = new JsonObject();
            foreach (var (name, value) in fields)
            {
                map[name] = value;
            }

            return Operation("object", map);
        }

        // ---- builtins ----

        private JsonNode? Builtin(ScriptExpression? target, List<ScriptExpression?> arguments, int depth)
        {
            var chain = WorkflowTemplateTokens.Flatten(target);
            if (chain is null || chain.Segments.Any(s => s.Name is null || s.Quoted))
            {
                return Refuse($"the call '{target}'", arguments, depth);
            }

            var name = string.Join('.', [chain.Root, .. chain.Segments.Select(s => s.Name!)]);
            var (op, count) = name switch
            {
                "array.size" => ("size", 1),
                "string.downcase" => ("lower", 1),
                "string.strip" => ("trim", 1),
                "string.starts_with" => ("starts_with", 2),
                "string.ends_with" => ("ends_with", 2),
                "string.contains" => ("in", 2),
                "date.parse" => ("date.parse", 1),
                _ => (null, 0),
            };

            if (op is null)
            {
                return Refuse(name, arguments, depth);
            }

            if (arguments.Count != count)
            {
                return Refuse($"{name} with {arguments.Count.ToString(CultureInfo.InvariantCulture)} arguments", arguments, depth);
            }

            if (op == "date.parse")
            {
                // The literal string, typed date by its comparison (§ 2.2).
                return arguments[0] is ScriptLiteral { Value: string date } && IsoDate.IsMatch(date)
                    ? JsonValue.Create(date)
                    : Refuse("date.parse of anything but a YYYY-MM-DD literal", arguments, depth);
            }

            var operands = arguments.Select(a => Rule(a, depth + 1)).ToList();
            if (op == "in")
            {
                // string.contains x "p" is { "in": ["p", x] }: the needle first.
                operands.Reverse();
            }

            return Operation(op, new JsonArray([.. operands]));
        }

        // ---- reads ----

        private JsonNode? Read(ScriptExpression expression)
        {
            var chain = WorkflowTemplateTokens.Flatten(expression);
            if (chain is null || chain.Segments.Any(s => s.Name is null))
            {
                return Refuse($"the computed read '{expression}'");
            }

            var steps = chain.Segments.Select(s => s.Name!).ToList();
            var written = string.Join('.', [chain.Root, .. steps]);
            switch (chain.Root)
            {
                case "input":
                    return InputRead(steps, written);
                case "node":
                    return NodeRead(steps, written);
                case "item":
                    if (steps.Count == 0)
                    {
                        return Refuse("item read bare");
                    }

                    if (scope.Item is null && !scope.ItemIsFolderItem)
                    {
                        return NotInScope("item");
                    }

                    return steps[0] == "value" && scope.Item is { } element
                        ? Sized("item", element, steps, written)
                        : Var(written);
                case "pass":
                    if (!scope.InWhile)
                    {
                        return NotInScope("pass");
                    }

                    return steps.Count == 0 ? Refuse("pass read bare") : Var(written);
                case "run":
                    return steps is [var fact] && JsonLogicOperations.RunFacts.Contains(fact, StringComparer.Ordinal)
                        ? Var(written)
                        : Refuse($"the read {written}");
                case "profile" when steps.Count > 0:
                    // Written as read, so the 31 checker refuses it with its own sentence (S03-018, § 2.3).
                    return Var(written);
                default:
                    // snippet.*, macro.*, a local variable: no row of § 2.2 reads them.
                    return Refuse($"the read {written}");
            }
        }

        private JsonNode? InputRead(List<string> steps, string written)
        {
            if (steps.Count == 0)
            {
                return Refuse("input read bare");
            }

            var id = steps[0];
            if (!scope.Inputs.TryGetValue(id, out var format))
            {
                return NotInScope($"input.{id}");
            }

            var root = $"input.{id}";
            if (steps.Count == 1)
            {
                return Var(IsText(format) ? $"{root}.content" : $"{root}.value");
            }

            return Sized(root, format, ["value", .. steps.Skip(1)], written);
        }

        private JsonNode? NodeRead(List<string> steps, string written)
        {
            if (steps.Count == 0)
            {
                return Refuse("node read bare");
            }

            var id = steps[0];
            if (!scope.Nodes.TryGetValue(id, out var node))
            {
                return NotInScope($"node.{id}");
            }

            var members = steps.Skip(1).ToList();
            if (members is ["value"] && FormatSet.NameOf(node.Format) == "classification")
            {
                // A 30 classifier gate keeps its format: its value is the classification's value field (§ 2.2).
                return Var($"{node.Path}.value.value");
            }

            if (members is ["value", ..])
            {
                return Sized(node.Path, node.Format, members, written);
            }

            if (members is ["content", "size"])
            {
                return Refuse($"{written} on a value that is not a list");
            }

            return members.Count == 0 ? Refuse($"the read {written}") : Var(string.Join('.', [node.Path, .. members]));
        }

        /// <summary>
        /// <paramref name="root"/> then <paramref name="steps"/> (from <c>value</c> on) as a read; a last step <c>size</c>
        /// that is no field of the value read before it is <c>size</c> of that value when it is a list (§ 2.2), and
        /// no row of the table otherwise.
        /// </summary>
        private JsonNode? Sized(string root, JsonNode format, List<string> steps, string written)
        {
            if (steps is [.., "size"]
                && FormatReads.Read(format, steps, scope.Formats).Fault != ReadFault.None)
            {
                var before = FormatReads.Read(format, steps[..^1], scope.Formats);
                return before.Fault == ReadFault.None && FormatTypes.Shape(before.Format, scope.Formats) == FormatShape.List
                    ? Operation("size", new JsonArray(Var(string.Join('.', [root, .. steps[..^1]]))))
                    : Refuse($"{written} on a value that is not a list");
            }

            return Var(string.Join('.', [root, .. steps]));
        }

        private bool IsText(JsonNode format) => FormatTypes.Shape(format, scope.Formats) == FormatShape.Text;

        // ---- writing ----

        private static JsonObject Var(string path) => new() { ["var"] = path };

        private static JsonObject Operation(string name, JsonNode operands) => new() { [name] = operands };

        private JsonNode? NotInScope(string read) => Refuse($"{read}, which is not in scope here");

        /// <summary>
        /// <see cref="Refuse(string)"/> for a call, after its <paramref name="arguments"/> are walked, so a reason inside
        /// them is said too (§ 2.3: one notice per reason).
        /// </summary>
        private JsonNode? Refuse(string construct, List<ScriptExpression?> arguments, int depth)
        {
            Refuse(construct);
            foreach (var argument in arguments)
            {
                Rule(argument, depth + 1);
            }

            return null;
        }

        private JsonNode? Refuse(string construct)
        {
            var notice = MigrationSentences.NoJsonLogicForm(text, construct);
            if (!notices.Contains(notice, StringComparer.Ordinal))
            {
                notices.Add(notice);
            }

            return null;
        }
    }
}
