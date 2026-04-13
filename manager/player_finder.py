"""
玩家搜尋（在哪個伺服器）。
"""

from __future__ import annotations

from typing import Optional

import httpx

from core.constants import GAMES_API, PRESENCE_API
from core.roblox_api import get_user_id, get_user_presence


def find_player_server(place_id: int, target_username: str) -> Optional[str]:
    """
    搜尋目標玩家所在的伺服器 JobID。
    返回 job_id 或 None。
    """
    uid = get_user_id(target_username)
    if not uid:
        return None

    presences = get_user_presence([uid])
    if not presences:
        return None

    p = presences[0]
    if p.get("placeId") == place_id:
        return p.get("gameId")
    return None
