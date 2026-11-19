using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentPulse.Models;

public partial class QuotaGroup : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPercent))]
    private int _percent = 100;

    public string FormattedPercent => $"{Percent}%";

    [ObservableProperty]
    private string _resetText = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSecondaryPercent))]
    private int _secondaryPercent = 100;

    public string FormattedSecondaryPercent => $"{SecondaryPercent}%";

    [ObservableProperty]
    private string _secondaryResetText = "Ready";
}
