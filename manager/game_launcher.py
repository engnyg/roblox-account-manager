"""
遊戲啟動器。
移植自 C# Account.cs JoinServer()。
"""

from __future__ import annotations

import random
import re
import subprocess
import sys
import time
import uuid
from typing import TYPE_CHECKING, Optional
from urllib.parse import quote

import httpx
from loguru import logger

if TYPE_CHECKING:
    from core.account import Account


def _get_roblox_path() -> str | None:
    """嘗試找到 RobloxPlayerBeta.exe 的路徑。"""
    if sys.platform != "win32":
        return None
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Roblox\RobloxStudioLauncherBeta") as key:
            path, _ = winreg.QueryValueEx(key, "LauncherPath")
            return path
    except Exception:
        pass
    import os
    candidates = [
        r"C:\Program Files (x86)\Roblox\Versions",
        os.path.join(os.environ.get("LocalAppData", ""), r"Roblox\Versions"),
    ]
    for base in candidates:
        if os.path.isdir(base):
            for ver in sorted(os.listdir(base), reverse=True):
                exe = os.path.join(base, ver, "RobloxPlayerBeta.exe")
                if os.path.exists(exe):
                    return exe
    return None


def _random_browser_tracker() -> str:
    return str(random.randint(100000, 175000)) + str(random.randint(100000, 900000))


_UUID_RE = r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"

_ACCESS_CODE_PATTERNS = [
    # 舊版 inline JS: Roblox.GameLauncher.joinPrivateGame(placeId, 'UUID')
    rf"joinPrivateGame\s*\(\s*\d+\s*,\s*['\"]({_UUID_RE})['\"]",
    # JSON 嵌入: "accessCode":"UUID"
    rf'"accessCode"\s*:\s*"({_UUID_RE})"',
    # JSON 嵌入: "privateServerAccessCode":"UUID"
    rf'"privateServerAccessCode"\s*:\s*"({_UUID_RE})"',
    # JS 物件: accessCode: 'UUID'
    rf"\baccessCode\s*:\s*['\"]({_UUID_RE})['\"]",
]


def _fetch_access_code(
    account: "Account",
    place_id: int,
    link_code: str,
) -> tuple[bool, str]:
    """
    訪問遊戲頁面 HTML，提取 UUID 格式的真正 accessCode。
    返回 (success, uuid_access_code_or_error)。
    """
    try:
        r = httpx.get(
            f"https://www.roblox.com/games/{place_id}",
            params={"privateServerLinkCode": link_code},
            cookies={".ROBLOSECURITY": account.security_token},
            headers={"User-Agent": "Mozilla/5.0"},
            timeout=15,
            follow_redirects=True,
        )
        if not r.is_success:
            return False, f"HTTP {r.status_code}"
        for pattern in _ACCESS_CODE_PATTERNS:
            m = re.search(pattern, r.text, re.IGNORECASE)
            if m:
                logger.debug(f"_fetch_access_code: found UUID via pattern={pattern[:40]}")
                return True, m.group(1)
        logger.warning(f"_fetch_access_code: no UUID found in page (len={len(r.text)}, url={r.url})")
        return False, "頁面中找不到 accessCode（連結可能已失效或尚未開啟伺服器）"
    except Exception as e:
        return False, str(e)


def parse_private_server_url(url: str) -> tuple[Optional[int], Optional[str]]:
    """
    解析 Roblox 私人伺服器 URL，返回 (place_id, link_code)。

    支援兩種格式：
    - 完整格式: /games/{placeId}/...?privateServerLinkCode={code}
      → (place_id, link_code)
    - 分享短連結: /share?code={shareCode}&type=Server
      → (None, shareCode)  ← place_id=None 代表使用 shareCode 模式
    """
    # 新版分享連結
    share_match = re.search(r'[?&]code=([a-fA-F0-9]+)', url)
    if share_match and 'type=Server' in url:
        return None, share_match.group(1)

    # 舊版完整格式
    place_match = re.search(r'/games/(\d+)', url)
    code_match = re.search(r'[?&]privateServerLinkCode=([^\s&]+)', url)
    if place_match and code_match:
        return int(place_match.group(1)), code_match.group(1)

    return None, None


