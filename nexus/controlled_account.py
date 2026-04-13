"""
受控帳號模型（Nexus 連線狀態）。
對應 C# Nexus/ControlledAccount.cs。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional, TYPE_CHECKING

import websockets

if TYPE_CHECKING:
    pass


@dataclass
class ControlledAccount:
    username: str
    user_id: int = 0
    in_game_job_id: str = ""
    connected_at: Optional[datetime] = None
    disconnected_at: Optional[datetime] = None
    websocket: Optional[object] = field(default=None, repr=False)

    @property
    def is_connected(self) -> bool:
        return self.websocket is not None

    async def send(self, message: str):
        if self.websocket:
            try:
                await self.websocket.send(message)
            except Exception:
                self.websocket = None

    def connect(self, ws):
        self.websocket = ws
        self.connected_at = datetime.now()
        self.disconnected_at = None

    def disconnect(self):
        self.websocket = None
        self.disconnected_at = datetime.now()

    def handle_message(self, raw: str):
        from nexus.command import parse_message
        cmd, data = parse_message(raw)
        # 可在此擴充指令處理邏輯
        from loguru import logger
        logger.debug(f"[Nexus] {self.username} → {cmd}: {data}")
