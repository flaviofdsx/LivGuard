using System.Text.Json;
using Flavirus.Core.Engine;
using Flavirus.Core.Models;
using Microsoft.Data.Sqlite;

const string ApiEndpoint = "https://mb-api.abuse.ch/api/v1/";

try
{
    return await UpdateSignaturesAsync();
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"[ERRO] Falha na comunicação com MalwareBazaar: {ex.Message}");
    return 1;
}
catch (TaskCanceledException ex)
{
    Console.Error.WriteLine($"[ERRO] A solicitação para MalwareBazaar expirou: {ex.Message}");
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"[ERRO] Resposta JSON inválida do MalwareBazaar: {ex.Message}");
    return 1;
}
catch (SqliteException ex)
{
    Console.Error.WriteLine($"[ERRO] Falha ao atualizar o banco local: {ex.Message}");
    return 1;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"[ERRO] Falha ao ler a resposta ou gravar os dados: {ex.Message}");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[ERRO] Resposta inesperada do MalwareBazaar: {ex.Message}");
    return 1;
}

static async Task<int> UpdateSignaturesAsync()
{
    var authKey = Environment.GetEnvironmentVariable("MALWAREBAZAAR_AUTH_KEY");
    if (string.IsNullOrWhiteSpace(authKey))
    {
        Console.Error.WriteLine(
            "[AVISO] Configure MALWAREBAZAAR_AUTH_KEY para atualizar assinaturas externas.");
        Console.Error.WriteLine(
            "[INFO] A assinatura local do EICAR continua disponível; nenhum hash remoto foi atualizado.");
        return 2;
    }

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.Add("Auth-Key", authKey);

    using var requestContent = new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
            ["query"] = "recent_detections",
            ["hours"] = "48"
        });
    using var response = await client.PostAsync(ApiEndpoint, requestContent);
    response.EnsureSuccessStatusCode();

    await using var responseStream = await response.Content.ReadAsStreamAsync();
    using var document = await JsonDocument.ParseAsync(responseStream);
    var root = document.RootElement;
    if (!root.TryGetProperty("query_status", out var statusElement) ||
        statusElement.ValueKind != JsonValueKind.String)
    {
        throw new InvalidDataException("A resposta não contém query_status.");
    }

    var status = statusElement.GetString();
    if (status == "no_results")
    {
        Console.WriteLine("[INFO] Nenhuma detecção recente para importar.");
        return 0;
    }
    if (status != "ok")
        throw new InvalidDataException($"MalwareBazaar retornou o status '{status}'.");
    if (!root.TryGetProperty("data", out var data) ||
        data.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidDataException("A resposta não contém uma lista de detecções.");
    }

    var localAppData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localAppData))
        throw new InvalidOperationException("A pasta LocalApplicationData não está disponível.");

    var dataDirectory = Path.Combine(localAppData, "LivGuard");
    Directory.CreateDirectory(dataDirectory);
    using var database = new SignatureDatabase(
        Path.Combine(dataDirectory, "signatures.db"));

    var imported = 0;
    var invalid = 0;
    foreach (var item in data.EnumerateArray())
    {
        var hash = GetString(item, "sha256_hash");
        if (!SignatureDatabase.IsSha256Hash(hash))
        {
            invalid++;
            continue;
        }

        var family = GetString(item, "signature");
        if (string.IsNullOrWhiteSpace(family))
            family = "MalwareBazaar";

        database.AddSignature(
            hash!,
            family,
            ThreatSeverity.High,
            $"MalwareBazaar recent detection: {family}");
        imported++;
    }

    Console.WriteLine($"[OK] {imported} assinaturas SHA-256 importadas/atualizadas.");
    if (invalid > 0)
        Console.WriteLine($"[AVISO] {invalid} registros sem SHA-256 válido foram ignorados.");
    Console.WriteLine($"Banco local: {Path.Combine(dataDirectory, "signatures.db")}");
    return 0;
}

static string? GetString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var value) &&
    value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
