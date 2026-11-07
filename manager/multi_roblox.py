"""
Multi-Roblox：解除單一進程限制。
對應 C# AccountManager 中的 rbxMultiMutex 邏輯。

新版 Roblox 的單例機制有兩個物件：
  - ROBLOX_singletonMutex：新實例啟動時嘗試取得；取不到就不會去動舊視窗。
    管理器必須「真正擁有」它（光開 handle 沒用，見下方說明）。
  - ROBLOX_singletonEvent：新實例用它通知舊實例關閉（日誌：Received close main window request）。
    處理方式：啟動前從舊實例內關閉它的 handle（manager/singleton_guard.py），
    再用同名的 Mutex 佔住這個名稱，之後的實例就建不出這個 event。

過去的寫法 `CreateMutexW(None, True, name)` 有個陷阱：mutex 已經存在時（例如上一個管理器
行程結束、而 Roblox 還握著 handle，mutex 變成 abandoned），initial-owner 參數會被忽略，
我們其實沒有擁有權，卻仍回報「已取得」→ 之後的 Roblox 搶到 mutex，就把舊的關掉了。

mutex 的擁有權屬於「執行緒」，所以由一條常駐的背景執行緒負責：不斷嘗試 WaitForSingleObject，
一旦取得（含 WAIT_ABANDONED）就一直持有到停用為止。

event 名稱佔位與啟動前清除 handle 的做法，參考 MultiRoblox-RAM
（Required Notice: Copyright (c) 2026 PookiePepelsss，PolyForm Noncommercial 1.0.0；
 詳見 THIRD_PARTY_NOTICES.md）。
"""

from __future__ import annotations

import sys
import threading
import time

import psutil
from loguru import logger

from manager.singleton_guard import (
    SINGLETON_EVENT_NAME, SINGLETON_MUTEX_NAME, close_singleton_event_handles,
)

_MUTEX_NAME = SINGLETON_MUTEX_NAME
_EVENT_NAME = SINGLETON_EVENT_NAME

_WAIT_OBJECT_0 = 0x00000000
_WAIT_ABANDONED = 0x00000080
_WAIT_TIMEOUT = 0x00000102

# 狀態
STATE_OFF = "off"          # 未啟用
STATE_WAITING = "waiting"  # 已啟用，但 mutex 目前被別的行程（通常是執行中的 Roblox）擁有
STATE_ACTIVE = "active"    # 已擁有 mutex，多開生效


def is_windows() -> bool:
    return sys.platform == "win32"


def _kernel32():
    import ctypes
    from ctypes import wintypes

    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    k.CreateMutexW.restype = wintypes.HANDLE
    k.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    k.WaitForSingleObject.restype = wintypes.DWORD
    k.ReleaseMutex.argtypes = [wintypes.HANDLE]
    k.ReleaseMutex.restype = wintypes.BOOL
    k.CloseHandle.argtypes = [wintypes.HANDLE]
    k.CloseHandle.restype = wintypes.BOOL
    return k


class _MutexKeeper(threading.Thread):
    """常駐執行緒：取得並持有 singleton mutex（擁有權綁定在這條執行緒上），並佔住 event 名稱。"""

    def __init__(self, mutex_name: str, event_name: str):
        super().__init__(daemon=True, name="multi-roblox-mutex")
        self._mutex_name = mutex_name
        self._event_name = event_name
        self._halt = threading.Event()
        self.owned = threading.Event()
        self.squatted = threading.Event()
        self.error: str | None = None

    def stop(self):
        self._halt.set()

    def run(self):
        try:
            k = _kernel32()
        except Exception as e:  # pragma: no cover
            self.error = str(e)
            logger.error(f"Multi-Roblox: 無法載入 kernel32: {e}")
            return

        handle = k.CreateMutexW(None, False, self._mutex_name)
        if not handle:
            self.error = "CreateMutexW failed"
            logger.error("Multi-Roblox: CreateMutexW 失敗")
            return

        squat_handle = None
        announced_wait = False
        try:
            while not self._halt.is_set():
                if not self.owned.is_set():
                    result = k.WaitForSingleObject(handle, 200)
                    if result in (_WAIT_OBJECT_0, _WAIT_ABANDONED):
                        self.owned.set()
                        how = "接手被遺棄的 mutex" if result == _WAIT_ABANDONED else "取得 mutex"
                        logger.info(f"Multi-Roblox enabled ({how})")
                    elif result == _WAIT_TIMEOUT:
                        if not announced_wait:
                            announced_wait = True
                            logger.warning(
                                "Multi-Roblox: mutex 目前被其他行程（多半是執行中的 Roblox）擁有，"
                                "多開尚未生效；該 Roblox 關閉後會自動接手"
                            )
                    else:
                        self.error = f"WaitForSingleObject 回傳 {result:#x}"
                        logger.error(f"Multi-Roblox: {self.error}")
                        break
                else:
                    self._halt.wait(0.2)

                # 佔住 event 名稱：只要名稱還被「Event」物件佔用（舊 Roblox 還握著），
                # 建立同名 Mutex 會失敗（ERROR_INVALID_HANDLE）；等 handle 被關閉後才會成功。
                if squat_handle is None:
                    squat_handle = k.CreateMutexW(None, False, self._event_name) or None
                    if squat_handle:
                        self.squatted.set()
                        logger.info(f"Multi-Roblox: 已佔住 {self._event_name} 名稱")
        finally:
            if self.owned.is_set():
                k.ReleaseMutex(handle)
                self.owned.clear()
            k.CloseHandle(handle)
            if squat_handle:
                k.CloseHandle(squat_handle)
            self.squatted.clear()


