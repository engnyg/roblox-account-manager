"""
Nexus 指令定義。
對應 C# Nexus/Command.cs。
"""

from __future__ import annotations

from enum import Enum


class NexusCommand(str, Enum):
    # 從 Lua 腳本接收
    PING = "Ping"
    CONNECT = "Connect"
    DISCONNECT = "Disconnect"
    STATUS = "Status"

    # 發送給 Lua 腳本
    EXECUTE_SCRIPT = "ExecuteScript"
    KICK = "Kick"
    CHAT = "Chat"
    REJOIN = "Rejoin"
    JOIN_SERVER = "JoinServer"


def parse_message(raw: str) -> tuple[str, dict]:
    """解析 WebSocket 訊息（JSON）。"""
    import json
    try:
        data = json.loads(raw)
        cmd = data.get("command", "")
        return cmd, data
    except Exception:
        return raw.strip(), {}


def make_message(command: NexusCommand, **kwargs) -> str:
    """建構 JSON 訊息。"""
    import json
    payload = {"command": command.value, **kwargs}
    return json.dumps(payload)
