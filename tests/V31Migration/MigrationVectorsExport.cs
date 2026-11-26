using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 5, "Migration vectors"): the cases of the <c>migration-vectors</c> family and
/// their export into the content repository named by <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites
/// <c>v31/conformance/migration-vectors/</c>, one folder per case (<see cref="Files"/>: the 30 package under
/// <c>source/</c> byte for byte, <c>options.json</c>, the 31 package the migration writes under <c>expected/</c>,
/// <c>notices.json</c> and <c>about.txt</c>), then sets the family's <c>cases</c> in <c>v31/conformance/index.json</c> to
/// the case names, leaving every other byte of that file as it was. A no-op without the variable, as
/// <see cref="RefusalPackagesExport"/> is: it is a generator, not a check.
/// <see cref="MigrationVectorsTests.ThePinnedVectors_AreWhatMigrateGives"/> holds the pin to it.
/// </summary>
[Collection("V31 conformance export")] // #1505: the exporters each rewrite v31/conformance/index.json, so they run one at a time.
public class MigrationVectorsExport
{
    /// <summary>The family's id, and its folder's name under <c>v31/conformance/</c>.</summary>
    public const string Family = "migration-vectors";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// A case: its folder's <paramref name="Name"/>, the in-repo <paramref name="Package"/> it exports (a folder of
    /// <c>vendored/</c> or <c>fixture-packages/</c>; <c>null</c> for <see cref="HandBuilt"/>), and what it shows, for
    /// <c>about.txt</c>.
    /// </summary>
    internal sealed record VectorCase(string Name, string? Package, string About);

    /// <summary>The name of the case whose package is <see cref="HandBuilt"/>.</summary>
    internal const string HandBuiltName = "hand-built";

    /// <summary>The cases, in the family's order (ordinal by name).</summary>
    internal static IReadOnlyList<VectorCase> Cases() =>
    [
        new("composer-gates", "composer-gates",
            "Area 01's fixture composer-gates: two deliverables composed from the same nodes, a deliverable's if converted "
            + "to JSON Logic, two terms_subset nodes that gate a deliverable each, written as checks with their failWith "
            + "sentence, and prompts that read a deliverable's composed text, which a 31 prompt does not read: hand edits."),
        new("composer-macros", "composer-macros",
            "Area 01's fixture composer-macros: the deliverable composer's macros, one deliverable per rule (prepend, append, "
            + "both, two at one place), a macro's own if and a placement's if, optional macros with their defaults (the "
            + "boolean inputs that offer them), the profile and the run date, node macros around a loop's item, signed "
            + "deliverables, and placed macros reading a node, a macro and a deliverable; the hand edits those that 31 "
            + "cannot place need."),
        new("composer-sources", "composer-sources",
            "Area 01's fixture composer-sources: one deliverable per kind of source it composes (one node, two nodes, a for "
            + "over an input with an if inside, a for over a node's list with a for inside, a while, an if, a case and a let "
            + "of type text), each written as the template 31 composes; the free-text fields 31 suggests a text format for; "
            + "and a let whose expression the converter refuses (string.append), a hand edit."),
        new("example-conditional-sections", "example-conditional-sections",
            "The vendored example-conditional-sections: a case on each section's kind inside a for over sections named by "
            + "their title (labelField), an if with no else inside a for, and an if over a snippet folder's items; no hand "
            + "edit, so the migration writes its snapshot test."),
        new("example-macros", "example-macros",
            "The vendored example-macros: macros, a macro folder and an optional macro with a default; a loop over the macro "
            + "folder unrolled into one placement per macro, a body if around a placement hoisted into the placement's "
            + "header if, and the hand edits a macro 31 cannot place needs (placed by the engine before or after the reply, "
            + "an instruction that is a template, placed on a deliverable under a condition)."),
        new("example-nested-note", "example-nested-note",
            "The vendored example-nested-note: the hand-chosen rename of the reserved id parts to sections, a deliverable "
            + "whose hand-chosen whenEmpty is fail, a loop with an inner loop and a while, a let computing a typed value, "
            + "and the formats 31 writes for its nodes, with the free-text fields it suggests a text format for; no hand "
            + "edit, so the migration writes its snapshot test."),
        new(HandBuiltName, null,
            "A package written for this case: an optional macro (leaflet, default off) whose id an input already has, so "
            + "the boolean input that offers it takes the suffix _macro; an input that takes one of two types, a hand edit; "
            + "and a deliverable's if the converter refuses (string.upcase has no JSON Logic form), kept as 30's text with a "
            + "hand edit."),
        new("test-general", "test/general",
            "The vendored test/general: a snippet folder whose items' format is chosen by hand (standards takes "
            + "standard_section) and the loop over it, prompts that pipe into the concept renderers, the hand-chosen "
            + "whenEmpty hold, a macro appended on the deliverable whose id test is reserved at 31 and so renamed test_snippet "
            + "by the migration itself (A10-006; options.json chooses no rename), and a macro placed in a snippet folder's "
            + "item, a hand edit."),
    ];

