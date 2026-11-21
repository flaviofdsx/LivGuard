namespace Flavirus.UI.Views;

public class ProcessItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RamUsage { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RiskColor { get; set; } = string.Empty;
}
