using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 1.2 the optional-macro row, 1.3, 1.4; part 05 §§ 8.1 to 8.4, 8.6; part 02
/// § 10; D3, D4, D5; R5, R7): the macros pass. Each 30 macro becomes <c>macros/&lt;id&gt;.md</c> (a folder
/// <c>macros/&lt;folder&gt;/index.json</c> and one file per item), with <c>label</c>, <c>description: null</c>, its
/// <c>if</c>, its <c>format</c> and its <c>instruction</c> lines, its body as 30 wrote it (31 drops one final line ending
/// as it renders, R5). A macro D4 makes fixed text becomes a snippet instead (<see cref="Renames.BecomesSnippet"/>). An
/// optional macro is offered by an optional boolean input its <c>if</c> reads (§ 1.3). A node instance (<c>before</c>,
/// <c>after</c>) is placed at the end of the node's prompt, its instruction left for the operator (D3, A10-003).
/// </summary>
internal static partial class Migrate
{
    /// <summary>The macros pass, after the templates pass (it reads <see cref="Migrated.MacroBodies"/>, the placements and their rules).</summary>
    public static void Macros(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);

        // A macro whose prompt placements already disagree was said by the templates pass (A10-036); it has no rule.
        var disagreeing = m.MacroPlacements.Where(p => p.Rule is not null).Select(p => p.Macro)
            .Where(macro => !m.PlacementRules.ContainsKey(macro)).ToHashSet(StringComparer.Ordinal);

        var byTheEngine = NodeInstances(m, tree);
        PlacementConditions(m, byTheEngine.Where(macro => !disagreeing.Contains(macro)).ToHashSet(StringComparer.Ordinal));
        disagreeing.UnionWith(m.MacroPlacements.Where(p => p.Rule is not null).Select(p => p.Macro)
            .Where(macro => !m.PlacementRules.ContainsKey(macro)));

