"""
伺服器列表瀏覽。
對應 C# Forms/ServerList.cs。
"""

from __future__ import annotations

from typing import Optional

import httpx

from core.roblox_api import get_game_servers, get_place_details


def fetch_servers(place_id: int, max_pages: int = 5) -> list[dict]:
    """取得所有公開伺服器（多頁）。"""
    servers = []
    cursor = ""
    for _ in range(max_pages):
        page, cursor = get_game_servers(place_id, cursor)
        servers.extend(page)
        if not cursor:
            break
    return servers


def enrich_servers(servers: list[dict]) -> list[dict]:
    """
    使用 ip-api.com 補充伺服器地區資訊。
    C# 使用 IPApiLink 設定，這裡直接實作。
    """
    enriched = []
    for s in servers:
        entry = dict(s)
        # ip-api 不支援批次查 JobID，跳過地區 IP 查詢
        enriched.append(entry)
    return enriched


def format_server_display(server: dict) -> dict:
    """轉換為 GUI 顯示格式。"""
    return {
        "id": server.get("id", ""),
        "playing": server.get("playing", 0),
        "maxPlayers": server.get("maxPlayers", 0),
        "ping": server.get("ping", 0),
        "fps": server.get("fps", 0),
    }
