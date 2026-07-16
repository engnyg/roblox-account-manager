"""
Mixer：多開 Roblox 的統一控制面板後端。
移植自 MultiRoblox-RAM 的 Mixer 概念：
- 個別 Roblox 進程的系統音量（透過 pycaw）
- 全域 FPS 上限（沿用 manager.fps_unlocker）
- 一鍵結束全部 Roblox 進程
"""

from __future__ import annotations

import sys
from typing import Optional

from loguru import logger

from manager.roblox_process import RobloxProcess, get_roblox_processes


def is_windows() -> bool:
    return sys.platform == "win32"


def _sessions():
    if not is_windows():
        return []
    try:
        from pycaw.pycaw import AudioUtilities
        return AudioUtilities.GetAllSessions()
    except Exception as e:
        logger.debug(f"pycaw unavailable: {e}")
        return []


def get_process_volume(pid: int) -> Optional[float]:
    """回傳 0.0~1.0 之間的音量；找不到該進程的音訊工作階段則回傳 None。"""
    for session in _sessions():
        if session.Process and session.Process.pid == pid:
            try:
                return session.SimpleAudioVolume.GetMasterVolume()
            except Exception:
                return None
    return None


def set_process_volume(pid: int, volume: float) -> bool:
    """設定進程音量（0.0~1.0）。"""
    volume = max(0.0, min(1.0, volume))
    for session in _sessions():
        if session.Process and session.Process.pid == pid:
            try:
                session.SimpleAudioVolume.SetMasterVolume(volume, None)
                return True
            except Exception as e:
                logger.error(f"set_process_volume: {e}")
                return False
    return False


def set_process_muted(pid: int, muted: bool) -> bool:
    for session in _sessions():
        if session.Process and session.Process.pid == pid:
            try:
                session.SimpleAudioVolume.SetMute(1 if muted else 0, None)
                return True
            except Exception:
                return False
    return False


def kill_all_roblox() -> int:
    """結束所有正在執行的 Roblox 進程，回傳結束的數量。"""
    processes = get_roblox_processes()
    for p in processes:
        p.kill()
    logger.info(f"Mixer: killed {len(processes)} Roblox process(es)")
    return len(processes)


def list_processes_with_volume() -> list[dict]:
    """回傳 [{"process": RobloxProcess, "volume": float|None}]。"""
    result = []
    for p in get_roblox_processes():
        result.append({"process": p, "volume": get_process_volume(p.pid)})
    return result
