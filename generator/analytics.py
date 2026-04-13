"""
匿名分析。移植自 lib/lib.py promptAnalytics / checkAnalytics / sendAnalytics。
"""

from __future__ import annotations

import hashlib
import hmac
import os
import uuid

import httpx
from loguru import logger

from core.constants import ANALYTICS_FILE, DATA_DIR


def _ensure_data_dir():
    os.makedirs(DATA_DIR, exist_ok=True)


def prompt_analytics() -> bool:
    """互動式詢問是否啟用分析（CLI 模式用）。返回是否已啟用。"""
    if os.path.exists(ANALYTICS_FILE):
        return read_analytics_enabled()

    _ensure_data_dir()
    while True:
        ans = input(
            "\nNo personal data is collected, but anonymous usage statistics help us improve. "
            "Allow data collection? [y/n] (Default: Yes): "
        ).strip().lower()
        if ans in ("y", "yes", ""):
            user_id = str(uuid.uuid4())
            _write_analytics(enabled=True, user_id=user_id)
            return True
        elif ans in ("n", "no"):
            _write_analytics(enabled=False)
            return False
        else:
            print("Please enter y or n.")


def read_analytics_enabled() -> bool:
    try:
        with open(ANALYTICS_FILE, encoding="utf-8") as f:
            for line in f:
                if line.startswith("analytics="):
                    return line.strip().split("=", 1)[1] == "1"
    except Exception:
        pass
    return False


def _write_analytics(enabled: bool, user_id: str | None = None):
    lines = ["DO NOT CHANGE ANYTHING IN THIS FILE\n", f"analytics={'1' if enabled else '0'}\n"]
    if user_id:
        lines.append(f"userID={user_id}\n")
    with open(ANALYTICS_FILE, "w", encoding="utf-8") as f:
        f.writelines(lines)


def _read_user_id() -> str:
    try:
        with open(ANALYTICS_FILE, encoding="utf-8") as f:
            for line in f:
                if line.startswith("userID="):
                    return line.strip().split("=", 1)[1]
    except Exception:
        pass
    return str(uuid.uuid4())


def send_analytics(version: str):
    if not read_analytics_enabled():
        return
    user_id = _read_user_id()
    key = b"Qing762.chy"
    signature = hmac.new(key, user_id.encode(), hashlib.sha256).hexdigest()
    try:
        httpx.post(
            "https://qing762.is-a.dev/analytics/roblox",
            json={"userId": user_id, "signature": signature, "version": version},
            timeout=10,
        )
    except Exception as e:
        logger.debug(f"Analytics send failed: {e}")


def init_analytics(enabled: bool):
    """GUI 模式：直接設定是否啟用（不互動）。"""
    _ensure_data_dir()
    if not os.path.exists(ANALYTICS_FILE):
        user_id = str(uuid.uuid4()) if enabled else None
        _write_analytics(enabled=enabled, user_id=user_id)
