using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.2 to 2.4, 2.6, 3.2, 5, 6.3, 7.2; part 08 § 4.2; D1, D12, D13): the
/// run-time render of one entry's body from its published plan (<c>resolve.json</c>). The body is rendered with Scriban
/// 7.2.5 in the invariant culture with strict variables over the run's data, a local head read through the entry's
/// <c>names</c> (D1). A placed snippet or part renders its own text in the same data and is spliced where it is placed;
/// one not placed renders nothing, so the tag's own whitespace marks leave § 2.6's empty line. A pipe into a renderer
/// renders the renderer's body with the piped value as <c>value</c> and gives one <see cref="ComposedRender"/> per step,
/// numbered as the entry's <c>renders</c> (<see cref="Compose"/>). Only the seven built-ins exist; values are written
/// by D13 (<see cref="TemplateValues"/>). Only package text (a body, a snippet's, a part's, a macro's instruction, a
/// renderer's) is ever parsed; a value never is. In a loop over a snippet folder, <c>item.content</c> renders the item's
/// body from the plan (part 05 § 6.2). Each run spot's output is recorded where it is written, which gives the run-neutral
/// text from the same render (part 05 § 4). A macro offered renders its instruction, its own marker written as the marker
/// to copy (D17), and its body is rendered for the reply (part 05 §§ 8.3, 8.4; D5, D14).
/// </summary>
public static partial class BodyComposer
{
    /// <summary>Part 05 § 2.3: the iterations one <c>for</c> may run, each time it runs (S05-012).</summary>
    public const int IterationLimit = 1_000;

    /// <summary>Part 05 § 2.3: the bytes of UTF-8 a composition may write (S05-014).</summary>
    public const int OutputLimit = 1_048_576;

    /// <summary>
    /// The deepest a unit may stand inside the units that place it: past the publish limit of a snippet chain (15,
    /// D12), so that a document publish did not write cannot recurse without bound.
    /// </summary>
    internal const int UnitDepth = 4 * DialectRules.NestingLimit;

