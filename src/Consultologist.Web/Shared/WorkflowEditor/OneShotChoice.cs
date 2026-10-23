namespace Consultologist.Web.Shared.WorkflowEditor;

/// <summary>#1136: one option of a <see cref="OneShotSelect"/> — the value the pick hands the handler and the text shown.</summary>
public sealed record OneShotChoice(string Value, string Text);
