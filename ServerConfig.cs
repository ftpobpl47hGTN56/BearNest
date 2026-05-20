namespace VpnClient
{
    public class ServerConfig
    {
        public string Name { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; }
        public string Id { get; set; } = "";
        public string Security { get; set; } = "";
        public string Network { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public string ShortId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string Path { get; set; } = "/";
        public string Flow { get; set; } = "";
        public string Fingerprint { get; set; } = "chrome";

        // Пинг в мс (-1 = не проверен, -2 = недоступен)
        public long PingMs { get; set; } = -1;

        // Отображение в UI
        public string PingDisplay => PingMs switch
        {
            -1 => "—",
            -2 => "❌",
            < 80 => $"🟢 {PingMs} мс",
            < 150 => $"🟡 {PingMs} мс",
            _ => $"🔴 {PingMs} мс"
        };

        public override string ToString() =>
            string.IsNullOrEmpty(Name)
                ? $"{Protocol}://{Address}:{Port}"
                : Name;
    }
}