    /// <summary>
    /// Composes the body of <paramref name="request"/>'s entry (<c>plan.Functions[function].Entries[key]</c> inside a
    /// function, else <c>plan.Entries[key]</c>). An entry the plan lacks, one with no body, or a text a placement or a
    /// renderer reaches that is not a dialect template (it does not parse, or breaks a statement rule) is not what publish
    /// wrote: S07-006. A <c>for</c> past <see cref="IterationLimit"/> is S05-012 at its line in the text that holds it;
    /// output past <see cref="OutputLimit"/> S05-014; <c>array.join</c> over a <c>null</c> element A07-006; the steps
    /// (each iteration of any <c>for</c>, in the body, its units and its renderers) past <see cref="ComposeRequest.StepLimit"/>
    /// <see cref="Composition.TooManySteps"/>; any other failure A06-008, never Scriban's message. Never throws on the
    /// plan, the data or the decisions; a null argument is the caller's error.
    /// </summary>
    /// <remarks>
    /// Renders are numbered as the entry's <c>renders</c> are (part 07 § 2.5, Task 7): one number per pipe step into a
    /// renderer in the body and in every unit its <c>placements</c> name, in order of appearance with each unit's text
    /// walked where it is placed (a macro's instruction and the snippets placed there included, its body not). The numbers
    /// stand whether a unit is placed or not; a macro body's renders, substituted after the reply, are numbered after the
    /// entry's own (<see cref="Composing.Numbered"/> + 1 onward), each offered macro's body in turn in prompt order.
    /// </remarks>
    public static Composition Compose(RunPlan plan, ComposeRequest request)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.EntryKey);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.Placed);

        var notPublished = ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence;
        PlanFunction? function = null;
        if (request.Function != null && !plan.Functions.TryGetValue(request.Function, out function))
            return Failed(notPublished);
        var entry = function == null ? plan.Entries.GetValueOrDefault(request.EntryKey) : function.Entries.GetValueOrDefault(request.EntryKey);
        if (entry == null || SourceLayout.Text(entry.Json["body"]) is not { } body)
            return Failed(notPublished);

        var composing = new Composing(plan, entry, function, request, notPublished);
        try
        {
            if (composing.Plan(body, null, 0) is not { } top)
                return Failed(notPublished);
            var text = composing.Render(top, composing.Globals(), out var spots);
            if (Encoding.UTF8.GetByteCount(text) > OutputLimit)
                return Failed(RunSentences.OutputPassedLimit(composing.Subject));
            return new Composition(
                text, NeutralText(text, spots), null, false, [.. spots.Select(s => s.Composed())], [.. composing.Renders], [.. composing.Offered], composing.PlacedUnits());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // What the render or Scriban threw is the body's failure: said by its own sentence when it stopped at a limit,
            // else A06-008, which never quotes Scriban's message (it can quote a value).
            var stop = composing.Stop ?? Stopped(exception);
            if (stop is { TooManySteps: true })
                return new Composition(null, null, null, true, [], [], [], []);
            return Failed(stop?.Sentence ?? RunSentences.CouldNotRender(composing.Subject));
        }
    }

    private static Composition Failed(string sentence) => new(null, null, sentence, false, [], [], [], []);

    /// <summary>The <see cref="ComposeStop"/> in <paramref name="exception"/>'s chain (Scriban wraps what a member or a function throws), or null.</summary>
    private static ComposeStop? Stopped(Exception exception)
    {
        var depth = 0;
        for (Exception? at = exception; at != null && depth++ < 64; at = at.InnerException)
        {
            if (at is ComposeStop stop)
                return stop;
        }

        return null;
    }

    /// <summary>
    /// One composition: its entry, its data and decisions, the step budget spent, the first stop met, the renders run and
    /// the units placed, and every text it has parsed (each once).
    /// </summary>
    internal sealed partial class Composing(RunPlan plan, PlanEntry entry, PlanFunction? function, ComposeRequest request, string notPublished)
    {
        private readonly Dictionary<string, (Template Template, TemplateSyntax Syntax)?> parsed = new(StringComparer.Ordinal);
        private readonly List<ComposedUnit?> units = [];
        private readonly ScriptObject builtins = TemplateValues.BuiltIns();
        private int planned;
        private ScriptObject? data;

        public RunPlan RunPlan { get; } = plan;

        public PlanEntry Entry { get; } = entry;

        public ComposeRequest Request { get; } = request;

        /// <summary>The entry's subject in sentences (<c>Node 'x'</c>, <c>… in function 'f'</c>).</summary>
        public string Subject { get; } = RunSentences.Subject(entry, request.Function);

        /// <summary>S07-006 for the plan's package: what a text publish did not write is said with.</summary>
        public string NotPublished { get; } = notPublished;

        /// <summary>The iterations run so far, across the body, its units and its renderers (D12).</summary>
        public int Steps { get; set; }

        /// <summary>The first stop met, kept should Scriban wrap or drop the exception that carried it.</summary>
        public ComposeStop? Stop { get; private set; }

        /// <summary>The renderer steps run, in the order run.</summary>
        public List<ComposedRender> Renders { get; } = [];

        /// <summary>How many pipe steps into a renderer the entry's own texts number (the last <see cref="ComposedRender.N"/> of its <c>renders</c>).</summary>
        public int Numbered { get; private set; }

        /// <summary>Records <paramref name="stop"/> (the first one wins) and gives it to throw.</summary>
        public ComposeStop Stopped(ComposeStop stop)
        {
            Stop ??= stop;
            return stop;
        }

        /// <summary>The macros offered, in prompt order (part 05 § 8.5).</summary>
        public List<OfferedMacro> Offered { get; } = [];

        /// <summary>The units placed, in order of appearance (a unit before the units its text places).</summary>
        public IReadOnlyList<ComposedUnit> PlacedUnits() => units.OfType<ComposedUnit>().ToList();

        /// <summary>
        /// The data the body reads, as Scriban sees it: each root of the scope (<c>item</c>, in a loop over a snippet folder,
        /// rendering its body where it is read: <see cref="FolderItemObject"/>), each local head of <c>names</c> set to what
        /// its rooted key holds (D1), and the roots a body places and pipes through (<c>snippet</c>, <c>part</c>,
        /// <c>macro</c>, <c>renderer</c>), which no scope value stands for.
        /// </summary>
        public ScriptObject Globals() => data ??= MakeGlobals();

        private ScriptObject MakeGlobals()
        {
            var globals = new ScriptObject();
            foreach (var (root, value) in Request.Scope)
                globals.SetValue(root, TemplateValues.FromJson(value), false);
            if (globals.TryGetValue("item", out var item))
                globals.SetValue("item", FolderItemObject.Of(this, item), false); // part 05 § 6.2: a folder item's body is rendered where read

            if (Entry.Json["names"] is JsonObject names)
            {
                foreach (var (head, rooted) in names)
                {
                    if (SourceLayout.Text(rooted) is { } key && Find(globals, key.Split('.'), out var found))
                        SetAt(globals, head.Split('.'), found);
                }
            }

            globals.SetValue("snippet", new SnippetRoot(this, null), false);
            globals.SetValue("part", new PartRoot(this), false);
            globals.SetValue("macro", new MacroRoot(this, null), false);
            globals.SetValue("renderer", new RendererRoot(this), false);
            return globals;
        }

        private static bool Find(ScriptObject root, string[] segments, out object? found)
        {
            object? at = root;
            foreach (var segment in segments)
            {
                if (at is not ScriptObject obj || !obj.TryGetValue(segment, out at))
                {
                    found = null;
                    return false;
                }
            }

            found = at;
            return true;
        }

        private static void SetAt(ScriptObject root, string[] segments, object? value)
        {
            var at = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!at.TryGetValue(segments[i], out var next) || next is not ScriptObject inner)
                {
                    inner = new DataObject(new JsonObject());
                    at.SetValue(segments[i], inner, false);
                }

                at = inner;
            }

            at.SetValue(segments[^1], value, false);
        }

        /// <summary>
        /// <paramref name="text"/> read once: its Scriban template and its dialect syntax, or <c>null</c> when it is not a
        /// dialect template publish would have written (it does not parse, or breaks a statement rule).
        /// </summary>
        private (Template Template, TemplateSyntax Syntax)? Parse(string text)
        {
            if (parsed.TryGetValue(text, out var known))
                return known;
            (Template, TemplateSyntax)? read = null;
            var syntax = TemplateSyntax.Parse(text);
            if (syntax.Faults.Count == 0)
            {
                var template = Template.Parse(text);
                if (!template.HasErrors && template.Page != null)
                    read = (template, syntax);
            }

            parsed[text] = read;
            return read;
        }

        /// <summary>Renders <paramref name="text"/> over <paramref name="globals"/> in a context of its own, sharing this composition's budget.</summary>
        public string Render(TextPlan text, ScriptObject globals) => Render(text, globals, out _);

        /// <summary>
        /// Renders <paramref name="text"/> as <see cref="Render(TextPlan, ScriptObject)"/> does, giving the run spots written
        /// in it, at their places in the text it gives (<paramref name="spots"/>, in the order of the text).
        /// </summary>
        public string Render(TextPlan text, ScriptObject globals, out IReadOnlyList<FilledSpot> spots)
        {
            var output = new LimitedOutput(this);
            var context = new ComposeContext(this, text, builtins, output);
            context.PushGlobal(globals);
            text.Template.Render(context);
            spots = context.Spots;
            return output.ToString();
        }

        /// <summary>
        /// Renders the unit <paramref name="placement"/> places when its site is placed (<see cref="ComposeRequest.Placed"/>),
        /// recording it in order of appearance, with the run spots written in it; <c>null</c>, which writes nothing, when it
        /// is not. A macro is offered as <see cref="PlaceMacro"/> says.
        /// </summary>
        public PlacedText? Place(Placement placement)
        {
            if (placement.Kind == "macro")
                return PlaceMacro(placement);
            if (!Request.Placed.TryGetValue(placement.Site, out var placed) || !placed)
                return null;
            var index = units.Count;
            units.Add(null);
            var rendered = Render(placement.Text, Globals(), out var spots);
            units[index] = new ComposedUnit(placement.Site, CanonicalJson.HashText(rendered));
            return new PlacedText(rendered, spots);
        }
    }

    /// <summary>
    /// #1504 (specVersion 31, area 07): one text as this composition renders it at one place: its template and syntax, the
    /// site whose text it is (<c>null</c> for the body, <c>renderer.&lt;id&gt;</c> for a renderer), and by the source offset
    /// of each tag that places a unit or pipes into a renderer, what stands there.
    /// </summary>
    internal sealed class TextPlan(string? site, Template template, TemplateSyntax syntax)
    {
        public string? Site { get; } = site;

        public Template Template { get; } = template;

        public TemplateSyntax Syntax { get; } = syntax;

        /// <summary>Each placement by the offset of its unit's path.</summary>
        public Dictionary<int, Placement> Placements { get; } = [];

        /// <summary>Each pipe step into a renderer by the offset of the step.</summary>
        public Dictionary<int, RenderStep> Renders { get; } = [];

        /// <summary>Each output statement that is one of the entry's run spots, by the statement's offset.</summary>
        public Dictionary<int, PlannedSpot> Spots { get; } = [];

        /// <summary>In a macro's instruction, the output statement holding the macro's own marker, by its offset, with what it writes (D17).</summary>
        public Dictionary<int, string> Markers { get; } = [];

        /// <summary>
        /// #1505 (area 08; part 06 § 6.4; D7): each output statement whose value is escaped where it is written (one of the
        /// entry's <c>escapes</c>), by the statement's offset: a placement among them is its unit's text escaped whole.
        /// </summary>
        public HashSet<int> Escapes { get; } = [];
    }

    /// <summary>A unit placed at one tag: its <paramref name="Site"/>, its kind (<c>snippet</c>, <c>part</c>, <c>macro</c>), and its text planned at that place (a macro's instruction).</summary>
    internal sealed record Placement(string Site, string Kind, TextPlan Text);

    /// <summary>A pipe step into the renderer <paramref name="Renderer"/> (its id): its number for the entry, its source, and whether it stands in a run spot.</summary>
    internal sealed record RenderStep(int N, string Renderer, string Source, bool RunDependent);
}

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 2.3, 2.4, 2.6; D12, D13): the Scriban context one text renders in: the
/// seven built-ins only, strict variables, the invariant culture, no automatic indentation (a value's lines are written
/// as they are), each value written by D13, an empty list false, and the limits: each <c>for</c>'s iterations, the
/// composition's steps and its output.
/// </summary>
internal sealed class ComposeContext : TemplateContext
{
    private readonly Stack<int> iterations = new();

