using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): an <see cref="ITemplateReads"/> standing in for area 07's dialect. A body is found
/// by its declaration's name: a node's key (<c>node.plan</c>; inside a function <c>draft_section/node.patient_draft</c>),
/// a deliverable's key, a text's path (<c>snippets/standards/hpi.md</c>). A body named in <c>reads</c> reads those
/// paths; one named in <c>bodies</c> is that <see cref="TemplateReads"/>; any other is unknown when
/// <c>everyBody</c> is false, else reads nothing.
/// </summary>
internal sealed class StubTemplates(
    IReadOnlyDictionary<string, string[]> reads,
    bool everyBody = true,
    IReadOnlyDictionary<string, TemplateReads>? bodies = null) : ITemplateReads
{
    /// <summary>A stub where every body is known and reads nothing.</summary>
    public static StubTemplates Empty() => new(new Dictionary<string, string[]>());

    /// <summary>The names <see cref="Of"/> was asked for, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <inheritdoc/>
    public TemplateReads? Of(object declaration, SourceFunction? function)
    {
        var name = declaration switch
        {
            SourceNode node => function == null ? node.Key : $"{function.Id}/{node.Key}",
            SourceDeliverable deliverable => deliverable.Key,
            SourceText text => text.Path,
            _ => "",
        };
        Asked.Add(name);
        if (bodies != null && bodies.TryGetValue(name, out var body))
            return body;
        if (reads.TryGetValue(name, out var paths))
            return new TemplateReads(paths, [], [], [], false);
        return everyBody ? TemplateReads.None : null;
    }
}
