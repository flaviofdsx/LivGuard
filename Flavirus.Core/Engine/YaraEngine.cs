using System.Text;

namespace Flavirus.Core.Engine;

public sealed record YaraRule(
    string Name, string Description,
    Models.ThreatSeverity Severity,
    IReadOnlyList<string> StringPatterns,
    IReadOnlyList<byte[]> HexPatterns);

public class YaraEngine
{
    private sealed record CompiledRule(YaraRule Rule, byte[][] Patterns);

    private const int BufferSize = 81920;
    private readonly List<CompiledRule> _rules = new();
    private int _maxPatternLength;

    public void LoadRules(IEnumerable<YaraRule> rules)
    {
        foreach (var rule in rules)
        {
            var patterns = rule.HexPatterns
                .Where(pattern => pattern.Length > 0)
                .Concat(rule.StringPatterns
                    .Where(pattern => pattern.Length >= 6)
                    .Select(Encoding.ASCII.GetBytes))
                .ToArray();
            _rules.Add(new CompiledRule(rule, patterns));
            _maxPatternLength = Math.Max(
                _maxPatternLength,
                patterns.Select(pattern => pattern.Length).DefaultIfEmpty().Max());
        }
    }

    public int RuleCount => _rules.Count;

    public YaraRule? Match(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Match(content.AsSpan());
    }

    public async Task<YaraRule?> MatchAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var overlap = Math.Max(0, _maxPatternLength - 1);
        var buffer = new byte[BufferSize + overlap];
        var buffered = 0;
        while (true)
        {
            var bytesRead = await content.ReadAsync(
                buffer.AsMemory(buffered, BufferSize),
                cancellationToken);
            if (bytesRead == 0)
                return null;

            var length = buffered + bytesRead;
            var match = Match(buffer.AsSpan(0, length));
            if (match is not null)
                return match;

            buffered = Math.Min(overlap, length);
            buffer.AsSpan(length - buffered, buffered).CopyTo(buffer);
        }

    }

    private YaraRule? Match(ReadOnlySpan<byte> content)
    {
        foreach (var rule in _rules)
            foreach (var pattern in rule.Patterns)
                if (content.IndexOf(pattern) >= 0)
                    return rule.Rule;

        return null;
    }

}