    public ComposeContext(BodyComposer.Composing composing, BodyComposer.TextPlan text, ScriptObject builtins, LimitedOutput output)
        : base(builtins)
    {
        Composing = composing;
        Text = text;
        Written = output;
        StrictVariables = true;
        LoopLimit = 0; // each for is counted here instead (OnStepLoop)
        AutoIndent = false;
        PushCulture(CultureInfo.InvariantCulture);
        PushOutput(output);
    }

    public BodyComposer.Composing Composing { get; }

    /// <summary>The text being rendered, whose tags say what is placed, which renderer step is which and which output is a run spot.</summary>
    public BodyComposer.TextPlan Text { get; }

    /// <summary>The text's output, whose length is where the next write stands.</summary>
    public LimitedOutput Written { get; }

    /// <summary>The run spots written to <see cref="Written"/>, at their places in it, in the order written (part 05 § 4).</summary>
    public List<BodyComposer.FilledSpot> Spots { get; } = [];

    public string Subject => Composing.Subject;

    /// <summary>Records and gives the stop to throw: <paramref name="sentence"/>, or A06-008 for <c>null</c>.</summary>
    public ComposeStop Stopped(string? sentence) => Composing.Stopped(new ComposeStop(sentence));

    /// <summary>
    /// A run spot's output statement writes itself, recording where its output stands (part 05 § 4); a macro's own marker in
    /// its instruction writes the marker as the model is to copy it, never evaluated (D17); #1505 (area 08, D7): an output
    /// the entry's <c>escapes</c> lists writes its value escaped (a run spot's recorded output too); any other node
    /// evaluates as Scriban's.
    /// </summary>
    public override object? Evaluate(ScriptNode? scriptNode, bool aliasReturnedFunction)
    {
        if (scriptNode is not ScriptExpressionStatement statement)
            return base.Evaluate(scriptNode, aliasReturnedFunction);
        var offset = BodyComposer.Offset(statement);
        if (Text.Markers.TryGetValue(offset, out var literal))
        {
            Write(literal);
            return null;
        }

        var escaped = Text.Escapes.Contains(offset);
        if (Text.Spots.TryGetValue(offset, out var spot))
        {
            Composing.WriteSpot(this, spot, () => base.Evaluate(statement, aliasReturnedFunction), escaped);
            return null;
        }

        if (!escaped)
            return base.Evaluate(scriptNode, aliasReturnedFunction);
        WriteValue(base.Evaluate(statement, aliasReturnedFunction), escape: true);
        return null;
    }

