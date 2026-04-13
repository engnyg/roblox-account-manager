"""
批次操作。
對應 C# Classes/Batch.cs。
"""

from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING, Callable

from loguru import logger

if TYPE_CHECKING:
    from core.account import Account


async def batch_join(
    accounts: list["Account"],
    place_id: int,
    job_id: str = "",
    delay: float = 8.0,
    progress_callback: Callable[[int, int], None] | None = None,
):
    """批次讓所有帳號加入同一個伺服器。"""
    from manager.game_launcher import join_server
    total = len(accounts)
    for i, acc in enumerate(accounts):
        ok, msg = join_server(acc, place_id, job_id)
        if progress_callback:
            progress_callback(i + 1, total)
        if ok:
            logger.info(f"[{i+1}/{total}] {acc.username} joined place {place_id}")
        else:
            logger.warning(f"[{i+1}/{total}] {acc.username} failed: {msg}")
        if i < total - 1:
            await asyncio.sleep(delay)


async def batch_set_privacy(
    accounts: list["Account"],
    follow_privacy: int = 4,
    progress_callback: Callable[[int, int], None] | None = None,
):
    """批次設定隱私。"""
    total = len(accounts)
    for i, acc in enumerate(accounts):
        acc.set_follow_privacy(follow_privacy)
        if progress_callback:
            progress_callback(i + 1, total)
        await asyncio.sleep(0.5)


async def batch_logout_other_sessions(
    accounts: list["Account"],
    progress_callback: Callable[[int, int], None] | None = None,
):
    """批次登出其他 session。"""
    total = len(accounts)
    for i, acc in enumerate(accounts):
        acc.logout_other_sessions()
        if progress_callback:
            progress_callback(i + 1, total)
        await asyncio.sleep(1)
