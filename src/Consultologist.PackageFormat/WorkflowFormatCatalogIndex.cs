using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// The format catalogue's index (package-format-v25-design.md § 3.3):
/// <c>agents/formats.json</c> in the agents registry, published immutably per
/// CalVer version. Two sections: <c>formats</c> binds each catalogue id to its
/// file and to the agent or engine operation that produces it; <c>producers</c>
/// binds a producer class that needs one agent for every format of that class
/// — today <c>custom</c>, the generic structured agent (§ 3.4). The index is
/// the binding layer; the format spec owns the vocabulary and the engine the
/// implementation.
/// </summary>
/// <param name="Version">The catalogue version, <c>vYYYY.MM.N</c>.</param>
/// <param name="Formats">Catalogue id → entry. A catalogue id is referenced by a package by id only, never with a version.</param>
/// <param name="Producers">The producer-class bindings.</param>
public sealed record WorkflowFormatCatalogIndex(
    string Version,
    Dictionary<string, WorkflowFormatCatalogEntry> Formats,
    WorkflowFormatCatalogProducers Producers);

/// <summary>
/// One catalogue format: its file, and either an agent (an attested format),
/// an engine operation (a computed one), or neither (a rendered one). Agent
/// identity is two fields wherever code reads it; <c>name@N</c> is a display form.
/// </summary>
public sealed record WorkflowFormatCatalogEntry(
    string File,
    string? AgentName = null,
    int? AgentVersion = null,
    string? Op = null)
{
    public bool NamesAgent => AgentName is not null || AgentVersion is not null;

    public bool NamesOp => Op is not null;

    /// <summary><c>name@N</c>, for sentences and chips; null when no agent is named.</summary>
    public string? AgentDisplay => AgentName is null || AgentVersion is null ? null : $"{AgentName}@{AgentVersion}";
}

/// <summary>The producer-class bindings. <c>custom</c> is required: every custom format in every package goes through it.</summary>
public sealed record WorkflowFormatCatalogProducers(
    WorkflowFormatCatalogAgentBinding? Custom);

/// <summary>
/// The generic agent behind a producer class and the trailer sentence the
/// engine appends to the rendered prompt before the schema (§ 3.4) — a
/// published constant, so the record can say what the agent was told.
/// </summary>
public sealed record WorkflowFormatCatalogAgentBinding(
    string AgentName,
    int AgentVersion,
    string Trailer)
{
    public string AgentDisplay => $"{AgentName}@{AgentVersion}";
}

/// <summary>The registry the catalogue publishes to: <c>formats/vYYYY.MM.N/formats.json</c>, files first and the index last.</summary>
public static class WorkflowFormatRegistry
{
    public const string Name = "formats";

    public const string IndexFileName = "formats.json";
}

/// <summary>
/// A catalogue id's spelling: snake_case, as every declared id (#1037, design
/// § 16 — <c>concept_list</c>, <c>terms_subset</c>). The hyphenated contract-id
/// form the catalogue published up to formats@v2026.10.1 (<c>concept-list</c>,
/// <c>terms-subset</c>) is still READ until the renamed catalogue is pinned; it
/// leaves with that re-pin. A package-local format id is snake_case too, so the
/// two namespaces are kept apart by the reservation alone (§ 3.6: a catalogue id
/// cannot be declared locally).
/// </summary>
public static class WorkflowFormatCatalogIds
{
    private static readonly Regex HyphenatedPattern = new("^[a-z][a-z0-9-]*$", RegexOptions.Compiled);

    public static bool IsValid(string? id) => id != null && (WorkflowDeclaredIds.IsValid(id) || HyphenatedPattern.IsMatch(id));
}

