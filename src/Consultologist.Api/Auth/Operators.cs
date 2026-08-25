namespace Consultologist.Api.Auth;

/// <summary>
/// #384: who may call operator surfaces. Not a role — an ordinary account
/// whose AppUserId is listed in Operators__AppUserIds. Nobody by default.
/// </summary>
public static class Operators
{
    public const string SettingName = "Operators__AppUserIds";

    private static readonly Lazy<IReadOnlySet<string>> Configured = new(() => Parse(Environment.GetEnvironmentVariable(SettingName)));

    public static IReadOnlySet<string> Parse(string? setting) =>
        (setting ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    public static bool IsOperator(AppAccount account) => IsOperator(account, Configured.Value);

    public static bool IsOperator(AppAccount account, IReadOnlySet<string> operators) =>
        operators.Contains(account.AppUserId);
}