def _resolve_job_id_via_servers_api(
    account: "Account",
    place_id: Optional[int],
    link_code: str,
) -> tuple[bool, str]:
    """
    不走 gamejoin API，改用 servers listing API 直接拿 Job ID。
    1. share code → 追蹤重導向取得 placeId
    2. placeId → universeId
    3. universeId → 列出 Private servers → 取第一個 id
    """
    cookies = {".ROBLOSECURITY": account.security_token}
    headers = {"User-Agent": "Mozilla/5.0"}

    resolved_place_id = place_id

    if resolved_place_id is None:
        try:
            _browser_headers = {
                "User-Agent": (
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                    "AppleWebKit/537.36 (KHTML, like Gecko) "
                    "Chrome/124.0.0.0 Safari/537.36"
                ),
                "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                "Accept-Language": "en-US,en;q=0.9",
                "Referer": "https://www.roblox.com/",
            }
            r = httpx.get(
                "https://www.roblox.com/share",
                params={"code": link_code, "type": "Server"},
                cookies=cookies, headers=_browser_headers,
                follow_redirects=True, timeout=10,
            )
            # 先搜 final URL（登入狀態下桌面瀏覽器應重導向至 /games/PLACE_ID/）
            _PLACE_PATTERNS = [
                r'/games/(\d+)',
                r'"placeId"\s*:\s*(\d+)',
                r'"PlaceId"\s*:\s*(\d+)',
                r'"universeId"\s*:\s*(\d+)',
                r'roblox\.com/games/(\d+)',
                r'content="https://www\.roblox\.com/games/(\d+)',
            ]
            search_targets = [str(r.url), r.text]
            for target in search_targets:
                for pat in _PLACE_PATTERNS:
                    m = re.search(pat, target)
                    if m:
                        resolved_place_id = int(m.group(1))
                        break
                if resolved_place_id:
                    break
            if resolved_place_id is None:
                logger.warning(f"share code 解析失敗，final url={r.url} len={len(r.text)}")
                return False, "share code 無法解析為 place ID（請確認連結有效）"
            logger.debug(f"share code resolved to placeId={resolved_place_id}")
        except Exception as e:
            return False, f"share code 解析失敗: {e}"

    try:
        r = httpx.get(
            f"https://apis.roblox.com/universes/v1/places/{resolved_place_id}/universe",
            cookies=cookies, headers=headers, timeout=10,
        )
        if not r.is_success:
            return False, f"universe ID 查詢失敗: HTTP {r.status_code}"
        universe_id = r.json().get("universeId")
        if not universe_id:
            return False, "無法取得 universeId"
    except Exception as e:
        return False, f"universe ID 查詢失敗: {e}"

    try:
        r = httpx.get(
            f"https://games.roblox.com/v1/games/{universe_id}/servers/Private",
            params={"sortOrder": "Asc", "limit": 100},
            cookies=cookies, headers=headers, timeout=10,
        )
        if not r.is_success:
            return False, f"伺服器列表查詢失敗: HTTP {r.status_code}"
        servers = r.json().get("data", [])
        if not servers:
            return False, "目前沒有運行中的私人伺服器（伺服器可能已停止）"
        job_id = servers[0].get("id")
        if job_id:
            return True, job_id
        return False, "伺服器列表中找不到 Job ID"
    except Exception as e:
        return False, f"伺服器列表查詢失敗: {e}"


