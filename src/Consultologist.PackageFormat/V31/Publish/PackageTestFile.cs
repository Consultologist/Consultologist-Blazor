using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 §§ 8.1 to 8.3): one in-package test read against the compiled package.
/// <paramref name="FileHash"/> is the SHA-256 of the file's text as stored (line endings normalised);
/// <paramref name="Request"/> the offline request it states (strict: no missing stub is generated, D6); <paramref name="Expect"/>
/// its <c>expect</c> object, every member of it of the shape § 8.3 gives.
/// </summary>
public sealed record PackageTestFile(string Id, string Path, string FileHash, OfflineRequest Request, JsonObject Expect);

/// <summary>
/// #1508 (area 11; part 07 §§ 8.1 to 8.3; R9; Review Focus 3): reads a test's meaning against the plan. <c>SourceShapes.Test</c>
/// has already checked that the six members are present; this reader checks that they say what the compiled package needs:
/// every declared input listed (S07-010), <c>profile</c> exactly <c>needs.profile</c> (S07-011), <c>run</c> exactly the facts the
/// package reads (S07-012, a date as <c>{ "value", "timeZone" }</c>), and every <c>outputs</c> key and <c>expect</c> member of
/// the grammar's shape (A11-005). Whatever the file holds, no exception is thrown; every refusal found is returned in member
/// order (one A11-005 for the first fault of a member), at the test's path, and none carries a value from the file.
/// </summary>
public static class PackageTestFiles
{
    private const string Segment = "[a-z][a-z0-9_]*";
    private const string Item = "[a-z0-9_]+";
    private const string Tail = "(?:#[1-9][0-9]*)*(?::" + Item + "(?:#[1-9][0-9]*)*(?:/" + Item + "(?:#[1-9][0-9]*)*)*)?";
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex OutputKey = new($@"\Anode\.{Segment}(?:\.{Segment})*{Tail}\z", Options);
    private static readonly Regex RowKey = new($@"\A(?:node|check|deliverable)\.{Segment}(?:\.{Segment})*{Tail}\z", Options);
    private static readonly Regex CheckKey = new($@"\Acheck\.{Segment}(?:\.{Segment})*{Tail}\z", Options);
    private static readonly Regex DeliverableKey = new($@"\Adeliverable\.{Segment}(?:\.{Segment})*(?::{Item}(?:/{Item})*)?\z", Options);
    private static readonly Regex Sha256 = new(@"\Asha256:[0-9a-f]{64}\z", Options);
    private static readonly Regex PlainName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", Options);

    private static readonly string[] DeliverableStates = ["released", "held", "failed", "skipped", "blocked"];

