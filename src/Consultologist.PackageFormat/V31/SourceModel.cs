using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): what <see cref="SourceParser.Parse"/> made of an upload.
/// <see cref="Package"/> is null exactly when <see cref="Refusals"/> is not empty; the refusals are
/// said once each and ordered by <see cref="SourceRefusal.Path"/> (ordinal), then in the order the
/// reader found them. <see cref="Normalised"/> lists the files whose <c>\r\n</c> intake turned into
/// <c>\n</c>, whatever the outcome (part 01 § 2.4: the publish report notes them).
/// </summary>
public sealed record SourceParseResult(SourcePackage? Package, IReadOnlyList<SourceRefusal> Refusals, IReadOnlyList<string> Normalised);

/// <summary>
/// #1499: a package source read whole (part 01 § 2): the manifest, the nine top-level folders and
/// every <c>README.md</c> (path to text).
/// </summary>
public sealed record SourcePackage(
    SourceManifest Manifest,
    SourceBody Nodes,
    SourceBody Checks,
    SourceIndex<SourceFunction> Functions,
    SourceDefinitions Definitions,
    SourceDeliveryFolder Deliverables,
    SourceIndex<SourceTest> Tests,
    IReadOnlyDictionary<string, string> Readmes);

/// <summary>#1499: <c>manifest.json</c> (part 01 § 3), its inputs in written order; <see cref="Json"/> is the whole file.</summary>
public sealed record SourceManifest(
    string Name,
    string Version,
    string? DerivedFrom,
    string? Title,
    IReadOnlyList<string>? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SourceInput> Inputs,
    JsonObject Json);

/// <summary>#1499: one member of the manifest's <c>inputs</c>.</summary>
public sealed record SourceInput(string Id, JsonObject Json);

/// <summary>#1499: a folder read through its <c>index.json</c>: its path, the index's description and its members in listed order.</summary>
public sealed record SourceIndex<T>(string Path, IReadOnlyList<string>? Description, IReadOnlyList<T> Items);

/// <summary>#1499: <c>nodes/</c> or <c>checks/</c>: the top level of production or of verification, in listed order.</summary>
public sealed record SourceBody(string Path, IReadOnlyList<string>? Description, IReadOnlyList<SourceNode> Nodes);

/// <summary>#1499: the four definition folders, of the package or of one function (part 04 § 7.1).</summary>
public sealed record SourceDefinitions(
    SourceIndex<SourceText> Renderers,
    SourceIndex<SourceTextEntry> Snippets,
    SourceIndex<SourceTextEntry> Macros,
    SourceIndex<SourceFormat> Formats);

/// <summary>#1499: a file with front matter: its id, path, header and body (kept byte for byte after line-ending normalisation).</summary>
public sealed record SourceText(string Id, string Path, JsonObject Header, string Body);

/// <summary>#1499: a member of <c>snippets/</c> or <c>macros/</c>: one file, or a folder of items.</summary>
public abstract record SourceTextEntry(string Id);

/// <summary>#1499: a snippet or macro that is one file.</summary>
public sealed record SourceSingleText(SourceText Text) : SourceTextEntry(Text.Id);

/// <summary>#1499: a snippet folder or macro folder: its <c>index.json</c> and its items in listed order.</summary>
public sealed record SourceTextFolder(string Id, string Path, JsonObject Index, IReadOnlyList<SourceText> Items) : SourceTextEntry(Id);

/// <summary>#1499: <c>formats/&lt;id&gt;.json</c>, the JSON Schema as written.</summary>
public sealed record SourceFormat(string Id, string Path, JsonObject Schema);

/// <summary>#1499: <c>tests/&lt;id&gt;.json</c>.</summary>
public sealed record SourceTest(string Id, string Path, JsonObject Json);

/// <summary>
/// #1499: <c>deliverables/</c> (<see cref="Id"/> null) or one delivery folder under it. Delivery
/// folders are one level deep, so <see cref="Folders"/> is empty below the top.
/// </summary>
public sealed record SourceDeliveryFolder(
    string? Id,
    string Path,
    JsonObject Index,
    IReadOnlyList<SourceDeliverable> Deliverables,
    IReadOnlyList<SourceDeliveryFolder> Folders);

/// <summary>#1499: one deliverable; <see cref="Key"/> is its rooted path (<c>deliverable.emr.note</c>, part 01 § 9.3).</summary>
public sealed record SourceDeliverable(string Id, string Key, string Path, JsonObject Header, string Body);

/// <summary>
/// #1499: a node of <c>nodes/</c>, <c>checks/</c> or a function's body. <see cref="Key"/> is its
/// rooted path (<c>node.sections.paragraphs.draft_paragraph</c>, part 01 § 9.3); <see cref="Path"/>
/// is its file, or its folder for a flow node.
/// </summary>
public abstract record SourceNode(string Id, string Key, string Path, string Kind, JsonObject Header);

/// <summary>#1499: an <c>agent</c>, <c>rendered</c>, <c>computed</c> or <c>call</c> node (or a check): a file, header then body.</summary>
public sealed record SourceLeaf(string Id, string Key, string Path, string Kind, JsonObject Header, string Body)
    : SourceNode(Id, Key, Path, Kind, Header);

/// <summary>#1499: a body: the nodes it lists, in order, and the key it returns (null only in an empty <c>case</c> branch).</summary>
public sealed record SourceBlock(IReadOnlyList<SourceNode> Nodes, string? Ret);

/// <summary>#1499: a <c>for</c> (part 04 § 2).</summary>
public sealed record SourceFor(
    string Id, string Key, string Path, string Kind, JsonObject Header,
    string In, bool Parallel, int? MaxParallel, JsonNode? Until, int? Max, string? ItemLabel, SourceBlock Body)
    : SourceNode(Id, Key, Path, Kind, Header);

/// <summary>#1499: a <c>while</c> (part 04 § 3).</summary>
public sealed record SourceWhile(
    string Id, string Key, string Path, string Kind, JsonObject Header,
    JsonNode While, int Max, JsonNode? Until, bool CheckFirst, SourceBlock Body)
    : SourceNode(Id, Key, Path, Kind, Header);

/// <summary>#1499: an <c>if</c> (part 04 § 5.1).</summary>
public sealed record SourceIf(
    string Id, string Key, string Path, string Kind, JsonObject Header,
    JsonNode If, SourceBlock Then, SourceBlock? Else)
    : SourceNode(Id, Key, Path, Kind, Header);

/// <summary>#1499: a <c>case</c> (part 04 § 5.2).</summary>
public sealed record SourceCase(
    string Id, string Key, string Path, string Kind, JsonObject Header,
    string Case, IReadOnlyList<SourceBranch> Branches, SourceBlock? Else)
    : SourceNode(Id, Key, Path, Kind, Header);

/// <summary>#1499: one branch of a <c>case</c>: the values it takes and its body.</summary>
public sealed record SourceBranch(IReadOnlyList<string> When, SourceBlock Body);

/// <summary>#1499: <c>functions/&lt;id&gt;/</c> (part 04 § 7): its index, parameters, body and own definitions.</summary>
public sealed record SourceFunction(
    string Id,
    string Path,
    JsonObject Index,
    IReadOnlyList<SourceParameter> Inputs,
    SourceBlock Body,
    SourceDefinitions Definitions);

/// <summary>#1499: one parameter of a function, in written order.</summary>
public sealed record SourceParameter(string Id, JsonObject Json);
