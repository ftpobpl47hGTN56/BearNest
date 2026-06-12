using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace VpnClient
{
    public partial class MainWindow : Window
    {
        private bool _logPaused = false;
        private readonly List<string> _pausedLogs = new();
        private readonly CoreManager _core;
        private readonly AppStorage _storage;
        private readonly TrayManager _tray;
        private List<ServerConfig> _servers = new();
        private ServerConfig? _selectedServer;
        private SubscriptionInfo? _subInfo;

        private System.Threading.CancellationTokenSource? _watchdogCts;

        private readonly DispatcherTimer _sessionTimer = new();
        private readonly RollingLogger _logger = new();
        private readonly List<string> _allLogs = new();
        private string _logFilter = "All";
        private DateTime _connectTime;

        private long _bytesSent = 0;
        private long _bytesReceived = 0;

        private static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string XrayPath = System.IO.Path.Combine(AppDir, "core", "xray.exe");
        private static readonly string ConfigPath = System.IO.Path.Combine(AppDir, "core", "config.json");
        private bool _isSwitching = false;
        private const string ProxyHost = "127.0.0.1";
        private const int ProxyPort = 19808;

        public MainWindow()
        {
            InitializeComponent();

            _storage = new AppStorage();
            _core = new CoreManager(XrayPath, ConfigPath);
            _tray = new TrayManager();

            _core.LogReceived += OnLog;
            _core.StatusChanged += OnStatusChanged;

            _tray.OnShowWindow += ShowWindow;
            _tray.OnConnect += () => Dispatcher.Invoke(async () => await ConnectAsync());
            _tray.OnDisconnect += () => Dispatcher.Invoke(async () => await DisconnectAsync());
            _tray.OnAutoStartToggled += () => AutoStart.Toggle();

            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick += OnSessionTimerTick;

            var savedUrl = _storage.GetSubscriptionUrl();
            if (!string.IsNullOrEmpty(savedUrl))
                SubUrlBox.Text = savedUrl;

            if (SystemProxy.IsEnabled())
            {
                SystemProxy.Disable();
                OnLog($"[BearNest] {LocalizationManager.Get("LogProxyOld")}");
            }

            Loaded += async (_, _) => await LoadSubscriptionAsync();
            Loaded += (_, _) =>
            {
                var savedLang = _storage.Get("language") ?? LocalizationManager.Languages[0];
                CmbLanguage.SelectedItem = savedLang;
                LocalizationManager.Apply(savedLang);
            };

            ChkAutoStart.Checked -= ChkAutoStart_Checked;
            ChkAutoStart.Unchecked -= ChkAutoStart_Unchecked;
            ChkAutoStart.IsChecked = AutoStart.IsEnabled();
            ChkAutoStart.Checked += ChkAutoStart_Checked;
            ChkAutoStart.Unchecked += ChkAutoStart_Unchecked;
            TxtXrayPath.Text = _storage.Get("xray_path") ?? XrayPath;

            // ── Загружаем настройки сплит-тоннелинга ──
            TxtBypassDomains.Text = _storage.GetBypassDomains();
            TxtBypassIPs.Text = _storage.GetBypassIPs();
            ChkBypassLan.IsChecked = _storage.GetBypassLan();

            foreach (var name in ThemeManager.ThemeNames)
                CmbTheme.Items.Add(name);

            var savedLang2 = _storage.Get("language") ?? LocalizationManager.Languages[0];
            CmbLanguage.SelectedItem = savedLang2;
            LocalizationManager.Apply(savedLang2);

            var customPath = _storage.Get("theme_custom_path");
            if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
            {
                var name = $"📁 {System.IO.Path.GetFileNameWithoutExtension(customPath)}";
                CmbTheme.Items.Add(name);
                ThemeManager.ApplyFromFile(customPath);
                CmbTheme.SelectedItem = name;
            }
            else
            {
                var savedTheme = _storage.Get("theme") ?? ThemeManager.ThemeNames[0];
                CmbTheme.SelectedItem = savedTheme;
                ThemeManager.Apply(savedTheme);
            }

            foreach (var lang in LocalizationManager.Languages)
                CmbLanguage.Items.Add(lang);

            CmbLogFilter.Items.Add("🔵 All");
            CmbLogFilter.Items.Add("✅ Info");
            CmbLogFilter.Items.Add("⚠️ Warning");
            CmbLogFilter.Items.Add("❌ Error");
            CmbLogFilter.Items.Add("🐛 Debug");
            CmbLogFilter.Items.Add("🐻 BearNest");
            CmbLogFilter.SelectedIndex = 0;
        }

        // ── ТАЙМЕР СЕССИИ ────────────────────────────────────────────
        private void OnSessionTimerTick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _connectTime;

            string timeStr = elapsed.TotalDays >= 1
                ? $"{(int)elapsed.TotalDays}д {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            string startStr = _connectTime.ToString("dd.MM HH:mm");
            TimerText.Text = $"{timeStr}  (с {startStr})";
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloon("BearNest", "Приложение свёрнуто в трей");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Items.Clear();
            _allLogs.Clear();
        }

        private async void BtnLoadSub_Click(object sender, RoutedEventArgs e)
            => await LoadSubscriptionAsync();

        private async System.Threading.Tasks.Task LoadSubscriptionAsync()
        {
            BtnLoadSub.IsEnabled = false;
            BtnLoadSub.Content = new Emoji.Wpf.TextBlock { Text = LocalizationManager.Get("BtnLoad") };
            ServerList.Items.Clear();

            try
            {
                var url = SubUrlBox.Text.Trim();
                _storage.SetSubscriptionUrl(url);
                OnLog($"[BearNest] {LocalizationManager.Get("LogDownloading")}");

                int proxyPort = _core.IsRunning ? ProxyPort : 0;

                var (content, info) = await SubscriptionParser.DownloadWithInfoAsync(url, proxyPort);
                _subInfo = info;

                OnLog($"[BearNest] {LocalizationManager.Get("LogReceived")} {content.Length}");

                UpdateSubInfoPanel(info);

                _servers = SubscriptionParser.Parse(content);
                OnLog($"[BearNest] {LocalizationManager.Get("LogFound")} {_servers.Count}");

                foreach (var s in _servers)
                    ServerList.Items.Add(s);

                var lastServer = _storage.GetLastServer();
                if (!string.IsNullOrEmpty(lastServer))
                {
                    for (int i = 0; i < _servers.Count; i++)
                    {
                        if (_servers[i].Name == lastServer)
                        {
                            ServerList.SelectedIndex = i;
                            OnLog($"[BearNest] {LocalizationManager.Get("LogRestored")} {lastServer}");
                            break;
                        }
                    }
                }

                if (_servers.Count > 0)
                    OnLog($"[BearNest] {LocalizationManager.Get("LogHint")}");
            }
            catch (Exception ex)
            {
                OnLog($"[BearNest] Ошибка загрузки: {ex.Message}");
            }
            finally
            {
                BtnLoadSub.IsEnabled = true;
                BtnLoadSub.Content = new Emoji.Wpf.TextBlock { Text = LocalizationManager.Get("BtnLoad") };
            }
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count == 0) { OnLog("[BearNest] Сначала загрузи подписку"); return; }

            BtnPing.IsEnabled = false;
            BtnPing.Content = new Emoji.Wpf.TextBlock { Text = LocalizationManager.Get("BtnPing") };
            OnLog($"[BearNest] {LocalizationManager.Get("LogPinging")} {_servers.Count}");

            var pings = await ServerPinger.PingAllAsync(_servers);

            for (int i = 0; i < _servers.Count; i++)
                _servers[i].PingMs = pings[i] >= 0 ? pings[i] : -2;

            ServerList.Items.Clear();
            foreach (var s in _servers)
                ServerList.Items.Add(s);

            int bestIdx = ServerPinger.FindBest(pings);
            if (bestIdx >= 0)
                OnLog($"[BearNest] {LocalizationManager.Get("LogBest")} ...");

            if (_selectedServer != null)
            {
                var idx = _servers.IndexOf(_selectedServer);
                if (idx >= 0) ServerList.SelectedIndex = idx;
            }
            else if (bestIdx >= 0)
            {
                ServerList.SelectedIndex = bestIdx;
            }

            for (int i = 0; i < _servers.Count; i++)
                OnLog($"  {_servers[i].Name}: {_servers[i].PingDisplay}");

            BtnPing.IsEnabled = true;
            BtnPing.Content = LocalizationManager.Get("BtnPing");
        }

        private void ServerList_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ServerList.SelectedItem is ServerConfig s)
            {
                _selectedServer = s;
                SelectedServerText.Text =
                $"{LocalizationManager.Get("SelectedServer")} {s.Name} | {s.Protocol} | {s.Address}:{s.Port}";
                _storage.SetLastServer(s.Name);
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
            => await ConnectAsync();

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_selectedServer == null)
            {
                OnLog("[BearNest] Выбери сервер из списка");
                return;
            }

            var splitSettings = BuildSplitTunnelSettings();
            var json = ConfigGenerator.Generate(_selectedServer, splitSettings);

            System.IO.File.WriteAllText(ConfigPath, json);
            OnLog($"[BearNest] {LocalizationManager.Get("LogConnecting")} {_selectedServer.Name}");

            BtnStart.IsEnabled = false;
            await _core.StartAsync();

            await System.Threading.Tasks.Task.Delay(800);
            SystemProxy.Enable(ProxyHost, ProxyPort);
            OnLog($"[BearNest] {LocalizationManager.Get("LogProxyOn")} {ProxyHost}:{ProxyPort}");

            _connectTime = DateTime.Now;
            _sessionTimer.Start();

            StartWatchdog();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
            => await DisconnectAsync();

        private async System.Threading.Tasks.Task DisconnectAsync()
        {
            BtnStop.IsEnabled = false;
            StopWatchdog();

            _sessionTimer.Stop();
            TimerText.Text = "";
            TrafficText.Text = "";

            SystemProxy.Disable();
            OnLog($"[BearNest] {LocalizationManager.Get("LogProxyOff")}");
            await _core.StopAsync();
        }

        private void StartWatchdog()
        {
            StopWatchdog();
            _watchdogCts = new System.Threading.CancellationTokenSource();
            var ct = _watchdogCts.Token;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                int failCount = 0;
                while (!ct.IsCancellationRequested)
                {
                    await System.Threading.Tasks.Task.Delay(30_000, ct);
                    if (_selectedServer == null || ct.IsCancellationRequested) break;

                    var ms = await ServerPinger.PingAsync(_selectedServer.Address, _selectedServer.Port);
                    if (ms < 0)
                    {
                        failCount++;
                        Dispatcher.Invoke(() =>
                            OnLog($"[BearNest] ⚠️ Нет ответа ({failCount}/3): {_selectedServer.Name}"));
                        if (failCount >= 3)
                        {
                            Dispatcher.Invoke(() => OnLog("[BearNest] 🔄 Авто-переключение..."));
                            await SwitchToNextServerAsync();
                            failCount = 0;
                        }
                    }
                    else
                    {
                        failCount = 0;
                        if (_selectedServer != null)
                        {
                            _selectedServer.PingMs = ms;
                            Dispatcher.Invoke(() =>
                            {
                                var idx = ServerList.SelectedIndex;
                                if (idx >= 0)
                                {
                                    ServerList.Items.RemoveAt(idx);
                                    ServerList.Items.Insert(idx, _selectedServer);
                                    ServerList.SelectedIndex = idx;
                                }
                            });
                        }
                    }
                }
            }, ct);
        }

        private void StopWatchdog()
        {
            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();
            _watchdogCts = null;
        }

        private async System.Threading.Tasks.Task SwitchToNextServerAsync()
        {
            if (_servers.Count == 0) return;
            int currentIdx = _selectedServer == null ? -1 : _servers.IndexOf(_selectedServer);

            for (int i = 1; i <= _servers.Count; i++)
            {
                int nextIdx = (currentIdx + i) % _servers.Count;
                var nextServer = _servers[nextIdx];
                var ms = await ServerPinger.PingAsync(nextServer.Address, nextServer.Port);
                if (ms < 0) continue;

                Dispatcher.Invoke(async () =>
                {
                    _selectedServer = nextServer;
                    ServerList.SelectedIndex = nextIdx;
                    SelectedServerText.Text =
                        $"{LocalizationManager.Get("SelectedServer")} {nextServer.Name} | {nextServer.Protocol} | {nextServer.Address}:{nextServer.Port}";
                    OnLog($"[BearNest] {LocalizationManager.Get("SwitchedTo")} {nextServer.Name}");
                    _storage.SetLastServer(nextServer.Name);

                    var splitSettings = BuildSplitTunnelSettings();
                    var json = ConfigGenerator.Generate(nextServer, splitSettings);
                    System.IO.File.WriteAllText(ConfigPath, json);
                    await _core.StartAsync();
                });
                return;
            }
            Dispatcher.Invoke(() => OnLog("[BearNest] ❌ Нет доступных серверов"));
        }

        private void OnLog(string line)
        {
            _logger.Write(line);

            Dispatcher.Invoke(() =>
            {
                // Во время паузы — только буферизуем, UI не трогаем
                if (_logPaused)
                {
                    _pausedLogs.Add(line);
                    if (_pausedLogs.Count > 1000)
                        _pausedLogs.RemoveAt(0);
                    return;
                }

                var enriched = EnrichLogLine(line);

                _allLogs.Add(enriched);
                if (_allLogs.Count > 2000)
                    _allLogs.RemoveAt(0);

                if (MatchesFilter(enriched))
                {
                    LogBox.Items.Add(enriched);
                    LogBox.ScrollIntoView(LogBox.Items[^1]);
                    while (LogBox.Items.Count > 200)
                        LogBox.Items.RemoveAt(0);
                }
            });
        }

        // ── ПАУЗА ЛОГОВ ──────────────────────────────────────────────
        private void BtnPauseLog_Click(object sender, RoutedEventArgs e)
        {
            _logPaused = !_logPaused;

            if (_logPaused)
            {
                BtnPauseLog.Content = new Emoji.Wpf.TextBlock { Text = "▶ Продолжить" };
            }
            else
            {
                BtnPauseLog.Content = new Emoji.Wpf.TextBlock { Text = "⏸ Пауза" };
                _ = FlushPausedLogsAsync();
            }
        }

        private async Task FlushPausedLogsAsync()
        {
            var batch = _pausedLogs.ToList();
            _pausedLogs.Clear();

            const int chunkSize = 30;

            for (int i = 0; i < batch.Count; i += chunkSize)
            {
                var chunk = batch.Skip(i).Take(chunkSize).ToList();

                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var line in chunk)
                    {
                        var enriched = EnrichLogLine(line);

                        _allLogs.Add(enriched);
                        if (_allLogs.Count > 2000)
                            _allLogs.RemoveAt(0);

                        if (MatchesFilter(enriched))
                        {
                            while (LogBox.Items.Count >= 200)
                                LogBox.Items.RemoveAt(0);
                            LogBox.Items.Add(enriched);
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                await Task.Delay(16);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (LogBox.Items.Count > 0)
                    LogBox.ScrollIntoView(LogBox.Items[^1]);
            });
        }

        // ════════════════════════════════════════════════════════════
        // ЛОГГЕР — КОПИРОВАНИЕ ТЕКСТА
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Ctrl+C — копирует выделенные строки.
        /// Ctrl+A — выделяет все строки.
        /// </summary>
        private void LogBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.C &&
                System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                CopySelectedLogLines();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.A &&
                     System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                LogBox.SelectAll();
                e.Handled = true;
            }
        }

        /// <summary>Контекстное меню → Копировать выделенное.</summary>
        private void LogBox_CopySelected_Click(object sender, RoutedEventArgs e)
            => CopySelectedLogLines();

        /// <summary>Контекстное меню → Выделить всё.</summary>
        private void LogBox_SelectAll_Click(object sender, RoutedEventArgs e)
            => LogBox.SelectAll();

        /// <summary>
        /// Копирует выделенные строки лога в буфер обмена.
        /// Если ничего не выбрано — показывает подсказку.
        /// </summary>
        private void CopySelectedLogLines()
        {
            if (LogBox.SelectedItems.Count == 0)
            {
                ShowToast("ℹ️", "Выдели строки для копирования (Ctrl+A — всё)");
                return;
            }

            var lines = LogBox.SelectedItems
                .Cast<string>()
                .ToList();

            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShowToast("📋", $"Скопировано {lines.Count} строк");
        }

        // ── ЭМОДЗИ ДЛЯ ЧИТАЕМОСТИ ЛОГОВ ─────────────────────────────
        private static string EnrichLogLine(string line)
        {
            // BearNest строки уже имеют свои эмодзи — не трогаем
            if (line.StartsWith("[BearNest]")) return line;

            // Xray строки — добавляем префикс (оригинальный тег остаётся → фильтр работает)
            if (line.Contains("[Info]")) return "ℹ️ " + line;
            if (line.Contains("[Warning]")) return "⚠️ " + line;
            if (line.Contains("[Error]")) return "❌ " + line;
            if (line.Contains("[Debug]")) return "🐛 " + line;

            return line;
        }

        private bool MatchesFilter(string line)
        {
            return _logFilter switch
            {
                "✅ Info" => line.Contains("[Info]"),
                "⚠️ Warning" => line.Contains("[Warning]"),
                "❌ Error" => line.Contains("[Error]"),
                "🐛 Debug" => line.Contains("[Debug]"),
                "🐻 BearNest" => line.StartsWith("[BearNest]"),
                _ => true
            };
        }

        private void ApplyLogFilter()
        {
            LogBox.Items.Clear();
            foreach (var line in _allLogs)
            {
                if (MatchesFilter(line))
                    LogBox.Items.Add(line);
            }
            if (LogBox.Items.Count > 0)
                LogBox.ScrollIntoView(LogBox.Items[^1]);
        }

        private void CmbLogFilter_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _logFilter = CmbLogFilter.SelectedItem as string ?? "All";
            ApplyLogFilter();
        }

        private static string FormatLogLine(string line)
        {
            if (line.Contains("[Warning]") && line.Contains("connection ends"))
                return $"[~] соединение закрыто клиентом";
            if (line.Contains("[Warning]") && line.Contains("failed to write"))
                return $"[~] браузер закрыл соединение";
            if (line.Contains("[Info]") && line.Contains("Reading config"))
                return $"[✓] конфиг загружен";
            if (line.Contains("[Warning]") && line.Contains("started"))
                return $"[✓] xray ядро запущено";
            if (line.Contains("Xray") && line.Contains("windows/amd64"))
            {
                var ver = line.Split(' ').ElementAtOrDefault(1) ?? "";
                return $"[✓] xray v{ver}";
            }
            return line;
        }

        private System.Windows.Media.Brush GetLogColor(string line)
        {
            var res = System.Windows.Application.Current.Resources;

            if (line.StartsWith("[BearNest]"))
            {
                if (line.Contains("Подключаемся") || line.Contains("Прокси включён"))
                    return res["BrushConnected"] as System.Windows.Media.Brush
                        ?? System.Windows.Media.Brushes.LightGreen;
                if (line.Contains("Прокси выключен") || line.Contains("остановлен"))
                    return res["BrushDisconnected"] as System.Windows.Media.Brush
                        ?? System.Windows.Media.Brushes.Salmon;
                if (line.Contains("⚠️") || line.Contains("Нет ответа"))
                    return System.Windows.Media.Brushes.Orange;
                if (line.Contains("🔄") || line.Contains("Переключились"))
                    return res["BrushAccent"] as System.Windows.Media.Brush
                        ?? System.Windows.Media.Brushes.CornflowerBlue;
                if (line.Contains("❌"))
                    return res["BrushDisconnected"] as System.Windows.Media.Brush
                        ?? System.Windows.Media.Brushes.Salmon;
                return res["BrushText"] as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.White;
            }

            if (line.Contains("[Warning]") && line.Contains("connection ends"))
                return res["BrushTextMuted"] as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.Gray;
            if (line.Contains("[Warning]") && line.Contains("started"))
                return res["BrushConnected"] as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.LightGreen;
            if (line.Contains("[Info]"))
                return res["BrushAccent"] as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.CornflowerBlue;
            if (line.Contains("Xray") && line.Contains("windows"))
                return res["BrushConnected"] as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.LightGreen;

            return res["BrushTextMuted"] as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
        }

        public void CleanupOnExit()
        {
            StopWatchdog();
            SystemProxy.Disable();
            _core.KillImmediate();
            _logger.Dispose();
            _storage.Dispose();
            _tray.Dispose();
            _core.Dispose();
        }

        private void OnStatusChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                _tray.SetConnected(isRunning);

                if (isRunning)
                {
                    BtnSwitch.IsEnabled = _servers.Count > 1;
                    StatusText.Text = LocalizationManager.Get("StatusConnected");
                    StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    BtnStart.IsEnabled = false;
                    BtnStop.IsEnabled = true;
                    _tray.ShowBalloon("BearNest", $"Подключён: {_selectedServer?.Name}");
                }
                else
                {
                    BtnSwitch.IsEnabled = false;
                    StatusText.Text = LocalizationManager.Get("StatusStopped");
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(243, 139, 168));
                    BtnStart.IsEnabled = true;
                    BtnStop.IsEnabled = false;
                    _sessionTimer.Stop();
                    TimerText.Text = "";
                    TrafficText.Text = "";

                    if (!_isSwitching)
                        SystemProxy.Disable();

                    OnLog($"[BearNest] {LocalizationManager.Get("LogStopped")}");
                }
            });
        }

        // ── НАСТРОЙКИ ────────────────────────────────────────────────

        private void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            AutoStart.Enable();
            OnLog($"[BearNest] {LocalizationManager.Get("LogAutoStart")}");
        }

        private void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            AutoStart.Disable();
            OnLog($"[BearNest] {LocalizationManager.Get("LogAutoStartOff")}");
        }

        private void BtnOpenLogs_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("explorer.exe", RollingLogger.LogDir);
        }

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "bearnest_backup",
                DefaultExt = ".json",
                Filter = "JSON файл|*.json"
            };

            if (dlg.ShowDialog() != true) return;

            var data = new
            {
                sub_url = _storage.GetSubscriptionUrl(),
                last_server = _storage.GetLastServer(),
                auto_connect = _storage.GetAutoConnect(),
                xray_path = TxtXrayPath.Text
            };

            var json = System.Text.Json.JsonSerializer.Serialize(data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(dlg.FileName, json);

            OnLog($"[BearNest] Настройки экспортированы: {dlg.FileName}");
            System.Windows.MessageBox.Show("Настройки сохранены!", "BearNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".json",
                Filter = "JSON файл|*.json"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("sub_url", out var subUrl))
                {
                    _storage.SetSubscriptionUrl(subUrl.GetString() ?? "");
                    SubUrlBox.Text = subUrl.GetString() ?? "";
                }
                if (root.TryGetProperty("last_server", out var lastServer))
                    _storage.SetLastServer(lastServer.GetString() ?? "");
                if (root.TryGetProperty("xray_path", out var xrayPath))
                    TxtXrayPath.Text = xrayPath.GetString() ?? "";

                OnLog($"[BearNest] Настройки импортированы: {dlg.FileName}");
                System.Windows.MessageBox.Show("Настройки загружены! Перезапустите приложение.", "BearNest",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                OnLog($"[BearNest] Ошибка импорта: {ex.Message}");
                System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "BearNest",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveXrayPath_Click(object sender, RoutedEventArgs e)
        {
            _storage.Set("xray_path", TxtXrayPath.Text);
            OnLog($"[BearNest] Путь к xray сохранён: {TxtXrayPath.Text}");
            System.Windows.MessageBox.Show("Сохранено! Перезапустите приложение.", "BearNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CmbTheme_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbTheme.SelectedItem is string theme)
            {
                ThemeManager.Apply(theme);
                _storage.Set("theme", theme);
                OnLog($"[BearNest] {LocalizationManager.Get("LogThemeChanged")} {theme}");
            }
        }

        private void BtnThemeEditor_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ThemeEditorWindow { Owner = this };
            editor.ShowDialog();
        }

        private void BtnLoadTheme_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".xaml",
                Filter = "XAML тема|*.xaml",
                InitialDirectory = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Themes")
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                ThemeManager.ApplyFromFile(dlg.FileName);
                _storage.Set("theme_custom_path", dlg.FileName);
                _storage.Set("theme", "Custom");

                var name = $"📁 {System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)}";
                if (!CmbTheme.Items.Contains(name))
                    CmbTheme.Items.Add(name);
                CmbTheme.SelectedItem = name;

                OnLog($"[BearNest] {LocalizationManager.Get("LogThemeLoaded")} {dlg.FileName}");
            }
            catch (Exception ex)
            {
                OnLog($"[BearNest] Ошибка загрузки темы: {ex.Message}");
            }
        }

        private void UpdateSubInfoPanel(SubscriptionInfo info)
        {
            if (info.IsEmpty)
            {
                SubInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }

            SubInfoPanel.Visibility = Visibility.Visible;
            SubTitleText.Text = string.IsNullOrEmpty(info.Title) ? "Подписка активна" : info.Title;
            SubTrafficText.Text = info.TrafficUsed;
            SubExpireText.Text = info.ExpireText;

            SubExpireText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    info.StatusColor));
        }

        private void CmbLanguage_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbLanguage.SelectedItem is string lang)
            {
                LocalizationManager.Apply(lang);
                _storage.Set("language", lang);

                if (!_core.IsRunning)
                    StatusText.Text = LocalizationManager.Get("StatusStopped");
            }
        }

        private void TitleBar_MouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Hide();

        private async void BtnSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count == 0) return;

            BtnSwitch.IsEnabled = false;
            BtnSwitch.Content = new Emoji.Wpf.TextBlock { Text = LocalizationManager.Get("BtnSwitch") };

            int currentIdx = _selectedServer == null ? -1
                : _servers.IndexOf(_selectedServer);

            for (int i = 1; i <= _servers.Count; i++)
            {
                int nextIdx = (currentIdx + i) % _servers.Count;
                var candidate = _servers[nextIdx];
                var ms = await ServerPinger.PingAsync(candidate.Address, candidate.Port);
                if (ms < 0) continue;

                await SwitchToServerDirectAsync(candidate, nextIdx);
                BtnSwitch.IsEnabled = true;
                BtnSwitch.Content = LocalizationManager.Get("BtnSwitch");
                return;
            }

            ShowToast("❌", LocalizationManager.Get("LogNoServers"));
            BtnSwitch.IsEnabled = true;
            BtnSwitch.Content = LocalizationManager.Get("BtnSwitch");
        }

        private async System.Threading.Tasks.Task SwitchToServerDirectAsync(
            ServerConfig server, int idx)
        {
            _isSwitching = true;
            OnLog($"[BearNest] ⇄ {LocalizationManager.Get("LogSwitchedTo")} {server.Name}");

            StopWatchdog();

            var splitSettings = BuildSplitTunnelSettings();
            var json = ConfigGenerator.Generate(server, splitSettings);
            System.IO.File.WriteAllText(ConfigPath, json);

            await _core.StopAsync();
            await System.Threading.Tasks.Task.Delay(300);
            await _core.StartAsync();
            _isSwitching = false;

            _selectedServer = server;
            Dispatcher.Invoke(() =>
            {
                ServerList.SelectedIndex = idx;
                SelectedServerText.Text =
                    $"{server.Name} | {server.Protocol} | {server.Address}:{server.Port}";
            });

            _storage.SetLastServer(server.Name);
            StartWatchdog();

            ShowToast("✅", $"{LocalizationManager.Get("LogSwitchedTo")} {server.Name}");
        }

        private System.Threading.CancellationTokenSource? _toastCts;

        private void ShowToast(string icon, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _toastCts?.Cancel();
                _toastCts = new System.Threading.CancellationTokenSource();

                ToastIcon.Text = icon;
                ToastText.Text = message;

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                var slideIn = new System.Windows.Media.Animation.DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(200));

                ToastPanel.BeginAnimation(OpacityProperty, fadeIn);
                ToastTranslate.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty, slideIn);

                var ct = _toastCts.Token;
                _ = System.Threading.Tasks.Task.Delay(3000, ct).ContinueWith(_ =>
                {
                    if (ct.IsCancellationRequested) return;
                    Dispatcher.Invoke(() =>
                    {
                        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(300));
                        ToastPanel.BeginAnimation(OpacityProperty, fadeOut);
                        ToastTranslate.BeginAnimation(
                            System.Windows.Media.TranslateTransform.YProperty, slideOut);
                    });
                }, ct);
            });
        }

        // ════════════════════════════════════════════════════════════
        // BYPASS TAB — БЫСТРОЕ ДОБАВЛЕНИЕ ДОМЕНА
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Кнопка "➕ Add" — добавляет домен из поля TxtQuickDomain.
        /// Поддерживает: просто домен (google.com), полный URL (https://google.com/path).
        /// После успешного добавления очищает поле ввода.
        /// </summary>
        private void BtnAddDomainManual_Click(object sender, RoutedEventArgs e)
        {
            var input = TxtQuickDomain.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                ShowToast("⚠️", "Введи домен или URL");
                return;
            }

            if (AddDomainToBypass(input))
                TxtQuickDomain.Text = "";
        }

        /// <summary>
        /// Кнопка "📋 Add from clipboard" — берёт текст из буфера обмена,
        /// парсит домен и добавляет в список.
        /// Удобно: скопировал URL в браузере (Ctrl+L → Ctrl+C) → нажал кнопку.
        /// </summary>
        private void BtnAddFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            string text;
            try
            {
                text = System.Windows.Clipboard.GetText()?.Trim() ?? string.Empty;
            }
            catch
            {
                ShowToast("⚠️", "Не удалось прочитать буфер обмена");
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                ShowToast("⚠️", "Буфер обмена пуст");
                return;
            }

            AddDomainToBypass(text);
        }

        /// <summary>
        /// Общая логика добавления домена в TxtBypassDomains.
        /// Возвращает true если домен успешно добавлен, false — если ошибка или дубликат.
        /// </summary>
        private bool AddDomainToBypass(string input)
        {
            var domain = ExtractDomain(input);

            if (string.IsNullOrEmpty(domain))
            {
                ShowToast("⚠️", $"Не удалось распознать домен: {input}");
                OnLog($"[BearNest] ⚠️ Bypass: не распознан домен из «{input}»");
                return false;
            }

            // Проверяем дубликат
            var existing = (TxtBypassDomains.Text ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            if (existing.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase)))
            {
                ShowToast("ℹ️", $"{domain} уже есть в списке");
                return false;
            }

            // Добавляем строку
            if (string.IsNullOrWhiteSpace(TxtBypassDomains.Text))
                TxtBypassDomains.Text = domain;
            else
                TxtBypassDomains.Text = TxtBypassDomains.Text.TrimEnd('\r', '\n') + "\n" + domain;

            OnLog($"[BearNest] 📋 Bypass: добавлен домен «{domain}»");
            ShowToast("✅", $"Добавлен: {domain}");
            return true;
        }

        /// <summary>
        /// Извлекает хост из строки. Принимает:
        ///   • полный URL:      https://www.youtube.com/watch?v=...  → youtube.com
        ///   • URL без схемы:   www.youtube.com/watch?v=...          → youtube.com
        ///   • просто домен:    youtube.com                          → youtube.com
        ///   • домен + порт:    192.168.1.1:8080                     → 192.168.1.1:8080 (оставляем как есть)
        ///
        /// Возвращает пустую строку если парсинг не удался.
        /// </summary>
        private static string ExtractDomain(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = input.Trim();

            // 1. Полный URL со схемой
            if (input.Contains("://"))
            {
                if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host.ToLowerInvariant();
                    // Убираем www. — xray лучше матчит без него
                    return host.StartsWith("www.") ? host[4..] : host;
                }
                return string.Empty;
            }

            // 2. URL без схемы — пробуем добавить https://
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out var uri2))
            {
                var host = uri2.Host.ToLowerInvariant();
                // Должен содержать точку (чтобы не принять "localhost" или мусор)
                if (host.Contains('.'))
                    return host.StartsWith("www.") ? host[4..] : host;
            }

            return string.Empty;
        }

        // ════════════════════════════════════════════════════════════
        // СПЛИТ-ТОННЕЛИНГ — методы сохранения / применения
        // ════════════════════════════════════════════════════════════

        /// <summary>Собирает SplitTunnelSettings из текущего состояния UI.</summary>
        private SplitTunnelSettings BuildSplitTunnelSettings()
        {
            var domains = (TxtBypassDomains.Text ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            var ips = (TxtBypassIPs.Text ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .ToList();

            return new SplitTunnelSettings
            {
                BypassDomains = domains,
                BypassIPs = ips,
                BypassLan = ChkBypassLan.IsChecked == true
            };
        }

        /// <summary>Кнопка "💾 Save rules" в Bypass таб.</summary>
        private async void BtnSaveSplitTunnel_Click(object sender, RoutedEventArgs e)
        {
            // 1. Сохраняем настройки в storage
            _storage.SetBypassDomains(TxtBypassDomains.Text ?? "");
            _storage.SetBypassIPs(TxtBypassIPs.Text ?? "");
            _storage.SetBypassLan(ChkBypassLan.IsChecked == true);

            var settings = BuildSplitTunnelSettings();
            int domainCount = settings.BypassDomains.Count;
            int ipCount = settings.BypassIPs.Count;
            string lanStatus = settings.BypassLan ? ", LAN bypass ON" : "";

            OnLog($"[BearNest] 🔀 Split tunneling: {domainCount} доменов, {ipCount} IP{lanStatus}");

            // 2. Если VPN не запущен — просто сообщаем о сохранении
            if (!_core.IsRunning || _selectedServer == null)
            {
                TxtSplitStatus.Text = $"✅ Сохранено: {domainCount} доменов, {ipCount} IP{lanStatus}";
                ShowToast("💾", $"Сохранено ({domainCount}д + {ipCount}IP). Применится при подключении.");
                return;
            }

            // 3. VPN активен — пересоздаём конфиг и перезапускаем xray
            var btn = (System.Windows.Controls.Button)sender;
            btn.IsEnabled = false;
            TxtSplitStatus.Text = "⏳ Применяю...";
            OnLog("[BearNest] 🔄 Перезапуск xray с новыми правилами split tunneling...");

            try
            {
                var json = ConfigGenerator.Generate(_selectedServer, settings);
                System.IO.File.WriteAllText(ConfigPath, json);

                // _isSwitching = true запрещает OnStatusChanged(false)
                // вызвать SystemProxy.Disable() в момент остановки
                _isSwitching = true;

                StopWatchdog();
                await _core.StopAsync();
                await Task.Delay(300);
                await _core.StartAsync();

                _isSwitching = false;
                StartWatchdog();

                TxtSplitStatus.Text = $"✅ Применено: {domainCount} доменов, {ipCount} IP{lanStatus}";
                ShowToast("✅", $"Split tunneling применён ({domainCount}д + {ipCount}IP)");
            }
            catch (Exception ex)
            {
                _isSwitching = false;
                TxtSplitStatus.Text = $"❌ Ошибка: {ex.Message}";
                OnLog($"[BearNest] ❌ Ошибка перезапуска после split tunneling: {ex.Message}");
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        /// <summary>Кнопка "🗑 Delete all rules" в Bypass таб.</summary>
        private void BtnClearSplitTunnel_Click(object sender, RoutedEventArgs e)
        {
            TxtBypassDomains.Text = "";
            TxtBypassIPs.Text = "";
            ChkBypassLan.IsChecked = true;

            _storage.SetBypassDomains("");
            _storage.SetBypassIPs("");
            _storage.SetBypassLan(true);

            TxtSplitStatus.Text = "🗑 Правила очищены";
            OnLog("[BearNest] 🔀 Split tunneling: правила очищены");
        }
    }

    public static class VisualTreeHelperExtensions
    {
        public static T? FindVisualChild<T>(this System.Windows.DependencyObject parent)
            where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
