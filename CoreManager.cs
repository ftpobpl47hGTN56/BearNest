using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VpnClient
{
    public class CoreManager : IDisposable
    {
        // ---- настройки ----
        private readonly string _xrayExePath;
        private readonly string _configPath;

        // ---- состояние ----
        private Process? _process;
        private CancellationTokenSource _cts = new();

        // ---- события ----
        public event Action<string>? LogReceived;   // строка лога из xray
        public event Action<bool>? StatusChanged; // true = запущен, false = остановлен

        public bool IsRunning => _process is { HasExited: false };

        public CoreManager(string xrayExePath, string configPath)
        {
            _xrayExePath = xrayExePath;
            _configPath = configPath;
        }

        // ── ЗАПУСК ──────────────────────────────────────────────────
        public Task StartAsync()
        {
            if (IsRunning)
                return Task.CompletedTask;

            _cts = new CancellationTokenSource();

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _xrayExePath,
                    Arguments = $"run -c \"{_configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true
            };

            _process.Exited += OnProcessExited;
            _process.Start();

            // Читаем stdout и stderr асинхронно
            // ВАЖНО: нельзя читать синхронно — будет deadlock при
            // переполнении внутреннего буфера процесса
            _ = ReadStreamAsync(_process.StandardOutput, _cts.Token);
            _ = ReadStreamAsync(_process.StandardError, _cts.Token);

            StatusChanged?.Invoke(true);
            LogReceived?.Invoke("[VpnClient] Xray process started.");

            return Task.CompletedTask;
        }

        // ── ОСТАНОВКА ────────────────────────────────────────────────
        public async Task StopAsync()
        {
            _cts.Cancel();

            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            StatusChanged?.Invoke(false);
            LogReceived?.Invoke("[VpnClient] Xray process stopped.");
        }

        // Немедленное убийство процесса без ожидания — для закрытия приложения
        public void KillImmediate()
        {
            _cts.Cancel();
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }

        // ── ЧТЕНИЕ ПОТОКА ВЫВОДА ─────────────────────────────────────
        private async Task ReadStreamAsync(StreamReader reader, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break; // поток закрылся
                    LogReceived?.Invoke(line);
                }
            }
            catch (OperationCanceledException) { /* нормальная остановка */ }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[VpnClient] Stream read error: {ex.Message}");
            }
        }

        // ── НЕОЖИДАННАЯ СМЕРТЬ ПРОЦЕССА ──────────────────────────────
        private void OnProcessExited(object? sender, EventArgs e)
        {
            StatusChanged?.Invoke(false);
            LogReceived?.Invoke($"[VpnClient] Xray exited with code: {_process?.ExitCode}");
        }

        public void Dispose()
        {
            _cts.Dispose();
            _process?.Dispose();
        }
    }
}