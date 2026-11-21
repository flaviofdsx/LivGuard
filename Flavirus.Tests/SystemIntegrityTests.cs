using Flavirus.Core.Behavior;

namespace Flavirus.Tests;

public class SystemIntegrityTests
{
    [Fact]
    public void DetectChanges_ReportsAddedChangedAndRemovedEntries_Once()
    {
        IReadOnlyDictionary<string, string> snapshot =
            new Dictionary<string, string>
            {
                ["HKCU\\Run\\Keep"] = "old-value",
                ["HKCU\\Run\\Remove"] = "value"
            };
        var integrity = new SystemIntegrity(() => snapshot);
        integrity.CaptureBaseline();

        snapshot = new Dictionary<string, string>
        {
            ["HKCU\\Run\\Keep"] = "new-value",
            ["HKCU\\Run\\Added"] = "value"
        };

        var changes = integrity.DetectChanges();

        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, change => change.Contains("alterada: HKCU\\Run\\Keep"));
        Assert.Contains(changes, change => change.Contains("Nova entrada de inicialização: HKCU\\Run\\Added"));
        Assert.Contains(changes, change => change.Contains("removida: HKCU\\Run\\Remove"));
        Assert.Empty(integrity.DetectChanges());
    }

    [Fact]
    public void DetectChanges_RequiresCapturedBaseline()
    {
        var integrity = new SystemIntegrity(
            () => new Dictionary<string, string>());

        Assert.Throws<InvalidOperationException>(integrity.DetectChanges);
    }
}
