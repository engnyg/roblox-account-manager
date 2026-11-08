# Third-party notices

## MultiRoblox-RAM

多開邏輯（關閉執行中 Roblox 的 `ROBLOX_singletonEvent` handle、以同名物件佔住 event 名稱、
直接以 `roblox-player:` URI 啟動 `RobloxPlayerBeta.exe`）參考並移植自 MultiRoblox-RAM 的原始碼
（`RobloxNative.cs` 的 HandleCloser / RunMutex 等），並做了調整。

此外，下列功能的**設計與行為參考**了 MultiRoblox-RAM 的功能集（以 C# 重新實作並依 V3 架構調整）：
私人伺服器加入流程、伺服器清單與遊戲排行榜、每個帳號的啟動目標、自動重新進入、帳號工具（PIN / 隱私 / 密碼 / 信箱 /
封鎖 / 好友 / 尋找玩家）、Web API、Nexus、實例監看、記憶體釋放、處理程序優先權、渲染器與畫質覆寫、
每個實例的音量、截圖追蹤（Discord webhook）、主題與主題編輯器、點擊音效、系統匣、記錄視窗、
以及 V2 / 原版 RAM 加密帳號檔（`accounts.dat`）的匯入。

涉及檔案：

- `src/Core/Native/SingletonGuard.cs`
- `src/Core/Native/MultiRoblox.cs`
- `src/Core/Native/RobloxLauncher.cs`
- `src/Core/Native/FastFlags.cs`、`ProcessTuning.cs`、`AudioSessions.cs`、`RobloxWatcher.cs`、`ScreenCapture.cs`
- `src/Core/Services/PrivateServer.cs`、`GameDiscovery.cs`、`AccountApi.cs`、`AccountTarget.cs`、`RelaunchSupervisor.cs`、
  `NexusServer.cs`、`WebApiServer.cs`、`ScreenshotTracker.cs`、`LegacyCrypto.cs`
- `src/App/` 內對應的視窗與 `ThemeManager.cs`、`UiSound.cs`、`TrayIcon.cs`

Required Notice: Copyright (c) 2026 PookiePepelsss (https://github.com/PookiePepelsss/MultiRoblox-RAM)

授權：PolyForm Noncommercial License 1.0.0
<https://polyformproject.org/licenses/noncommercial/1.0.0>

**此授權只允許非商業用途。** 散布含這些程式碼的軟體時，必須附上此授權的完整文字（或連結）與上方
Required Notice；不得將這部分程式碼用於商業用途。

## 相依套件

| 套件 | 用途 | 授權 |
| --- | --- | --- |
| Sodium.Core 1.3.5 | 匯入 V2 / RAM 加密帳號檔（libsodium：Argon2id + SecretBox） | MIT（libsodium：ISC） |
| NAudio.Wasapi 2.2.1 | 每個處理程序的音量（Core Audio） | MIT |
| Microsoft.Web.WebView2 1.0.2792.45 | 內嵌登入 / 在瀏覽器開啟帳號 | Microsoft 軟體授權條款（WebView2 SDK） |

## 本專案自行撰寫

- `src/Core/Native/ResourceLimits.cs`：每個實例的 CPU（Job Object CPU rate hard cap）與 RAM（working set hard cap）限制
- `src/Core/Native/WindowLayout.cs`：視窗大小與格狀排列
- 自訂密碼模式的帳號檔加密（PBKDF2-SHA256 + AES-256-GCM）
- 其餘 Roblox API、帳號儲存、介面程式碼
