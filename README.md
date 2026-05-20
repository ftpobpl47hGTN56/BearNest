#   BearNest VPN

# <img width="56" height="56" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />
A lightweight Windows VPN client built on [xray-core](https://github.com/XTLS/Xray-core),
written in C# WPF (.NET 8).

> No bloat. No memory leaks. No crashes after 8-hour sessions.



<img width="1047" height="841" alt="image" src="https://github.com/user-attachments/assets/8b502d09-4207-47d4-a492-5556394552e8" />


---

## ✨ Features

- **Subscription support** — paste your subscription URL and get the full server list instantly
- **Auto proxy** — system proxy enables/disables automatically on connect/disconnect
- **Ping test** — TCP ping all servers in parallel, auto-selects the fastest one
- **Watchdog** — monitors connection every 30 seconds, auto-switches server if it goes down
- **Tray icon** — dynamic icon changes based on connection state, minimizes to tray
- **4 built-in themes** — Dark, Light, Nord, Bear + full theme editor with RGB sliders
- **Session timer** — shows how long you've been connected
- **Subscription status** — shows provider name, traffic used, and expiry date
- **Rolling logs** — logs to file with automatic rotation, never bloats
- **Settings backup** — export/import all settings as JSON
- **Auto-start** — optional Windows startup

---

## What's inside
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

```
BearNest/
├── BearNest.exe
├── core/
│   ├── xray.exe          ← xray-core engine
│   ├── geoip.dat
│   ├── geosite.dat
│   └── wintun.dll
├── icons/
│   ├── bearnest_Connected.ico
│   ├── bearnest_error_DisConnected.ico
│   └── ...
└── Themes/
    ├── Dark.xaml
    ├── Light.xaml
    ├── Nord.xaml
    └── Bear.xaml
```

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

##  Quick Start

1. Download the latest release archive
2. Extract anywhere
3. Run `BearNest.exe`
4. Paste your subscription URL and click **Load**
5. Click **Ping Test** to find the fastest server
6. Hit **Connect**

No installation required. No admin rights for proxy mode.

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## Supported Protocols

Anything xray-core supports:

- VLESS + Reality (XHTTP, TCP, WebSocket, gRPC)
- VMess
- Trojan
- Shadowsocks

Subscription formats: **Clash YAML**, **base64 URI list**

---
<img width="56" height="56" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## Themes

| Dark | Light | Nord | Bear |
|------|-------|------|------|
| Catppuccin Mocha | Catppuccin Latte | Nord palette | Custom dark blue |

Theme editor lets you customize every color with RGB sliders and save as `.xaml` file.

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

##  Built With

- **C# / .NET 8 / WPF** — UI
- **xray-core** — proxy engine
- **SQLite** — settings storage
- **WinForms NotifyIcon** — system tray

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## ⚠️ Requirements
Download xray-core from:https://github.com/XTLS/Xray-core/releases
oor u can find xray file in core folder- which is zip .

Place xray.exe, geoip.dat, geosite.dat, wintun.dll into core/ folder

                
- Windows 10 / 11 x64
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- A working subscription URL (Clash YAML format)

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## 🌐 Language

UI language: English / Russian 


<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## 📋 Roadmap

- [ ] TUN mode (full traffic capture, like Happ)
- [ ] BearNest Mobile (Android)
- [ ] Multiple subscription support
- [ ] Speed test per server

---
<img width="44" height="44" alt="image" src="https://github.com/user-attachments/assets/b9abbba7-79a0-4003-8d9c-73037b3be02c" />

## 📄 License

MIT — do whatever you want with it.

---

*Built because existing clients kept crashing after long sessions.*
