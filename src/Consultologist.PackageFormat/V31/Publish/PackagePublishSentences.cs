namespace Consultologist.PackageFormat.V31.Publish;

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 10; part 10 "Refusals"): the sentences publish says that are not said by an
/// earlier area. S07-007 to S07-016 and S10-005 are rows of <c>v31/sentences.json</c>, written as the row gives them with each
/// <c>&lt;…&gt;</c> filled; A11-004 to A11-007 are this area's own (<c>refusal-sentences.md</c>, "11 Registry and publish").
/// No sentence carries a value from an input, a model, a profile or a test: only ids, keys, paths, counts, hashes and
/// package text (Global Constraints). A sentence about one file is said at that file's path; one about the package, at <c>""</c>.
/// </summary>
public static class PackagePublishSentences
{
    /// <summary>The word a sentence lists an empty set with, as the earlier sentences do.</summary>
    private const string None = "none";

    /// <summary>S07-007: a test's first mismatch (part 07 § 8.3, § 9; D7): <paramref name="expected"/> and <paramref name="actual"/> are the report's words, hashes for text.</summary>
    public static SourceRefusal TestFailed(string testId, string expectation, string key, string expected, string actual, string path) =>
        new("S07-007", $"Test '{testId}' failed: {expectation} for {key} expected {expected}, got {actual}. Publish stopped.", path);

    /// <summary>S07-008: an agent call of the test ran with no stub (D6).</summary>
    public static SourceRefusal NoStub(string testId, string rowKey, string path) =>
        new("S07-008", $"Test '{testId}' has no stub for {rowKey}, which runs. Add it to outputs.", path);

    /// <summary>S07-009: a stub that does not fit its node's format; <paramref name="firstError"/> is a path and a keyword, never the value.</summary>
    public static SourceRefusal StubInvalid(string testId, string rowKey, string format, string firstError, string path) =>
        new("S07-009", $"Test '{testId}' stubs {rowKey} with a value that isn't {format}: {firstError}.", path);

    /// <summary>S07-010: the test lists no value for the declared input <paramref name="inputId"/>.</summary>
    public static SourceRefusal InputMissing(string testId, string inputId, string path) =>
        new("S07-010", $"Test '{testId}' gives no value for input '{inputId}'. List every input; use null for an optional input left unset.", path);

    /// <summary>S07-011: the test's <c>profile</c> is not exactly the fields the workflow reads, which <paramref name="fields"/> lists (sorted; <c>none</c> when empty).</summary>
    public static SourceRefusal ProfileMismatch(string testId, IEnumerable<string> fields, string path) =>
        new("S07-011", $"Test '{testId}' must give exactly the profile fields the workflow reads: {List(fields)}.", path);

    /// <summary>S07-012: the test's <c>run</c> is not exactly the facts the workflow reads, which <paramref name="facts"/> lists (sorted; <c>none</c> when empty).</summary>
    public static SourceRefusal RunMismatch(string testId, IEnumerable<string> facts, string path) =>
        new("S07-012", $"Test '{testId}' must give exactly the run facts the workflow reads: {List(facts)}.", path);

    /// <summary>S07-013: all tests together took longer than the limit (<c>limits.testsSeconds</c>, 60; D9). Said for the package, so at <c>""</c>.</summary>
    public static SourceRefusal TestsTookTooLong() =>
        new("S07-013", $"Tests took longer than {PackageTests.LimitSeconds} seconds in all. Publish stopped.", "");

    /// <summary>S07-014: the test stubs <paramref name="rowKey"/>, which does not run.</summary>
    public static SourceRefusal StubNotUsed(string testId, string rowKey, string path) =>
        new("S07-014", $"Test '{testId}' stubs {rowKey}, which doesn't run.", path);

    /// <summary>S07-015: the compiled copy at <paramref name="path"/> is committed (<see cref="ResolveSentences.EvidenceCommitted"/>).</summary>
    public static SourceRefusal EvidenceCommitted(string path) => ResolveSentences.EvidenceCommitted(path);

    /// <summary>S07-016: said once when an import dropped a <c>resolve.json</c> or <c>lock.json</c> (<see cref="ResolveSentences.ImportIgnored"/>); null when it dropped neither.</summary>
    public static SourceRefusal? ImportIgnored(IReadOnlyList<string> dropped) => ResolveSentences.ImportIgnored(dropped);

    /// <summary>A11-004: <c>manifest.json</c>'s <c>name</c> or <c>version</c> is not text (D2).</summary>
    public static SourceRefusal ManifestNameAndVersion() =>
        new("A11-004", "manifest.json must give name and version as text.", "manifest.json");

    /// <summary>A11-005: a member of the test is not written as the grammar says (part 07 § 8.1 to § 8.3); <paramref name="what"/> is a phrase of the grammar, never a value of the file.</summary>
    public static SourceRefusal TestGrammar(string testId, string member, string what, string path) =>
        new("A11-005", $"Test '{testId}' {member} is not written as the test grammar says: {what}.", path);

    /// <summary>A11-006: <paramref name="version"/> of <paramref name="package"/> has a stamped folder already (D2).</summary>
    public static SourceRefusal AlreadyPublished(string package, string version) =>
        new("A11-006", $"Version {version} of {package} is already published. Publish a new version.", "");

    /// <summary>A11-007: the engine failed at publish (A08-006's shape: the caller logs <see cref="SourceRefusal.EngineFault"/>, the type of the exception).</summary>
    public static SourceRefusal EngineFailed(string? faultType = null) =>
        new("A11-007", "The workflow couldn't be published because the engine failed. Tell the operator.", "") { EngineFault = faultType };

    /// <summary>S10-005 (the script and the operator's tools): <paramref name="package"/> is left at version 30 for <paramref name="reason"/>.</summary>
    public static SourceRefusal StaysAt30(string package, string reason) =>
        new("S10-005", $"{package} stays at version 30 for now: {reason}. Its pin is unchanged; you can migrate it in the Editor when Milestone 41 arrives.", "");

    /// <summary>
    /// A11-015 (#1508, Task 36; comment 8, R11): the deadline the bulk publish gives the owner of a package it cannot publish
    /// at 31, beside S10-005: it keeps its 30 version and its pin, and runs at 30 until 30 is retired (#1450).
    /// </summary>
    public const string StopsWhen30Retired = "This package stops working when specVersion 30 is retired.";

    /// <summary>
    /// A11-016 (#1508, Task 36; R21): the bulk publish was asked to migrate <paramref name="package"/> for an account whose
    /// pin does not name it, or <paramref name="package"/> is not an account package (<c>acct-…</c>): only an account's active
    /// account package is published as the account's.
    /// </summary>
    public static string NotTheActivePackage(string package) =>
        $"{package} is not the account's active account package; the bulk publish migrates only the account package an account's pin names.";

    /// <summary>
    /// A11-017 (#1508, final review of Part F): the bulk publish's owners' list, for a package whose row is <c>unknown</c>:
    /// the engine did not answer the request that sent it (a fault, a timeout, a lost answer), so a <c>--publish</c> run may
    /// have published it. The operator checks its version on the engine before the list is sent.
    /// </summary>
    public const string NotAnswered = "The engine did not answer for this package; check whether it was published before sending this list.";

    private static string List(IEnumerable<string> items)
    {
        var sorted = items.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return sorted.Count == 0 ? None : string.Join(", ", sorted);
    }
}