def resolve_private_server_job_id(
    account: "Account",
    place_id: Optional[int],
    link_code: str,
    *,
    timeout: float = 30.0,
) -> tuple[bool, str]:
    """
    透過私人伺服器連結代碼取得目前的 Job ID（需要已登入帳號）。
    先嘗試 servers listing API，失敗才 fallback 到 gamejoin API。
    返回 (success, job_id_or_error)。
    """
    import json as _json

    ok, job_id = _resolve_job_id_via_servers_api(account, place_id, link_code)
    if ok:
        logger.info(f"[{account.username}] 私人伺服器 Job ID (servers API): {job_id}")
        return True, job_id
    logger.debug(f"[{account.username}] servers API 失敗，改用 gamejoin: {job_id}")

    ok, csrf = account.get_csrf_token()
    if not ok:
        return False, f"取得 CSRF token 失敗: {csrf}"

    cookies = {".ROBLOSECURITY": account.security_token}
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "X-CSRF-TOKEN": csrf,
        "Origin": "https://www.roblox.com",
        "Referer": f"https://www.roblox.com/games/{place_id or ''}",
        "Accept": "application/json, text/plain, */*",
    }
    _BASE_BODY = {"isPlayTogetherGame": False, "isTeleport": False, "gamerTag": ""}
    # place_id=None 代表新版 share code 格式
    used_access_code_fallback = False
    if place_id is None:
        body = {**_BASE_BODY, "shareCode": link_code}
    else:
        # 從遊戲頁面 HTML 提取 UUID accessCode
        ok_ac, access_code = _fetch_access_code(account, place_id, link_code)
        if ok_ac:
            logger.debug(f"[{account.username}] accessCode UUID: {access_code}")
            body = {**_BASE_BODY, "placeId": place_id, "accessCode": access_code}
        else:
            logger.warning(f"[{account.username}] 無法取得 accessCode UUID: {access_code}")
            body = {**_BASE_BODY, "shareCode": link_code}
            used_access_code_fallback = True

    # 非致命錯誤狀態（伺服器暫時未就緒，繼續輪詢）
    _RETRY_STATUSES = {0, 1, 3}
    # 致命錯誤狀態
    _ERROR_STATUSES = {4, 5, 6, 7, 9, 10, 11, 12}

    logger.debug(f"[{account.username}] gamejoin body keys={list(body.keys())} place_id={place_id}")
    deadline = time.time() + timeout
    try:
        with httpx.Client(cookies=cookies, headers=headers, timeout=15, follow_redirects=True) as client:
            while time.time() < deadline:
                r = client.post(
                    "https://gamejoin.roblox.com/v1/join-private-game",
                    json=body,
                )
                if not r.is_success:
                    return False, f"HTTP {r.status_code}: {r.text[:300]}"

                data = r.json()
                status = data.get("status", -1)
                logger.debug(f"[{account.username}] gamejoin status={status} msg={data.get('message')}")

                if status == 2:
                    job_id = data.get("jobId")

                    # joinScript 可能是 dict 或 JSON 字串
                    if not job_id:
                        js = data.get("joinScript")
                        if isinstance(js, dict):
                            job_id = js.get("GameId") or js.get("gameId")
                        elif isinstance(js, str):
                            try:
                                sd = _json.loads(js)
                                job_id = sd.get("GameId") or sd.get("gameId")
                            except Exception:
                                pass

                    if job_id:
                        logger.info(f"[{account.username}] 私人伺服器 Job ID: {job_id}")
                        return True, job_id
                    return False, f"狀態=2 但無 Job ID，完整回應: {data}"

                elif status in _ERROR_STATUSES:
                    msg = data.get("message", "")
                    status_data = data.get("statusData")
                    logger.warning(f"[{account.username}] gamejoin error full={data}")
                    if status == 12:
                        if used_access_code_fallback:
                            return False, f"無法解析私人伺服器連結（頁面格式可能已更新），請改用直接加入按鈕。原始訊息：{msg}"
                        if status_data:
                            return False, f"帳號無法加入此遊戲（年齡限制或存取限制）。原始訊息：{msg}"
                        return False, f"無法加入此私人伺服器（連結可能已失效，或遊戲設定限制加入）。原始訊息：{msg}"
                    return False, f"錯誤 status={status}: {msg}"

                elif status not in _RETRY_STATUSES:
                    return False, f"未知 status={status}: {data.get('message', '')}"

                time.sleep(2)

        return False, "逾時：無法取得 Job ID"
    except Exception as e:
        return False, str(e)


