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
        // xmux параметры
        public bool HasXmux { get; set; } = false;
        public int XmuxMaxLifetime { get; set; } = 30000;
        public string XmuxMaxReuse { get; set; } = "64-128";
        public string XmuxConcurrency { get; set; } = "4-8"; 
        public string XhttpMode { get; set; } = "auto";   // ← добавь
        public string XhttpHost { get; set; } = "";        // ← добавь



        // Пинг в мс (-1 = не проверен, -2 = недоступен)
        public long PingMs { get; set; } = -1;

        // Отображение в UI
        public string PingDisplay => PingMs switch
        {
            -1 => "—",
            -2 => "❌",
            < 80 => $"🟢 {PingMs} ms",
            < 150 => $"🟡 {PingMs} ms",
            _ => $"🔴 {PingMs} ms"
        };

        public override string ToString() =>
            string.IsNullOrEmpty(Name)
                ? $"{Protocol}://{Address}:{Port}"
                : Name;
    }
}