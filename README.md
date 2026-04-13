# Roblox Account Manager V2

A Python/PySide6 desktop application for managing multiple Roblox accounts — rewritten from the original C# [Roblox Account Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager) by ic3w0lf22.

## Features

- **Account Management** — Store and manage multiple Roblox accounts with encrypted cookie storage
- **Account Generator** — Automatically create new Roblox accounts via browser automation (requires NopeCHA API key for captcha)
- **Multi-Roblox** — Run multiple Roblox instances simultaneously by bypassing the single-instance mutex
- **Game Launcher** — Launch accounts directly into any Roblox game/server
- **Server Browser** — Browse and join specific game servers
- **Avatar Customizer** — Randomize avatar appearance on generated accounts
- **FPS Unlocker** — Unlock Roblox FPS beyond the default cap
- **Nexus** — WebSocket server for in-game script communication with controlled accounts
- **Web API** — Local HTTP server for external account control
- **Watcher** — Monitor and auto-restart Roblox processes
- **Theme Engine** — Customizable dark/light themes

## Requirements

- Windows 10/11
- Python 3.11+
- [Ungoogled Chromium](https://github.com/ungoogled-software/ungoogled-chromium) (for account generation, place in `lib/`)

## Installation

```bash
git clone https://github.com/engnyg/roblox-account-manager.git
cd roblox-account-manager
pip install -r requirements.txt
python main.py
```

## Account Generation

Account generation requires:
1. **NopeCHA API key** — Set in Settings → Generator → Captcha API Key ([nopecha.com](https://nopecha.com))
2. **Ungoogled Chromium** — Extract to `lib/ungoogled-chromium-*/`

## Configuration

All settings are stored in `config.ini`. Key options:

| Section | Key | Description |
|---------|-----|-------------|
| `General` | `language` | UI language (`en` / `zh`) |
| `General` | `theme` | UI theme (`Dark` / `Light`) |
| `General` | `unlockfps` | Enable FPS unlocker |
| `Generator` | `defaultpassword` | Default password for generated accounts |
| `Generator` | `captchaapikey` | NopeCHA API key |
| `Generator` | `useproxy` | Use proxy for account generation |
| `WebServer` | `enabled` | Enable local Web API server |
| `WebServer` | `port` | Web API port (default: `7963`) |
| `AccountControl` | `enabled` | Enable Nexus WebSocket server |
| `AccountControl` | `port` | Nexus port (default: `7964`) |

## Building

```bash
pip install pyinstaller
pyinstaller build.spec
```

Output will be in `dist/`.

## Credits

- Original C# project: [ic3w0lf22/Roblox-Account-Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager)
- Captcha solving: [NopeCHA](https://nopecha.com)

## Disclaimer

This project is for educational purposes. Use responsibly and in accordance with [Roblox's Terms of Service](https://en.help.roblox.com/hc/en-us/articles/115004647846).