def join_private_server(
    account: "Account",
    place_id: Optional[int],
    link_code: str,
) -> tuple[bool, str]:
    """
    透過私人伺服器連結代碼（或 share code）啟動 Roblox 並加入私人伺服器。
    place_id=None 代表新版 share code 格式。
    返回 (success, message)。
    """
    ok, ticket = account.get_auth_ticket()
    if not ok:
        return False, f"取得 auth ticket 失敗: {ticket}"

    if not account.browser_tracker_id:
        account.browser_tracker_id = _random_browser_tracker()
    tracker = account.browser_tracker_id
    launch_time = int(time.time() * 1000)

    try:
        if place_id is None:
            # 新版 share code：直接用 link_code 作為 accessCode
            url = (
                f"https://assetgame.roblox.com/game/PlaceLauncher.ashx"
                f"?request=RequestPrivateGame"
                f"&browserTrackerId={tracker}"
                f"&accessCode={link_code}"
                f"&linkCode={link_code}"
                f"&isPlayTogetherGame=false"
            )
        else:
            # 舊版格式：從遊戲頁面 HTML 提取 UUID accessCode
            ok_ac, access_code = _fetch_access_code(account, place_id, link_code)
            if not ok_ac:
                logger.warning(f"[{account.username}] 無法取得 accessCode，改用 linkCode 直接加入: {access_code}")
                access_code = link_code
            url = (
                f"https://assetgame.roblox.com/game/PlaceLauncher.ashx"
                f"?request=RequestPrivateGame"
                f"&browserTrackerId={tracker}"
                f"&placeId={place_id}"
                f"&accessCode={access_code}"
                f"&linkCode={link_code}"
                f"&isPlayTogetherGame=false"
            )
        launcher_url = (
            f"roblox-player:1+launchmode:play"
            f"+gameinfo:{ticket}"
            f"+launchtime:{launch_time}"
            f"+placelauncherurl:{quote(url, safe='')}"
            f"+browsertrackerid:{tracker}"
            f"+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp"
        )

        from manager.roblox_launch import launch_roblox
        pid, how = launch_roblox(launcher_url)

        logger.info(f"[{account.username}] 已啟動私人伺服器 place={place_id} ({how}, pid={pid})")
        return True, "Success"
    except Exception as e:
        return False, str(e)


_JOB_UUID_RE = re.compile(_UUID_RE, re.IGNORECASE)

# Teleport-Controller 控制伺服器（本機）。bot 會回報自己目前所在的 placeId / jobId。
TELEPORT_CONTROLLER_URL = "http://127.0.0.1:8765/config"


def parse_join_input(text: str) -> tuple[Optional[int], str]:
    """
    解析「Job ID」欄位的輸入，回傳 (place_id_or_None, job_id)。

    除了單純的 Job ID（UUID），也接受直接貼上整條加入連結，例如：
      roblox://experiences/start?placeId=123&gameInstanceId=UUID
      https://www.roblox.com/games/start?placeId=123&gameInstanceId=UUID
      https://www.roblox.com/games/123/Name?gameInstanceId=UUID
    連結裡有 placeId 時一併回傳，避免 Job ID 搭配到錯的 Place ID。
    """
    raw = (text or "").strip().strip("\"'`<>").strip()
    if not raw:
        return None, ""

    place_id: Optional[int] = None
    m = re.search(r"[?&]placeId=(\d+)", raw, re.IGNORECASE) or re.search(r"/games/(\d+)", raw)
    if m:
        place_id = int(m.group(1))

    m = re.search(
        rf"(?:gameInstanceId|jobId|gameId)=({_UUID_RE})", raw, re.IGNORECASE
    )
    if m:
        return place_id, m.group(1).lower()

    m = _JOB_UUID_RE.search(raw)
    if m:
        return place_id, m.group(0).lower()

    # 不是 UUID 格式就原樣交給後續流程（讓 API 回報真正的錯誤）
    return place_id, raw


