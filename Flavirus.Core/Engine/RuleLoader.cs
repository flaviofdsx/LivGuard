using Flavirus.Core.Models;
using YamlDotNet.Serialization;

namespace Flavirus.Core.Engine;

public class RuleYamlModel
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";
    [YamlMember(Alias = "severity")]
    public int Severity { get; set; }
    [YamlMember(Alias = "strings")]
    public List<string> Strings { get; set; } = new();
    [YamlMember(Alias = "hex")]
    public List<string> Hex { get; set; } = new();
}

public static class RuleLoader
{
    public static IEnumerable<YaraRule> LoadFromYaml(string path)
    {
        var yaml = File.ReadAllText(path);
        var d = new DeserializerBuilder().Build();
        var items = d.Deserialize<List<RuleYamlModel>>(yaml) ?? new();
        foreach (var r in items)
        {
            yield return new YaraRule(r.Name, r.Description,
                (ThreatSeverity)Math.Clamp(r.Severity, 0, 3),
                r.Strings,
                r.Hex.Select(h => Convert.FromHexString(h.Replace(" ", ""))).ToList());
        }
    }
}
