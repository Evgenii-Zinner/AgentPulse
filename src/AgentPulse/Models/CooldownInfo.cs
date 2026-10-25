using System;

namespace AgentPulse.Models;

public class CooldownInfo
{
    public bool IsOnCooldown { get; set; }
    public TimeSpan CooldownRemaining { get; set; }
    public double MinutesRemaining => Math.Max(0, CooldownRemaining.TotalMinutes);
    public DateTime? ResetTimestamp { get; set; }
    public string? AffectedModel { get; set; }

    public string FormattedCooldown
    {
        get
        {
            if (!IsOnCooldown || CooldownRemaining <= TimeSpan.Zero)
                return "Ready";

            if (CooldownRemaining.TotalHours >= 1)
                return $"{(int)CooldownRemaining.TotalHours}h {CooldownRemaining.Minutes:D2}m {CooldownRemaining.Seconds:D2}s";

            return $"{CooldownRemaining.Minutes:D2}m {CooldownRemaining.Seconds:D2}s";
        }
    }
}
