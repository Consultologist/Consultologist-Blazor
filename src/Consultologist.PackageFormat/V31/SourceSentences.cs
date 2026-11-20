namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): every sentence the source reader says, one method each,
/// named after what it says. Each is the row of <c>v31/sentences.json</c> (or the area's own
/// <c>A02-NNN</c>) with its placeholders filled in, character for character, and carries that
/// row's id. The tests hold each against the published table.
/// </summary>
internal static class SourceSentences
{
    /// <summary>S01-030: the id does not match <c>^[a-z][a-z0-9_]*$</c>.</summary>
    public static SourceRefusal IdNotValid(string id, string path) =>
        new("S01-030", $"'{id}' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores.", path);

    /// <summary>S01-031: the id is longer than <see cref="SourceBounds.IdLength"/>.</summary>
    public static SourceRefusal IdTooLong(string id, string path) =>
        new("S01-031", $"Id '{id}' is {id.Length} characters; the most is SourceBounds.idLength ({SourceBounds.IdLength}).", path);

    /// <summary>S01-032: the id is a name Windows reserves.</summary>
    public static SourceRefusal IdReservedOnWindows(string id, string path) =>
        new("S01-032", $"Id '{id}' is reserved on Windows (con, prn, aux, nul, com1–com9, lpt1–lpt9) and can't name a file: rename it.", path);

    /// <summary>S01-033: the id is a word references read; <paramref name="rootOrMember"/> is <c>root</c> or <c>member</c>.</summary>
    public static SourceRefusal IdReserved(string id, string rootOrMember, string word, string path) =>
        new("S01-033", $"Id '{id}' is reserved: {rootOrMember} '{word}' is read in references, so nothing may be called '{id}'.", path);

    /// <summary>S01-014: the file starts with a byte-order mark.</summary>
    public static SourceRefusal StartsWithByteOrderMark(string path) =>
        new("S01-014", $"File '{path}' starts with a byte-order mark; save it as UTF-8 without one.", path);

    /// <summary>S01-015: a <c>\r</c> that does not end a line, on the 1-based <paramref name="line"/> (counted after <c>\r\n</c> became <c>\n</c>).</summary>
    public static SourceRefusal LoneCarriageReturn(string path, int line) =>
        new("S01-015", $"File '{path}' contains a carriage return on line {line} that doesn't end the line; lines end with \\n.", path);

    /// <summary>A02-002: the bytes are not UTF-8 (D5).</summary>
    public static SourceRefusal NotUtf8(string path) =>
        new("A02-002", $"File '{path}' is not valid UTF-8; save it as UTF-8.", path);

    /// <summary>S01-037: more than <see cref="SourceBounds.Files"/> files.</summary>
    public static SourceRefusal TooManyFiles(int count) =>
        new("S01-037", $"The package has {count} files; the most is SourceBounds.files ({SourceBounds.Files}).", "");

    /// <summary>S01-038: a file over <see cref="SourceBounds.FileBytes"/>; <c>&lt;n&gt;</c> is KiB, rounded up (D3).</summary>
    public static SourceRefusal FileTooLarge(string path, long bytes) =>
        new("S01-038", $"File '{path}' is {Kib(bytes)} KiB; the most is SourceBounds.fileBytes ({SourceBounds.FileBytes / 1024} KiB).", path);

    /// <summary>S01-039: all files over <see cref="SourceBounds.TotalBytes"/>; <c>&lt;n&gt;</c> is MiB, rounded up to one decimal place (D3).</summary>
    public static SourceRefusal PackageTooLarge(long bytes) =>
        new("S01-039", $"The package is {Mib(bytes)} MiB; the most is SourceBounds.totalBytes ({SourceBounds.TotalBytes / 1048576} MiB).", "");

    /// <summary>S01-040: a path over <see cref="SourceBounds.PathLength"/> characters.</summary>
    public static SourceRefusal PathTooLong(string path) =>
        new("S01-040", $"Path '{path}' is {path.Length} characters; the most is SourceBounds.pathLength ({SourceBounds.PathLength}), counted from the package root.", path);

