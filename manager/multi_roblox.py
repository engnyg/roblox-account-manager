"""
Multi-Roblox：解除單一進程限制（移除 Mutex）。
對應 C# AccountManager 中的 rbxMultiMutex 邏輯。
"""

from __future__ import annotations

import ctypes
import ctypes.wintypes
import sys

from loguru import logger


def is_windows() -> bool:
    return sys.platform == "win32"


_mutex_handle = None


def enable_multi_roblox() -> bool:
    """
    在 Windows 上佔用 Roblox 的 Mutex 名稱，使多個實例可同時運行。
    返回 True 表示成功。
    """
    global _mutex_handle
    if not is_windows():
        logger.warning("Multi-Roblox is only supported on Windows")
        return False

    if _mutex_handle is not None:
        return True

    try:
        kernel32 = ctypes.windll.kernel32
        # Roblox 使用的 mutex 名稱
        mutex_name = "ROBLOX_singletonMutex"
        handle = kernel32.CreateMutexW(None, True, mutex_name)
        if handle:
            _mutex_handle = handle
            logger.info("Multi-Roblox enabled (Mutex acquired)")
            return True
        logger.warning("Failed to acquire Roblox mutex")
        return False
    except Exception as e:
        logger.error(f"enable_multi_roblox: {e}")
        return False


def disable_multi_roblox():
    """釋放 Mutex。"""
    global _mutex_handle
    if _mutex_handle is not None and is_windows():
        try:
            ctypes.windll.kernel32.CloseHandle(_mutex_handle)
        except Exception:
            pass
        _mutex_handle = None
        logger.info("Multi-Roblox disabled")
