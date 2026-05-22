using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace VpnClient
{
    public partial class MainWindow : Window
    {
        private readonly CoreManager _core;
        private readonly AppStorage _storage;
        private readonly TrayManager _tray;
        private List<ServerConfig> _servers = new();
        private ServerConfig? _selectedServer;
        private SubscriptionInfo? _subInfo;

        private System.Threading.CancellationTokenSource? _watchdogCts;
         
        // Счётчик времени сессии
        private readonly DispatcherTimer _sessionTimer = new();
        private readonly RollingLogger _logger = new();
        // Все строки лога для фильтрации
        private readonly List<string> _allLogs = new();
        private string _logFilter = "All";
        private DateTime _connectTime;

        // Счётчик трафика
        private long _bytesSent = 0;
        private long _bytesReceived = 0;

        // Пути относительно папки с exe — работают в любом месте
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

            // Таймер сессии — обновляет каждую секунду
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
            };            // Инициализируем вкладку настроек
            ChkAutoStart.Checked   -= ChkAutoStart_Checked;
            ChkAutoStart.Unchecked -= ChkAutoStart_Unchecked;
            ChkAutoStart.IsChecked  = AutoStart.IsEnabled();
            ChkAutoStart.Checked   += ChkAutoStart_Checked;
            ChkAutoStart.Unchecked += ChkAutoStart_Unchecked;
            TxtXrayPath.Text = _storage.Get("xray_path") ?? XrayPath;

            // Инициализируем список тем
            foreach (var name in ThemeManager.ThemeNames)
                CmbTheme.Items.Add(name); 
                var savedLang = _storage.Get("language") ?? LocalizationManager.Languages[0];
                CmbLanguage.SelectedItem = savedLang;
                LocalizationManager.Apply(savedLang);

          // Восстанавливаем кастомную тему если была
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
            // Инициализируем список языков
            foreach (var lang in LocalizationManager.Languages)
                CmbLanguage.Items.Add(lang);

      
                // Инициализируем фильтр логов
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
            TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
        }

        // ── ПОКАЗАТЬ ОКНО ────────────────────────────────────────────
        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // ── СКРЫТЬ В ТРЕЙ ────────────────────────────────────────────
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloon("BearNest", "Приложение свёрнуто в трей");
        }

        // ── ОЧИСТИТЬ ЛОГ ─────────────────────────────────────────────
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Items.Clear();
            _allLogs.Clear();
        }

        // ── ЗАГРУЗИТЬ ПОДПИСКУ ───────────────────────────────────────
        private async void BtnLoadSub_Click(object sender, RoutedEventArgs e)
            => await LoadSubscriptionAsync();

        private async System.Threading.Tasks.Task LoadSubscriptionAsync()
        {
            BtnLoadSub.IsEnabled = false;
            BtnLoadSub.Content = "⏳ Загрузка...";
            ServerList.Items.Clear();

            try
            {
                var url = SubUrlBox.Text.Trim();
                _storage.SetSubscriptionUrl(url);
               OnLog($"[BearNest] {LocalizationManager.Get("LogDownloading")}");


                int proxyPort = _core.IsRunning ? ProxyPort : 0;

                // Используем новый метод с инфо
                var (content, info) = await SubscriptionParser.DownloadWithInfoAsync(url, proxyPort);
                _subInfo = info;

              OnLog($"[BearNest] {LocalizationManager.Get("LogReceived")} {content.Length}");


                // Обновляем панель статуса подписки
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
                    BtnLoadSub.Content = LocalizationManager.Get("BtnLoad");

            }
        }

        // ── ТЕСТ ПИНГА ───────────────────────────────────────────────
        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count == 0) { OnLog("[BearNest] Сначала загрузи подписку"); return; }

            BtnPing.IsEnabled = false;
            BtnPing.Content = "⏳ Пингуем...";
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

        // ── ВЫБОР СЕРВЕРА ────────────────────────────────────────────
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

        // ── ПОДКЛЮЧИТЬ ───────────────────────────────────────────────
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
            => await ConnectAsync();

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_selectedServer == null)
            {
                OnLog("[BearNest] Выбери сервер из списка");
                return;
            }

            var json = ConfigGenerator.Generate(_selectedServer);
            System.IO.File.WriteAllText(ConfigPath, json);
            OnLog($"[BearNest] {LocalizationManager.Get("LogConnecting")} {_selectedServer.Name}");


            BtnStart.IsEnabled = false;
            await _core.StartAsync();

            await System.Threading.Tasks.Task.Delay(800);
            SystemProxy.Enable(ProxyHost, ProxyPort);
            OnLog($"[BearNest] {LocalizationManager.Get("LogProxyOn")} {ProxyHost}:{ProxyPort}");


            // Запускаем таймер сессии
            _connectTime = DateTime.Now;
            _sessionTimer.Start();

            StartWatchdog();
        }

        // ── ОТКЛЮЧИТЬ ────────────────────────────────────────────────
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

        // ── WATCHDOG ─────────────────────────────────────────────────
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
                    var json = ConfigGenerator.Generate(nextServer);
                    System.IO.File.WriteAllText(ConfigPath, json);
                    await _core.StartAsync();
                });
                return;
            }
            Dispatcher.Invoke(() => OnLog("[BearNest] ❌ Нет доступных серверов"));
        }

        // ── ЛОГ ──────────────────────────────────────────────────────
        private void OnLog(string line)
        {
            _logger.Write(line);

            Dispatcher.Invoke(() =>
            {
                // Сохраняем все логи
                _allLogs.Add(line);
                if (_allLogs.Count > 2000)
                    _allLogs.RemoveAt(0);

                // Показываем только если проходит фильтр
                if (MatchesFilter(line))
                {
                    LogBox.Items.Add(line);
                    LogBox.ScrollIntoView(LogBox.Items[^1]);
                    while (LogBox.Items.Count > 500)
                        LogBox.Items.RemoveAt(0);
                }
            });
        }

        private bool MatchesFilter(string line)
        {
            return _logFilter switch
            {
                "✅ Info"       => line.Contains("[Info]"),
                "⚠️ Warning"   => line.Contains("[Warning]"),
                "❌ Error"      => line.Contains("[Error]"),
                "🐛 Debug"      => line.Contains("[Debug]"),
                "🐻 BearNest"   => line.StartsWith("[BearNest]"),
                _               => true // All
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
        // ── ФОРМАТИРОВАТЬ СТРОКУ ЛОГА ────────────────────────────
        private static string FormatLogLine(string line)
        {
            // xray Warning → коротко
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
                // "Xray 26.5.9 (Xray, Penetrates Everything.)"
                var ver = line.Split(' ').ElementAtOrDefault(1) ?? "";
                return $"[✓] xray v{ver}";
            }

            return line;
        }

        // ── ЦВЕТ СТРОКИ ЛОГА ────────────────────────────────────
        private System.Windows.Media.Brush GetLogColor(string line)
        {
            var res = System.Windows.Application.Current.Resources;

            // BearNest сообщения
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

            // xray строки
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

            // Дефолт — приглушённый
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

        // ── СТАТУС ───────────────────────────────────────────────────
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

                    // Не отключаем прокси, если идёт переключение серверов
                    if (!_isSwitching)
                    {
                        SystemProxy.Disable();
                    }

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
            System.Windows.MessageBox.Show("Настройки сохранены!", "BearNest", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "BearNest", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveXrayPath_Click(object sender, RoutedEventArgs e)
        {
            _storage.Set("xray_path", TxtXrayPath.Text);
            OnLog($"[BearNest] Путь к xray сохранён: {TxtXrayPath.Text}");
            System.Windows.MessageBox.Show("Сохранено! Перезапустите приложение.", "BearNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            Filter     = "XAML тема|*.xaml",
            InitialDirectory = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Themes")
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            ThemeManager.ApplyFromFile(dlg.FileName);
            _storage.Set("theme_custom_path", dlg.FileName);
            _storage.Set("theme", "Custom");

        // Добавляем в ComboBox если ещё нет
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

            // Цвет срока в зависимости от оставшихся дней
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

                // Обновляем статус текст — он не биндится автоматически
                // если был изменён программно
                if (!_core.IsRunning)
                    StatusText.Text = LocalizationManager.Get("StatusStopped");
            }
        }
        // ── КАСТОМНЫЙ ЗАГОЛОВОК ──────────────────────────────────
 
 
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


        // ── КНОПКА ПЕРЕКЛЮЧИТЬ ───────────────────────────────────
        private async void BtnSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count == 0) return;

            BtnSwitch.IsEnabled = false;
            BtnSwitch.Content   = "⏳...";

            int currentIdx = _selectedServer == null ? -1
                : _servers.IndexOf(_selectedServer);

            for (int i = 1; i <= _servers.Count; i++)
            {
                int nextIdx    = (currentIdx + i) % _servers.Count;
                var candidate  = _servers[nextIdx];
               var ms = await ServerPinger.PingAsync(candidate.Address, candidate.Port);
                if (ms < 0) continue;

                await SwitchToServerDirectAsync(candidate, nextIdx);
                BtnSwitch.IsEnabled = true;
                BtnSwitch.Content   = LocalizationManager.Get("BtnSwitch");
                return;
            }

            ShowToast("❌", LocalizationManager.Get("LogNoServers"));
            BtnSwitch.IsEnabled = true;
            BtnSwitch.Content   = LocalizationManager.Get("BtnSwitch");
        }

        // ── ПЕРЕКЛЮЧИТЬ БЕЗ ОТКЛЮЧЕНИЯ ───────────────────────────
        private async System.Threading.Tasks.Task SwitchToServerDirectAsync(
            ServerConfig server, int idx)
        {
         _isSwitching = true;  // ← включаем флаг
            OnLog($"[BearNest] ⇄ {LocalizationManager.Get("LogSwitchedTo")} {server.Name}");

            StopWatchdog();

            var json = ConfigGenerator.Generate(server);
            System.IO.File.WriteAllText(ConfigPath, json);

            await _core.StopAsync();
            await System.Threading.Tasks.Task.Delay(300);
            await _core.StartAsync();
            _isSwitching = false;  // ← выключаем флаг

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

        // ── КАСТОМНОЕ УВЕДОМЛЕНИЕ ────────────────────────────────
        private System.Threading.CancellationTokenSource? _toastCts;

        private void ShowToast(string icon, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _toastCts?.Cancel();
                _toastCts = new System.Threading.CancellationTokenSource();

                ToastIcon.Text = icon;
                ToastText.Text = message;

                // Анимация появления
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(200));
                var slideIn = new System.Windows.Media.Animation.DoubleAnimation(30, 0,
                    TimeSpan.FromMilliseconds(200));

                ToastPanel.BeginAnimation(OpacityProperty, fadeIn);
                ToastTranslate.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty, slideIn);

                // Автоскрытие через 3 секунды
                var ct = _toastCts.Token;
                _ = System.Threading.Tasks.Task.Delay(3000, ct).ContinueWith(_ =>
                {
                    if (ct.IsCancellationRequested) return;
                    Dispatcher.Invoke(() =>
                    {
                        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                            TimeSpan.FromMilliseconds(300));
                        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, 30,
                            TimeSpan.FromMilliseconds(300));
                        ToastPanel.BeginAnimation(OpacityProperty, fadeOut);
                        ToastTranslate.BeginAnimation(
                             System.Windows.Media.TranslateTransform.YProperty, slideOut);
                    });
                }, ct);
            });
        }

    }
}