# Third-party notices

## MultiRoblox-RAM

部分多開邏輯（關閉執行中 Roblox 的 `ROBLOX_singletonEvent` handle、以同名物件佔住
event 名稱、直接以 `roblox-player:` URI 啟動 `RobloxPlayerBeta.exe`）是參考並移植自
MultiRoblox-RAM 的原始碼（`RobloxNative.cs` 的 HandleCloser / RunMutex 等），
以 Python / ctypes 重寫並做了調整。

涉及檔案：

- `manager/singleton_guard.py`
- `manager/multi_roblox.py`（event 名稱佔位、啟動前準備）
- `manager/roblox_launch.py`（直接啟動）

Required Notice: Copyright (c) 2026 PookiePepelsss (https://github.com/PookiePepelsss/MultiRoblox-RAM)

授權：PolyForm Noncommercial License 1.0.0
<https://polyformproject.org/licenses/noncommercial/1.0.0>

**此授權只允許非商業用途。** 若要散布含這些程式碼的軟體，必須同時附上此授權的完整文字
（或連結）與上方 Required Notice；不得將這部分程式碼用於商業用途。

## 本專案自行新增（非衍生自上述專案）

- `manager/resource_limits.py`：每個實例的 CPU（Job Object CPU rate hard cap）與
  RAM（working set hard cap）限制。
- Mixer 對話框中的 CPU / RAM 限制介面。
