namespace Flavirus.Core.Models;

public class FlavirusConfig
{
    public bool ProtectionEnabled { get; set; } = true;
    public bool GameMode { get; set; } = false;
    public bool RealTimeFileShield { get; set; } = true;
    public bool WebShield { get; set; } = true;
    public bool RansomwareShield { get; set; } = true;
    public bool AiGuard { get; set; } = true;
    public bool GhostMode { get; set; } = false;
    public bool ParanoidMode { get; set; } = false;
    public string SignatureDbPath { get; set; } = "signatures.db";
    public string QuarantinePath { get; set; } = "quarantine";
    public string VaultPath { get; set; } = "vault";
    public List<string> RansomwareProtectedFolders { get; set; } = new();
    public List<string> BlockedUrls { get; set; } = new();
}
