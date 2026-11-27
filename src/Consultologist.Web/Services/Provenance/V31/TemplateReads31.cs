using System.Text;
using System.Text.Json;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// One evaluation of a template site (part 08 § 10's <c>substitutions</c>): <paramref name="Pass"/> one number per enclosing
/// template loop (null in none), what it read, the hash of what it wrote, and where that sits in the composed text
/// (<paramref name="Start"/> and <paramref name="Size"/>, in runes), with the renderer it piped into, if any.
/// </summary>
public sealed record Substitution31(IReadOnlyList<int>? Pass, string Reads, string Hash, int Start, int Size, string? Renders);

/// <summary>One expression of a deliverable's template (part 08 § 10's <c>sites</c>) and its evaluations, in pass order as stored.</summary>
public sealed record SiteReads31(int Site, string At, string Expr, string? Loop, IReadOnlyList<Substitution31> Passes);

/// <summary>
/// #1509 (Task 20; part 08 § 10, part 09 § 6.4 "Deliverables"): a deliverable row's reads by template line. The sites come
/// in the order the row gives them (source order), each with its substitutions; a substitution naming a site the row does
/// not list is kept, under a site of its own after the listed ones. A substitution's text is cut from the composed text by
/// its position, never found by search. The template is shown from the pinned version (<c>resolve.json</c>'s
/// <c>deliverables.&lt;key&gt;.body</c>, part 07 § 2.5) only when its hash is the row's <c>templateHash</c>. Pure; a member
/// missing or of the wrong kind reads as absent.
/// </summary>
public static class TemplateReads31
{
    public static IReadOnlyList<SiteReads31> Group(Row31 deliverable)
    {
        ArgumentNullException.ThrowIfNull(deliverable);

        var bySite = new Dictionary<int, List<Substitution31>>();
        var unlisted = new List<int>();
        foreach (var element in deliverable.List("substitutions"))
        {
            if (Int(element, "site") is not { } site)
            {
                continue;
            }

            if (!bySite.TryGetValue(site, out var list))
            {
                bySite[site] = list = [];
                unlisted.Add(site);
            }

            list.Add(new Substitution31(Pass(element), Str(element, "reads") ?? "", Str(element, "hash") ?? "",
                Int(element, "start") ?? 0, Int(element, "size") ?? 0, Str(element, "renders")));
        }

        var sites = new List<SiteReads31>();
        foreach (var element in deliverable.List("sites"))
        {
            if (Int(element, "site") is not { } site || sites.Any(s => s.Site == site))
            {
                continue;
            }

            unlisted.Remove(site);
            sites.Add(new SiteReads31(site, Str(element, "at") ?? "", Str(element, "expr") ?? "", Str(element, "loop"),
                bySite.GetValueOrDefault(site) ?? []));
        }

        sites.AddRange(unlisted.Order().Select(site => new SiteReads31(site, "", "", null, bySite[site])));
        return sites;
    }

    /// <summary>The <paramref name="size"/> runes of <paramref name="composed"/> from rune <paramref name="start"/>; null when they do not fit.</summary>
    public static string? Slice(string? composed, int start, int size)
    {
        if (composed is null || start < 0 || size < 0)
        {
            return null;
        }

        var text = new StringBuilder();
        var at = 0;
        foreach (var rune in composed.EnumerateRunes())
        {
            if (at >= start + size)
            {
                break;
            }

            if (at >= start)
            {
                text.Append(rune.ToString());
            }

            at++;
        }

        return at >= start + size ? text.ToString() : null;
    }

    /// <summary>The template the job ran, from the pinned version's <c>resolve.json</c>; null when it was not read or its hash is not the row's.</summary>
    public static string? Template(JsonElement? resolve, Row31 deliverable)
    {
        ArgumentNullException.ThrowIfNull(deliverable);
        return deliverable.Text("templateHash") is { Length: > 0 } pinned
               && Entry(resolve, deliverable.Key) is { } entry
               && Str(entry, "body") is { } body
               && string.Equals(CanonicalJson.HashText(body), pinned, StringComparison.Ordinal)
            ? body
            : null;
    }

    /// <summary>The deliverable's <c>bundle</c> (its folder's, part 06 § 8: <c>"zip"</c>) in <c>resolve.json</c>; null for none or when it was not read.</summary>
    public static string? Bundle(JsonElement? resolve, string key) =>
        Entry(resolve, key) is { } entry ? Str(entry, "bundle") : null;

    private static JsonElement? Entry(JsonElement? resolve, string key) =>
        resolve is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("deliverables", out var deliverables) && deliverables.ValueKind == JsonValueKind.Object
        && deliverables.TryGetProperty(key, out var entry) && entry.ValueKind == JsonValueKind.Object
            ? entry
            : null;

    private static IReadOnlyList<int>? Pass(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("pass", out var pass) && pass.ValueKind == JsonValueKind.Array
            ? pass.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out _)).Select(n => n.GetInt32()).ToList()
            : null;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
