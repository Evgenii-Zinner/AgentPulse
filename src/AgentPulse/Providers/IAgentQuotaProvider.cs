using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;

namespace AgentPulse.Providers;

public interface IAgentQuotaProvider
{
    Task<AgentStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
