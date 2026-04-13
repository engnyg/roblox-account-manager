"""
Cookie 自動刷新管理器。
對應 C# AccountManager.AutoCookieRefresh 定時器。
"""

from __future__ import annotations

import threading
from datetime import datetime
from typing import Callable

from loguru import logger

from core.account import Account


class CookieRefreshManager:
    def __init__(self, get_accounts: Callable[[], list[Account]], save_accounts: Callable, interval_seconds: int = 3600):
        self._get_accounts = get_accounts
        self._save_accounts = save_accounts
        self._interval = interval_seconds
        self._timer: threading.Timer | None = None
        self._running = False

    def start(self):
        if self._running:
            return
        self._running = True
        self._schedule()
        logger.info("CookieRefreshManager started")

    def stop(self):
        self._running = False
        if self._timer:
            self._timer.cancel()
            self._timer = None
        logger.info("CookieRefreshManager stopped")

    def _schedule(self):
        if not self._running:
            return
        self._timer = threading.Timer(self._interval, self._run)
        self._timer.daemon = True
        self._timer.start()

    def _run(self):
        try:
            self.refresh_all()
        finally:
            self._schedule()

    def refresh_all(self):
        accounts = self._get_accounts()
        refreshed = 0
        for acc in accounts:
            if not acc.security_token:
                continue
            ok, _ = acc.get_csrf_token()
            if ok:
                acc.last_attempted_refresh = datetime.now()
                refreshed += 1
        if refreshed:
            self._save_accounts()
            logger.debug(f"Cookie refresh: {refreshed}/{len(accounts)} accounts refreshed")
