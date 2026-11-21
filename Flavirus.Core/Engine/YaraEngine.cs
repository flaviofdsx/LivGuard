namespace Flavirus.Core.Engine;

public sealed record YaraRule(
    string Name, string Description,
    Models.ThreatSeverity Severity,
    IReadOnlyList<string> StringPatterns,
    IReadOnlyList<byte[]> HexPatterns);

public class YaraEngine
{
    private readonly List<YaraRule> _rules = new();
    public void LoadRules(IEnumerable<YaraRule> rules) => _rules.AddRange(rules);
    public int RuleCount => _rules.Count;

    public YaraRule? Match(byte[] content)
    {
        foreach (var rule in _rules)
        {
            foreach (var hex in rule.HexPatterns)
                if (Contains(content, hex)) return rule;
            foreach (var s in rule.StringPatterns.Where(x => x.Length >= 6))
            {
                var b = System.Text.Encoding.ASCII.GetBytes(s);
                if (Contains(content, b)) return rule;
            }
        }
        return null;
    }

    private static bool Contains(byte[] hay, byte[] needle)
    {
        if (needle.Length == 0 || hay.Length < needle.Length) return false;
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }
}