/// <summary>
/// The strict read of the index: every structural refusal as a sentence. The
/// rules that need the format files (attested ⇒ an agent, computed ⇒ an op,
/// rendered ⇒ neither) live in <see cref="WorkflowFormatCatalog.Build"/>, since
/// the producer is in the file, not the index.
/// </summary>
public static class WorkflowFormatCatalogIndexJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Reads <c>formats.json</c>. <paramref name="subject"/> names the index in
    /// every sentence ("Format catalogue v2026.11.1", "agents/formats.json").
    /// False with every reason added to <paramref name="errors"/>, so the agents
    /// repo's CI lists all defects at once.
    /// </summary>
    public static bool TryRead(string? json, string subject, List<string> errors, out WorkflowFormatCatalogIndex? index)
    {
        index = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add($"{subject} is empty.");
            return false;
        }

        IndexDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<IndexDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            errors.Add(string.IsNullOrWhiteSpace(ex.Path) || ex.Path == "$"
                ? $"{subject} is not valid JSON."
                : $"{subject} property '{ex.Path}' is not part of the format catalogue index.");
            return false;
        }

        if (document is null)
        {
            errors.Add($"{subject} is not valid JSON.");
            return false;
        }

        var before = errors.Count;

        if (!CalVerVersion.TryParse(document.Version, out _))
        {
            errors.Add($"{subject} must declare a CalVer version (\"vYYYY.MM.N\").");
        }

        var formats = new Dictionary<string, WorkflowFormatCatalogEntry>(StringComparer.Ordinal);

        if (document.Formats is not { Count: > 0 })
        {
            errors.Add($"{subject} declares no formats.");
        }
        else
        {
            foreach (var (id, entry) in document.Formats)
            {
                if (ReadEntry(subject, id, entry, errors) is { } read)
                {
                    formats[id] = read;
                }
            }
        }

        var producers = ReadProducers(subject, document.Producers, errors);

        if (errors.Count > before)
        {
            return false;
        }

        index = new WorkflowFormatCatalogIndex(document.Version!, formats, producers!);
        return true;
    }

    private static WorkflowFormatCatalogEntry? ReadEntry(string subject, string id, EntryDocument? entry, List<string> errors)
    {
        var prefix = $"{subject} format '{id}'";
        var ok = true;

        if (!WorkflowFormatCatalogIds.IsValid(id))
        {
            errors.Add($"{prefix} id must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
            ok = false;
        }

        if (string.Equals(id, WorkflowFormatProducers.Custom, StringComparison.Ordinal))
        {
            errors.Add($"{prefix} is a producer binding, never a format entry; a custom format is a package's own.");
            ok = false;
        }

        if (entry is null)
        {
            errors.Add($"{prefix} must be an object with a file.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry.File))
        {
            errors.Add($"{prefix} must declare a file.");
            ok = false;
        }
        else if (entry.File.StartsWith('/') || entry.File.Contains("..", StringComparison.Ordinal) || entry.File.Contains('\\'))
        {
            errors.Add($"{prefix} file '{entry.File}' must be a relative path within the catalogue.");
            ok = false;
        }

        var hasName = entry.AgentName is not null;
        var hasVersion = entry.AgentVersion is not null;

        if (hasName != hasVersion)
        {
            errors.Add($"{prefix} must declare agentName and agentVersion together.");
            ok = false;
        }

        if (hasName && string.IsNullOrWhiteSpace(entry.AgentName))
        {
            errors.Add($"{prefix} agentName must not be blank.");
            ok = false;
        }

        if (hasVersion && entry.AgentVersion <= 0)
        {
            errors.Add($"{prefix} agentVersion must be a positive integer.");
            ok = false;
        }

        if (entry.Op is not null && string.IsNullOrWhiteSpace(entry.Op))
        {
            errors.Add($"{prefix} op must not be blank.");
            ok = false;
        }

        if ((hasName || hasVersion) && entry.Op is not null)
        {
            errors.Add($"{prefix} names both an agent and an op; an attested format has an agent, a computed one an op.");
            ok = false;
        }

        return ok ? new WorkflowFormatCatalogEntry(entry.File!, entry.AgentName, entry.AgentVersion, entry.Op) : null;
    }

    private static WorkflowFormatCatalogProducers? ReadProducers(string subject, ProducersDocument? producers, List<string> errors)
    {
        if (producers?.Custom is null)
        {
            errors.Add($"{subject} must declare producers.custom (the generic agent every custom format goes through, with its trailer).");
            return null;
        }

        var custom = producers.Custom;
        var ok = true;

        if (string.IsNullOrWhiteSpace(custom.AgentName))
        {
            errors.Add($"{subject} producers.custom must declare agentName.");
            ok = false;
        }

        if (custom.AgentVersion is null or <= 0)
        {
            errors.Add($"{subject} producers.custom agentVersion must be a positive integer.");
            ok = false;
        }

        if (string.IsNullOrWhiteSpace(custom.Trailer))
        {
            errors.Add($"{subject} producers.custom must declare the trailer sentence the engine appends before the schema.");
            ok = false;
        }

        return ok
            ? new WorkflowFormatCatalogProducers(new WorkflowFormatCatalogAgentBinding(custom.AgentName!, custom.AgentVersion!.Value, custom.Trailer!))
            : null;
    }

    private sealed record IndexDocument(
        string? Version,
        Dictionary<string, EntryDocument?>? Formats,
        ProducersDocument? Producers);

    private sealed record EntryDocument(
        string? File,
        string? AgentName,
        int? AgentVersion,
        string? Op);

    private sealed record ProducersDocument(BindingDocument? Custom);

    private sealed record BindingDocument(
        string? AgentName,
        int? AgentVersion,
        string? Trailer);
}
