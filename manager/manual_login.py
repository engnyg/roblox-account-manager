"""
手動登入：開啟瀏覽器讓使用者登入，自動捕獲 .ROBLOSECURITY cookie。
"""

from __future__ import annotations

import time
from typing import Optional, Callable

from DrissionPage import Chromium, ChromiumOptions
from loguru import logger

from core.account import Account
from core.constants import find_ungoogled_chromium


def run_manual_login(
    on_success: Callable[[Account], None],
    on_error: Callable[[str], None],
) -> None:
    """
    開啟瀏覽器到 Roblox 登入頁，等待使用者登入後捕獲 cookie。
    應在背景執行緒中呼叫。
    """
    co = ChromiumOptions()
    co.auto_port().mute(False)
    co.set_argument("--lang", "en")

    ungoogled = find_ungoogled_chromium()
    if ungoogled:
        co.set_browser_path(ungoogled)

    try:
        chrome = Chromium(addr_or_opts=co)
        page = chrome.latest_tab
        page.set.window.max()
        page.get("https://www.roblox.com/login")
    except Exception as e:
        on_error(f"Browser launch failed: {e}")
        return

    security_token: Optional[str] = None
    try:
        # 最多等 5 分鐘
        for _ in range(300):
            time.sleep(1)
            try:
                url = page.url
            except Exception:
                break

            if "roblox.com/home" in url or "roblox.com/discover" in url:
                cookies = {c["name"]: c["value"] for c in page.cookies()}
                security_token = cookies.get(".ROBLOSECURITY", "")
                if security_token:
                    break
    except Exception as e:
        logger.warning(f"Manual login polling error: {e}")
    finally:
        try:
            chrome.quit()
        except Exception:
            pass

    if not security_token:
        on_error("Login timed out or cookie not found.")
        return

    account = Account(security_token=security_token, valid=True)
    # 從 API 取得 username / user_id
    account.fetch_info()
    logger.info(f"Manual login: {account.username} ({account.user_id})")
    on_success(account)
