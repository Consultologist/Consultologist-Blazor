using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    /// <summary>One call instance once its function's frame is open: the function, its frame, and its arguments' hashes in parameter order.</summary>
    private sealed record CallState(PlanFunction Function, RunFrame Frame, JsonArray Arguments);

    private sealed partial class Run
    {
        /// <summary>Each call instance once its frame is open, by instance (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunInstance, CallState> calls = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Moves a call instance on (part 04 § 7.7; part 09 § 3.1): once ready, its <c>if</c> decided as a leaf's, it binds
        /// each parameter from its argument and opens its function's body inline, in a frame of its own with every body
        /// instance at once (keys through the call node, ranked under the call, scheduled by the same rounds); once that
        /// frame has finished it settles as the function's <c>ret</c>: the same state, reason and read form. A function or a
        /// <c>ret</c> that is not there, or a chain of calls deeper than publish allows (<see cref="FormatBounds.Depth"/>), is
        /// not what publish wrote, and fails the call with S07-006. False when nothing moved.
        /// </summary>
        private async Task<bool> CallAsync(RunInstance instance)
        {
            if (!calls.TryGetValue(instance, out var call))
            {
                if (!await GateAsync(instance))
                    return true; // skipped, or failed

                // RunPlan.Build refuses a call naming no function or a function with no ret in its body, and a ring of
                // calls; publish bounds the depth, so a chain past it (S04-056) is a document publish did not write.
                if (instance.Entry.Function is not { } id || !plan.Functions.TryGetValue(id, out var function)
                    || function.Ret == null || instance.Frame.Depth + 1 > FormatBounds.Depth + 1)
                {
                    await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                    return true;
                }

                var parameters = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
                var arguments = new JsonArray();
                var with = instance.Entry.Json["with"] as JsonObject;
                foreach (var parameter in function.Inputs)
                {
                    var bound = Argument(instance, SourceLayout.Text(with?[parameter]));
                    parameters[parameter] = bound;
                    arguments.Add(new JsonObject { ["param"] = parameter, ["hash"] = CanonicalJson.Hash(bound) });
                }

                var frame = instance.Frame.OpenCall(instance, function, parameters);
                calls[instance] = new CallState(function, frame, arguments);
                foreach (var key in function.Body)
                {
                    if (function.Entries.TryGetValue(key, out var body))
                        Add(frame, body);
                }

                return true;
            }

            if (!call.Frame.Instances.All(i => i.IsSettled))
                return false;

            // RunPlan.Build refuses a ret that is not one of the function's top-level nodes, each of which has an instance here.
            if (call.Frame.Instances.FirstOrDefault(i => i.Entry.Key == call.Function.Ret) is not { State: { } state, Read: { } read } ret)
            {
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return true;
            }

            var content = SourceLayout.Text(read["content"]);
            await SettleAsync(instance, state, state == RowState.Completed ? null : ret.Reason, read.DeepClone().AsObject(),
                content == null ? null : CanonicalJson.HashText(content));
            return true;
        }

        /// <summary>
        /// What <c>param.&lt;id&gt;</c> reads for the argument <paramref name="reference"/> of <paramref name="call"/>, read in
        /// the caller's frame (part 04 § 7.7): <c>null</c> for the literal <c>null</c>; for a whole reference (<c>node.&lt;id&gt;</c>,
        /// <c>check.&lt;id&gt;</c>, <c>item</c>, <c>item.parent</c>…, <c>param.&lt;id&gt;</c>) only its <c>.value</c> and
        /// <c>.content</c>, as the caller reads them, never its <c>.state</c> or <c>.items</c>; for a snippet folder item
        /// (<c>snippet.&lt;folder&gt;.&lt;item&gt;</c>) its header data and its body, as a loop over the folder gives them; for any
        /// other reference (a field path, <c>run.&lt;fact&gt;</c>, <c>profile.&lt;field&gt;</c>) its value, and as
        /// <c>.content</c> its text: the text itself, else its canonical JSON.
        /// </summary>
        private JsonObject? Argument(RunInstance call, string? reference)
        {
            if (reference == null)
                return null;

            var frame = call.Frame;
            var segments = reference.Split('.');
            if (segments is ["snippet", var folder, var stem])
                return SnippetItem(frame, $"snippet.{folder}", stem);

            var keyed = segments[0] is "node" or "check" && frame.Map.ContainsKey(reference);
            if (keyed || (segments[0] == "item" && segments.Skip(1).All(s => s == "parent")) || segments is ["param", _])
            {
                var whole = keyed ? RunScope.Read(reference, frame) : At(scope.Data(call.Entry, frame, [reference]), reference);
                if (whole is not JsonObject read)
                    return null; // a parameter passed on that was itself null
                return new JsonObject { ["value"] = read["value"]?.DeepClone(), ["content"] = read["content"]?.DeepClone() };
            }

            var value = At(scope.Data(call.Entry, frame, [reference]), reference);
            return new JsonObject { ["value"] = value?.DeepClone(), ["content"] = Text(value) };
        }

        /// <summary>The snippet folder item <paramref name="stem"/> of <paramref name="folder"/> (the caller's function's own folder, else the package's) as a parameter reads it: <c>{ value, content }</c>; null when it is not there.</summary>
        private JsonObject? SnippetItem(RunFrame frame, string folder, string stem)
        {
            foreach (var (id, value, content, _) in SnippetItems(frame, folder) ?? [])
            {
                if (id == stem)
                    return new JsonObject { ["value"] = value?.DeepClone(), ["content"] = content };
            }

            return null;
        }

        /// <summary>A value's text: a string itself, <c>null</c> for <c>null</c>, else its canonical JSON.</summary>
        private static string? Text(JsonNode? value) => value switch
        {
            null => null,
            JsonValue text when text.GetValueKind() == JsonValueKind.String => text.GetValue<string>(),
            _ => CanonicalJson.Write(value),
        };

        /// <summary>
        /// A call row's detail (part 08 § 7): the function's id (in place of the function the row is written in), its
        /// <c>depth</c> (0 outside every function, <i>d</i>+1 inside a function reached at depth <i>d</i>), and its
        /// <c>arguments</c>' hashes in parameter order, never their values (<c>[]</c> for a call that never opened).
        /// </summary>
        private void CallDetail(RunInstance instance, JsonObject detail)
        {
            var call = calls.GetValueOrDefault(instance);
            detail["function"] = instance.Entry.Function;
            detail["depth"] = instance.Frame.Depth;
            detail["arguments"] = call?.Arguments.DeepClone() ?? new JsonArray();
        }
    }
}
