using System;
using System.Collections.Generic;

namespace AgentPulse.Models;

public class AgentStatus
{
    public string ProviderName { get; set; } = "Antigravity";
    public string AccountIdentifier { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Tier { get; set; } = "Standard";
    public int AvailablePromptCredits { get; set; }
    public int MonthlyPromptCredits { get; set; }
    public bool HasCredits => MonthlyPromptCredits > 0;
    public List<ModelQuota> Models { get; set; } = new();
    public CooldownInfo Cooldown { get; set; } = new();
    public StatusLevel StatusLevel { get; set; } = StatusLevel.Normal;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? StatusMessage { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorDetail { get; set; }
}
