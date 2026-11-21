using Flavirus.Core.Engine;
using Xunit;

namespace Flavirus.Tests;

public class ScanEngineTests
{
    private static YaraEngine LoadYara()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "rules.yaml"),
            Path.Combine(AppContext.BaseDirectory, "rules.yaml"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Flavirus.Core", "Data", "rules.yaml")
        };

        var rulesPath = candidates.FirstOrDefault(File.Exists);
        Assert.NotNull(rulesPath);
        var yara = new YaraEngine();
        yara.LoadRules(RuleLoader.LoadFromYaml(rulesPath!));
        return yara;
    }

    [Fact]
    public async Task Detects_Eicar()
    {
        var path = Path.Combine(Path.GetTempPath(), "eicar_" + Guid.NewGuid() + ".com");
        await File.WriteAllTextAsync(path,
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        using var db = new SignatureDatabase(":memory:");
        var engine = new ScanEngine(db, new YaraEngine());
        var r = await engine.ScanFileAsync(path);

        var threat = Assert.Single(r.Threats);
        Assert.Equal("EICAR_Test_File", threat.RuleName);
        Assert.Equal(
            "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f",
            threat.Sha256);
        File.Delete(path);
    }

    [Theory]
    [InlineData("WNcry@2ol7", "Heuristica_WannaCry_NaoVerificada")]
    [InlineData("~WTR4132.tmp", "Heuristica_Stuxnet_NaoVerificada")]
    [InlineData("perfc.dat", "Heuristica_NotPetya_NaoVerificada")]
    public void Detects_UnverifiedMalwareIndicators(string indicator, string expectedRule)
    {
        var yara = LoadYara();
        var rule = yara.Match(System.Text.Encoding.ASCII.GetBytes(indicator));

        Assert.NotNull(rule);
        Assert.Equal(expectedRule, rule.Name);
    }
}
