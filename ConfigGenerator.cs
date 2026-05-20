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
        public static string Generate(ServerConfig s)
        {
            // Базовая структура конфига xray
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
                    new JsonObject { ["protocol"] = "freedom", ["tag"] = "direct" },
                    new JsonObject { ["protocol"] = "blackhole", ["tag"] = "block" }
                },
                ["routing"] = new JsonObject
                {
                    ["domainStrategy"] = "IPIfNonMatch",
                    ["rules"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"]        = "field",
                            ["outboundTag"] = "direct",
                            ["protocol"]    = new JsonArray { "bittorrent" }
                        }
                    }
                }
            };

            return config.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });
        }

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
                    stream["xhttpSettings"] = new JsonObject
                    {
                        ["path"] = s.Path,
                        ["host"] = s.ServerName,
                        ["mode"] = "auto"
                    };
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