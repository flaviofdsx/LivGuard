namespace Flavirus.Core.Models;

public enum ThreatSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public record ThreatInfo(
    string FilePath,
    string RuleName,
    ThreatSeverity Severity,
    string Description,
    DateTime DetectedAt,
    string? Sha256 = null);
