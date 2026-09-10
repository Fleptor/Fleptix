namespace Fleptix.Observer.Services;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service that manages feature entitlements, Lemon Squeezy license validation,
/// local encrypted/persisted license records, and tier resolution.
/// </summary>
public class LicenseService : ILicenseService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LicenseService> _logger;
    private readonly string _licenseFilePath;
    private readonly object _lock = new();

    private LicenseInfo _cachedLicense = new();

    public LicenseService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<LicenseService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Resolve persistent license store path (prioritize /app/snapshots volume mount)
        _licenseFilePath = ResolveLicenseFilePath();

        // Initialize state on startup
        LoadStoredLicense();
    }

    private string ResolveLicenseFilePath()
    {
        if (Directory.Exists("/app/snapshots"))
        {
            return "/app/snapshots/license.json";
        }

        var localSnapshots = Path.Combine(Directory.GetCurrentDirectory(), "snapshots");
        if (!Directory.Exists(localSnapshots))
        {
            try { Directory.CreateDirectory(localSnapshots); } catch { }
        }
        return Path.Combine(localSnapshots, "license.json");
    }

    public bool IsTimeMachineEnabled()
    {
        var tier = GetLicenseTier();
        return tier is LicenseTier.Personal or LicenseTier.Commercial or LicenseTier.Evaluation;
    }

    public LicenseTier GetLicenseTier()
    {
        lock (_lock)
        {
            if (_cachedLicense.IsActive)
            {
                return _cachedLicense.Tier;
            }
        }

        // Check environment variable FLEPTIX_LICENSE_KEY
        var envKey = Environment.GetEnvironmentVariable("FLEPTIX_LICENSE_KEY")
            ?? _configuration["TimeMachine:LicenseKey"]
            ?? _configuration["Fleptix:LicenseKey"];

        if (!string.IsNullOrWhiteSpace(envKey))
        {
            var tier = ResolveTierFromKeyString(envKey);
            if (tier != LicenseTier.Community)
            {
                return tier;
            }
        }

        // Development / Evaluation override
        var envVar = Environment.GetEnvironmentVariable("TIMEMACHINE_ENABLED");
        if (!string.IsNullOrWhiteSpace(envVar) && bool.TryParse(envVar, out var envEnabled) && envEnabled)
        {
            return LicenseTier.Evaluation;
        }

        if (_configuration.GetValue<bool>("TimeMachine:Enabled"))
        {
            return LicenseTier.Evaluation;
        }

        return LicenseTier.Community;
    }

    public string GetTierDisplayName() => GetLicenseTier() switch
    {
        LicenseTier.Commercial => "Time Machine Pro (Commercial Edition)",
        LicenseTier.Personal => "Time Machine (Personal / Homelab)",
        LicenseTier.Evaluation => "Time Machine (Evaluation Mode)",
        _ => "Fleptix Observer (Community Open-Core)"
    };

    public int GetMaxAllowedNodes() => GetLicenseTier() switch
    {
        LicenseTier.Commercial => 3,
        LicenseTier.Personal => 1,
        LicenseTier.Evaluation => 1,
        _ => 1
    };

    public LicenseInfo GetLicenseInfo()
    {
        lock (_lock)
        {
            var currentTier = GetLicenseTier();
            return _cachedLicense with
            {
                Tier = currentTier,
                TierDisplayName = GetTierDisplayName(),
                MaxAllowedNodes = GetMaxAllowedNodes(),
                IsActive = currentTier is not LicenseTier.Community
            };
        }
    }

    public async Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseActivationResult { Success = false, Message = "License key cannot be empty." };
        }

        var cleanedKey = licenseKey.Trim();

        // Validate basic format: either a valid GUID/UUID or legacy prefix
        var isGuid = Guid.TryParse(cleanedKey, out _);
        var isPrefixed = cleanedKey.StartsWith("FLEPTIX-PRO-", StringComparison.OrdinalIgnoreCase) ||
                         cleanedKey.StartsWith("FLEPTIX-PERS-", StringComparison.OrdinalIgnoreCase);

        if (!isGuid && !isPrefixed && cleanedKey.Length < 16)
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "Invalid license key format. Expected a Lemon Squeezy UUID (e.g. FADD8E30-C83D-4380-9609-3A5E896F8264)."
            };
        }

        _logger.LogInformation("Attempting Lemon Squeezy license activation for key: {MaskedKey}", MaskKey(cleanedKey));

        try
        {
            var client = _httpClientFactory.CreateClient("LemonSqueezy");
            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = cleanedKey,
                ["instance_name"] = $"fleptix-{Environment.MachineName.ToLowerInvariant()}"
            });

            var response = await client.PostAsync("https://api.lemonsqueezy.com/v1/licenses/activate", requestContent, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Lemon Squeezy API returned status {Status}: {Body}", response.StatusCode, responseJson);
            }

            var apiResult = JsonSerializer.Deserialize<LemonSqueezyActivationResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResult != null && apiResult.Activated)
            {
                var tier = ResolveTierFromLemonPayload(apiResult);
                var license = new LicenseInfo
                {
                    IsActive = true,
                    Tier = tier,
                    TierDisplayName = tier == LicenseTier.Commercial ? "Time Machine Pro (Commercial Edition)" : "Time Machine (Personal / Homelab)",
                    LicenseKeyRaw = cleanedKey,
                    LicenseKeyMasked = MaskKey(cleanedKey),
                    CustomerName = apiResult.Meta?.CustomerName ?? "Registered Licensee",
                    CustomerEmail = apiResult.Meta?.CustomerEmail ?? string.Empty,
                    ActivatedAt = DateTime.UtcNow,
                    ExpiresAt = apiResult.LicenseKey?.ExpiresAt,
                    MaxAllowedNodes = tier == LicenseTier.Commercial ? 3 : 1,
                    InstanceId = apiResult.Instance?.Id,
                    VariantId = apiResult.Meta?.VariantId,
                    VariantName = apiResult.Meta?.VariantName
                };

                SaveLicense(license);
                _logger.LogInformation("License key successfully activated: Tier={Tier}, Customer={Customer}", tier, license.CustomerName);

                return new LicenseActivationResult
                {
                    Success = true,
                    Message = $"Successfully activated {license.TierDisplayName}!",
                    License = license
                };
            }

            // If Lemon Squeezy returned a specific activation error (e.g. limit reached, disabled)
            if (apiResult != null && !string.IsNullOrWhiteSpace(apiResult.Error))
            {
                return new LicenseActivationResult
                {
                    Success = false,
                    Message = $"Activation failed: {apiResult.Error}"
                };
            }

            // Fallback for valid GUID or development key when API endpoint is unreachable
            if (isPrefixed || (isGuid && !response.IsSuccessStatusCode))
            {
                var fallbackTier = ResolveTierFromKeyString(cleanedKey);
                var fallbackLicense = new LicenseInfo
                {
                    IsActive = true,
                    Tier = fallbackTier,
                    TierDisplayName = fallbackTier == LicenseTier.Commercial ? "Time Machine Pro (Commercial Edition)" : "Time Machine (Personal / Homelab)",
                    LicenseKeyRaw = cleanedKey,
                    LicenseKeyMasked = MaskKey(cleanedKey),
                    CustomerName = "Offline / Enterprise Licensee",
                    ActivatedAt = DateTime.UtcNow,
                    MaxAllowedNodes = fallbackTier == LicenseTier.Commercial ? 3 : 1
                };

                SaveLicense(fallbackLicense);
                return new LicenseActivationResult
                {
                    Success = true,
                    Message = $"Activated {fallbackLicense.TierDisplayName} (Direct Entitlement).",
                    License = fallbackLicense
                };
            }

            return new LicenseActivationResult
            {
                Success = false,
                Message = "Activation rejected by licensing authority. Please check your key or order status."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during license activation");

            // Offline resilience: If network error and key is a valid GUID/prefixed format
            if (isGuid || isPrefixed)
            {
                var offlineTier = ResolveTierFromKeyString(cleanedKey);
                var offlineLicense = new LicenseInfo
                {
                    IsActive = true,
                    Tier = offlineTier,
                    TierDisplayName = offlineTier == LicenseTier.Commercial ? "Time Machine Pro (Commercial Edition)" : "Time Machine (Personal / Homelab)",
                    LicenseKeyRaw = cleanedKey,
                    LicenseKeyMasked = MaskKey(cleanedKey),
                    CustomerName = "Offline Homelab Licensee",
                    ActivatedAt = DateTime.UtcNow,
                    MaxAllowedNodes = offlineTier == LicenseTier.Commercial ? 3 : 1
                };

                SaveLicense(offlineLicense);
                return new LicenseActivationResult
                {
                    Success = true,
                    Message = $"Activated {offlineLicense.TierDisplayName} (Offline Verification Mode).",
                    License = offlineLicense
                };
            }

            return new LicenseActivationResult
            {
                Success = false,
                Message = $"Licensing connection failed: {ex.Message}"
            };
        }
    }

    public async Task<LicenseDeactivationResult> DeactivateLicenseAsync(CancellationToken cancellationToken = default)
    {
        string? rawKey;
        string? instanceId;

        lock (_lock)
        {
            if (!_cachedLicense.IsActive || string.IsNullOrWhiteSpace(_cachedLicense.LicenseKeyRaw))
            {
                return new LicenseDeactivationResult { Success = true, Message = "No active license to deactivate." };
            }

            rawKey = _cachedLicense.LicenseKeyRaw;
            instanceId = _cachedLicense.InstanceId;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                var client = _httpClientFactory.CreateClient("LemonSqueezy");
                var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["license_key"] = rawKey,
                    ["instance_id"] = instanceId
                });

                await client.PostAsync("https://api.lemonsqueezy.com/v1/licenses/deactivate", requestContent, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not notify Lemon Squeezy of deactivation");
        }

        lock (_lock)
        {
            _cachedLicense = new LicenseInfo();
            try
            {
                if (File.Exists(_licenseFilePath))
                {
                    File.Delete(_licenseFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete stored license file");
            }
        }

        return new LicenseDeactivationResult
        {
            Success = true,
            Message = "License deactivated successfully. Reverted to Community Open-Core edition."
        };
    }

    private LicenseTier ResolveTierFromLemonPayload(LemonSqueezyActivationResponse payload)
    {
        var variantId = payload.Meta?.VariantId ?? 0;
        var variantName = (payload.Meta?.VariantName ?? "").ToLowerInvariant();

        // 2112410 is Commercial Pro variant on Lemon Squeezy
        if (variantId == 2112410 || variantName.Contains("commercial") || variantName.Contains("pro"))
        {
            return LicenseTier.Commercial;
        }

        // 2112392 is Personal variant on Lemon Squeezy
        if (variantId == 2112392 || variantName.Contains("personal") || variantName.Contains("homelab"))
        {
            return LicenseTier.Personal;
        }

        return LicenseTier.Personal;
    }

    private LicenseTier ResolveTierFromKeyString(string key)
    {
        if (key.StartsWith("FLEPTIX-PRO-", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.Commercial;
        }
        if (key.StartsWith("FLEPTIX-PERS-", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.Personal;
        }
        return LicenseTier.Personal;
    }

    private void SaveLicense(LicenseInfo license)
    {
        lock (_lock)
        {
            _cachedLicense = license;
            try
            {
                var dir = Path.GetDirectoryName(_licenseFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(license, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_licenseFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist license file to {Path}", _licenseFilePath);
            }
        }
    }

    private void LoadStoredLicense()
    {
        try
        {
            if (File.Exists(_licenseFilePath))
            {
                var json = File.ReadAllText(_licenseFilePath);
                var loaded = JsonSerializer.Deserialize<LicenseInfo>(json);
                if (loaded != null && loaded.IsActive)
                {
                    lock (_lock)
                    {
                        _cachedLicense = loaded;
                    }
                    _logger.LogInformation("Loaded active license: {Tier} for {Customer}", loaded.TierDisplayName, loaded.CustomerName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load license from {Path}", _licenseFilePath);
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "—";
        if (key.Length <= 8) return "********";
        return $"{key[..8]}-****-****-****-{key[^4..]}";
    }

    // ==========================================
    // Internal Lemon Squeezy API DTOs
    // ==========================================
    private class LemonSqueezyActivationResponse
    {
        [JsonPropertyName("activated")]
        public bool Activated { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("license_key")]
        public LemonLicenseKey? LicenseKey { get; set; }

        [JsonPropertyName("instance")]
        public LemonInstance? Instance { get; set; }

        [JsonPropertyName("meta")]
        public LemonMeta? Meta { get; set; }
    }

    private class LemonLicenseKey
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }
    }

    private class LemonInstance
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class LemonMeta
    {
        [JsonPropertyName("variant_id")]
        public long VariantId { get; set; }

        [JsonPropertyName("variant_name")]
        public string? VariantName { get; set; }

        [JsonPropertyName("customer_name")]
        public string? CustomerName { get; set; }

        [JsonPropertyName("customer_email")]
        public string? CustomerEmail { get; set; }
    }
}
