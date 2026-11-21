using System.Security.Cryptography;
using System.Text;

namespace Flavirus.Core.Quarantine;

public class QuarantineManager
{
    private readonly string _dir;
    private readonly byte[] _key;

    public QuarantineManager(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("Flavirus_DEV_KEY_ChangeMe"));
    }

    public async Task<string> QuarantineAsync(string filePath)
    {
        var id = Guid.NewGuid().ToString("N");
        var dest = Path.Combine(_dir, id + ".flq");
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        await using (var input = File.OpenRead(filePath))
        await using (var output = File.Create(dest))
        {
            await output.WriteAsync(aes.IV);
            await using var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
            await input.CopyToAsync(crypto);
        }
        File.Delete(filePath);
        return id;
    }

    public async Task<bool> RestoreAsync(string id, string restorePath)
    {
        var src = Path.Combine(_dir, id + ".flq");
        if (!File.Exists(src)) return false;
        using var aes = Aes.Create();
        aes.Key = _key;
        await using var input = File.OpenRead(src);
        var iv = new byte[16];
        await input.ReadExactlyAsync(iv);
        aes.IV = iv;
        await using var output = File.Create(restorePath);
        await using var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        await crypto.CopyToAsync(output);
        File.Delete(src);
        return true;
    }
}
