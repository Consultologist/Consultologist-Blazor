namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// The editor's read-only view of a condition string. The engine owns the
/// grammar (Consultologist.Api.Workflow.WorkflowResultConditions); this only
/// needs to answer two questions the authoring surfaces ask, so it reads the
/// text rather than mirroring the parser — a second parser would be a second
/// thing to keep in step.
/// </summary>
public static class WorkflowResultConditionText
{
    /// <summary>The input id a condition reads, or null when it is unparseable.</summary>
    public static string? InputOf(string? when)
    {
        var text = when?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var cut = text.IndexOf("==", StringComparison.Ordinal);
        if (cut < 0)
        {
            cut = text.IndexOf("!=", StringComparison.Ordinal);
        }

        var id = (cut < 0 ? text : text[..cut]).Trim();
        return id.Length == 0 ? null : id;
    }

    public static bool ReadsInput(string? when, string inputId) =>
        string.Equals(InputOf(when), inputId, StringComparison.Ordinal);

    /// <summary>Compose: null literal is the bare truthy form.</summary>
    public static string Compose(string inputId, bool negated, string? literal) =>
        literal is null
            ? inputId
            : $"{inputId} {(negated ? "!=" : "==")} {literal}";

    public static bool IsNegated(string? when) =>
        when?.Contains("!=", StringComparison.Ordinal) == true;

    /// <summary>The literal a condition compares against, or null for the bare form.</summary>
    public static string? LiteralOf(string? when)
    {
        var text = when?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var cut = text.IndexOf("==", StringComparison.Ordinal);
        if (cut < 0)
        {
            cut = text.IndexOf("!=", StringComparison.Ordinal);
        }

        return cut < 0 ? null : text[(cut + 2)..].Trim().Trim('"');
    }
}
