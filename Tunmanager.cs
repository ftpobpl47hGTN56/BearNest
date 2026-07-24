using System;
using System.Collections.Generic;
using System.Linq;
using System.Text; 
using System.Diagnostics; 
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace VpnClient
{
    /// <summary>
    /// Управляет TUN-режимом: поднимает виртуальный адаптер через tun2socks,
    /// заворачивает ВЕСЬ трафик системы (включая UDP) в SOCKS5 прокси xray.
    ///
    /// Зачем нужен: системный прокси (SystemProxy.Enable) перехватывает только
    /// HTTP/HTTPS-трафик приложений, читающих настройки WinINet. Игры, P2P и
    /// любой UDP идут напрямую в сеть, минуя VPN. TUN решает это на уровне
    /// сетевого стека — приложения даже не знают, что трафик заворачивается.
    ///
    /// Требует прав администратора (создание адаптера + правка таблицы маршрутов).
    /// </summary>
    public class TunManager : IDisposable
    {
        // ── Параметры виртуального адаптера ──────────────────────────
        private const string TunName = "BearNestTun";
        private const string TunIp = "10.255.0.2";
        private const string TunGateway = "10.255.0.1";
        private const string TunMask = "255.255.255.0";

        private readonly string _tun2socksPath;
        private Process? _proc;

        /// <summary>IP VPN-сервера, для которого создан прямой маршрут. Нужен для отката.</summary>
        private string? _serverIp;

        /// <summary>Шлюз по умолчанию, сохранённый до подмены маршрутов.</summary>
        private string? _originalGateway;

        public bool IsRunning => _proc is { HasExited: false };

        /// <summary>Событие для проброса логов tun2socks в UI.</summary>
        public event Action<string>? LogReceived;

        public TunManager(string tun2socksPath) => _tun2socksPath = tun2socksPath;

        // ═════════════════════════════════════════════════════════════
        // ЗАПУСК
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Поднимает TUN-адаптер и перенаправляет весь трафик в SOCKS5.
        /// </summary>
        /// <param name="serverAddress">Домен или IP VPN-сервера (из ServerConfig.Address)</param>
        /// <param name="socksPort">Порт локального SOCKS5 инбаунда xray (19808)</param>
        public async Task StartAsync(string serverAddress, int socksPort)
        {
            if (!System.IO.File.Exists(_tun2socksPath))
                throw new System.IO.FileNotFoundException(
                    $"tun2socks.exe не найден: {_tun2socksPath}");

            // ── 1. Резолвим адрес сервера в IP ────────────────────────
            // В конфиг xray попадает домен (se.skywhirl.store), но маршрут
            // в таблице маршрутизации Windows строится только по IP.
            _serverIp = await ResolveToIpAsync(serverAddress);
            Log($"🌐 VPN-сервер: {serverAddress} → {_serverIp}");

            // ── 2. Запоминаем текущий шлюз ДО подмены маршрутов ───────
            _originalGateway = GetDefaultGateway();
            if (string.IsNullOrEmpty(_originalGateway))
                throw new InvalidOperationException(
                    "Не удалось определить шлюз по умолчанию");
            Log($"🌐 Шлюз по умолчанию: {_originalGateway}");

            // ── 3. Прямой маршрут к VPN-серверу ───────────────────────
            // КРИТИЧНО: делаем ДО поднятия TUN. Иначе xray попытается
            // достучаться до своего сервера через TUN → петля → всё висит.
            await RunCmdAsync(
                $"route add {_serverIp} mask 255.255.255.255 {_originalGateway} metric 1");
            Log($"🌐 Прямой маршрут к серверу: {_serverIp} → {_originalGateway}");

            // ── 4. Запускаем tun2socks ────────────────────────────────
            var psi = new ProcessStartInfo
            {
                FileName = _tun2socksPath,
                // ВАЖНО: длинные флаги требуют ДВУХ дефисов.
                // С одним дефисом pflag трактует их как набор коротких
                // флагов (-l -o -g ...) и падает с "unknown shorthand flag".
                Arguments = $"--device tun://{TunName} " +
                                $"--proxy socks5://127.0.0.1:{socksPort} " +
                                $"--udp-timeout 300s " +
                                $"--loglevel warning",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(_tun2socksPath)!
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[tun2socks] {e.Data}");
            };
            _proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[tun2socks] {e.Data}");
            };

            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            Log("🌐 tun2socks запущен, жду создания адаптера...");

            // ── 5. Ждём появления адаптера в системе ──────────────────
            if (!await WaitForAdapterAsync(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    $"Адаптер {TunName} не появился за 10 сек. " +
                    "Проверь, что wintun.dll лежит рядом с tun2socks.exe " +
                    "и приложение запущено от администратора.");
            Log($"🌐 Адаптер {TunName} создан");

            // ── 6. Назначаем IP адаптеру ──────────────────────────────
            await RunProcessAsync("netsh.exe",
                $"interface ip set address name=\"{TunName}\" " +
                $"static {TunIp} {TunMask} {TunGateway}");

            // DNS на адаптере — иначе резолв утечёт мимо туннеля
            await RunProcessAsync("netsh.exe",
                $"interface ip set dns name=\"{TunName}\" static 1.1.1.1");
            await RunProcessAsync("netsh.exe",
                $"interface ip add dns name=\"{TunName}\" 8.8.8.8 index=2");
            Log($"🌐 IP {TunIp} и DNS назначены адаптеру");

            // ── 7. Заворачиваем весь трафик в TUN ─────────────────────
            // Две /1 сети вместо одной 0.0.0.0/0: они покрывают всё адресное
            // пространство, но имеют более специфичную маску, поэтому
            // выигрывают у дефолтного маршрута без его удаления.
            // Плюс: при падении приложения дефолт остаётся цел.
            await RunCmdAsync($"route add 0.0.0.0 mask 128.0.0.0 {TunGateway} metric 1");
            await RunCmdAsync($"route add 128.0.0.0 mask 128.0.0.0 {TunGateway} metric 1");
            Log("🌐 TUN-режим активен — весь трафик через туннель");
        }

        // ═════════════════════════════════════════════════════════════
        // ОСТАНОВКА
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Снимает маршруты и гасит tun2socks. Вызывать ОБЯЗАТЕЛЬНО,
        /// иначе после закрытия приложения интернет пропадёт —
        /// маршруты останутся указывать на несуществующий адаптер.
        /// </summary>
        public async Task StopAsync()
        {
            // Порядок обратный запуску: сначала маршруты, потом процесс
            await RunCmdAsync("route delete 0.0.0.0 mask 128.0.0.0");
            await RunCmdAsync("route delete 128.0.0.0 mask 128.0.0.0");

            if (!string.IsNullOrEmpty(_serverIp))
                await RunCmdAsync($"route delete {_serverIp} mask 255.255.255.255");

            Log("🌐 Маршруты TUN сняты");

            if (_proc is { HasExited: false })
            {
                try
                {
                    _proc.Kill(entireProcessTree: true);
                    await _proc.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Ошибка остановки tun2socks: {ex.Message}");
                }
            }

            _proc?.Dispose();
            _proc = null;
            _serverIp = null;
            Log("🌐 TUN-режим выключен");
        }

        /// <summary>
        /// Синхронная аварийная очистка для CleanupOnExit.
        /// Маршруты снимаются в любом случае, даже при краше приложения.
        /// </summary>
        public void CleanupImmediate()
        {
            RunCmdSync("route delete 0.0.0.0 mask 128.0.0.0");
            RunCmdSync("route delete 128.0.0.0 mask 128.0.0.0");

            if (!string.IsNullOrEmpty(_serverIp))
                RunCmdSync($"route delete {_serverIp} mask 255.255.255.255");

            try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
            _proc?.Dispose();
            _proc = null;
        }

        // ═════════════════════════════════════════════════════════════
        // ВСПОМОГАТЕЛЬНОЕ
        // ═════════════════════════════════════════════════════════════

        /// <summary>Резолвит домен в IPv4. Если пришёл готовый IP — возвращает как есть.</summary>
        private static async Task<string> ResolveToIpAsync(string address)
        {
            if (IPAddress.TryParse(address, out var parsed))
                return parsed.ToString();

            var addresses = await Dns.GetHostAddressesAsync(address);
            var ipv4 = addresses.FirstOrDefault(a =>
                a.AddressFamily == AddressFamily.InterNetwork);

            if (ipv4 == null)
                throw new InvalidOperationException($"Не удалось резолвить {address} в IPv4");

            return ipv4.ToString();
        }

        /// <summary>
        /// Находит шлюз по умолчанию, игнорируя сам TUN-адаптер
        /// (иначе после перезапуска подхватит 10.255.0.1 и получится петля).
        /// </summary>
        private static string? GetDefaultGateway()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && !n.Name.Contains(TunName, StringComparison.OrdinalIgnoreCase)
                         && !n.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase))
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Where(g => g?.Address != null
                         && g.Address.AddressFamily == AddressFamily.InterNetwork
                         && !g.Address.Equals(IPAddress.Any))
                .Select(g => g.Address.ToString())
                .FirstOrDefault();
        }

        /// <summary>Опрашивает список интерфейсов, пока не появится TUN-адаптер.</summary>
        private static async Task<bool> WaitForAdapterAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                bool found = NetworkInterface.GetAllNetworkInterfaces()
                    .Any(n => n.Name.Contains(TunName, StringComparison.OrdinalIgnoreCase));

                if (found) return true;
                await Task.Delay(300);
            }
            return false;
        }

        private static Task RunCmdAsync(string args)
            => RunProcessAsync("cmd.exe", $"/c {args}");

        private static async Task RunProcessAsync(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }

        private static void RunCmdSync(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c {args}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { /* аварийная очистка — глушим всё */ }
        }

        private void Log(string msg) => LogReceived?.Invoke($"[BearNest] {msg}");

        public void Dispose() => CleanupImmediate();
    }
}