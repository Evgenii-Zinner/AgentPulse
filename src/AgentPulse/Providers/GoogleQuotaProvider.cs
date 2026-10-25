using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;
using AgentPulse.Services;

namespace AgentPulse.Providers;

public class GoogleQuotaProvider : IAgentQuotaProvider
{
    private readonly GoogleAuthService _authService;
    private readonly HttpClient _httpClient;

    public string ProviderName => "Google Cloud Quotas";
    public bool IsConfigured => _authService.IsAuthenticated;
    public DateTime? LastUpdated { get; private set; }

    public event Action<AgentStatus>? StatusUpdated;

    public GoogleQuotaProvider(GoogleAuthService authService)
    {
        _authService = authService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<AgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.IsAuthenticated)
        {
            var unauthStatus = new AgentStatus
            {
                ProviderName = ProviderName,
                IsSuccess = false,
                StatusLevel = StatusLevel.Normal,
                AccountIdentifier = string.Empty,
                AccountName = "Not Connected",
                StatusMessage = "Sign in with Google to view live quotas.",
                LastUpdated = DateTime.UtcNow,
                Models = new List<ModelQuota>()
            };
            LastUpdated = unauthStatus.LastUpdated;
            StatusUpdated?.Invoke(unauthStatus);
            return unauthStatus;
        }

        try
        {
            var token = await _authService.GetValidAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                var expiredStatus = new AgentStatus
                {
                    ProviderName = ProviderName,
                    IsSuccess = false,
                    StatusLevel = StatusLevel.Warning,
                    AccountIdentifier = _authService.CurrentUser?.Email ?? string.Empty,
                    AccountName = _authService.CurrentUser?.Name ?? "Google User",
                    StatusMessage = "Session expired. Please sign in again.",
                    LastUpdated = DateTime.UtcNow,
                    Models = new List<ModelQuota>()
                };
                LastUpdated = expiredStatus.LastUpdated;
                StatusUpdated?.Invoke(expiredStatus);
                return expiredStatus;
            }

            if (_authService.CurrentUser == null)
            {
                await _authService.TryFetchUserInfoAsync(cancellationToken);
            }

            var email = _authService.CurrentUser?.Email ?? string.Empty;
            var name = _authService.CurrentUser?.Name ?? "Google User";

            var status = await FetchLiveCloudQuotasAsync(token, email, name, cancellationToken);
            LastUpdated = status.LastUpdated;
            StatusUpdated?.Invoke(status);
            return status;
        }
        catch (Exception ex)
        {
            var errStatus = new AgentStatus
            {
                ProviderName = ProviderName,
                IsSuccess = false,
                StatusLevel = StatusLevel.Warning,
                StatusMessage = $"Query error: {ex.Message}",
                ErrorDetail = ex.ToString(),
                LastUpdated = DateTime.UtcNow,
                Models = new List<ModelQuota>()
            };
            LastUpdated = errStatus.LastUpdated;
            StatusUpdated?.Invoke(errStatus);
            return errStatus;
        }
    }

    private async Task<AgentStatus> FetchLiveCloudQuotasAsync(
        string accessToken,
        string email,
        string name,
        CancellationToken cancellationToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var projectId = _authService.Config.ProjectId;
        if (string.IsNullOrEmpty(projectId))
        {
            projectId = "agent-pulse-1";
        }

        var allQuotas = new List<ModelQuota>();

        // Query AI and Developer Services from Cloud Quotas API
        var services = new[]
        {
            "cloudaicompanion.googleapis.com",
            "generativelanguage.googleapis.com"
        };

        foreach (var serviceName in services)
        {
            try
            {
                var url = $"https://cloudquotas.googleapis.com/v1/projects/{projectId}/locations/global/services/{serviceName}/quotaInfos";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _httpClient.SendAsync(req, cancellationToken);

                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    var parsed = ParseQuotaInfosJson(content, serviceName);
                    allQuotas.AddRange(parsed);
                }
            }
            catch { }
        }

        var statusMessage = allQuotas.Count > 0
            ? $"Operational - {allQuotas.Count} live quotas retrieved for project '{projectId}'"
            : $"Connected - No quota metrics returned for project '{projectId}'";

        return new AgentStatus
        {
            ProviderName = ProviderName,
            IsSuccess = true,
            LastUpdated = DateTime.UtcNow,
            AccountName = name,
            AccountIdentifier = email,
            Tier = $"Project: {projectId}",
            Models = allQuotas,
            StatusMessage = statusMessage,
            StatusLevel = StatusLevel.Normal
        };
    }

    private static List<ModelQuota> ParseQuotaInfosJson(string json, string defaultService)
    {
        var list = new List<ModelQuota>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("quotaInfos", out var quotaInfos) && quotaInfos.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in quotaInfos.EnumerateArray())
                {
                    var quotaId = item.TryGetProperty("quotaId", out var qid) ? qid.GetString() ?? "" : "";
                    var metric = item.TryGetProperty("metric", out var m) ? m.GetString() ?? "" : "";
                    var service = item.TryGetProperty("service", out var s) ? s.GetString() ?? defaultService : defaultService;
                    var refreshInterval = item.TryGetProperty("refreshInterval", out var ri) ? ri.GetString() ?? "" : "";
                    var metricDisplayName = item.TryGetProperty("metricDisplayName", out var mdn) ? mdn.GetString() : null;
                    var quotaDisplayName = item.TryGetProperty("quotaDisplayName", out var qdn) ? qdn.GetString() : null;

                    var name = !string.IsNullOrEmpty(quotaDisplayName)
                        ? quotaDisplayName
                        : (!string.IsNullOrEmpty(metricDisplayName) ? metricDisplayName : quotaId);

                    // Extract limit value from dimensionsInfos
                    string limitValue = "Not configured";
                    if (item.TryGetProperty("dimensionsInfos", out var dimInfos) && dimInfos.ValueKind == JsonValueKind.Array)
                    {
                        var firstDim = dimInfos.EnumerateArray().FirstOrDefault();
                        if (firstDim.ValueKind != JsonValueKind.Undefined && firstDim.TryGetProperty("details", out var details))
                        {
                            if (details.TryGetProperty("value", out var val))
                            {
                                var valStr = val.GetString() ?? val.ToString();
                                limitValue = !string.IsNullOrEmpty(refreshInterval)
                                    ? $"{valStr} / {refreshInterval}"
                                    : valStr;
                            }
                        }
                    }

                    // Extract dimensions
                    var dimensionsList = new List<string>();
                    if (item.TryGetProperty("dimensions", out var dims) && dims.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dims.EnumerateArray())
                        {
                            var dStr = d.GetString();
                            if (!string.IsNullOrEmpty(dStr)) dimensionsList.Add(dStr);
                        }
                    }

                    var cleanService = service.Replace(".googleapis.com", "");

                    list.Add(new ModelQuota
                    {
                        Id = quotaId,
                        Name = name,
                        Service = cleanService,
                        Metric = metric,
                        LimitValue = limitValue,
                        Dimensions = dimensionsList.Count > 0 ? string.Join(", ", dimensionsList) : "global",
                        Tier = string.IsNullOrEmpty(refreshInterval) ? "quota" : refreshInterval
                    });
                }
            }
        }
        catch { }

        return list;
    }
}
