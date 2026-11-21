using System.Globalization;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.3, 2.1): which spec versions this engine admits. The compiled-in lists
/// (<see cref="WorkflowPackageValidator.AcceptedSpecVersions"/>, <see cref="WorkflowPackageStore.SupportedSpecVersions"/>)
/// stay 5–30 until the bump; <c>WorkflowPackages__PreviewSpecVersions</c> (dev only) admits each listed version beside
/// them. A listed version this build doesn't implement, one already released, or an entry that isn't an integer stops the
/// host at startup (Program.cs resolves this before the host runs), naming the setting and the value: a typo must not
/// silently admit nothing, and an unimplemented version must never be admitted.
/// </summary>
public sealed class SpecVersionAdmission
{
    /// <summary>The configuration key; <c>WorkflowPackages__PreviewSpecVersions</c> as an app setting.</summary>
    public const string PreviewSetting = "WorkflowPackages:PreviewSpecVersions";

    private const string PreviewSettingName = "WorkflowPackages__PreviewSpecVersions";

    /// <summary>The versions this build implements but has not released: the only values the preview setting may list.</summary>
    public static readonly IReadOnlyList<int> Implemented = [31];

    public SpecVersionAdmission(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Preview = Read(configuration[PreviewSetting]);
    }

    /// <summary>The preview versions admitted beside the compiled-in list, sorted and distinct; empty when the setting is unset.</summary>
    public IReadOnlyList<int> Preview { get; }

    /// <summary>Whether a package at this version is admitted: the compiled-in supported list, or a preview version.</summary>
    public bool Admits(int specVersion) =>
        WorkflowPackageStore.SupportedSpecVersions.Contains(specVersion) || Preview.Contains(specVersion);

    /// <summary>Whether this deployment runs specVersion 31.</summary>
    public bool Runs31 => Admits(31);

    /// <summary>S09-001: the version is not admitted, with the accepted list and the preview versions after it, in order.</summary>
    public string NotSupported(int specVersion) =>
        $"specVersion {specVersion.ToString(CultureInfo.InvariantCulture)} is not supported: this engine accepts specVersion {DescribeAdmitted()} (pre-v5 packages are archived; see registry-operations.md).";

    // "5, 6, … 30 or 31": the validator's wording, the preview versions joining its end.
    private string DescribeAdmitted()
    {
        if (Preview.Count == 0)
        {
            return WorkflowPackageValidator.DescribeAcceptedSpecVersions();
        }

        var versions = WorkflowPackageValidator.AcceptedSpecVersions.Concat(Preview)
            .Select(v => v.ToString(CultureInfo.InvariantCulture))
            .ToList();
        return $"{string.Join(", ", versions.Take(versions.Count - 1))} or {versions[^1]}";
    }

    private static IReadOnlyList<int> Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var preview = new SortedSet<int>();
        foreach (var raw in value.Split(','))
        {
            var entry = raw.Trim();
            if (!int.TryParse(entry, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidOperationException(
                    $"{PreviewSettingName} lists '{entry}', which is not a spec version: write a comma-separated list of integers, for example 31.");
            }

            if (WorkflowPackageValidator.AcceptedSpecVersions.Contains(version) || WorkflowPackageStore.SupportedSpecVersions.Contains(version))
            {
                throw new InvalidOperationException(
                    $"{PreviewSettingName} lists '{entry}', which this build already admits; list only versions it implements but has not released ({Describe(Implemented)}).");
            }

            if (!Implemented.Contains(version))
            {
                throw new InvalidOperationException(
                    $"{PreviewSettingName} lists '{entry}', which this build doesn't implement; it may list only {Describe(Implemented)}.");
            }

            preview.Add(version);
        }

        return preview.ToArray();
    }

    private static string Describe(IEnumerable<int> versions) =>
        string.Join(", ", versions.Select(v => v.ToString(CultureInfo.InvariantCulture)));
}
