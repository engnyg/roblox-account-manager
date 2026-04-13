# Roblox Account Manager V2

以 Python/PySide6 重寫的 Roblox 多帳號管理工具，靈感來自以下兩個原版專案：
- [ic3w0lf22/Roblox-Account-Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager) — C# 帳號管理器
- [qing762/roblox-auto-signup](https://github.com/qing762/roblox-auto-signup) — 自動註冊腳本

## 功能

- **帳號管理** — 儲存並管理多個 Roblox 帳號，Cookie 加密保存
- **帳號生成器** — 透過瀏覽器自動化批量建立新帳號（需 NopeCHA API Key 解驗證碼）
- **Multi-Roblox** — 繞過單一實例限制，同時運行多個 Roblox
- **遊戲啟動器** — 直接將指定帳號加入任意遊戲／伺服器
- **伺服器瀏覽器** — 瀏覽並加入特定遊戲伺服器
- **頭像隨機化** — 自動隨機化生成帳號的外觀
- **FPS 解鎖** — 解除 Roblox 預設 FPS 上限
- **Nexus** — WebSocket 伺服器，供遊戲內腳本與帳號通訊
- **Web API** — 本地 HTTP 伺服器，支援外部程式控制帳號
- **Watcher** — 監控並自動重啟 Roblox 進程
- **主題引擎** — 可自訂深色／淺色主題

## 系統需求

- Windows 10/11
- Python 3.11+
- [Ungoogled Chromium](https://github.com/ungoogled-software/ungoogled-chromium)（帳號生成用，放入 `lib/`）

## 安裝

```bash
git clone https://github.com/engnyg/roblox-account-manager.git
cd roblox-account-manager
pip install -r requirements.txt
python main.py
```

## 帳號生成

帳號生成需要：
1. **NopeCHA API Key** — 至 設定 → 生成器 → Captcha API Key 填入（[nopecha.com](https://nopecha.com)）
2. **Ungoogled Chromium** — 解壓至 `lib/ungoogled-chromium-*/`

## 設定說明

所有設定儲存於 `config.ini`，主要選項：

| 區段 | 鍵值 | 說明 |
|------|------|------|
| `General` | `language` | 介面語言（`en` / `zh`） |
| `General` | `theme` | 介面主題（`Dark` / `Light`） |
| `General` | `unlockfps` | 啟用 FPS 解鎖 |
| `Generator` | `defaultpassword` | 生成帳號的預設密碼 |
| `Generator` | `captchaapikey` | NopeCHA API Key |
| `Generator` | `useproxy` | 生成時使用代理 |
| `WebServer` | `enabled` | 啟用本地 Web API 伺服器 |
| `WebServer` | `port` | Web API 埠號（預設：`7963`） |
| `AccountControl` | `enabled` | 啟用 Nexus WebSocket 伺服器 |
| `AccountControl` | `port` | Nexus 埠號（預設：`7964`） |

## 打包

```bash
pip install pyinstaller
pyinstaller build.spec
```

輸出檔案位於 `dist/`。

## 致謝

- 原版帳號管理器：[ic3w0lf22/Roblox-Account-Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager)
- 原版自動註冊：[qing762/roblox-auto-signup](https://github.com/qing762/roblox-auto-signup)
- 驗證碼解題：[NopeCHA](https://nopecha.com)

## 免責聲明

本專案僅供學習研究使用，請遵守 [Roblox 使用條款](https://en.help.roblox.com/hc/en-us/articles/115004647846)。
