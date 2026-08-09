namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Per-tab memory of the editor pane you were last on, so leaving Templates
/// for another page and coming back returns you to it instead of starting
/// over. Same shape and the same reason as ConsultJobSession (#207): scoped DI
/// in Blazor WASM is one instance per tab session, and navigation never clears
/// it — that is the point.
///
/// Deliberately not persisted. A cold load, including a browser refresh, is a
/// new session and opens on Graph, which is the default the editor chose.
/// Remembering across page switches and starting fresh on load are different
/// questions, and this only answers the first.
///
/// A stale key needs no handling here: LoadAsync keeps a selection only when
/// the loaded version still has it, so switching packages self-corrects to
/// Graph.
/// </summary>
public sealed class WorkflowEditorSession
{
    public string? SelectedKey { get; set; }
}
