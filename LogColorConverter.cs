using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace VpnClient
{
    public class LogColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string line)
                return new SolidColorBrush(Color.FromRgb(150, 150, 160));

            // ── BearNest сообщения ────────────────────────────────
            if (line.StartsWith("[BearNest]"))
            {
                if (line.Contains("Подключаемся") || line.Contains("Connecting") ||
                    line.Contains("Прокси включён") || line.Contains("Proxy enabled"))
                    return new SolidColorBrush(Color.FromRgb(166, 227, 161)); // зелёный

                if (line.Contains("Прокси выключен") || line.Contains("Proxy disabled") ||
                    line.Contains("остановлен") || line.Contains("stopped"))
                    return new SolidColorBrush(Color.FromRgb(243, 139, 168)); // розово-красный

                if (line.Contains("Нет ответа") || line.Contains("No response"))
                    return new SolidColorBrush(Color.FromRgb(250, 179, 135)); // оранжевый

                if (line.Contains("Переключились") || line.Contains("Switched") ||
                    line.Contains("Авто-переключение") || line.Contains("Auto-switch"))
                    return new SolidColorBrush(Color.FromRgb(137, 180, 250)); // синий

                if (line.Contains("Ошибка") || line.Contains("Error") || line.Contains("❌"))
                    return new SolidColorBrush(Color.FromRgb(243, 139, 168)); // красный

                if (line.Contains("Split tunneling") || line.Contains("🔀"))
                    return new SolidColorBrush(Color.FromRgb(203, 166, 247)); // фиолетовый

                // остальные BearNest — голубой
                return new SolidColorBrush(Color.FromRgb(137, 220, 235));
            }

            // ── Xray категории ────────────────────────────────────
            if (line.Contains("[Error]"))
                return new SolidColorBrush(Color.FromRgb(243, 139, 168)); // красный

            if (line.Contains("[Warning]"))
                return new SolidColorBrush(Color.FromRgb(250, 179, 135)); // оранжевый

            if (line.Contains("[Info]"))
                return new SolidColorBrush(Color.FromRgb(137, 180, 250)); // синий

            if (line.Contains("[Debug]"))
                return new SolidColorBrush(Color.FromRgb(128, 128, 140)); // серый

            // Xray стартовая строка
            if (line.Contains("Xray") && line.Contains("windows"))
                return new SolidColorBrush(Color.FromRgb(166, 227, 161)); // зелёный

            // По умолчанию — приглушённый
            return new SolidColorBrush(Color.FromRgb(150, 150, 160));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ════════════════════════════════════════════════════════════════
    // НОВОЕ: конвертер "есть ли в строке домен?" → bool
    // Используется в MainWindow.xaml: кнопка "➕ в обход" видна только
    // на строках, из которых реально можно достать домен.
    // ════════════════════════════════════════════════════════════════
    public class LogHasDomainConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string line || string.IsNullOrWhiteSpace(line))
                return false;
            return LogDomainExtractor.HasDomain(line);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ════════════════════════════════════════════════════════════════
    // НОВОЕ: единый парсер домена из строки лога.
    // Один источник правды и для конвертера (HasDomain),
    // и для обработчика кнопки в MainWindow (Extract).
    // ════════════════════════════════════════════════════════════════
    public static class LogDomainExtractor
    {
        // Ловим хост после "//", "tcp:" или "udp:".
        // Примеры строк xray access-лога:
        //   accepted //events.7tv.io:443 [socks >> proxy]
        //   accepted tcp:aka.ms:443 [socks >> direct]
        // Требуем на конце .<буквенная-зона> (≥2 букв) → чистые IP не матчатся.
        private static readonly Regex HostPort = new(
            @"(?://|tcp:|udp:)([a-zA-Z0-9\.\-]+\.[a-zA-Z]{2,})(?::\d+)?",
            RegexOptions.Compiled);

        /// <summary>Извлекает домен из строки лога. Пустая строка — если не найден.</summary>
        public static string Extract(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var m = HostPort.Match(line);
            if (!m.Success)
                return string.Empty;

            var host = m.Groups[1].Value.ToLowerInvariant();
            // www. убираем — xray лучше матчит без него
            return host.StartsWith("www.") ? host[4..] : host;
        }

        /// <summary>true, если в строке есть распознаваемый домен.</summary>
        public static bool HasDomain(string line) => Extract(line).Length > 0;
    }
}
