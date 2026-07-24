using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnClient
{
    public static class ConfigGenerator
    {
        // ── ГЛАВНЫЙ МЕТОД ─────────────────────────────────────────────
        // split = null → поведение как раньше (только bittorrent → direct)
        public static string Generate(ServerConfig s, SplitTunnelSettings? split = null)
        {
            var config = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["loglevel"] = "error"
                },
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["listen"]   = "127.0.0.1",
                        ["port"]     = 19808,
                        ["protocol"] = "socks",
                        ["settings"] = new JsonObject
                        {
                            ["auth"] = "noauth",
                            ["udp"]  = true
                        },
                        // ДОБАВЛЕНО: sniffing нужен для TUN-режима.
                        // tun2socks отдаёт xray уже РЕЗОЛВНУТЫЕ IP, а не домены.
                        // Без sniffing правила по доменам (bypass-список) просто
                        // не срабатывают — xray видит только IP и не знает, какому
                        // домену он принадлежит.
                        ["sniffing"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["destOverride"] = new JsonArray { "http", "tls", "quic" },
                            ["routeOnly"] = false
                        },
                        ["tag"] = "socks"
                    },
                    new JsonObject
                    {
                        ["listen"]   = "127.0.0.1",
                        ["port"]     = 19809,
                        ["protocol"] = "http",
                        ["tag"]      = "http"
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    BuildOutbound(s),
                    BuildDirectOutbound(split),
                    new JsonObject { ["protocol"] = "blackhole", ["tag"] = "block" }
                },
                ["routing"] = new JsonObject
                {
                    // СТАЛО:
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = BuildRoutingRules(split)
                }
            };

            // ── ДОБАВЛЕНО: DNS-секция для TUN-режима ─────────────────
            // В TUN-режиме DNS-запросы системы тоже идут в туннель.
            // Без явной DNS-секции xray будет использовать системный резолвер,
            // который в TUN-режиме указывает на сам туннель → петля.
            if (split?.TunMode == true)
            {
                config["dns"] = new JsonObject
                {
                    ["servers"] = new JsonArray { "1.1.1.1", "8.8.8.8" },
                    ["queryStrategy"] = "UseIPv4",
                    ["disableCache"] = false
                };
            }

            return config.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });
        }

        private static bool IsValidXrayDomain(string d)
        {
            // разрешаем: domain.com, sub.domain.com, geosite:tag, domain:tag
            if (d.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)) return true;
            if (d.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase)) return true;
            if (d.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase)) return true;
            if (d.StartsWith("full:", StringComparison.OrdinalIgnoreCase)) return true;
            // обычный домен — только буквы, цифры, точки, дефисы
            return System.Text.RegularExpressions.Regex.IsMatch(d, @"^[a-zA-Z0-9\.\-\*]+$");
        }


        // ── DIRECT OUTBOUND ──────────────────────────────────────────
        /// <summary>
        /// Строит outbound "direct" (freedom).
        ///
        /// В обычном режиме freedom просто отдаёт пакет ОС — этого достаточно.
        ///
        /// В TUN-режиме так делать НЕЛЬЗЯ: маршруты 0.0.0.0/1 и 128.0.0.0/1
        /// уже указывают на TUN-адаптер, поэтому пакет вернётся обратно
        /// в tun2socks → в xray → петля, и bypass-домены просто не открываются.
        ///
        /// Решение: sockopt.interface привязывает сокет к реальному
        /// сетевому интерфейсу, минуя таблицу маршрутизации.
        /// </summary>
        private static JsonObject BuildDirectOutbound(SplitTunnelSettings? split)
        {
            var direct = new JsonObject
            {
                ["protocol"] = "freedom",
                ["tag"] = "direct"
            };

            if (split?.TunMode == true && !string.IsNullOrWhiteSpace(split.PhysicalInterface))
            {
                direct["streamSettings"] = new JsonObject
                {
                    ["sockopt"] = new JsonObject
                    {
                        // Имя реального адаптера (Ethernet 2 / Wi-Fi).
                        // Без этого bypass в TUN-режиме уходит в петлю.
                        ["interface"] = split.PhysicalInterface
                    }
                };
            }

            return direct;
        }

        // ── СТРОИМ ROUTING RULES ─────────────────────────────────────
        private static JsonArray BuildRoutingRules(SplitTunnelSettings? split)
        {
            var rules = new JsonArray();

            // 0. ДОБАВЛЕНО: DNS через прокси (только в TUN-режиме).
            //    Идёт ПЕРВЫМ правилом — DNS должен решаться до всего остального,
            //    иначе резолв утечёт напрямую и DPI увидит запрашиваемые домены.
            if (split?.TunMode == true)
            {
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "proxy",
                    ["port"] = 53,
                    ["network"] = "udp"
                });
            }

            // 1. Bittorrent всегда напрямую (было раньше, оставляем)
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["protocol"] = new JsonArray { "bittorrent" }
            });

            if (split == null) return rules;

            // 2. Bypass LAN — локальная сеть напрямую
            if (split.BypassLan)
            {
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["ip"] = new JsonArray { "geoip:private" }
                });
            }

            // 3. Bypass домены — идут напрямую без VPN
            var domains = split.BypassDomains
                .SelectMany(d => d.Split('|', StringSplitOptions.RemoveEmptyEntries))  // разбиваем если вдруг пришли с |
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Where(d => !d.Contains(' '))           // убираем строки с пробелами
                .Where(d => IsValidXrayDomain(d))       // только валидные записи
                .Distinct()
                .ToList();

            if (domains.Count > 0)
            {
                var domainArr = new JsonArray();
                foreach (var d in domains)
                    domainArr.Add(JsonValue.Create(d));

                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["domain"] = domainArr
                });
            }

            // 4. Bypass IP / CIDR — идут напрямую без VPN
            var ips = split.BypassIPs
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .ToList();

            if (ips.Count > 0)
            {
                var ipArr = new JsonArray();
                foreach (var ip in ips)
                    ipArr.Add(JsonValue.Create(ip));

                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["ip"] = ipArr
                });
            }

            return rules;
        }

        // ── СТРОИМ OUTBOUND ───────────────────────────────────────────
        private static JsonObject BuildOutbound(ServerConfig s)
        {
            var outbound = new JsonObject { ["tag"] = "proxy" };

            if (s.Protocol == "vless")
            {
                outbound["protocol"] = "vless";
                outbound["settings"] = new JsonObject
                {
                    ["vnext"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["address"] = s.Address,
                            ["port"]    = s.Port,
                            ["users"]   = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["id"]         = s.Id,
                                    ["encryption"] = "none",
                                    ["flow"]       = s.Flow
                                }
                            }
                        }
                    }
                };

                var stream = new JsonObject
                {
                    ["network"] = s.Network
                };

                // TLS / Reality
                if (s.Security == "reality")
                {
                    stream["security"] = "reality";
                    stream["realitySettings"] = new JsonObject
                    {
                        ["serverName"] = s.ServerName,
                        ["fingerprint"] = s.Fingerprint,
                        ["publicKey"] = s.PublicKey,
                        ["shortId"] = s.ShortId
                    };
                }
                else if (s.Security == "tls")
                {
                    stream["security"] = "tls";
                    stream["tlsSettings"] = new JsonObject
                    {
                        ["serverName"] = s.ServerName,
                        ["fingerprint"] = s.Fingerprint
                    };
                }

                // Transport
                if (s.Network == "ws")
                {
                    stream["wsSettings"] = new JsonObject
                    {
                        ["path"] = s.Path,
                        ["headers"] = new JsonObject { ["Host"] = s.ServerName }
                    };
                }
                else if (s.Network == "xhttp")
                {
                    var xhttpSettings = new JsonObject
                    {
                        ["path"] = s.Path,
                        ["host"] = !string.IsNullOrEmpty(s.XhttpHost) ? s.XhttpHost : s.ServerName,
                        ["mode"] = !string.IsNullOrEmpty(s.XhttpMode) ? s.XhttpMode : "auto"
                    };

                    if (s.HasXmux)
                    {
                        xhttpSettings["extra"] = new JsonObject
                        {
                            ["xmux"] = new JsonObject
                            {
                                ["cMaxLifetimeMs"] = s.XmuxMaxLifetime,
                                ["cMaxReuseTimes"] = s.XmuxMaxReuse,
                                ["maxConcurrency"] = s.XmuxConcurrency,
                                ["maxConnections"] = 0,
                                ["hKeepAlivePeriod"] = 30
                            }
                        };
                    }

                    stream["xhttpSettings"] = xhttpSettings;
                }
                else if (s.Network == "grpc")
                {
                    stream["grpcSettings"] = new JsonObject
                    {
                        ["serviceName"] = s.Path
                    };
                }

                outbound["streamSettings"] = stream;
            }
            else if (s.Protocol == "vmess")
            {
                outbound["protocol"] = "vmess";
                outbound["settings"] = new JsonObject
                {
                    ["vnext"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["address"] = s.Address,
                            ["port"]    = s.Port,
                            ["users"]   = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["id"]       = s.Id,
                                    ["alterId"]  = 0,
                                    ["security"] = "auto"
                                }
                            }
                        }
                    }
                };
            }
            else if (s.Protocol == "trojan")
            {
                outbound["protocol"] = "trojan";
                outbound["settings"] = new JsonObject
                {
                    ["servers"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["address"]  = s.Address,
                            ["port"]     = s.Port,
                            ["password"] = s.Id
                        }
                    }
                };
            }

            return outbound;
        }
    }
}
