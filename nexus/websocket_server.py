"""
Nexus WebSocket 伺服器。
對應 C# Nexus/WebsocketServer.cs（WebSocketSharp → websockets）。
"""

from __future__ import annotations

import asyncio
from typing import Callable, Optional
from urllib.parse import parse_qs, urlparse

import websockets
from loguru import logger

from nexus.controlled_account import ControlledAccount
from core.constants import NEXUS_PORT


class NexusServer:
    def __init__(self, port: int = NEXUS_PORT, password: str = ""):
        self._port = port
        self._password = password
        self._accounts: dict[str, ControlledAccount] = {}  # username → ControlledAccount
        self._server: Optional[websockets.WebSocketServer] = None
        self._on_connect: Optional[Callable[[ControlledAccount], None]] = None
        self._on_disconnect: Optional[Callable[[ControlledAccount], None]] = None

    def set_callbacks(
        self,
        on_connect: Callable[[ControlledAccount], None],
        on_disconnect: Callable[[ControlledAccount], None],
    ):
        self._on_connect = on_connect
        self._on_disconnect = on_disconnect

    def get_accounts(self) -> list[ControlledAccount]:
        return list(self._accounts.values())

    async def start(self):
        self._server = await websockets.serve(
            self._handler,
            "localhost",
            self._port,
        )
        logger.info(f"Nexus WebSocket server listening on ws://localhost:{self._port}")

    async def stop(self):
        if self._server:
            self._server.close()
            await self._server.wait_closed()
            logger.info("Nexus server stopped")

    async def _handler(self, ws):
        # Parse query string from path
        parsed = urlparse(ws.path)
        qs = parse_qs(parsed.query)

        username = (qs.get("name", [""])[0]).strip()
        user_id_str = (qs.get("id", ["0"])[0]).strip()
        job_id = (qs.get("jobId", ["UNKNOWN"])[0]).strip()

        if not username or not user_id_str:
            await ws.close(1008, "Missing name or id")
            return

        try:
            user_id = int(user_id_str)
        except ValueError:
            await ws.close(1008, "Invalid id")
            return

        account = self._accounts.get(username)
        if not account:
            account = ControlledAccount(username=username, user_id=user_id)
            self._accounts[username] = account

        account.connect(ws)
        account.in_game_job_id = job_id
        logger.info(f"[Nexus] {username} connected (jobId={job_id})")

        if self._on_connect:
            self._on_connect(account)

        try:
            async for message in ws:
                account.handle_message(message)
        except websockets.ConnectionClosed:
            pass
        finally:
            account.disconnect()
            logger.info(f"[Nexus] {username} disconnected")
            if self._on_disconnect:
                self._on_disconnect(account)

    async def broadcast(self, message: str):
        for acc in self._accounts.values():
            await acc.send(message)

    async def send_to(self, username: str, message: str):
        acc = self._accounts.get(username)
        if acc:
            await acc.send(message)
