using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Flavirus.Core.Behavior;
using Flavirus.Core.Engine;
using Flavirus.Core.Models;
using Flavirus.Core.Quarantine;
using Flavirus.Core.Ransomware;
using Flavirus.Core.Vault;
using Flavirus.Core.Web;

namespace Flavirus.UI;

public partial class App : Application
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LivGuard");
    public static FlavirusConfig Config { get; } = new();
    public static ScanEngine Engine { get; private set; } = null!;
    public static WebShield Web { get; private set; } = null!;
    public static BehaviorShield Behavior { get; private set; } = null!;
    public static RansomwareShield? Ransomware { get; private set; }
    public static QuarantineManager Quarantine { get; private set; } = null!;
    public static VaultManager Vault { get; private set; } = null!;
    public static bool GameMode { get; private set; }
    private static FileSystemWatcher? _realTimeWatcher;
    private static readonly WindowsPowerPlanManager PowerPlanManager = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            TryWriteCrashLog(ex.Exception);
            MessageBox.Show($"Erro: {ex.Exception.Message}", "Flavirus",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var baseDir = AppContext.BaseDirectory;
            var db = new SignatureDatabase(Path.Combine(DataDirectory, Config.SignatureDbPath));
            var yara = new YaraEngine();
            var rulesPath = Path.Combine(baseDir, "Data", "rules.yaml");
            if (!File.Exists(rulesPath)) rulesPath = Path.Combine(baseDir, "rules.yaml");
            if (File.Exists(rulesPath)) yara.LoadRules(RuleLoader.LoadFromYaml(rulesPath));
            Engine = new ScanEngine(db, yara);
            Web = new WebShield();
            Web.LoadFromList(new[] { "malware-test.com", "phishing-example.org" });
            Behavior = new BehaviorShield();
            Quarantine = new QuarantineManager(Path.Combine(DataDirectory, Config.QuarantinePath));
            Vault = new VaultManager(
                Path.Combine(DataDirectory, Config.VaultPath),
                "senha_mestre_dev");
            StartRealTimeProtection();
            LogEvent("SISTEMA", "Proteção em tempo real iniciada");
            Task.Run(() =>
            {
                try
                {
                    Ransomware = new RansomwareShield();
                    var dirs = new[] {
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    }.Where(Directory.Exists).ToArray();
                    if (dirs.Length > 0) Ransomware.Start(dirs);
                }
                catch { }
            });
            new TrayIconManager().Initialize();
        }
        catch (Exception ex)
        {
            TryWriteCrashLog(ex);
            MessageBox.Show(
                $"Flavirus falhou ao iniciar:\n\n{ex.Message}\n\nVeja crash.log em {DataDirectory}.",
                "Flavirus", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var result = PowerPlanManager.TryRestore();
        if (!result.Success)
        {
            MessageBox.Show(
                $"Não foi possível restaurar o plano de energia anterior:{Environment.NewLine}{result.Error}",
                "Modo Jogo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnExit(e);
    }

    public static (bool Success, string Error) SetGameModePowerPlan(bool enabled) =>
        enabled ? PowerPlanManager.TryEnable() : PowerPlanManager.TryRestore();

    public static void StartRealTimeProtection()
    {
        if (_realTimeWatcher is not null)
            return;

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        if (!Directory.Exists(downloads))
            return;

        _realTimeWatcher = new FileSystemWatcher(downloads, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _realTimeWatcher.Created += (_, args) =>
            _ = ScanDroppedFileAsync(args.FullPath);
    }

    private static async Task ScanDroppedFileAsync(string filePath)
    {
        if (!Config.ProtectionEnabled || !File.Exists(filePath))
            return;

        try
        {
            await Task.Delay(250);
            var result = await Engine.ScanFileAsync(filePath);
            if (result.ThreatsFound <= 0)
                return;

            await Quarantine.QuarantineAsync(filePath);
            LogEvent("TEMPO REAL",
                $"Ameaça bloqueada e isolada: {Path.GetFileName(filePath)}");
            TrayIconManager.NotifyBlocked(Path.GetFileName(filePath));
        }
        catch
        {
        }
    }

    public static void LogEvent(string eventName, string details)
    {
        try
        {
            var dbPath = Path.Combine(DataDirectory, Config.SignatureDbPath);
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS HistoricoEventos (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Data TEXT NOT NULL,
                    Evento TEXT NOT NULL,
                    Detalhes TEXT
                );
                """;
            command.ExecuteNonQuery();
            command.CommandText =
                """
                INSERT INTO HistoricoEventos(Data, Evento, Detalhes)
                VALUES(datetime('now', 'localtime'), $evento, $detalhes)
                """;
            command.Parameters.AddWithValue("$evento", eventName);
            command.Parameters.AddWithValue("$detalhes", details);
            command.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    private static void TryWriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(Path.Combine(DataDirectory, "crash.log"), exception.ToString());
        }
        catch
        {
        }
    }

    public static void SetGameMode(bool enabled)
    {
        GameMode = enabled;
        Config.GameMode = enabled;
        if (!Current.Resources.Contains("BgBrush"))
            return;

        if (enabled)
        {
            Current.Resources["BgBrush"] =
                new SolidColorBrush(Color.FromRgb(15, 5, 5));
            Current.Resources["PanelBrush"] =
                new SolidColorBrush(Color.FromRgb(25, 8, 8));
            Current.Resources["PanelBorderBrush"] =
                new SolidColorBrush(Color.FromRgb(40, 15, 15));
            Current.Resources["AccentCyan"] =
                new SolidColorBrush(Color.FromRgb(255, 61, 61));
            Current.Resources["AccentGreen"] =
                new SolidColorBrush(Color.FromRgb(255, 61, 61));
            Current.Resources["AlertRed"] =
                new SolidColorBrush(Color.FromRgb(255, 140, 140));
            LogEvent("GAME MODE", "Modo gamer ativado");
        }
        else
        {
            Current.Resources["BgBrush"] =
                new SolidColorBrush(Color.FromRgb(10, 14, 10));
            Current.Resources["PanelBrush"] =
                new SolidColorBrush(Color.FromRgb(18, 24, 18));
            Current.Resources["PanelBorderBrush"] =
                new SolidColorBrush(Color.FromRgb(28, 38, 28));
            Current.Resources["AccentCyan"] =
                new SolidColorBrush(Color.FromRgb(0, 229, 255));
            Current.Resources["AccentGreen"] =
                new SolidColorBrush(Color.FromRgb(0, 255, 65));
            Current.Resources["AlertRed"] =
                new SolidColorBrush(Color.FromRgb(255, 61, 61));
            LogEvent("GAME MODE", "Modo gamer desativado");
        }
    }
}