    /// <summary>
    /// The test <paramref name="test"/> (its file's <paramref name="text"/> as stored) against <paramref name="plan"/>: the
    /// test and no refusals, or no test and every refusal found. A test with refusals is never returned.
    /// </summary>
    public static (PackageTestFile? Test, IReadOnlyList<SourceRefusal> Refusals) Read(SourceTest test, string text, RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(plan);

        var refusals = new List<SourceRefusal>();
        void Grammar(string member, string what) =>
            refusals.Add(PackagePublishSentences.TestGrammar(test.Id, member, what, test.Path));

        Inputs(test, plan, refusals, Grammar);
        Profile(test, plan, refusals, Grammar);
        Run(test, plan, refusals, Grammar);
        Outputs(test, Grammar);
        var expect = Expect(test, Grammar);
        if (refusals.Count > 0)
            return (null, refusals);

        var request = SnapshotTest.Request(test.Json) with { GenerateMissing = false };
        var hash = CanonicalJson.HashText(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        return (new PackageTestFile(test.Id, test.Path, hash, request, expect), refusals);
    }

    // ---- inputs, profile, run -------------------------------------------------------------------

    private static void Inputs(SourceTest test, RunPlan plan, List<SourceRefusal> refusals, Action<string, string> grammar)
    {
        if (Member(test, "inputs", grammar) is not { } inputs)
            return;
        foreach (var id in plan.InputOrder)
        {
            if (!inputs.ContainsKey(id))
                refusals.Add(PackagePublishSentences.InputMissing(test.Id, id, test.Path));
        }
    }

    private static void Profile(SourceTest test, RunPlan plan, List<SourceRefusal> refusals, Action<string, string> grammar)
    {
        if (Member(test, "profile", grammar) is not { } profile)
            return;
        var needs = plan.NeedsProfile.Distinct(StringComparer.Ordinal).ToList();
        if (!SameSet(profile.Select(p => p.Key), needs))
            refusals.Add(PackagePublishSentences.ProfileMismatch(test.Id, needs, test.Path));

        foreach (var (field, value) in profile)
        {
            if (value is not JsonValue text || !text.TryGetValue<string>(out _))
            {
                grammar("profile", $"{Name(field, "a field")} is not text");
                break;
            }
        }
    }

    private static void Run(SourceTest test, RunPlan plan, List<SourceRefusal> refusals, Action<string, string> grammar)
    {
        if (Member(test, "run", grammar) is not { } run)
            return;
        var facts = plan.RunFacts
            .Select(fact => fact.StartsWith("run.", StringComparison.Ordinal) ? fact["run.".Length..] : fact)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!SameSet(run.Select(r => r.Key), facts))
            refusals.Add(PackagePublishSentences.RunMismatch(test.Id, facts, test.Path));

        foreach (var (fact, value) in run)
        {
            if (fact == "date")
            {
                if (!IsDate(value))
                {
                    grammar("run", "date is not { \"value\", \"timeZone\" } with both as text");
                    break;
                }
            }
            else if (value is not JsonValue text || !text.TryGetValue<string>(out _))
            {
                grammar("run", $"{Name(fact, "a fact")} is not text");
                break;
            }
        }
    }

    private static bool IsDate(JsonNode? value) =>
        value is JsonObject { Count: 2 } date && IsText(date["value"]) && IsText(date["timeZone"]);

    // ---- outputs --------------------------------------------------------------------------------

    private static void Outputs(SourceTest test, Action<string, string> grammar)
    {
        if (Member(test, "outputs", grammar) is not { } outputs)
            return;
        var position = 0;
        foreach (var (key, _) in outputs)
        {
            position++;
            if (!OutputKey.IsMatch(key))
            {
                grammar("outputs", $"key number {position} is not a node's row key (node.<id>, then : and its item path, # and its pass)");
                return;
            }
        }
    }

    // ---- expect ---------------------------------------------------------------------------------

    private static JsonObject Expect(SourceTest test, Action<string, string> grammar)
    {
        if (Member(test, "expect", grammar) is not { } expect)
            return [];

        var position = 0;
        foreach (var (name, value) in expect)
        {
            position++;
            var member = $"expect.{name}";
            string? fault = name switch
            {
                "ran" or "skipped" => Keys(value, RowKey),
                "decisions" => Decisions(value),
                "loops" => ByKey(value, RowKey, Loop),
                "values" => ByKey(value, RowKey, _ => null),
                "prompts" => ByKey(value, RowKey, Prompt),
                "checks" => ByKey(value, CheckKey, Check),
                "deliverables" => ByKey(value, DeliverableKey, Deliverable),
                _ => null,
            };
            if (name is not ("ran" or "skipped" or "decisions" or "loops" or "values" or "prompts" or "checks" or "deliverables"))
                grammar("expect", $"member number {position} is not an expectation (ran, skipped, decisions, loops, values, prompts, checks, deliverables)");
            else if (fault != null)
                grammar(member, fault);
        }

        return expect;
    }

