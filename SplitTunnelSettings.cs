using System.Collections.Generic;

namespace VpnClient
{
    public class SplitTunnelSettings
    {
        /// <summary>Домены которые идут напрямую, без VPN. Один домен = одна строка.</summary>
        public List<string> BypassDomains { get; set; } = new();

        /// <summary>IP-адреса / CIDR-диапазоны которые идут напрямую. Например: 1.2.3.4, 10.0.0.0/8</summary>
        public List<string> BypassIPs { get; set; } = new();

        /// <summary>Пропускать локальную сеть (192.168.x.x, 10.x.x.x, 172.16.x.x) напрямую</summary>
        public bool BypassLan { get; set; } = true;

        /// <summary>
        /// Активен ли TUN-режим. Влияет на генерацию конфига xray:
        ///   • добавляется секция "dns" — иначе резолв уйдёт в петлю через TUN
        ///   • добавляется правило маршрутизации DNS (порт 53 UDP) в прокси
        /// Значение приходит из чекбокса ChkTunMode в MainWindow.
        /// </summary>
        public bool TunMode { get; set; } = false;

        /// <summary>
        /// Имя физического сетевого адаптера (например "Ethernet 2", "Wi-Fi").
        /// Нужно только в TUN-режиме: outbound "direct" привязывается к нему
        /// через sockopt.interface, иначе bypass-трафик зацикливается в туннеле.
        /// Заполняется автоматически в BuildSplitTunnelSettings().
        /// </summary>
        public string PhysicalInterface { get; set; } = "";

    }

}