    /// <summary>D13's writing of a value; a placed unit's text is spliced, its run spots moved to where it stands.</summary>
    public override TemplateContext Write(SourceSpan span, object? textAsObject)
    {
        WriteValue(textAsObject, escape: false);
        return this;
    }

    /// <summary>
    /// <paramref name="value"/> written by D13 (<c>null</c> writes nothing), a placed unit's text spliced with its run spots
    /// moved to where it stands; with <paramref name="escape"/>, as plain text in Markdown (<see cref="MarkdownEscape"/>,
    /// a placed unit's text whole, its spots' outputs with it).
    /// </summary>
    private void WriteValue(object? value, bool escape)
    {
        if (value is BodyComposer.PlacedText placed)
        {
            if (escape)
                placed = BodyComposer.Escaped(placed);
            var start = Written.Length;
            if (placed.Text.Length > 0)
                Write(placed.Text);
            Spots.AddRange(placed.Spots.Select(s => s with { Start = s.Start + start }));
            return;
        }

        if (value is null)
            return;
        var text = TemplateValues.Written(value) ?? throw Stopped(null);
        if (escape)
            text = MarkdownEscape.Escape(text);
        if (text.Length > 0)
            Write(text);
    }

    public override bool ToBool(SourceSpan span, object? value) =>
        value is not ScriptArray { Count: 0 } && base.ToBool(span, value);

