using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace VpnClient
{
    public static class ServerPinger
    {
        // ── TCP ПИНГ — подключаемся к порту сервера ──────────────────
        // Намного надёжнее чем ICMP для VPN серверов
        public static async Task<long> PingAsync(string host, int port = 443, int timeoutMs = 3000)
        {
            try
            {
                var sw = Stopwatch.StartNew();

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs);

                // Ждём подключения или таймаута
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                sw.Stop();

                if (completed == timeoutTask)
                    return -1; // таймаут

                if (connectTask.IsFaulted)
                    return -1; // ошибка подключения

                return sw.ElapsedMilliseconds;
            }
            catch
            {
                return -1;
            }
        }

        // ── ПИНГ ВСЕХ СЕРВЕРОВ ПАРАЛЛЕЛЬНО ──────────────────────────
        public static async Task<long[]> PingAllAsync(List<ServerConfig> servers)
        {
            var tasks = new Task<long>[servers.Count];

            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                // Передаём реальный порт сервера
                tasks[i] = PingAsync(server.Address, server.Port);
            }

            return await Task.WhenAll(tasks);
        }

        // ── НАЙТИ ЛУЧШИЙ СЕРВЕР ──────────────────────────────────────
        public static int FindBest(long[] pings)
        {
            int best = -1;
            long bestMs = long.MaxValue;

            for (int i = 0; i < pings.Length; i++)
            {
                if (pings[i] >= 0 && pings[i] < bestMs)
                {
                    bestMs = pings[i];
                    best = i;
                }
            }

            return best;
        }
    }
}