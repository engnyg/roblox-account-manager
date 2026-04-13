"""
Roblox 帳號自動建立主流程。
從現有 main.py 重構，移除所有 CLI 互動，改為可被 GUI 呼叫的函式。
"""

from __future__ import annotations

import asyncio
import locale
import os
import random
import re
import shutil
import tempfile
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional, Callable

from DrissionPage import Chromium, ChromiumOptions, errors
from loguru import logger

from core.account import Account
from core.constants import find_ungoogled_chromium
from core.account_store import append_account_txt, append_cookies_json
from core.email_service import EmailService
from core.username_generator import create_username
from core.password_generator import generate_password
from generator.avatar_customizer import randomize_avatar
from generator.analytics import send_analytics


@dataclass
class GeneratorConfig:
    password: str = ""  # 空字串 = 每個帳號產生獨立隨機密碼
    verification: bool = True
    name_format: Optional[str] = None
    scrambled_username: bool = True
    customization: bool = True
    follow_users: list[str] = field(default_factory=list)
    proxies: list[str] = field(default_factory=list)
    captcha_api_key: str = ""
    incognito: bool = True
    browser_path: Optional[str] = None
    browser_user_data_dir: Optional[str] = None  # 已安裝 NopeCHA 的 Chrome profile 路徑
    count: int = 1
    version: str = "unknown"


@dataclass
class GeneratorResult:
    account: Optional[Account] = None
    success: bool = False
    error: str = ""


