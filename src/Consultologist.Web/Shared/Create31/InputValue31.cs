using System.Text.Json.Nodes;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow.V31;

namespace Consultologist.Web.Shared.Create31;

/// <summary>What an <see cref="InputValue31"/> node holds: one answer, a group's fields, or an array's rows.</summary>
public enum InputValueKind { Scalar, Group, Rows }

/// <summary>
/// #1509 (Task 9): a specVersion 31 input's value on the Create page, as a mutable tree that mirrors its
/// <see cref="InputShape"/> (part 09 § 5.1). A value starts blank and is never defaulted: a boolean is unset until it is
/// answered (D2). <see cref="ToJson"/> is what is sent (D3): a blank value is <c>null</c>, which leaves a top-level input out
/// of <c>Inputs</c> and writes JSON <c>null</c> for a field inside an object or a row, so an object keeps every property;
/// a blank text is never <c>""</c>.
/// </summary>
public sealed class InputValue31
{
    private readonly List<InputValue31> rows = [];

    private InputValue31(InputShape shape)
    {
        Shape = shape;
        Kind = shape.Widget switch
        {
            InputWidget.Group => InputValueKind.Group,
            InputWidget.Rows => InputValueKind.Rows,
            _ => InputValueKind.Scalar,
        };
        Fields = Kind == InputValueKind.Group ? shape.Fields.Select(field => new InputValue31(field.Shape)).ToList() : [];
    }

    public static InputValue31 For(InputShape shape) => new(shape);

    /// <summary>Whether every part of the shape has a widget; an input that is not is not drawn (A12-006).</summary>
    public static bool IsSupported(InputShape shape) => shape.Widget switch
    {
        InputWidget.Unsupported => false,
        InputWidget.Group => shape.Fields.All(field => IsSupported(field.Shape)),
        InputWidget.Rows => shape.Items is { } items && IsSupported(items),
        _ => true,
    };

    public InputShape Shape { get; }

    public InputValueKind Kind { get; }

    /// <summary>A scalar's answer as typed: text, a choice's value, a date (YYYY-MM-DD) or a number's spelling.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>A boolean's answer; null is unset.</summary>
    public bool? Flag { get; set; }

    /// <summary>A group's fields, in the shape's (written) order.</summary>
    public IReadOnlyList<InputValue31> Fields { get; }

    /// <summary>An array's rows, in the order the clinician put them.</summary>
    public IReadOnlyList<InputValue31> Rows => rows;

    public bool IsBlank => Kind switch
    {
        InputValueKind.Group => Fields.All(member => member.IsBlank),
        InputValueKind.Rows => rows.Count == 0,
        _ => Shape.Widget == InputWidget.Boolean ? Flag is null : string.IsNullOrWhiteSpace(Text),
    };

    public InputValue31 AddRow()
    {
        var row = new InputValue31(Shape.Items!);
        rows.Add(row);
        return row;
    }

    public void RemoveRow(int index) => rows.RemoveAt(index);

    public void MoveRow(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || target < 0 || index >= rows.Count || target >= rows.Count)
            return;
        (rows[index], rows[target]) = (rows[target], rows[index]);
    }

    public void Clear()
    {
        Text = string.Empty;
        Flag = null;
        foreach (var field in Fields)
            field.Clear();
        rows.Clear();
    }

    /// <summary>What this value sends (D3); null when it is blank, or when a number does not parse (its problem says so).</summary>
    public JsonNode? ToJson()
    {
        if (IsBlank)
            return null;

        switch (Kind)
        {
            case InputValueKind.Group:
                var group = new JsonObject();
                for (var index = 0; index < Fields.Count; index++)
                    group[Shape.Fields[index].Id] = Fields[index].ToJson();
                return group;
            case InputValueKind.Rows:
                return new JsonArray(rows.Select(row => row.ToJson()).ToArray());
        }

        return Shape.Widget switch
        {
            InputWidget.Boolean => JsonValue.Create(Flag!.Value),
            InputWidget.Number or InputWidget.Integer => NumberProblem() is null ? JsonNode.Parse(Text.Trim()) : null,
            _ => JsonValue.Create(Text),
        };
    }

    /// <summary>
    /// The first thing wrong with this value, or null: <c>"Required."</c> for a blank that must be filled in (as the page
    /// says it today), a number that does not parse, an empty row, or a field's problem after its label.
    /// </summary>
    public string? Problem(InputShape shape, bool required)
    {
        if (IsBlank)
            return required ? "Required." : null;

        switch (Kind)
        {
            case InputValueKind.Group:
                for (var index = 0; index < Fields.Count; index++)
                {
                    var field = shape.Fields[index];
                    if (Fields[index].Problem(field.Shape, field.Required) is { } problem)
                        return $"{field.Label}: {problem}";
                }
                return null;
            case InputValueKind.Rows:
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].IsBlank)
                        return $"Row {index + 1} is empty; fill it in or remove it.";
                    if (rows[index].Problem(shape.Items!, required: true) is { } problem)
                        return $"Row {index + 1}: {problem}";
                }
                return null;
        }

        return NumberProblem();
    }

    private string? NumberProblem()
    {
        var spelling = Text.Trim();
        return Shape.Widget switch
        {
            InputWidget.Number when !ConsultInputValue.TryParseNumber(spelling, out _) =>
                "Must be a plain decimal number, like 12 or 1.50.",
            InputWidget.Integer when !ConsultInputValue.TryParseNumber(spelling, out _) || spelling.Contains('.') =>
                "Must be a whole number, like 12.",
            _ => null,
        };
    }

    /// <summary>Fills this value from what was sent or held (a re-attached run, a form response, a questionnaire file).</summary>
    public void Restore(JsonNode? node)
    {
        Clear();
        if (node is null)
            return;

        switch (Kind)
        {
            case InputValueKind.Group when node is JsonObject group:
                for (var index = 0; index < Fields.Count; index++)
                    Fields[index].Restore(group[Shape.Fields[index].Id]);
                return;
            case InputValueKind.Rows when node is JsonArray array:
                foreach (var element in array)
                    AddRow().Restore(element);
                return;
            case InputValueKind.Scalar when node is JsonValue scalar:
                if (Shape.Widget == InputWidget.Boolean)
                    Flag = scalar.TryGetValue<bool>(out var flag) ? flag : null;
                else
                    Text = scalar.TryGetValue<string>(out var text) ? text : scalar.ToJsonString();
                return;
        }
    }
}
