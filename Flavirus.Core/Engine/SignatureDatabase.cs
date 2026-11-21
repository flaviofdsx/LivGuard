using Microsoft.Data.Sqlite;
using Flavirus.Core.Models;

namespace Flavirus.Core.Engine;

public class SignatureDatabase : IDisposable
{
    private static readonly (string Hash, string Name, ThreatSeverity Severity, string Description)[]
        BuiltInSignatures =
        {
            (
                "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f",
                "EICAR_Test_File",
                ThreatSeverity.High,
                "Assinatura do arquivo de teste EICAR (nao e um malware real)."
            )
        };

    private readonly SqliteConnection _conn;

    public SignatureDatabase(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Signatures (
                Hash TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Severity INTEGER NOT NULL,
                Description TEXT
            );
            """;
        cmd.ExecuteNonQuery();

        foreach (var signature in BuiltInSignatures)
        {
            using var seed = _conn.CreateCommand();
            seed.CommandText =
                """
                INSERT OR IGNORE INTO Signatures(Hash, Name, Severity, Description)
                VALUES($hash, $name, $severity, $description)
                """;
            seed.Parameters.AddWithValue("$hash", signature.Hash);
            seed.Parameters.AddWithValue("$name", signature.Name);
            seed.Parameters.AddWithValue("$severity", (int)signature.Severity);
            seed.Parameters.AddWithValue("$description", signature.Description);
            seed.ExecuteNonQuery();
        }
    }

    public ThreatInfo? Match(string sha256, string filePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Name, Severity, Description FROM Signatures WHERE Hash = $h LIMIT 1";
        cmd.Parameters.AddWithValue("$h", sha256);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ThreatInfo(filePath, r.GetString(0), (ThreatSeverity)r.GetInt32(1),
            r.GetString(2), DateTime.UtcNow, sha256);
    }

    public void AddSignature(string hash, string name, ThreatSeverity sev, string desc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Signatures(Hash,Name,Severity,Description) VALUES($h,$n,$s,$d)";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$s", (int)sev);
        cmd.Parameters.AddWithValue("$d", desc);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
