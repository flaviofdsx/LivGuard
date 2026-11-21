using System.Management;
using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Flavirus.UI.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly Queue<double> _dashboardCpuSamples = new();
    private Canvas? _dashboardGraphCanvas;
    private System.Windows.Shapes.Polyline? _dashboardGraphLine;
    private TextBlock? _dashboardMetricsText;
    private ulong? _previousIdleTime;
    private ulong? _previousKernelTime;
    private ulong? _previousUserTime;
    private long? _previousNetworkBytes;
    private long _previousNetworkTimestamp;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemFileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out SystemFileTime idleTime,
        out SystemFileTime kernelTime,
        out SystemFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateClock();
        _timer.Start();
        
        // Abre o painel inicial
        ShowPanel();
    }

    private void UpdateClock()
    {
        try
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            var up = DateTime.UtcNow - _start;
            
            // Atualiza o uptime se estiver na tela principal
            if (ContentArea.Children.Count > 0 && ContentArea.Children[0] is Grid mainGrid)
            {
                var uptimeText = mainGrid.FindName("UptimeText") as TextBlock;
                if (uptimeText != null)
                    uptimeText.Text = $"{(int)up.TotalHours:D2}:{up.Minutes:D2}:{up.Seconds:D2}";
            }

            ConnectionStatus.Text = App.Config.ProtectionEnabled ? "[ ONLINE ]" : "[ PAUSADO ]";
            ConnectionStatus.Foreground = App.Config.ProtectionEnabled
                ? (Brush)FindResource("AccentGreen") : (Brush)FindResource("AlertRed");
        }
        catch { }

        UpdateDashboardMetrics();
    }

    private void UpdateDashboardMetrics()
    {
        if (_dashboardGraphCanvas is null
            || _dashboardGraphLine is null
            || _dashboardMetricsText is null
            || !_dashboardGraphCanvas.IsVisible)
            return;

        var cpu = GetCpuUsagePercent();
        var memory = GetMemoryUsagePercent();
        var networkRate = GetNetworkRate();

        if (cpu.HasValue)
        {
            _dashboardCpuSamples.Enqueue(cpu.Value);
            while (_dashboardCpuSamples.Count > 60)
                _dashboardCpuSamples.Dequeue();
        }

        _dashboardMetricsText.Text =
            $"CPU: {FormatPercent(cpu)}  |  RAM: {FormatPercent(memory)}  |  Rede: {FormatNetworkRate(networkRate)}";
        UpdateDashboardGraphLine();
    }

    private double? GetCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return null;

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();
        if (!_previousIdleTime.HasValue
            || !_previousKernelTime.HasValue
            || !_previousUserTime.HasValue
            || idle < _previousIdleTime.Value
            || kernel < _previousKernelTime.Value
            || user < _previousUserTime.Value)
        {
            _previousIdleTime = idle;
            _previousKernelTime = kernel;
            _previousUserTime = user;
            return null;
        }

        var idleDelta = idle - _previousIdleTime.Value;
        var totalDelta = kernel - _previousKernelTime.Value + user - _previousUserTime.Value;
        _previousIdleTime = idle;
        _previousKernelTime = kernel;
        _previousUserTime = user;

        if (totalDelta == 0 || idleDelta > totalDelta)
            return null;

        return Math.Clamp((1d - idleDelta / (double)totalDelta) * 100d, 0d, 100d);
    }

    private static double? GetMemoryUsagePercent()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
            return null;

        var used = status.TotalPhysical - Math.Min(status.TotalPhysical, status.AvailablePhysical);
        return used * 100d / status.TotalPhysical;
    }

    private double? GetNetworkRate()
    {
        var totalBytes = GetNetworkBytesTransferred();
        var timestamp = Stopwatch.GetTimestamp();
        if (!totalBytes.HasValue)
        {
            _previousNetworkBytes = null;
            _previousNetworkTimestamp = timestamp;
            return null;
        }

        double? rate = null;
        if (_previousNetworkBytes.HasValue
            && totalBytes.Value >= _previousNetworkBytes.Value
            && timestamp > _previousNetworkTimestamp)
        {
            rate = (totalBytes.Value - _previousNetworkBytes.Value)
                * (double)Stopwatch.Frequency
                / (timestamp - _previousNetworkTimestamp);
        }

        _previousNetworkBytes = totalBytes.Value;
        _previousNetworkTimestamp = timestamp;
        return rate;
    }

    private static long? GetNetworkBytesTransferred()
    {
        try
        {
            long totalBytes = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                var statistics = adapter.GetIPStatistics();
                totalBytes += statistics.BytesReceived + statistics.BytesSent;
            }

            return totalBytes;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string FormatPercent(double? value) =>
        value.HasValue ? $"{value.Value:F0}%" : "--";

    private static string FormatNetworkRate(double? bytesPerSecond) =>
        bytesPerSecond.HasValue ? $"{bytesPerSecond.Value / 1024d:F1} KB/s" : "--";

    private void UpdateDashboardGraphLine()
    {
        if (_dashboardGraphCanvas is null || _dashboardGraphLine is null)
            return;

        var width = _dashboardGraphCanvas.ActualWidth;
        var height = _dashboardGraphCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        _dashboardGraphLine.Points.Clear();
        var step = width / 59d;
        var index = 0;
        foreach (var sample in _dashboardCpuSamples)
        {
            var x = width - (_dashboardCpuSamples.Count - index - 1) * step;
            var y = height - 4 - sample / 100d * (height - 8);
            _dashboardGraphLine.Points.Add(new Point(x, y));
            index++;
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            switch (tag)
            {
                case "Painel": ShowPanel(); break;
                case "Scans": ShowScans(); break;
                case "Quarantine": ShowQuarantine(); break;
                case "Vault": ShowVault(); break;
                case "Firewall": ShowFirewall(); break;
                case "AI": ShowAiGuard(); break;
                case "Privacy": ShowPrivacy(); break;
                case "Cleanup": ShowCleanup(); break;
                case "Network": ShowNetwork(); break;
                case "ChildLock": ShowChildLock(); break;
                case "Config": ShowConfig(); break;
                case "Logs": ShowLogs(); break;
                case "About": ShowAbout(); break;
                case "ReleaseNotes": ShowReleaseNotes(); break;
            }
        }
    }

    private void ShowPlaceholder(string title, string description)
    {
        ContentArea.Children.Clear();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 14,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = "[ EM DESENVOLVIMENTO ]",
            Foreground = (Brush)FindResource("AlertRed"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 30, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        ContentArea.Children.Add(stack);
    }

    private void ShowFirewall()
    {
        ContentArea.Children.Clear();
        var grid = new Grid();

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "FIREWALL",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Regras locais para URLs e domínios bloqueados.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        grid.Children.Add(title);

        var panel = new StackPanel { Margin = new Thickness(0, 55, 0, 0) };
        var input = new TextBox
        {
            Height = 40,
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush")
        };
        panel.Children.Add(input);

        var add = new Button
        {
            Content = "BLOQUEAR DOMÍNIO",
            Margin = new Thickness(0, 12, 0, 20),
            Padding = new Thickness(14, 8, 14, 8),
            Background = (Brush)FindResource("AccentGreen"),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        add.Click += (_, _) =>
        {
            var value = input.Text.Trim();
            if (value.Length == 0)
                return;
            var shield = App.Web;
            shield.LoadFromList(new[] { value });
            input.Text = "";
            ShowFirewall();
            MessageBox.Show($"Regra adicionada: {value}", "Flavirus");
        };
        panel.Children.Add(add);

        panel.Children.Add(new TextBlock
        {
            Text = "DOMÍNIOS CONFIGURADOS",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        foreach (var domain in new[] { "malware-test.com", "phishing-example.org" })
            panel.Children.Add(CreateFirewallRule(domain));

        grid.Children.Add(panel);
        ContentArea.Children.Add(grid);
    }

    private Border CreateFirewallRule(string domain)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = domain,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var status = new TextBlock
        {
            Text = "BLOQUEADO",
            Foreground = (Brush)FindResource("AlertRed"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(status, 1);
        row.Children.Add(status);
        card.Child = row;
        return card;
    }

    private void ShowAiGuard()
    {
        ContentArea.Children.Clear();
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "IA GUARD",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Monitoramento comportamental dos processos em execução.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var statusCard = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 20)
        };
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusText = new StackPanel();
        statusText.Children.Add(new TextBlock
        {
            Text = "MOTOR HEURÍSTICO",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold
        });
        statusText.Children.Add(new TextBlock
        {
            Text = "APRENDIZADO ATIVO",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 0)
        });
        statusRow.Children.Add(statusText);
        var refresh = new Button
        {
            Content = "ATUALIZAR PROCESSOS",
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("AccentCyan"),
            BorderBrush = (Brush)FindResource("AccentCyan"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 8, 15, 8),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        refresh.Click += (_, _) => ShowAiGuard();
        Grid.SetColumn(refresh, 1);
        statusRow.Children.Add(refresh);
        statusCard.Child = statusRow;
        Grid.SetRow(statusCard, 1);
        mainGrid.Children.Add(statusCard);

        var list = new StackPanel();
        list.Children.Add(new TextBlock
        {
            Text = ">> PROCESSOS EM EXECUÇÃO",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var processes = System.Diagnostics.Process.GetProcesses()
            .Where(process =>
            {
                try { _ = process.WorkingSet64; return true; }
                catch { return false; }
            })
            .OrderByDescending(process =>
            {
                try { return process.WorkingSet64; }
                catch { return 0; }
            })
            .Take(15)
            .Select(process =>
            {
                var name = "Desconhecido";
                var path = "Acesso negado";
                long ram = 0;
                try
                {
                    name = process.ProcessName + ".exe";
                    path = process.MainModule?.FileName ?? "Desconhecido";
                    ram = process.WorkingSet64;
                }
                catch { }

                var risk = "SEGURO";
                var riskColor = "#FF00FF41";
                if (path.Contains("Temp", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("AppData", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("update", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("install", StringComparison.OrdinalIgnoreCase))
                {
                    risk = "SUSPEITO";
                    riskColor = "#FFFF3D3D";
                }
                else if (name.Contains("System", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("svchost", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    risk = "SISTEMA";
                    riskColor = "#FF6B7A6B";
                }

                return new ProcessItem
                {
                    Id = process.Id,
                    Name = name,
                    Path = path,
                    RamUsage = $"{Math.Round(ram / 1024d / 1024d, 1)} MB",
                    RiskLevel = risk,
                    RiskColor = riskColor
                };
            })
            .ToList();

        foreach (var process in processes)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new StackPanel();
            name.Children.Add(new TextBlock
            {
                Text = $"PID: {process.Id}",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });
            name.Children.Add(new TextBlock
            {
                Text = process.Name,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 0)
            });
            row.Children.Add(name);

            var path = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            path.Children.Add(new TextBlock
            {
                Text = "CAMINHO",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });
            path.Children.Add(new TextBlock
            {
                Text = process.Path,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(path, 1);
            row.Children.Add(path);

            var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(new TextBlock
            {
                Text = process.RamUsage,
                Foreground = (Brush)FindResource("AccentCyan"),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });
            status.Children.Add(new TextBlock
            {
                Text = process.RiskLevel,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(process.RiskColor)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(status, 2);
            row.Children.Add(status);

            var kill = new Button
            {
                Content = "MATAR",
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("AlertRed"),
                BorderBrush = (Brush)FindResource("AlertRed"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 10, 5),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            kill.Click += (_, _) =>
            {
                if (MessageBox.Show(
                        $"Encerrar {process.Name}?",
                        "IA Guard",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                try
                {
                    System.Diagnostics.Process.GetProcessById(process.Id).Kill();
                    ShowAiGuard();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Não foi possível encerrar o processo:\n{ex.Message}", "IA Guard");
                }
            };
            Grid.SetColumn(kill, 3);
            row.Children.Add(kill);
            card.Child = row;
            list.Children.Add(card);
        }

        Grid.SetRow(list, 2);
        mainGrid.Children.Add(list);
        ContentArea.Children.Add(mainGrid);
    }

    private void ShowConfig()
    {
        ContentArea.Children.Clear();
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "HARDWARE & MONITORAMENTO",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Estado real dos recursos e controles do motor Flavirus.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cpuCard = CreateResourceCard("CPU / SISTEMA", "Consultando...", "Verificando carga");
        content.Children.Add(cpuCard);

        var ramCard = CreateResourceCard("MEMÓRIA RAM", "Consultando...", "Verificando uso");
        ramCard.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(ramCard, 1);
        content.Children.Add(ramCard);

        var settingsCard = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(10, 0, 0, 0)
        };
        var settings = new StackPanel();
        settings.Children.Add(new TextBlock
        {
            Text = "MOTOR FLAVIRUS",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        settings.Children.Add(CreateConfigToggle("Proteção em tempo real", App.Config.RealTimeFileShield,
            value => App.Config.RealTimeFileShield = value));
        settings.Children.Add(CreateConfigToggle("IA Guard", App.Config.AiGuard,
            value => App.Config.AiGuard = value));
        settings.Children.Add(CreateConfigToggle("Ransomware Shield", App.Config.RansomwareShield,
            value => App.Config.RansomwareShield = value));

        var save = new Button
        {
            Content = "CONFIGURAÇÕES ATIVAS",
            Background = (Brush)FindResource("AccentCyan"),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(15, 8, 15, 8),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 10, 0, 0)
        };
        save.Click += (_, _) => AppendLocalLog("Configurações atualizadas");
        settings.Children.Add(save);
        settingsCard.Child = settings;
        Grid.SetColumn(settingsCard, 2);
        content.Children.Add(settingsCard);

        Grid.SetRow(content, 1);
        mainGrid.Children.Add(content);
        ContentArea.Children.Add(mainGrid);

        _ = LoadResourceMonitorAsync(cpuCard, ramCard);
    }

    private Border CreateConfigToggle(
        string title,
        bool initialValue,
        Action<bool> onChanged)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var row = new DockPanel();
        row.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var toggle = new CheckBox
        {
            IsChecked = initialValue,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        row.Children.Add(toggle);
        card.Child = row;
        return card;
    }

    private void ShowLogs()
    {
        ContentArea.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "LOGS",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var logLines = new System.Text.StringBuilder();
        try
        {
            var dbPath = Path.Combine(App.DataDirectory, App.Config.SignatureDbPath);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS HistoricoEventos (" +
                "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "Data TEXT NOT NULL, Evento TEXT NOT NULL, Detalhes TEXT)";
            command.ExecuteNonQuery();
            command.CommandText =
                "SELECT Data, Evento, Detalhes FROM HistoricoEventos " +
                "ORDER BY Id DESC LIMIT 100";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                logLines.AppendLine(
                    $"[{reader.GetString(0)}] [{reader.GetString(1)}] {reader.GetString(2)}");
        }
        catch (Exception ex)
        {
            logLines.AppendLine($"[ERRO] Falha ao ler histórico SQLite: {ex.Message}");
        }

        var crashLog = Path.Combine(App.DataDirectory, "crash.log");
        if (File.Exists(crashLog))
        {
            logLines.AppendLine();
            logLines.Append(File.ReadAllText(crashLog));
        }
        if (logLines.Length == 0)
            logLines.Append("Nenhum evento registrado.");
        var logText = logLines.ToString();

        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18)
        };
        card.Child = new TextBlock
        {
            Text = logText,
            Foreground = (Brush)FindResource("TextMuted"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(card);
        ContentArea.Children.Add(panel);
    }

    private void ShowAbout()
    {
        ContentArea.Children.Clear();
        var mainGrid = new Grid();

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "SOBRE O LIVGUARD",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Informações da aplicação e créditos.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 60, 0, 0)
        };
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(40),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 600
        };
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var shieldCanvas = new Canvas
        {
            Width = 100,
            Height = 112,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        shieldCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = (Brush)FindResource("AccentCyan"),
            StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 229, 255)),
            Data = Geometry.Parse("M 50,0 L 100,20 L 100,60 C 100,90 50,112 50,112 C 50,112 0,90 0,60 L 0,20 Z")
        });
        shieldCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = (Brush)FindResource("AccentCyan"),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 25,56 L 45,76 L 75,40")
        });
        center.Children.Add(shieldCanvas);

        center.Children.Add(new TextBlock
        {
            Text = "LIVGUARD ANTIVIRUS",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0)
        });
        center.Children.Add(new TextBlock
        {
            Text = "Criado por Flaviofdsx - Predator Edition",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 20)
        });
        center.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Width = 400,
            Fill = (Brush)FindResource("PanelBorderBrush"),
            Margin = new Thickness(0, 0, 0, 20)
        });
        center.Children.Add(new TextBlock
        {
            Text = "DESENVOLVIDO POR",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        center.Children.Add(new TextBlock
        {
            Text = "Flaviofdsx",
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 20)
        });
        var quote = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 0, 229, 255)),
            BorderBrush = (Brush)FindResource("AccentCyan"),
            BorderThickness = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(20, 15, 20, 15),
            Margin = new Thickness(0, 0, 0, 30)
        };
        quote.Child = new TextBlock
        {
            Text = "\"É a proteção que criei para a minha filha e para o mundo\"",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 16,
            FontStyle = FontStyles.Italic,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        center.Children.Add(quote);
        center.Children.Add(new TextBlock
        {
            Text = "AGRADECIMENTOS ESPECIAIS",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        });
        center.Children.Add(new TextBlock
        {
            Text = "À minha filha Lívia,",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        });
        center.Children.Add(new TextBlock
        {
            Text = "à minha esposa Adrielle",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        });
        center.Children.Add(new TextBlock
        {
            Text = "e ao meu compadre Sidnei.",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        });
        center.Children.Add(new TextBlock
        {
            Text = "por todo o apoio, inspiração e por acreditarem em mim.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 30)
        });
        center.Children.Add(new TextBlock
        {
            Text = "Tecnologias: C# .NET 11, WPF, SQLite.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        center.Children.Add(new TextBlock
        {
            Text = "Projeto para fins educacionais.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        });

        card.Child = center;
        scroll.Content = card;
        mainGrid.Children.Add(scroll);
        ContentArea.Children.Add(mainGrid);
    }

    private Border CreateResourceCard(string title, string mainText, string subText)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = mainText,
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = subText,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11
        });
        card.Child = stack;
        return card;
    }

    private async Task LoadResourceMonitorAsync(Border cpuCard, Border ramCard)
    {
        var resources = await Task.Run(() =>
        {
            var cpuLoad = "Indisponível";
            try
            {
                var load = GetWmiInfo("Win32_Processor", "LoadPercentage")
                    .Split('|')[0]
                    .Trim();
                if (int.TryParse(load, out var percentage))
                    cpuLoad = percentage + "%";
            }
            catch { }

            var totalText = GetWmiInfo("Win32_OperatingSystem", "TotalVisibleMemorySize");
            var freeText = GetWmiInfo("Win32_OperatingSystem", "FreePhysicalMemory");
            var ramText = "Indisponível";
            var usedText = "Memória não disponível";
            if (ulong.TryParse(totalText.Split('|')[0].Trim(), out var totalKb)
                && ulong.TryParse(freeText.Split('|')[0].Trim(), out var freeKb))
            {
                var usedKb = totalKb > freeKb ? totalKb - freeKb : 0;
                var totalGb = totalKb / 1024d / 1024d;
                var usedGb = usedKb / 1024d / 1024d;
                ramText = $"{usedGb:F1} / {totalGb:F1} GB";
                usedText = $"{Math.Round(usedKb * 100d / totalKb, 0)}% em uso";
            }
            return (cpuLoad, ramText, usedText);
        });

        await Dispatcher.InvokeAsync(() =>
        {
            UpdateCard(cpuCard, resources.cpuLoad, "Carga atual da CPU");
            UpdateCard(ramCard, resources.ramText, resources.usedText);
        });
    }

    private static void AppendLocalLog(string message)
    {
        try
        {
            var logPath = Path.Combine(App.DataDirectory, "flavirus.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void ShowScans()
    {
        ContentArea.Children.Clear();

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "VARREDURAS",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Escolha o tipo de varredura para proteger seu sistema.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var buttons = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var quickPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "..", "Local", "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        buttons.Children.Add(CreateActionButton(
            "VARREDURA RAPIDA",
            "Pastas críticas do Windows",
            "Iniciar Rapida",
            () => RunScan("Rápida", quickPaths)));

        var full = CreateActionButton(
            "VARREDURA COMPLETA",
            "Todos os discos (pode demorar)",
            "Iniciar Completa",
            () => RunScan("Completa", new[] { @"C:\" }));
        Grid.SetColumn(full, 1);
        buttons.Children.Add(full);

        var custom = CreateActionButton(
            "PERSONALIZADA",
            "Escolha uma pasta específica",
            "Selecionar Pasta",
            () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog();
                return dialog.ShowDialog() == true
                    ? RunScan("Personalizada", new[] { dialog.FolderName })
                    : Task.CompletedTask;
            });
        Grid.SetColumn(custom, 2);
        buttons.Children.Add(custom);

        var memory = CreateActionButton(
            "PROCESSOS EM MEMÓRIA",
            "Lista os processos em execução",
            "INSPECIONAR",
            async () =>
            {
                await Task.Run(() =>
                {
                    _ = System.Diagnostics.Process.GetProcesses()
                        .OrderByDescending(process =>
                        {
                            try { return process.WorkingSet64; }
                            catch { return 0; }
                        })
                        .Take(100)
                        .Count();
                });
                MessageBox.Show(
                    "Inspeção concluída. A análise avançada de shellcode ainda não está implementada.",
                    "LivGuard");
            });
        Grid.SetColumn(memory, 3);
        buttons.Children.Add(memory);

        Grid.SetRow(buttons, 1);
        mainGrid.Children.Add(buttons);

        var progressBorder = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24)
        };
        var progress = new StackPanel();

        var status = new TextBlock
        {
            Name = "ScanStatusText",
            Text = "Aguardando início da varredura...",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 14,
            FontWeight = FontWeights.Bold
        };
        var progressBar = new ProgressBar
        {
            Name = "ScanProgressBar",
            Height = 10,
            Margin = new Thickness(0, 15, 0, 15),
            Background = new SolidColorBrush(Color.FromRgb(20, 26, 20)),
            Foreground = (Brush)FindResource("AccentCyan"),
            BorderThickness = new Thickness(0)
        };
        var log = new TextBlock
        {
            Name = "ScanLogText",
            Text = "",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 150
        };

        progress.Children.Add(status);
        progress.Children.Add(progressBar);
        progress.Children.Add(log);
        progressBorder.Child = progress;

        Grid.SetRow(progressBorder, 2);
        mainGrid.Children.Add(progressBorder);
        ContentArea.Children.Add(mainGrid);
    }

    private Border CreateActionButton(
        string title,
        string subtitle,
        string buttonText,
        Func<Task> onClick)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 12, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("AccentGreen"),
            FontWeight = FontWeights.Bold,
            FontSize = 14
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 15)
        });

        var button = new Button
        {
            Content = buttonText,
            Background = (Brush)FindResource("AccentGreen"),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 5, 10, 5),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await onClick();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        stack.Children.Add(button);
        border.Child = stack;
        return border;
    }

    private async Task RunScan(string scanType, string[] paths)
    {
        if (ContentArea.Children[0] is not Grid mainGrid)
            return;

        var status = FindVisualChild<TextBlock>(mainGrid, "ScanStatusText");
        var progressBar = FindVisualChild<ProgressBar>(mainGrid, "ScanProgressBar");
        var log = FindVisualChild<TextBlock>(mainGrid, "ScanLogText");
        if (status is null || progressBar is null || log is null)
            return;

        status.Text = $"VARREDURA {scanType.ToUpperInvariant()} EM ANDAMENTO...";
        status.Foreground = (Brush)FindResource("AccentCyan");
        progressBar.IsIndeterminate = true;
        log.Text = "Iniciando motor Flavirus...\n";

        try
        {
            var totalFiles = 0;
            var totalThreats = 0;

            foreach (var path in paths)
            {
                if (!Directory.Exists(path))
                    continue;

                var result = await App.Engine.ScanDirectoryAsync(
                    path,
                    new Progress<string>(message => log.Text = message));
                totalFiles += result.FilesScanned;
                totalThreats += result.ThreatsFound;
            }

            status.Text = $"VARREDURA CONCLUÍDA - {totalFiles} arquivos escaneados";
            status.Foreground = totalThreats > 0
                ? (Brush)FindResource("AlertRed")
                : (Brush)FindResource("AccentGreen");
            log.Text = totalThreats > 0
                ? $"Atencao: {totalThreats} ameacas encontradas!"
                : "Nenhuma ameaca encontrada. Sistema limpo.";
        }
        catch (Exception ex)
        {
            status.Text = "ERRO NA VARREDURA";
            status.Foreground = (Brush)FindResource("AlertRed");
            log.Text = ex.Message;
        }
        finally
        {
            progressBar.IsIndeterminate = false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            var result = FindVisualChild<T>(child, name);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void ShowQuarantine()
    {
        ContentArea.Children.Clear();

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "QUARENTENA",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Arquivos isolados e neutralizados.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        header.Children.Add(title);

        var clearButton = new Button
        {
            Content = "ESVAZIAR QUARENTENA",
            Background = (Brush)FindResource("AlertRed"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(15, 8, 15, 8),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        clearButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "Excluir permanentemente todos os arquivos em quarentena?",
                    "Flavirus",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var file in LoadQuarantineFiles())
                    File.Delete(file);
                ShowQuarantine();
            }
        };
        Grid.SetColumn(clearButton, 1);
        header.Children.Add(clearButton);
        mainGrid.Children.Add(header);

        var items = LoadQuarantine();
        if (items.Count == 0)
        {
            var empty = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0)
            };
            empty.Children.Add(new TextBlock
            {
                Text = "[ QUARENTENA VAZIA ]",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            empty.Children.Add(new TextBlock
            {
                Text = "Nenhuma ameaça foi isolada.",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetRow(empty, 1);
            mainGrid.Children.Add(empty);
        }
        else
        {
            var list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            list.ItemContainerStyle = new Style(typeof(ListBoxItem));
            list.ItemContainerStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
            list.ItemContainerStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
            list.ItemContainerStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 8)));

            foreach (var item in items)
            {
                var card = new Border
                {
                    Background = (Brush)FindResource("PanelBrush"),
                    BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = item.Id + ".flq",
                    Foreground = (Brush)FindResource("TextPrimary"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold
                });
                info.Children.Add(new TextBlock
                {
                    Text = "Arquivo isolado",
                    Foreground = (Brush)FindResource("AlertRed"),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                row.Children.Add(info);

                var date = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                date.Children.Add(new TextBlock
                {
                    Text = "ISOLADO EM",
                    Foreground = (Brush)FindResource("TextMuted"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                });
                date.Children.Add(new TextBlock
                {
                    Text = item.IsolatedAt.ToString("dd/MM/yyyy HH:mm"),
                    Foreground = (Brush)FindResource("AccentCyan"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(date, 1);
                row.Children.Add(date);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var restore = new Button
                {
                    Content = "RESTAURAR",
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("AccentGreen"),
                    BorderBrush = (Brush)FindResource("AccentGreen"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0),
                    FontWeight = FontWeights.Bold
                };
                restore.Click += async (_, _) =>
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = item.Id,
                        Title = "Restaurar arquivo"
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        try
                        {
                            await App.Quarantine.RestoreAsync(item.Id, dialog.FileName);
                            MessageBox.Show("Arquivo restaurado.", "Flavirus");
                            ShowQuarantine();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Erro ao restaurar: {ex.Message}", "Flavirus");
                        }
                    }
                };
                var delete = new Button
                {
                    Content = "DELETAR",
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("AlertRed"),
                    BorderBrush = (Brush)FindResource("AlertRed"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontWeight = FontWeights.Bold
                };
                delete.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                            "Deletar este arquivo permanentemente?",
                            "Flavirus",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        var file = Path.Combine(App.DataDirectory, "quarantine", item.Id + ".flq");
                        if (File.Exists(file))
                            File.Delete(file);
                        ShowQuarantine();
                    }
                };
                buttons.Children.Add(restore);
                buttons.Children.Add(delete);
                Grid.SetColumn(buttons, 2);
                row.Children.Add(buttons);
                card.Child = row;
                list.Items.Add(card);
            }
            Grid.SetRow(list, 1);
            mainGrid.Children.Add(list);
        }

        ContentArea.Children.Add(mainGrid);
    }

    private static string[] LoadQuarantineFiles()
    {
        var directory = Path.Combine(App.DataDirectory, "quarantine");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.flq")
            : Array.Empty<string>();
    }

    private static List<QuarantineItem> LoadQuarantine()
    {
        return LoadQuarantineFiles()
            .Select(file =>
            {
                var info = new FileInfo(file);
                return new QuarantineItem
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    IsolatedAt = info.CreationTime
                };
            })
            .OrderByDescending(item => item.IsolatedAt)
            .ToList();
    }

    private void ShowVault()
    {
        ContentArea.Children.Clear();

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "COFRE",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Proteja arquivos com criptografia AES.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var dropArea = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 18, 13)),
            BorderBrush = (Brush)FindResource("AccentGreen"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(30),
            Margin = new Thickness(0, 0, 0, 20),
            AllowDrop = true
        };
        var dropText = new TextBlock
        {
            Text = "[ SOLTE ARQUIVOS PARA PROTEGER ]",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        dropArea.Child = dropText;
        dropArea.Drop += async (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
            {
                if (!File.Exists(file))
                    continue;

                try
                {
                    await App.Vault.AddAsync(file);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Erro ao proteger {Path.GetFileName(file)}:\n{ex.Message}",
                        "Flavirus");
                }
            }
            ShowVault();
        };
        Grid.SetRow(dropArea, 1);
        mainGrid.Children.Add(dropArea);

        var vaultFiles = GetVaultFiles();
        var listPanel = new StackPanel();
        if (vaultFiles.Length == 0)
        {
            listPanel.Children.Add(new TextBlock
            {
                Text = "Nenhum arquivo protegido no cofre.",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var file in vaultFiles)
            {
                var card = new Border
                {
                    Background = (Brush)FindResource("PanelBrush"),
                    BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = Path.GetFileNameWithoutExtension(file),
                    Foreground = (Brush)FindResource("TextPrimary"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{Math.Round(new FileInfo(file).Length / 1024d, 1)} KB - Criptografado",
                    Foreground = (Brush)FindResource("AccentGreen"),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                row.Children.Add(info);

                var extract = new Button
                {
                    Content = "EXTRAIR",
                    Background = (Brush)FindResource("PanelBrush"),
                    Foreground = (Brush)FindResource("AccentCyan"),
                    BorderBrush = (Brush)FindResource("AccentCyan"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                extract.Click += async (_, _) =>
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = Path.GetFileNameWithoutExtension(file),
                        Title = "Extrair arquivo protegido"
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        try
                        {
                            await App.Vault.ExtractAsync(
                                Path.GetFileNameWithoutExtension(file),
                                dialog.FileName);
                            MessageBox.Show("Arquivo extraído com sucesso.", "Flavirus");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Erro ao extrair:\n{ex.Message}", "Flavirus");
                        }
                    }
                };
                Grid.SetColumn(extract, 1);
                row.Children.Add(extract);
                card.Child = row;
                listPanel.Children.Add(card);
            }
        }
        Grid.SetRow(listPanel, 2);
        mainGrid.Children.Add(listPanel);
        ContentArea.Children.Add(mainGrid);
    }

    private static string[] GetVaultFiles()
    {
        var directory = Path.Combine(App.DataDirectory, "vault");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.vault")
            : Array.Empty<string>();
    }

    private void ShowPrivacy()
    {
        ContentArea.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "PRIVACY SHIELD",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Monitoramento de processos relacionados a chamadas e mídia.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 20)
        });

        var names = new[] { "camera", "zoom", "discord", "teams", "skype", "obs" };
        var matches = System.Diagnostics.Process.GetProcesses()
            .Where(process =>
            {
                try
                {
                    return names.Any(name =>
                        process.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            })
            .Take(20)
            .ToList();

        if (matches.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "[ NENHUM PROCESSO DE MÍDIA DETECTADO ]",
                Foreground = (Brush)FindResource("AccentGreen"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0)
            });
        }
        else
        {
            foreach (var process in matches)
            {
                var card = new Border
                {
                    Background = (Brush)FindResource("PanelBrush"),
                    BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = $"{process.ProcessName}.exe",
                    Foreground = (Brush)FindResource("TextPrimary"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var stop = new Button
                {
                    Content = "ENCERRAR",
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("AlertRed"),
                    BorderBrush = (Brush)FindResource("AlertRed"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontWeight = FontWeights.Bold
                };
                stop.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                            $"Encerrar {process.ProcessName}.exe?",
                            "Privacy Shield",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        process.Kill();
                        ShowPrivacy();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Não foi possível encerrar o processo:\n{ex.Message}",
                            "Privacy Shield");
                    }
                };
                Grid.SetColumn(stop, 1);
                row.Children.Add(stop);
                card.Child = row;
                panel.Children.Add(card);
            }
        }

        ContentArea.Children.Add(panel);
    }

    private async void ShowCleanup()
    {
        ContentArea.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "OTIMIZADOR",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Limpeza segura da pasta temporária do usuário.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 20)
        });

        var tempPath = Path.GetTempPath();
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(30),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 500
        };
        var content = new StackPanel();
        var sizeText = new TextBlock
        {
            Text = "Calculando...",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 48,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(sizeText);
        var detailsText = new TextBlock
        {
            Text = "Verificando arquivos temporários...",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 20)
        };
        content.Children.Add(detailsText);

        var clean = new Button
        {
            Content = "LIMPAR ARQUIVOS TEMPORÁRIOS",
            Background = (Brush)FindResource("AccentCyan"),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(20, 10, 20, 10),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        clean.IsEnabled = false;
        clean.Click += async (_, _) =>
        {
            clean.IsEnabled = false;
            clean.Content = "LIMPANDO...";
            detailsText.Text = "Removendo arquivos temporários disponíveis...";
            try
            {
                var result = await Task.Run(() => CleanTemporaryFiles(tempPath));
                App.LogEvent("OTIMIZADOR", $"{result.Removed} arquivos temporários removidos");
                var stats = await Task.Run(() => GetTemporaryFileStats(tempPath));
                sizeText.Text = $"{Math.Round(stats.Size / 1024d / 1024d, 1)} MB";
                detailsText.Text = $"{stats.Count} arquivos restantes; {result.Removed} removidos"
                    + (result.Skipped + stats.Skipped > 0
                        ? $"; {result.Skipped + stats.Skipped} itens ignorados por acesso ou uso"
                        : string.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro na limpeza:\n{ex.Message}", "LivGuard");
            }
            finally
            {
                clean.Content = "LIMPAR ARQUIVOS TEMPORÁRIOS";
                clean.IsEnabled = true;
            }
        };
        content.Children.Add(clean);
        card.Child = content;
        panel.Children.Add(card);
        ContentArea.Children.Add(panel);

        try
        {
            var stats = await Task.Run(() => GetTemporaryFileStats(tempPath));
            sizeText.Text = $"{Math.Round(stats.Size / 1024d / 1024d, 1)} MB";
            detailsText.Text = $"{stats.Count} arquivos temporários encontrados"
                + (stats.Skipped > 0 ? $"; {stats.Skipped} itens ignorados por acesso ou uso" : string.Empty);
            clean.IsEnabled = stats.Count > 0;
        }
        catch (Exception ex)
        {
            detailsText.Text = "Não foi possível verificar os arquivos temporários.";
            MessageBox.Show($"Erro ao verificar arquivos temporários:\n{ex.Message}", "LivGuard");
        }
    }

    private static (long Size, int Count, int Skipped) GetTemporaryFileStats(string root)
    {
        var size = 0L;
        var count = 0;
        var skipped = 0;
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                        count++;
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            directories.Push(child);
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }

        return (size, count, skipped);
    }

    private static (int Removed, int Skipped) CleanTemporaryFiles(string root)
    {
        var removed = 0;
        var skipped = 0;
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            directories.Push(child);
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }

        return (removed, skipped);
    }

    private async void ShowNetwork()
    {
        ContentArea.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "RADAR DE REDE",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Dispositivos conhecidos pela tabela ARP local.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 20)
        });

        var status = new TextBlock
        {
            Text = "Consultando tabela ARP...",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 10)
        };
        panel.Children.Add(status);
        ContentArea.Children.Add(panel);

        var report = await Task.Run(GetNetworkReport);
        status.Text = $"{report.Adapters.Count} adaptador(es), {report.Devices.Count} entrada(s) ARP.";

        AddNetworkSection(panel, "ADAPTADORES DE REDE");
        foreach (var adapter in report.Adapters)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var adapterText = new TextBlock
            {
                Text =
                    $"Nome: {adapter.Name}\n" +
                    $"Tipo: {adapter.Description}\n" +
                    $"MAC: {adapter.Mac}\n" +
                    $"Status: {adapter.Status}\n" +
                    $"Velocidade: {adapter.Speed}\n" +
                    $"SSID: {adapter.Ssid}\n" +
                    $"IPv4: {adapter.Ipv4}\n" +
                    $"Máscara: {adapter.Mask}\n" +
                    $"IPv6: {adapter.Ipv6}\n" +
                    $"Gateway: {adapter.Gateway}\n" +
                    $"DHCP: {adapter.Dhcp}\n" +
                    $"Servidor DHCP: {adapter.DhcpServer}\n" +
                    $"DNS: {adapter.Dns}",
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            card.Child = adapterText;
            panel.Children.Add(card);
        }

        AddNetworkSection(panel, "DISPOSITIVOS NA TABELA ARP");
        foreach (var device in report.Devices)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = device.Ip,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var mac = new TextBlock
            {
                Text = $"MAC: {device.Mac}",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(mac, 1);
            row.Children.Add(mac);
            var type = new TextBlock
            {
                Text =
                    $"{device.Type}\nInterface: {device.Interface}",
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(type, 2);
            row.Children.Add(type);
            card.Child = row;
            panel.Children.Add(card);
        }
    }

    private sealed record NetworkDevice(
        string Ip,
        string Mac,
        string Type,
        string Interface);

    private sealed record NetworkAdapter(
        string Name,
        string Description,
        string Mac,
        string Status,
        string Speed,
        string Ssid,
        string Ipv4,
        string Mask,
        string Ipv6,
        string Gateway,
        string Dhcp,
        string DhcpServer,
        string Dns);

    private sealed record NetworkReport(
        List<NetworkAdapter> Adapters,
        List<NetworkDevice> Devices);

    private static string GetWindowsSsid()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null)
                return "Não disponível";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var line = output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(entry =>
                    entry.TrimStart().StartsWith("SSID",
                        StringComparison.OrdinalIgnoreCase)
                    && !entry.TrimStart().StartsWith("BSSID",
                        StringComparison.OrdinalIgnoreCase));
            return line is null
                ? "Não disponível"
                : line[(line.IndexOf(':') + 1)..].Trim();
        }
        catch
        {
            return "Não disponível";
        }
    }

    private static void AddNetworkSection(
        StackPanel panel,
        string title)
    {
        var brush = (SolidColorBrush)Application.Current.Resources["TextMuted"];
        panel.Children.Add(new TextBlock
        {
            Text = ">> " + title,
            Foreground = brush,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 8)
        });
    }

    private static NetworkReport GetNetworkReport()
    {
        var adapters = new List<NetworkAdapter>();

        foreach (var networkInterface in
            System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = networkInterface.GetIPProperties();
            var mac = string.Join(
                "-",
                networkInterface.GetPhysicalAddress()
                    .GetAddressBytes()
                    .Select(value => value.ToString("X2")));
            var ipv4 = properties.UnicastAddresses
                .Where(address =>
                    address.Address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString());
            var ipv6 = properties.UnicastAddresses
                .Where(address =>
                    address.Address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Select(address => address.Address.ToString());
            var gateway = properties.GatewayAddresses
                .Select(address => address.Address.ToString());
            var dns = properties.DnsAddresses
                .Select(address => address.ToString());
            var dhcp = properties.DhcpServerAddresses
                .Select(address => address.ToString());
            var mask = properties.UnicastAddresses
                .Where(address =>
                    address.Address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address =>
                    address.IPv4Mask?.ToString() ?? "Não disponível");

            adapters.Add(new NetworkAdapter(
                networkInterface.Name,
                networkInterface.Description,
                string.IsNullOrWhiteSpace(mac) ? "Não disponível" : mac,
                networkInterface.OperationalStatus.ToString(),
                networkInterface.Speed >= 1_000_000
                    ? $"{Math.Round(networkInterface.Speed / 1_000_000d, 0)} Mbps"
                    : "Não disponível",
                networkInterface.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                    ? GetWindowsSsid()
                    : "Não aplicável",
                string.Join(", ", ipv4),
                string.Join(", ", mask),
                string.Join(", ", ipv6),
                string.Join(", ", gateway),
                dhcp.Any() ? "Sim" : "Não",
                dhcp.Any() ? string.Join(", ", dhcp) : "Não disponível",
                dns.Any() ? string.Join(", ", dns) : "Não disponível"));
        }

        var devices = new List<NetworkDevice>();
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(start);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var interfaceName = "Não identificado";

                foreach (var line in output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Interface:", StringComparison.OrdinalIgnoreCase))
                    {
                        interfaceName = trimmed;
                        continue;
                    }
                    if (trimmed.StartsWith("Internet Address",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parts = line.Split(
                        (char[])null!,
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3
                        && parts[0].Count(character => character == '.') == 3
                        && parts[1].Contains('-'))
                    {
                        devices.Add(new NetworkDevice(
                            parts[0],
                            parts[1],
                            parts[2],
                            interfaceName));
                    }
                }
            }
        }
        catch
        {
        }

        return new NetworkReport(adapters, devices);
    }

    private void ShowChildLock()
    {
        ContentArea.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "PROTEÇÃO INFANTIL",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Bloqueio local baseado em regras configuradas.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 20)
        });

        var enabledPath = Path.Combine(App.DataDirectory, "childlock.enabled");
        var enabled = File.Exists(enabledPath);
        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Width = 500,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = enabled ? "Bloqueio ativado" : "Bloqueio desativado",
            Foreground = enabled
                ? (Brush)FindResource("AccentGreen")
                : (Brush)FindResource("TextMuted"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var toggle = new Button
        {
            Content = enabled ? "DESATIVAR" : "ATIVAR",
            Background = enabled
                ? (Brush)FindResource("AlertRed")
                : (Brush)FindResource("AccentCyan"),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(15, 8, 15, 8),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0)
        };
        toggle.Click += (_, _) =>
        {
            if (File.Exists(enabledPath))
                File.Delete(enabledPath);
            else
                File.WriteAllText(enabledPath, "enabled");
            App.LogEvent("PROTEÇÃO INFANTIL",
                File.Exists(enabledPath) ? "Bloqueio ativado" : "Bloqueio desativado");
            ShowChildLock();
        };
        content.Children.Add(toggle);
        card.Child = content;
        panel.Children.Add(card);
        ContentArea.Children.Add(panel);
    }

    private async void RunBootScan()
    {
        ContentArea.Children.Clear();
        var terminal = new TextBlock
        {
            Foreground = (Brush)FindResource("AccentCyan"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Margin = new Thickness(30)
        };
        ContentArea.Children.Add(terminal);

        var lines = new[]
        {
            "LIVGUARD STARTUP SCANNER",
            "Inicializando motor Flavirus Core...",
            "Carregando definições locais...",
            "Verificando Downloads e pasta temporária...",
            "Inspecionando processos em execução...",
            "Nenhuma ameaça conhecida foi encontrada.",
            "Scanner concluído."
        };

        foreach (var line in lines)
        {
            terminal.Text += line + Environment.NewLine;
            await Task.Delay(400);
        }

        await Task.Delay(500);
        ShowPanel();
    }

    private void ShowReleaseNotes()
    {
        ContentArea.Children.Clear();
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        title.Children.Add(new TextBlock
        {
            Text = "RELEASE NOTES",
            Foreground = (Brush)FindResource("AccentCyan"),
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Histórico de versões e evolução do LivGuard.",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 0)
        });
        mainGrid.Children.Add(title);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var content = new StackPanel { Margin = new Thickness(0, 0, 20, 20) };

        void AddVersion(
            string version,
            string title,
            string date,
            string[] features)
        {
            var cardContent = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Background = (Brush)FindResource("AccentCyan"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = version,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            header.Children.Add(badge);

            var titleBlock = new StackPanel
            {
                Margin = new Thickness(15, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleBlock.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            titleBlock.Children.Add(new TextBlock
            {
                Text = date,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(titleBlock, 1);
            header.Children.Add(titleBlock);
            cardContent.Children.Add(header);

            foreach (var feature in features)
            {
                cardContent.Children.Add(new TextBlock
                {
                    Text = "• " + feature,
                    Foreground = (Brush)FindResource("TextMuted"),
                    FontSize = 12,
                    Margin = new Thickness(10, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 15)
            };
            card.Child = cardContent;
            content.Children.Add(card);
        }

        AddVersion("v0.1.0", "Alpha - Estrutura Base", "Fase 1", new[]
        {
            "Estrutura .NET 8/WPF criada.",
            "Motor de hash SHA-256 e regras YARA implementado.",
            "Quarentena AES implementada.",
            "Ícone da bandeja configurado."
        });
        AddVersion("v0.5.0", "Beta - Tema Predator", "Fase 2", new[]
        {
            "Tema escuro e visual de monitoramento.",
            "Painel customizado e janela sem bordas.",
            "Cartões de status, hardware e telemetria.",
            "Controles de proteção no painel."
        });
        AddVersion("v0.8.0", "RC - Proteção Real", "Fase 3", new[]
        {
            "Proteção em tempo real da pasta Downloads.",
            "Histórico de eventos em SQLite.",
            "Web Shield com lista local de bloqueios.",
            "Game Mode com paleta vermelha."
        });
        AddVersion("v1.0.0", "Release - Recursos Premium", "Fase 4", new[]
        {
            "Varreduras rápida, completa e personalizada.",
            "Cofre AES e quarentena funcional.",
            "IA Guard com monitoramento de processos.",
            "Privacy Shield e Otimizador de temporários.",
            "Telegram não configurado por padrão."
        });
        AddVersion("v1.1.0", "Enterprise - Suite", "Fase 5", new[]
        {
            "Radar Wi-Fi usando a tabela ARP local.",
            "Proteção Infantil baseada em estado local.",
            "Boot Scan visual no aplicativo.",
            "Sistema de logs e telas de hardware."
        });
        AddVersion("v1.1.1", "Gold - Identidade Final", "Fase 6", new[]
        {
            "Rebranding LivGuard Antivirus.",
            "Ícone .ico definitivo no executável e tray.",
            "Créditos a Flaviofdsx, Lívia, Adrielle e Sidnei.",
            "Correções de navegação, build e tema."
        });
        AddVersion("v1.2.0", "Live Graph", "Fase 7", new[]
        {
            "Gráfico de CPU e métricas reais de memória RAM e tráfego de rede.",
            "Varredura YARA em blocos para arquivos menores que 500 MiB.",
            "Assinaturas EICAR e de notas de resgate, cobertas por testes xUnit seguros.",
            "Modo Jogo tenta ativar Alto Desempenho e restaura o plano anterior quando disponível.",
            "Licença MIT, EditorConfig, atributos Git e CI Windows com .NET 11."
        });

        scroll.Content = content;
        Grid.SetRow(scroll, 1);
        mainGrid.Children.Add(scroll);
        ContentArea.Children.Add(mainGrid);
    }

    private void ShowPanel()
    {
        ContentArea.Children.Clear();
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var statusCard = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border
        {
            Width = 70,
            Height = 70,
            CornerRadius = new CornerRadius(35),
            Background = new SolidColorBrush(Color.FromRgb(13, 18, 13)),
            BorderBrush = (Brush)FindResource("AccentGreen"),
            BorderThickness = new Thickness(2)
        };
        var shieldCanvas = new Canvas
        {
            Width = 50,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        shieldCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = (Brush)FindResource("AccentCyan"),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 229, 255)),
            Data = Geometry.Parse("M 25,0 L 50,10 L 50,30 C 50,45 25,56 25,56 C 25,56 0,45 0,30 L 0,10 Z")
        });
        shieldCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = (Brush)FindResource("AccentCyan"),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 12,28 L 22,38 L 38,20")
        });
        icon.Child = shieldCanvas;
        statusGrid.Children.Add(icon);
        var statusText = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        statusText.Children.Add(new TextBlock
        {
            Text = "SISTEMA PROTEGIDO",
            Foreground = (Brush)FindResource("AccentGreen"),
            FontWeight = FontWeights.Bold,
            FontSize = 20
        });
        statusText.Children.Add(new TextBlock
        {
            Text = "Todos os shields ativos - Definições atualizadas",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(statusText, 1);
        statusGrid.Children.Add(statusText);
        var togglePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        togglePanel.Children.Add(new TextBlock
        {
            Text = "PROTEÇÃO GERAL",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("PredatorToggle"),
            IsChecked = App.Config.ProtectionEnabled,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        toggle.Checked += (_, _) => { App.Config.ProtectionEnabled = true; UpdateClock(); };
        toggle.Unchecked += (_, _) => { App.Config.ProtectionEnabled = false; UpdateClock(); };
        togglePanel.Children.Add(toggle);

        var bootScan = new Button
        {
            Content = "BOOT SCAN",
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("AccentCyan"),
            BorderBrush = (Brush)FindResource("AccentCyan"),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 10, 0, 0)
        };
        bootScan.Click += (_, _) => RunBootScan();
        togglePanel.Children.Add(bootScan);

        var gameMode = new Button
        {
            Content = App.Config.GameMode ? "GAME MODE: ON" : "GAME MODE: OFF",
            Background = App.Config.GameMode
                ? Brushes.Transparent
                : (Brush)FindResource("AccentCyan"),
            Foreground = App.Config.GameMode
                ? (Brush)FindResource("AccentCyan")
                : Brushes.Black,
            BorderBrush = App.Config.GameMode
                ? (Brush)FindResource("AccentCyan")
                : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 10, 0, 0),
            FontWeight = FontWeights.Bold
        };
        gameMode.Click += async (_, _) =>
        {
            var enabled = !App.Config.GameMode;
            var powerPlan = await Task.Run(() => App.SetGameModePowerPlan(enabled));
            App.SetGameMode(enabled);

            if (!powerPlan.Success)
            {
                MessageBox.Show(
                    powerPlan.Error,
                    "Modo Jogo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (enabled)
                _ = TryLaunchPredatorSenseAsync();

            ShowPanel();
        };
        togglePanel.Children.Add(gameMode);
        Grid.SetColumn(togglePanel, 2);
        statusGrid.Children.Add(togglePanel);
        statusCard.Child = statusGrid;
        topGrid.Children.Add(statusCard);

        var uptimeCard = CreateCard("TEMPO DE PROTEÇÃO", "00:00:00", "sem interrupções", (Brush)FindResource("AccentCyan"));
        ((StackPanel)uptimeCard.Child).Children[1].SetValue(TextBlock.NameProperty, "UptimeText");
        uptimeCard.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(uptimeCard, 1);
        topGrid.Children.Add(uptimeCard);
        mainGrid.Children.Add(topGrid);

        var hardwareGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        hardwareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hardwareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hardwareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var osCard = CreateCard("SISTEMA", "Carregando...", "kernel protegido", (Brush)FindResource("TextPrimary"));
        hardwareGrid.Children.Add(osCard);
        var cpuCard = CreateCard("PROCESSADOR", "Carregando...", "informações de hardware", (Brush)FindResource("AccentCyan"));
        cpuCard.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(cpuCard, 1);
        hardwareGrid.Children.Add(cpuCard);
        var gpuCard = CreateCard("PLACA DE VÍDEO", "Carregando...", "aceleração de hardware", (Brush)FindResource("TextPrimary"));
        gpuCard.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(gpuCard, 2);
        hardwareGrid.Children.Add(gpuCard);
        Grid.SetRow(hardwareGrid, 1);
        mainGrid.Children.Add(hardwareGrid);

        var middleGrid = new Grid();
        middleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        middleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var graphCard = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var graphStack = new StackPanel();
        graphStack.Children.Add(new TextBlock
        {
            Text = ">> ATIVIDADE EM TEMPO REAL",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 15)
        });
        var canvas = new Canvas
        {
            Height = 150,
            Background = new SolidColorBrush(Color.FromRgb(10, 14, 10)),
            ClipToBounds = true
        };
        for (var i = 0; i < 5; i++)
        {
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0,
                Y1 = i * 30,
                X2 = canvas.ActualWidth,
                Y2 = i * 30,
                Stroke = new SolidColorBrush(Color.FromRgb(25, 35, 25))
            });
        }
        var polyline = new System.Windows.Shapes.Polyline
        {
            Stroke = (Brush)FindResource("AccentGreen"),
            StrokeThickness = 2
        };
        canvas.Children.Add(polyline);
        canvas.SizeChanged += (_, _) =>
        {
            foreach (var gridLine in canvas.Children.OfType<System.Windows.Shapes.Line>())
                gridLine.X2 = canvas.ActualWidth;
            UpdateDashboardGraphLine();
        };
        graphStack.Children.Add(canvas);
        var metricsText = new TextBlock
        {
            Text = "CPU: --  |  RAM: --  |  Rede: --",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0)
        };
        graphStack.Children.Add(metricsText);
        _dashboardGraphCanvas = canvas;
        _dashboardGraphLine = polyline;
        _dashboardMetricsText = metricsText;
        graphCard.Child = graphStack;
        middleGrid.Children.Add(graphCard);

        var feedCard = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20)
        };
        var feed = new StackPanel();
        feed.Children.Add(new TextBlock
        {
            Text = ">> ÚLTIMOS EVENTOS",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 15)
        });
        AddFeedItem(feed, "SISTEMA", "Assinaturas atualizadas", (Brush)FindResource("AccentCyan"));
        AddFeedItem(feed, "WEB SHIELD", "Proteção de navegação ativa", (Brush)FindResource("AlertRed"));
        AddFeedItem(feed, "FIREWALL", "Aplicativos monitorados", (Brush)FindResource("AccentGreen"));
        AddFeedItem(feed, "IA GUARD", "Motor heurístico ativo", (Brush)FindResource("TextMuted"));
        feedCard.Child = feed;
        Grid.SetColumn(feedCard, 1);
        middleGrid.Children.Add(feedCard);
        Grid.SetRow(middleGrid, 2);
        mainGrid.Children.Add(middleGrid);

        ContentArea.Children.Add(mainGrid);
        UpdateClock();
        _ = LoadDashboardHardwareAsync(osCard, cpuCard, gpuCard);
    }

    private void AddFeedItem(StackPanel parent, string tag, string message, Brush color)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = $"[{tag}]",
            Foreground = color,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Top
        });
        var messageText = new TextBlock
        {
            Text = message,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(messageText, 1);
        grid.Children.Add(messageText);
        parent.Children.Add(grid);
        parent.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Fill = (Brush)FindResource("PanelBorderBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private Border CreateCard(string title, string mainText, string subText, Brush textColor)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 12, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("TextMuted"), FontSize = 10, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = mainText, Foreground = textColor, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = subText, Foreground = (Brush)FindResource("TextMuted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });

        border.Child = stack;
        return border;
    }

    private async Task LoadHardwareAsync(Border systemCard, Border hardwareCard)
    {
        var systemInfo = await Task.Run(() =>
        {
            var os = GetWmiInfo("Win32_OperatingSystem", "Caption")
                .Replace("Microsoft ", "")
                .Split('|')[0]
                .Trim();
            var cpu = GetWmiInfo("Win32_Processor", "Name")
                .Split('|')[0]
                .Trim();

            var memoryText = GetWmiInfo("Win32_ComputerSystem", "TotalPhysicalMemory");
            var ram = "RAM desconhecida";
            if (ulong.TryParse(memoryText.Split('|')[0].Trim(), out var ramBytes))
                ram = $"{Math.Round(ramBytes / 1024d / 1024d / 1024d, 1)} GB RAM";

            var gpu = GetWmiInfo("Win32_VideoController", "Name")
                .Split('|')[0]
                .Trim();

            return (os, cpu, gpu, ram);
        });

        await Dispatcher.InvokeAsync(() =>
        {
            UpdateCard(systemCard, systemInfo.os, systemInfo.cpu);
            UpdateCard(hardwareCard, systemInfo.gpu, systemInfo.ram);
        });
    }

    private static void UpdateCard(Border card, string mainText, string subText)
    {
        if (card.Child is not StackPanel stack || stack.Children.Count < 3)
            return;

        if (stack.Children[1] is TextBlock main)
            main.Text = mainText;
        if (stack.Children[2] is TextBlock sub)
            sub.Text = subText;
    }

    private async Task LoadDashboardHardwareAsync(
        Border systemCard,
        Border cpuCard,
        Border gpuCard)
    {
        var info = await Task.Run(() =>
        {
            var os = GetWmiInfo("Win32_OperatingSystem", "Caption")
                .Replace("Microsoft ", "")
                .Split('|')[0]
                .Trim();
            var cpu = GetWmiInfo("Win32_Processor", "Name")
                .Split('|')[0]
                .Trim();
            var gpu = GetWmiInfo("Win32_VideoController", "Name")
                .Split('|')[0]
                .Trim();
            var memoryText = GetWmiInfo("Win32_ComputerSystem", "TotalPhysicalMemory");
            var ram = "RAM desconhecida";
            if (ulong.TryParse(memoryText.Split('|')[0].Trim(), out var ramBytes))
                ram = $"{Math.Round(ramBytes / 1024d / 1024d / 1024d, 1)} GB";
            return (os, cpu, gpu, ram);
        });

        await Dispatcher.InvokeAsync(() =>
        {
            UpdateCard(systemCard, info.os, "Kernel protegido");
            UpdateCard(cpuCard, info.cpu, info.ram + " RAM total");
            UpdateCard(gpuCard, info.gpu, "Aceleração de hardware");
        });
    }

    private string GetWmiInfo(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            var results = new List<string>();
            foreach (ManagementObject obj in searcher.Get())
            {
                var val = obj[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(val)) results.Add(val.Trim());
            }
            return results.Count > 0 ? string.Join("|", results) : "Desconhecido";
        }
        catch { return "Desconhecido"; }
    }

    private async Task TryLaunchPredatorSenseAsync()
    {
        await Task.Run(() =>
        {
            var candidates = new List<string>
            {
                @"C:\Program Files\Acer\PredatorSense\PredatorSense.exe",
                @"C:\Program Files (x86)\Acer\PredatorSense\PredatorSense.exe"
            };

            var windowsApps = @"C:\Program Files\WindowsApps";
            if (Directory.Exists(windowsApps))
            {
                try
                {
                    candidates.AddRange(
                        Directory.EnumerateFiles(
                            windowsApps,
                            "PredatorSense.exe",
                            SearchOption.AllDirectories));
                }
                catch
                {
                }
            }

            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable is null)
                return false;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }).ContinueWith(task =>
        {
            if (task.Result)
                return;

            Dispatcher.Invoke(() =>
                MessageBox.Show(
                    "PredatorSense não foi encontrado ou não pôde ser aberto. Ative o modo Turbo manualmente, caso esteja disponível.",
                    "Game Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
        });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}