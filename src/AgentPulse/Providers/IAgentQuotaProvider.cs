using System;
using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;

namespace AgentPulse.Providers;

public interface IAgentQuotaProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    DateTime? LastUpdated { get; }
    Task<AgentStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
