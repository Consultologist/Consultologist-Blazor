using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the way the source reader's tests reach the published
/// contract in the <c>external/consultologist-package-format</c> submodule, and the check that
/// a refusal says what the design says. Read off the submodule rather than fetched, as in
/// <see cref="V31ContractsTests"/>, and failing loudly when it is not checked out.
/// </summary>
internal static class V31Contract
{
    /// <summary>The area's own sentences (ids that have no row in sentences.json), as the plan lists them.</summary>
    private static readonly Dictionary<string, string> AreaSentences = new()
    {
        ["A02-001"] = "<Declaration> declares '<member>' as <what it is>; it is <what it must be>.",
        ["A02-002"] = "File '<path>' is not valid UTF-8; save it as UTF-8.",
        ["A02-003"] = "The package has no manifest.json; it holds the package's name, version and inputs.",
        ["A02-004"] = "The manifest declares specVersion <n>; this reader reads 31.",
        ["A02-005"] = "File '<path>' must hold one JSON object.",
        ["A02-006"] = "Node '<id>' is kind <kind> in a file; a <kind> is a folder with an index.json.",
        ["A02-007"] = "Node '<id>' is kind <kind> in a folder; a <kind> node is a file, <id>.md.",
        ["A02-008"] = "<subject> has no body; name the nodes that run in nodes.",
    };

    /// <summary>Area 03's own sentences (<c>A03-NNN</c>), as plan area-03-types lists them.</summary>
    private static readonly Dictionary<string, string> TypeAreaSentences = new()
    {
        ["A03-001"] = "Package format '<id>' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name.",
        ["A03-002"] = "<Format> at <path> refers to #/$defs/<name>, which the file doesn't define.",
        ["A03-003"] = "<Format> at <path> writes '<keyword>' as <what it is>; it is <what it must be>.",
        ["A03-004"] = "<Format> at <path> uses '<keyword>', which belongs on <an object | a list | a string>.",
        ["A03-005"] = "<Format> at <path> declares no type; write type, $ref or anyOf.",
        ["A03-006"] = "<Format> at <path> is an object without '<keyword>'; every object writes properties, required and additionalProperties: false.",
        ["A03-007"] = "<Format> at <path> is a list without items; write the one schema its items follow.",
        ["A03-008"] = "<Format> at <path> writes anyOf with <n> member; a union has two or more.",
        ["A03-009"] = "<Format> declares <keyword> '<value>' (accepted: <list>).",
        ["A03-010"] = "<Subject> lists null but is not nullable; write \"type\": [\"string\", \"null\"], or remove null.",
        ["A03-011"] = "Format '<id>' expands to more than 10,000 schemas through the formats it refers to; refer to fewer.",
    };

    /// <summary>Area 04's own sentences (<c>A04-NNN</c>), as plan area-04-json-logic lists them.</summary>
    private static readonly Dictionary<string, string> LogicAreaSentences = new()
    {
        ["A04-001"] = "<site> gives <operation> <operand> (<format>); <operation> takes <expected>.",
        ["A04-002"] = "<site> compares <a> (<format>) with <b> (<format>); = and ≠ compare two values of one type.",
        ["A04-003"] = "<site> mixes <format> and <format> in <the branches of an if | a list>; they have one type.",
        ["A04-004"] = "<site> writes an object with <n> members where a rule is expected; an operation is an object with one member, and object builds an object.",
        ["A04-005"] = "<site> writes var with <what it is>; var takes one path, written as text.",
        ["A04-006"] = "<site> reads '<path>', which is not a path; a path is a root, then names joined by dots.",
        ["A04-007"] = "<site> writes object with <what it is>; object takes one object, from field names to rules.",
        ["A04-008"] = "<site> builds an object where no format gives its fields; object builds the value of a computed node.",
        ["A04-009"] = "Computed node '<id>' builds <format> with <field> as <format>; the format declares it <format>.",
        ["A04-010"] = "<site> gives <operation> a <operand> that is <what it is>; <operand> is written as text.",
        ["A04-011"] = "<site> <member> could not be evaluated: <detail>.",
    };

