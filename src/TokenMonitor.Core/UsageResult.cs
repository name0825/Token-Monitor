namespace TokenMonitor.Core;

public sealed class UsageResult
{
    private UsageResult(bool isSuccess, IReadOnlyList<UsageSnapshot>? snapshots, UsageFailureKind? failureKind, string? message, string? fallbackMessage, string? fallbackWarning)
    {
        IsSuccess = isSuccess;
        Snapshots = snapshots;
        FailureKind = failureKind;
        Message = message;
        FallbackMessage = fallbackMessage;
        FallbackWarning = fallbackWarning;
    }

    public bool IsSuccess { get; }
    public IReadOnlyList<UsageSnapshot>? Snapshots { get; }
    public UsageFailureKind? FailureKind { get; }
    public string? Message { get; }

    /// <summary>Failure message from the fallback provider, set only when both primary and fallback failed.</summary>
    public string? FallbackMessage { get; }

    /// <summary>Failure message from the fallback provider, set only when the primary succeeded with stale data and the fallback failed.</summary>
    public string? FallbackWarning { get; }

    public static UsageResult Success(IReadOnlyList<UsageSnapshot> snapshots, string? fallbackWarning = null) => new(true, snapshots, null, null, null, fallbackWarning);

    public static UsageResult Failure(UsageFailureKind failureKind, string message, string? fallbackMessage = null) =>
        new(false, null, failureKind, message, fallbackMessage, null);
}