    /// <summary>
    /// The package of the case <see cref="HandBuiltName"/>, at specVersion 30: invented content, as small as shows its
    /// three constructs (see its <see cref="VectorCase.About"/>) with the loop every 30 deliverable needs.
    /// <c>manifest.json</c> and every other file, by path.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> HandBuilt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["manifest.json"] = """
            {
              "name": "migration-vector",
              "version": "v2026.11.1",
              "specVersion": 30,
              "derivedFrom": null,
              "title": "Migration vector",
              "description": "A migration vector (#1507): a macro choice whose id an input already has, an input of two types, and a deliverable's condition the converter refuses. Fictional; not intended for clinical use.",
              "tags": [
                "fixture"
              ],
              "templating": {
                "engine": "scriban",
                "engineVersion": "7.2.5"
              },
              "inputs": [
                {
                  "id": "consult_draft",
                  "label": "Consult draft",
                  "required": true
                },
                {
                  "id": "problems",
                  "label": "Problem list",
                  "required": true,
                  "type": "array",
                  "items": "text"
                },
                {
                  "id": "leaflet",
                  "label": "Leaflet title",
                  "required": false
                },
                {
                  "id": "seen_on",
                  "label": "Seen on",
                  "required": false,
                  "type": [
                    "date",
                    "text"
                  ]
                }
              ],
              "macros": [
                "leaflet"
              ],
              "nodes": [
                {
                  "id": "problem_loop",
                  "label": "Problems",
                  "for": "item in input.problems",
                  "nodes": [
                    "problem_line"
                  ],
                  "ret": "node.problem_line"
                },
                {
                  "id": "problem_line",
                  "label": "Writing a problem's line",
                  "format": "prose"
                },
                {
                  "id": "letter",
                  "label": "Drafting the letter",
                  "format": "prose"
                }
              ],
              "deliverables": [
                {
                  "id": "clinic_letter",
                  "label": "Clinic letter",
                  "sources": [
                    "node.problem_loop",
                    "node.letter"
                  ]
                },
                {
                  "id": "urgent_copy",
                  "label": "Urgent copy",
                  "sources": [
                    "node.problem_loop"
                  ],
                  "if": "(input.consult_draft | string.upcase) == \"URGENT\""
                }
              ]
            }

            """,
        ["macros/leaflet.md"] = """
            {
              "label": "Leaflet given",
              "instruction": "The leaflet line goes after the letter's last paragraph.",
              "optional": true,
              "default": false
            }
            Margaret Okafor was given the clinic's leaflet on her medicine.

            """,
        ["prompts/problem_line.md"] = """
            Write one line of the clinic letter about this problem.
            {{ item.value }}

            """,
        ["prompts/letter.md"] = """
            Write a short clinic letter to Dr A. Sample from this draft.
            {{ input.consult_draft }}
            {{ node.problem_loop.content }}
            {{ macro.leaflet.prompt }}

