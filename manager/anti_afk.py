"""
防掛機（Anti-AFK）。
移植自 MultiRoblox-RAM 的 Anti-AFK 概念：定期對所有 Roblox 視窗傳送
無害的按鍵訊息（不搶焦點），避免帳號因閒置太久被 Roblox 判定並踢出遊戲。
"""

from __future__ import annotations

import sys
import threading
from typing import Optional

from loguru import logger

from manager.roblox_process import get_roblox_processes


def is_windows() -> bool:
    return sys.platform == "win32"


def _find_hwnds_for_pid(pid: int) -> list[int]:
    if not is_windows():
        return []
    import ctypes
    import ctypes.wintypes as wt

    user32 = ctypes.windll.user32
    hwnds: list[int] = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def _enum(hwnd, _lparam):
        pid_out = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid_out))
        if pid_out.value == pid and user32.IsWindowVisible(hwnd):
            hwnds.append(hwnd)
        return True

    user32.EnumWindows(_enum, 0)
    return hwnds


def _send_afk_key(hwnd: int):
    """對指定視窗送出 Space 鍵 down/up（PostMessage，不搶焦點）。"""
    import ctypes
    WM_KEYDOWN = 0x0100
    WM_KEYUP = 0x0101
    VK_SPACE = 0x20
    user32 = ctypes.windll.user32
    user32.PostMessageW(hwnd, WM_KEYDOWN, VK_SPACE, 0)
    user32.PostMessageW(hwnd, WM_KEYUP, VK_SPACE, 0x00C00001)


class AntiAFK:
    """對所有執行中的 Roblox 進程定期送出防閒置按鍵。"""

    def __init__(self, interval: float = 120.0):
        self.interval = interval
        self._timer: Optional[threading.Timer] = None
        self._running = False

    @property
    def is_running(self) -> bool:
        return self._running

    def start(self):
        if self._running or not is_windows():
            return
        self._running = True
        self._schedule()
        logger.info(f"Anti-AFK started (interval={self.interval}s)")

    def stop(self):
        self._running = False
        if self._timer:
            self._timer.cancel()
            self._timer = None
        logger.info("Anti-AFK stopped")

    def _schedule(self):
        if not self._running:
            return
        self._timer = threading.Timer(self.interval, self._tick)
        self._timer.daemon = True
        self._timer.start()

    def _tick(self):
        try:
            self._send_all()
        except Exception as e:
            logger.error(f"Anti-AFK tick error: {e}")
        self._schedule()

    def _send_all(self):
        count = 0
        for proc in get_roblox_processes():
            for hwnd in _find_hwnds_for_pid(proc.pid):
                _send_afk_key(hwnd)
                count += 1
        if count:
            logger.debug(f"Anti-AFK: sent keepalive key to {count} window(s)")