    /// <summary>S01-021: a member the declaration does not write.</summary>
    public static SourceRefusal MemberMissing(string declaration, string member, string path) =>
        new("S01-021", $"{declaration} is missing '{member}'; every member is written, and null means none.", path);

    /// <summary>S01-022: <c>null</c> in a member that can never be null.</summary>
    public static SourceRefusal MemberNull(string declaration, string member, string path) =>
        new("S01-022", $"{declaration} sets '{member}' to null, which it can never be.", path);

    /// <summary>S01-023: a member the declaration's kind does not have; <paramref name="closest"/> is D20's.</summary>
    public static SourceRefusal MemberUnknown(string declaration, string path, string member, string closest) =>
        new("S01-023", $"{declaration} in '{path}' declares unknown member '{member}'. Did you mean '{closest}'?", path);

    /// <summary>S01-024: <c>"description": []</c>.</summary>
    public static SourceRefusal DescriptionEmpty(string declaration, string path) =>
        new("S01-024", $"{declaration} has an empty description; write null for none.", path);

    /// <summary>S01-025: a description of more than 500 code points, its lines joined with <c>\n</c>.</summary>
    public static SourceRefusal DescriptionTooLong(string declaration, int characters, string path) =>
        new("S01-025", $"{declaration}'s description is {characters} characters; the most is 500.", path);

    /// <summary>S01-026: a label that is empty or only whitespace.</summary>
    public static SourceRefusal LabelEmpty(string declaration, string path) =>
        new("S01-026", $"{declaration} has an empty label.", path);

    /// <summary>A02-001 (D7): a member of the wrong JSON type.</summary>
    public static SourceRefusal MemberWrongType(string declaration, string member, string whatItIs, string whatItMustBe, string path) =>
        new("A02-001", $"{declaration} declares '{member}' as {whatItIs}; it is {whatItMustBe}.", path);

    /// <summary>S01-006: a file no index lists; <paramref name="folder"/> is the folder whose index should, or <c>the package root</c>.</summary>
    public static SourceRefusal NotListed(string path, string folder) =>
        new("S01-006", $"File '{path}' is not listed: every file in {folder} is named once in its index.json, or is index.json or README.md.", path);

    /// <summary>S01-007: a folder without its <c>index.json</c>; the refusal's path is the missing file.</summary>
    public static SourceRefusal NoIndex(string folder) =>
        new("S01-007", $"{folder} has no index.json; every folder lists its members, even when it has none (\"items\": []).", folder + "/index.json");

    /// <summary>S01-008: a top-level folder that is not there at all; the refusal's path is the index it needs.</summary>
    public static SourceRefusal NoTopFolder(string folder) =>
        new("S01-008", $"The package has no {folder}/ folder; all nine top-level folders are always present.", folder + "/index.json");

    /// <summary>
    /// S01-009: a listed id with no member of the kind its folder holds; <paramref name="list"/> is the index's path (and member, for a body).
    /// The package's <c>formats/</c> says S02-039 instead (<see cref="FormatSentences.FormatFileMissing"/>, area 03's D13).
    /// </summary>
    public static SourceRefusal ListedButMissing(string list, string id, string folder) =>
        new("S01-009", $"{list} lists '{id}', but {folder} has no {id}.md or {id}/.", IndexPath(list));

    /// <summary>S01-010: an id listed more than once in one list.</summary>
    public static SourceRefusal ListedTwice(string list, string id) =>
        new("S01-010", $"{list} lists '{id}' twice.", IndexPath(list));

    /// <summary>S01-011: an id listed with a trailing <c>/</c>; <paramref name="id"/> is the bare id.</summary>
    public static SourceRefusal ListedWithSlash(string list, string id) =>
        new("S01-011", $"{list} lists '{id}/'; list a folder by its bare id.", IndexPath(list));

    /// <summary>S01-012: a folder holding both <c>&lt;id&gt;.md</c> and <c>&lt;id&gt;/</c>; the refusal's path is the file.</summary>
    public static SourceRefusal FileAndFolder(string folder, string id) =>
        new("S01-012", $"{folder} holds both {id}.md and {id}/: an id names one file or one folder.", $"{folder}/{id}.md");

