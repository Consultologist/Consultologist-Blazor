namespace Consultologist.PackageFormat;

/// <summary>
/// v10 (#493): one shape for a declaration wherever it sits — an input, a
/// field of an object, or the element spec of an array — so the starter's
/// value check, the renderer, the publish-time probe and the fan's element
/// declaration recurse over the same thing instead of each fabricating a
/// WorkflowInputSpec for the level below. Type is the resolved name (text
/// when absent); Items and Fields are the next level down.
/// </summary>
public sealed record WorkflowDeclarationNode(
    string Type,
    string Label,
    bool Required,
    WorkflowDeclarationNode? Items,
    IReadOnlyList<WorkflowDeclarationNode>? Fields,
    IReadOnlyList<string>? Values,
    string Id = "")
{
    public static WorkflowDeclarationNode Of(WorkflowInputSpec input) =>
        new(WorkflowInputTypes.Of(input), input.Label, input.Required, ElementOf(input.Items, input.Fields, input.Values, input.Label), FieldsOf(input.Fields), ValuesOf(input.Items, input.Values), input.Id);

    public static WorkflowDeclarationNode Of(WorkflowFieldSpec field) =>
        new(WorkflowInputTypes.Of(field), field.Label, field.Required, ElementOf(field.Items, field.Fields, field.Values, field.Label), FieldsOf(field.Fields), ValuesOf(field.Items, field.Values), field.Id);

    /// <summary>
    /// The element of an array. A bare items (the v9 form) takes the array's
    /// own fields and values as the element's; a spec carries its own.
    /// </summary>
    public static WorkflowDeclarationNode? ElementOf(WorkflowElementSpec? items, List<WorkflowFieldSpec>? arrayFields, List<string>? arrayValues, string label)
    {
        if (items is null)
        {
            return null;
        }

        return items.IsBare
            ? new WorkflowDeclarationNode(items.Type, label, true, null, FieldsOf(arrayFields), items.Type == WorkflowInputTypes.Enum ? arrayValues : null)
            : new WorkflowDeclarationNode(items.Type, label, true, ElementOf(items.Items, items.Fields, items.Values, label), FieldsOf(items.Fields), items.Type == WorkflowInputTypes.Enum ? items.Values : null);
    }

    private static IReadOnlyList<WorkflowDeclarationNode>? FieldsOf(List<WorkflowFieldSpec>? fields) =>
        fields?.Select(Of).ToList();

    // The array's values belong to its elements when the elements are enums
    // (v9); an enum input's values are its own.
    private static IReadOnlyList<string>? ValuesOf(WorkflowElementSpec? items, List<string>? values) =>
        items is null || (items.IsBare && items.Type == WorkflowInputTypes.Enum) ? values : (items.IsBare ? null : values);

    public bool IsArray => Type == WorkflowInputTypes.Array;
    public bool IsObject => Type == WorkflowInputTypes.Object;
    public bool IsScalar => !IsArray && !IsObject;

    /// <summary>Deeper than one level: a field that is structure, or an element that is not a bare scalar/object of scalars.</summary>
    public bool IsDeeperThanOneLevel =>
        (Fields?.Any(f => !f.IsScalar) ?? false)
        || (Items is { } element && (element.IsArray || (element.Fields?.Any(f => !f.IsScalar) ?? false)));

    /// <summary>"a text", "an enum", "an array of arrays of number" — for a sentence about a declaration.</summary>
    public string Describe() => Type switch
    {
        WorkflowInputTypes.Array => Items is null ? "an array of text" : $"an array of {Items.DescribePlural()}",
        WorkflowInputTypes.Enum or WorkflowInputTypes.Object => $"an {Type}",
        _ => $"a {Type}"
    };

    private string DescribePlural() => Type switch
    {
        WorkflowInputTypes.Array => Items is null ? "arrays of text" : $"arrays of {Items.DescribePlural()}",
        _ => Type
    };
}
