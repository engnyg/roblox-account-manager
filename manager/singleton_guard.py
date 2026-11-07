"""
關閉執行中 Roblox 的 ROBLOX_singletonEvent handle（讓新實例不會把舊實例擠掉）。

新版 Roblox 客戶端啟動時會透過具名 event 通知「舊的」實例關閉
（日誌特徵：WindowsAppReloadTelemetry EventWaitResultUpdate →
 Systray: Received close main window request）。要多開，除了持有
ROBLOX_singletonMutex 之外，還得讓舊實例「聽不到」這個 event：
  1. 從外部關閉舊實例裡指向該 event 的 handle（本模組）。
  2. 之後用同名的 Mutex 佔住這個名稱，新的實例就建立不了 event
     （見 manager/multi_roblox.py 的 _MutexKeeper）。

----------------------------------------------------------------------------
Derived from MultiRoblox-RAM (RobloxNative.cs: HandleCloser / RunMutex)
Required Notice: Copyright (c) 2026 PookiePepelsss
(https://github.com/PookiePepelsss/MultiRoblox-RAM)
Licensed under the PolyForm Noncommercial License 1.0.0
(https://polyformproject.org/licenses/noncommercial/1.0.0).
本移植版僅供非商業用途；詳見專案根目錄 THIRD_PARTY_NOTICES.md。
改良：先用 ObjectTypeIndex 只挑出 Event 類型的 handle 才查名稱，
避免對 named pipe 等物件呼叫 NtQueryObject 造成卡死。
----------------------------------------------------------------------------
"""

from __future__ import annotations

import ctypes
import os
import sys
from ctypes import wintypes
from typing import Iterable, Optional

import psutil
from loguru import logger

SINGLETON_EVENT_NAME = "ROBLOX_singletonEvent"
SINGLETON_MUTEX_NAME = "ROBLOX_singletonMutex"
ROBLOX_PROCESS_NAME = "RobloxPlayerBeta.exe"

_SYSTEM_EXTENDED_HANDLE_INFORMATION = 64
_STATUS_INFO_LENGTH_MISMATCH = 0xC0000004
_OBJECT_NAME_INFORMATION = 1
_PROCESS_DUP_HANDLE = 0x0040
_DUPLICATE_CLOSE_SOURCE = 0x1
_DUPLICATE_SAME_ACCESS = 0x2


class _HandleEntry(ctypes.Structure):
    # SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX（x64 共 40 bytes）
    _fields_ = [
        ("Object", ctypes.c_void_p),
        ("UniqueProcessId", ctypes.c_size_t),
        ("HandleValue", ctypes.c_size_t),
        ("GrantedAccess", ctypes.c_ulong),
        ("CreatorBackTraceIndex", ctypes.c_ushort),
        ("ObjectTypeIndex", ctypes.c_ushort),
        ("HandleAttributes", ctypes.c_ulong),
        ("Reserved", ctypes.c_ulong),
    ]


def _libs():
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    nt = ctypes.WinDLL("ntdll")

    k.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    k.OpenProcess.restype = wintypes.HANDLE
    k.CloseHandle.argtypes = [wintypes.HANDLE]
    k.CloseHandle.restype = wintypes.BOOL
    k.GetCurrentProcess.restype = wintypes.HANDLE
    k.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
    k.CreateEventW.restype = wintypes.HANDLE
    k.DuplicateHandle.argtypes = [
        wintypes.HANDLE, wintypes.HANDLE, wintypes.HANDLE,
        ctypes.POINTER(wintypes.HANDLE), wintypes.DWORD, wintypes.BOOL, wintypes.DWORD,
    ]
    k.DuplicateHandle.restype = wintypes.BOOL

    nt.NtQuerySystemInformation.argtypes = [
        ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
    ]
    nt.NtQuerySystemInformation.restype = ctypes.c_long
    nt.NtQueryObject.argtypes = [
        wintypes.HANDLE, ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
    ]
    nt.NtQueryObject.restype = ctypes.c_long
    return k, nt