    /// <summary>S01-013: a folder inside a delivery folder; <paramref name="path"/> is the inner folder's.</summary>
    public static SourceRefusal NestedDeliveryFolder(string path) =>
        new("S01-013", $"Delivery folder '{path}' is inside another delivery folder; delivery folders are one level deep.", path);

    /// <summary>S01-016: a <c>.md</c> file that is not fence, one JSON object, fence.</summary>
    public static SourceRefusal NoFrontMatter(string path) =>
        new("S01-016", $"File '{path}' must start with a line of exactly ---, then one JSON object, then a line of exactly ---.", path);

    /// <summary>
    /// S01-017: a header (or, D6, a plain JSON file) that is not JSON. The <c>&lt;parser message&gt;</c>
    /// is this reader's own, <c>line &lt;n&gt;, column &lt;m&gt;</c>: the 1-based line of the file (a
    /// header's first line is the file's line 2) and the 1-based column in characters, never the
    /// wording of the JSON library underneath.
    /// </summary>
    public static SourceRefusal NotJson(string path, int line, int column) =>
        new("S01-017", string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The header of '{path}' is not valid JSON: line {line}, column {column}."), path);

    /// <summary>S01-018: a member written twice in one object of a header (or, D6, a plain JSON file).</summary>
    public static SourceRefusal DuplicateMember(string path, string member) =>
        new("S01-018", $"The header of '{path}' declares '{member}' twice.", path);

    /// <summary>S01-019: a required body that is empty; <paramref name="declaration"/> is D9's (<c>Renderer 'x'</c>), <paramref name="what"/> <c>prompt</c>, <c>template</c> or <c>text</c>.</summary>
    public static SourceRefusal EmptyBody(string declaration, string what, string path) =>
        new("S01-019", $"{declaration} has an empty body; its {what} is the body.", path);

    /// <summary>S01-020: a body where none may be; <paramref name="what"/> is <c>computed node</c>, <c>call</c> or <c>check</c>.</summary>
    public static SourceRefusal HasBody(string declaration, string what, string path) =>
        new("S01-020", $"{declaration} has a body; a {what} keeps everything in its header.", path);

    /// <summary>
    /// S01-028: a producer the kind does not have. <paramref name="node"/> is <c>Node</c> or <c>Check</c>;
    /// the template's article is kept, so it reads "a agent node's" (ruled on). A reserved producer
    /// says S02-010 instead (<see cref="FormatSentences.ReservedProducer"/>, area 03's D14).
    /// </summary>
    public static SourceRefusal ProducerDisagrees(string node, string key, string kind, string value, string expected, string path) =>
        new("S01-028", $"{node} '{key}' is kind {kind} and declares producer '{value}'; a {kind} node's producer is {expected}.", path);

    /// <summary>
    /// S02-007: a <c>kind</c> that is none of the eight; the declaration word is <c>Node</c> in a check too (as written).
    /// <c>let</c> says S02-008 instead (<see cref="FormatSentences.KindLet"/>, area 03's D14).
    /// </summary>
    public static SourceRefusal UnknownKind(string id, string kind, string path) =>
        new("S02-007", $"Node '{id}' declares unknown kind '{kind}' (accepted: agent, rendered, computed, for, while, if, case, call).", path);

    /// <summary>A02-006 (D21): a <c>.md</c> leaf that declares a flow kind.</summary>
    public static SourceRefusal FlowKindInFile(string id, string kind, string path) =>
        new("A02-006", $"Node '{id}' is kind {kind} in a file; a {kind} is a folder with an index.json.", path);

    /// <summary>A02-007 (D21): a folder's <c>index.json</c> that declares a leaf kind.</summary>
    public static SourceRefusal LeafKindInFolder(string id, string kind, string path) =>
        new("A02-007", $"Node '{id}' is kind {kind} in a folder; a {kind} node is a file, {id}.md.", path);

    /// <summary>
    /// S04-009: a flow node's <c>label</c> is <c>null</c> (in place of S01-022). The row is written for
    /// <c>Loop</c>; <paramref name="word"/> is the kind's (<c>Loop</c>, <c>While</c>, <c>If</c>, <c>Case</c>), as § 10.1 says.
    /// </summary>
    public static SourceRefusal FlowLabelNull(string word, string id, string path) =>
        new("S04-009", $"{word} '{id}' declares label null; a loop's or branch's label is what the rail, History and the diagram show: write one.", path);

    /// <summary>S04-010: a member that is another flow kind's own (in place of S01-023); <paramref name="owner"/> is <c>a for</c>, <c>a while</c>, <c>an if</c> or <c>a case</c>.</summary>
    public static SourceRefusal MemberOfAnotherKind(string word, string id, string member, string owner, string path) =>
        new("S04-010", $"{word} '{id}' declares {member}, which belongs to {owner}.", path);

    /// <summary>S04-011: a body's <c>ret</c> is <c>null</c>; <paramref name="subject"/> is § 10.1's (<c>If 'x' then</c>).</summary>
    public static SourceRefusal ReturnsNothing(string subject, string path) =>
        new("S04-011", $"{subject} returns nothing; name the body node the outside reads with ret.", path);

    /// <summary>S04-013: a body lists an id twice, or an id another body of the same node lists (in place of S01-010).</summary>
    public static SourceRefusal BodyListsTwice(string subject, string id, string path) =>
        new("S04-013", $"{subject} lists node '{id}' twice; a body is a set.", path);

    /// <summary>S04-018: a <c>for</c>'s <c>nodes</c> is <c>[]</c>.</summary>
    public static SourceRefusal LoopHasNoBody(string id, string path) =>
        new("S04-018", $"Loop '{id}' has no body; name the nodes that run per item in nodes.", path);

    /// <summary>S04-019: <c>parallel</c> true with <c>maxParallel</c> 1.</summary>
    public static SourceRefusal ParallelOfOne(string id, string path) =>
        new("S04-019", $"Loop '{id}' declares parallel true with maxParallel 1, which runs one item at a time; declare \"parallel\": false and \"maxParallel\": null instead.", path);

    /// <summary>S01-005 (S04-020 has the same words): <c>maxParallel</c> above <see cref="FormatBounds.Parallel"/>; <paramref name="written"/> as the JSON wrote it.</summary>
    public static SourceRefusal MaxParallelTooHigh(string id, string written, string path) =>
        new("S01-005", $"Loop '{id}' declares maxParallel {written}; the most is FormatBounds.parallel (15).", path);

    /// <summary>S04-021: <c>maxParallel</c> 0, negative, or not a whole number; <paramref name="written"/> as the JSON wrote it.</summary>
    public static SourceRefusal MaxParallelTooLow(string id, string written, string path) =>
        new("S04-021", $"Loop '{id}' declares maxParallel {written}; the least is 2, or null for no cap of its own.", path);

    /// <summary>S04-022: <c>maxParallel</c> not <c>null</c> with <c>parallel</c> false.</summary>
    public static SourceRefusal MaxParallelWhenSequential(string id, string written, string path) =>
        new("S04-022", $"Loop '{id}' declares maxParallel {written} with parallel false; a sequential loop runs one item at a time: write \"maxParallel\": null.", path);

    /// <summary>S04-023: <c>until</c> not <c>null</c> with <c>parallel</c> true.</summary>
    public static SourceRefusal UntilWhenParallel(string id, string path) =>
        new("S04-023", $"Loop '{id}' declares until with parallel true; until stops after the item where it holds, which needs items in order: declare \"parallel\": false and \"maxParallel\": null.", path);

    /// <summary>S04-024: a <c>for</c>'s <c>max</c> below 1. The row's text is fixed ("max 0") and is used as written, whatever the number.</summary>
    public static SourceRefusal LoopMaxTooLow(string id, string path) =>
        new("S04-024", $"Loop '{id}' declares max 0; the least is 1.", path);

    /// <summary>S04-031: a <c>while</c>'s <c>max</c> is <c>null</c> (in place of S01-022).</summary>
    public static SourceRefusal WhileMaxNull(string id, string path) =>
        new("S04-031", $"While '{id}' declares max null; a while must declare its bound (1 to 15).", path);

    /// <summary>S01-003 (S04-032 has the same words): a <c>while</c>'s <c>max</c> above <see cref="FormatBounds.Passes"/>.</summary>
    public static SourceRefusal WhileMaxTooHigh(string id, string written, string path) =>
        new("S01-003", $"While '{id}' declares max {written}; a while runs at most FormatBounds.passes (15) passes.", path);

    /// <summary>S04-033: a <c>while</c>'s <c>max</c> below 1.</summary>
    public static SourceRefusal WhileMaxTooLow(string id, string written, string path) =>
        new("S04-033", $"While '{id}' declares max {written}; the least is 1.", path);

    /// <summary>S04-034: a <c>while</c>'s <c>nodes</c> is <c>[]</c>.</summary>
    public static SourceRefusal WhileHasNoBody(string id, string path) =>
        new("S04-034", $"While '{id}' has no body; name the nodes that run per pass in nodes.", path);

    /// <summary>S04-041: an <c>if</c>'s <c>then</c> is <c>null</c> (in place of S01-022).</summary>
    public static SourceRefusal ThenNull(string id, string path) =>
        new("S04-041", $"If '{id}' declares then null; an if runs a then body.", path);

    /// <summary>S04-042: a <c>case</c>'s <c>branches</c> is <c>[]</c>.</summary>
    public static SourceRefusal NoBranches(string id, string path) =>
        new("S04-042", $"Case '{id}' has no branches; a case runs one branch per set of values.", path);

    /// <summary>S04-043: a branch's <c>when</c> is <c>[]</c>; <paramref name="branch"/> counts from 1.</summary>
    public static SourceRefusal BranchListsNoValue(string id, int branch, string path) =>
        new("S04-043", $"Case '{id}' branch {branch} lists no value; a branch's when names one or more values.", path);

    /// <summary>
    /// S04-048: a branch with <c>nodes</c> <c>[]</c> and a <c>ret</c>, or <c>nodes</c> and a <c>null</c>
    /// <c>ret</c>. <paramref name="nodes"/> is <c>[]</c> or the ids joined by <c>, </c>; <paramref name="ret"/> the written value or <c>null</c>.
    /// </summary>
    public static SourceRefusal EmptyBranchHalfWritten(string id, int branch, string nodes, string ret, string path) =>
        new("S04-048", $"Case '{id}' branch {branch} writes nodes {nodes} with ret {ret}; an empty branch writes nodes [] and ret null together.", path);

    /// <summary>S04-076: a branch's <c>when</c> holds <c>null</c>.</summary>
    public static SourceRefusal BranchListsNull(string id, int branch, string path) =>
        new("S04-076", $"Case '{id}' branch {branch} lists null; a null subject goes to the else.", path);

    /// <summary>S04-062: a function folder holds <c>checks/</c> or <c>deliverables/</c>; the refusal's path is that folder.</summary>
    public static SourceRefusal FunctionHoldsTopLevelFolder(string function, string folder, string path) =>
        new("S04-062", $"Function '{function}' holds a {folder} folder; checks and deliverables live only at the package's top level: return a check_result or a value instead.", path);

    /// <summary>S04-067: a function parameter declares <c>required</c> or <c>channel</c> (in place of S01-023).</summary>
    public static SourceRefusal ParameterDeclaresInputMember(string function, string parameter, string member, string path) =>
        new("S04-067", $"Function '{function}' parameter '{parameter}' declares {member}; a parameter has label, description and format, and every one is passed.", path);

    /// <summary>A02-008 (D22): a <c>then</c>, an <c>else</c>, a non-empty branch or a function whose <c>nodes</c> is <c>[]</c>.</summary>
    public static SourceRefusal NoBody(string subject, string path) =>
        new("A02-008", $"{subject} has no body; name the nodes that run in nodes.", path);

    /// <summary>S01-034: a package format named like a <c>built-in</c> or <c>catalogue</c> one.</summary>
    public static SourceRefusal FormatReusesName(string id, string builtInOrCatalogue, string path) =>
        new("S01-034", $"Format '{id}' reuses the name of a {builtInOrCatalogue} format: rename it.", path);

    /// <summary>S01-035: a package renderer named like a built-in one.</summary>
    public static SourceRefusal RendererReusesName(string id, string path) =>
        new("S01-035", $"Renderer '{id}' reuses the name of a built-in renderer: rename it.", path);

    /// <summary>S01-041: JSON nested past <see cref="SourceBounds.JsonDepth"/> (D2); <paramref name="pointer"/> is the container's.</summary>
    public static SourceRefusal JsonTooDeep(string path, string pointer) =>
        new("S01-041", $"File '{path}' nests JSON more than 32 levels deep at {pointer}; the most is SourceBounds.jsonDepth ({SourceBounds.JsonDepth}).", path);

    /// <summary>A02-003 (D8): no <c>manifest.json</c>.</summary>
    public static SourceRefusal NoManifest() =>
        new("A02-003", "The package has no manifest.json; it holds the package's name, version and inputs.", "manifest.json");

    /// <summary>A02-004 (D8): a <c>specVersion</c> other than 31; <paramref name="written"/> is the number as written.</summary>
    public static SourceRefusal SpecVersionNotRead(string written) =>
        new("A02-004", $"The manifest declares specVersion {written}; this reader reads 31.", "manifest.json");

    /// <summary>A02-005 (D6): a plain JSON file whose root is not an object.</summary>
    public static SourceRefusal NotAnObject(string path) =>
        new("A02-005", $"File '{path}' must hold one JSON object.", path);

    /// <summary>
    /// S01-002: two nodes of one id where the deeper one's folder is inside the other's.
    /// <paramref name="path"/> is the deeper node's enclosing ids (<c>sections.paragraphs</c>, with the
    /// root in <c>checks/</c>, D13); <paramref name="other"/> is <c>at the top level</c> or <c>in …</c>.
    /// </summary>
    public static SourceRefusal NodeReusesVisibleId(string id, string path, string other, string file) =>
        new("S01-002", $"Node '{id}' in {path} reuses the id of node '{id}' {other}, which {path} can read: rename one. "
            + "Ids may repeat only where neither can see the other (for example in two sibling loops).", file);

    /// <summary>S01-036: two nodes of one id inside a <c>while</c>; each path is a key without its root (D14).</summary>
    public static SourceRefusal NodeTwiceInWhile(string id, string whileId, string first, string second, string file) =>
        new("S01-036", $"Node '{id}' appears twice inside while '{whileId}' ({first} and {second}): pass.\u2026.node.{id} must name one node.", file);

    /// <summary>S01-042: an expression of <paramref name="count"/> nodes, past <see cref="SourceBounds.ExpressionNodes"/>.</summary>
    public static SourceRefusal ExpressionTooLarge(string site, string path, int count) =>
        new("S01-042", $"{site} in '{path}' has {count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} expression nodes; "
            + "the most is SourceBounds.expressionNodes (1,000).", path);

    /// <summary>S04-012: a body's <c>ret</c> that is not one of its own listed children's full names.</summary>
    public static SourceRefusal ReturnsNonChild(string subject, string ret, string fullNames, string path) =>
        new("S04-012", $"{subject} returns {ret}, which is not one of its own children ({fullNames}): "
            + "write the child's full name, for example node.sections.paragraphs.", path);

    /// <summary>The file a <c>&lt;list&gt;</c> names: the index's path, before the body member a flow folder's list adds.</summary>
    private static string IndexPath(string list)
    {
        var space = list.IndexOf(' ');
        return space < 0 ? list : list[..space];
    }

    private static string Kib(long bytes) =>
        ((bytes + 1023) / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Mib(long bytes)
    {
        var tenths = (bytes * 10 + 1048575) / 1048576;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10}");
    }
}
