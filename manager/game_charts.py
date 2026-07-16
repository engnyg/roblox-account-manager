"""
遊戲探索排行榜。
移植自 MultiRoblox-RAM 的「Game Discovery / Charts」功能：
顯示 Roblox 首頁的熱門/精選/吸金榜遊戲，方便挑選要加入的遊戲。

Roblox 的排行榜 API 屬未公開文件的內部端點，欄位名稱可能隨時變動，
因此這裡對回傳資料做防禦性解析，並保留舊版 API 作為 fallback。
"""

from __future__ import annotations

import uuid
from typing import Any

from loguru import logger

from core.roblox_api import (
    get_explore_sorts, get_explore_sort_content,
    get_game_sorts_legacy, get_games_by_sort_token_legacy,
)


def _normalize_game(raw: dict) -> dict:
    """把不同 API 版本的欄位名稱統一成固定格式。"""
    universe_id = raw.get("universeId") or raw.get("id") or 0
    place_id = (
        raw.get("rootPlaceId") or raw.get("placeId")
        or raw.get("gameId") or universe_id
    )
    name = raw.get("name") or raw.get("gameName") or raw.get("title") or ""
    playing = raw.get("playerCount") or raw.get("playing") or raw.get("totalPlayerCount") or 0
    return {
        "name": name,
        "placeId": int(place_id) if place_id else 0,
        "universeId": int(universe_id) if universe_id else 0,
        "playing": int(playing) if isinstance(playing, (int, float)) else 0,
    }


def fetch_charts(max_sorts: int = 8, games_per_sort: int = 20) -> list[dict]:
    """
    取得排行榜清單。
    返回 [{"name": 排序名稱, "games": [{"name","placeId","universeId","playing"}]}]
    """
    session_id = str(uuid.uuid4())

    sorts = get_explore_sorts(session_id)
    result: list[dict] = []

    if sorts:
        for s in sorts[:max_sorts]:
            sort_id = s.get("sortId") or s.get("id") or ""
            sort_name = s.get("sortDisplayName") or s.get("displayName") or sort_id
            if not sort_id:
                continue
            raw_games = get_explore_sort_content(session_id, sort_id)[:games_per_sort]
            games = [_normalize_game(g) for g in raw_games]
            games = [g for g in games if g["placeId"]]
            if games:
                result.append({"name": sort_name, "games": games})
        if result:
            return result
        logger.warning("game_charts: explore-api 回傳空結果，改用舊版 API")

    # Fallback：舊版已淘汰但可能仍可用的 API
    legacy_sorts = get_game_sorts_legacy()
    for s in legacy_sorts[:max_sorts]:
        token = s.get("token") or s.get("sortId") or ""
        sort_name = s.get("sortDisplayName") or s.get("name") or token
        if not token:
            continue
        raw_games = get_games_by_sort_token_legacy(token, games_per_sort)
        games = [_normalize_game(g) for g in raw_games]
        games = [g for g in games if g["placeId"]]
        if games:
            result.append({"name": sort_name, "games": games})

    return result
