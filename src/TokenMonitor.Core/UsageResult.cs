namespace TokenMonitor.Core;

public sealed class UsageResult
{
    private UsageResult(bool isSuccess, IReadOnlyList<UsageSnapshot>? snapshots, UsageFailureKind? failureKind, string? message)
    {
        IsSuccess = isSuccess;
        Snapshots = snapshots;
        FailureKind = failureKind;
        Message = message;
    }

    public bool IsSuccess { get; }
    public IReadOnlyList<UsageSnapshot>? Snapshots { get; }
    public UsageFailureKind? FailureKind { get; }
    public string? Message { get; }

    public static UsageResult Success(IReadOnlyList<UsageSnapshot> snapshots) => new(true, snapshots, null, null);

    public static UsageResult Failure(UsageFailureKind failureKind, string message) => new(false, null, failureKind, message);
}
