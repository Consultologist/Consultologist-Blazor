using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// The format-file rules of package-format-v25-design.md §§ 3.1–3.2 as named
/// sentences: the type and producer vocabularies, the origin rule (a
/// package-local file is always custom), what each producer's file must and
/// must not carry, and the parameter declarations. One validator for a
/// catalogue file and a package-local one; <paramref name="origin"/> is the
/// only thing the lister knows that the file does not.
/// </summary>
public static class WorkflowFormatValidator
{
    // A parameter name is a key in the node's `with` — a plain identifier, as
    // the manifest's own keys are (failWith beside of and in).
    private static readonly Regex ParameterNamePattern = new("^[a-z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public static bool IsValidParameterName(string? name) => name != null && ParameterNamePattern.IsMatch(name);

    /// <summary>
    /// Adds one sentence per broken rule, each opening with
    /// <paramref name="subject"/> ("Format 'medication_list'").
    /// </summary>
    public static void Validate(string subject, WorkflowFormatSpec spec, WorkflowFormatOrigin origin, List<string> errors)
    {
        var vocabulary = true;

        if (!WorkflowInputTypes.All.Contains(spec.Type, StringComparer.Ordinal))
        {
            errors.Add($"{subject} type '{spec.Type}' is not one of {string.Join(", ", WorkflowInputTypes.All)}.");
            vocabulary = false;
        }

        if (!WorkflowFormatProducers.IsKnown(spec.Producer))
        {
            errors.Add($"{subject} producer '{spec.Producer}' is not one of {string.Join(", ", WorkflowFormatProducers.All)}.");
            vocabulary = false;
        }

        if (!vocabulary)
        {
            // Every rule below reads the type or the producer; cascading
            // sentences would name consequences of the one defect.
            return;
        }

        if (spec.Description is not null && string.IsNullOrWhiteSpace(spec.Description))
        {
            errors.Add($"{subject} description must not be blank.");
        }

        var type = spec.Type;
        var producer = spec.Producer;
        var isText = type == WorkflowInputTypes.Text;
        var isEnum = type == WorkflowInputTypes.Enum;
        var isObject = type == WorkflowInputTypes.Object;

        ValidateOrigin(subject, producer, origin, errors);
        ValidateProducerShape(subject, producer, type, errors);
        ValidateOutputMacros(subject, spec, isText, errors);
        ValidateSchema(subject, spec, isObject, errors);
        ValidateValues(subject, spec, isEnum, errors);
        ValidateParameters(subject, spec, isEnum, errors);
    }

    private static void ValidateOrigin(string subject, string producer, WorkflowFormatOrigin origin, List<string> errors)
    {
        if (origin == WorkflowFormatOrigin.PackageLocal && producer != WorkflowFormatProducers.Custom)
        {
            errors.Add($"{subject} is package-local and must declare producer custom; attested, rendered and computed formats come from the catalogue.");
        }
    }

    private static void ValidateProducerShape(string subject, string producer, string type, List<string> errors)
    {
        switch (producer)
        {
            case WorkflowFormatProducers.Custom when type != WorkflowInputTypes.Object:
                errors.Add($"{subject} declares producer custom, which requires type object (the package's own shape, validated by the engine).");
                break;
            case WorkflowFormatProducers.Computed when type != WorkflowInputTypes.Object:
                errors.Add($"{subject} declares producer computed, which requires type object (the operation's result shape).");
                break;
            case WorkflowFormatProducers.Rendered when type != WorkflowInputTypes.Text:
                errors.Add($"{subject} declares producer rendered, which requires type text (the output is the rendered prompt's bytes).");
                break;
        }
    }

    private static void ValidateOutputMacros(string subject, WorkflowFormatSpec spec, bool isText, List<string> errors)
    {
        if (!spec.AllowsOutputMacros)
        {
            return;
        }

        if (!isText)
        {
            errors.Add($"{subject} declares outputMacros, which requires type text; macros are placed on prose only.");
        }

        if (spec.Producer is WorkflowFormatProducers.Rendered or WorkflowFormatProducers.Computed)
        {
            errors.Add($"{subject} declares outputMacros, which a {spec.Producer} format may not: no agent writes its output, so nothing can be placed on it.");
        }
    }

    private static void ValidateSchema(string subject, WorkflowFormatSpec spec, bool isObject, List<string> errors)
    {
        if (spec.Schema is null)
        {
            if (isObject)
            {
                errors.Add($"{subject} is type 'object' and must declare a schema (every object format carries its shape; the engine validates replies against it).");
            }

            return;
        }

        if (!isObject)
        {
            errors.Add($"{subject} declares a schema, which requires type object.");
            return;
        }

        if (spec.Schema is not JsonObject)
        {
            errors.Add($"{subject} schema must be a JSON object.");
            return;
        }

        WorkflowPackageValidator.ValidateStructuredOutputSubset($"{subject} schema", spec.Schema, errors);
    }

    private static void ValidateValues(string subject, WorkflowFormatSpec spec, bool isEnum, List<string> errors)
    {
        var declaresValues = spec.Values is not null;
        var valuesParameter = spec.ValuesParameter is not null;

        if (!isEnum)
        {
            if (declaresValues)
            {
                errors.Add($"{subject} declares values, which requires type enum.");
            }

            return;
        }

        if (declaresValues && valuesParameter)
        {
            errors.Add($"{subject} declares values and a parameter of kind values; an enum's answer set comes from exactly one.");
            return;
        }

        if (!declaresValues && !valuesParameter)
        {
            errors.Add($"{subject} is type 'enum' and must declare values or a parameter of kind values.");
            return;
        }

        if (declaresValues)
        {
            WorkflowPackageValidator.ValidateEnumValues(subject, spec.Values, errors);
        }
    }

    private static void ValidateParameters(string subject, WorkflowFormatSpec spec, bool isEnum, List<string> errors)
    {
        var producer = spec.Producer;

        if (spec.Parameters is null)
        {
            if (producer == WorkflowFormatProducers.Computed)
            {
                errors.Add($"{subject} declares producer computed and must declare at least one parameter (the operation's operands).");
            }

            return;
        }

        if (!WorkflowFormatProducers.TakesParameters(producer))
        {
            errors.Add($"{subject} declares parameters, which a {producer} format may not; only attested and computed formats take input through with.");
            return;
        }

        if (spec.Parameters.Count == 0 && producer == WorkflowFormatProducers.Computed)
        {
            errors.Add($"{subject} declares producer computed and must declare at least one parameter (the operation's operands).");
            return;
        }

        var valuesParameters = 0;

        foreach (var (name, parameter) in spec.Parameters)
        {
            if (!IsValidParameterName(name))
            {
                errors.Add($"{subject} parameter '{name}' must be an identifier (a lowercase letter, then letters, digits or underscores).");
            }

            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Kind))
            {
                errors.Add($"{subject} parameter '{name}' must declare a kind (one of {string.Join(", ", WorkflowFormatParameterKinds.All)}).");
                continue;
            }

            if (!WorkflowFormatParameterKinds.IsKnown(parameter.Kind))
            {
                errors.Add($"{subject} parameter '{name}' kind '{parameter.Kind}' is not one of {string.Join(", ", WorkflowFormatParameterKinds.All)}.");
                continue;
            }

            if (parameter.Format is not null)
            {
                if (parameter.Kind != WorkflowFormatParameterKinds.Node)
                {
                    errors.Add($"{subject} parameter '{name}' declares a format, which only a node parameter may (the format the named node must have).");
                }
                else if (string.IsNullOrWhiteSpace(parameter.Format))
                {
                    errors.Add($"{subject} parameter '{name}' format must not be blank.");
                }
            }

            if (parameter.Kind == WorkflowFormatParameterKinds.Values)
            {
                valuesParameters++;

                if (!isEnum)
                {
                    errors.Add($"{subject} parameter '{name}' is kind values, which only an enum format may declare.");
                }
            }
        }

        if (valuesParameters > 1)
        {
            errors.Add($"{subject} declares more than one parameter of kind values; an enum has one answer set.");
        }
    }
}