    protected override void OnEnterLoop(ScriptLoopStatementBase loop)
    {
        base.OnEnterLoop(loop);
        iterations.Push(0);
    }

    protected override void OnExitLoop(ScriptLoopStatementBase loop)
    {
        if (iterations.Count > 0)
            iterations.Pop();
        base.OnExitLoop(loop);
    }

    protected override bool OnStepLoop(ScriptLoopStatementBase loop)
    {
        if (iterations.Count > 0)
        {
            var count = iterations.Pop() + 1;
            iterations.Push(count);
            if (count > BodyComposer.IterationLimit)
                throw Stopped(RunSentences.ForReachedLimit(Subject, Math.Max(loop.Span.Start.Line, 0) + 1));
        }

        if (++Composing.Steps > Composing.Request.StepLimit)
            throw Composing.Stopped(new ComposeStop(null, tooManySteps: true));
        return base.OnStepLoop(loop);
    }
}

/// <summary>#1504 (specVersion 31, area 07; part 05 § 2.3, D12): a text's output, counted in UTF-8 against the composition's limit as it is written (S05-014).</summary>
internal sealed class LimitedOutput(BodyComposer.Composing composing) : IScriptOutput
{
    private readonly StringBuilder builder = new();
    private long bytes;

    /// <summary>The characters written so far (UTF-16 code units, as <see cref="string"/> indexes them).</summary>
    public int Length => builder.Length;

    public void Write(string text, int offset, int count)
    {
        bytes += Encoding.UTF8.GetByteCount(text.AsSpan(offset, count));
        if (bytes > BodyComposer.OutputLimit)
            throw composing.Stopped(new ComposeStop(RunSentences.OutputPassedLimit(composing.Subject)));
        builder.Append(text, offset, count);
    }

    public ValueTask WriteAsync(string text, int offset, int count, CancellationToken cancellationToken)
    {
        Write(text, offset, count);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => builder.ToString();
}