def _lookup_bot_place_for_job(job_id: str) -> Optional[int]:
    """
    向本機 Teleport-Controller 查詢：有沒有 bot 正在這個 Job ID 上？有的話回傳它的 placeId。
    bot 實際就在該伺服器內，所以它回報的 placeId 一定與 Job ID 相符。
    控制伺服器沒開或查不到就回傳 None，不影響正常流程。
    """
    try:
        r = httpx.get(TELEPORT_CONTROLLER_URL, timeout=1.0)
        if not r.is_success:
            return None
        for bot in r.json().get("bots", []):
            if str(bot.get("jobId", "")).lower() == job_id.lower() and bot.get("placeId"):
                return int(bot["placeId"])
    except Exception:
        pass
    return None


def normalize_join_target(place_id: Optional[int], job_text: str) -> tuple[Optional[int], str]:
    """
    整理加入目標：解析貼上的連結 → 再用 bot 回報的 placeId 校正。
    返回 (place_id, job_id)。沒有 Job ID 時原樣返回 place_id。
    """
    link_place, job_id = parse_join_input(job_text)
    if link_place is not None:
        place_id = link_place
    if job_id:
        bot_place = _lookup_bot_place_for_job(job_id)
        if bot_place is not None and bot_place != place_id:
            logger.info(f"Job ID {job_id} 屬於 place {bot_place}（輸入的是 {place_id}），已自動校正")
            place_id = bot_place
    return place_id, job_id


# gamejoin API 對「瀏覽器 UA」一律回 status=12 / "Unable to join Game 311"（連有效的公開伺服器也是），
# 只有用 Roblox 客戶端自己的 UA 才會回真實結果。預檢必須使用這個 UA。
_ROBLOX_CLIENT_UA = "Roblox/WinInet"

# gamejoin status：0/1/3 = 準備中（重試）、2 = 可加入、4 = 遊戲已停用、5 = 找不到該伺服器
# （已關閉，或 Job ID 與 Place ID 不匹配）、6 = 已滿。這些是「確定」的失敗，可以擋下加入。
# 其餘（12、HTTP 錯誤…）代表 API 本身拒絕或行為改變，不足以斷定 Job ID 失效，不應擋住加入。
_JOIN_RETRY_STATUSES = {0, 1, 3}
_JOIN_DEFINITIVE_FAIL = {
    4: "遊戲目前已停用",
    5: "找不到此伺服器（已關閉，或這個 Job ID 不屬於 Place ID {place}）",
    6: "伺服器已滿",
}


def _validate_job_id(
    account: "Account",
    place_id: int,
    job_id: str,
    tracker: str,
) -> tuple[bool, str, bool]:
    """
    透過 gamejoin API 驗證指定的 Job ID 目前是否仍可加入。
    舊版 PlaceLauncher.ashx RequestGameJob 對已失效/已滿/已關閉的 Job ID
    仍會回傳「Success」（因為它只是啟動 Roblox 客戶端，並不驗證伺服器是否存在），
    導致使用者誤以為「Job ID 沒用」。這裡先用官方 gamejoin API 主動驗證。

    返回 (ok, message, definitive)：
      definitive=True 表示失敗是確定的（伺服器不存在/已滿…），呼叫端應擋下加入；
      definitive=False 表示預檢無法斷定（API 拒絕、網路錯誤…），呼叫端應照常嘗試啟動。
    """
    ok, csrf = account.get_csrf_token()
    if not ok:
        return False, f"取得 CSRF token 失敗: {csrf}", False

    cookies = {".ROBLOSECURITY": account.security_token}
    headers = {
        "User-Agent": _ROBLOX_CLIENT_UA,
        "X-CSRF-TOKEN": csrf,
        "Origin": "https://www.roblox.com",
        "Referer": f"https://www.roblox.com/games/{place_id}",
        "Accept": "application/json, text/plain, */*",
    }
    body = {
        "placeId": place_id,
        "gameId": job_id,
        "isPlayTogetherGame": False,
        "isTeleport": False,
        "isPartyLeader": False,
        "gamerTag": "",
        "browserTrackerId": int(tracker) if tracker.isdigit() else 0,
    }
    try:
        data: dict = {}
        status = -1
        for attempt in range(4):
            # 每次嘗試都要用獨立的 GUID（不可用 Job ID，否則會被當成重複請求）
            body["gameJoinAttemptId"] = str(uuid.uuid4())
            r = httpx.post(
                "https://gamejoin.roblox.com/v1/join-game-instance",
                json=body, cookies=cookies, headers=headers, timeout=15,
            )
            if not r.is_success:
                logger.warning(f"[{account.username}] join-game-instance HTTP {r.status_code} place={place_id} job={job_id}")
                return False, f"HTTP {r.status_code}: {r.text[:200]}", False
            data = r.json()
            status = data.get("status", -1)
            logger.debug(f"[{account.username}] join-game-instance status={status} msg={data.get('message')} (try {attempt + 1})")
            if status == 2:
                return True, "OK", False
            if status in _JOIN_RETRY_STATUSES:
                time.sleep(1.0)
                continue
            break

        logger.warning(f"[{account.username}] Job ID 預檢未通過 place={place_id} job={job_id} status={status} data={data}")
        if status in _JOIN_DEFINITIVE_FAIL:
            reason = _JOIN_DEFINITIVE_FAIL[status].format(place=place_id)
            return False, f"無法加入此 Job ID（status={status}）：{reason}", True
        return False, f"status={status} {data.get('message', '')}".strip(), False
    except Exception as e:
        return False, str(e), False


