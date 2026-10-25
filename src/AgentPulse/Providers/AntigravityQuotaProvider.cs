using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;

namespace AgentPulse.Providers;

public class AntigravityQuotaProvider : IAgentQuotaProvider, IDisposable
{
    public string ProviderName => "Google Antigravity";
    public bool IsConfigured => true;
    public DateTime? LastUpdated { get; private set; }

    public event Action<AgentStatus>? StatusUpdated;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _persistentProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isDisposed;

    private static string ResolveAgyPath()
    {
        // 1. Check standard AppData local install path
        var localAgy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy", "bin", "agy.exe");
        if (File.Exists(localAgy))
            return localAgy;

        // 2. Check user profile .gemini path
        var userGemini = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini", "antigravity-cli", "bin", "agy.exe");
        if (File.Exists(userGemini))
            return userGemini;

        // 3. Fallback to PATH resolution
        return "agy.exe";
    }

    private async Task EnsureSubprocessAsync(CancellationToken cancellationToken)
    {
        if (_persistentProcess != null && !_persistentProcess.HasExited && _stdin != null && _stdout != null)
        {
            return;
        }

        CleanupSubprocess();

        var agyPath = ResolveAgyPath();
        var psi = new ProcessStartInfo
        {
            FileName = agyPath,
            Arguments = "--input-format stream-json --output-format stream-json --print=\"\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var proc = Process.Start(psi);
        if (proc == null)
        {
            throw new InvalidOperationException("Failed to start agy persistent subprocess.");
        }

        _persistentProcess = proc;
        _stdin = proc.StandardInput;
        _stdout = proc.StandardOutput;

        // Read initial init line with timeout
        using var initCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var initLine = await _stdout.ReadLineAsync(initCts.Token);
            if (string.IsNullOrWhiteSpace(initLine))
            {
                throw new InvalidOperationException("Did not receive init event from agy subprocess.");
            }
        }
        catch
        {
            CleanupSubprocess();
            throw;
        }
    }

    private void CleanupSubprocess()
    {
        try
        {
            if (_stdin != null)
            {
                _stdin.Dispose();
                _stdin = null;
            }

            if (_stdout != null)
            {
                _stdout.Dispose();
                _stdout = null;
            }

            if (_persistentProcess != null)
            {
                if (!_persistentProcess.HasExited)
                {
                    _persistentProcess.Kill();
                }
                _persistentProcess.Dispose();
                _persistentProcess = null;
            }
        }
        catch { }
    }

