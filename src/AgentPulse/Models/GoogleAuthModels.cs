using System;
using System.Text.Json.Serialization;

namespace AgentPulse.Models;

public class GoogleAuthConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
}

public class GoogleTokenInfo
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; } = 3600;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-5);
}

public class GoogleUserProfile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("verified_email")]
    public bool VerifiedEmail { get; set; }
}