def _multi_roblox_warning() -> str:
    """多開沒有真正生效時回傳警語（這次啟動會把執行中的 Roblox 關掉），生效時回傳空字串。"""
    if sys.platform != "win32":
        return ""
    try:
        from manager.multi_roblox import multi_roblox_state, STATE_ACTIVE, STATE_WAITING
        state = multi_roblox_state()
    except Exception:
        return ""
    if state == STATE_ACTIVE:
        return ""
    if state == STATE_WAITING:
        logger.warning("多開未生效：mutex 被執行中的 Roblox 擁有，這次啟動可能會關閉它")
        return "（⚠ 多開未生效：目前有 Roblox 持有 mutex，這次啟動會關閉它；請先關閉所有 Roblox 再試）"
    return "（⚠ 多開已停用，這次啟動會關閉執行中的 Roblox）"


def join_server(
    account: "Account",
    place_id: int,
    job_id: str = "",
    follow_user: bool = False,
    join_vip: bool = False,
) -> tuple[bool, str]:
    """
    啟動 Roblox 並加入伺服器。（多開沒有真正生效時，訊息會附上警語）
    job_id 可以是單純的 Job ID，也可以是整條加入連結；會自動解析並校正 Place ID。
    返回 (success, message)。
    """
    if job_id and not follow_user:
        place_id, job_id = normalize_join_target(place_id, job_id)

    ok, ticket = account.get_auth_ticket()
    if not ok:
        return False, f"Failed to get auth ticket: {ticket}"

    if not account.browser_tracker_id:
        account.browser_tracker_id = _random_browser_tracker()

    tracker = account.browser_tracker_id
    launch_time = int(time.time() * 1000)

    precheck_note = _multi_roblox_warning()
    if job_id and not follow_user:
        ok_valid, msg, definitive = _validate_job_id(account, place_id, job_id, tracker)
        if not ok_valid:
            if definitive:
                return False, msg
            # 預檢無法斷定（不是確定失效）→ 仍照常啟動，只是提醒
            precheck_note += f"（預檢無法確認：{msg}，仍已嘗試啟動）"

    try:
        if follow_user:
            url = f"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId={place_id}"
        elif job_id:
            url = f"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={tracker}&placeId={place_id}&gameId={job_id}&isPlayTogetherGame=false"
        else:
            url = f"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={tracker}&placeId={place_id}&isPlayTogetherGame=false"

        launcher_url = (
            f"roblox-player:1+launchmode:play"
            f"+gameinfo:{ticket}"
            f"+launchtime:{launch_time}"
            f"+placelauncherurl:{quote(url, safe='')}"
            f"+browsertrackerid:{tracker}"
            f"+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp"
        )

        from manager.roblox_launch import launch_roblox
        pid, how = launch_roblox(launcher_url)

        logger.info(f"Launched Roblox for {account.username} → place {place_id} ({how}, pid={pid})")
        return True, "Success" + precheck_note
    except Exception as e:
        return False, str(e)
