"""
Roblox REST API 封裝（無需帳號 session 的公開端點）。
需要帳號授權的操作在 core/account.py 中。
"""

from __future__ import annotations

import re
from typing import Optional

import httpx

from core.constants import AUTH_API, USERS_API, GAMES_API, PRESENCE_API


def get_csrf_token() -> Optional[str]:
    """取得匿名 CSRF token（用於密碼驗證等）。"""
    try:
        r = httpx.post(
            f"{AUTH_API}/v2/login",
            headers={"User-Agent": "Mozilla/5.0"},
            timeout=10,
        )
        return r.headers.get("x-csrf-token")
    except Exception:
        return None


def validate_password(username: str, password: str) -> tuple[bool, str]:
    """檢查密碼是否符合 Roblox 要求。返回 (is_valid, message)。"""
    token = get_csrf_token()
    headers = {
        "accept": "application/json",
        "content-type": "application/json",
        "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
        "x-csrf-token": token or "",
    }
    try:
        r = httpx.post(
            f"{AUTH_API}/v2/passwords/validate",
            json={"username": username, "password": password},
            headers=headers,
            timeout=10,
        )
        data = r.json()
        if data.get("code") == 0:
            return True, "Password is valid"
        return False, data.get("message", "Password does not meet requirements")
    except Exception as e:
        return False, str(e)


def validate_username(username: str) -> bool:
    """檢查使用者名稱是否可用。"""
    try:
        r = httpx.get(
            f"{AUTH_API}/v2/usernames/validate",
            params={
                "request.username": username,
                "request.birthday": "04/15/02",
                "request.context": "Signup",
            },
            timeout=10,
        )
        return r.json().get("code") == 0
    except Exception:
        return False


def get_user_id(username: str) -> Optional[int]:
    """根據使用者名稱取得 UserID。"""
    try:
        r = httpx.post(
            f"{USERS_API}/v1/usernames/users",
            json={"usernames": [username]},
            timeout=10,
        )
        data = r.json().get("data", [])
        if data:
            return data[0]["id"]
    except Exception:
        pass
    return None


def get_game_servers(place_id: int, cursor: str = "") -> tuple[list[dict], str]:
    """取得遊戲伺服器列表。返回 (servers, next_cursor)。"""
    try:
        params = {"sortOrder": "Asc", "limit": 100}
        if cursor:
            params["cursor"] = cursor
        r = httpx.get(
            f"{GAMES_API}/v1/games/{place_id}/servers/Public",
            params=params,
            timeout=15,
        )
        data = r.json()
        return data.get("data", []), data.get("nextPageCursor", "")
    except Exception:
        return [], ""


def get_user_presence(user_ids: list[int]) -> list[dict]:
    """批次查詢使用者在線狀態。"""
    try:
        r = httpx.post(
            f"{PRESENCE_API}/v1/presence/users",
            json={"userIds": user_ids},
            timeout=10,
        )
        return r.json().get("userPresences", [])
    except Exception:
        return []


def get_place_details(place_id: int) -> Optional[dict]:
    """取得遊戲詳細資訊。"""
    try:
        r = httpx.get(
            f"{GAMES_API}/v1/games/multiget-place-details",
            params={"placeIds": place_id},
            timeout=10,
        )
        data = r.json()
        if data:
            return data[0]
    except Exception:
        pass
    return None


def get_random_job_id(place_id: int) -> Optional[str]:
    """取得隨機伺服器 JobID（用於 ShuffleJobID）。"""
    servers, _ = get_game_servers(place_id)
    if not servers:
        return None
    import random
    server = random.choice(servers)
    return server.get("id")
