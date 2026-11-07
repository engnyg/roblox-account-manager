"""
Roblox 視窗大小 / 排列。

啟動後等 Roblox 的視窗出現，把它調成指定大小；可選擇自動排成格狀（每個實例佔一格，
視窗關閉後空出的格子會被下一個實例使用）。也能對已在執行的實例一鍵套用。

注意：Roblox 若在「全螢幕」模式（F11）就不吃外部調整；請用視窗模式。
設定存在 config.ini 的 [Window]：ApplyOnLaunch / Width / Height / Tile。
"""

from __future__ import annotations

import ctypes
import sys
import threading
import time
from ctypes import wintypes
from dataclasses import dataclass
from typing import Iterable, Optional

import psutil
from loguru import logger

MIN_SIZE = 200
MAX_SIZE = 8000

_SW_RESTORE = 9
_SWP_NOZORDER = 0x0004
_SWP_NOMOVE = 0x0002
_SWP_NOACTIVATE = 0x0010
_SPI_GETWORKAREA = 0x0030


@dataclass(frozen=True)
class WindowSettings:
    apply_on_launch: bool = False
    width: int = 800
    height: int = 600
    tile: bool = True


def clamp_size(width: int, height: int) -> tuple[int, int]:
    return (max(MIN_SIZE, min(MAX_SIZE, int(width))), max(MIN_SIZE, min(MAX_SIZE, int(height))))


def load_settings() -> WindowSettings:
    try:
        from core.settings import get_settings
        s = get_settings()
        w, h = clamp_size(s.get_int("Window", "Width", 800), s.get_int("Window", "Height", 600))
        return WindowSettings(
            apply_on_launch=s.get_bool("Window", "ApplyOnLaunch", False),
            width=w, height=h, tile=s.get_bool("Window", "Tile", True),
        )
    except Exception:
        return WindowSettings()


def save_settings(ws: WindowSettings) -> None:
    from core.settings import get_settings
    s = get_settings()
    s.set("Window", "ApplyOnLaunch", "true" if ws.apply_on_launch else "false")
    s.set("Window", "Width", str(ws.width))
    s.set("Window", "Height", str(ws.height))
    s.set("Window", "Tile", "true" if ws.tile else "false")


# --------------------------------------------------------------------------- #
# Win32
# --------------------------------------------------------------------------- #

_user32 = None


def _u():
    global _user32
    if _user32 is None:
        u = ctypes.WinDLL("user32", use_last_error=True)
        u.EnumWindows.argtypes = [ctypes.c_void_p, wintypes.LPARAM]
        u.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        u.GetWindowThreadProcessId.restype = wintypes.DWORD
        u.IsWindowVisible.argtypes = [wintypes.HWND]
        u.IsZoomed.argtypes = [wintypes.HWND]
        u.IsWindow.argtypes = [wintypes.HWND]
        u.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
        u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
        u.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        u.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        u.SetWindowPos.argtypes = [
            wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT,
        ]
        u.SetWindowPos.restype = wintypes.BOOL
        u.SystemParametersInfoW.argtypes = [wintypes.UINT, wintypes.UINT, ctypes.c_void_p, wintypes.UINT]
        _user32 = u
    return _user32


_WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)


def find_window(pid: int) -> Optional[int]:
    """找出 pid 的主視窗（可見的頂層視窗；優先 Roblox 的 WINDOWSCLIENT 類別）。"""
    if sys.platform != "win32":
        return None
    u = _u()
    found: list[tuple[int, int]] = []  # (priority, hwnd)

    def cb(hwnd, _lparam):
        p = wintypes.DWORD()
        u.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value != pid or not u.IsWindowVisible(hwnd):
            return True
        rc = wintypes.RECT()
        u.GetWindowRect(hwnd, ctypes.byref(rc))
        if rc.right - rc.left < 100 or rc.bottom - rc.top < 100:
            return True
        buf = ctypes.create_unicode_buffer(64)
        u.GetClassNameW(hwnd, buf, 64)
        prio = 2 if buf.value == "WINDOWSCLIENT" else (1 if u.GetWindowTextLengthW(hwnd) > 0 else 0)
        found.append((prio, hwnd))
        return True

    u.EnumWindows(_WNDENUMPROC(cb), 0)
    if not found:
        return None
    found.sort(reverse=True)
    return found[0][1]


