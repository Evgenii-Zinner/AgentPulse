using System;
using System.Collections.Generic;

namespace AgentPulse.Models;

public class AgentStatus
{
    public List<ModelQuota> Models { get; set; } = new();
    public CooldownInfo Cooldown { get; set; } = new();
    public StatusLevel StatusLevel { get; set; } = StatusLevel.Normal;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; } = true;
}
