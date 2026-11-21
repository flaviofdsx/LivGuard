using System.Globalization;
using Microsoft.Win32;

namespace Flavirus.Core.Behavior;

public class SystemIntegrity
{
    private readonly Func<IReadOnlyDictionary<string, string>> _snapshotReader;
    private readonly object _gate = new();
    private Dictionary<string, string>? _baseline;

    public SystemIntegrity()
        : this(ReadRegistrySnapshot)
    {
    }

    internal SystemIntegrity(
        Func<IReadOnlyDictionary<string, string>> snapshotReader)
    {
        _snapshotReader = snapshotReader ??
            throw new ArgumentNullException(nameof(snapshotReader));
    }

    public void CaptureBaseline()
    {
        lock (_gate)
            _baseline = CopySnapshot(_snapshotReader());
    }

    public IReadOnlyList<string> DetectChanges()
    {
        lock (_gate)
        {
            if (_baseline is null)
                throw new InvalidOperationException("CaptureBaseline must be called first.");

            var current = CopySnapshot(_snapshotReader());
            var changes = new List<string>();

            foreach (var entry in current)
            {
                if (!_baseline.TryGetValue(entry.Key, out var previousValue))
                    changes.Add($"Nova entrada de inicialização: {entry.Key}");
                else if (!string.Equals(previousValue, entry.Value, StringComparison.Ordinal))
                    changes.Add($"Entrada de inicialização alterada: {entry.Key}");
            }

            foreach (var entry in _baseline)
            {
                if (!current.ContainsKey(entry.Key))
                    changes.Add($"Entrada de inicialização removida: {entry.Key}");
            }

            _baseline = current;
            return changes;
        }
    }

    private static Dictionary<string, string> CopySnapshot(
        IReadOnlyDictionary<string, string> snapshot)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
            copy[entry.Key] = entry.Value;
        return copy;
    }

    private static IReadOnlyDictionary<string, string> ReadRegistrySnapshot()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("System Integrity requires Windows.");

        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path) in GetMonitoredKeys())
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = root.OpenSubKey(path, writable: false);
            if (key is null)
                continue;

            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(
                    name,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is not null)
                    snapshot[$@"{hive}\{path}\{name}"] = FormatValue(value);
            }
        }

        return snapshot;
    }

    private static (RegistryHive Hive, string Path)[] GetMonitoredKeys()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("System Integrity requires Windows.");

        return new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")
        };
    }

    private static string FormatValue(object value) =>
        value switch
        {
            string[] strings => string.Join("\0", strings),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
}