    /// <summary>#1502: area 05's own sentences (<c>A05-NNN</c>), as plan area-05-resolver-and-lock lists them; each is added as a task first says it.</summary>
    private static readonly Dictionary<string, string> ResolveAreaSentences = new()
    {
        ["A05-001"] = "<Kind> '<id>' <member> '<text>' is not a reference; a reference is a root, then names joined by dots.",
        ["A05-003"] = "Node '<a>' reads node '<b>', which reads node '<a>': nodes can't read each other in a circle. Break the circle.",
        ["A05-004"] = "Function '<f>' format '<id>' is compiled as '<f>__<id>', which the package already uses: rename one.",
        ["A05-005"] = "Call '<id>' with <param> is <what it is>; an argument is a reference, written as text, or null.",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    /// <summary>A path under <c>external/consultologist-package-format/v31/</c>; fails when the file is missing.</summary>
    public static string Path(params string[] parts)
    {
        var path = System.IO.Path.Combine(
            [RepoRoot(), "external", "consultologist-package-format", "v31", .. parts]);
        Assert.True(
            File.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init).");
        return path;
    }

    /// <summary>#1502 (area 05): the folder of the pinned suite's <c>compiled-documents</c> family; fails when the pin has none.</summary>
    public static string CompiledDocumentsFolder()
    {
        var path = System.IO.Path.Combine(
            RepoRoot(), "external", "consultologist-package-format", "v31", "conformance", "compiled-documents");
        Assert.True(
            Directory.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init), or its pin has no compiled documents.");
        return path;
    }

    public static JsonNode Json(params string[] parts) => JsonNode.Parse(File.ReadAllText(Path(parts)))!;

    /// <summary>
    /// The published registry: the built-in formats and renderers of <c>v31/formats/index.json</c> and
    /// <c>v31/renderers/index.json</c>, and the agents catalogue's formats
    /// (<c>external/consultologist-agents/agents/v31/formats/index.json</c>), so that S01-034 and
    /// S01-035 mean what an outside implementation reading the same files sees.
    /// </summary>
    public static SourceRegistry PublishedRegistry() => new(
        Items(Json("formats", "index.json")),
        Items(JsonNode.Parse(File.ReadAllText(AgentsFormatsIndex()))!),
        Items(Json("renderers", "index.json")));

    /// <summary>
    /// #1500 (specVersion 31, area 03): the published <see cref="TypeRegistry"/>, read off the two
    /// submodules: the built-in formats and renderers of <c>v31/</c>, and the agents catalogue's
    /// formats, bindings and agents (<c>external/consultologist-agents/agents/v31/</c>). The type
    /// tests and the refusal packages share it.
    /// </summary>
    public static TypeRegistry PublishedTypes()
    {
        var contract = System.IO.Path.GetDirectoryName(Path("bounds.json"))!;
        var agents = System.IO.Path.GetDirectoryName(AgentsFormatsIndex())!;
        var catalogue = System.IO.Path.GetDirectoryName(agents)!;
        return TypeRegistry.Read(
            System.IO.Path.Combine(contract, "formats"),
            agents,
            System.IO.Path.Combine(contract, "renderers", "index.json"),
            System.IO.Path.Combine(catalogue, "bindings.json"),
            System.IO.Path.Combine(catalogue, "agents.json"));
    }

    /// <summary>The folder of the pinned suite's refusal packages; fails when the submodule is not checked out.</summary>
    public static string RefusalPackagesFolder()
    {
        var path = System.IO.Path.Combine(
            RepoRoot(), "external", "consultologist-package-format", "v31", "conformance", "refusal-packages");
        Assert.True(
            Directory.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init), or its pin has no refusal packages.");
        return path;
    }

    private static HashSet<string> Items(JsonNode index) =>
        index["items"]!.AsArray().Select(i => (string)i!).ToHashSet(StringComparer.Ordinal);

    private static string AgentsFormatsIndex()
    {
        var path = System.IO.Path.Combine(RepoRoot(), "external", "consultologist-agents", "agents", "v31", "formats", "index.json");
        Assert.True(File.Exists(path), $"{path} is missing; check out the submodules.");
        return path;
    }

    /// <summary>Asserts the refusal's sentence is its row of sentences.json (or the area table) with the placeholders filled in.</summary>
    public static void AssertSentence(SourceRefusal refusal)
    {
        string row;
        if (refusal.Id.StartsWith("A02-", StringComparison.Ordinal))
        {
            Assert.True(AreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A03-", StringComparison.Ordinal))
        {
            Assert.True(TypeAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A04-", StringComparison.Ordinal))
        {
            Assert.True(LogicAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A05-", StringComparison.Ordinal))
        {
            Assert.True(ResolveAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else
        {
            var found = Json("sentences.json")["sentences"]!.AsArray()
                .FirstOrDefault(s => (string?)s!["id"] == refusal.Id);
            Assert.True(found != null, $"{refusal.Id} has no row in v31/sentences.json.");
            row = (string)found!["sentence"]!;
        }

        var pattern = "^" + ToPattern(row) + "$";
        // Part 04 § 10.1 writes S04-009 for Loop "(also While, If, Case)": the word is the flow kind's.
        if (refusal.Id == "S04-009")
        {
            // The row must still start with the word the slice below replaces.
            Assert.StartsWith("Loop '", row);
            pattern = "^(?:Loop|While|If|Case)" + pattern["^Loop".Length..];
        }

        Assert.True(
            Regex.IsMatch(refusal.Sentence, pattern, RegexOptions.Singleline),
            $"{refusal.Id}: \"{refusal.Sentence}\" does not match the row \"{row}\".");
    }

    /// <summary>Literal text is escaped; each outermost <c>&lt;…&gt;</c> (nested ones and choices included) becomes <c>.+?</c>.</summary>
    private static string ToPattern(string row)
    {
        var pattern = new StringBuilder();
        var literal = new StringBuilder();
        var depth = 0;
        foreach (var c in row)
        {
            if (c == '<')
            {
                if (depth == 0)
                {
                    pattern.Append(Regex.Escape(literal.ToString())).Append(".+?");
                    literal.Clear();
                }

                depth++;
            }
            else if (c == '>' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                literal.Append(c);
            }
        }

        return pattern.Append(Regex.Escape(literal.ToString())).ToString();
    }
}
