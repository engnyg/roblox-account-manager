"""
直接啟動 RobloxPlayerBeta.exe（繞過 roblox-player: 協定處理器 / Bootstrapper）。

用 os.startfile(roblox-player:...) 會交給 Roblox / Froststrap / Bloxstrap 等 bootstrapper，
它們可能彈出確認視窗、也可能在啟動前做自己的事。直接把 URI 當作引數丟給 RobloxPlayerBeta.exe
可以避開這些，並且讓我們拿到真正的 pid（追蹤 handle 清除的豁免、套用 CPU/RAM 限制）。

啟動前會先呼叫 multi_roblox.prepare_for_launch()：關閉執行中舊實例的
ROBLOX_singletonEvent handle，並確認 event 名稱已被佔住（做法參考 MultiRoblox-RAM，
見 THIRD_PARTY_NOTICES.md）。
"""

from __future__ import annotations

import glob
import os
import subprocess
import sys
from typing import Optional

import psutil
from loguru import logger

ROBLOX_EXE = "RobloxPlayerBeta.exe"

# 可能的安裝根目錄（%LOCALAPPDATA% 底下）
_LOCAL_ROOTS = ("Roblox", "Froststrap", "Bloxstrap", "Voidstrap", "Fishstrap")

_DETACHED_PROCESS = 0x00000008
_CREATE_NEW_PROCESS_GROUP = 0x00000200


def _settings():
    try:
        from core.settings import get_settings
        return get_settings()
    except Exception:
        return None


def _running_roblox_exe() -> Optional[str]:
    """正在執行的 Roblox 用的 exe（最可能是目前的版本）。"""
    for p in psutil.process_iter(["name", "exe"]):
        try:
            if p.info["name"] == ROBLOX_EXE and p.info["exe"] and os.path.isfile(p.info["exe"]):
                return p.info["exe"]
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return None


def _installed_candidates() -> list[str]:
    patterns = []
    local = os.environ.get("LOCALAPPDATA", "")
    for root in _LOCAL_ROOTS:
        if local:
            patterns.append(os.path.join(local, root, "Versions", "*", ROBLOX_EXE))
    for pf in (os.environ.get("ProgramFiles(x86)", ""), os.environ.get("ProgramFiles", "")):
        if pf:
            patterns.append(os.path.join(pf, "Roblox", "Versions", "*", ROBLOX_EXE))
    found: list[str] = []
    for pat in patterns:
        found.extend(glob.glob(pat))
    return found


def find_roblox_exe() -> Optional[str]:
    """
    找出 RobloxPlayerBeta.exe：
      1. config.ini [General] RobloxExePath（手動指定）
      2. 正在執行中的 Roblox 所用的 exe
      3. 各安裝位置中「最近修改」的版本
    """
    s = _settings()
    if s is not None:
        custom = s.get("General", "RobloxExePath", "").strip().strip('"')
        if custom:
            if os.path.isfile(custom):
                return custom
            logger.warning(f"RobloxExePath 指定的檔案不存在：{custom}")

    running = _running_roblox_exe()
    if running:
        return running

    cands = _installed_candidates()
    if not cands:
        return None
    return max(cands, key=lambda f: os.path.getmtime(f))


def direct_launch_enabled() -> bool:
    s = _settings()
    return True if s is None else s.get_bool("General", "DirectLaunch", True)


def launch_roblox(launcher_url: str) -> tuple[Optional[int], str]:
    """
    啟動 Roblox 並載入 launcher_url（roblox-player: URI）。
    回傳 (pid 或 None, 說明)。pid 為 None 表示改用了協定處理器（os.startfile），無法取得 pid。
    """
    if sys.platform != "win32":
        subprocess.Popen(["xdg-open", launcher_url])
        return None, "xdg-open"

    from manager.multi_roblox import prepare_for_launch, track_roblox_pid

    exe = find_roblox_exe() if direct_launch_enabled() else None
    if exe is None:
        if direct_launch_enabled():
            logger.warning("找不到 RobloxPlayerBeta.exe，改用協定處理器啟動")
        prepare_for_launch()
        os.startfile(launcher_url)
        return None, "protocol"

    prepare_for_launch()
    proc = subprocess.Popen(
        [exe, launcher_url],
        cwd=os.path.dirname(exe),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=_DETACHED_PROCESS | _CREATE_NEW_PROCESS_GROUP,
        close_fds=True,
    )
    track_roblox_pid(proc.pid)
    logger.info(f"直接啟動 Roblox pid={proc.pid} ({exe})")

    try:
        from manager.resource_limits import apply_defaults
        apply_defaults(proc.pid)
    except Exception as e:  # 限制失敗不影響啟動
        logger.warning(f"套用預設資源限制失敗: {e}")
    try:
        from manager.window_layout import schedule_layout
        schedule_layout(proc.pid)  # 設定未啟用時什麼都不做
    except Exception as e:
        logger.warning(f"排程視窗大小失敗: {e}")
    return proc.pid, "direct"
