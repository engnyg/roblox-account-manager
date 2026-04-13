"""
帳號瀏覽器：用 Playwright 以帳號 Cookie 開啟瀏覽器。
對應 C# Classes/AccountBrowser.cs。
"""

from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING

from loguru import logger

if TYPE_CHECKING:
    from core.account import Account


async def open_account_browser(account: "Account", url: str = "https://www.roblox.com") -> bool:
    """
    以帳號 cookie 開啟 Playwright 瀏覽器。
    """
    try:
        from playwright.async_api import async_playwright
        async with async_playwright() as pw:
            browser = await pw.chromium.launch(headless=False)
            ctx = await browser.new_context()
            await ctx.add_cookies([{
                "name": ".ROBLOSECURITY",
                "value": account.security_token,
                "domain": ".roblox.com",
                "path": "/",
            }])
            page = await ctx.new_page()
            await page.goto(url)
            logger.info(f"Opened browser for {account.username}")
            # Keep open until closed by user
            await page.wait_for_event("close", timeout=0)
            await browser.close()
            return True
    except Exception as e:
        logger.error(f"account_browser: {e}")
        return False
