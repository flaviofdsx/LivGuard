using System.ComponentModel;
using System.Diagnostics;
using Flavirus.Core.Engine;

namespace Flavirus.Core.Behavior;

public sealed record MemoryProcessFinding(
    int ProcessId,
    string ProcessName,
    IReadOnlyList<YaraRule> MatchingRules);

public sealed record MemoryScanIssue(
    int ProcessId,
    string ProcessName,
    string Message);

public sealed record MemoryScanReport(
    int ProcessesScanned,
    int ProcessesSkipped,
    IReadOnlyList<MemoryProcessFinding> Findings,
    IReadOnlyList<MemoryScanIssue> Issues);

public sealed class MemoryScanner
{
    private static readonly HashSet<string> ProtectedProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit",
            "services", "lsass", "winlogon", "svchost"
        };

    private readonly YaraEngine _yara;

    public MemoryScanner(YaraEngine yara)
    {
        _yara = yara ?? throw new ArgumentNullException(nameof(yara));
    }

    public async Task<MemoryScanReport> ScanAllProcessesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Process memory scanning requires Windows.");
        if (_yara.RuleCount == 0)
            throw new InvalidOperationException("No YARA rules are loaded.");

        var processes = Process.GetProcesses();
        var findings = new List<MemoryProcessFinding>();
        var issues = new List<MemoryScanIssue>();
        var processesScanned = 0;
        var processesSkipped = 0;

        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processId = process.Id;
                string processName;

                try
                {
                    processName = process.ProcessName;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or ArgumentException or Win32Exception)
                {
                    issues.Add(new MemoryScanIssue(
                        processId,
                        "Indisponível",
                        $"Não foi possível consultar o processo: {ex.Message}"));
                    process.Dispose();
                    continue;
                }

                if (processId <= 4 ||
                    processId == Environment.ProcessId ||
                    ProtectedProcessNames.Contains(processName))
                {
                    processesSkipped++;
                    process.Dispose();
                    continue;
                }

                progress?.Report($"Analisando memória: {processName} (PID {processId})");

                try
                {
                    var matchingRules = await Task.Run(
                        () => _yara.ScanProcessMemory(processId),
                        cancellationToken);
                    processesScanned++;
                    if (matchingRules.Count > 0)
                    {
                        findings.Add(new MemoryProcessFinding(
                            processId,
                            processName,
                            matchingRules));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or ArgumentException or
                    UnauthorizedAccessException or Win32Exception or
                    DllNotFoundException or BadImageFormatException)
                {
                    issues.Add(new MemoryScanIssue(
                        processId,
                        processName,
                        ex.Message));
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return new MemoryScanReport(
            processesScanned,
            processesSkipped,
            findings,
            issues);
    }
}
