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

    private sealed class ChunkedReadStream(byte[] content, int maxReadSize)
        : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(
                buffer[..Math.Min(buffer.Length, maxReadSize)],
                cancellationToken);
        }
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

    [Fact]
    public void Detects_Eicar_Yara_Signature_InMemory()
    {
        var content = System.Text.Encoding.ASCII.GetBytes(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        var rule = LoadYara().Match(content);

        Assert.Equal("EICAR_Test_File", rule?.Name);
    }

    [Theory]
    [InlineData("All your files are encrypted. Send 1 Bitcoin to this address.")]
    [InlineData("your files have been encrypted. Send 1 Bitcoin to this address.")]
    public async Task Detects_Ransomware_Note_In_TempFile(string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "livguard_note_" + Guid.NewGuid() + ".txt");

        try
        {
            await File.WriteAllTextAsync(path, content);

            using var db = new SignatureDatabase(":memory:");
            var engine = new ScanEngine(db, LoadYara());
            var result = await engine.ScanFileAsync(path);

            var threat = Assert.Single(result.Threats);
            Assert.Equal("Ransomware_Note_Standard", threat.RuleName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Detects_Yara_Pattern_In_Large_TextFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "livguard_large_" + Guid.NewGuid() + ".txt");
        var marker = System.Text.Encoding.ASCII.GetBytes("Invoke-WebRequest");
        var fileSize = 51L * 1024 * 1024;

        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(fileSize);
                stream.Position = fileSize - marker.Length;
                await stream.WriteAsync(marker);
            }

            using var db = new SignatureDatabase(":memory:");
            var engine = new ScanEngine(db, LoadYara());
            var result = await engine.ScanFileAsync(path);

            var threat = Assert.Single(result.Threats);
            Assert.Equal("Suspicious_PowerShell_Download", threat.RuleName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MatchAsync_Finds_Pattern_Across_Stream_Reads()
    {
        var content = System.Text.Encoding.ASCII.GetBytes("prefix Invoke-WebRequest suffix");
        await using var stream = new ChunkedReadStream(content, 5);

        var match = await LoadYara().MatchAsync(stream);

        Assert.Equal("Suspicious_PowerShell_Download", match?.Name);
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
