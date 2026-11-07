"""
每個 Roblox 實例的 CPU / RAM 限制（Windows）。

CPU：把行程放進 Job Object，啟用 CPU rate control 的 hard cap。
     cpu_percent 是「佔整台電腦 CPU（所有核心）的百分比」，到達上限時行程會被節流。
     例：8 核心的電腦，12.5% ≈ 一個核心。
RAM：用 SetProcessWorkingSetSizeEx 設定 working set「硬上限」。超過上限的頁面會被換出到
     分頁檔 → 實體記憶體佔用被壓低、不會讓 Roblox 崩潰，但限制太低會變卡
     （下限 256 MB）。這不是 commit limit：commit limit 一旦超過會讓配置失敗而崩潰。
     工作管理員的「記憶體（使用中的工作集）」會反映上限；「已認可」不會變。
清除限制：CPU 設回 100% 上限、RAM 取消硬上限。
"""

from __future__ import annotations

import ctypes
import sys
import threading
from ctypes import wintypes
from dataclasses import dataclass
from typing import Optional

import psutil
from loguru import logger

MIN_MEMORY_MB = 256
MAX_MEMORY_MB = 262144

_PROCESS_SET_QUOTA = 0x0100
_PROCESS_TERMINATE = 0x0001
_PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

_JobObjectCpuRateControlInformation = 15
_CPU_RATE_CONTROL_ENABLE = 0x1
_CPU_RATE_CONTROL_HARD_CAP = 0x4

_QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x2
_QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x4
_QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x8


class LimitError(Exception):
    pass


@dataclass(frozen=True)
class Limits:
    cpu_percent: int = 0   # 1–100；0 = 不限制
    memory_mb: int = 0     # MIN_MEMORY_MB 以上；0 = 不限制

    @property
    def active(self) -> bool:
        return self.cpu_percent > 0 or self.memory_mb > 0

    def describe(self) -> str:
        parts = []
        if self.cpu_percent > 0:
            parts.append(f"CPU {self.cpu_percent}%")
        if self.memory_mb > 0:
            parts.append(f"RAM {self.memory_mb} MB")
        return " / ".join(parts)


class _CpuRateInfo(ctypes.Structure):
    _fields_ = [("ControlFlags", wintypes.DWORD), ("CpuRate", wintypes.DWORD)]


def _k():
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    k.OpenProcess.restype = wintypes.HANDLE
    k.CloseHandle.argtypes = [wintypes.HANDLE]
    k.CloseHandle.restype = wintypes.BOOL
    k.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
    k.CreateJobObjectW.restype = wintypes.HANDLE
    k.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    k.AssignProcessToJobObject.restype = wintypes.BOOL
    k.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    k.SetInformationJobObject.restype = wintypes.BOOL
    k.SetProcessWorkingSetSizeEx.argtypes = [wintypes.HANDLE, ctypes.c_size_t, ctypes.c_size_t, wintypes.DWORD]
    k.SetProcessWorkingSetSizeEx.restype = wintypes.BOOL
    return k


_lock = threading.Lock()
# pid -> {"create_time": float, "job": handle|None, "limits": Limits}
_state: dict[int, dict] = {}
_kernel = None


def _kernel32():
    global _kernel
    if _kernel is None:
        _kernel = _k()
    return _kernel


def _err(what: str) -> LimitError:
    code = ctypes.get_last_error()
    return LimitError(f"{what} 失敗（Win32 錯誤 {code}）")


def clamp_limits(cpu_percent: int, memory_mb: int) -> Limits:
    cpu = max(0, min(100, int(cpu_percent or 0)))
    mem = int(memory_mb or 0)
    if mem > 0:
        mem = max(MIN_MEMORY_MB, min(MAX_MEMORY_MB, mem))
    return Limits(cpu, mem)


def _prune_dead_locked():
    k = _kernel32()
    for pid in list(_state):
        ent = _state[pid]
        try:
            alive = psutil.Process(pid).create_time() == ent["create_time"]
        except psutil.NoSuchProcess:
            alive = False
        if not alive:
            if ent.get("job"):
                k.CloseHandle(ent["job"])
            del _state[pid]


def _set_cpu_rate(job, percent: int):
    k = _kernel32()
    # CpuRate 單位為「整體 CPU 的 1/100 個百分點」（1–10000）
    info = _CpuRateInfo(
        _CPU_RATE_CONTROL_ENABLE | _CPU_RATE_CONTROL_HARD_CAP,
        max(1, min(10000, percent * 100)),
    )
    if not k.SetInformationJobObject(job, _JobObjectCpuRateControlInformation, ctypes.byref(info), ctypes.sizeof(info)):
        raise _err("設定 CPU 上限")