class AccountGenerator:
    def __init__(self, config: GeneratorConfig, progress_callback: Optional[Callable[[str, int], None]] = None):
        self.config = config
        self._progress = progress_callback or (lambda msg, pct: None)
        self._email_service = EmailService()

    def _report(self, msg: str, pct: int):
        self._progress(msg, pct)

    async def generate_one(self, index: int, total: int) -> GeneratorResult:
        cfg = self.config
        prefix = f"[{index + 1}/{total}]"
        self._report(f"Initial setup {prefix}", 0)

        # 空密碼 = 每個帳號使用獨立隨機密碼
        password = cfg.password if cfg.password else generate_password()

        co = ChromiumOptions()
        co.set_argument("--lang", "en")
        co.auto_port().mute(True)

        # 優先使用 Ungoogled Chromium（支援 --load-extension MV3 service worker）
        browser_exe = cfg.browser_path or find_ungoogled_chromium()
        if browser_exe:
            co.set_browser_path(browser_exe)
            logger.info(f"Using browser: {browser_exe}")

        _tmp_user_data: Optional[str] = None
        if cfg.captcha_api_key:
            if cfg.browser_user_data_dir:
                # 使用已安裝 NopeCHA 的 Chrome profile（最可靠的方式）
                # 複製 profile 到暫存資料夾，避免與現有 Chrome 實例的 profile 鎖定衝突
                profile_path = cfg.browser_user_data_dir
                profile_dir_name = os.path.basename(profile_path)
                _tmp_user_data = tempfile.mkdtemp(prefix="roblox_gen_")
                tmp_profile_dst = os.path.join(_tmp_user_data, profile_dir_name)
                logger.info(f"Copying Chrome profile to temp dir: {_tmp_user_data}")
                self._report(f"Copying Chrome profile {prefix}", 2)
                shutil.copytree(
                    profile_path,
                    tmp_profile_dst,
                    ignore=shutil.ignore_patterns("lock", "*.lock", "SingletonLock", "SingletonCookie"),
                )
                co.set_user_data_path(_tmp_user_data)
                co.set_argument(f"--profile-directory={profile_dir_name}")
                logger.info(f"Using copied Chrome profile with NopeCHA: {tmp_profile_dst}")
            else:
                from generator.captcha_bypass import get_nopecha_extension_path
                co.add_extension(get_nopecha_extension_path())
                co.incognito()
        elif cfg.incognito:
            co.incognito()

        if cfg.proxies:
            selected_proxy = random.choice(cfg.proxies)
            co.set_proxy(selected_proxy)

        username = create_username(cfg.name_format, cfg.scrambled_username)
        self._report(f"Username: {username} {prefix}", 5)

        send_analytics(cfg.version)

        # Browser init
        try:
            chrome = Chromium(addr_or_opts=co)
            page = chrome.latest_tab
            page.set.window.max()
        except Exception as e:
            return GeneratorResult(error=f"Browser init failed: {e}")

        email = email_password = None
        email_response = None
        account_cookies: list[dict] = []

        try:
            if cfg.verification:
                try:
                    email, email_password, _, email_response = await self._email_service.generate_email(password)
                    self._report(f"Email generated {prefix}", 15)
                except Exception as e:
                    logger.warning(f"Email generation failed: {e}")
                    return GeneratorResult(error=f"Email failed: {e}")

            # Signup
            if cfg.captcha_api_key and not cfg.browser_user_data_dir:
                # --load-extension 模式：嘗試設定 API key（Chrome 146+ 可能無效）
                from generator.captcha_bypass import setup_nopecha
                setup_nopecha(page, cfg.captcha_api_key)
                await asyncio.sleep(4)

            page.get("https://www.roblox.com/CreateAccount")
            await asyncio.sleep(3)

            # Dismiss cookie banner
            try:
                page.ele("@class=btn-cta-lg cookie-btn btn-primary-md btn-min-width", timeout=3).click()
            except errors.ElementNotFoundError:
                pass

            # --- Birthday ---
            now = datetime.now()
            year_val = str(now.year - 19)
            month_val = str(now.month)
            day_val = str(now.day)

            # 嘗試新版 Roblox 頁面結構（2025）
            _birthday_set = False
            try:
                # 新版：input 欄位
                m_input = page.ele("xpath://input[@id='MonthDropdown' or @placeholder='Month' or @aria-label='Month']", timeout=3)
                if m_input:
                    m_input.clear()
                    m_input.input(month_val)
                    page.ele("xpath://input[@id='DayDropdown' or @placeholder='Day' or @aria-label='Day']", timeout=3).input(day_val)
                    page.ele("xpath://input[@id='YearDropdown' or @placeholder='Year' or @aria-label='Year']", timeout=3).input(year_val)
                    _birthday_set = True
            except Exception:
                pass

            if not _birthday_set:
                try:
                    # 舊版：select 下拉
                    old_locale = locale.getlocale(locale.LC_TIME)
                    try:
                        locale.setlocale(locale.LC_TIME, "C")
                        month_abbr = now.strftime("%b")
                    finally:
                        try:
                            locale.setlocale(locale.LC_TIME, old_locale)
                        except Exception:
                            pass
                    page.ele("#MonthDropdown", timeout=5).select.by_value(month_abbr)
                    try:
                        page.ele("#DayDropdown", timeout=5).select.by_value(f"0{now.day}" if now.day <= 9 else str(now.day))
                    except Exception:
                        page.ele("#DayDropdown", timeout=5).select.by_value(str(now.day))
                    page.ele("#YearDropdown", timeout=5).select.by_value(year_val)
                    _birthday_set = True
                except Exception:
                    pass

            if not _birthday_set:
                # dump 頁面 HTML 幫助 debug
                try:
                    html_snippet = page.run_js("return document.body.innerHTML.substring(0, 3000)")
                    logger.error(f"Birthday fields not found. Page HTML (first 3000 chars):\n{html_snippet}")
                except Exception:
                    pass
                raise RuntimeError("Cannot find birthday input fields on Roblox signup page")

            page.ele("#signup-username", timeout=10).input(username)
            page.ele("#signup-password", timeout=10).input(password)
            await asyncio.sleep(2)

            try:
                page.ele("@@id=signup-checkbox@@class=checkbox").click()
            except errors.ElementNotFoundError:
                pass
            await asyncio.sleep(1)

            page.ele(
                "@@id=signup-button@@name=signupSubmit@@class=btn-primary-md signup-submit-button btn-full-width",
                timeout=10,
            ).click()
            self._report(f"Signup submitted {prefix}", 35)

            # Korean compliance
            if page.ele(".text-left korea-compliance-description-text-margin-left", timeout=3):
                for i in range(4):
                    try:
                        page.ele(f"@@id=compliance-privacy-policy-{i}@@class=checkbox").click()
                    except Exception:
                        pass
                try:
                    page.ele("@@id=signup-agreements-button").click()
                except Exception:
                    pass

            # Wait for home page (NopeCHA auto-solves captcha in background)
            max_wait = 300 if cfg.captcha_api_key else 30
            waited = 0
            interval = 2
            while waited < max_wait:
                current_url = page.url
                if "roblox.com/home" in current_url:
                    break
                # Check if captcha appeared (for logging only)
                if cfg.captcha_api_key and waited == 0:
                    try:
                        frame = page.get_frame("xpath://*[@id='arkose-iframe']")
                        if frame:
                            logger.info(f"Captcha detected {prefix}, waiting for NopeCHA...")
                            self._report(f"Solving captcha {prefix}", 38)
                    except Exception:
                        pass
                await asyncio.sleep(interval)
                waited += interval

            self._report(f"Signup done {prefix}", 50)

            # Email verification
            if cfg.verification and email:
                await self._verify_email(page, email, email_password, email_response, prefix)

            # Collect cookies
            for c in page.cookies():
                account_cookies.append({"name": c["name"], "value": c["value"]})
            self._report(f"Cookies collected {prefix}", 70)

            # Avatar customization
            if cfg.customization:
                await randomize_avatar(page)
                self._report(f"Avatar customized {prefix}", 80)

            # Follow users
            if cfg.follow_users:
                await self._follow_users(cfg.follow_users, page)
                self._report(f"Following users {prefix}", 88)

        except Exception as e:
            logger.error(f"Generation error: {e}")
            return GeneratorResult(error=str(e))
        finally:
            try:
                page.set.cookies.clear()
                page.clear_cache()
                chrome.set.cookies.clear()
                chrome.clear_cache()
                chrome.quit()
            except Exception:
                pass
            # 清理暫存 profile 複本
            if _tmp_user_data:
                shutil.rmtree(_tmp_user_data, ignore_errors=True)

        # Build account object
        security_token = next((c["value"] for c in account_cookies if c["name"] == ".ROBLOSECURITY"), "")
        account = Account(
            security_token=security_token,
            username=username,
            password=password,
            email=email or "",
            email_password=email_password or "",
        )
        account.valid = bool(security_token)

        # Save
        account_data = {
            "username": username,
            "password": password,
            "email": email,
            "emailPassword": email_password,
            "cookies": account_cookies,
        }
        append_account_txt(account)
        append_cookies_json(account_data)

        self._email_service.reset()
        self._report(f"Done {prefix}", 100)
        return GeneratorResult(account=account, success=True)

    async def _verify_email(self, page, email, email_password, email_response, prefix):
        try:
            page.ele(".btn-primary-md btn-primary-md btn-min-width").click()

            # Try inline email field
            email_input = None
            for selector in [
                ". form-control input-field verification-upsell-modal-input",
                ".form-control input-field verification-upsell-modal-input",
            ]:
                elem = page.ele(selector, timeout=3)
                if elem:
                    email_input = elem
                    break

            if email_input:
                email_input.input(email)
                page.ele(".modal-button verification-upsell-btn btn-cta-md btn-min-width").click()
                self._report(f"Email submitted {prefix}", 60)

            # Wait for verification email
            link = await self._wait_for_verification_link(email, email_password, email_response)
            if link:
                self._report(f"Verifying email {prefix}", 65)
                page.get(link)
                await asyncio.sleep(3)
        except Exception as e:
            logger.warning(f"Email verification failed: {e}")

    async def _wait_for_verification_link(self, email, password, email_response) -> Optional[str]:
        import re as _re
        max_attempts = 30
        for _ in range(max_attempts):
            try:
                messages = self._email_service.fetch_messages(email, password, email_response)
                for msg in messages:
                    body = getattr(msg, "text", None)
                    if not body and hasattr(msg, "html") and msg.html:
                        body = msg.html[0]
                    if body:
                        match = _re.search(
                            r"https://www\.roblox\.com/account/settings/verify-email\?ticket=[^\s\"')]+",
                            body,
                        )
                        if match:
                            return match.group(0)
            except Exception:
                pass
            await asyncio.sleep(5)
        return None

    async def _follow_users(self, usernames: list[str], tab):
        import httpx as _httpx
        for uname in usernames:
            try:
                r = _httpx.post(
                    "https://users.roblox.com/v1/usernames/users",
                    json={"usernames": [uname]},
                    timeout=10,
                )
                data = r.json().get("data", [])
                if not data:
                    continue
                user_id = data[0]["id"]
                tab.get(f"https://www.roblox.com/users/{user_id}/profile")
                await asyncio.sleep(2)
                try:
                    tab.ele('xpath://*[@id="user-profile-header-contextual-menu-button"]', timeout=5).click()
                    tab.ele('xpath://*[@id="radix-9"]/div/div/button[1]', timeout=5).click()
                except errors.ElementNotFoundError:
                    pass
            except Exception as e:
                logger.warning(f"Follow {uname} failed: {e}")

    async def run(self) -> list[GeneratorResult]:
        results = []
        for i in range(self.config.count):
            result = await self.generate_one(i, self.config.count)
            results.append(result)
        return results