_keeper: _MutexKeeper | None = None
_lock = threading.Lock()
_tracked_pids: set[int] = set()


def enable_multi_roblox(wait: float = 1.0) -> bool:
    """
    啟用多開。返回 True 表示「已真正擁有 mutex」，多開立即生效。
    返回 False 表示尚未擁有（mutex 被其他行程擁有）；背景執行緒會持續嘗試並在取得後生效，
    可用 multi_roblox_state() 查詢目前狀態。
    """
    global _keeper
    if not is_windows():
        logger.warning("Multi-Roblox is only supported on Windows")
        return False

    with _lock:
        if _keeper is None or not _keeper.is_alive():
            _keeper = _MutexKeeper(_MUTEX_NAME, _EVENT_NAME)
            _keeper.start()
        keeper = _keeper

    deadline = time.time() + wait
    while time.time() < deadline:
        if keeper.owned.is_set():
            return True
        if not keeper.is_alive():
            return False
        time.sleep(0.02)
    return keeper.owned.is_set()


def multi_roblox_state() -> str:
    """回傳 STATE_OFF / STATE_WAITING / STATE_ACTIVE。"""
    keeper = _keeper
    if keeper is None or not keeper.is_alive():
        return STATE_OFF
    return STATE_ACTIVE if keeper.owned.is_set() else STATE_WAITING


def is_event_squatted() -> bool:
    keeper = _keeper
    return bool(keeper and keeper.is_alive() and keeper.squatted.is_set())


def disable_multi_roblox():
    """釋放 Mutex。"""
    global _keeper
    with _lock:
        keeper, _keeper = _keeper, None
    if keeper is not None:
        keeper.stop()
        keeper.join(timeout=2.0)
        logger.info("Multi-Roblox disabled")


# --------------------------------------------------------------------------- #
# 啟動前準備 / 追蹤我們啟動的行程
# --------------------------------------------------------------------------- #

def track_roblox_pid(pid: int):
    """記錄由本程式啟動的 Roblox pid；啟動前清除 handle 時會跳過它們。"""
    _tracked_pids.add(pid)


def _live_tracked_pids() -> set[int]:
    alive = {p for p in _tracked_pids if psutil.pid_exists(p)}
    _tracked_pids.intersection_update(alive)
    return alive


def prepare_for_launch(squat_wait: float = 1.5) -> int:
    """
    每次啟動 Roblox「之前一刻」呼叫：
      1. 關閉執行中（非本程式追蹤）Roblox 的 ROBLOX_singletonEvent handle，
         它們才不會被新實例通知關閉；
      2. 等待 event 名稱被佔住（新客戶端啟動後一兩秒就會自己建立 event，必須先佔位）。
    必須在啟動前一刻做：太早做，中間又有新實例冒出新的 handle 就漏掉了。
    回傳關閉的 handle 數量。多開未啟用時什麼都不做。
    """
    if not is_windows() or _keeper is None or not _keeper.is_alive():
        return 0
    try:
        closed = close_singleton_event_handles(exempt_pids=_live_tracked_pids())
    except Exception as e:
        logger.error(f"prepare_for_launch: 關閉 singleton handle 失敗: {e}")
        closed = 0

    deadline = time.time() + squat_wait
    while time.time() < deadline and not is_event_squatted():
        time.sleep(0.05)
    if not is_event_squatted():
        logger.warning("Multi-Roblox: 尚未能佔住 event 名稱（可能仍有 Roblox 握著該 event），舊實例可能被關閉")
    return closed