        foreach (var macro in m.Manifest30.Macros ?? [])
        {
            if (macro.File.EndsWith('/'))
            {
                Folder(m, tree, macro, byTheEngine, disagreeing);
                continue;
            }

            if (Renames.BecomesSnippet(m, macro.Id))
            {
                AsSnippet(m, macro);
                continue;
            }

            var header = m.Resolved30.Headers.GetValueOrDefault(macro.File);
            m.Put(macro.File, MacroHeader(m, tree, macro.Id, macro.File, macro.Label ?? header?.Label, header, macro.Optional, macro.Default, macro.Instruction, byTheEngine, disagreeing),
                MacroBody(m, macro.File));
            m.MacroBodyFiles.Add(macro.File);
            m.Items("macros").Add(macro.Id);
        }
    }

    /// <summary>
    /// D3 (part 05 § 10): each 30 node instance (<c>macros: [{ id, at, if }]</c>, placed <c>before</c> or <c>after</c> the
    /// reply by the engine) becomes the placement <c>{{ macro.&lt;id&gt; }}</c> on its own line at the end of the node's
    /// prompt (unless the prompt already places it: a template places a macro once), its <c>if</c> that placement's rule
    /// (converted at the node's place, and counted even where the prompt's own placement stands for it, so a condition that
    /// differs is said, never dropped), and one hand edit on the macro's <c>instruction</c> per macro, naming the sides
    /// (A10-003), which is written <c>[]</c>. The macros placed so are returned.
    /// </summary>
    private static HashSet<string> NodeInstances(Migrated m, NodeTree tree)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var sides = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);   // macro → its sides, in 30's order of first instance
        var order = new List<string>();
        foreach (var (id, node) in tree.ById)
        {
            foreach (var instance in node.Macros ?? [])
            {
                if (instance is null)
                    continue;
                var macroFile = MacroFileOf(m, instance.Id);
                var where = instance.IsAfter ? WorkflowNodeMacroSpec.After : WorkflowNodeMacroSpec.Before;
                placed.Add(instance.Id);
                if (!sides.TryGetValue(instance.Id, out var side))
                {
                    sides[instance.Id] = side = new SortedSet<string>(StringComparer.Ordinal);
                    order.Add(instance.Id);
                }

                side.Add(where);

                var file = $"{FolderOf(tree, id)}/{id}.md";
                if (!m.Sources.TryGetValue(file, out var prompt) || prompt.Body is not { } body || (string?)prompt.Json["kind"] != "agent")
                    continue;

                var condition = instance.If ?? instance.When;
                var rule = condition is null
                    ? null
                    : new PlacementRule(Condition(m, macroFile, "if", condition, m.Scope(id), ConversionKind.Condition), condition);
                if (!m.MacroPlacements.Any(p => p.Macro == instance.Id && p.File == file))
                    prompt.Body = (body.Length == 0 || body.EndsWith('\n') ? body : body + "\n") + $"{{{{ macro.{instance.Id.Replace('/', '.')} }}}}\n";
                m.MacroPlacements.Add(new MacroPlacement(instance.Id, id, file, rule));
            }
        }

        // One instruction to write per macro: one notice, naming each side it was placed on.
        foreach (var macro in order)
        {
            var where = sides[macro].Count == 2 ? $"{WorkflowNodeMacroSpec.Before} and {WorkflowNodeMacroSpec.After}" : sides[macro].Single();
            m.HandEdit(MacroFileOf(m, macro), "instruction", MigrationSentences.PlacedByTheEngine(macro, where));
        }

        return placed;
    }

    /// <summary>
    /// A macro folder (part 05 § 8.1): <c>macros/&lt;folder&gt;/index.json</c> (30's label as its description, its items in
    /// 30's order, <c>if: null</c>: 30 had no folder gate) and one macro file per item, keyed <c>&lt;folder&gt;/&lt;item&gt;</c>.
    /// </summary>
    private static void Folder(Migrated m, NodeTree tree, WorkflowMacroSpec folder, HashSet<string> byTheEngine, HashSet<string> disagreeing)
    {
        var index = m.Files30.TryGetValue(folder.File + WorkflowDataResolver.IndexFileName, out var indexJson)
            ? WorkflowTextHeaders.ReadIndex(indexJson, folder.Id, "Macro", m.Manifest30.SpecVersion, [])
            : null;
        m.Put($"macros/{folder.Id}/{WorkflowDataResolver.IndexFileName}", new JsonObject
        {
            ["description"] = index?.Label is { } label ? new JsonArray(label) : null,
            ["items"] = new JsonArray(),
            ["if"] = null,
        });
        m.Items("macros").Add(folder.Id);

        foreach (var (item, file) in index?.Items ?? [])
        {
            var path30 = folder.File + file;
            var written = $"macros/{folder.Id}/{item}.md";
            var header = m.Resolved30.Headers.GetValueOrDefault(path30);
            m.Put(written, MacroHeader(m, tree, $"{folder.Id}/{item}", written, header?.Label, header, null, null, null, byTheEngine, disagreeing), MacroBody(m, path30));
            m.MacroBodyFiles.Add(written);
            m.Items($"macros/{folder.Id}").Add(item);
        }
    }

    /// <summary>
    /// #1507 (Task 15 fix round 1; D4, R5): the body of a snippet written from a 30 macro. 30 placed a deliverable macro with
    /// all its trailing white space trimmed (<c>ConsultMacroExpander</c>'s <c>TrimEnd()</c>), and a snippet is read whole at
    /// 31 (part 05 § 8.6 drops a final line ending from a macro's body only), so a snippet only deliverables read is written
    /// with that white space taken off, and keeps 30's bytes. One a 30 prompt also read as <c>.content</c> (which 30 read with
    /// its file's trailing line ending) is written whole: the two reads cannot both keep 30's bytes, and the comparison says so.
    /// </summary>
    internal static string SnippetBody(Migrated m, WorkflowMacroSpec macro, bool readAsContent)
    {
        var body = MacroBody(m, macro.File);
        return readAsContent ? body : body.TrimEnd();
    }

    /// <summary>
    /// D4 (part 05 § 10): a macro that is fixed text (no condition anywhere, placed only bare on deliverables or read as
    /// <c>.content</c>) is the single snippet <c>snippets/&lt;id&gt;.md</c>: its label, <c>description: null</c>,
    /// <c>if: null</c>, <c>format: "markdown_block"</c> (as the layout writes every 30 snippet) and its body. The templates
    /// pass already reads it as <c>snippet.&lt;id&gt;.content</c>; the deliverables pass places it.
    /// </summary>
    private static void AsSnippet(Migrated m, WorkflowMacroSpec macro)
    {
        m.Put($"snippets/{macro.Id}.md", new JsonObject
        {
            ["label"] = macro.Label ?? m.Resolved30.Headers.GetValueOrDefault(macro.File)?.Label ?? macro.Id,
            ["description"] = null,
            ["if"] = null,
            ["format"] = "markdown_block",
        }, SnippetBody(m, macro, readAsContent: m.Walk30.Templates.Any(t => t.References.MacroContents.Contains(macro.Id))));
        m.MacroBodyFiles.Add($"snippets/{macro.Id}.md");
        m.Items("snippets").Add(macro.Id);
    }

    /// <summary>
    /// Part 05 § 8.1: a macro's header. <paramref name="key"/> is its id (<c>&lt;folder&gt;/&lt;item&gt;</c> for a folder's),
    /// <paramref name="file"/> its 31 file; the 30 facts are the manifest's (<paramref name="optional"/>,
    /// <paramref name="defaultOn"/>, <paramref name="instruction"/>) or else its header's. #1508 (comment 6, item 1; R13): a
    /// macro 30's model chose (<see cref="ModelChosen"/>) gets a <c>judgement</c> node per agent that places it
    /// (<see cref="ChoiceClassifiers"/>), its <c>if</c> the macro's offer condition, and the macro's <c>if</c> gains each
    /// one's answer, joined by <c>and</c> after the rest; with more than one, its <c>if</c> is a hand edit (A10-078).
    /// </summary>
    private static JsonObject MacroHeader(
        Migrated m, NodeTree tree, string key, string file, string? label, WorkflowTextHeader? header, bool? optional, bool? defaultOn, string? instruction,
        HashSet<string> byTheEngine, HashSet<string> disagreeing)
    {
        var name = label ?? key;
        var choice = (optional ?? header?.Optional) == true ? ChoiceInput(m, key, name) : null;
        var gate = MacroIf(m, key, file, header?.If, choice, (defaultOn ?? header?.Default) == true, disagreeing);
        if (choice is not null && ReadsAnotherBoolean(m, gate, choice))
        {
            // Final review A3 (R7): a MacroChoices entry maps to the one boolean input the macro's if reads; this if
            // reads another too, so the 31 start refuses the entry (A10-044). The package publishes: a notice.
            var notice = MigrationSentences.ChoiceRefusedAtStart(key, choice);
            m.Notice(notice);
            m.MacroInputs.Add(notice);
        }

        var instruction30 = instruction ?? header?.Instruction;
        if (ModelChosen(key, instruction30, byTheEngine, disagreeing))
        {
            var classifiers = ChoiceClassifiers(m, tree, key, name, gate);
            var answers = classifiers.Select(classifier => (JsonNode)new JsonObject
            {
                ["==="] = new JsonArray(new JsonObject { ["var"] = $"{classifier}.value.answer" }, true),
            }).ToList();

            // Fix round 1 (A10-078): two placing nodes' answers joined by and is a condition 30 never had.
            if (classifiers.Count > 1)
                m.HandEdit(file, "if", MigrationSentences.SplitPerPlacer(key, [.. classifiers.Select(c => c["node.".Length..])]));
            if (answers.Count > 0)
            {
                List<JsonNode> parts = gate switch
                {
                    null => [],
                    JsonObject { Count: 1 } all when all["and"] is JsonArray joined => [.. joined.Select(p => p!.DeepClone())],
                    _ => [gate],
                };
                parts.AddRange(answers);
                gate = parts.Count == 1 ? parts[0] : new JsonObject { ["and"] = new JsonArray([.. parts]) };
            }
        }

        return new JsonObject
        {
            ["label"] = name,
            ["description"] = null,
            ["if"] = gate,
            ["format"] = MacroFormat(m, key),
            ["instruction"] = byTheEngine.Contains(key) ? new JsonArray() : Instruction(m, key, file, instruction30),
        };
    }

    /// <summary>
    /// Part 05 § 8.2, part 10 § 1.3: the macro's <c>if</c>: its own 30 <c>if</c> converted (at the top level, where 30 read
    /// it), then the condition its placements all carry (<see cref="Migrated.PlacementRules"/>), then the choice input's test
    /// (<c>{ "===": [ input, true ] }</c> for a default off, <c>{ "!==": [ input, false ] }</c> for a default on), joined
    /// with <c>and</c> in that order; <c>null</c> when there is none. When its placements disagree (A10-036, said), the
    /// conditions are never joined: the <c>if</c> is written as a JSON string naming them, which the checker refuses (D3).
    /// </summary>
    private static JsonNode? MacroIf(Migrated m, string key, string file, string? own30, string? choice, bool defaultOn, HashSet<string> disagreeing)
    {
        JsonNode? test = choice is null
            ? null
            : new JsonObject
            {
                [defaultOn ? "!==" : "==="] = new JsonArray(new JsonObject { ["var"] = $"input.{choice}.value" }, JsonValue.Create(!defaultOn)),
            };

        if (disagreeing.Contains(key))
        {
            var placed = m.MacroPlacements.Where(p => p.Macro == key)
                .Select(p => $"{p.File}: {p.Rule?.Text30 ?? "always"}").Distinct(StringComparer.Ordinal);
            var pieces = new List<string>();
            if (own30 is not null)
                pieces.Add(own30);
            pieces.Add("placed under one of " + string.Join("; ", placed));
            if (test is not null)
                pieces.Add(CanonicalJson.Write(test));
            return JsonValue.Create(string.Join(" && ", pieces));
        }

        var parts = new List<JsonNode>();
        if (own30 is not null)
            parts.Add(Condition(m, file, "if", own30, m.Scope((string?)null), ConversionKind.Condition));
        if (m.PlacementRules.TryGetValue(key, out var rule))
            parts.Add(rule.Logic.DeepClone());
        if (test is not null)
            parts.Add(test);
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new JsonObject { ["and"] = new JsonArray([.. parts]) },
        };
    }

    /// <summary>
    /// Whether the macro's converted <paramref name="gate"/> reads a boolean input of the 31 manifest other than its
    /// <paramref name="choice"/> input, as <see cref="Run.RunInputsBuilder.Choices"/> counts them (R7). A gate written as a
    /// JSON string (A10-036, a hand edit) reads none.
    /// </summary>
    private static bool ReadsAnotherBoolean(Migrated m, JsonNode? gate, string choice) =>
        gate is JsonObject
        && JsonLogicShape.Paths(gate)
            .Select(path => path.Split('.'))
            .Any(path => path is ["input", var input, ..] && input != choice
                && m.Manifest["inputs"]?[input]?["format"] is JsonValue format && format.TryGetValue<string>(out var f) && f == "boolean");

    /// <summary>
    /// Part 10 § 1.3, part 02 § 10: the optional boolean input that offers the optional macro <paramref name="key"/>,
    /// added to the manifest (<c>label</c> the macro's, <c>description: null</c>, <c>format: "boolean"</c>,
    /// <c>required: false</c>, <c>channel: null</c>), and its id: the macro's (a folder item's own id), or, when an input
    /// already has it, with <c>_macro</c> (A10-007 names it either way, and a suffixed one is in the report's macro
    /// inputs); when that is taken too, the first free <c>_2</c>, <c>_3</c>, …, said first as the renames say it (S10-006),
    /// then with A10-007, both in the report's macro inputs.
    /// </summary>
    private static string ChoiceInput(Migrated m, string key, string label)
    {
        if (m.Manifest["inputs"] is not JsonObject inputs)
            m.Manifest["inputs"] = inputs = new JsonObject();
        var id = key[(key.IndexOf('/', StringComparison.Ordinal) + 1)..];
        string chosen;
        string notice;
        if (!inputs.ContainsKey(id))
        {
            chosen = id;
            notice = MigrationSentences.OfferedByInput(key, chosen);
        }
        else
        {
            var suffixed = Renames.Fit(id, "_macro");
            chosen = suffixed;
            for (var n = 2; inputs.ContainsKey(chosen); n++)
                chosen = Renames.Fit(id, string.Create(CultureInfo.InvariantCulture, $"_macro_{n}"));
            if (chosen != suffixed)
            {
                var renamed = MigrationSentences.RenameTaken(id, chosen, suffixed);
                m.Notice(renamed);
                m.MacroInputs.Add(renamed);
            }

            notice = MigrationSentences.OfferedByInput(key, chosen);
        }

        inputs[chosen] = new JsonObject
        {
            ["label"] = label,
            ["description"] = null,
            ["format"] = "boolean",
            ["required"] = false,
            ["channel"] = null,
        };
        m.Notice(notice);
        if (chosen != id)
            m.MacroInputs.Add(notice);
        return chosen;
    }

    /// <summary>
    /// Part 05 § 8.3's format match: the text format of the agents that place the macro (its body is inserted verbatim, so
    /// it takes theirs), each placement through a snippet body counted as the agents whose 30 prompts place that snippet
    /// (or its folder): <c>markdown_inline</c> when one is inline (a block macro is refused there), <c>markdown_block</c>
    /// when every one is, else <c>plaintext</c> (30's prose nodes are plain text unless a deliverable composes them,
    /// R9; a plain macro in a Markdown node is escaped). A snippet placed only by another snippet reaches no agent here:
    /// 30's agents are all prose, so that leaves the format they would give.
    /// </summary>
    private static string MacroFormat(Migrated m, string key)
    {
        var files = new List<string>();
        foreach (var placement in m.MacroPlacements.Where(p => p.Macro == key))
        {
            if (placement.Owner is not null)
                files.Add(placement.File);
            else if (SnippetOf(placement.File) is { } snippet)
                files.AddRange(PlacersOf(m, snippet));
        }

        var formats = files.Select(f => m.Sources.TryGetValue(f, out var node) ? (string?)node.Json["format"] : null).ToList();
        if (formats.Contains("markdown_inline"))
            return "markdown_inline";
        return formats.Count > 0 && formats.All(f => f == "markdown_block") ? "markdown_block" : "plaintext";
    }

    /// <summary>The snippet (or snippet folder) of a 31 snippet file: <c>snippets/&lt;id&gt;.md</c> or <c>snippets/&lt;folder&gt;/&lt;item&gt;.md</c>.</summary>
    private static string? SnippetOf(string file) =>
        file.StartsWith("snippets/", StringComparison.Ordinal) ? file["snippets/".Length..].Split('/')[0].Replace(".md", "", StringComparison.Ordinal) : null;

    /// <summary>The 31 files of the agents whose 30 prompts place snippet (or snippet folder) <paramref name="snippet"/>.</summary>
    private static IEnumerable<string> PlacersOf(Migrated m, string snippet) =>
        m.Walk30.Templates
            .Where(t => t.Kind == TemplateKind.Prompt && (t.References.Snippets.Contains(snippet) || t.References.SnippetFolders.Contains(snippet)))
            .Where(t => m.Keys.TryGetValue(t.Id, out var rooted) && rooted.StartsWith("node.", StringComparison.Ordinal))
            .Select(t => "nodes/" + m.Keys[t.Id]["node.".Length..].Replace('.', '/') + ".md");

    /// <summary>
    /// D5 (part 05 §§ 8.1, 8.4; 30's <see cref="WorkflowMacroMechanics.Prompt"/>): the instruction as lines: 30's instruction
    /// (its end trimmed, as 30 trims it) split on <c>\n</c>, an empty line, then 30's mechanics sentence naming the macro's
    /// marker (<c>{{ macro.&lt;id&gt;.content }}</c> inside a line, the inline form), so the rendered instruction has the
    /// bytes 30's <c>.prompt</c> had. A macro with no 30 instruction has the sentence alone. One whose 30 instruction is a
    /// template (it holds a tag, rendered before the model saw it: <c>{{ "" }}</c>, another macro's text) is a hand edit
    /// (A10-042), written as 30's lines alone, which the checker refuses since they hold no marker of the macro's own
    /// (S05-044); when they do hold it (30 rendered the macro's text there), the sentence is added too, so the marker is
    /// there twice and still refused.
    /// </summary>
    private static JsonArray Instruction(Migrated m, string key, string file, string? instruction30)
    {
        var marker = $"macro.{key.Replace('/', '.')}.content";
        var sentence = WorkflowMacroMechanics.Sentence.Replace("<id>", key.Replace('/', '.'), StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(instruction30))
            return new JsonArray(sentence);
        var lines = new JsonArray();
        foreach (var line in instruction30.TrimEnd().Split('\n'))
            lines.Add(line);
        var template = instruction30.Contains("{{", StringComparison.Ordinal);
        if (template)
            m.HandEdit(file, "instruction", MigrationSentences.InstructionIsATemplate);
        if (!template || instruction30.Contains(marker, StringComparison.Ordinal))
        {
            lines.Add(string.Empty);
            lines.Add(sentence);
        }

        return lines;
    }

    /// <summary>A macro's body in the 31 dialect (<see cref="Migrated.MacroBodies"/>), its final line ending as 30 had it (R5: 31 drops one as it renders).</summary>
    private static string MacroBody(Migrated m, string path30) =>
        m.MacroBodies.GetValueOrDefault(path30) ?? m.Resolved30.Files.GetValueOrDefault(path30) ?? string.Empty;

    /// <summary>The 31 file of macro <paramref name="key"/> (<c>&lt;id&gt;</c> or <c>&lt;folder&gt;/&lt;id&gt;</c>), the same as 30's.</summary>
    private static string MacroFileOf(Migrated m, string key) =>
        key.Contains('/', StringComparison.Ordinal)
            ? $"macros/{key}.md"
            : m.Manifest30.Macros?.FirstOrDefault(x => x.Id == key && !x.File.EndsWith('/'))?.File ?? $"macros/{key}.md";
}
