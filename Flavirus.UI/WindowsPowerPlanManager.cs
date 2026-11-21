using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Flavirus.UI;

internal sealed class WindowsPowerPlanManager
{
    private static readonly Guid HighPerformanceScheme =
        new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private Guid? _previousScheme;

    public (bool Success, string Error) TryEnable()
    {
        var schemes = RunPowerCfg("/list");
        if (!schemes.Success)
            return (false, FormatError("Não foi possível consultar os planos de energia.", schemes));

        if (!schemes.Output.Contains(
                HighPerformanceScheme.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
        {
            return (false,
                "O plano Alto Desempenho não está disponível. O tema do Modo Jogo será ativado sem alterar o plano de energia.");
        }

        var active = RunPowerCfg("/getactivescheme");
        if (!active.Success)
            return (false, FormatError("Não foi possível consultar o plano ativo.", active));

        var currentScheme = ExtractSchemeId(active.Output);
        if (!currentScheme.HasValue)
            return (false, "Não foi possível identificar o plano de energia ativo do Windows.");

        var change = RunPowerCfg("/setactive", HighPerformanceScheme.ToString("D"));
        if (!change.Success)
            return (false, FormatError("Não foi possível ativar Alto Desempenho.", change));

        _previousScheme = currentScheme;
        return (true, string.Empty);
    }

    public (bool Success, string Error) TryRestore()
    {
        if (!_previousScheme.HasValue)
            return (true, string.Empty);

        var active = RunPowerCfg("/getactivescheme");
        if (!active.Success)
            return (false, FormatError("Não foi possível consultar o plano ativo.", active));

        var currentScheme = ExtractSchemeId(active.Output);
        if (!currentScheme.HasValue)
            return (false, "Não foi possível identificar o plano de energia ativo do Windows.");

        var previousScheme = _previousScheme.Value;
        if (currentScheme != HighPerformanceScheme)
        {
            _previousScheme = null;
            return (true, string.Empty);
        }

        var restore = RunPowerCfg("/setactive", previousScheme.ToString("D"));
        if (!restore.Success)
            return (false, FormatError("Não foi possível restaurar o plano de energia anterior.", restore));

        _previousScheme = null;
        return (true, string.Empty);
    }

    private static (bool Success, string Output, string Error) RunPowerCfg(
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return (false, string.Empty, ex.Message);
        }

        if (process is null)
            return (false, string.Empty, "O processo powercfg não pôde ser iniciado.");

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(output, error);
            return (process.ExitCode == 0, output.Result, error.Result);
        }
    }

    private static Guid? ExtractSchemeId(string output)
    {
        var match = Regex.Match(
            output,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success && Guid.TryParse(match.Value, out var scheme)
            ? scheme
            : null;
    }

    private static string FormatError(
        string message,
        (bool Success, string Output, string Error) result)
    {
        var details = string.IsNullOrWhiteSpace(result.Error)
            ? result.Output.Trim()
            : result.Error.Trim();
        return string.IsNullOrWhiteSpace(details)
            ? message
            : $"{message}{Environment.NewLine}{details}";
    }
}
