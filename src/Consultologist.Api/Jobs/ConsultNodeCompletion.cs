using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B5 #1004, package-format-v25-design.md §§ 6–7): a node's completion
/// order, stated once, in the activity that holds the reply and the scope:
/// <list type="number">
/// <item>the reply is taken and <c>agentOutputHash</c> computed — <see cref="ConsultNodeDispatch.Settle"/>'s, over the bytes as returned;</item>
/// <item>validation and <c>onInvalid</c> — <see cref="ConsultNodeDispatch.Settle"/>'s;</item>
/// <item><c>failIfEmpty</c> is judged on the reply, before anything is composed;</item>
/// <item>#1044 (package-format-v25.md § The reply is a template): the reply is
/// RENDERED as a template over the node's own scope — the scope its prompt
/// rendered over — so every <c>{{ macro.&lt;id&gt;.text }}</c> the model wrote
/// where an inline macro's instruction told it to is that macro's text, rendered, and
/// any other read a template could make renders the same way; a reply that
/// does not parse or reads what the scope has no value for FAILS the node
/// (the renderer's <see cref="InvalidOperationException"/>, which the Durable
/// policy does not retry — the prompt or the agent is at fault). Nothing is
/// sanctioned, dropped or rewritten. The package's walk is still the
/// cross-check on the prompt's own <c>.prompt</c> reads (a render read the
/// walk does not reach is an engine fault, thrown): the walk is what made the
/// fills' reads edges of this node;</item>
/// <item>the before/after instances compose around the rendered reply —
/// <c>forItem</c> anchors, <c>when</c> gates (a refusal is recorded by name
/// with the explainer's sentence);</item>
/// <item><c>outputHash</c> is taken over what came out — what <c>node.&lt;id&gt;.text</c>,
/// the aggregator and the deliverable read.</item>
/// </list>
/// Every text composed here — the reply, an instance's — renders over the
/// node's own 25 scope, which is why its reads are the node's derived edges.
/// </summary>
internal static class ConsultNodeCompletion
{
    public static NodeRunResult Complete(
        NodeRunResult settled,
        ConsultPromptNodeActivityInput input,
        WorkflowPackage package,
        IReadOnlySet<string> promptReads)
    {
        var nodeId = NodeIdOf(input.NodeId);
        var itemId = input.Item?.GetValueOrDefault("id");
        var node = package.Nodes?.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        var formatType = ConsultRenderPayload.FormatTypeOf(package, node);

        // 3. failIfEmpty, on the reply.
        if (input.FailIfEmpty != null && IsEmpty(settled, formatType))
        {
            throw new ConsultNodeEmptyReplyException(nodeId, input.FailIfEmpty);
        }

        var hasInstances = input.Instances is { Count: > 0 };

        if (!hasInstances && promptReads.Count == 0 && !LooksLikeATemplate(settled.RawOutput))
        {
            // Nothing to compose, nothing to render (plain text renders to itself):
            // step 6 is step 1's digest, already taken.
            return settled;
        }

        var scope = ConsultRenderPayload.ScopeAt25(input, package);
        var sanctioned = SanctionedBy(package, nodeId, itemId);

        foreach (var read in promptReads.Order(StringComparer.Ordinal))
        {
            if (!sanctioned.Contains(read))
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' rendered macro.{read}.prompt, which the package's walk does not reach from its prompt; the walk and the render disagree.");
            }
        }

        // 4. The reply rendered (#1044): a classification is its normalised answer
        // and renders nothing; everything else is a template over the node's scope.
        var trace = new TemplateRenderer.TemplateRenderTrace();
        var renderedReply = settled.Classification is not null
            ? settled.RawOutput
            : TemplateRenderer.Render($"Node '{nodeId}' reply", settled.RawOutput, scope, ConceptPipes.All, trace);

        // 5. The instances: anchored by forItem, gated by when.
        var instances = new List<WorkflowNodeCompletion.Instance>();
        var excluded = new List<ConsultNodeExclusion>();

