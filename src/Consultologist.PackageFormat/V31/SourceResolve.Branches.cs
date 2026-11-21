namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the branches of a package (part 04 § 5; § 10.1's <c>case</c> rows): each
/// <c>case</c>'s subject (<see cref="ScopeTree.Subject"/>) held to an enumerated string, and its branches' values to
/// the subject's.
/// </summary>
public static partial class SourceResolve
{
    /// <summary>Every case, in source order.</summary>
    private static void Branches(ScopeTree tree, List<SourceRefusal> refusals)
    {
        foreach (var choice in tree.All().OfType<SourceCase>())
            Case(tree.Subject(choice), choice, tree.FormatsOf(tree.FrameOf(choice)), refusals);
    }

    /// <summary>
    /// One case: its subject's refusal, if any, alone; then S04-044 for a subject that is no enumerated string (an
    /// <c>enum</c>, or a <c>oneOf</c> of <c>const</c>s; nullable allowed), alone; then, in written order, S04-045 for
    /// each value that is not the subject's and S04-046 for each value an earlier branch lists; then, with no
    /// <c>else</c>, S04-047 for the values no branch lists and S04-075 for a subject that can be null.
    /// </summary>
    private static void Case(CaseSubject subject, SourceCase choice, FormatSet formats, List<SourceRefusal> refusals)
    {
        var path = SourceLayout.Join(choice.Path, "index.json");
        if (subject.Resolved.Refusal is { } refusal)
        {
            refusals.Add(refusal);
            return;
        }

        if (subject.Format == null)
            return;
        if (subject.Values is not { } values)
        {
            refusals.Add(ResolveSentences.SwitchesOnNoEnum(choice.Id, subject.Reference, TypeStrings.Print(subject.Format, formats), path));
            return;
        }

        var listed = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < choice.Branches.Count; i++)
        {
            var n = i + 1;
            foreach (var value in choice.Branches[i].When.Distinct(StringComparer.Ordinal))
            {
                if (!values.Contains(value, StringComparer.Ordinal))
                    refusals.Add(ResolveSentences.ListsNoValue(choice.Id, n, value, subject.Reference, Quoted(values), path));
                else if (listed.TryGetValue(value, out var first))
                    refusals.Add(ResolveSentences.ListsValueTwice(choice.Id, value, first, n, path));
                else
                    listed[value] = n;
            }
        }

        if (choice.Else != null)
            return;
        var unlisted = values.Where(v => !listed.ContainsKey(v)).ToList();
        if (unlisted.Count > 0)
            refusals.Add(ResolveSentences.ListsNotEveryValue(choice.Id, Quoted(unlisted), path));
        if (subject.Nullable)
            refusals.Add(ResolveSentences.NullSubjectWithNoElse(choice.Id, subject.Reference, path));
    }

    /// <summary>Values as part 04's rows show them: each in double quotes, joined by <c>, </c>.</summary>
    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"\"{v}\""));
}
