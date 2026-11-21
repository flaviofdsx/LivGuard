using System.Diagnostics;
using Flavirus.Core.Models;

namespace Flavirus.Core.Engine;

public sealed class ScanEngine : IDisposable
{
    private const long MaxYaraScanBytes = 500L * 1024 * 1024;
    private readonly SignatureDatabase _db;
    private readonly YaraEngine _yara;

    public ScanEngine(SignatureDatabase db, YaraEngine yara)
    { _db = db; _yara = yara; }

    public async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken ct = default)
    {
        var threats = new List<ThreatInfo>();
        var sw = Stopwatch.StartNew();
        try
        {
            var sha = await HashScanner.ComputeSha256Async(filePath, ct);
            var hit = _db.Match(sha, filePath);
            if (hit is not null) threats.Add(hit);

            var size = new FileInfo(filePath).Length;
            if (size > 0 && size < MaxYaraScanBytes)
            {
                var y = await Task.Run(() => _yara.MatchFile(filePath), ct);
                if (y is not null && hit is null)
                    threats.Add(new ThreatInfo(filePath, y.Name, y.Severity,
                        y.Description, DateTime.UtcNow, sha));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        sw.Stop();
        return new ScanResult(filePath, 1, threats.Count, sw.Elapsed, threats);
    }

    public async Task<ScanResult> ScanDirectoryAsync(
        string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var threats = new List<ThreatInfo>();
        var sw = Stopwatch.StartNew();
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                var r = await ScanFileAsync(file, ct);
                threats.AddRange(r.Threats);
                count++;
                progress?.Report($"{count} • {threats.Count} ameaças • {file}");
            }
        }
        catch (UnauthorizedAccessException) { }
        sw.Stop();
        return new ScanResult(root, count, threats.Count, sw.Elapsed, threats);
    }

    public void Dispose() => _yara.Dispose();
}