        foreach (var instance in input.Instances ?? Array.Empty<ConsultNodeMacroDescriptor>())
        {
            if (instance.ForItem != null && !string.Equals(instance.ForItem, itemId, StringComparison.Ordinal))
            {
                continue;
            }

            // v25 (#1038, design § 16): judged over the same scope the instance
            // renders over — the item, the node texts, the facts — never the ≤24
            // tree; the fixed reason form (provenance@v2026.10.2).
            if (instance.When != null
                && !WorkflowConditionAt25.Holds($"Node '{nodeId}' macro '{instance.Id}' condition", instance.When, scope, ConceptPipes.All))
            {
                excluded.Add(new ConsultNodeExclusion(WorkflowNodeCompletion.MacroKind, instance.Id, WorkflowConditionAt25.Reason(instance.When)));
                continue;
            }

            instances.Add(new WorkflowNodeCompletion.Instance(instance.Id, instance.After, RenderMacro(scope, instance.Id, nodeId)));
        }

        var composed = WorkflowNodeCompletion.Compose(renderedReply, instances, trace.TextReads.ToList());

        // 6. outputHash over what came out.
        return settled with
        {
            RawOutput = composed.Text,
            OutputHash = ConsultGenerationProvenance.Sha256Hex(settled.Classification ?? composed.Text),
            Appended = composed.Appended.Count > 0 ? composed.Appended.Select(p => new ConsultAppendedEntry(p.Kind, p.Id)).ToList() : null,
            Excluded = excluded.Count > 0 ? excluded : null,
        };
    }

    /// <summary>#1044: whether a reply carries anything a render would change — a Scriban code or statement block; plain text renders to itself.</summary>
    internal static bool LooksLikeATemplate(string reply) =>
        reply.Contains("{{", StringComparison.Ordinal) || reply.Contains("{%", StringComparison.Ordinal);

    /// <summary>Whether a failure that crossed the activity boundary is the empty-reply refusal (its type name rides FailureDetails; the message never does).</summary>
    public static bool IsEmptyReplyFailure(string? errorType) =>
        string.Equals(errorType, typeof(ConsultNodeEmptyReplyException).FullName, StringComparison.Ordinal);

    /// <summary>
    /// Empty, by the format's type: a blank reply on text; an empty top-level
    /// array on an array format (a concept-list with no concepts, as ever); an
    /// object or an enum is never empty — it parsed and validated.
    /// </summary>
    internal static bool IsEmpty(NodeRunResult settled, string formatType)
    {
        switch (formatType)
        {
            case WorkflowInputTypes.Text:
                return string.IsNullOrWhiteSpace(settled.RawOutput);

            case WorkflowInputTypes.Array:
                if (settled.Concepts is not null)
                {
                    return settled.Concepts.Count == 0;
                }

                try
                {
                    return JsonNode.Parse(settled.RawOutput) is JsonArray { Count: 0 };
                }
                catch (JsonException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>The bare node id of an instance key (<c>node:item</c> on a fan).</summary>
    private static string NodeIdOf(string instanceKey)
    {
        var colon = instanceKey.IndexOf(':');
        return colon < 0 ? instanceKey : instanceKey[..colon];
    }

    /// <summary>The inline macros the package's walk says this prompt reaches — the validator's closure, for this instance (B5: the static half).</summary>
    private static IReadOnlySet<string> SanctionedBy(WorkflowPackage package, string nodeId, string? itemId)
    {
        if (package.SourceFiles is null)
        {
            return TemplateReferenceContext.NoIds;
        }

        var nodes = WorkflowDerivedEdges.PrepareNodes(package.Manifest, package.SourceFiles, package.Formats, out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(package.Manifest, package.SourceFiles, nodes, snippets, contextFor);
        return walk.PromptReadsOf(nodeId, itemId);
    }

    private static string RenderMacro(TemplateRenderScope scope, string macroId, string nodeId)
    {
        if (!scope.Macros.TryGetValue(macroId, out var source))
        {
            throw new InvalidOperationException($"Node '{nodeId}' places macro '{macroId}', which the package does not carry.");
        }

        return TemplateRenderer.Render($"Macro '{macroId}'", source.Text, scope, ConceptPipes.All);
    }
}

/// <summary>
/// v25 (B5 #1004): the reply was empty and the node declared <c>failIfEmpty</c>.
/// An <see cref="InvalidOperationException"/>, so the Durable policy does not
/// retry a judgment the same reply would fail again; the message is the
/// author's sentence, never the reply.
/// </summary>
public sealed class ConsultNodeEmptyReplyException : InvalidOperationException
{
    public ConsultNodeEmptyReplyException(string nodeId, string sentence)
        : base($"Node '{nodeId}' reply is empty: {sentence}")
    {
        Sentence = sentence;
    }

    public string Sentence { get; }
}
