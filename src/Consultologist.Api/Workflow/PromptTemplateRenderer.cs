using System.Globalization;
using Scriban;
using Scriban.Runtime;

namespace Consultologist.Api.Workflow;

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
        IReadOnlyDictionary<string, string>? variableTypes = null)
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
                // date can be formatted and a boolean can drive {{ if }}.
                // Everything else — and every v5-v7 job, which carries no
                // types at all — enters as the string it always did.
                scriptObject.Add(name, TypedOrString(name, value, variableTypes));
            }

            var context = new TemplateContext { StrictVariables = true };
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
    /// A declared input's value as its own type, or the string when it has no
    /// type. Parsing cannot fail here: the job starter already refused any
    /// value that was not canonical for its declared type, so a bad date never
    /// reaches a template. A defensive fall back to the string keeps a replay
    /// of an older job honest rather than throwing on it.
    /// </summary>
    private static object? TypedOrString(
        string name,
        string value,
        IReadOnlyDictionary<string, string>? variableTypes)
    {
        if (variableTypes is null || !variableTypes.TryGetValue(name, out var type))
        {
            return value;
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
        // text and enum stay strings — both are JSON strings on the wire, and
        // the `(x | string.strip) == ""` idiom the published packages use to
        // test absence would silently stop firing on null.
        if (string.IsNullOrWhiteSpace(value)
            && type is WorkflowInputTypes.Boolean or WorkflowInputTypes.Date)
        {
            return null;
        }

        return type switch
        {
            WorkflowInputTypes.Date when DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                => date.ToDateTime(TimeOnly.MinValue),
            WorkflowInputTypes.Boolean when bool.TryParse(value, out var flag) => flag,
            _ => value
        };
    }
}
