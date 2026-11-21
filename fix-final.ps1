# ============================================================
#  FLAVIRUS - Implementacao de Navegacao + Hardware Info
# ============================================================
$ErrorActionPreference = "Stop"
$root = Get-Location
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

function Save-Utf8Bom {
    param([string]$Path, [string]$Content)
    $full = Join-Path $root $Path
    $dir  = Split-Path $full -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($full, $Content, $utf8Bom)
    Write-Host "  [ok] $Path" -ForegroundColor Green
}

Write-Host "`n=== FLAVIRUS: UI UPDATE (Navigation + Hardware) ===" -ForegroundColor Cyan

# ==========================================================
#  1) MainWindow.xaml - Com navegacao e Hardware Info
# ==========================================================
Save-Utf8Bom "Flavirus.UI/Views/MainWindow.xaml" @'
<Window x:Class="Flavirus.UI.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FLAVIRUS // PREDATOR EDITION"
        Height="720" Width="1180"
        Background="{StaticResource BgBrush}"
        WindowStartupLocation="CenterScreen"
        FontFamily="{StaticResource PredatorFont}">
    <Border BorderBrush="{StaticResource AccentGreen}" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="42"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="28"/>
            </Grid.RowDefinitions>
            
            <!-- HEADER -->
            <Border Grid.Row="0" Background="#FF0D120D"
                    BorderBrush="{StaticResource PanelBorderBrush}"
                    BorderThickness="0,0,0,1">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Orientation="Horizontal" Margin="16,0,0,0" VerticalAlignment="Center">
                        <Ellipse Width="8" Height="8" Fill="{StaticResource AccentGreen}" Margin="0,0,10,0"/>
                        <TextBlock Text="FLAVIRUS" Foreground="{StaticResource AccentGreen}"
                                   FontWeight="Bold" FontSize="14"/>
                        <TextBlock Text=" // PREDATOR EDITION" Foreground="{StaticResource TextMuted}"
                                   FontSize="12" VerticalAlignment="Center"/>
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="0,0,8,0" VerticalAlignment="Center">
                        <TextBlock x:Name="ClockText" Foreground="{StaticResource TextMuted}"
                                   FontSize="12" Margin="0,0,20,0" VerticalAlignment="Center"/>
                        <Button Content="—" Width="36" Height="28" Click="Minimize_Click"
                                Background="Transparent" Foreground="{StaticResource TextMuted}"
                                BorderThickness="0" FontWeight="Bold"/>
                        <Button Content="X" Width="36" Height="28" Click="Close_Click"
                                Background="Transparent" Foreground="{StaticResource AlertRed}"
                                BorderThickness="0" FontWeight="Bold"/>
                    </StackPanel>
                </Grid>
            </Border>

            <!-- CONTEUDO -->
            <Grid Grid.Row="1">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="230"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                
                <!-- SIDEBAR -->
                <Border Grid.Column="0" Background="{StaticResource PanelBrush}"
                        BorderBrush="{StaticResource PanelBorderBrush}" BorderThickness="0,0,1,0">
                    <StackPanel Margin="0,20,0,0">
                        <TextBlock Text=">> STATUS" Foreground="{StaticResource TextMuted}"
                                   FontSize="10" FontWeight="Bold" Margin="20,0,0,10"/>
                        <Button Content="PAINEL"      Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Painel"/>
                        <Button Content="VARREDURAS"  Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Scans"/>
                        <Button Content="QUARENTENA"  Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Quarantine"/>
                        <Button Content="COFRE"       Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Vault"/>
                        <Button Content="FIREWALL"    Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Firewall"/>
                        <Button Content="IA GUARD"    Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="AI"/>

                        <TextBlock Text=">> SISTEMA" Foreground="{StaticResource TextMuted}"
                                   FontSize="10" FontWeight="Bold" Margin="20,30,0,10"/>
                        <Button Content="CONFIG"      Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Config"/>
                        <Button Content="LOGS"        Style="{StaticResource SidebarButton}" Click="Nav_Click" Tag="Logs"/>

                        <Border Background="#FF0D120D" CornerRadius="6" Margin="16,40,16,0" Padding="14">
                            <StackPanel>
                                <TextBlock Text="ASSINATURAS" Foreground="{StaticResource TextMuted}" FontSize="9" FontWeight="Bold"/>
                                <TextBlock Text="v2.14.089" Foreground="{StaticResource AccentCyan}" FontSize="14" FontWeight="Bold" Margin="0,4,0,0"/>
                                <TextBlock Text="ATUALIZADO HOJE" Foreground="{StaticResource TextMuted}" FontSize="9" Margin="0,2,0,0"/>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </Border>

                <!-- AREA PRINCIPAL (ScrollViewer para telas que crescem) -->
                <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto">
                    <Grid x:Name="ContentArea" Margin="24">
                        <!-- Aqui o codigo C# vai injetar as telas -->
                    </Grid>
                </ScrollViewer>
            </Grid>

            <!-- FOOTER -->
            <Border Grid.Row="2" Background="#FF0D120D"
                    BorderBrush="{StaticResource PanelBorderBrush}" BorderThickness="0,1,0,0">
                <Grid Margin="16,0">
                    <TextBlock Text="FLAVIRUS CORE ENGINE - BUILD 2025.11 - PREDATOR EDITION"
                               Foreground="{StaticResource TextMuted}" FontSize="10"
                               VerticalAlignment="Center"/>
                    <TextBlock x:Name="ConnectionStatus" Text="[ ONLINE ]"
                               Foreground="{StaticResource AccentGreen}"
                               FontSize="10" FontWeight="Bold"
                               HorizontalAlignment="Right" VerticalAlignment="Center"/>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
