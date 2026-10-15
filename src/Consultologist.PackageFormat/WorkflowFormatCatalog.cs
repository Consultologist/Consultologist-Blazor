namespace Consultologist.PackageFormat;

/// <summary>
/// The format catalogue at one version: the index bound to its files
/// (package-format-v25-design.md § 3.3). Built, never loaded, here — the
/// engine and the agents repo's CI hand in the index and a file reader, and
/// get back every cross-check the index alone cannot make, because the
/// producer is in the file: an attested format names an agent, a computed one
/// an operation the engine publishes, a rendered one neither, and a custom
/// one is never a catalogue entry. A parameter's <c>format</c> must resolve in
/// the same index — the stranding check widened from "every published schema
/// still resolves" to "every published format reference still resolves with
/// the same parameters".
/// </summary>
public sealed record WorkflowFormatCatalog(
    string Version,
    IReadOnlyDictionary<string, WorkflowFormatCatalogFormat> Formats,
    WorkflowFormatCatalogAgentBinding CustomProducer)
{
    /// <summary><c>formats@vYYYY.MM.N</c> — the display form of the pin, as <c>output-contracts@v…</c> is today.</summary>
    public string ResolvedRef => $"{WorkflowFormatRegistry.Name}@{Version}";

    public bool TryGet(string id, out WorkflowFormatCatalogFormat? format) =>
        Formats.TryGetValue(id, out format);

    /// <summary>
    /// Binds <paramref name="index"/> to its files. <paramref name="readFile"/>
    /// takes the entry's relative path and returns the file's text, or null when
    /// it is missing. Null with the reasons added to <paramref name="errors"/>
    /// when anything is wrong; the sentences are the same for the engine's
    /// start-up refusal and the agents repo's CI listing.
    /// </summary>
    public static WorkflowFormatCatalog? Build(
        WorkflowFormatCatalogIndex index,
        Func<string, string?> readFile,
        List<string> errors)
    {
        var before = errors.Count;
        var subject = $"Format catalogue {index.Version}";
        var formats = new Dictionary<string, WorkflowFormatCatalogFormat>(StringComparer.Ordinal);

        foreach (var (id, entry) in index.Formats)
        {
            var formatSubject = $"{subject} format '{id}'";
            var json = readFile(entry.File);

            if (json is null)
            {
                errors.Add($"{formatSubject} file '{entry.File}' is missing.");
                continue;
            }

            if (!WorkflowFormatSpecJson.TryRead(json, formatSubject, errors, out var spec))
            {
                continue;
            }

            var fileErrors = errors.Count;
            WorkflowFormatValidator.Validate(formatSubject, spec!, WorkflowFormatOrigin.Catalogue, errors);

            if (errors.Count > fileErrors)
            {
                continue;
            }

            ValidateBinding(formatSubject, spec!, entry, errors);

            formats[id] = new WorkflowFormatCatalogFormat(id, spec!, WorkflowFormatSpecJson.CanonicalJson(json), entry);
        }

        // Parameter format references resolve within the index, whether or
        // not the referenced file read — a missing file has its own sentence.
        foreach (var (id, format) in formats)
        {
            foreach (var (name, parameter) in format.Spec.Parameters ?? [])
            {
                if (parameter.Format is { } reference && !index.Formats.ContainsKey(reference))
                {
                    errors.Add($"{subject} format '{id}' parameter '{name}' names format '{reference}', which the catalogue does not publish.");
                }
            }
        }

        if (errors.Count > before)
        {
            return null;
        }

        return new WorkflowFormatCatalog(index.Version, formats, index.Producers.Custom!);
    }

    private static void ValidateBinding(string subject, WorkflowFormatSpec spec, WorkflowFormatCatalogEntry entry, List<string> errors)
    {
        switch (spec.Producer)
        {
            case WorkflowFormatProducers.Attested:
                if (!entry.NamesAgent)
                {
                    errors.Add($"{subject} is attested and its index entry must name the agent (agentName and agentVersion).");
                }

                if (entry.NamesOp)
                {
                    errors.Add($"{subject} is attested and its index entry must not name an op.");
                }

                break;

            case WorkflowFormatProducers.Computed:
                if (!entry.NamesOp)
                {
                    errors.Add($"{subject} is computed and its index entry must name the op (one of {string.Join(", ", WorkflowCheckOps.AllAt25)}).");
                }
                // #1037: the 25 spelling; the hyphenated one the catalogue published up to
                // formats@v2026.10.1 is read until the renamed catalogue is pinned.
                else if (!WorkflowCheckOps.AllAt25.Contains(entry.Op!, StringComparer.Ordinal) && !WorkflowCheckOps.All.Contains(entry.Op!, StringComparer.Ordinal))
                {
                    errors.Add($"{subject} names op '{entry.Op}', which the engine does not publish (one of {string.Join(", ", WorkflowCheckOps.AllAt25)}).");
                }

                if (entry.NamesAgent)
                {
                    errors.Add($"{subject} is computed and its index entry must not name an agent.");
                }

                break;

            case WorkflowFormatProducers.Rendered:
                if (entry.NamesAgent || entry.NamesOp)
                {
                    errors.Add($"{subject} is rendered and its index entry must name neither an agent nor an op.");
                }

                break;

            case WorkflowFormatProducers.Custom:
                errors.Add($"{subject} declares producer custom, which is never a catalogue entry; a custom format is a package's own, served through producers.custom.");
                break;
        }
    }
}

/// <summary>
/// One catalogue format, bound: the file's spec, its canonical JSON (the bytes
/// behind <c>formatHash</c>), and the index entry that names its agent or op.
/// </summary>
public sealed record WorkflowFormatCatalogFormat(
    string Id,
    WorkflowFormatSpec Spec,
    string CanonicalJson,
    WorkflowFormatCatalogEntry Entry);
