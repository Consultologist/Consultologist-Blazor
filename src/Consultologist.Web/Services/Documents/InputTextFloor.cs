using System.Text.RegularExpressions;

namespace Consultologist.Web.Services.Documents;

/// <summary>
/// The client's copy of the engine's content floor (#290/#291), so a document
/// that carries no referral is caught the moment it is attached rather than at
/// Create. A file whose extracted text falls below the floor still attaches —
/// this is a warning, not a block — but the slot says so, and names the
/// cloud-link shortcut case that produces it most often.
///
/// The authority is <c>Consultologist.Api.Jobs.InputContent</c>: the server
/// re-extracts and re-checks on the job start and its refusal is what actually
/// governs. Web carries no reference to the Api (it hand-mirrors wire types the
/// same way <see cref="AI.ConsultInputValue"/> does), so the floor algorithm is
/// replicated here and kept in step by <c>InputTextFloorTests</c>. Keep the
/// URL regex, the whitespace rule and the default in sync with that class.
/// </summary>
internal static class InputTextFloor
{
    /// <summary>
    /// Below this many non-URL, non-whitespace characters, a required input is
    /// treated as carrying no referral. Mirrors the server default; the server
    /// setting <c>Inputs__MinimumRequiredCharacters</c> is not readable from a
    /// WASM client, so the client warns on the default and the server — which
    /// can be reconfigured — remains authoritative.
    /// </summary>
    internal const int MinimumCharacters = 40;

    // The extensions of the "link" files a desktop sync client drops in place
    // of the document: a Google Docs/Sheets/Slides shortcut, a Windows .url, a
    // macOS .webloc. They hold a URL, never the document, so they always fall
    // below the floor — but naming the cause is more use than a generic warning.
    private static readonly string[] ShortcutExtensions =
        [".gdoc", ".gsheet", ".gslides", ".url", ".webloc"];

    // Bare URLs and the autolinked forms mail clients produce. Deliberately
    // greedy to the next whitespace: a SharePoint link is one long token and
    // the whole of it is noise. (No Compiled: this runs under WASM.)
    private static readonly Regex Urls = new(
        @"\b(?:https?://|ftp://|www\.|mailto:)\S+",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Characters left once URLs and whitespace are removed — what a reader
    /// would call the prose. Iterates <see cref="char"/>, as the server does.
    /// </summary>
    internal static int MeaningfulLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var withoutUrls = Urls.Replace(text, " ");
        var count = 0;

        foreach (var ch in withoutUrls)
        {
            if (!char.IsWhiteSpace(ch))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether the extracted text carries too little prose to be a referral.</summary>
    internal static bool IsBelowFloor(string? text) => MeaningfulLength(text) < MinimumCharacters;

    /// <summary>Whether a filename is one of the cloud-link shortcut kinds.</summary>
    internal static bool LooksLikeShortcut(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && ShortcutExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