def _work_area() -> tuple[int, int, int, int]:
    rc = wintypes.RECT()
    if _u().SystemParametersInfoW(_SPI_GETWORKAREA, 0, ctypes.byref(rc), 0):
        return rc.left, rc.top, rc.right, rc.bottom
    return 0, 0, 1920, 1040


# --------------------------------------------------------------------------- #
# 格狀排列
# --------------------------------------------------------------------------- #

_lock = threading.Lock()
_slots: dict[int, int] = {}  # pid -> 格子編號


def _alloc_slot(pid: int) -> int:
    with _lock:
        for p in list(_slots):
            if not psutil.pid_exists(p):
                del _slots[p]
        if pid in _slots:
            return _slots[pid]
        used = set(_slots.values())
        n = 0
        while n in used:
            n += 1
        _slots[pid] = n
        return n


def slot_position(slot: int, width: int, height: int) -> tuple[int, int]:
    left, top, right, bottom = _work_area()
    cols = max(1, (right - left) // width)
    rows = max(1, (bottom - top) // height)
    s = slot % (cols * rows)
    # 超過一整個螢幕後從頭疊上去，稍微錯開
    shift = (slot // (cols * rows)) * 32
    return left + (s % cols) * width + shift, top + (s // cols) * height + shift


def apply_layout(pid: int, width: int, height: int, tile: bool, slot: Optional[int] = None) -> bool:
    """對 pid 的視窗套用大小（tile=True 時一併移到格子位置）。找不到視窗回傳 False。"""
    if sys.platform != "win32":
        return False
    width, height = clamp_size(width, height)
    hwnd = find_window(pid)
    if not hwnd:
        return False
    u = _u()
    if u.IsZoomed(hwnd):
        u.ShowWindow(hwnd, _SW_RESTORE)
    flags = _SWP_NOZORDER | _SWP_NOACTIVATE
    x = y = 0
    if tile:
        x, y = slot_position(slot if slot is not None else _alloc_slot(pid), width, height)
    else:
        flags |= _SWP_NOMOVE
    ok = bool(u.SetWindowPos(hwnd, 0, x, y, width, height, flags))
    if not ok:
        logger.warning(f"pid {pid} 調整視窗失敗（Win32 錯誤 {ctypes.get_last_error()}）")
    return ok


def schedule_layout(pid: int, ws: Optional[WindowSettings] = None, timeout: float = 90.0) -> None:
    """背景等視窗出現後套用設定；Roblox 載入完成後常會自己再調一次大小，所以稍後再套一次。"""
    ws = ws or load_settings()
    if not ws.apply_on_launch:
        return

    def run():
        deadline = time.time() + timeout
        slot = _alloc_slot(pid) if ws.tile else None
        applied = False
        while time.time() < deadline and psutil.pid_exists(pid):
            if apply_layout(pid, ws.width, ws.height, ws.tile, slot):
                applied = True
                break
            time.sleep(0.5)
        if not applied:
            logger.warning(f"pid {pid}：等不到視窗，未套用大小")
            return
        logger.info(f"pid {pid} 視窗大小 {ws.width}x{ws.height}" + (f"，格子 {slot}" if ws.tile else ""))
        for delay in (3.0, 6.0):
            time.sleep(delay)
            if not psutil.pid_exists(pid):
                return
            apply_layout(pid, ws.width, ws.height, ws.tile, slot)

    threading.Thread(target=run, daemon=True, name=f"window-layout-{pid}").start()


def arrange_all(pids: Iterable[int], ws: WindowSettings) -> tuple[int, int]:
    """對一批實例套用（重新依序編格子）。回傳 (成功數, 總數)。"""
    pids = sorted(pids)
    with _lock:
        _slots.clear()
    ok = 0
    for i, pid in enumerate(pids):
        if apply_layout(pid, ws.width, ws.height, ws.tile, i if ws.tile else None):
            ok += 1
            if ws.tile:
                with _lock:
                    _slots[pid] = i
    return ok, len(pids)
