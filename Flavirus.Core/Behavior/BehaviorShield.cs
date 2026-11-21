using System.Diagnostics;

namespace Flavirus.Core.Behavior;

public class BehaviorShield
{
    private readonly HashSet<string> _suspiciousNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mimikatz.exe", "procdump.exe", "psexec.exe",
        "nc.exe", "ncat.exe", "cobaltstrike.exe"
    };

    public event Action<string, string>? SuspiciousProcessDetected;

    public void ScanRunningProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName + ".exe";
                if (_suspiciousNames.Contains(name))
                    SuspiciousProcessDetected?.Invoke(name, $"PID {p.Id}");
            }
            catch { }
        }
    }
}
