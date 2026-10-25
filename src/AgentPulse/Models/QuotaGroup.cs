using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentPulse.Models;

public partial class QuotaGroup : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _id = string.Empty;

    // Primary limit (5-hour rolling limit or weekly limit)
    [ObservableProperty]
    private string _primaryLabel = "5h";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPercent))]
    [NotifyPropertyChangedFor(nameof(IsThrottled))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _percent = 100;

    [ObservableProperty]
    private double _fraction = 1.0;

    public string FormattedPercent => $"{Percent}%";

    [ObservableProperty]
    private string _resetText = "Ready";

    public bool IsThrottled => Percent <= 0;
    public string StatusText => IsThrottled ? "Throttled" : (Percent < 20 ? "Low" : "Ready");

    // Secondary Overall budget (Weekly or Daily limit)
    [ObservableProperty]
    private bool _hasSecondaryLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSecondaryPercent))]
    private int _secondaryPercent = 100;

    [ObservableProperty]
    private double _secondaryFraction = 1.0;

    public string FormattedSecondaryPercent => $"{SecondaryPercent}%";

    [ObservableProperty]
    private string _secondaryResetText = "Ready";

    [ObservableProperty]
    private string _secondaryLabel = "WEEKLY";
}
