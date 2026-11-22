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

    /// <summary>#1503: area 06's own sentences (<c>A06-NNN</c>), as plan area-06-engine-loading-and-scheduling lists them; each is added as a task first says it.</summary>
    private static readonly Dictionary<string, string> EngineAreaSentences = new()
    {
        ["A06-001"] = "<site> <member> could not be decided: <matches | has_word> took longer than <n> seconds on this text. Use a simpler pattern.",
        ["A06-002"] = "While '<id>' declares checkFirst true, but its check reads node '<n>' of its own body, which has no value before the first pass: declare \"checkFirst\": false, or read something that runs before the while.",
        ["A06-003"] = "Node '<id>' computed a value that isn't <format>.",
        ["A06-004"] = "Workflow <package>@<version> isn't published.",
        ["A06-005"] = "Node '<id>' gave a reply that isn't <format>.",
        ["A06-006"] = "Loop '<id>' was given <reference>, which isn't a list.",
        ["A06-007"] = "Case '<id>' was given <reference>, which no branch lists, and it has no else.",
        ["A06-008"] = "<Subject> couldn't be rendered.",
        ["A06-009"] = "This job's inputs are no longer held, so it can't run. Run the workflow again.",
        ["A06-010"] = "The engine couldn't finish this job. Tell the operator.",
        ["A06-011"] = "Input '<id>' is required; fill it in to run this workflow.",
        ["A06-012"] = "Input '<id>' was given a value that isn't <format>.",
        ["A06-013"] = "Unknown input(s) <ids> (declared: <ids>).",
    };

    /// <summary>
    /// #1504: area 07's own sentences (<c>A07-NNN</c>), as plan area-07-prompts-and-macros lists them: the first nine at
    /// once (later tasks say the rest), then each as a task adds it (A07-010 and A07-011, Task 5; A07-012 and A07-013, Task 8b; A07-014, Task 12b; A07-015 and A07-016, Task 12c).
    /// </summary>
    private static readonly Dictionary<string, string> TemplateAreaSentences = new()
    {
        ["A07-001"] = "Snippet '<id>' places <part.* or macro.*>, and <subject> places it in <a part's text | a macro's instruction>; a snippet placed there doesn't place parts or macros.",
        ["A07-002"] = "Part '<name>' of node '<id>' uses <if or part.*>; a part's text only presents.",
        ["A07-003"] = "Macro '<id>' places <macro.* or part.*> in its instruction; combine instructions by placing each macro in the prompt.",
        ["A07-004"] = "<Subject> uses <form> at line <n>; a for runs over a whole list in order and reads only for.first, for.last, for.index, for.even and for.odd.",
        ["A07-005"] = "<Subject> can't be read as a template at line <n>: close every tag and every if and for.",
        ["A07-006"] = "<Subject> stopped rendering: array.join met a list with an empty element.",
        ["A07-007"] = "<Subject> writes <path> at line <n>, which is <an object | a list>; read a member, loop over it or pipe it into a renderer.",
        ["A07-008"] = "<Subject> reads <path> at line <n>; read <.value | .content | .title> of it.",
        ["A07-009"] = "<Subject> places <unit> at line <n> inside <an if | a for>; a part, a snippet or a macro is placed at the top level of its template, and its own if decides it.",
        ["A07-010"] = "Snippet '<id>' declares format <format>; a snippet's text is plaintext, markdown_block or markdown_inline.",
        ["A07-011"] = "<Subject> places more than <limit> parts, snippets and macros, counting what its parts and snippets place; place fewer.",
        ["A07-012"] = "Snippet '<id>' <places <unit> | pipes into <renderer> | reads <path>>, and <subject> reads it as <path> at line <n>; "
            + "a folder item's body read in a loop places no part, macro or snippet, pipes into no renderer and reads no run.* or run-dependent value.",
        ["A07-013"] = "Snippet '<id>' holds a tag at line <n>, and <subject> <passes it to function '<f>' as param.<p> | reads it in its expression>, "
            + "where it isn't rendered; keep a folder item's body plain text, or read it as item.content in its loop.",
        ["A07-014"] = "Nodes '<a>' and '<b>' bind agent '<name>' at versions <m> and <n>; a package binds one version of an agent.",
        ["A07-015"] = "MacroChoices names '<id>', which is not a boolean input of the package.",
        ["A07-016"] = "Input '<id>' is given one value in Inputs and another in MacroChoices; send it once.",
    };

    /// <summary>
    /// #1505: area 08's own sentences (<c>A08-NNN</c>), as plan area-08-checks-deliverables-profile lists them, each as a
    /// task adds it (A08-001, Task 3; A08-002, Task 4; A08-003, Task 5; A08-004, Task 13; A08-005, Task 8; A08-006, Task 16).
    /// </summary>
    private static readonly Dictionary<string, string> ChecksAreaSentences = new()
    {
        ["A08-001"] = "Deliverable '<id>' is checked by <reference>; a deliverable's check names a check.*.",
        ["A08-002"] = "Deliverables folder '<path>' has bundle <value>; write \"zip\" or null.",
        ["A08-003"] = "Check '<id>' reads deliverable.<deliverable>.state, and deliverable '<deliverable>' waits for check '<id>' through its check '<gate>': read its .content, or read the state in a check it doesn't wait for.",
        ["A08-004"] = "The signature chosen for this job isn't in your profile. Choose one in Profile.",
        ["A08-005"] = "Deliverable '<id>' was empty, and the workflow fails an empty <label>.",
        ["A08-006"] = "<Subject> couldn't be checked: the engine failed reading its template. This is a fault in the engine, not in the package; report it.",
    };

    /// <summary>
    /// #1506: area 09's own sentences (<c>A09-NNN</c>), as plan area-09-the-record lists them (A09-001: the tenth limit, the
    /// instances one job may open, part 09 § 3.4; <c>&lt;n&gt;</c> is written as the other limits are, <c>10,000</c>; A09-002:
    /// a record row larger than the row store holds, part 09 § 9).
    /// </summary>
    private static readonly Dictionary<string, string> RecordAreaSentences = new()
    {
        ["A09-001"] = "The job reached <n> steps, the most this engine runs for one job.",
        // #1506 (final review F7): a record row larger than the row store holds; <row key> is package ids and item ids
        ["A09-002"] = "The job's record of '<row key>' is larger than this engine stores. Tell the operator.",
    };

    /// <summary>
    /// #1507: area 10's own sentences (<c>A10-NNN</c>), as plan area-10-migration lists them, each added by the task that
    /// first says it (A10-008: a <c>!x</c> on text that became <c>blank(x)</c>, part 10 § 2.3, R1; A10-012, A10-015,
    /// A10-016, A10-017 and A10-018: the migration's gates and the chain's rung refusals, part 10 § 1.1, § 1.4, D2; A10-019 and
    /// A10-020: a 30 file or package that does not read; A10-021: a package format's free-text field, part 02 § 11; A10-022
    /// to A10-025: the reasons of the first pass's hand edits, part 10 § 1.4; A10-026: a lone carriage return made a line end,
    /// part 01 § 2.4; A10-013 and A10-027 to A10-032: the nodes pass's notices and reasons, part 10 § 1.2, D16, the Task 4
    /// ruling on size, part 04 § 5.2, part 02 § 3.3; A10-006, A10-033 and A10-034: the renames pass, part 10 § 1.9, part 01 §§ 6 and 7, D6;
    /// A10-001, A10-005 and A10-035 to A10-039: the templates pass's reasons, part 10 § 1.2, § 1.4, part 05 §§ 2, 3.1, 7, R6;
    /// A10-003, A10-004, A10-007, A10-040 to A10-042 and A10-045: the macros pass, part 10 §§ 1.3, 1.4, part 05 § 8, D3 to D5;
    /// A10-043 and A10-044: a MacroChoices entry the 31 start refuses, part 10 § 1.3, R7).
    /// </summary>
    private static readonly Dictionary<string, string> MigrationAreaSentences = new()
    {
        ["A10-001"] = "the body's if at line <n> does more than choose text to show; write it as parts with their own if.",
        ["A10-003"] = "macro '<id>' was placed <before | after | before and after> the reply by the engine; say in its instruction where the model writes its marker.",
        ["A10-004"] = "macro '<id>' is read both as fixed text and as an instruction; split it into a snippet and a macro.",
        ["A10-005"] = "the template uses <construct> at line <n>, which the 31 dialect doesn't have; rewrite it.",
        ["A10-006"] = "Renamed <kind> '<id>' to '<new id>': '<id>' is reserved at 31.",
        ["A10-007"] = "Macro '<id>' is now offered by the input '<input id>'.",
        ["A10-008"] = "<file>, <member>: '!<path>' became blank(<path>); it now also holds for empty and white-space-only text.",
        ["A10-012"] = "The package is at specVersion <n>; migration starts at 5.",
        ["A10-013"] = "Node '<id>' kept a reply that did not fit its format (onInvalid: flag); 31 fails it instead.",
        ["A10-015"] = "The package is already at specVersion <n>.",
        ["A10-016"] = "The manifest does not read as a package: it is not a JSON object with a whole-number specVersion.",
        ["A10-017"] = "At specVersion <n>: <refusal>",
        ["A10-018"] = "At specVersion <n>: the rung could not read this package; fix the manifest, then migrate.",
        ["A10-019"] = "The file '<file>' does not read: <fault>; fix it, then migrate.",
        ["A10-020"] = "The package does not read at specVersion 30; fix it, then migrate.",
        ["A10-021"] = "Format '<id>', field '<field>' holds free text; consider a text format.",
        ["A10-022"] = "a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.",
        ["A10-023"] = "the input takes one of several types, which a 31 input cannot; choose one format.",
        ["A10-024"] = "the field takes one of several types, which 31 writes as an anyOf; choose one.",
        ["A10-025"] = "the format is nullable at its root, which a format a model writes cannot be at 31; make the root an object.",
        ["A10-026"] = "<file>: a carriage return that didn't end a line became a line end.",
        ["A10-027"] = "<file>, <member>: size(<list>) is null when <list> is null, where 30 counted 0.",
        ["A10-028"] = "the case switches on <subject>, whose values 31 cannot list; switch on an enumerated string.",
        ["A10-029"] = "<subject> can be null, which a 31 case sends to its else; add an else body.",
        ["A10-030"] = "Node '<id>' declared <member> at 30, which 31 has no member for; it was dropped.",
        ["A10-031"] = "a package format's root is an object or a list at 31; make it one, or use a registry format.",
        ["A10-032"] = "the case switches on classifier '<id>', whose answers are not typed at 31; give it a package format whose value is an enum of its answers.",
        ["A10-033"] = "The chosen rename of '<id>' to '<new id>' cannot be made: '<new id>' <is already taken | is not a 31 id>; choose another.",
        ["A10-034"] = "Renamed <kind> '<id>' to '<new id>': an id is at most 64 characters at 31.",
        ["A10-035"] = "the body's if at line <n> decides what a <snippet | macro | rendered node> body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.",
        ["A10-036"] = "macro '<id>' is placed under different conditions in <files>; a macro has one if, so place it under one condition, or split it into one macro per condition.",
        ["A10-037"] = "the template writes <path> at line <n>, which is <an object | a list> at 31; read a member, loop over it or pipe it into a renderer.",
        ["A10-038"] = "the template can't be read at line <n>; close every tag, if and for.",
        ["A10-039"] = "the template reads <path> at line <n>, which a 31 <prompt | rendered node> doesn't read; rewrite it.",
        ["A10-040"] = "macro '<id>' is read as fixed text at line <n>, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.",
        ["A10-041"] = "the body places macro '<id>', which a 31 macro body cannot; place it in the prompt instead.",
        ["A10-042"] = "the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.",
        ["A10-043"] = "MacroChoices names '<id>', which is not a macro of the package.",
        ["A10-045"] = "macro '<id>' is read as fixed text at line <n>, but a macro folder's item is never fixed text at 31; place its text as a snippet.",
        ["A10-044"] = "MacroChoices names macro '<id>', whose if does not read exactly one boolean input; send the input in Inputs instead.",
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

    /// <summary>#1504 (area 07): the folder of the pinned suite's family <paramref name="family"/>; fails when the pin has none.</summary>
    public static string ConformanceFolder(string family)
    {
        var path = System.IO.Path.Combine(RepoRoot(), "external", "consultologist-package-format", "v31", "conformance", family);
        Assert.True(
            Directory.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init), or its pin has no {family}.");
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
        else if (refusal.Id.StartsWith("A06-", StringComparison.Ordinal))
        {
            Assert.True(EngineAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A07-", StringComparison.Ordinal))
        {
            Assert.True(TemplateAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A08-", StringComparison.Ordinal))
        {
            Assert.True(ChecksAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A09-", StringComparison.Ordinal))
        {
            Assert.True(RecordAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else if (refusal.Id.StartsWith("A10-", StringComparison.Ordinal))
        {
            Assert.True(MigrationAreaSentences.TryGetValue(refusal.Id, out var area), $"{refusal.Id} is not an area sentence.");
            row = area!;
        }
        else
        {
            var found = Json("sentences.json")["sentences"]!.AsArray()
                .FirstOrDefault(s => (string?)s!["id"] == refusal.Id);
            Assert.True(found != null, $"{refusal.Id} has no row in v31/sentences.json.");
            row = (string)found!["sentence"]!;
        }

        // #1504: S05-025's row is written as S05-024's middle ("… reads <path> and the run-dependent <value> in one output
        // …"): the sentence is S05-024's with run.<fact> in that place.
        if (refusal.Id == "S05-025")
        {
            var full = (string)Json("sentences.json")["sentences"]!.AsArray().First(s => (string?)s!["id"] == "S05-024")!["sentence"]!;
            Assert.Equal("… reads <path> and the run-dependent <value> in one output …", row);
            Assert.Contains(" and run.<fact> in one output ", full, StringComparison.Ordinal);
            row = full.Replace(" and run.<fact> in one output ", " and the run-dependent <value> in one output ", StringComparison.Ordinal);
        }

        // #1504: S05-043's row is written as S05-042's for part.* ("Macro '<id>' places part.<name> in its body; …", part 05
        // R-M1: "the rest unchanged"): the sentence is that head with S05-042's rest.
        if (refusal.Id == "S05-043")
        {
            var full = (string)Json("sentences.json")["sentences"]!.AsArray().First(s => (string?)s!["id"] == "S05-042")!["sentence"]!;
            Assert.Equal("Macro '<id>' places part.<name> in its body; …", row);
            row = row[..^1] + full[(full.IndexOf("; ", StringComparison.Ordinal) + 2)..];
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
