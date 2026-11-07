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


def parse_roblox_cmdline(cmdline: str) -> Optional[str]:
    """
    判斷是否為「主要」Roblox 遊戲行程，並回傳 tracker id（可為空字串）；不是則回傳 None。
    兩種形式：
      - 經 bootstrapper / 協定處理器：`... -t <ticket> -j <url> -b <tracker> ...`
      - 直接以 URI 啟動：`RobloxPlayerBeta.exe roblox-player:1+...+browsertrackerid:<tracker>+...`
    """
    if not cmdline or cmdline.startswith("\\??\\"):
        return None
    if "-t " in cmdline or "-j " in cmdline:
        m = re.search(r"-b (\d+)", cmdline)
        return m.group(1) if m else ""
    if "roblox-player:" in cmdline:
        m = re.search(r"browsertrackerid:(\d+)", cmdline, re.IGNORECASE)
        return m.group(1) if m else ""
    return None


def get_roblox_processes() -> list[RobloxProcess]:
    """掃描所有正在執行的 Roblox 進程。"""
    result = []
    for proc in psutil.process_iter(["pid", "name", "cmdline", "create_time"]):
        try:
            if proc.info["name"] != "RobloxPlayerBeta.exe":
                continue
            cmdline = " ".join(proc.info["cmdline"] or [])
            tracker_id = parse_roblox_cmdline(cmdline)
            if tracker_id is None:  # 第二進程等
                continue
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
