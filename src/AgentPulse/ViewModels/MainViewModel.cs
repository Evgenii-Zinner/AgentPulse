using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;
using AgentPulse.Providers;
using AgentPulse.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace AgentPulse.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AntigravityQuotaProvider _quotaProvider;
    private readonly LockScreenUpdater _lockScreenUpdater;
    private readonly DispatcherQueue _dispatcherQueue;

    // Countdown timer for live UI seconds countdown
    private DispatcherQueueTimer? _countdownTimer;

    // ONE-SHOT timer set to the exact second the next quota resets
    private Timer? _exactResetTimer;

    private AgentStatus? _lastStatus;
    private bool _isDisposed;

    [ObservableProperty]
    private string _providerName = "Google Antigravity";

    [ObservableProperty]
    private string _accountIdentifier = "Active Session";

    [ObservableProperty]
    private string _accountName = "Antigravity Developer";

    [ObservableProperty]
    private string _tier = "Pro";

    [ObservableProperty]
    private string _statusMessage = "Synchronizing Antigravity quotas...";

    [ObservableProperty]
    private StatusLevel _statusLevel = StatusLevel.Normal;

    [ObservableProperty]
    private string _statusPillColor = "#10B981";

    [ObservableProperty]
    private string _statusPillText = "Operational";

    [ObservableProperty]
    private bool _isOnCooldown;

    [ObservableProperty]
    private string _cooldownRemainingText = "Ready";

    [ObservableProperty]
    private string _cooldownAffectedModel = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = "Never";

    [ObservableProperty]
    private string _nextResetScheduledText = "All Ready";

    [ObservableProperty]
    private string _trayTooltip = "Agent Pulse: Initializing...";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<ModelQuota> _models = new();

    [ObservableProperty]
    private QuotaGroup _antigravityGemini = new() { Name = "Gemini", Id = "gemini", Percent = 100, ResetText = "Ready" };

    [ObservableProperty]
    private QuotaGroup _antigravityClaude = new() { Name = "Claude & GPT", Id = "claude", Percent = 100, ResetText = "Ready" };

    [ObservableProperty]
    private QuotaGroup _antigravityArgon = new() { Name = "Gemini 4 Argon", Id = "argon", Percent = 100, ResetText = "Ready" };

    public MainViewModel(AntigravityQuotaProvider quotaProvider, LockScreenUpdater lockScreenUpdater)
    {
        _quotaProvider = quotaProvider;
        _lockScreenUpdater = lockScreenUpdater;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _quotaProvider.StatusUpdated += OnStatusUpdated;

        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch { }

        StartUiTimers();
        _ = RefreshAsync();
    }

    private void StartUiTimers()
    {
        _countdownTimer = _dispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (s, e) => UpdateCountdown();
        _countdownTimer.Start();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            var status = await _quotaProvider.GetStatusAsync();
            ApplyStatus(status);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnStatusUpdated(AgentStatus status)
    {
        _dispatcherQueue.TryEnqueue(() => ApplyStatus(status));
    }

    private void ApplyStatus(AgentStatus status)
    {
        _lastStatus = status;

        ProviderName = status.ProviderName;
        AccountIdentifier = string.IsNullOrEmpty(status.AccountIdentifier) ? "Antigravity Session" : status.AccountIdentifier;
        AccountName = string.IsNullOrEmpty(status.AccountName) ? "Antigravity Pro" : status.AccountName;
        Tier = status.Tier;
        StatusLevel = status.StatusLevel;
        StatusMessage = status.StatusMessage ?? "Operational";
        LastUpdatedText = status.LastUpdated.ToLocalTime().ToString("HH:mm:ss");

        Models.Clear();
        foreach (var m in status.Models)
        {
            Models.Add(m);
        }

        // Categorize into Gemini, Claude, Argon directly from the official /usage groups
        var gemini = status.Models.Where(m =>
            m.Name.Contains("gemini", StringComparison.OrdinalIgnoreCase) &&
            !m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase)).ToList();

        var claude = status.Models.Where(m =>
            m.Name.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("gpt", StringComparison.OrdinalIgnoreCase)).ToList();

        var argon = status.Models.Where(m =>
            m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase)).ToList();

        UpdateGroup(AntigravityGemini, gemini, "Gemini", "gemini");
        UpdateGroup(AntigravityClaude, claude, "Claude & GPT", "claude");
        UpdateGroup(AntigravityArgon, argon, "Gemini 4 Argon", "argon");

        // Force UI notification for updated groups
        OnPropertyChanged(nameof(AntigravityGemini));
        OnPropertyChanged(nameof(AntigravityClaude));
        OnPropertyChanged(nameof(AntigravityArgon));

        // Cooldown state
        IsOnCooldown = status.Cooldown.IsOnCooldown;
        CooldownAffectedModel = status.Cooldown.AffectedModel ?? string.Empty;
        CooldownRemainingText = status.Cooldown.FormattedCooldown;

        // Visual Pill styling
        switch (status.StatusLevel)
        {
            case StatusLevel.Throttled:
                StatusPillColor = "#EF4444";
                StatusPillText = "Cooldown Active";
                break;
            case StatusLevel.Warning:
                StatusPillColor = "#F59E0B";
                StatusPillText = "Quota Warning";
                break;
            default:
                StatusPillColor = "#10B981";
                StatusPillText = "Operational";
                break;
        }

        TrayTooltip = $"Antigravity: Gemini {AntigravityGemini.Percent}% (5h) / {AntigravityGemini.SecondaryPercent}% (Wk) | Argon {AntigravityArgon.Percent}%";

        // Push updates to Windows Lock Screen & Badge
        _lockScreenUpdater.UpdateBadge(status);
        _lockScreenUpdater.UpdateTileText(status);

        // Schedule ONE single timer targeting the exact next reset timestamp
        ScheduleNextExactResetTimer(status);
    }

    private static void UpdateGroup(QuotaGroup target, List<ModelQuota> list, string defaultName, string defaultId)
    {
        if (list.Count == 0)
        {
            target.Name = defaultName;
            target.Id = defaultId;
            target.PrimaryLabel = "5h";
            target.Percent = 100;
            target.Fraction = 1.0;
            target.ResetText = "Ready";
            target.HasSecondaryLimit = false;
            return;
        }

        var item = list.First();
        target.Name = item.Name;
        target.Id = item.Id;
        target.PrimaryLabel = item.PrimaryLimitLabel;
        target.Percent = item.PercentRemaining;
        target.Fraction = item.RemainingFraction;
        target.ResetText = item.FormattedReset;
        target.HasSecondaryLimit = item.HasSecondaryLimit;
        target.SecondaryPercent = item.SecondaryPercentRemaining;
        target.SecondaryFraction = item.SecondaryRemainingFraction;
        target.SecondaryResetText = item.FormattedSecondaryReset;
        target.SecondaryLabel = item.SecondaryLimitLabel;
    }

    private void ScheduleNextExactResetTimer(AgentStatus status)
    {
        _exactResetTimer?.Dispose();
        _exactResetTimer = null;

        var nextResetModel = status.Models
            .Where(m => m.ResetAt.HasValue && m.ResetAt.Value > DateTime.UtcNow && m.RemainingFraction < 0.99)
            .OrderBy(m => m.ResetAt)
            .FirstOrDefault();

        if (nextResetModel != null && nextResetModel.ResetAt.HasValue)
        {
            var delay = nextResetModel.ResetAt.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                var localResetTime = nextResetModel.ResetAt.Value.ToLocalTime();
                NextResetScheduledText = $"Reset at {localResetTime:HH:mm:ss}";

                var triggerDelay = delay.Add(TimeSpan.FromSeconds(1));

                _exactResetTimer = new Timer(async _ =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_lastStatus != null)
                        {
                            _lockScreenUpdater.UpdateTileText(_lastStatus, isRefreshedNotification: true);
                            _lockScreenUpdater.UpdateBadge(_lastStatus, isRefreshedNotification: true);
                        }
                    });

                    await RefreshAsync();

                }, null, triggerDelay, Timeout.InfiniteTimeSpan);

                return;
            }
        }

        NextResetScheduledText = "All Ready";
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _countdownTimer?.Stop();
            if (_lastStatus != null)
            {
                _lockScreenUpdater.UpdateBadge(_lastStatus);
                _lockScreenUpdater.UpdateTileText(_lastStatus);
            }
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _countdownTimer?.Start();
            _ = RefreshAsync();
        }
    }

    private void UpdateCountdown()
    {
        if (Models.Count == 0) return;

        // Live countdown updates for QuotaGroups while window is open
        if (_lastStatus != null)
        {
            var gemini = _lastStatus.Models.FirstOrDefault(m =>
                m.Name.Contains("gemini", StringComparison.OrdinalIgnoreCase) &&
                !m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase));
            if (gemini != null)
            {
                AntigravityGemini.ResetText = gemini.FormattedReset;
                AntigravityGemini.SecondaryResetText = gemini.FormattedSecondaryReset;
            }

            var claude = _lastStatus.Models.FirstOrDefault(m =>
                m.Name.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("gpt", StringComparison.OrdinalIgnoreCase));
            if (claude != null)
            {
                AntigravityClaude.ResetText = claude.FormattedReset;
                AntigravityClaude.SecondaryResetText = claude.FormattedSecondaryReset;
            }

            var argon = _lastStatus.Models.FirstOrDefault(m =>
                m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase));
            if (argon != null)
            {
                AntigravityArgon.ResetText = argon.FormattedReset;
            }
        }

        var cooldownModel = Models.FirstOrDefault(m => m.IsOnCooldown || (m.ResetAt.HasValue && m.ResetAt.Value > DateTime.UtcNow && m.RemainingFraction < 0.05));
        if (cooldownModel != null && cooldownModel.ResetAt.HasValue)
        {
            var remaining = cooldownModel.ResetAt.Value - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                IsOnCooldown = true;
                CooldownAffectedModel = cooldownModel.Name;

                if (remaining.TotalHours >= 1)
                    CooldownRemainingText = $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
                else
                    CooldownRemainingText = $"{remaining.Minutes:D2}m {remaining.Seconds:D2}s";

                StatusPillColor = "#EF4444";
                StatusPillText = "Cooldown Active";
                TrayTooltip = $"Antigravity: COOLDOWN ({CooldownRemainingText})";
                return;
            }
        }

        if (IsOnCooldown)
        {
            IsOnCooldown = false;
            CooldownRemainingText = "Ready";
            StatusPillColor = "#10B981";
            StatusPillText = "Operational";
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch { }

        _exactResetTimer?.Dispose();
        _countdownTimer?.Stop();
        _quotaProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
