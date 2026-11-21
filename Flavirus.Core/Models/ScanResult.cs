namespace Flavirus.Core.Models;

public record ScanResult(
    string TargetPath,
    int FilesScanned,
    int ThreatsFound,
    TimeSpan Duration,
    IReadOnlyList<ThreatInfo> Threats);
