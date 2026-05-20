using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks; 

namespace VpnClient
{
    public class SubscriptionInfo
    {
        public string Title { get; set; } = "";
        public long Upload { get; set; }   // байты
        public long Download { get; set; }   // байты
        public long Total { get; set; }   // байты (0 = безлимит)
        public long ExpireUnix { get; set; }   // unix timestamp

        public DateTime? ExpireDate =>
            ExpireUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).LocalDateTime
                : null;

        public int? DaysLeft =>
            ExpireDate.HasValue
                ? Math.Max(0, (int)(ExpireDate.Value - DateTime.Now).TotalDays)
                : null;

        public string TrafficUsed =>
            $"{FormatBytes(Upload + Download)} / {(Total > 0 ? FormatBytes(Total) : "∞")}";

        public string ExpireText =>
            ExpireDate.HasValue
                ? $"до {ExpireDate.Value:dd.MM.yyyy} ({DaysLeft} дн.)"
                : "—";

        public string StatusColor =>
            DaysLeft switch
            {
                null => "#89b4fa",
                <= 3 => "#f38ba8",
                <= 7 => "#fab387",
                _ => "#a6e3a1"
            };

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F1} MB";
            double gb = mb / 1024.0;
            if (gb < 1024) return $"{gb:F1} GB";
            return $"{gb / 1024.0:F2} TB";
        }

        public bool IsEmpty => string.IsNullOrEmpty(Title) && ExpireUnix == 0;
    }
}