using System.Globalization;
using System.Text.Json;
using Scriban;
using Scriban.Functions;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// Renders a prompt template in strict mode: exactly the declared
/// variables are supplied, any other access throws, and the prelude (if any) is
/// prepended followed by one blank line.
/// </summary>
public static class PromptTemplateRenderer
{
    public static string Render(
        WorkflowPromptTemplate prompt,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string>? variableTypes = null,
        // v9 (#425): the declarations behind the typed variables, so an object's
        // fields render as their own types. Optional — the activity has the
        // package in hand and passes them; an older job, or a caller without
        // the package, still materialises structure by its JSON kinds.
        IReadOnlyDictionary<string, WorkflowInputSpec>? declarations = null)
    {
        var declared = new HashSet<string>(prompt.Variables, StringComparer.Ordinal);
        if (!declared.SetEquals(variables.Keys))
        {
            throw new InvalidOperationException(
                $"Prompt '{prompt.Id}' expects exactly [{string.Join(", ", prompt.Variables)}] " +
                $"but was supplied [{string.Join(", ", variables.Keys)}].");
        }

        var template = Template.Parse(prompt.TemplateText);
        if (template.HasErrors)
        {
            throw new InvalidOperationException(
                $"Prompt '{prompt.Id}' template does not parse: {string.Join("; ", template.Messages)}");
        }

        string rendered;
        try
        {
            var scriptObject = new ScriptObject();
            foreach (var (name, value) in variables)
            {
                // v8: a typed input enters the template as its own type, so a
                // date can be formatted and a boolean can drive {{ if }}. v9:
                // structure enters as structure — an array to iterate, an
                // object to reach into, a number to compare. Everything else —
                // and every v5-v7 job, which carries no types at all — enters
                // as the string it always did.
                scriptObject.Add(name, Materialise(name, value, variableTypes, declarations));
            }

            var context = new RenderingContext();

            // #357: a date renders as the ISO calendar date it was supplied as.
            // Scriban's own default is "%d %b %Y", so a value the format
            // rejects rather than normalises on the way in — 2026-8-1 is a
            // 422 — was being silently reformatted on the way out.
            //
            // This is the only knob Scriban consults when stringifying a
            // DateTime, it leaves date.to_string with an explicit pattern
            // alone, and it is per-context: each TemplateContext deep-clones
            // the builtins, and this method builds a fresh one per render.
            ((DateTimeFunctions)context.BuiltinObject[DateTimeFunctions.DateVariable.Name]!).Format = "%Y-%m-%d";

            context.PushGlobal(scriptObject);
            rendered = template.Render(context);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Prompt '{prompt.Id}' failed to render: {ex.Message}", ex);
        }

        return string.IsNullOrEmpty(prompt.PreludeText)
            ? rendered
            : $"{prompt.PreludeText.TrimEnd()}\n\n{rendered}";
    }

    /// <summary>
    /// The context every template renders in — at the job, and at the
    /// publish-time probe, so the two agree. Strict about variables, as
    /// always. And one rule of its own (v9 § 4, #425): an <b>empty array is
    /// falsy</b>. Scriban's only falsy values are null, EmptyScriptObject and
    /// false, so an empty list — and an absent optional array, which renders
    /// as one — would have made {{ if prior_notes }} fire for a question
    /// nobody answered: #358's trap, one type along. Overriding the one
    /// method truthiness lives in removes the trap rather than documenting it.
    /// </summary>
    internal sealed class RenderingContext : TemplateContext
    {
        public RenderingContext()
        {
            StrictVariables = true;
        }

        public override bool ToBool(SourceSpan span, object? value) =>
            value is ScriptArray { Count: 0 } ? false : base.ToBool(span, value);
    }