def _set_ram_cap(hproc, memory_mb: int):
    k = _kernel32()
    if memory_mb > 0:
        size = memory_mb * 1024 * 1024
        ok = k.SetProcessWorkingSetSizeEx(
            hproc, 1024 * 1024, size,
            _QUOTA_LIMITS_HARDWS_MIN_DISABLE | _QUOTA_LIMITS_HARDWS_MAX_ENABLE,
        )
    else:
        # 取消硬上限；軟上限放到實體記憶體大小，等同不限制
        total = psutil.virtual_memory().total
        ok = k.SetProcessWorkingSetSizeEx(
            hproc, 1024 * 1024, total,
            _QUOTA_LIMITS_HARDWS_MIN_DISABLE | _QUOTA_LIMITS_HARDWS_MAX_DISABLE,
        )
    if not ok:
        raise _err("設定 RAM 上限")


def apply_limits(pid: int, limits: Limits) -> Limits:
    """
    對 pid 套用限制（Limits(0, 0) 等同清除）。成功回傳實際套用的 Limits；
    失敗拋出 LimitError。重複呼叫會更新數值。
    """
    if sys.platform != "win32":
        raise LimitError("資源限制只支援 Windows")

    k = _kernel32()
    try:
        create_time = psutil.Process(pid).create_time()
    except psutil.NoSuchProcess:
        raise LimitError(f"行程 {pid} 不存在")

    hproc = k.OpenProcess(
        _PROCESS_SET_QUOTA | _PROCESS_TERMINATE | _PROCESS_QUERY_LIMITED_INFORMATION, False, pid
    )
    if not hproc:
        raise _err(f"開啟行程 {pid}")

    try:
        with _lock:
            _prune_dead_locked()
            ent = _state.get(pid)
            prev = ent["limits"] if ent else Limits()

            # ---- CPU ----
            job = ent["job"] if ent else None
            if limits.cpu_percent > 0 or job is not None:
                if job is None:
                    job = k.CreateJobObjectW(None, None)
                    if not job:
                        raise _err("建立 Job Object")
                    if not k.AssignProcessToJobObject(job, hproc):
                        err = _err("把行程加入 Job Object")
                        k.CloseHandle(job)
                        raise err
                _set_cpu_rate(job, limits.cpu_percent if limits.cpu_percent > 0 else 100)

            # ---- RAM ----
            if limits.memory_mb > 0 or prev.memory_mb > 0:
                _set_ram_cap(hproc, limits.memory_mb)

            if job is None and not limits.active:
                _state.pop(pid, None)
            else:
                _state[pid] = {"create_time": create_time, "job": job, "limits": limits}
        logger.info(f"pid {pid} 資源限制：{limits.describe() or '已清除'}")
        return limits
    finally:
        k.CloseHandle(hproc)


def clear_limits(pid: int) -> None:
    apply_limits(pid, Limits())


def get_applied(pid: int) -> Limits:
    with _lock:
        ent = _state.get(pid)
        if ent:
            try:
                if psutil.Process(pid).create_time() == ent["create_time"]:
                    return ent["limits"]
            except psutil.NoSuchProcess:
                pass
    return Limits()


# --------------------------------------------------------------------------- #
# 預設值（config.ini [Limits]）與自動套用
# --------------------------------------------------------------------------- #

def default_limits() -> Limits:
    try:
        from core.settings import get_settings
        s = get_settings()
        return clamp_limits(s.get_int("Limits", "DefaultCpuPercent", 0), s.get_int("Limits", "DefaultMemoryMB", 0))
    except Exception:
        return Limits()


def auto_apply_enabled() -> bool:
    try:
        from core.settings import get_settings
        return get_settings().get_bool("Limits", "AutoApply", True)
    except Exception:
        return True


def save_defaults(limits: Limits, auto_apply: bool) -> None:
    from core.settings import get_settings
    s = get_settings()
    s.set("Limits", "DefaultCpuPercent", str(limits.cpu_percent))
    s.set("Limits", "DefaultMemoryMB", str(limits.memory_mb))
    s.set("Limits", "AutoApply", "true" if auto_apply else "false")


def apply_defaults(pid: int) -> Optional[Limits]:
    """新啟動的實例：若啟用自動套用且預設值不為空，就套用。"""
    if not auto_apply_enabled():
        return None
    limits = default_limits()
    if not limits.active:
        return None
    # 剛啟動的行程可能還沒建立完成，簡單重試幾次
    import time
    last: Optional[Exception] = None
    for _ in range(5):
        try:
            return apply_limits(pid, limits)
        except LimitError as e:
            last = e
            time.sleep(0.4)
    logger.warning(f"pid {pid} 套用預設限制失敗：{last}")
    return None
