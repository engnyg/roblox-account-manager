"""
Roblox 進程監控 + 日誌追蹤。
對應 C# Classes/RobloxWatcher.cs。
"""

from __future__ import annotations

import ctypes
import ctypes.wintypes
import threading
from datetime import datetime
from typing import Callable, Optional, TYPE_CHECKING

from loguru import logger

from manager.roblox_process import get_roblox_processes, RobloxProcess

if TYPE_CHECKING:
    from core.account import Account


class RobloxWatcher:
    def __init__(
        self,
        get_accounts: Callable[[], list["Account"]],
        save_accounts: Callable,
    ):
        self._get_accounts = get_accounts
        self._save_accounts = save_accounts
        self._timer: Optional[threading.Timer] = None
        self._running = False

        # Config (mirrors C# static fields)
        self.verify_data_model = True
        self.ignore_existing = True
        self.close_if_memory_low = False
        self.memory_low_mb = 200
        self.close_if_wrong_title = False
        self.expected_title = "Roblox"
        self.remember_positions = False

        self._seen_pids: set[int] = set()
        if self.ignore_existing:
            for p in get_roblox_processes():
                self._seen_pids.add(p.pid)

    def start(self, interval: float = 0.25):
        if self._running:
            return
        self._running = True
        self._schedule(interval)
        logger.info("RobloxWatcher started")

    def stop(self):
        self._running = False
        if self._timer:
            self._timer.cancel()
        logger.info("RobloxWatcher stopped")

    def _schedule(self, interval: float):
        if not self._running:
            return
        self._timer = threading.Timer(interval, lambda: (self._tick(), self._schedule(interval)))
        self._timer.daemon = True
        self._timer.start()

    def _tick(self):
        try:
            self._check_processes()
        except Exception as e:
            logger.error(f"Watcher tick error: {e}")

    def _check_processes(self):
        processes = get_roblox_processes()
        accounts = self._get_accounts()
        account_map = {a.browser_tracker_id: a for a in accounts if a.browser_tracker_id}

        for rp in processes:
            elapsed = (datetime.now() - rp.start_time).total_seconds() if rp.start_time else 0

            if elapsed < 30:
                continue  # 剛啟動，跳過

            if self.close_if_memory_low and rp.memory_mb < self.memory_low_mb:
                logger.info(f"Killing PID {rp.pid}: low memory ({rp.memory_mb:.0f} MB)")
                rp.kill()
                continue

            if self.close_if_wrong_title and rp.window_title != self.expected_title:
                logger.info(f"Killing PID {rp.pid}: wrong title '{rp.window_title}'")
                rp.kill()
                continue

            if self.remember_positions and rp.tracker_id in account_map:
                self._save_window_position(rp, account_map[rp.tracker_id])

    def _save_window_position(self, rp: RobloxProcess, account: "Account"):
        if not self._get_window_rect:
            return
        rect = self._get_window_rect(rp.pid)
        if rect:
            x, y, w, h = rect
            account.set_field("Window_Position_X", str(x))
            account.set_field("Window_Position_Y", str(y))
            account.set_field("Window_Width", str(w))
            account.set_field("Window_Height", str(h))

    @staticmethod
    def _get_window_rect(pid: int) -> Optional[tuple[int, int, int, int]]:
        import sys
        if sys.platform != "win32":
            return None
        try:
            import psutil
            proc = psutil.Process(pid)
            hwnd = proc.pid  # We'd need EnumWindows to find hwnd by pid properly
            # Simplified: skip actual position saving unless hwnd is known
            return None
        except Exception:
            return None
