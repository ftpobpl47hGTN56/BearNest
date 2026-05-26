using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Web;

namespace VpnClient
{
    public class SubscriptionParser
    {
        public static async Task<string> DownloadAsync(string url, int proxyPort = 0)
        {
            HttpClientHandler handler;

            if (proxyPort > 0)
            {
                handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"socks5://127.0.0.1:{proxyPort}"),
                    UseProxy = true
                };
            }
            else
            {
                handler = new HttpClientHandler { UseProxy = false };
            }

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };


            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Happ/2.16.2/Windows/2605221224603");


            client.DefaultRequestHeaders.Add("Accept", "application/json");
       
            // Добавь эти три:
            client.DefaultRequestHeaders.Add("X-Device-Os", "Windows");
            client.DefaultRequestHeaders.Add("X-Ver-Os", "10_10.0.19045");
            client.DefaultRequestHeaders.Add("X-Hwid", "4c7f17cb-05b4-4ec0-b254-275442c3c71d");


            return await client.GetStringAsync(url);
        }

        public static List<ServerConfig> Parse(string content)
        {
            // В методе Parse — добавь проверку В САМОМ НАЧАЛЕ, до остальных форматов:
            if (content.TrimStart().StartsWith("["))
            {
                try
                {
                    var fromHapp = ParseHappXrayArray(content);
                    if (fromHapp.Count > 0) return fromHapp;
                }
                catch { }
            }



            var result = new List<ServerConfig>();
            content = content.Trim();
            // Нормализуем переносы строк — убираем \r
            content = content.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // Определяем формат
            if (content.Contains("proxies:"))
            {
                // Clash/Mihomo YAML формат
                result.AddRange(ParseClashYaml(content));
            }
            else
            {
                // Пробуем base64
                if (!content.Contains("://"))
                {
                    try
                    {
                        content = PadBase64(content);
                        content = Encoding.UTF8.GetString(Convert.FromBase64String(content));
                    }
                    catch { }
                }

                // Список URI ссылок
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    try
                    {
                        ServerConfig? cfg = null;
                        if (trimmed.StartsWith("vless://")) cfg = ParseVless(trimmed);
                        else if (trimmed.StartsWith("vmess://")) cfg = ParseVmess(trimmed);
                        else if (trimmed.StartsWith("trojan://")) cfg = ParseTrojan(trimmed);
                        else if (trimmed.StartsWith("ss://")) cfg = ParseShadowsocks(trimmed);
                        if (cfg != null) result.Add(cfg);
                    }
                    catch { }
                }
            }

            return result;
        }


        // ── Новый метод ──────────────────────────────────────────────────────
        private static List<ServerConfig> ParseHappXrayArray(string json)
        {
            var result = new List<ServerConfig>();
            var arr = JsonNode.Parse(json) as JsonArray;
            if (arr == null) return result;

            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;

                var remarks = obj["remarks"]?.GetValue<string>() ?? "";
                var outbounds = obj["outbounds"] as JsonArray;
                if (outbounds == null) continue;

                // Берём первый outbound с тегом начинающимся на "proxy"
                JsonObject? proxyOut = null;
                foreach (var o in outbounds)
                {
                    if (o is not JsonObject oo) continue;
                    var tag = oo["tag"]?.GetValue<string>() ?? "";
                    if (tag.StartsWith("proxy"))
                    {
                        proxyOut = oo;
                        break;
                    }
                }
                if (proxyOut == null) continue;

                var protocol = proxyOut["protocol"]?.GetValue<string>() ?? "";
                if (protocol != "vless" && protocol != "vmess" && protocol != "trojan") continue;

                var settings = proxyOut["settings"] as JsonObject;
                var stream = proxyOut["streamSettings"] as JsonObject;
                if (settings == null) continue;

                // Извлекаем адрес/порт/id из vnext[0]
                var vnext = (settings["vnext"] as JsonArray)?[0] as JsonObject;
                if (vnext == null) continue;

                var address = vnext["address"]?.GetValue<string>() ?? "";
                int.TryParse(vnext["port"]?.ToString(), out var port);

                var users = (vnext["users"] as JsonArray)?[0] as JsonObject;
                var id = users?["id"]?.GetValue<string>() ?? "";
                var flow = users?["flow"]?.GetValue<string>() ?? "";

                // streamSettings
                var network = stream?["network"]?.GetValue<string>() ?? "tcp";
                var security = stream?["security"]?.GetValue<string>() ?? "none";

                var cfg = new ServerConfig
                {
                    Protocol = protocol,
                    Name = remarks,
                    Address = address,
                    Port = port,
                    Id = id,
                    Flow = flow,
                    Network = network,
                    Security = security,
                };

                // Reality
                if (security == "reality")
                {
                    var rs = stream?["realitySettings"] as JsonObject;
                    cfg.ServerName = rs?["serverName"]?.GetValue<string>() ?? "";
                    cfg.PublicKey = rs?["publicKey"]?.GetValue<string>() ?? "";
                    cfg.ShortId = rs?["shortId"]?.GetValue<string>() ?? "";
                    cfg.Fingerprint = rs?["fingerprint"]?.GetValue<string>() ?? "chrome";
                }
                else if (security == "tls")
                {
                    var ts = stream?["tlsSettings"] as JsonObject;
                    cfg.ServerName = ts?["serverName"]?.GetValue<string>() ?? "";
                    cfg.Fingerprint = ts?["fingerprint"]?.GetValue<string>() ?? "chrome";
                }

                // xhttp
                if (network == "xhttp")
                {
                    var xh = stream?["xhttpSettings"] as JsonObject;
                    cfg.Path = xh?["path"]?.GetValue<string>() ?? "/";
                    cfg.XhttpHost = xh?["host"]?.GetValue<string>() ?? "";
                    cfg.XhttpMode = xh?["mode"]?.GetValue<string>() ?? "auto";

                    var xmux = (xh?["extra"] as JsonObject)?["xmux"] as JsonObject;
                    if (xmux != null)
                    {
                        cfg.HasXmux = true;
                        if (int.TryParse(xmux["cMaxLifetimeMs"]?.ToString(), out var lt))
                            cfg.XmuxMaxLifetime = lt;
                        cfg.XmuxMaxReuse = xmux["cMaxReuseTimes"]?.ToString() ?? "";
                        cfg.XmuxConcurrency = xmux["maxConcurrency"]?.ToString() ?? "";
                    }
                }
                else if (network == "ws")
                {
                    var ws = stream?["wsSettings"] as JsonObject;
                    cfg.Path = ws?["path"]?.GetValue<string>() ?? "/";
                }

                if (string.IsNullOrEmpty(cfg.Address) || port == 0) continue;
                result.Add(cfg);
            }

            return result;
        }




        // ── ПАРСЕР CLASH YAML ────────────────────────────────────────
        // Читаем секцию proxies: вручную без YAML библиотеки
        private static List<ServerConfig> ParseClashYaml(string yaml)
        {
            var result = new List<ServerConfig>();

            var proxiesIdx = yaml.IndexOf("\nproxies:", StringComparison.OrdinalIgnoreCase);
            if (proxiesIdx < 0) proxiesIdx = yaml.IndexOf("proxies:", StringComparison.OrdinalIgnoreCase);
            System.Diagnostics.Debug.WriteLine($"[Parser] proxies: найден на позиции {proxiesIdx}");

            if (proxiesIdx < 0) return result;

            // Находим конец строки "proxies:" и берём следующую строку
            var lineEnd = yaml.IndexOf('\n', proxiesIdx + 1);
            var start = lineEnd + 1;

            // ОТЛАДКА — смотрим что там на позиции start
            System.Diagnostics.Debug.WriteLine($"[Parser] start={start}, первые 100 символов после proxies:");
            System.Diagnostics.Debug.WriteLine($"[Parser] >>>{yaml[start..Math.Min(start + 100, yaml.Length)]}<<<");

            var lines = yaml[start..].Split('\n');
            System.Diagnostics.Debug.WriteLine($"[Parser] всего строк после proxies: {lines.Length}");
            var proxyLines = new List<string>();
            foreach (var line in lines)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] строка: '{line[..Math.Min(50, line.Length)]}'");
                if (line.TrimStart().StartsWith("rules:") ||
         line.TrimStart().StartsWith("rule-providers:"))
                {
                    System.Diagnostics.Debug.WriteLine($"[Parser] СТОП на строке: '{line}'");
                    break;
                }
                proxyLines.Add(line);
            }

            System.Diagnostics.Debug.WriteLine($"[Parser] proxyLines собрано: {proxyLines.Count}");

            var proxyBlocks = new List<string>();
            var current = new StringBuilder();

            foreach (var line in proxyLines)
            {
                if (line.TrimStart().StartsWith("- ") && current.Length > 0)
                {
                    proxyBlocks.Add(current.ToString());
                    current.Clear();
                }
                current.AppendLine(line);
            }
            if (current.Length > 0)
                proxyBlocks.Add(current.ToString());

            System.Diagnostics.Debug.WriteLine($"[Parser] proxyBlocks найдено: {proxyBlocks.Count}");

            foreach (var block in proxyBlocks)
            {
                try
                {
                    var cfg = ParseClashProxy(block);
                    if (cfg != null) result.Add(cfg);
                }
                catch { }
            }

            return result;
        }

        private static ServerConfig? ParseClashProxy(string block)
        {
            // Читаем все строки блока
            var lines = block.Split('\n');

            string Get(string key)
            {
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().TrimStart('-').Trim();
                    if (trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed[(key.Length + 1)..].Trim().Trim('"', '\'');
                        return val;
                    }
                }
                return "";
            }

            var type = Get("type").ToLower();
            if (string.IsNullOrEmpty(type)) return null;
            if (type != "vless" && type != "vmess" && type != "trojan" &&
                type != "ss" && type != "shadowsocks") return null;

            int.TryParse(Get("port"), out var port);

            // Reality-opts — ищем вложенные поля
            string GetNested(string parentKey, string childKey)
            {
                bool inParent = false;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith($"{parentKey}:", StringComparison.OrdinalIgnoreCase))
                    {
                        inParent = true;
                        continue;
                    }
                    if (inParent)
                    {
                        // Если строка не начинается с пробела — вышли из блока
                        if (line.Length > 0 && line[0] != ' ' && !line.StartsWith("  "))
                            break;
                        if (trimmed.StartsWith($"{childKey}:", StringComparison.OrdinalIgnoreCase))
                            return trimmed[(childKey.Length + 1)..].Trim().Trim('"', '\'');
                    }
                }
                return "";
            }

            var cfg = new ServerConfig
            {
                Protocol = type == "shadowsocks" ? "ss" : type,
                Name = Get("name"),
                Address = Get("server"),
                Port = port,
                Id = Get("uuid"),
                Network = Get("network"),
                ServerName = Get("servername"),
                Flow = Get("flow"),
                Fingerprint = Get("client-fingerprint"),
                Path = Get("ws-path"),
                PublicKey = GetNested("reality-opts", "public-key"),
                ShortId = GetNested("reality-opts", "short-id"),
            };

            // Security
            if (block.Contains("reality-opts:"))
                cfg.Security = "reality";
            else if (Get("tls") == "true")
                cfg.Security = "tls";
            else
                cfg.Security = "none";

            // Trojan
            if (type == "trojan")
            {
                cfg.Security = "tls";
                cfg.Id = Get("password");
            }

            // SS
            if (type == "ss" || type == "shadowsocks")
                cfg.Id = Get("password");

            // xhttp-opts
            if (cfg.Network == "xhttp")
            {
                var mode = GetNested("xhttp-opts", "mode");
                if (!string.IsNullOrEmpty(mode)) cfg.XhttpMode = mode;

                var host = GetNested("xhttp-opts", "host");
                if (!string.IsNullOrEmpty(host)) cfg.XhttpHost = host;

                var xmuxConcurrency = GetNested("xmux", "maxConcurrency");
                if (!string.IsNullOrEmpty(xmuxConcurrency))
                {
                    cfg.HasXmux = true;
                    cfg.XmuxConcurrency = xmuxConcurrency;
                    var lifetime = GetNested("xmux", "cMaxLifetimeMs");
                    if (int.TryParse(lifetime, out var lt)) cfg.XmuxMaxLifetime = lt;
                    var reuse = GetNested("xmux", "cMaxReuseTimes");
                    if (!string.IsNullOrEmpty(reuse)) cfg.XmuxMaxReuse = reuse;
                }
            }

            if (string.IsNullOrEmpty(cfg.Name)) cfg.Name = $"{type}://{cfg.Address}:{cfg.Port}";
            if (string.IsNullOrEmpty(cfg.Network)) cfg.Network = "tcp";
            if (string.IsNullOrEmpty(cfg.Fingerprint)) cfg.Fingerprint = "chrome";

            return cfg;
        }

        private static ServerConfig? ParseVless(string uri)
        {
            var u = new Uri(uri);
            var query = HttpUtility.ParseQueryString(u.Query);
            return new ServerConfig
            {
                Protocol = "vless",
                Id = u.UserInfo,
                Address = u.Host,
                Port = u.Port,
                Security = query["security"] ?? "none",
                Network = query["type"] ?? "tcp",
                ServerName = query["sni"] ?? u.Host,
                PublicKey = query["pbk"] ?? "",
                ShortId = query["sid"] ?? "",
                Flow = query["flow"] ?? "",
                Fingerprint = query["fp"] ?? "chrome",
                Path = query["path"] ?? "/",
                Name = Uri.UnescapeDataString(u.Fragment.TrimStart('#'))
            };
        }

        private static ServerConfig? ParseVmess(string uri)
        {
            var base64 = PadBase64(uri[8..]);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            string Get(string key)
            {
                var idx = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return "";
                idx = json.IndexOf(':', idx) + 1;
                while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"')) idx++;
                var end = json.IndexOfAny(new[] { '"', ',', '}' }, idx);
                return end > idx ? json[idx..end] : "";
            }

            return new ServerConfig
            {
                Protocol = "vmess",
                Id = Get("id"),
                Address = Get("add"),
                Port = int.TryParse(Get("port"), out var p) ? p : 443,
                Network = Get("net"),
                Security = Get("tls") == "tls" ? "tls" : "none",
                ServerName = Get("sni"),
                Path = Get("path"),
                Name = Get("ps")
            };
        }

        private static ServerConfig? ParseTrojan(string uri)
        {
            var u = new Uri(uri);
            var query = HttpUtility.ParseQueryString(u.Query);
            return new ServerConfig
            {
                Protocol = "trojan",
                Id = u.UserInfo,
                Address = u.Host,
                Port = u.Port,
                Security = "tls",
                Network = query["type"] ?? "tcp",
                ServerName = query["sni"] ?? u.Host,
                Name = Uri.UnescapeDataString(u.Fragment.TrimStart('#'))
            };
        }

        private static ServerConfig? ParseShadowsocks(string uri)
        {
            var u = new Uri(uri);
            return new ServerConfig
            {
                Protocol = "ss",
                Address = u.Host,
                Port = u.Port,
                Name = Uri.UnescapeDataString(u.Fragment.TrimStart('#'))
            };
        }

        private static string PadBase64(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return (s.Length % 4) switch
            {
                2 => s + "==",
                3 => s + "=",
                _ => s
            };
        }

        // ── СКАЧАТЬ С ИНФО О ПОДПИСКЕ ────────────────────────────
        public static async Task<(string Content, SubscriptionInfo Info)>
            DownloadWithInfoAsync(string url, int proxyPort = 0)
        {
            HttpClientHandler handler = proxyPort > 0
                ? new HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy($"socks5://127.0.0.1:{proxyPort}"),
                    UseProxy = true
                }
                : new HttpClientHandler { UseProxy = false };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Happ/2.16.2/Windows/2605221224603");


            client.DefaultRequestHeaders.Add("Accept", "application/json");
            // Добавь эти три:
            client.DefaultRequestHeaders.Add("X-Device-Os", "Windows");
            client.DefaultRequestHeaders.Add("X-Ver-Os", "10_10.0.19045");
            client.DefaultRequestHeaders.Add("X-Hwid", "4c7f17cb-05b4-4ec0-b254-275442c3c71d");


            using var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            // Временно — смотрим первые 500 символов
            System.Diagnostics.Debug.WriteLine($"[JSON] ContentType: {response.Content.Headers.ContentType}");
            System.Diagnostics.Debug.WriteLine($"[JSON] Length: {content.Length}");
            System.Diagnostics.Debug.WriteLine($"[JSON] First500: {content[..Math.Min(500, content.Length)]}");

           

                        // Парсим заголовки
                        var info = new SubscriptionInfo();

            // profile-title
            if (response.Headers.TryGetValues("profile-title", out var titles))
            {
                var raw = titles.FirstOrDefault() ?? "";
                // Декодируем base64 если нужно
                if (raw.StartsWith("base64:"))
                {
                    try
                    {
                        var b64 = raw[7..]; // убираем "base64:"
                        b64 = b64.Replace('-', '+').Replace('_', '/');
                        switch (b64.Length % 4)
                        {
                            case 2: b64 += "=="; break;
                            case 3: b64 += "="; break;
                        }
                        raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    }
                    catch { }
                }
                info.Title = Uri.UnescapeDataString(raw);
            }

            // subscription-userinfo: upload=0; download=0; total=0; expire=0
            if (response.Headers.TryGetValues("subscription-userinfo", out var userInfos))
            {
                var raw = userInfos.FirstOrDefault() ?? "";
                foreach (var part in raw.Split(';'))
                {
                    var kv = part.Trim().Split('=');
                    if (kv.Length != 2) continue;
                    var k = kv[0].Trim();
                    if (!long.TryParse(kv[1].Trim(), out var v)) continue;
                    switch (k)
                    {
                        case "upload": info.Upload = v; break;
                        case "download": info.Download = v; break;
                        case "total": info.Total = v; break;
                        case "expire": info.ExpireUnix = v; break;
                    }
                }
            }

            return (content, info);
        }

    }
}
