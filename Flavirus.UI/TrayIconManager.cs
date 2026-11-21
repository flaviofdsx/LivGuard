using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Flavirus.UI.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace Flavirus.UI;

public class TrayIconManager
{
    private static TrayIconManager? _instance;
    private TaskbarIcon? _icon;
    private MenuItem? _toggleItem;
    private MainWindow? _mainWindow;

    public void Initialize()
    {
        _instance = this;
        _icon = new TaskbarIcon
        {
            ToolTipText = "LIVGUARD · Protegido"
        };
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            _icon.Icon = File.Exists(iconPath)
                ? new Icon(iconPath)
                : CreateFlavirusIcon();
        }
        catch
        {
            _icon.Icon = CreateFlavirusIcon();
        }

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Abrir painel LivGuard" };
        open.Click += (_, _) => ShowMainWindow();
        _toggleItem = new MenuItem { Header = "Desligar proteÃ§Ã£o", IsCheckable = true, IsChecked = true };
        _toggleItem.Click += (_, _) => ToggleProtection();
        var scan = new MenuItem { Header = "Varredura rÃ¡pida" };
        scan.Click += async (_, _) => await RunQuickScan();
        var exit = new MenuItem { Header = "Sair" };
        exit.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(scan);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon CreateFlavirusIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(255, 10, 14, 10));
        graphics.FillEllipse(background, 2, 2, 60, 60);
        using var border = new Pen(Color.FromArgb(255, 0, 229, 255), 4);
        graphics.DrawEllipse(border, 2, 2, 60, 60);

        using var font = new Font("Segoe MDL2 Assets", 28,
            System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
        using var foreground = new SolidBrush(Color.FromArgb(255, 0, 229, 255));
        const string shield = "\uEA18";
        var size = graphics.MeasureString(shield, font);
        graphics.DrawString(shield, font, foreground,
            (64 - size.Width) / 2, (64 - size.Height) / 2);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static void NotifyBlocked(string fileName)
    {
        _instance?._icon?.ShowBalloonTip(
            "LIVGUARD: AMEAÇA BLOQUEADA",
            $"{fileName} foi enviado para a quarentena.",
            BalloonIcon.Warning);
    }

    private void ToggleProtection()
    {
        App.Config.ProtectionEnabled = !App.Config.ProtectionEnabled;
        if (_toggleItem != null)
        {
            _toggleItem.IsChecked = App.Config.ProtectionEnabled;
            _toggleItem.Header = App.Config.ProtectionEnabled ? "Desligar proteÃ§Ã£o" : "Ligar proteÃ§Ã£o";
        }
        if (_icon != null)
        {
            _icon.ToolTipText = App.Config.ProtectionEnabled
                ? "LIVGUARD · Protegido" : "LIVGUARD · DESLIGADO";
        }
    }

    private async Task RunQuickScan()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var r = await App.Engine.ScanDirectoryAsync(downloads);
        _icon?.ShowBalloonTip("LIVGUARD",
            $"Scan: {r.FilesScanned} arquivos Â· {r.ThreatsFound} ameaÃ§as.",
            BalloonIcon.Info);
    }

    public void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                if (_mainWindow is not null)
                {
                    if (_mainWindow.WindowState == WindowState.Minimized)
                        _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    return;
                }

                _mainWindow = new MainWindow();
                _mainWindow.Show();
                _mainWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Falha ao abrir painel:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                    "Flavirus", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }
}