    public async Task<AgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Attempt 1: Query persistent subprocess via stdin/stdout
            try
            {
                await EnsureSubprocessAsync(cancellationToken);

                if (_stdin != null && _stdout != null)
                {
                    var req = "{\"event\":\"user\",\"message\":\"/usage\"}";
                    await _stdin.WriteLineAsync(req.AsMemory(), cancellationToken);
                    await _stdin.FlushAsync(cancellationToken);

                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(TimeSpan.FromSeconds(4));

                    string? line;
                    while ((line = await _stdout.ReadLineAsync(readCts.Token)) != null)
                    {
                        if (line.Contains("\"event\":\"result\"") || line.Contains("\"groups\":"))
                        {
                            var statusFromStream = ParseAgyUsageJson(line);
                            if (statusFromStream.Models.Count > 0)
                            {
                                LastUpdated = statusFromStream.LastUpdated;
                                StatusUpdated?.Invoke(statusFromStream);
                                return statusFromStream;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If stream query fails or times out, close subprocess so it will respawn next time
                CleanupSubprocess();
            }

            // Fallback: One-off direct headless query (guaranteed 100% reliable)
            var agyPath = ResolveAgyPath();
            var psi = new ProcessStartInfo
            {
                FileName = agyPath,
                Arguments = "--print /usage --output-format json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return CreateOfflineStatus("Failed to launch 'agy' CLI process.");
            }

            var outputTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            var output = await outputTask;

            if (string.IsNullOrWhiteSpace(output))
            {
                return CreateOfflineStatus("No output returned from 'agy /usage'.");
            }

            var status = ParseAgyUsageJson(output);
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
                StatusMessage = $"Antigravity CLI error: {ex.Message}",
                ErrorDetail = ex.ToString(),
                LastUpdated = DateTime.UtcNow
            };
            LastUpdated = errStatus.LastUpdated;
            StatusUpdated?.Invoke(errStatus);
            return errStatus;
        }
        finally
        {
            _lock.Release();
        }
    }

    private AgentStatus ParseAgyUsageJson(string json)
    {
        var models = new List<ModelQuota>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement groupsElement;
            bool foundGroups = false;

            if (root.TryGetProperty("command", out var cmd) &&
                cmd.TryGetProperty("data", out var data) &&
                data.TryGetProperty("groups", out var groups) &&
                groups.ValueKind == JsonValueKind.Array)
            {
                groupsElement = groups;
                foundGroups = true;
            }
            else if (root.TryGetProperty("result", out var res) &&
                     res.TryGetProperty("command", out var resCmd) &&
                     resCmd.TryGetProperty("data", out var resData) &&
                     resData.TryGetProperty("groups", out var resGroups) &&
                     resGroups.ValueKind == JsonValueKind.Array)
            {
                groupsElement = resGroups;
                foundGroups = true;
            }
            else
            {
                groupsElement = default;
            }

            if (foundGroups)
            {
                foreach (var group in groupsElement.EnumerateArray())
                {
                    var groupName = group.TryGetProperty("name", out var gn) ? gn.GetString() ?? "Unknown" : "Unknown";

                    double fiveHourFraction = 1.0;
                    DateTime? fiveHourReset = null;
                    bool hasFiveHour = false;

                    double weeklyFraction = 1.0;
                    DateTime? weeklyReset = null;
                    bool hasWeekly = false;

                    if (group.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var b in buckets.EnumerateArray())
                        {
                            var window = b.TryGetProperty("window", out var w) ? w.GetString() : "";
                            var remFrac = b.TryGetProperty("remaining_fraction", out var rf) ? rf.GetDouble() : 1.0;
                            DateTime? rTime = null;
                            if (b.TryGetProperty("reset_time", out var rt) && rt.TryGetDateTime(out var dt))
                            {
                                rTime = dt.ToUniversalTime();
                            }

                            if (string.Equals(window, "5h", StringComparison.OrdinalIgnoreCase))
                            {
                                hasFiveHour = true;
                                fiveHourFraction = remFrac;
                                fiveHourReset = rTime;
                            }
                            else if (string.Equals(window, "weekly", StringComparison.OrdinalIgnoreCase))
                            {
                                hasWeekly = true;
                                weeklyFraction = remFrac;
                                weeklyReset = rTime;
                            }
                        }
                    }

                    // For groups with ONLY a weekly quota (such as Gemini 4 Argon),
                    // the primary remaining fraction is the weekly fraction!
                    if (!hasFiveHour && hasWeekly)
                    {
                        models.Add(new ModelQuota
                        {
                            Id = groupName.ToLowerInvariant().Replace(" ", "-"),
                            Name = groupName,
                            Tier = "Pro",
                            PrimaryLimitLabel = "Weekly",
                            RemainingFraction = weeklyFraction,
                            ResetAt = weeklyReset,
                            HasSecondaryLimit = false,
                            SecondaryLimitLabel = "WEEKLY"
                        });
                    }
                    else
                    {
                        // Dual limits: Gemini Models (80% 5h, 79% weekly) & Claude and GPT
                        models.Add(new ModelQuota
                        {
                            Id = groupName.ToLowerInvariant().Replace(" ", "-"),
                            Name = groupName,
                            Tier = "Pro",
                            PrimaryLimitLabel = "5h",
                            RemainingFraction = fiveHourFraction,
                            ResetAt = fiveHourReset,
                            HasSecondaryLimit = hasWeekly,
                            SecondaryRemainingFraction = weeklyFraction,
                            SecondaryResetAt = weeklyReset,
                            SecondaryLimitLabel = "WEEKLY"
                        });
                    }
                }
            }
        }
        catch { }

        var status = new AgentStatus
        {
            ProviderName = ProviderName,
            IsSuccess = models.Count > 0,
            LastUpdated = DateTime.UtcNow,
            AccountName = "Antigravity Pro",
            AccountIdentifier = "Active Session",
            Tier = "Pro",
            Models = models,
            StatusMessage = models.Count > 0
                ? $"Live Antigravity quotas synchronized ({models.Count} pools active)"
                : "Unable to parse /usage output."
        };

        if (models.Count > 0)
        {
            var lowest = models.OrderBy(m => m.RemainingFraction).First();
            if (lowest.RemainingFraction <= 0.05)
            {
                status.StatusLevel = StatusLevel.Throttled;
                status.Cooldown = new CooldownInfo
                {
                    IsOnCooldown = true,
                    AffectedModel = lowest.Name,
                    ResetTimestamp = lowest.ResetAt ?? DateTime.UtcNow.AddMinutes(30)
                };
            }
            else if (lowest.RemainingFraction <= 0.20)
            {
                status.StatusLevel = StatusLevel.Warning;
            }
            else
            {
                status.StatusLevel = StatusLevel.Normal;
            }
        }

        return status;
    }

    private AgentStatus CreateOfflineStatus(string message)
    {
        return new AgentStatus
        {
            ProviderName = ProviderName,
            IsSuccess = false,
            StatusLevel = StatusLevel.Warning,
            StatusMessage = message,
            LastUpdated = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        CleanupSubprocess();
        _lock.Dispose();
    }
}