            """,
    };

    [Fact]
    public void Export()
    {
        var root = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT_V31");
        if (string.IsNullOrWhiteSpace(root))
            return;

        var family = Path.Combine(root, "v31", "conformance", Family);
        if (Directory.Exists(family))
            Directory.Delete(family, recursive: true);

        var cases = new List<string>();
        foreach (var vector in Cases())
        {
            foreach (var (path, bytes) in Files(vector))
            {
                var file = Path.Combine([family, vector.Name, .. path.Split('/')]);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, bytes);
            }

            cases.Add(vector.Name);
        }

        var index = Path.Combine(root, "v31", "conformance", "index.json");
        File.WriteAllText(
            index,
            RefusalPackagesExport.WithCases(File.ReadAllText(index, Utf8NoBom), Family, cases.Order(StringComparer.Ordinal).ToList()),
            Utf8NoBom);
    }

    /// <summary>
    /// <paramref name="vector"/>'s 30 package: its manifest's text and its other files by path, as the migration is given
    /// them. An in-repo package is read as <see cref="ByteEquivalenceFixtures.ReadPackage"/> reads it, so the derived
    /// <c>dag.mmd</c> is not among its files.
    /// </summary>
    internal static (string ManifestJson, Dictionary<string, string> Files) Source(VectorCase vector)
    {
        if (vector.Package is { } package)
            return ByteEquivalenceFixtures.ReadPackage(package);

        var files = HandBuilt.Where(f => f.Key != "manifest.json").ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        return (HandBuilt["manifest.json"], files);
    }

    /// <summary>
    /// The options <paramref name="vector"/> is migrated with: <see cref="MigrationFixture.Options"/> of its package (its
    /// <c>options.json</c>); the hand-built one has no hand choices (<see cref="MigrationFixture.NoChoices"/>).
    /// </summary>
    internal static Migration31Options Options(VectorCase vector) =>
        vector.Package is { } package ? MigrationFixture.Options(package) : MigrationFixture.NoChoices;

    /// <summary>
    /// The files of <paramref name="vector"/>'s case folder, by path within it: <c>source/manifest.json</c> and
    /// <c>source/&lt;path&gt;</c> for each 30 file (<see cref="Source"/>, byte for byte); <c>options.json</c>
    /// (<see cref="OptionsJson"/>); <c>expected/manifest.json</c> and <c>expected/&lt;path&gt;</c> for each file the
    /// migration writes (its snapshot test among them where it writes one); <c>notices.json</c>
    /// (<see cref="MigratedPackagesTests.NoticesJson"/>'s shape, as the <c>migrated/</c> goldens hold it); and
    /// <c>about.txt</c>.
    /// </summary>
    internal static Dictionary<string, byte[]> Files(VectorCase vector)
    {
        var (manifestJson, sourceFiles) = Source(vector);
        var options = Options(vector);
        var result = WorkflowPackageMigration30To31.Migrate(manifestJson, sourceFiles, options);
        Assert.True(result.Succeeded, $"{vector.Name}: " + string.Join("\n", result.Refusals));

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["source/manifest.json"] = Utf8NoBom.GetBytes(manifestJson),
        };
        foreach (var (path, text) in sourceFiles)
            files[$"source/{path}"] = Utf8NoBom.GetBytes(text);
        files["options.json"] = Utf8NoBom.GetBytes(OptionsJson(options));
        files["expected/manifest.json"] = Utf8NoBom.GetBytes(result.ManifestJson);
        foreach (var (path, text) in result.Files)
            files[$"expected/{path}"] = Utf8NoBom.GetBytes(text);
        files["notices.json"] = Utf8NoBom.GetBytes(MigratedPackagesTests.NoticesJson(result));
        files["about.txt"] = Utf8NoBom.GetBytes(About(vector, result));
        return files;
    }

    /// <summary>
    /// <c>options.json</c>: every option the case was migrated with, every member written. The package's own
    /// <c>options.json</c> (R18, <see cref="Migration31Options.ToJson"/>): <c>renames</c> (a reserved or long 30 id to its
    /// hand-chosen 31 id), <c>whenEmpty</c> (a deliverable's id to its hand-chosen <c>whenEmpty</c>) and
    /// <c>folderFormats</c> (a snippet folder's id to the format its items take), each <c>{}</c> when the case chooses
    /// none; then the vector's own <c>toolchain</c>, the toolchain the migration publishes its result with, in
    /// <c>lock.json</c>'s names (<see cref="CompiledDocumentsExport.ToolchainJson"/>). Written as <c>notices.json</c> is.
    /// </summary>
    internal static string OptionsJson(Migration31Options options)
    {
        var json = options.ToJson();
        json["toolchain"] = JsonNode.Parse(CompiledDocumentsExport.ToolchainJson(options.Toolchain));
        return ByteEquivalenceFixtures.WriteJson(json);
    }

    /// <summary>The case's <c>about.txt</c>: what the package shows, how the case is run, and what the migration said, by count.</summary>
    private static string About(VectorCase vector, Migration31Result result) =>
        vector.About + "\n\n"
        + "Migrating source/ (specVersion 30) with options.json gives expected/ (specVersion 31), byte for byte, and "
        + "notices.json: every notice, the hand edits among them, and the refusals, each in the migration's order.\n\n"
        + $"Files: {result.Files.Count + 1} written. Notices: {result.Notices.Count}. Hand edits: {result.HandEdits.Count}. "
        + $"Snapshot test: {(result.Files.ContainsKey("tests/migration_snapshot.json") ? "tests/migration_snapshot.json" : "none")}.\n";
}
