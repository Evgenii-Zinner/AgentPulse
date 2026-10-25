using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentPulse.Models;

namespace AgentPulse.Services;

public class GoogleAuthService
{
    private const string GoogleAuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string GoogleUserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

    // Granular read-only scope for viewing Google Cloud & Gemini quotas and limits
    public const string DefaultScopes = "openid email profile https://www.googleapis.com/auth/cloudquotas.readonly";

    private readonly HttpClient _httpClient = new();
    private readonly string _storageDir;
    private readonly string _tokensFilePath;

    public GoogleAuthConfig Config { get; private set; } = new();
    public GoogleTokenInfo? CurrentToken { get; private set; }
    public GoogleUserProfile? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentToken != null && !string.IsNullOrEmpty(CurrentToken.AccessToken);

    public event Action<bool>? AuthStateChanged;

    public GoogleAuthService()
    {
        _storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentPulse");
        Directory.CreateDirectory(_storageDir);

        _tokensFilePath = Path.Combine(_storageDir, "google_tokens.json");

        LoadClientCredentials();
        LoadStoredTokens();
        _ = TryFetchUserInfoAsync();
    }

    private void LoadClientCredentials()
    {
        // 1. Check for client_secret_*.json in application directory
        var searchDirs = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
            Directory.GetCurrentDirectory()
        };

        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "client_secret*.json", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    try
                    {
                        var json = File.ReadAllText(files[0]);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        JsonElement target = root;
                        if (root.TryGetProperty("installed", out var installed))
                        {
                            target = installed;
                        }
                        else if (root.TryGetProperty("web", out var web))
                        {
                            target = web;
                        }

                        var cid = target.TryGetProperty("client_id", out var c) ? c.GetString() : null;
                        var csec = target.TryGetProperty("client_secret", out var cs) ? cs.GetString() : null;
                        var pid = target.TryGetProperty("project_id", out var p) ? p.GetString() : null;

                        if (!string.IsNullOrEmpty(cid))
                        {
                            Config = new GoogleAuthConfig
                            {
                                ClientId = cid,
                                ClientSecret = csec ?? string.Empty,
                                ProjectId = pid ?? string.Empty
                            };
                            return;
                        }
                    }
                    catch { }
                }
            }
        }

        // 2. Default to environment variables or empty fallback
        Config = new GoogleAuthConfig
        {
            ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty,
            ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty,
            ProjectId = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID") ?? string.Empty
        };
    }

    private void LoadStoredTokens()
    {
        try
        {
            if (File.Exists(_tokensFilePath))
            {
                var json = File.ReadAllText(_tokensFilePath);
                CurrentToken = JsonSerializer.Deserialize<GoogleTokenInfo>(json);
            }
        }
        catch { }
    }

    private void SaveTokens()
    {
        try
        {
            if (CurrentToken == null)
            {
                if (File.Exists(_tokensFilePath))
                    File.Delete(_tokensFilePath);
            }
            else
            {
                var json = JsonSerializer.Serialize(CurrentToken, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_tokensFilePath, json);
            }
        }
        catch { }
    }

    public async Task<bool> StartLoginFlowAsync(CancellationToken cancellationToken = default)
    {
        // Pick a free local port
        var port = GetRandomUnusedPort();
        var redirectUri = $"http://localhost:{port}/";

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var state = Guid.NewGuid().ToString("N");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            var scopeEncoded = UrlEncoder.Default.Encode(DefaultScopes);
            var authUrl = $"{GoogleAuthEndpoint}?" +
                          $"client_id={UrlEncoder.Default.Encode(Config.ClientId)}&" +
                          $"redirect_uri={UrlEncoder.Default.Encode(redirectUri)}&" +
                          $"response_type=code&" +
                          $"scope={scopeEncoded}&" +
                          $"code_challenge={codeChallenge}&" +
                          $"code_challenge_method=S256&" +
                          $"state={state}&" +
                          $"access_type=offline&" +
                          $"prompt=consent";

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
            var req = context.Request;
            var res = context.Response;

            var query = req.QueryString;
            var returnedCode = query["code"];
            var returnedError = query["error"];

            var responseHtml = @"<!DOCTYPE html>
<html>
<head>
    <title>Agent Pulse - Authentication</title>
    <style>
        body { background: #0A0A0A; color: #FFFFFF; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
        .card { background: #141414; border: 1px solid #262626; border-radius: 12px; padding: 32px 48px; text-align: center; max-width: 400px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
        h1 { font-size: 20px; margin-bottom: 8px; color: #10B981; }
        p { font-size: 14px; color: #A3A3A3; line-height: 1.5; }
    </style>
</head>
<body>
    <div class='card'>
        <h1>Authenticated Successfully</h1>
        <p>Google OAuth verification is complete.<br/>You may safely close this browser window and return to <strong>Agent Pulse</strong>.</p>
    </div>
</body>
</html>";

            if (!string.IsNullOrEmpty(returnedError) || string.IsNullOrEmpty(returnedCode))
            {
                responseHtml = $@"<!DOCTYPE html><html><body style='background:#0A0A0A;color:#EF4444;font-family:sans-serif;text-align:center;padding:50px;'>
<h2>Authentication Error</h2><p>{WebUtility.HtmlEncode(returnedError ?? "No code received")}</p></body></html>";
            }

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            res.ContentLength64 = buffer.Length;
            res.ContentType = "text/html";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            res.OutputStream.Close();

            if (!string.IsNullOrEmpty(returnedError) || string.IsNullOrEmpty(returnedCode))
            {
                return false;
            }

            var success = await ExchangeCodeForTokensAsync(returnedCode, redirectUri, codeVerifier, cancellationToken);
            if (success)
            {
                await TryFetchUserInfoAsync(cancellationToken);
                AuthStateChanged?.Invoke(true);
                return true;
            }

            return false;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private async Task<bool> ExchangeCodeForTokensAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            { "code", code },
            { "client_id", Config.ClientId },
            { "client_secret", Config.ClientSecret },
            { "redirect_uri", redirectUri },
            { "grant_type", "authorization_code" },
            { "code_verifier", codeVerifier }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleTokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonSerializer.Deserialize<GoogleTokenInfo>(json);
        if (token != null && !string.IsNullOrEmpty(token.AccessToken))
        {
            token.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn);
            CurrentToken = token;
            SaveTokens();
            return true;
        }

        return false;
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentToken == null)
            return null;

        if (!CurrentToken.IsExpired)
            return CurrentToken.AccessToken;

        if (string.IsNullOrEmpty(CurrentToken.RefreshToken))
            return null;

        try
        {
            var parameters = new Dictionary<string, string>
            {
                { "client_id", Config.ClientId },
                { "client_secret", Config.ClientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", CurrentToken.RefreshToken }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, GoogleTokenEndpoint)
            {
                Content = new FormUrlEncodedContent(parameters)
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var refreshed = JsonSerializer.Deserialize<GoogleTokenInfo>(json);
            if (refreshed != null && !string.IsNullOrEmpty(refreshed.AccessToken))
            {
                CurrentToken.AccessToken = refreshed.AccessToken;
                CurrentToken.ExpiresIn = refreshed.ExpiresIn;
                CurrentToken.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn > 60 ? refreshed.ExpiresIn - 60 : refreshed.ExpiresIn);

                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    CurrentToken.RefreshToken = refreshed.RefreshToken;

                SaveTokens();
                return CurrentToken.AccessToken;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public async Task<GoogleUserProfile?> TryFetchUserInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetValidAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, GoogleUserInfoEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                CurrentUser = JsonSerializer.Deserialize<GoogleUserProfile>(json);
                return CurrentUser;
            }
        }
        catch { }

        return null;
    }

    public Task SignOutAsync()
    {
        CurrentToken = null;
        CurrentUser = null;
        SaveTokens();
        AuthStateChanged?.Invoke(false);
        return Task.CompletedTask;
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(challengeBytes);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
