using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.3 to 8.6; part 08 § 4; D8, D11): one attempt of an agent call as it is
/// sent. <paramref name="Text"/> is what is sent, <c>null</c> when the prompt could not be prepared: then
/// <paramref name="ErrorKind"/> says why (<c>"plan"</c>: the plan lacks the entry, it is no agent or has no binding,
/// S07-006; <c>"steps"</c>: <see cref="Composition.TooManySteps"/>, S05-062; <c>"compose"</c>: <see cref="Composition.Failure"/>; <c>"engine"</c>: the engine failed reading the body, A11-008).
/// <paramref name="Composition"/> is the body's (its offered macros in prompt order), <paramref name="Detail"/> the row's
/// detail in part 08 § 4's names, <paramref name="Format"/> the format the prompt ends with (a <c>via: "custom"</c>
/// binding's, D8; <c>null</c> for a <c>via: "format"</c> agent, which carries its own), <paramref name="Ordered"/> the
/// node's <c>ordered</c>, and <paramref name="PackageMacros"/> the ids of every macro the entry's package and function
/// define (<c>letrozole</c>, <c>section_notes.hpi</c>).
/// </summary>
public sealed record PreparedPrompt(
    string? Text, Composition Composition, JsonObject Detail, EnvelopedFormat? Format, bool Ordered, IReadOnlyCollection<string> PackageMacros, string? ErrorKind)
{
    /// <summary>Whether the entry returns a text format, whose reply's markers are checked and replaced (part 05 § 8.3: a marker has no place in an object reply).</summary>
    public bool TextReply { get; init; }

    /// <summary>
    /// #1506 (area 09; D2, D10): the text the attempt produced, which never rides in <see cref="Detail"/>
    /// (<see cref="AgentTurn.Produced"/>'s shape); <c>null</c> when the prompt was not prepared.
    /// </summary>
    public JsonObject? Produced { get; init; }
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 8.3 to 8.6, 8.8; part 08 §§ 4, 4.2, 4.3, 8.2, 9; part 02 § 3.5; D8, D10,
/// D11): one attempt of an agent call, prepared and finished, with no Azure in between. <see cref="Prepare"/> composes the
/// prompt (<see cref="BodyComposer.Compose"/>), ends it with the sent format for a custom binding, appends the retry text
/// for a retry, and gives the row's detail; <see cref="Finish"/> unwraps an enveloped reply and checks and replaces a text
/// reply's markers (<see cref="MacroReply"/>). Neither ever puts a value, a reply or a profile field in a sentence or the
/// detail: only ids, keys, hashes and lengths. Never throws on the plan, the data or a reply.
/// </summary>
public static class AgentTurn
{
    /// <summary>The trailer before a custom format's schema (the 30 catalogue's custom producer's sentence): the reply is one object, enveloped when its format is not one (D8).</summary>
    public const string CustomTrailer = "Answer with one JSON object conforming to this schema.";

    /// <summary>Part 08 § 4's <c>hashVersion</c> of a 31 agent row's <c>inputHash</c>.</summary>
    public const int HashVersion = 7;

    private static readonly IReadOnlyDictionary<string, bool> NothingPlaced = new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>
    /// The prompt of <paramref name="call"/>'s entry: its body composed over the call's scope with the call's placements
    /// decided (<see cref="AgentCall.Placed"/>) in at most <paramref name="stepLimit"/> steps; for a <c>via: "custom"</c>
    /// binding ended with the format sent (<see cref="EnvelopedFormat"/> with <paramref name="strictRejects"/>, the attested
    /// agent's, removed) through <see cref="WorkflowCustomPrompt.Compose"/>; and, after two newlines, D11's retry text: for
    /// <see cref="AgentCall.Faults"/>, each fault's sentence on its own line; for another repair, the format's line.
    /// </summary>
    public static PreparedPrompt Prepare(RunPlan plan, AgentCall call, int stepLimit, IReadOnlyList<string> strictRejects)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(strictRejects);

        var map = call.Function == null ? plan.Entries : plan.Functions.GetValueOrDefault(call.Function)?.Entries;
        var entry = map?.GetValueOrDefault(call.EntryKey);
        if (map == null || entry is not { Kind: PlanKind.Agent } || Via(plan, entry) is not { } via)
        {
            var sentence = ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence;
            return Unprepared(new Composition(null, null, sentence, false, [], [], [], []), "plan");
        }

        var composition = BodyComposer.Compose(plan, new ComposeRequest(call.EntryKey, call.Function, call.Scope, call.Placed ?? NothingPlaced, stepLimit));
        if (composition.TooManySteps)
            return Unprepared(composition, "steps");
        if (composition.Text is not { } body)
            return Unprepared(composition, composition.EngineFault != null ? "engine" : "compose");

        var (format, formats) = PlanScope(plan, entry.Format);
        var enveloped = EnvelopedFormat.Of(format, formats, strictRejects);
        var custom = via == "custom";
        string Sent(string text) => Retry(custom ? WorkflowCustomPrompt.Compose(text, CustomTrailer, CanonicalJson.Write(enveloped.Sent)) : text, call);

        var sent = Sent(body);
        var neutral = composition.NeutralText is { } run ? Sent(run) : null;
        var detail = Detail(entry, map, call, composition, sent, neutral, enveloped);
        return new PreparedPrompt(sent, composition, detail, custom ? enveloped : null, entry.Json["ordered"] is JsonValue o && o.TryGetValue<bool>(out var ordered) && ordered,
            PackageMacros(plan, call.Function), null)
        {
            TextReply = plan.Formats.IsText(entry.Format),
            Produced = Produced(call.Key, composition, sent, neutral),
        };
    }

    /// <summary>
    /// <paramref name="reply"/> to the attempt <paramref name="prepared"/> sent, finished: unwrapped from its envelope (D8;
    /// a reply that is not the envelope is given as it is, and its format then refuses it); for a text format, checked and
    /// replaced by <see cref="MacroReply.Process"/>. <see cref="AgentOutcomeKind.Completed"/> with the substituted text and
    /// the detail (each macro placed said), or, when the markers are wrong, with the reply as it came and
    /// <see cref="AgentOutcome.Invalid"/> set. A prompt that was not prepared is <see cref="AgentOutcomeKind.Failed"/> with
    /// its <see cref="PreparedPrompt.ErrorKind"/>.
    /// </summary>
    public static AgentOutcome Finish(PreparedPrompt prepared, string reply)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(reply);
        if (prepared.Text == null)
            return new AgentOutcome(AgentOutcomeKind.Failed, null, 0, prepared.ErrorKind ?? "plan");

        var value = prepared.Format?.Unwrap(reply) ?? reply;
        var detail = prepared.Detail.DeepClone().AsObject();
        var produced = prepared.Produced?.DeepClone().AsObject();
        if (!prepared.TextReply)
            return new AgentOutcome(AgentOutcomeKind.Completed, value, 0, null, detail, Produced: produced);

        var result = MacroReply.Process(value, prepared.Composition.Macros, prepared.PackageMacros, prepared.Ordered);
        if (result.Text is not { } text)
            return new AgentOutcome(AgentOutcomeKind.Completed, value, 0, null, detail, result.Faults, produced);
        foreach (var row in (detail["macros"] as JsonArray ?? []).OfType<JsonObject>())
            row["placed"] = SourceLayout.Text(row["macro"]) is { } site && result.Placed.Contains(site, StringComparer.Ordinal);
        return new AgentOutcome(AgentOutcomeKind.Completed, text, 0, null, detail, Produced: produced);
    }

    private static PreparedPrompt Unprepared(Composition composition, string kind) => new(null, composition, [], null, false, [], kind);

    /// <summary>D11: <paramref name="text"/> with the retry text after two newlines, for a call that asks again; else as it is.</summary>
    private static string Retry(string text, AgentCall call)
    {
        if (call.Faults is { Count: > 0 } faults)
        {
            var lines = string.Concat(faults.Select(f => $"- {f}\n"));
            return $"{text.TrimEnd()}\n\nYour last reply could not be used:\n{lines}Write the reply again, with each marker exactly as given above.";
        }

        return call.Repair
            ? $"{text.TrimEnd()}\n\nYour last reply could not be used: it did not fit the required format. Write the reply again."
            : text;
    }

    /// <summary>The route of the plan's binding for <paramref name="entry"/> (<c>bindings.&lt;format key&gt;.&lt;producer&gt;.via</c>): <c>format</c> or <c>custom</c>, else <c>null</c>.</summary>
    private static string? Via(RunPlan plan, PlanEntry entry)
    {
        const string defs = "#/$defs/";
        if (entry.Format is not JsonObject format || SourceLayout.Text(format["$ref"]) is not { } reference
            || !reference.StartsWith(defs, StringComparison.Ordinal) || SourceLayout.Text(entry.Json["producer"]) is not { } producer)
        {
            return null;
        }

        var via = SourceLayout.Text(((plan.Document["bindings"] as JsonObject)?[reference[defs.Length..]] as JsonObject)?[producer]?["via"]);
        return via is "format" or "custom" ? via : null;
    }

    /// <summary>The ids of the macros (not the folders) of the package and of <paramref name="function"/>.</summary>
    private static IReadOnlyCollection<string> PackageMacros(RunPlan plan, string? function)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sets = new[] { plan.Document["macros"] as JsonObject, function == null ? null : plan.Functions.GetValueOrDefault(function)?.Json["macros"] as JsonObject };
        foreach (var set in sets)
        {
            foreach (var (key, macro) in set ?? [])
            {
                if (key.StartsWith("macro.", StringComparison.Ordinal) && macro is JsonObject m && m.ContainsKey("instruction"))
                    ids.Add(key["macro.".Length..]);
            }
        }

        return ids;
    }

    // ---- the format in the plan's scope ---------------------------------------------------------

    /// <summary>
    /// <paramref name="format"/> and a scope its names resolve in, from the plan's bundle: each <c>#/$defs/&lt;key&gt;</c>
    /// that is not a definition of its own schema, and each <c>$id</c> of an entry (#1508, R2: how an entry refers to
    /// another), becomes the name <c>&lt;key&gt;</c>, found as the bundle's entry (as <see cref="PlanFormats.Expand"/>
    /// reads it), so the sent copy is built as publish builds it (a text format's description first, then its site's).
    /// </summary>
    private static (JsonNode? Format, FormatSet Formats) PlanScope(RunPlan plan, JsonNode? format)
    {
        var bundle = (plan.Document["formats"] as JsonObject)?["$defs"] as JsonObject ?? [];
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, schema) in bundle)
        {
            if (schema is JsonObject entry && SourceLayout.Text(entry["$id"]) is { } id)
                keys.TryAdd(id, key);
        }

        var named = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (key, schema) in bundle)
        {
            if (schema is JsonObject entry && Rebased(entry, bundle, keys, entry["$defs"] as JsonObject, 0) is JsonObject rebased)
                named[key] = rebased;
        }

        var registry = new TypeRegistry(named, new Dictionary<string, JsonObject>(), [], [], new Dictionary<string, BuiltInRenderer>());
        return (Rebased(format, bundle, keys, null, 0), FormatSet.ForPackage(registry, []));
    }

    /// <summary>A copy of <paramref name="node"/> with each reference to a bundle entry written as that entry's key (<see cref="PlanScope"/>), to the depth the plan's formats are expanded to.</summary>
    private static JsonNode? Rebased(JsonNode? node, JsonObject bundle, Dictionary<string, string> keys, JsonObject? own, int depth)
    {
        if (depth > PlanFormats.MaxDepth)
            return node?.DeepClone();
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key == "$ref" && SourceLayout.Text(value) is { } reference && Key(reference, bundle, keys, own) is { } name)
                        copy[key] = name;
                    else
                        copy[key] = Rebased(value, bundle, keys, own, depth + 1);
                }

                return copy;
            case JsonArray list:
                return new JsonArray([.. list.Select(item => Rebased(item, bundle, keys, own, depth + 1))]);
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>The bundle key <paramref name="reference"/> names: an entry's <c>$id</c>, or <c>#/$defs/&lt;key&gt;</c> that is not one of <paramref name="own"/>'s definitions; else null.</summary>
    private static string? Key(string reference, JsonObject bundle, Dictionary<string, string> keys, JsonObject? own)
    {
        if (keys.TryGetValue(reference, out var key))
            return key;
        const string defs = "#/$defs/";
        return reference.StartsWith(defs, StringComparison.Ordinal) && reference[defs.Length..] is var name
            && own?.ContainsKey(name) != true && bundle.ContainsKey(name)
                ? name
                : null;
    }

    // ---- the detail -----------------------------------------------------------------------------

    /// <summary>
    /// Part 08 § 4's members of the attempt's row: <c>inputHash</c> of the sent text, <c>hashVersion</c>,
    /// <c>runNeutralHash</c> (of the sent text with each run spot neutral; <c>null</c> with none), <c>runSpots</c> (§ 8.2),
    /// <c>renders</c> (§ 4.2), <c>macros</c> (§ 4.3, one per macro of the entry's <c>placements</c>), <c>profileReads</c>
    /// (§ 9), <c>sentFormatHash</c> and <c>notSent</c> (part 02 § 3.5), and <c>units</c>, each snippet and part placed by
    /// the hash of its text. Sub-rows are named by the row's key (<see cref="AgentCall.Key"/>).
    /// </summary>
    private static JsonObject Detail(
        PlanEntry entry, IReadOnlyDictionary<string, PlanEntry> map, AgentCall call, Composition composition, string sent, string? neutral,
        EnvelopedFormat format)
    {
        var key = call.Key;
        var profile = new List<ProfileRead>();
        foreach (var read in composition.Macros.SelectMany(m => m.ProfileReads))
        {
            if (!profile.Any(p => p.Field == read.Field))
                profile.Add(read);
        }

        return new JsonObject
        {
            ["inputHash"] = CanonicalJson.HashText(sent),
            ["hashVersion"] = HashVersion,
            ["runNeutralHash"] = neutral == null ? null : CanonicalJson.HashText(neutral),
            ["runSpots"] = SpotRows(key, composition),
            ["renders"] = RenderRows(key, composition),
            ["macros"] = Macros(entry, map, call, composition),
            ["profileReads"] = new JsonArray([.. profile.Select(p => (JsonNode)new JsonObject { ["field"] = p.Field, ["hash"] = p.Hash })]),
            ["sentFormatHash"] = format.Hash,
            ["notSent"] = new JsonArray([.. format.NotSent.Select(p => (JsonNode)p)]),
            ["units"] = UnitRows(composition),
        };
    }

    /// <summary>
    /// Part 08 § 8.2's run spots <paramref name="spots"/>, each named <c>&lt;key&gt;/spot:&lt;n&gt;</c>, <c>n</c> its 1-based
    /// order of filling (#1506, D5), with its body <c>site</c>, its <c>pass</c>, and its place in the sent text in runes
    /// (R10): <c>{ id, site, pass, reads, valueHash, output, outputHash, start, length }</c>.
    /// </summary>
    internal static JsonArray SpotRows(string key, IReadOnlyList<ComposedSpot> spots) =>
        new([.. spots.Select((s, i) => (JsonNode)new JsonObject
        {
            ["id"] = $"{key}/spot:{(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ["site"] = s.Site,
            ["pass"] = PassOf(s.Pass),
            ["reads"] = s.Reads,
            ["valueHash"] = s.ValueHash,
            ["output"] = s.Output,
            ["outputHash"] = s.OutputHash,
            ["start"] = s.Start,
            ["length"] = s.Length,
        })]);

    /// <summary>Part 08 § 8.2's run spots of <paramref name="composition"/> (<see cref="SpotRows(string, IReadOnlyList{ComposedSpot})"/>).</summary>
    internal static JsonArray SpotRows(string key, Composition composition) => SpotRows(key, composition.Spots);

    /// <summary>
    /// Part 08 § 4.2's renderer sub-rows of <paramref name="composition"/>, each named <c>&lt;key&gt;/render:&lt;n&gt;</c>, a
    /// chain's <c>source</c> the step before's, with (#1506, D5) its <c>pass</c>.
    /// </summary>
    internal static JsonArray RenderRows(string key, Composition composition) =>
        new([.. composition.Renders.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = $"{key}/render:{r.N.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ["renderer"] = r.Renderer,
            ["templateHash"] = r.TemplateHash,
            ["source"] = r.Source.StartsWith("render:", StringComparison.Ordinal) ? $"{key}/{r.Source}" : r.Source,
            ["inputHash"] = r.InputHash,
            ["pass"] = PassOf(r.Pass),
            ["inputSize"] = r.InputSize,
            ["outputHash"] = r.OutputHash,
            ["outputLength"] = r.OutputLength,
            ["runDependent"] = r.RunDependent,
        })]);

    /// <summary>Part 05 § 9's placed units of <paramref name="composition"/>: <c>{ site, hash, sent }</c> (#1506, R9: <c>sent</c> false for a snippet substituted into a macro's body).</summary>
    internal static JsonArray UnitRows(Composition composition) =>
        new([.. composition.Units.Select(u => (JsonNode)new JsonObject { ["site"] = u.Site, ["hash"] = u.TextHash, ["sent"] = u.Sent })]);

    /// <summary>A sub-row's <c>pass</c>: its numbers, or <c>null</c> outside every template <c>for</c>.</summary>
    private static JsonArray? PassOf(IReadOnlyList<int>? pass) =>
        pass == null ? null : new JsonArray([.. pass.Select(n => (JsonNode)n)]);

    /// <summary>
    /// #1506 (area 09; D2, D10; the controller's ruling 1): the text an attempt produced, which never rides in a row's detail:
    /// <c>{ sent, neutral, renders, macros }</c>: the prompt as sent; its run-neutral form (<c>null</c> with no run spot);
    /// each renderer step's <c>{ input, output }</c> by its sub-row id, with <c>#</c> and its pass joined by <c>,</c> inside a
    /// template <c>for</c> (<c>node.x/render:1#2</c>); and each offered macro's body as substituted, by its id
    /// (<c>section_notes.hpi</c>).
    /// </summary>
    internal static JsonObject Produced(string key, Composition composition, string sent, string? neutral)
    {
        var produced = new JsonObject { ["sent"] = sent, ["neutral"] = neutral, ["renders"] = RenderTexts(key, composition) };
        var macros = new JsonObject();
        foreach (var macro in composition.Macros)
            macros[macro.Site["macro.".Length..]] = macro.Body;
        produced["macros"] = macros;
        return produced;
    }

    /// <summary>#1506 (D2): each renderer step's <c>{ input, output }</c> by its sub-row id and pass (<see cref="Produced"/>).</summary>
    internal static JsonObject RenderTexts(string key, Composition composition)
    {
        var renders = new JsonObject();
        for (var i = 0; i < composition.Renders.Count && i < composition.RenderTexts.Count; i++)
        {
            var render = composition.Renders[i];
            var id = $"{key}/render:{render.N.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (render.Pass != null)
                id += "#" + string.Join(',', render.Pass.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            renders[id] = new JsonObject { ["input"] = composition.RenderTexts[i].Input, ["output"] = composition.RenderTexts[i].Output };
        }

        return renders;
    }

    /// <summary>
    /// Part 08 § 4.3's macro row for each macro of the entry's <c>placements</c>, offered or not: <c>decisions</c> are the
    /// decision ids of the non-null rules its offer depended on (its own, its folder's, a part's that places it), the
    /// <c>classifier</c> is D10's, and <c>placed</c> is <c>false</c> until <see cref="Finish"/> says otherwise.
    /// </summary>
    private static JsonArray Macros(PlanEntry entry, IReadOnlyDictionary<string, PlanEntry> map, AgentCall call, Composition composition)
    {
        var placements = (entry.Json["placements"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        JsonObject? Placement(string? site) => site == null ? null : placements.FirstOrDefault(p => SourceLayout.Text(p["site"]) == site);
        static bool Ruled(JsonObject? placement) => placement?["if"] is { } rule && rule.GetValueKind() != System.Text.Json.JsonValueKind.Null;

        var rows = new JsonArray();
        foreach (var placement in placements)
        {
            if (SourceLayout.Text(placement["site"]) is not { } site || !site.StartsWith("macro.", StringComparison.Ordinal))
                continue;
            var decisions = new List<string>();
            if (Ruled(placement))
                decisions.Add($"{call.Key}/if:{site}");
            if (site.Split('.') is [_, var folder, _] && Ruled(Placement($"macro-folder.{folder}")))
                decisions.Add($"{call.Key}/if:macro-folder.{folder}");
            var within = SourceLayout.Text(placement["within"]);
            for (var steps = 0; within != null && steps <= placements.Count; steps++)
            {
                var placing = Placement(within);
                if (within.StartsWith("part.", StringComparison.Ordinal) && Ruled(placing))
                    decisions.Add($"{call.Key}/if:{within}");
                within = SourceLayout.Text(placing?["within"]);
            }

            var offered = composition.Macros.FirstOrDefault(m => m.Site == site);
            var id = $"{call.Key}/macro:{site["macro.".Length..]}";
            rows.Add(new JsonObject
            {
                ["id"] = id,
                ["macro"] = site,
                ["offered"] = offered != null,
                ["decisions"] = new JsonArray([.. decisions.Select(d => (JsonNode)d)]),
                ["classifier"] = Classifier(map, entry, call, placement["if"]),
                ["placed"] = false,
                ["form"] = offered == null ? null : offered.Block ? "block" : "inline",
                ["contentHash"] = offered?.ContentHash,
                ["runNeutralHash"] = offered?.NeutralText is { } neutral ? CanonicalJson.HashText(neutral) : null, // #1506 (R7)
                ["runSpots"] = SpotRows(id, offered?.Spots ?? []),
            });
        }

        return rows;
    }

    /// <summary>
    /// D10: the row key of the node <paramref name="rule"/> reads when it reads exactly one node of the entry's map, in the
    /// instance the call stands in (its item and pass ids for the loops around both); otherwise <c>null</c>.
    /// </summary>
    private static string? Classifier(IReadOnlyDictionary<string, PlanEntry> map, PlanEntry entry, AgentCall call, JsonNode? rule)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Vars(rule, 0))
        {
            var segments = path.Split('.');
            for (var length = segments.Length; length >= 2; length--)
            {
                var key = string.Join('.', segments, 0, length);
                if (key.StartsWith("node.", StringComparison.Ordinal) && map.ContainsKey(key))
                {
                    nodes.Add(key);
                    break;
                }
            }
        }

        return nodes.Count == 1 ? InstanceKey(map, entry, call, nodes.First()) : null;
    }

    /// <summary>Every <c>var</c> path of <paramref name="rule"/>, to a bounded depth.</summary>
    private static IEnumerable<string> Vars(JsonNode? rule, int depth)
    {
        if (depth > PlanFormats.MaxDepth)
            yield break;
        switch (rule)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var path = key == "var" ? SourceLayout.Text(value) ?? SourceLayout.Text((value as JsonArray)?.FirstOrDefault()) : null;
                    if (path != null)
                        yield return path;
                    else
                        foreach (var inner in Vars(value, depth + 1))
                            yield return inner;
                }

                break;
            case JsonArray list:
                foreach (var item in list)
                    foreach (var inner in Vars(item, depth + 1))
                        yield return inner;
                break;
        }
    }

    /// <summary>
    /// The row key of <paramref name="classifier"/> (a key of the entry's map) in the instance <paramref name="call"/>
    /// stands in: under the call's prefix inside a function, with the item and pass ids (<see cref="RunKeys.Of"/>'s
    /// <c>:</c>, <c>/</c> and <c>#</c> parts) of the loops around it, which are the outer loops around the entry; <c>null</c>
    /// when it stands in a loop the entry does not.
    /// </summary>
    private static string? InstanceKey(IReadOnlyDictionary<string, PlanEntry> map, PlanEntry entry, AgentCall call, string classifier)
    {
        var cut = call.Key.IndexOfAny([':', '#']);
        var row = cut < 0 ? call.Key : call.Key[..cut];
        var suffix = cut < 0 ? string.Empty : call.Key[cut..];
        string baseKey;
        if (call.Function == null)
        {
            if (row != entry.Key)
                return null;
            baseKey = classifier;
        }
        else
        {
            var local = entry.Key["node.".Length..];
            if (!row.EndsWith("." + local, StringComparison.Ordinal))
                return null;
            baseKey = row[..^local.Length] + classifier["node.".Length..];
        }

        var around = Loops(map, entry.Key);
        var its = Loops(map, classifier);
        if (its.Count > around.Count || !its.SequenceEqual(around.Take(its.Count), StringComparer.Ordinal))
            return null;
        var parts = new List<string>();
        for (var i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] is ':' or '/' or '#')
                parts.Add(string.Empty);
            if (parts.Count > 0)
                parts[^1] += suffix[i];
        }

        var drop = around.Count - its.Count;
        return drop > parts.Count ? null : baseKey + string.Concat(parts.Take(parts.Count - drop));
    }

    /// <summary>The <c>for</c> and <c>while</c> entries around <paramref name="key"/> in <paramref name="map"/>, outermost first.</summary>
    private static List<string> Loops(IReadOnlyDictionary<string, PlanEntry> map, string key)
    {
        var loops = new List<string>();
        var steps = 0;
        for (var at = map.GetValueOrDefault(key)?.Parent; at != null && steps++ <= map.Count && map.TryGetValue(at, out var parent); at = parent.Parent)
        {
            if (parent.Kind is PlanKind.For or PlanKind.While)
                loops.Insert(0, at);
        }

        return loops;
    }
}
