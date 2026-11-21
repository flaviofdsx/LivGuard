namespace Flavirus.Core.Ransomware;

public class RansomwareShield : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, int> _recentWrites = new();
    private readonly object _lock = new();

    public event Action<string>? RapidEncryptionDetected;

    public void Start(IEnumerable<string> folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            w.Changed += OnChanged;
            w.Created += OnChanged;
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }

    private void OnChanged(object s, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(e.FullPath) ?? "";
            _recentWrites.TryGetValue(dir, out var c);
            _recentWrites[dir] = c + 1;

            if (_recentWrites[dir] > 100) // 100 escritas no mesmo dir em janela curta
            {
                RapidEncryptionDetected?.Invoke(dir);
                _recentWrites[dir] = 0;
            }
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
    }
}