    private static string? Keys(JsonNode? value, Regex pattern)
    {
        if (value is not JsonArray list)
            return "it is not a list of row keys";
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonValue key || !key.TryGetValue<string>(out var text) || !pattern.IsMatch(text))
                return $"item number {i + 1} is not a row key";
        }

        return null;
    }

    private static string? Decisions(JsonNode? value)
    {
        if (value is not JsonObject decisions)
            return "it is not an object";
        var position = 0;
        foreach (var (id, result) in decisions)
        {
            position++;
            if (id.Length == 0)
                return $"key number {position} is empty";
            var ok = result is JsonValue v && (v.TryGetValue<bool>(out _) || v.TryGetValue<string>(out _));
            if (!ok)
                return $"entry number {position} is not true, false or a branch id";
        }

        return null;
    }

    private static string? ByKey(JsonNode? value, Regex pattern, Func<JsonNode?, string?> entry)
    {
        if (value is not JsonObject map)
            return "it is not an object";
        var position = 0;
        foreach (var (key, item) in map)
        {
            position++;
            if (!pattern.IsMatch(key))
                return $"key number {position} is not a row key of its kind";
            if (entry(item) is { } fault)
                return $"entry number {position} {fault}";
        }

        return null;
    }

    private static string? Loop(JsonNode? value)
    {
        if (value is JsonObject { Count: 1 } counted && counted.ContainsKey("count"))
            return IsInteger(counted["count"]) ? null : "has a count that is not a whole number";
        if (value is JsonObject { Count: 2 } passed && passed.ContainsKey("passes") && passed.ContainsKey("stopped"))
            return IsInteger(passed["passes"]) && IsText(passed["stopped"]) ? null : "has passes that are not a whole number or a stopped that is not text";
        return "is not { count } or { passes, stopped }";
    }

    private static string? Prompt(JsonNode? value)
    {
        if (value is JsonObject { Count: 1 } one && one.ContainsKey("text"))
            return IsText(one["text"]) ? null : "has a text that is not text";
        if (value is JsonObject { Count: 1 } hashed && hashed.ContainsKey("hash"))
            return IsHash(hashed["hash"]) ? null : "has a hash that is not sha256: and 64 hex digits";
        return "is not { text } or { hash }";
    }

    private static string? Check(JsonNode? value) =>
        value is JsonObject { Count: 1 } one && one["pass"] is JsonValue pass && pass.TryGetValue<bool>(out _)
            ? null
            : "is not { pass } with true or false";

    private static string? Deliverable(JsonNode? value)
    {
        if (value is not JsonObject deliverable || deliverable["state"] is not JsonValue state || !state.TryGetValue<string>(out var word)
            || !DeliverableStates.Contains(word, StringComparer.Ordinal))
            return "has no state of released, held, failed, skipped or blocked";
        if (deliverable.Count == 1)
            return word == "skipped" ? null : "has a state alone, which only skipped may";
        if (deliverable.Count == 2 && deliverable.ContainsKey("text"))
            return IsText(deliverable["text"]) ? null : "has a text that is not text";
        if (deliverable.Count == 2 && deliverable.ContainsKey("hash"))
            return IsHash(deliverable["hash"]) ? null : "has a hash that is not sha256: and 64 hex digits";
        return "is not { state, text }, { state, hash } or { state: \"skipped\" }";
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>The member as an object; absent reads as empty, and a member of another type is A11-005 (null returned, nothing more said of it).</summary>
    private static JsonObject? Member(SourceTest test, string name, Action<string, string> grammar)
    {
        if (!test.Json.TryGetPropertyValue(name, out var value))
            return [];
        if (value is JsonObject member)
            return member;
        grammar(name, "it is not an object");
        return null;
    }

    private static bool SameSet(IEnumerable<string> given, IReadOnlyCollection<string> wanted) =>
        given.ToHashSet(StringComparer.Ordinal).SetEquals(wanted);

    private static bool IsText(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out _);

    private static bool IsHash(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) && Sha256.IsMatch(text);

    private static bool IsInteger(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out _);

    /// <summary>A name the sentence may print: a plain identifier up to 64 characters, else <paramref name="otherwise"/> (a hostile name is never copied).</summary>
    private static string Name(string name, string otherwise) => PlainName.IsMatch(name) ? $"'{name}'" : otherwise;
}
