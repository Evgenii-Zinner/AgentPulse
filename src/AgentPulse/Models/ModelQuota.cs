using System;

namespace AgentPulse.Models;

public class ModelQuota
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string LimitValue { get; set; } = string.Empty;
    public string UsageValue { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    
    // Primary limit (5-hour rolling limit or weekly limit)
    public string PrimaryLimitLabel { get; set; } = "5h";
    public double RemainingFraction { get; set; } = 1.0; // 0.0 - 1.0
    public int PercentRemaining => (int)Math.Round(Math.Clamp(RemainingFraction * 100.0, 0, 100));
    public string FormattedPercent => $"{PercentRemaining}%";
    public DateTime? ResetAt { get; set; }

    // Secondary Overall budget (Weekly or Daily limit)
    public bool HasSecondaryLimit { get; set; }
    public double SecondaryRemainingFraction { get; set; } = 1.0;
    public int SecondaryPercentRemaining => (int)Math.Round(Math.Clamp(SecondaryRemainingFraction * 100.0, 0, 100));
    public string FormattedSecondaryPercent => $"{SecondaryPercentRemaining}%";
    public DateTime? SecondaryResetAt { get; set; }
    public string SecondaryLimitLabel { get; set; } = "WEEKLY";

    public bool IsOnCooldown => RemainingFraction <= 0.001 || (ResetAt.HasValue && ResetAt.Value > DateTime.UtcNow && RemainingFraction < 0.05);

    public TimeSpan CooldownRemaining
    {
        get
        {
            if (ResetAt.HasValue && ResetAt.Value > DateTime.UtcNow)
                return ResetAt.Value - DateTime.UtcNow;
            return TimeSpan.Zero;
        }
    }

    public string FormattedReset
    {
        get
        {
            if (!ResetAt.HasValue)
                return "Ready";

            var diff = ResetAt.Value - DateTime.UtcNow;
            if (diff <= TimeSpan.Zero)
                return "Resetting";

            if (diff.TotalDays >= 1)
                return $"{(int)diff.TotalDays}d {diff.Hours}h";

            if (diff.TotalHours >= 1)
                return $"{(int)diff.TotalHours}h {diff.Minutes:D2}m";

            return $"{diff.Minutes}m {diff.Seconds}s";
        }
    }

    public string FormattedSecondaryReset
    {
        get
        {
            if (!SecondaryResetAt.HasValue)
                return "Ready";

            var diff = SecondaryResetAt.Value - DateTime.UtcNow;
            if (diff <= TimeSpan.Zero)
                return "Resetting";

            if (diff.TotalDays >= 1)
                return $"{(int)diff.TotalDays}d {diff.Hours}h";

            if (diff.TotalHours >= 1)
                return $"{(int)diff.TotalHours}h {diff.Minutes}m";

            return $"{diff.Minutes}m";
        }
    }

    public string StatusBadgeText
    {
        get
        {
            if (PercentRemaining <= 5) return "CRITICAL";
            if (PercentRemaining <= 20) return "LOW";
            if (PercentRemaining <= 50) return "FAIR";
            return "OPTIMAL";
        }
    }
}
