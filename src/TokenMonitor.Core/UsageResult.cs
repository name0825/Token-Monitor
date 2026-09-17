namespace TokenMonitor.Core;

public sealed class UsageResult
{
    private UsageResult(bool isSuccess, IReadOnlyList<UsageSnapshot>? snapshots, UsageFailureKind? failureKind, string? message, string? fallbackMessage)
    {
        IsSuccess = isSuccess;
        Snapshots = snapshots;
        FailureKind = failureKind;
        Message = message;
        FallbackMessage = fallbackMessage;
    }

    public bool IsSuccess { get; }
    public IReadOnlyList<UsageSnapshot>? Snapshots { get; }
    public UsageFailureKind? FailureKind { get; }
    public string? Message { get; }

    /// <summary>Failure message from the fallback provider, set only when both primary and fallback failed.</summary>
    public string? FallbackMessage { get; }

    public static UsageResult Success(IReadOnlyList<UsageSnapshot> snapshots) => new(true, snapshots, null, null, null);

    public static UsageResult Failure(UsageFailureKind failureKind, string message, string? fallbackMessage = null) =>
        new(false, null, failureKind, message, fallbackMessage);
}
