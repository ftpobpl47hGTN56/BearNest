using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace VpnClient
{
    public static class SystemProxy
    {
        // ── WinAPI для применения настроек прокси без перезапуска ───
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            nint hInternet,
            int dwOption,
            nint lpBuffer,
            int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        // ── ВКЛЮЧИТЬ ПРОКСИ ─────────────────────────────────────────
        public static void Enable(string host, int port)
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);

            if (key == null) return;

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"{host}:{port}", RegistryValueKind.String);
            // Локальные адреса не проксируем
            key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>",
                         RegistryValueKind.String);

            // Применяем немедленно — без этого браузер не увидит изменений
            RefreshSettings();
        }

        // ── ВЫКЛЮЧИТЬ ПРОКСИ ────────────────────────────────────────
        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);

            if (key == null) return;

            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);

            RefreshSettings();
        }

        // ── ПРИМЕНИТЬ ИЗМЕНЕНИЯ РЕЕСТРА НЕМЕДЛЕННО ──────────────────
        private static void RefreshSettings()
        {
            InternetSetOption(0, INTERNET_OPTION_SETTINGS_CHANGED, 0, 0);
            InternetSetOption(0, INTERNET_OPTION_REFRESH, 0, 0);
        }

        // ── ПРОВЕРИТЬ ТЕКУЩЕЕ СОСТОЯНИЕ ─────────────────────────────
        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");

            if (key == null) return false;

            var val = key.GetValue("ProxyEnable");
            return val is int i && i == 1;
        }
    }
}
