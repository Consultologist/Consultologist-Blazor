using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 14 (#1509, D9): a job is stopped when it is not terminal and was started more than 60 minutes ago.</summary>
public class JobLiveness31Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public void ATerminalJob_IsNeverStopped(string status) =>
        Assert.False(JobLiveness31.IsStopped(status, Now.AddHours(-5), Now));

    [Fact]
    public void AJobWithNoStartTime_IsNotStopped() =>
        Assert.False(JobLiveness31.IsStopped("Running", null, Now));

    [Fact]
    public void ADeliveringJob_IsNotStopped_At59Minutes() =>
        Assert.False(JobLiveness31.IsStopped("Delivering", Now.AddMinutes(-59), Now));

    [Fact]
    public void ADeliveringJob_IsStopped_At61Minutes() =>
        Assert.True(JobLiveness31.IsStopped("Delivering", Now.AddMinutes(-61), Now));

    [Fact]
    public void AJobWithNoStatus_ThatIsOld_IsStopped() =>
        Assert.True(JobLiveness31.IsStopped(null, Now.AddMinutes(-90), Now));

    [Fact]
    public void TheLimit_IsSixtyMinutes() => Assert.Equal(60, JobLiveness31.StoppedAfterMinutes);
}
