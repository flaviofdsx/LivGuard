using System.Runtime.InteropServices;
using System.Text;
using Flavirus.Core.Engine;
using Flavirus.Core.Models;

namespace Flavirus.Tests;

public class YaraProcessScanTests
{
    [Fact]
    public void ScanProcessMemory_FindsPatternInCurrentProcess()
    {
        const string marker = "LIVGUARD_MEMORY_SCAN_TEST_MARKER_83C1";
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        var memory = Marshal.AllocHGlobal(markerBytes.Length);

        try
        {
            Marshal.Copy(markerBytes, 0, memory, markerBytes.Length);

            using var yara = new YaraEngine();
            yara.LoadRules(new[]
            {
                new YaraRule(
                    "ProcessMemoryMarker",
                    "Test marker",
                    ThreatSeverity.Low,
                    new[] { marker },
                    Array.Empty<byte[]>())
            });

            var matches = yara.ScanProcessMemory(Environment.ProcessId);

            Assert.Contains(matches, rule => rule.Name == "ProcessMemoryMarker");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void ScanProcessMemory_RejectsInvalidProcessId()
    {
        using var yara = new YaraEngine();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => yara.ScanProcessMemory(0));
    }
}
