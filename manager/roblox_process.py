"""
單一 Roblox 進程管理。
對應 C# Classes/RobloxProcess.cs。
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional

import psutil


@dataclass
class RobloxProcess:
    pid: int
    tracker_id: str = ""
    username: str = ""
    start_time: Optional[datetime] = None
    command_line: str = ""

    @property
    def process(self) -> Optional[psutil.Process]:
        try:
            return psutil.Process(self.pid)
        except psutil.NoSuchProcess:
            return None

    @property
    def is_alive(self) -> bool:
        p = self.process
        return p is not None and p.is_running()

    @property
    def memory_mb(self) -> float:
        p = self.process
        if p:
            try:
                return p.memory_info().rss / 1024 / 1024
            except Exception:
                pass
        return 0.0

    @property
    def window_title(self) -> str:
        if p := self.process:
            try:
                return p.name()
            except Exception:
                pass
        return ""

    def kill(self):
        if p := self.process:
            try:
                p.kill()
            except Exception:
                pass


def get_roblox_processes() -> list[RobloxProcess]:
    """掃描所有正在執行的 Roblox 進程。"""
    result = []
    for proc in psutil.process_iter(["pid", "name", "cmdline", "create_time"]):
        try:
            if proc.info["name"] != "RobloxPlayerBeta.exe":
                continue
            cmdline = " ".join(proc.info["cmdline"] or [])
            # 過濾第二進程（沒有 -t 和 -j 的）
            if not cmdline or cmdline.startswith("\\??\\"):
                continue
            if "-t " not in cmdline and "-j " not in cmdline:
                continue
            tracker_match = re.search(r"-b (\d+)", cmdline)
            tracker_id = tracker_match.group(1) if tracker_match else ""
            rp = RobloxProcess(
                pid=proc.info["pid"],
                tracker_id=tracker_id,
                command_line=cmdline,
                start_time=datetime.fromtimestamp(proc.info["create_time"]),
            )
            result.append(rp)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return result
