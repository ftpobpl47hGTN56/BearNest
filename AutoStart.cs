using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VpnClient
{
    public static class AutoStart
    {
        private const string AppName = "BearNest";
        private const string RegKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static void Enable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
            key?.SetValue(AppName, $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName}\"");
        }

        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue(AppName) != null;
        }

        public static void Toggle()
        {
            if (IsEnabled()) Disable();
            else Enable();
        }
    }
}