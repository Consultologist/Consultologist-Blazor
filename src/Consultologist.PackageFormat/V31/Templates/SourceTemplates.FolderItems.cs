namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.1, 3.2, 6.2; part 04 § 2.6): a snippet folder's items read by a loop.
/// In a loop over <c>snippet.&lt;folder&gt;</c>, <c>item.content</c> is the item's body "rendered in the loop's scope", so a
/// body that reads it (or <c>item.parent.….content</c> of such a loop) places, in effect, every item of that folder
/// there. Each item's body is checked as a snippet body at the reader's place and by the reader's row of § 3.1, once per
/// place (S05-022 for each read the place does not give, said on every line that reads it), and its reads are the
/// reader's (<see cref="CheckedBody.FolderItems"/>). Since one tag stands for a different body per item, the body keeps
/// to plain presentation: it places no part, macro or snippet (nor another folder item through <c>item.content</c>),
/// pipes into no renderer and holds no run spot (A07-012).
/// </summary>
public static partial class SourceTemplates
{
    /// <summary>A folder item's body checked at one place: the body (<c>null</c> when its syntax is faulted), the reads the place does not give, and what it does that A07-012 refuses.</summary>
    private sealed record PlacedFolderItem(CheckedBody? Body, IReadOnlyList<string> Unavailable, IReadOnlyList<string> Barred);

    private sealed partial class Context
    {
        private readonly Dictionary<SourceText, Dictionary<(ScopeFrame Frame, TemplateSite Reads), PlacedFolderItem>> folderItems =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The bodies of the folder items <paramref name="reader"/> reads as <c>item.content</c>, each checked at its place by
        /// the row <paramref name="reads"/>, each once, in the order read; for each line that reads them, S05-022 for each read
        /// an item's body lacks there and A07-012 for each thing it does that a folder item's body read in a loop may not.
        /// </summary>
        private IReadOnlyList<CheckedBody> FolderItems(CheckedBody reader, TemplateSite reads)
        {
            List<CheckedBody>? bodies = null;
            var seen = new HashSet<CheckedBody>(ReferenceEqualityComparer.Instance);
            var host = reader.Host;
            foreach (var read in reader.Reads)
            {
                if (FolderRead(read, host) is not { } folder)
                    continue;
                foreach (var item in folder.Items)
                {
                    var name = $"{folder.Id}/{item.Id}";
                    var placed = FolderItem(item, name, host.Frame, reads);
                    foreach (var path in placed.Unavailable)
                        Refusals.Add(DialectSentences.SnippetReadsUnavailable(name, path, host.Subject, read.Line, host.Path));
                    foreach (var does in placed.Barred)
                        Refusals.Add(DialectSentences.FolderItemDoes(name, does, host.Subject, read.Written, read.Line, host.Path));
                    if (placed.Body is { } body && seen.Add(body))
                        (bodies ??= []).Add(body);
                }
            }

            return bodies ?? (IReadOnlyList<CheckedBody>)[];
        }

        /// <summary>
        /// The snippet folder whose item <paramref name="read"/> reads the body of: <c>item.content</c> or
        /// <c>item.parent.….content</c> as the resolver types it at <paramref name="host"/>'s place, the loop it names
        /// being over <c>snippet.&lt;folder&gt;</c> (the function's own folder, else the package's); <c>null</c> otherwise.
        /// </summary>
        private SourceTextFolder? FolderRead(TypedRead read, TemplateHost host)
        {
            // A for variable named like a root has its own head; only a read the resolver typed as a whole is the item's.
            if (read.Head != read.Written)
                return null;
            var s = read.Written.Split('.');
            if (s.Length < 2 || s[0] != "item" || s[^1] != "content" || s[1..^1].Any(p => p != "parent"))
                return null;
            if (host.Frame.Loop(s.Length - 2) is not { } loop || resolution.Tree.Source(loop) is not { KnownLength: not null } source)
                return null;
            var id = source.Reference.Split('.') is ["snippet", var folder] ? folder : null;
            var entry = host.Function?.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == id)
                ?? package.Definitions.Snippets.Items.FirstOrDefault(e => e.Id == id);
            return entry as SourceTextFolder;
        }

        /// <summary>
        /// <paramref name="item"/>'s body checked at <paramref name="frame"/> by the row <paramref name="reads"/> (part 05 §
        /// 3.2), once per place: its reads, what that place does not give it, and what A07-012 refuses (each placement as
        /// written, each pipe into a renderer, each run spot's read, each folder item it reads as <c>item.content</c>).
        /// Never expanded: a folder item's body places nothing. A body with a syntax fault (said by
        /// <see cref="Definitions"/>) gives none.
        /// </summary>
        private PlacedFolderItem FolderItem(SourceText item, string name, ScopeFrame frame, TemplateSite reads)
        {
            if (!folderItems.TryGetValue(item, out var known))
                folderItems[item] = known = [];
            if (known.TryGetValue((frame, reads), out var done))
                return done;

            var result = new PlacedFolderItem(null, [], []);
            var syntax = SyntaxOf(item);
            if (syntax.Faults.Count == 0)
            {
                var host = new TemplateHost(TemplateSite.SnippetBody, $"Snippet '{name}'", item.Path, item, frame.Function, frame, null);
                var unavailable = new List<string>();
                var check = new BodyCheck(host, syntax, package, resolution, registry, producers, reads, unavailable);
                Refusals.AddRange(check.Header());
                var body = check.Run();
                // What holds whatever the place is said of the snippet (as for any snippet); a run spot's S05-060 is not
                // said, as the spot itself is refused below.
                Refusals.AddRange(check.Refusals);
                var barred = body.Placements.Select(p => $"places {p.Unit}")
                    .Concat(body.Renders.Select(r => $"pipes into {DialectRules.RendererRoot}.{r.Renderer}"))
                    .Concat(body.RunSpots.Select(r => $"reads {r.Reads}"))
                    .Concat(body.Reads.Where(r => FolderRead(r, host) is not null).Select(r => $"places {r.Written}"))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                result = new PlacedFolderItem(body, unavailable.Distinct(StringComparer.Ordinal).ToList(), barred);
            }

            known[(frame, reads)] = result;
            return result;
        }
    }
}
