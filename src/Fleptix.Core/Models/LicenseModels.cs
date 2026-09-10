namespace Fleptix.Core.Models;

using Fleptix.Core.Interfaces;

/// <summary>
/// Detailed license entitlements and customer metadata.
/// </summary>
public record LicenseInfo
{
    public bool IsActive { get; init; }
    public LicenseTier Tier { get; init; } = LicenseTier.Community;
    public string TierDisplayName { get; init; } = "Fleptix Observer (Community Open-Core)";
    public string? LicenseKeyMasked { get; init; }
    public string? LicenseKeyRaw { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int MaxAllowedNodes { get; init; } = 1;
    public string? InstanceId { get; init; }
    public long? VariantId { get; init; }
    public string? VariantName { get; init; }
}

/// <summary>
/// Request payload for activating a Lemon Squeezy license key.
/// </summary>
public record LicenseActivationRequest
{
    public string LicenseKey { get; init; } = string.Empty;
}

/// <summary>
/// Result of license key activation attempt.
/// </summary>
public record LicenseActivationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public LicenseInfo? License { get; init; }
}

/// <summary>
/// Result of license key deactivation attempt.
/// </summary>
public record LicenseDeactivationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
