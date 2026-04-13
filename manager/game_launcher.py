"""
遊戲啟動器。
移植自 C# Account.cs JoinServer()。
"""

from __future__ import annotations

import random
import re
import subprocess
import sys
from typing import TYPE_CHECKING
from urllib.parse import quote

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


def join_server(
    account: "Account",
    place_id: int,
    job_id: str = "",
    follow_user: bool = False,
    join_vip: bool = False,
) -> tuple[bool, str]:
    """
    啟動 Roblox 並加入伺服器。
    返回 (success, message)。
    """
    ok, ticket = account.get_auth_ticket()
    if not ok:
        return False, f"Failed to get auth ticket: {ticket}"

    if not account.browser_tracker_id:
        account.browser_tracker_id = _random_browser_tracker()

    tracker = account.browser_tracker_id
    import time
    launch_time = int(time.time() * 1000)

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

        if sys.platform == "win32":
            import os
            os.startfile(launcher_url)
        else:
            subprocess.Popen(["xdg-open", launcher_url])

        logger.info(f"Launched Roblox for {account.username} → place {place_id}")
        return True, "Success"
    except Exception as e:
        return False, str(e)
