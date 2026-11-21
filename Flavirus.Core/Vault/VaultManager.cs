using System.Security.Cryptography;
using System.Text;

namespace Flavirus.Core.Vault;

public class VaultManager
{
    private readonly string _dir;
    private readonly byte[] _key;

    public VaultManager(string dir, string masterPassword)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        _key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(masterPassword),
            Encoding.UTF8.GetBytes("FlavirusVaultSalt"),
            100_000, HashAlgorithmName.SHA256, 32);
    }

    public async Task AddAsync(string sourceFile)
    {
        var dest = Path.Combine(_dir, Path.GetFileName(sourceFile) + ".vault");
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        await using (var input = File.OpenRead(sourceFile))
        await using (var output = File.Create(dest))
        {
            await output.WriteAsync(aes.IV);
            await using var cs = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
            await input.CopyToAsync(cs);
        }
        File.Delete(sourceFile);
    }

    public async Task ExtractAsync(string vaultName, string destPath)
    {
        var src = Path.Combine(_dir, vaultName + ".vault");
        using var aes = Aes.Create();
        aes.Key = _key;
        await using var input = File.OpenRead(src);
        var iv = new byte[16];
        await input.ReadExactlyAsync(iv);
        aes.IV = iv;
        await using var output = File.Create(destPath);
        await using var cs = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        await cs.CopyToAsync(output);
    }
}