def _snapshot_handles(nt):
    """回傳 (buffer, entries)；buffer 必須跟著 entries 一起保留。"""
    size = 1 << 20
    while True:
        buf = ctypes.create_string_buffer(size)
        needed = ctypes.c_ulong(0)
        status = nt.NtQuerySystemInformation(
            _SYSTEM_EXTENDED_HANDLE_INFORMATION, buf, size, ctypes.byref(needed)
        ) & 0xFFFFFFFF
        if status == 0:
            break
        if status == _STATUS_INFO_LENGTH_MISMATCH:
            size = max(size * 2, needed.value + (1 << 16))
            continue
        raise OSError(f"NtQuerySystemInformation failed: {status:#x}")
    count = ctypes.c_size_t.from_buffer(buf, 0).value
    header = ctypes.sizeof(ctypes.c_size_t) * 2  # NumberOfHandles + Reserved
    entries = (_HandleEntry * count).from_buffer(buf, header)
    return buf, entries


def _query_name(nt, handle) -> str:
    """取得物件名稱；失敗回傳空字串。"""
    buf = ctypes.create_string_buffer(2048)
    ret = ctypes.c_ulong(0)
    status = nt.NtQueryObject(handle, _OBJECT_NAME_INFORMATION, buf, ctypes.sizeof(buf), ctypes.byref(ret))
    if status < 0:
        return ""
    length = ctypes.c_ushort.from_buffer(buf, 0).value
    if length == 0:
        return ""
    ptr_offset = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 4
    ptr = ctypes.c_void_p.from_buffer(buf, ptr_offset).value
    if not ptr:
        return ""
    return ctypes.string_at(ptr, length).decode("utf-16-le", errors="ignore")


def close_singleton_event_handles(
    exempt_pids: Optional[Iterable[int]] = None,
    process_name: str = ROBLOX_PROCESS_NAME,
    name_substr: str = SINGLETON_EVENT_NAME,
) -> int:
    """
    關閉所有執行中 Roblox 內名稱含 name_substr 的 Event handle，回傳關閉的數量。

    exempt_pids：由本程式啟動並追蹤中的實例，跳過不處理（避免影響它們）。
    process_name / name_substr 可替換，方便用替身行程測試而不碰真正的 Roblox。
    """
    if sys.platform != "win32":
        return 0

    exempt = set(exempt_pids or ())
    target_pids = set()
    for p in psutil.process_iter(["pid", "name"]):
        try:
            if p.info["name"] == process_name and p.info["pid"] not in exempt:
                target_pids.add(p.info["pid"])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    if not target_pids:
        return 0

    k, nt = _libs()

    # 先建立一個匿名 event，從 handle 表裡找出 Event 類型的 ObjectTypeIndex，
    # 之後只對這個類型查名稱（對其他類型呼叫 NtQueryObject 有卡死風險）。
    probe = k.CreateEventW(None, True, False, None)
    if not probe:
        return 0
    my_pid = os.getpid()
    event_type_index = None
    try:
        buf, entries = _snapshot_handles(nt)
        for e in entries:
            if e.UniqueProcessId == my_pid and e.HandleValue == int(probe):
                event_type_index = e.ObjectTypeIndex
                break
    except OSError as ex:
        logger.warning(f"singleton_guard: {ex}")
        return 0
    finally:
        k.CloseHandle(probe)
    if event_type_index is None:
        logger.warning("singleton_guard: 找不到 Event 物件類型索引，略過")
        return 0

    closed = 0
    self_proc = k.GetCurrentProcess()
    proc_handles: dict[int, int] = {}
    try:
        for e in entries:
            pid = e.UniqueProcessId
            if pid not in target_pids or e.ObjectTypeIndex != event_type_index:
                continue
            src = proc_handles.get(pid)
            if src is None:
                src = k.OpenProcess(_PROCESS_DUP_HANDLE, False, pid)
                if not src:
                    proc_handles[pid] = 0
                    logger.debug(f"singleton_guard: 無法開啟 pid {pid}（err {ctypes.get_last_error()}）")
                    continue
                proc_handles[pid] = src
            if not src:
                continue

            dup = wintypes.HANDLE()
            if not k.DuplicateHandle(src, e.HandleValue, self_proc, ctypes.byref(dup), 0, False, _DUPLICATE_SAME_ACCESS):
                continue
            try:
                name = _query_name(nt, dup)
            finally:
                k.CloseHandle(dup)
            if name_substr in name:
                # target 為 NULL + DUPLICATE_CLOSE_SOURCE = 只從來源行程關閉該 handle
                if k.DuplicateHandle(src, e.HandleValue, None, None, 0, False, _DUPLICATE_CLOSE_SOURCE):
                    closed += 1
                    logger.info(f"singleton_guard: 已關閉 pid {pid} 的 {name_substr} handle")
    finally:
        for src in proc_handles.values():
            if src:
                k.CloseHandle(src)
    del buf
    return closed
