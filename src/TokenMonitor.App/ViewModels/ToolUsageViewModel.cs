using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TokenMonitor.Core;

namespace TokenMonitor.App.ViewModels;

public sealed class ToolUsageViewModel : INotifyPropertyChanged
{
    public const double BarTrackWidth = 64;

    private static readonly Brush LowBrush = CreateBrush(0x4C, 0xAF, 0x50);
    private static readonly Brush MediumBrush = CreateBrush(0xFF, 0xB3, 0x00);
    private static readonly Brush HighBrush = CreateBrush(0xE5, 0x39, 0x35);

    private readonly Tool _tool;

    private UsageSnapshot? _fiveHour;
    private UsageSnapshot? _weekly;
    private DateTimeOffset? _observedAt;
    private UsageOrigin? _origin;

    private bool _isVisible = true;
    private double _fiveHourPercent;
    private string _fiveHourText = Placeholder;
    private string _fiveHourReset = Placeholder;
    private double _weeklyPercent;
    private string _weeklyText = Placeholder;
    private string _weeklyReset = Placeholder;
    private string _statusText = "대기 중";
    private bool _hasError;
    private string _errorText = string.Empty;

    private const string Placeholder = "—";

    public ToolUsageViewModel(Tool tool)
    {
        _tool = tool;
        DisplayName = tool == Tool.Claude ? "Claude" : "Codex";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public double FiveHourPercent
    {
        get => _fiveHourPercent;
        private set
        {
            if (Set(ref _fiveHourPercent, value))
            {
                OnPropertyChanged(nameof(FiveHourBarWidth));
                OnPropertyChanged(nameof(FiveHourBrush));
            }
        }
    }

    public double FiveHourBarWidth => BarWidth(_fiveHourPercent);

    public Brush FiveHourBrush => BrushFor(_fiveHourPercent);

    public string FiveHourText
    {
        get => _fiveHourText;
        private set => Set(ref _fiveHourText, value);
    }

    public string FiveHourReset
    {
        get => _fiveHourReset;
        private set => Set(ref _fiveHourReset, value);
    }

    public double WeeklyPercent
    {
        get => _weeklyPercent;
        private set
        {
            if (Set(ref _weeklyPercent, value))
            {
                OnPropertyChanged(nameof(WeeklyBarWidth));
                OnPropertyChanged(nameof(WeeklyBrush));
            }
        }
    }

    public double WeeklyBarWidth => BarWidth(_weeklyPercent);

    public Brush WeeklyBrush => BrushFor(_weeklyPercent);

    public string WeeklyText
    {
        get => _weeklyText;
        private set => Set(ref _weeklyText, value);
    }

    public string WeeklyReset
    {
        get => _weeklyReset;
        private set => Set(ref _weeklyReset, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => Set(ref _hasError, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public void Apply(UsageResult result, DateTimeOffset now)
    {
        if (result.IsSuccess && result.Snapshots is { Count: > 0 } snapshots)
        {
            HasError = false;
            ErrorText = string.Empty;

            foreach (UsageSnapshot snapshot in snapshots)
            {
                if (snapshot.Tool != _tool)
                {
                    continue;
                }

                if (snapshot.Window == UsageWindow.FiveHour)
                {
                    if (_fiveHour is null || snapshot.ObservedAt >= _fiveHour.ObservedAt)
                    {
                        _fiveHour = snapshot;
                    }
                }
                else
                {
                    if (_weekly is null || snapshot.ObservedAt >= _weekly.ObservedAt)
                    {
                        _weekly = snapshot;
                    }
                }
            }

            UsageSnapshot? latest = null;
            if (_fiveHour is { } fiveHour && (latest is null || fiveHour.ObservedAt > latest.ObservedAt))
            {
                latest = fiveHour;
            }

            if (_weekly is { } weekly && (latest is null || weekly.ObservedAt > latest.ObservedAt))
            {
                latest = weekly;
            }

            if (latest is { } newest)
            {
                _origin = newest.Origin;
                _observedAt = newest.ObservedAt;
            }
        }
        else
        {
            HasError = true;
            ErrorText = DescribeFailure(result);
        }

        Render(now);
    }

    public void Render(DateTimeOffset now)
    {
        RenderWindow(_fiveHour, now, out double fiveHourPercent, out string fiveHourText, out string fiveHourReset);
        FiveHourPercent = fiveHourPercent;
        FiveHourText = fiveHourText;
        FiveHourReset = fiveHourReset;

        RenderWindow(_weekly, now, out double weeklyPercent, out string weeklyText, out string weeklyReset);
        WeeklyPercent = weeklyPercent;
        WeeklyText = weeklyText;
        WeeklyReset = weeklyReset;

        StatusText = _origin is { } origin && _observedAt is { } observedAt
            ? UsageFormatting.FormatOrigin(origin) + " · " + UsageFormatting.FormatFreshness(observedAt, now)
            : "대기 중";
    }

    private static void RenderWindow(UsageSnapshot? snapshot, DateTimeOffset now, out double percent, out string text, out string reset)
    {
        if (snapshot is null)
        {
            percent = 0;
            text = Placeholder;
            reset = Placeholder;
            return;
        }

        UsageSnapshot current = snapshot.AsOf(now);
        percent = Math.Clamp(current.UsedPercent, 0, 100);
        text = UsageFormatting.FormatPercent(current.UsedPercent);
        reset = UsageFormatting.FormatRemaining(current.ResetsAt, now);
    }

    private static string DescribeFailure(UsageResult result)
    {
        string baseText = result.FailureKind switch
        {
            UsageFailureKind.NotFound => "설정 없음",
            UsageFailureKind.NoData => "데이터 없음",
            UsageFailureKind.Unauthorized => "인증 만료",
            UsageFailureKind.RateLimited => "요청 제한",
            UsageFailureKind.InvalidData => "형식 오류",
            _ => "사용 불가",
        };

        return string.IsNullOrEmpty(result.FallbackMessage)
            ? baseText
            : $"{baseText} · 폴백 실패: {result.FallbackMessage}";
    }

    private static double BarWidth(double percent) => BarTrackWidth * Math.Clamp(percent, 0, 100) / 100;

    private static Brush BrushFor(double percent) => percent switch
    {
        < 60 => LowBrush,
        <= 85 => MediumBrush,
        _ => HighBrush,
    };

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