    /// <summary>
    /// A declared input's value as its own type, or the string when it has no
    /// type. Parsing cannot fail here: the job starter already refused any
    /// value that was not canonical for its declared type, so a bad date never
    /// reaches a template. A defensive fall back to the string keeps a replay
    /// of an older job honest rather than throwing on it.
    /// </summary>
    private static object? Materialise(
        string name,
        string value,
        IReadOnlyDictionary<string, string>? variableTypes,
        IReadOnlyDictionary<string, WorkflowInputSpec>? declarations)
    {
        // The tag names a declared input's type. An input fan's item:value has
        // no tag — the orchestrator types input: bindings only — but it has a
        // declaration (#426), and the declaration says what the element is.
        if (variableTypes is null || !variableTypes.TryGetValue(name, out var type))
        {
            if (declarations is null || !declarations.TryGetValue(name, out var elementDeclaration))
            {
                return value;
            }

            type = WorkflowInputTypes.Of(elementDeclaration);
        }

        // #358: an unanswered optional of a CONVERTED type is null, not the
        // empty string. Scriban's only falsy values are null,
        // EmptyScriptObject.Default and bool false, so the empty string was
        // truthy and {{ if billable }} fired for a question nobody answered —
        // on every emailed job, since a boolean cannot be supplied by email at
        // all (a string in a boolean slot is a 422).
        //
        // null rather than false: false is an ANSWER, and it renders the five
        // characters "false" wherever the variable is interpolated bare. null
        // is falsy and renders nothing, which is v7 § 3's rule unchanged.
        //
        // v9 (#425): an absent optional array is an EMPTY array and an absent
        // optional object an EMPTY object — both falsy under RenderingContext,
        // both safe to reach into. null would have made the idiom the format
        // prescribes, {{ if notes.size > 0 }}, throw on exactly the job where
        // the slot was left empty: Scriban refuses a member of null.
        //
        // text and enum stay strings — both are JSON strings on the wire, and
        // the `(x | string.strip) == ""` idiom the published packages use to
        // test absence would silently stop firing on null.
        if (string.IsNullOrWhiteSpace(value))
        {
            return type switch
            {
                WorkflowInputTypes.Boolean or WorkflowInputTypes.Date or WorkflowInputTypes.Number => null,
                WorkflowInputTypes.Array => new ScriptArray(),
                WorkflowInputTypes.Object => EmptyScriptObject.Default,
                _ => value
            };
        }

        return type switch
        {
            WorkflowInputTypes.Date when DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                => date.ToDateTime(TimeOnly.MinValue),
            WorkflowInputTypes.Boolean when bool.TryParse(value, out var flag) => flag,
            WorkflowInputTypes.Number when decimal.TryParse(
                value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                => number,
            WorkflowInputTypes.Object or WorkflowInputTypes.Array => Structure(value, declarations?.GetValueOrDefault(name)),
            _ => value
        };
    }

    /// <summary>
    /// The carrier (#423) read back and handed to Scriban as what it is: a
    /// ScriptArray in the caller's order, a ScriptObject with exactly the
    /// supplied fields, scalars as their declared field type when the
    /// declaration is in hand and as their JSON kind when it is not.
    /// </summary>
    private static object Structure(string carrier, WorkflowInputSpec? declaration)
    {
        ConsultInputValue parsed;

        try
        {
            parsed = ConsultInputValue.FromJson(carrier);
        }
        catch (JsonException)
        {
            // Not a carrier after all — an older job's plain text under a type
            // it never had. The string it always was.
            return carrier;
        }

        return parsed.Kind switch
        {
            ConsultInputKind.Array => new ScriptArray(parsed.Elements!.Select(element =>
                element.IsObject
                    ? ObjectToScript(element, declaration?.Fields)
                    : Scalar(element, declaration?.Items))),
            ConsultInputKind.Object => ObjectToScript(parsed, declaration?.Fields),
            _ => Scalar(parsed, null) ?? carrier
        };
    }

    private static ScriptObject ObjectToScript(ConsultInputValue value, IReadOnlyList<WorkflowFieldSpec>? fields)
    {
        var script = new ScriptObject();

        foreach (var entry in value.Fields!)
        {
            var field = fields?.FirstOrDefault(f => string.Equals(f.Id, entry.Id, StringComparison.Ordinal));
            script[entry.Id] = Scalar(entry.Value, field is null ? null : WorkflowInputTypes.Of(field));
        }

        return script;
    }

    private static object? Scalar(ConsultInputValue value, string? type) => value.Kind switch
    {
        ConsultInputKind.Boolean => value.Flag!.Value,
        ConsultInputKind.Number => value.NumberValue!.Value,
        ConsultInputKind.Null => null,
        ConsultInputKind.Text when type == WorkflowInputTypes.Date && DateOnly.TryParseExact(
            value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            => date.ToDateTime(TimeOnly.MinValue),
        _ => value.Text
    };
}
