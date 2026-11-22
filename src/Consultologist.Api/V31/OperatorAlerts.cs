using System.Globalization;
using System.Text;
using Consultologist.Api.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): one alert for the operator. <paramref name="Kind"/> is one of
/// <see cref="OperatorAlertKinds"/>; <paramref name="JobId"/> the job it is about, if any; <paramref name="Key"/> the row
/// key it is about (package text), if any; <paramref name="Number"/> a count or minutes, if any. Never a value from an
/// input, a model or a profile.
/// </summary>
public sealed record OperatorAlert(string Kind, string? JobId, string? Key, int? Number);

/// <summary>#1505 (specVersion 31, area 08; part 09 § 3.9; D14): the kinds of <see cref="OperatorAlert"/>.</summary>
public static class OperatorAlertKinds
{
    /// <summary>A job failed (timed out, or failed for an engine fault, included). <c>Key</c> is the row that failed, if any.</summary>
    public const string JobFailed = "job-failed";

    /// <summary>A job's 429 waits passed 5 minutes in all. <c>Key</c> is the row whose wait passed it; <c>Number</c> the minutes.</summary>
    public const string CapacityWait = "capacity-wait";

    /// <summary>A job reached 80% of its <c>jobMinutes</c>. <c>Number</c> is the minutes it has run.</summary>
    public const string JobSlow = "job-slow";

    /// <summary>A delivery row was marked <c>failed</c>. <c>Key</c> is the first such row; <c>Number</c> how many there are.</summary>
    public const string DeliveryFailed = "delivery-failed";

    /// <summary>A publish failed (area 10's to raise).</summary>
    public const string PublishFailed = "publish-failed";

    /// <summary>An exporter warned (area 11's to raise).</summary>
    public const string ExporterWarning = "exporter-warning";
}

/// <summary>#1505 (specVersion 31, area 08; part 09 § 3.9; D14): tells the operator of an <see cref="OperatorAlert"/>.</summary>
public interface IOperatorAlerts
{
    /// <summary>Logs <paramref name="alert"/> and mails it to the operator when an address and the mailbox are set. Never throws for a mail fault.</summary>
    Task RaiseAsync(OperatorAlert alert, CancellationToken ct);
}

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): every alert is logged as a warning (<c>Operator alert. Kind=…</c>), its
/// kind, job, key and number; and, when <c>Operators__AlertAddress</c> is set and the mailbox is configured
/// (<c>EmailIntake__MailboxAddress</c>, the mailbox the delivery channels send from), mailed there from the mailbox. The
/// message's subject and body hold the kind, a fixed sentence for it, the job's id, the key and the number, nothing else.
/// A mail fault is logged (its type only) and never reaches the caller.
/// </summary>
public sealed class OperatorAlerts(IGraphMailClient mail, IConfiguration configuration, ILogger<OperatorAlerts> logger) : IOperatorAlerts
{
    /// <summary>The configuration key of the operator's address; <c>Operators__AlertAddress</c> as an app setting.</summary>
    public const string AddressSetting = "Operators:AlertAddress";

    /// <summary>The configuration key of the mailbox alerts are sent from, as the delivery channels read it.</summary>
    internal const string MailboxSetting = "EmailIntake:MailboxAddress";

    public async Task RaiseAsync(OperatorAlert alert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alert);
        logger.LogWarning(
            "Operator alert. Kind={Kind}, JobId={JobId}, Key={Key}, Number={Number}",
            alert.Kind, alert.JobId, alert.Key, alert.Number);
        var (subject, body) = Compose(alert);
        await MailAsync(mail, configuration, logger, alert.Kind, subject, body, ct);
    }

    /// <summary>The alert's message: its subject and its body, ids, keys and counts only.</summary>
    internal static (string Subject, string Body) Compose(OperatorAlert alert)
    {
        var body = new StringBuilder()
            .Append(Sentence(alert.Kind)).Append("\n\n")
            .Append("Kind: ").Append(alert.Kind).Append('\n');
        if (alert.JobId != null)
            body.Append("Job: ").Append(alert.JobId).Append('\n');
        if (alert.Key != null)
            body.Append("Key: ").Append(alert.Key).Append('\n');
        if (alert.Number is { } number)
            body.Append("Number: ").Append(number.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return ($"Consultologist operator alert: {alert.Kind}", body.ToString());
    }

    /// <summary>The fixed sentence for an alert's kind.</summary>
    private static string Sentence(string kind) => kind switch
    {
        OperatorAlertKinds.JobFailed => "A job failed.",
        OperatorAlertKinds.CapacityWait => "A job's model calls have waited on the model service's capacity (429) for more than 5 minutes in all.",
        OperatorAlertKinds.JobSlow => "A job has run for 80% of its time limit.",
        OperatorAlertKinds.DeliveryFailed => "A job's delivery failed.",
        OperatorAlertKinds.PublishFailed => "A publish failed.",
        OperatorAlertKinds.ExporterWarning => "An exporter warned.",
        _ => "Something needs the operator.",
    };

    /// <summary>
    /// Mails <paramref name="subject"/> and <paramref name="body"/> to <see cref="AddressSetting"/> from the mailbox; with either
    /// unset, nothing is sent (the caller has logged). A fault is logged, its type only, and swallowed. True when sent.
    /// </summary>
    internal static async Task<bool> MailAsync(
        IGraphMailClient mail, IConfiguration configuration, ILogger logger, string kind, string subject, string body, CancellationToken ct)
    {
        var address = configuration[AddressSetting];
        var mailbox = configuration[MailboxSetting];
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(mailbox))
            return false;

        try
        {
            await mail.SendMailAsync(mailbox, address.Trim(), subject, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Operator mail failed. Kind={Kind}, ExceptionType={ExceptionType}", kind, ex.GetType().FullName);
            return false;
        }
    }
}