'@

# ==========================================================
#  2) MainWindow.xaml.cs - Logica de Hardware e Navegacao
# ==========================================================
Save-Utf8Bom "Flavirus.UI/Views/MainWindow.xaml.cs" @'
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Flavirus.UI.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;

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
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            switch (tag)
            {
                case "Painel": ShowPanel(); break;
                case "Scans": ShowPlaceholder("VARREDURAS", "Iniciar varredura rapida, completa ou personalizada."); break;
                case "Quarantine": ShowPlaceholder("QUARENTENA", "Arquivos infectados isolados com seguranca."); break;
                case "Vault": ShowPlaceholder("COFRE", "Criptografia AES-256 para seus arquivos confidenciais."); break;
                case "Firewall": ShowPlaceholder("FIREWALL", "Controle de trafego de rede e bloqueio de IPs."); break;
                case "AI": ShowPlaceholder("IA GUARD", "Analise heuristica baseada em machine learning."); break;
                case "Config": ShowPlaceholder("CONFIGURAÇÕES", "Ajustes do motor do antivirus e atualizacoes."); break;
                case "Logs": ShowPlaceholder("LOGS", "Historico de eventos e ameacas detectadas."); break;
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

    private void ShowPanel()
    {
        ContentArea.Children.Clear();

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- Bloco 1: Status Protecao ---
        var borderStatus = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28)
        };

        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icone
        var borderIcon = new Border
        {
            Width = 90, Height = 90, CornerRadius = new CornerRadius(45),
            Background = new SolidColorBrush(Color.FromRgb(13, 18, 13)),
            BorderBrush = (Brush)FindResource("AccentGreen"),
            BorderThickness = new Thickness(2)
        };
        borderIcon.Child = new TextBlock
        {
            Text = "[ V ]", FontSize = 28, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentGreen"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusGrid.Children.Add(borderIcon);

        // Textos
        var stackText = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        stackText.Children.Add(new TextBlock
        {
            Text = "SISTEMA PROTEGIDO", Foreground = (Brush)FindResource("AccentGreen"),
            FontWeight = FontWeights.Bold, FontSize = 22
        });
        stackText.Children.Add(new TextBlock
        {
            Text = "Todos os shields ativos - Definicoes atualizadas", Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 12, Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(stackText, 1);
        statusGrid.Children.Add(stackText);

        // Toggle
        var stackToggle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        stackToggle.Children.Add(new TextBlock
        {
            Text = "PROTEÇÃO GERAL", Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var toggle = new CheckBox { Style = (Style)FindResource("PredatorToggle"), IsChecked = App.Config.ProtectionEnabled, HorizontalAlignment = HorizontalAlignment.Center };
        toggle.Checked += (_, _) => App.Config.ProtectionEnabled = true;
        toggle.Unchecked += (_, _) => App.Config.ProtectionEnabled = false;
        stackToggle.Children.Add(toggle);
        Grid.SetColumn(stackToggle, 2);
        statusGrid.Children.Add(stackToggle);

        borderStatus.Child = statusGrid;
        mainGrid.Children.Add(borderStatus);

        // --- Bloco 2: Telemetria e Hardware ---
        var stackBottom = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        Grid.SetRow(stackBottom, 1);
        
        stackBottom.Children.Add(new TextBlock
        {
            Text = ">> TELEMETRIA & HARDWARE", Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12)
        });

        var gridCards = new Grid();
        gridCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Card 1 - Uptime
        var card1 = CreateCard("TEMPO DE PROTEÇÃO", "00:00:00", "sem interrupções", (Brush)FindResource("AccentGreen"));
        (card1.Child as StackPanel)!.Children[1].SetValue(TextBlock.NameProperty, "UptimeText");
        gridCards.Children.Add(card1);

        // Card 2 - Windows / CPU
        var os = GetWmiInfo("Win32_OperatingSystem", "Caption").Replace("Microsoft ", "").Split('|')[0];
        var cpu = GetWmiInfo("Win32_Processor", "Name").Split('|')[0].Trim();
        var card2 = CreateCard("SISTEMA OPERACIONAL", os, cpu, (Brush)FindResource("AccentCyan"));
        Grid.SetColumn(card2, 1);
        gridCards.Children.Add(card2);

        // Card 3 - RAM / GPU
        var ramBytes = Convert.ToUInt64(GetWmiInfo("Win32_ComputerSystem", "TotalPhysicalMemory"));
        var ramGb = $"{Math.Round(ramBytes / (1024.0 * 1024.0 * 1024.0), 1)} GB RAM";
        var gpu = GetWmiInfo("Win32_VideoController", "Name").Split('|')[0].Trim();
        var card3 = CreateCard("HARDWARE", gpu, ramGb, (Brush)FindResource("TextPrimary"));
        Grid.SetColumn(card3, 2);
        gridCards.Children.Add(card3);

        stackBottom.Children.Add(gridCards);
        mainGrid.Children.Add(stackBottom);

        ContentArea.Children.Add(mainGrid);
        UpdateClock(); // Atualiza o uptime na hora
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
'@

# ==========================================================
#  3) Ajuste no csproj para incluir System.Management
# ==========================================================
Write-Host "`n[+] Verificando pacote System.Management..." -ForegroundColor Yellow
$csproj = Join-Path $root "Flavirus.UI/Flavirus.UI.csproj"
$content = Get-Content $csproj -Raw
if ($content -notmatch "System.Management") {
    dotnet add Flavirus.UI package System.Management
}

# ==========================================================
#  4) Build Final
# ==========================================================
Write-Host "`n=== Compilando... ===" -ForegroundColor Yellow
dotnet build Flavirus.slnx -c Debug

Write-Host "`n============================================" -ForegroundColor Green
Write-Host " ✅ NAVEGAÇÃO E HARDWARE IMPLEMENTADOS!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host "Para testar, rode:" -ForegroundColor Yellow
Write-Host "  dotnet run --project Flavirus.UI" -ForegroundColor White