using System.Text;
using System.Text.RegularExpressions;
using dnYara;

namespace Flavirus.Core.Engine;

public sealed record YaraRule(
    string Name, string Description,
    Models.ThreatSeverity Severity,
    IReadOnlyList<string> StringPatterns,
    IReadOnlyList<byte[]> HexPatterns);

public sealed class YaraEngine : IDisposable
{
    private const long MaxScanBytes = 500L * 1024 * 1024;
    private static readonly object NativeSync = new();
    // libyara is process-global; keep one context because dnYara finalization is not idempotent.
    private static readonly Lazy<YaraContext> NativeContext =
        new(() => new YaraContext());
    private static readonly Regex RuleIdentifier =
        new(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);

    private readonly object _sync = new();
    private readonly Dictionary<string, YaraRule> _rules = new(StringComparer.Ordinal);
    private CompiledRules? _compiledRules;
    private bool _disposed;

    public int RuleCount
    {
        get
        {
            lock (_sync)
                return _rules.Count;
        }
    }

    public void LoadRules(IEnumerable<YaraRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var newRules = rules.ToArray();
        var source = BuildRuleSource(newRules, out var compiledRules);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (source.Length == 0)
            {
                lock (NativeSync)
                {
                    _compiledRules?.Dispose();
                    _compiledRules = null;
                    _rules.Clear();
                }
                return;
            }

            lock (NativeSync)
            {
                _ = NativeContext.Value;
                using var compiler = new Compiler();
                compiler.AddRuleString(source);
                var newlyCompiled = compiler.Compile();

                _compiledRules?.Dispose();
                _compiledRules = newlyCompiled;
                _rules.Clear();
                foreach (var rule in compiledRules)
                    _rules.Add(rule.Key, rule.Value);
            }
        }
    }

    public YaraRule? Match(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > MaxScanBytes)
            throw new InvalidDataException("YARA input exceeds the 500 MiB scan limit.");

        var tempPath = Path.Combine(
            Path.GetTempPath(), $"livguard-scan-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, content);
            return MatchFile(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public YaraRule? MatchFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File to scan was not found.", filePath);
        if (fileInfo.Length > MaxScanBytes)
            throw new InvalidDataException("YARA input exceeds the 500 MiB scan limit.");

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_compiledRules is null)
                return null;

            lock (NativeSync)
            {
                var scanner = new Scanner();
                var results = scanner.ScanFile(filePath, _compiledRules);
                return results.Count == 0
                    ? null
                    : GetRule(results[0].MatchingRule.Identifier);
            }
        }
    }

    public IReadOnlyList<YaraRule> ScanProcessMemory(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_compiledRules is null)
                return Array.Empty<YaraRule>();

            lock (NativeSync)
            {
                var scanner = new Scanner();
                return scanner.ScanProcess(processId, _compiledRules)
                    .Select(result => GetRule(result.MatchingRule.Identifier))
                    .Where(rule => rule is not null)
                    .Select(rule => rule!)
                    .DistinctBy(rule => rule.Name)
                    .ToArray();
            }
        }
    }

    public async Task<YaraRule?> MatchAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var tempPath = Path.Combine(
            Path.GetTempPath(), $"livguard-scan-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var tempFile = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                long totalBytes = 0;
                int bytesRead;
                while ((bytesRead = await content.ReadAsync(
                           buffer.AsMemory(), cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaxScanBytes)
                        throw new InvalidDataException(
                            "YARA input exceeds the 500 MiB scan limit.");

                    await tempFile.WriteAsync(
                        buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }

            return await Task.Run(
                () => MatchFile(tempPath), cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private YaraRule? GetRule(string identifier) =>
        _rules.TryGetValue(identifier, out var rule) ? rule : null;

    private static string BuildRuleSource(
        IEnumerable<YaraRule> rules,
        out Dictionary<string, YaraRule> compiledRules)
    {
        var source = new StringBuilder();
        compiledRules = new Dictionary<string, YaraRule>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);
            if (!RuleIdentifier.IsMatch(rule.Name))
                throw new ArgumentException(
                    $"'{rule.Name}' is not a valid YARA rule identifier.",
                    nameof(rules));
            if (!compiledRules.TryAdd(rule.Name, rule))
                throw new ArgumentException(
                    $"Duplicate YARA rule identifier '{rule.Name}'.",
                    nameof(rules));

            var patterns = rule.HexPatterns
                .Where(pattern => pattern.Length > 0)
                .Concat(rule.StringPatterns
                    .Where(pattern => pattern.Length >= 6)
                    .Select(Encoding.ASCII.GetBytes))
                .ToArray();
            if (patterns.Length == 0)
            {
                compiledRules.Remove(rule.Name);
                continue;
            }

            source.Append("rule ").Append(rule.Name).AppendLine()
                .AppendLine("{")
                .AppendLine("    strings:");
            for (var index = 0; index < patterns.Length; index++)
            {
                source.Append("        $pattern").Append(index).Append(" = { ");
                source.AppendJoin(' ', patterns[index].Select(
                    value => value.ToString("X2")));
                source.AppendLine(" }");
            }

            source.AppendLine("    condition:")
                .AppendLine("        any of them")
                .AppendLine("}")
                .AppendLine();
        }

        return source.ToString();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            lock (NativeSync)
            {
                _compiledRules?.Dispose();
                _compiledRules = null;
                _rules.Clear();
                _disposed = true;
            }
        }
    }
}
