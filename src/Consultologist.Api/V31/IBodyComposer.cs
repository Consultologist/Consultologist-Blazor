using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; D12): what composing a body gave: its text; or <paramref name="TooManySteps"/> when it
/// passed its step budget; or <paramref name="Failure"/>, the composer's sentence for another failure.
/// </summary>
public sealed record ComposedBody(string? Text, bool TooManySteps, string? Failure);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.1; D12): composes an agent's prompt or a rendered node's or a deliverable's
/// text from its entry's <c>body</c> over the data the runner gave it. The entry is
/// <c>plan.Functions[function].Entries[entryKey]</c> inside a function, else <c>plan.Entries[entryKey]</c>. This is the seam
/// area 07 replaces with the 31 Scriban dialect (parts, macros, snippets, renderers).
/// </summary>
public interface IBodyComposer
{
    /// <summary>The body of the entry <paramref name="entryKey"/> composed over <paramref name="scope"/>, in at most <paramref name="stepLimit"/> steps.</summary>
    ComposedBody Compose(RunPlan plan, string entryKey, string? function, JsonObject scope, int stepLimit);
}

/// <summary>
/// #1503 (specVersion 31, area 06; D12): the default composer until area 07: the entry's <c>body</c> rendered with Scriban as
/// it stands, over <paramref name="scope"/>'s paths as Scriban objects, with strict variables (a read the scope does not
/// hold fails rather than rendering empty), the invariant culture, and Scriban's loop limit set to the step budget (a loop
/// past it is <see cref="ComposedBody.TooManySteps"/>). A body that does not parse, or an entry the plan lacks, is not
/// what publish wrote (S07-006); another failure is A06-008, which never quotes a value.
/// </summary>
public sealed class ScribanBodyComposer : IBodyComposer
{
    public ComposedBody Compose(RunPlan plan, string entryKey, string? function, JsonObject scope, int stepLimit)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entryKey);
        ArgumentNullException.ThrowIfNull(scope);

        var entry = V31Entries.Find(plan, entryKey, function);
        if (entry == null || SourceLayout.Text(entry.Json["body"]) is not { } body)
            return new ComposedBody(null, false, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);

        var template = Template.Parse(body);
        if (template.HasErrors)
            return new ComposedBody(null, false, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);

        var context = new TemplateContext
        {
            StrictVariables = true,
            LoopLimit = Math.Max(1, stepLimit),
        };
        context.PushCulture(CultureInfo.InvariantCulture);
        if (Scriptable(scope, 0) is ScriptObject globals)
            context.PushGlobal(globals);

        try
        {
            return new ComposedBody(template.Render(context), false, null);
        }
        catch (ScriptRuntimeException ex) when (PastTheLoopLimit(ex))
        {
            return new ComposedBody(null, true, null);
        }
        catch (Exception ex) when (ex is ScriptRuntimeException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return new ComposedBody(null, false, $"{V31Entries.Subject(entry, function)} couldn't be rendered.");
        }
    }

    /// <summary>The deepest scope nesting turned into Scriban objects; deeper reads <c>null</c>.</summary>
    private const int MaxDepth = 256;

    /// <summary>JSON as Scriban sees it: objects as ScriptObjects, arrays as ScriptArrays, numbers as decimals (else doubles).</summary>
    private static object? Scriptable(JsonNode? node, int depth)
    {
        if (depth > MaxDepth)
            return null;
        switch (node)
        {
            case JsonObject obj:
                var script = new ScriptObject();
                foreach (var (key, value) in obj)
                    script[key] = Scriptable(value, depth + 1);
                return script;
            case JsonArray array:
                return new ScriptArray(array.Select(item => Scriptable(item, depth + 1)));
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case System.Text.Json.JsonValueKind.String:
                        return value.GetValue<string>();
                    case System.Text.Json.JsonValueKind.True:
                        return true;
                    case System.Text.Json.JsonValueKind.False:
                        return false;
                    case System.Text.Json.JsonValueKind.Number:
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

    /// <summary>Whether Scriban stopped at its loop limit (a loop's iterations, or a range's length).</summary>
    private static bool PastTheLoopLimit(ScriptRuntimeException ex) =>
        ex.OriginalMessage.StartsWith("Exceeding number of iteration limit", StringComparison.Ordinal)
        || ex.OriginalMessage.StartsWith("Range expression exceeds LoopLimit", StringComparison.Ordinal);
}

/// <summary>#1503 (specVersion 31, area 06): an entry of a plan by the key a call names, and its subject in a sentence.</summary>
internal static class V31Entries
{
    /// <summary>The entry <paramref name="entryKey"/>: in <paramref name="function"/>'s entries inside one, else the package's; null when absent.</summary>
    public static PlanEntry? Find(RunPlan plan, string entryKey, string? function)
    {
        if (function == null)
            return plan.Entries.GetValueOrDefault(entryKey);
        return plan.Functions.TryGetValue(function, out var found) ? found.Entries.GetValueOrDefault(entryKey) : null;
    }

    /// <summary>Part 09 § 9's subject: <c>Node 'id'</c>, <c>Check 'id'</c> or <c>Deliverable 'id'</c>, with <c> in function 'f'</c> inside one.</summary>
    public static string Subject(PlanEntry entry, string? function)
    {
        var word = entry.Kind == PlanKind.Deliverable ? "Deliverable"
            : entry.Key.StartsWith("check.", StringComparison.Ordinal) ? "Check"
            : "Node";
        return function == null ? $"{word} '{entry.Id}'" : $"{word} '{entry.Id}' in function '{function}'";
    }
}
