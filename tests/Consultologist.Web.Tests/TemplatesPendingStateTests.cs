using System.Reflection;
using Bunit;
using Consultologist.Web.Pages;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #326: Templates.razor names its pending-change fields by hand in nine
/// separate places — PendingCount, PendingSummary, the draft record, persist,
/// restore, LoadAsync, DiscardAsync, ComposeManifest's no-change guard, and
/// the id-collision checks. Adding a field means an edit at each; missing one
/// compiles and usually passes. It has already cost a silently dropped
/// manifest entry and the two-publish bug in #322.
///
/// So these tests do not restate the field list — a tenth hand-maintained
/// list would be no improvement on nine. They take <c>PendingCount</c> as the
/// definition of "pending", since it is the list the publish button already
/// depends on, discover its members by probing, and hold the other lists to
/// what it found.
/// </summary>
public class TemplatesPendingStateTests : ClientRenderTestContext
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// The newline is load-bearing: bindingEdits keys are "nodeId\nvariable"
    /// and persist splits on it, so a token without one throws there for a
    /// reason that has nothing to do with what is being tested.
    /// </summary>
    private const string Token = "probe\nprobe";

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        return Render<Templates>();
    }

    private static int PendingCount(Templates editor) =>
        (int)typeof(Templates).GetProperty("PendingCount", Members)!.GetValue(editor)!;

    private static Task Invoke(IRenderedComponent<Templates> page, string method) =>
        page.InvokeAsync(() => (Task)typeof(Templates).GetMethod(method, Members)!.Invoke(page.Instance, null)!);

    // ---- discovery -------------------------------------------------------

    /// <summary>
    /// Every field PendingCount reacts to. Probing is the only honest way to
    /// ask: a field counts if making it non-empty moves the count.
    /// </summary>
    private static List<FieldInfo> CountedFields(Templates editor)
    {
        var counted = new List<FieldInfo>();

        foreach (var field in typeof(Templates).GetFields(Members).Where(f => !f.IsStatic))
        {
            var original = field.GetValue(editor);
            if (!TryMakePending(editor, field))
            {
                continue;
            }

            if (PendingCount(editor) > 0)
            {
                counted.Add(field);
            }

            Restore(editor, field, original);
        }

        return counted;
    }

    private static void Restore(Templates editor, FieldInfo field, object? original)
    {
        // A readonly collection was mutated in place rather than replaced, and
        // every one of them starts empty on a freshly loaded editor.
        if (field.IsInitOnly)
        {
            field.GetValue(editor)?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(field.GetValue(editor), null);
            return;
        }

        field.SetValue(editor, original);
    }

    private static bool TryMakePending(Templates editor, FieldInfo field)
    {
        try
        {
            if (field.GetValue(editor) is { } current && TryAddProbe(current))
            {
                return true;
            }

            if (field.IsInitOnly)
            {
                return false;
            }

            field.SetValue(editor, Probe(field.FieldType, 0));
            return true;
        }
        catch
        {
            // Not every field is probeable (injected services, interfaces,
            // array-backed read-only views). Those are not pending state.
            return false;
        }
    }

    private static bool TryAddProbe(object collection)
    {
        var type = collection.GetType();
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();

        if (definition == typeof(List<>) || definition == typeof(HashSet<>))
        {
            type.GetMethod("Add", arguments)!.Invoke(collection, new[] { Probe(arguments[0], 0) });
            return true;
        }

        if (definition == typeof(Dictionary<,>))
        {
            type.GetMethod("Add", arguments)!.Invoke(
                collection, new[] { Probe(arguments[0], 0), Probe(arguments[1], 0) });
            return true;
        }

        return false;
    }

    /// <summary>
    /// A non-empty value of any type the editor's pending state uses. Every
    /// string in it is the token, so the draft test can ask whether the field
    /// reached the payload at all.
    /// </summary>
    private static object? Probe(Type type, int depth)
    {
        if (depth > 4)
        {
            throw new InvalidOperationException("probe nested too deeply");
        }

        if (type == typeof(string)) return Token;
        if (type == typeof(bool)) return true;
        if (type == typeof(int)) return 1;

        // A delegate's only constructor is (object target, IntPtr method), and
        // the generic path below would happily satisfy it with a bogus pointer.
        // Invoking what comes back kills the test host rather than throwing —
        // found when the pending registry (#326) put Func<int> in a field.
        if (typeof(Delegate).IsAssignableFrom(type) || type.IsPointer || type == typeof(IntPtr))
        {
            throw new InvalidOperationException($"{type.Name} is not safely probeable");
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Probe(underlying, depth + 1);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(List<>) || definition == typeof(HashSet<>))
            {
                var list = Activator.CreateInstance(type)!;
                type.GetMethod("Add", arguments)!.Invoke(list, new[] { Probe(arguments[0], depth + 1) });
                return list;
            }

            if (definition == typeof(Dictionary<,>))
            {
                var map = Activator.CreateInstance(type)!;
                type.GetMethod("Add", arguments)!.Invoke(
                    map, new[] { Probe(arguments[0], depth + 1), Probe(arguments[1], depth + 1) });
                return map;
            }

            if (type.IsValueType && type.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true)
            {
                return Activator.CreateInstance(type, arguments.Select(a => Probe(a, depth + 1)).ToArray());
            }
        }

        // Records expose a protected copy constructor taking themselves;
        // following it recurses forever.
        var constructor = type.GetConstructors(Members)
            .Where(c => c.GetParameters().All(p => p.ParameterType != type))
            .OrderBy(c => c.GetParameters().Length)
            .First();

        var instance = constructor.Invoke(
            constructor.GetParameters().Select(p => Probe(p.ParameterType, depth + 1)).ToArray());

        // The settable-property classes (AddedItem, AddedNode, AddedPrompt)
        // build through an object initializer, so the constructor leaves them
        // blank and nothing would reach the draft payload.
        foreach (var property in type.GetProperties(Members).Where(p => p.CanWrite && p.PropertyType == typeof(string)))
        {
            property.SetValue(instance, Token);
        }

        return instance;
    }

    // ---- the tests -------------------------------------------------------

    [Fact]
    public void ProbingFindsThePendingFields()
    {
        // Guards the discovery itself. Without a floor the three tests below
        // would pass by iterating nothing, which is exactly the failure they
        // exist to catch. A ratchet, not a list: adding a field raises the
        // count and this still passes.
        var counted = CountedFields(RenderEditor().Instance);

        Assert.True(
            counted.Count >= 16,
            $"discovery found only {counted.Count}: {string.Join(", ", counted.Select(f => f.Name))}");
    }

    /// <summary>
    /// Reads the registry itself (#326) so the test still names no fields: the
    /// entries answer for their own counts.
    /// </summary>
    private static List<(string Name, Func<int> Count)> RegistryEntries(Templates editor)
    {
        var kinds = (System.Collections.IEnumerable)typeof(Templates)
            .GetProperty("PendingKinds", Members)!.GetValue(editor)!;

        var entries = new List<(string, Func<int>)>();

        foreach (var kind in kinds)
        {
            var type = kind!.GetType();
            var name = (string)type.GetProperty("Name")!.GetValue(kind)!;
            var counter = (Delegate)type.GetProperty("Count")!.GetValue(kind)!;
            entries.Add((name, () => (int)counter.DynamicInvoke()!));
        }

        return entries;
    }

    [Fact]
    public async Task EveryPendingFieldIsCoveredByExactlyOneRegistryEntry()
    {
        // Holds the registry to a 1:1 mapping: no field wired into two entries,
        // double-counting the badge and the publish gate.
        //
        // What it deliberately does NOT claim: that a *new* field must be
        // registered. PendingCount is now derived from the registry, so an
        // unregistered field moves nothing and is invisible to discovery. No
        // test can force registration, because "is this pending state?" is only
        // answerable by the registry itself. The win is that there is one place
        // to forget instead of nine — ProbingFindsThePendingFields catches an
        // entry going missing.
        var page = RenderEditor();
        var editor = page.Instance;

        foreach (var field in CountedFields(editor))
        {
            var entries = RegistryEntries(editor);
            var before = entries.Select(entry => entry.Count()).ToList();

            TryMakePending(editor, field);

            var moved = entries
                .Where((entry, index) => entry.Count() != before[index])
                .Select(entry => entry.Name)
                .ToList();

            Assert.True(
                moved.Count == 1,
                $"{field.Name} moved {moved.Count} registry entries ({string.Join(", ", moved)}); expected exactly one");

            await Invoke(page, "DiscardAsync");
        }
    }

    [Fact]
    public async Task DiscardClearsEveryPendingChange()
    {
        var page = RenderEditor();

        foreach (var field in CountedFields(page.Instance))
        {
            TryMakePending(page.Instance, field);
            Assert.True(PendingCount(page.Instance) > 0, field.Name);

            await Invoke(page, "DiscardAsync");

            Assert.Equal(0, PendingCount(page.Instance));
        }
    }

    [Fact]
    public async Task LoadingAPackageClearsEveryPendingChange()
    {
        // Loading a different version with edits still in hand would carry
        // them onto a package they were never written against.
        var page = RenderEditor();

        foreach (var field in CountedFields(page.Instance))
        {
            TryMakePending(page.Instance, field);
            Assert.True(PendingCount(page.Instance) > 0, field.Name);

            await Invoke(page, "LoadAsync");

            Assert.Equal(0, PendingCount(page.Instance));
        }
    }

    [Fact]
    public async Task EveryPendingChangeReachesTheSavedDraft()
    {
        // The failure this catches is silent: a field missing from
        // DraftPayload or from PendingChangedAsync is simply gone on reload,
        // with a pending badge that still counts it.
        //
        // Only the persist half is asserted. Restore deliberately drops
        // entries that no longer validate against the loaded package — a
        // synthetic probe is exactly such an entry, so a round-trip would
        // fail for a correct reason.
        var page = RenderEditor();

        foreach (var field in CountedFields(page.Instance))
        {
            var original = field.GetValue(page.Instance);
            TryMakePending(page.Instance, field);

            await Invoke(page, "PendingChangedAsync");

            var saved = JSInterop.Invocations["localStorage.setItem"];
            var json = (string)saved[^1].Arguments[1]!;

            Assert.True(
                json.Contains("probe", StringComparison.Ordinal),
                $"{field.Name} is counted as pending but never reaches the saved draft");

            Restore(page.Instance, field, original);
        }
    }
}
