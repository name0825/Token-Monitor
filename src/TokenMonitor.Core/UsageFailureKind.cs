namespace TokenMonitor.Core;

public enum UsageFailureKind
{
    NotFound,
    NoData,
    Unauthorized,
    RateLimited,
    InvalidData,
    Unavailable
}
